using System.Collections.ObjectModel;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Views.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public sealed partial class AutomationListPageViewModel : ObservableRecipient
{
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly AutomationRuleEngine _automationRuleEngine;
    private readonly IAppSettingsService _appSettingsService;

    [ObservableProperty]
    public partial Visibility EmptyStateVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial bool AutomationExecutionNotificationEnabled { get; set; }

    public string AutomationExecutionNotificationEnabledText => AutomationExecutionNotificationEnabled ? "开" : "关";

    public ObservableCollection<AutomationListItem> Automations { get; } = [];

    public AutomationListPageViewModel(
        IAutomationPipelineRepository pipelineRepository,
        AutomationRuleEngine automationRuleEngine,
        IAppSettingsService appSettingsService)
    {
        _pipelineRepository = pipelineRepository;
        _automationRuleEngine = automationRuleEngine;
        _appSettingsService = appSettingsService;
        AutomationExecutionNotificationEnabled = _appSettingsService.AutomationExecutionNotificationEnabled;
        _pipelineRepository.PipelinesChanged += OnPipelinesChanged;
        Automations.CollectionChanged += (_, _) => UpdatePageState();
        Reload();
    }

    public async Task RemoveAsync(AutomationListItem item)
    {
        var next = _pipelineRepository.Pipelines
            .Where(pipeline => pipeline.Id != item.Id)
            .ToArray();

        await _pipelineRepository.ReplaceAllAsync(next);
    }

    public async Task<bool> TestAsync(AutomationListItem item)
    {
        var pipeline = _pipelineRepository.Pipelines.FirstOrDefault(current => current.Id == item.Id);
        if (pipeline is null)
        {
            return false;
        }

        if (item.IsTesting)
        {
            return false;
        }

        item.IsTesting = true;
        item.LastRunText = "测试中...";
        try
        {
            await _automationRuleEngine.ExecutePipelineNowAsync(pipeline).WaitAsync(TimeSpan.FromSeconds(30));
            return true;
        }
        catch (TimeoutException)
        {
            item.LastRunText = "测试超时：请查看执行日志";
            return false;
        }
        catch (Exception ex)
        {
            item.LastRunText = $"测试失败：{ex.Message}";
            return false;
        }
        finally
        {
            item.IsTesting = false;
        }
    }

    public async Task SetAutomationEnabledAsync(Guid pipelineId, bool isEnabled)
    {
        var next = _pipelineRepository.Pipelines.ToList();
        var changed = false;

        foreach (var pipeline in next)
        {
            if (pipeline.Id == pipelineId)
            {
                if (pipeline.IsEnabled != isEnabled)
                {
                    pipeline.IsEnabled = isEnabled;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await _pipelineRepository.ReplaceAllAsync(next);
        }
    }

    private void OnPipelinesChanged(object? sender, EventArgs e)
    {
        Reload();
    }

    private void Reload()
    {
        Automations.Clear();

        foreach (var pipeline in _pipelineRepository.Pipelines)
        {
            Automations.Add(ToListItem(pipeline));
        }

        UpdatePageState();
    }

    private void UpdatePageState()
    {
        EmptyStateVisibility = Automations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    partial void OnAutomationExecutionNotificationEnabledChanged(bool value)
    {
        _appSettingsService.SetAutomationExecutionNotificationEnabledAsync(value).Forget(nameof(AutomationListPageViewModel));
        OnPropertyChanged(nameof(AutomationExecutionNotificationEnabledText));
    }

    private AutomationListItem ToListItem(AutomationPipeline pipeline)
    {
        var triggerType = AutomationTriggerCatalog.Normalize(pipeline.Trigger.TriggerType);
        var triggerText = string.IsNullOrWhiteSpace(triggerType)
            ? "未配置"
            : AutomationTriggerCatalog.GetDisplayName(triggerType);

        var targetText =
            pipeline.Trigger.Parameters.TryGetValue("processName", out var processName) && !string.IsNullOrWhiteSpace(processName) ? processName :
            triggerType == AutomationTriggerCatalog.BluetoothConnected ? "蓝牙设备" :
            triggerType == AutomationTriggerCatalog.OutputDeviceChanged ? "音频设备" :
            pipeline.Trigger.Parameters.TryGetValue("mode", out var mode) && !string.IsNullOrWhiteSpace(mode) ? mode :
            triggerText;

        var totalActionCount = pipeline.Steps.Count;
        return new AutomationListItem(
            pipeline.Id,
            AutomationDisplayTextConverter.ToDisplayText(pipeline.Name),
            AutomationDisplayTextConverter.ToDisplayText(triggerText),
            AutomationDisplayTextConverter.ToDisplayText(targetText),
            totalActionCount,
            pipeline.IsEnabled,
            "尚未执行记录",
            value => _ = SetAutomationEnabledAsync(pipeline.Id, value));
    }

}

public sealed partial class AutomationListItem : ObservableObject
{
    private readonly Action<bool> _enabledChanged;
    private readonly string _targetText;
    private readonly int _totalActionCount;
    private bool _suppressEnabledChanged;

    public Guid Id { get; }

    public string Name { get; }

    public string TriggerText { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial string LastRunText { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    public bool CanTest => !IsTesting;

    public string TestButtonText => IsTesting ? "测试中..." : "测试";

    public string EnabledText => IsEnabled ? "已启用" : "已停用";

    public string StatusLineText => IsEnabled
        ? $"已启用 · {_targetText} · {_totalActionCount} 个动作"
        : $"已停用 · {_targetText}";

    public AutomationListItem(
        Guid id,
        string name,
        string triggerText,
        string targetText,
        int totalActionCount,
        bool isEnabled,
        string lastRunText,
        Action<bool> enabledChanged)
    {
        Id = id;
        Name = name;
        TriggerText = triggerText;
        _targetText = targetText;
        _totalActionCount = totalActionCount;
        _enabledChanged = enabledChanged;
        _suppressEnabledChanged = true;
        IsEnabled = isEnabled;
        _suppressEnabledChanged = false;
        LastRunText = lastRunText;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnabledText));
        OnPropertyChanged(nameof(StatusLineText));
        if (!_suppressEnabledChanged)
        {
            _enabledChanged(value);
        }
    }

    partial void OnIsTestingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(TestButtonText));
    }
}
