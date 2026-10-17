using System.Collections.ObjectModel;
using System.IO;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.Extensions;
using BluetoothAudioReceiver.Models.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public sealed partial class AutomationBuilderPageViewModel : ObservableRecipient
{
    private const string DefaultAutomationName = "自动化";
    private const string GHelperActionTitle = "GHelper 快捷键";
    private const string WindowsHotkeyActionTitle = "Win11 系统快捷键";
    private const string CustomHotkeyActionTitle = "自定义快捷键";
    private const string NotConfiguredText = "未设置";
    private const string NotSelectedProgramText = "未选择程序";
    private const string PerformanceModeTriggerTitle = "性能模式";
    private const string OutputDeviceEnterText = "进入";
    private const string OutputDeviceExitText = "离开";
    private static readonly IReadOnlyList<string> EmptyParameterOptions = [NotConfiguredText];
    private static readonly IReadOnlyList<string> TouchpadToggleOptions = ["关闭", "开启"];
    private static readonly IReadOnlyList<string> _inputDeviceActionOptions = ["连接", "断开"];
    private static readonly IReadOnlyList<string> _performanceWindowModeOptions = ["全屏", "非全屏"];
    private static readonly IReadOnlyList<string> _performanceGpuComparisonOptions = ["大于", "小于"];
    private static readonly IReadOnlyList<string> _performanceGpuUsageOptions =
        ["20%", "40%", "60%", "80%"];
    private static readonly IReadOnlyList<string> _outputTriggerStateOptions =
        [OutputDeviceEnterText, OutputDeviceExitText];
    private static readonly IReadOnlyList<int> PriorityOptionValues = [0, 10, 50, 100];
    private static readonly IReadOnlyDictionary<string, double> CooldownOptionValues =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["30 秒"] = 30,
            ["60 秒"] = 60,
            ["30 分钟"] = 30 * 60,
            ["60 分钟"] = 60 * 60,
            ["2 小时"] = 2 * 60 * 60,
            ["3 小时"] = 3 * 60 * 60,
            ["6 小时"] = 6 * 60 * 60,
            ["12 小时"] = 12 * 60 * 60
        };
    private static readonly IReadOnlyList<string> RepeatExecutionOptionValues = ["允许", "不允许"];
    private static readonly IReadOnlyList<string> SwitchOptionValues = ["开", "关"];
    private static readonly IReadOnlyList<string> RunTimeModeOptionValues = ["全天", "仅白天", "仅夜晚"];

    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly AutomationRuleEngine _automationRuleEngine;
    private readonly IAppSettingsService _settingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly IAudioPlaybackConnectionService _audioPlaybackConnectionService;
    private Guid? _editingPipelineId;

    private static readonly IReadOnlyList<string> TriggerActionOptions =
    [
        "程序启动",
        "程序退出",
        PerformanceModeTriggerTitle,
        "电源状态",
        "蓝牙已连接",
        "输出设备变化",
        "到达日出",
        "到达日落"
    ];

    private static readonly IReadOnlyList<string> EditorActionOptions =
    [
        "调整亮度",
        "输入音频",
        "启动程序",
        "关闭程序",
        "打开网页",
        "电源模式",
        "深色模式",
        "等待",
        GHelperActionTitle,
        WindowsHotkeyActionTitle,
        CustomHotkeyActionTitle,
        "通知",
        "音频输出",
        "触摸板"
    ];

    /// <summary>「选择动作」弹窗的分类分组，需覆盖 <see cref="EditorActionOptions"/> 的全部动作。</summary>
    private static readonly IReadOnlyList<AutomationActionCategory> ActionPickerCategoryList =
    [
        new("显示与外观", ["调整亮度", "深色模式"]),
        new("电源", ["电源模式"]),
        new("程序与网页", ["启动程序", "关闭程序", "打开网页"]),
        new("音频", ["输入音频", "音频输出"]),
        new("快捷键", [GHelperActionTitle, WindowsHotkeyActionTitle, CustomHotkeyActionTitle]),
        new("系统与流程", ["通知", "触摸板", "等待"])
    ];

    private static readonly IReadOnlyDictionary<string, string> TriggerTitleToTypeMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["程序启动"] = AutomationSupportCatalog.TriggerForegroundAppChanged,
            ["程序退出"] = AutomationSupportCatalog.TriggerProcessStopped,
            [PerformanceModeTriggerTitle] = AutomationSupportCatalog.TriggerPerformanceModeEntered,
            ["电源状态"] = AutomationSupportCatalog.TriggerPowerStateChanged,
            ["蓝牙已连接"] = AutomationTriggerCatalog.BluetoothConnected,
            ["输出设备变化"] = AutomationTriggerCatalog.OutputDeviceChanged,
            ["到达日出"] = AutomationTriggerCatalog.SunriseReached,
            ["到达日落"] = AutomationTriggerCatalog.SunsetReached
        };

    private static readonly IReadOnlyDictionary<string, string> ActionTitleToTypeMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["调整亮度"] = AutomationSupportCatalog.ActionSetBrightness,
            ["输入音频"] = AutomationSupportCatalog.ActionSetInputDevice,
            ["启动程序"] = AutomationSupportCatalog.ActionProcessControl,
            ["关闭程序"] = AutomationSupportCatalog.ActionProcessControl,
            ["打开网页"] = AutomationSupportCatalog.ActionProcessControl,
            ["电源模式"] = AutomationSupportCatalog.ActionSetPowerMode,
            ["深色模式"] = AutomationSupportCatalog.ActionSetTheme,
            ["等待"] = AutomationSupportCatalog.ActionDelaySeconds,
            [GHelperActionTitle] = AutomationSupportCatalog.ActionSendGHelperHotkey,
            [WindowsHotkeyActionTitle] = AutomationSupportCatalog.ActionSendWindowsHotkey,
            [CustomHotkeyActionTitle] = AutomationSupportCatalog.ActionSendCustomHotkey,
            ["通知"] = AutomationSupportCatalog.ActionShowNotification,
            ["音频输出"] = AutomationSupportCatalog.ActionSetOutputDevice,
            ["触摸板"] = AutomationSupportCatalog.ActionDisableTouchpad
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ActionParameterMap =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["电源状态"] = AutomationOptionCatalog.PowerStateOptions,
            ["调整亮度"] = AutomationOptionCatalog.BrightnessOptions,
            ["电源模式"] = AutomationOptionCatalog.GetPowerModeOptions(),
            ["深色模式"] = AutomationOptionCatalog.ThemeOptions,
            ["等待"] = AutomationOptionCatalog.WaitOptions,
            [GHelperActionTitle] = GHelperShortcutCatalog.SelectionTexts,
            [WindowsHotkeyActionTitle] = WindowsShortcutCatalog.SelectionTexts,
            ["触摸板"] = TouchpadToggleOptions
        };

    public AutomationBuilderPageViewModel(
        IAutomationPipelineRepository pipelineRepository,
        AutomationRuleEngine automationRuleEngine,
        IAppSettingsService settingsService,
        IAudioRouteHotkeyService audioRouteHotkeyService,
        IAudioPlaybackConnectionService audioPlaybackConnectionService)
    {
        _pipelineRepository = pipelineRepository;
        _automationRuleEngine = automationRuleEngine;
        _settingsService = settingsService;
        _audioRouteHotkeyService = audioRouteHotkeyService;
        _audioPlaybackConnectionService = audioPlaybackConnectionService;
        LoadOutputDevicesAsync().Forget(nameof(AutomationBuilderPageViewModel));
        LoadBluetoothTriggerDevicesAsync().Forget(nameof(AutomationBuilderPageViewModel));
    }

    public IReadOnlyList<string> SyntaxOptions { get; } =
    [
        "如果",
        "并且",
        "或者",
        "否则",
        "执行",
        "等待",
        "停止",
        "结束"
    ];

    public IReadOnlyList<string> ActionOptions => EditorActionOptions;
    public IReadOnlyList<AutomationActionCategory> ActionPickerCategories => ActionPickerCategoryList;
    public IReadOnlyList<string> TriggerOptions => TriggerActionOptions;
    public IReadOnlyList<string> InputDeviceActionOptions => _inputDeviceActionOptions;
    public IReadOnlyList<string> PerformanceWindowModeOptions => _performanceWindowModeOptions;
    public IReadOnlyList<string> PerformanceGpuComparisonOptions => _performanceGpuComparisonOptions;
    public IReadOnlyList<string> PerformanceGpuUsageOptions => _performanceGpuUsageOptions;
    public IReadOnlyList<string> OutputTriggerStateOptions => _outputTriggerStateOptions;
    public IReadOnlyList<int> PriorityOptions => PriorityOptionValues;
    public IReadOnlyList<string> CooldownOptions => CooldownOptionValues.Keys.ToArray();
    public IReadOnlyList<string> RepeatExecutionOptions => RepeatExecutionOptionValues;
    public IReadOnlyList<string> SwitchOptions => SwitchOptionValues;
    public IReadOnlyList<string> RunTimeModeOptions => RunTimeModeOptionValues;

    [ObservableProperty]
    public partial string AutomationName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double StepIntervalSeconds { get; set; } = 1;

    [ObservableProperty]
    public partial double CooldownSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial bool NotifyOnSuccess { get; set; }

    [ObservableProperty]
    public partial bool NotifyOnFailure { get; set; }

    [ObservableProperty]
    public partial int Priority { get; set; }

    [ObservableProperty]
    public partial bool AllowRepeatedExecution { get; set; } = true;

    [ObservableProperty]
    public partial bool ExecutionNotificationEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool RunOnStartupEnabled { get; set; }

    [ObservableProperty]
    public partial string RunTimeMode { get; set; } = "全天";

    [ObservableProperty]
    public partial bool OnceOnlyEnabled { get; set; }


    public string AllowRepeatedExecutionText
    {
        get => AllowRepeatedExecution ? "允许" : "不允许";
        set
        {
            var next = string.Equals(value, "允许", StringComparison.OrdinalIgnoreCase);
            if (AllowRepeatedExecution != next)
            {
                AllowRepeatedExecution = next;
                OnPropertyChanged();
            }
        }
    }

    public string ExecutionNotificationEnabledText
    {
        get => ExecutionNotificationEnabled ? "开" : "关";
        set
        {
            var next = string.Equals(value, "开", StringComparison.Ordinal);
            if (ExecutionNotificationEnabled != next)
            {
                ExecutionNotificationEnabled = next;
                OnPropertyChanged();
            }
        }
    }

    public string RunOnStartupEnabledText
    {
        get => RunOnStartupEnabled ? "开" : "关";
        set
        {
            var next = string.Equals(value, "开", StringComparison.Ordinal);
            if (RunOnStartupEnabled != next)
            {
                RunOnStartupEnabled = next;
                OnPropertyChanged();
            }
        }
    }

    public string OnceOnlyEnabledText
    {
        get => OnceOnlyEnabled ? "开" : "关";
        set
        {
            var next = string.Equals(value, "开", StringComparison.Ordinal);
            if (OnceOnlyEnabled != next)
            {
                OnceOnlyEnabled = next;
                OnPropertyChanged();
            }
        }
    }

    public string CooldownText
    {
        get => FormatCooldownDuration(CooldownSeconds);
        set
        {
            var next = ResolveCooldownSeconds(value);
            if (Math.Abs(CooldownSeconds - next) > 0.001)
            {
                CooldownSeconds = next;
                OnPropertyChanged();
            }
        }
    }

    partial void OnExecutionNotificationEnabledChanged(bool value)
        => OnPropertyChanged(nameof(ExecutionNotificationEnabledText));

    partial void OnCooldownSecondsChanged(double value)
        => OnPropertyChanged(nameof(CooldownText));

    partial void OnRunOnStartupEnabledChanged(bool value)
        => OnPropertyChanged(nameof(RunOnStartupEnabledText));

    partial void OnOnceOnlyEnabledChanged(bool value)
        => OnPropertyChanged(nameof(OnceOnlyEnabledText));

    private static double ResolveCooldownSeconds(string? displayText)
        => !string.IsNullOrWhiteSpace(displayText) &&
           CooldownOptionValues.TryGetValue(displayText, out var seconds)
            ? seconds
            : ResolveCooldownSeconds(30);

    private static double ResolveCooldownSeconds(double seconds)
        => CooldownOptionValues.Values
            .OrderBy(option => Math.Abs(option - seconds))
            .ThenBy(option => option)
            .First();

    private static string FormatCooldownDuration(double seconds)
    {
        var normalizedSeconds = ResolveCooldownSeconds(seconds);
        return CooldownOptionValues.FirstOrDefault(pair => Math.Abs(pair.Value - normalizedSeconds) < 0.001).Key
            ?? "30 秒";
    }

    [ObservableProperty]
    public partial AutomationFailurePolicy FailurePolicy { get; set; } = AutomationFailurePolicy.Continue;

    [ObservableProperty]
    public partial string LastSaveErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPipelineLoadFailed { get; private set; }

    public bool HasPersistedPipeline => _editingPipelineId.HasValue;

    public bool CanSavePipeline => !IsPipelineLoadFailed;

    public ObservableCollection<AutomationNode> Nodes { get; } = [];

    public ObservableCollection<AutomationSectionViewModel> Sections { get; } = [];

    public IReadOnlyList<AudioOutputDeviceItem> OutputDevices { get; private set; } = [];
    public IReadOnlyList<BluetoothTriggerDeviceItem> BluetoothTriggerDevices { get; private set; } = [];

    public void LoadPipeline(Guid? pipelineId)
    {
        _editingPipelineId = pipelineId;
        IsPipelineLoadFailed = false;
        LastSaveErrorMessage = string.Empty;
        OnPropertyChanged(nameof(CanSavePipeline));
        Nodes.Clear();

        if (pipelineId is null)
        {
            ResetEditorState();
            RebuildSections();
            return;
        }

        var pipeline = _pipelineRepository.Pipelines.FirstOrDefault(item => item.Id == pipelineId.Value);
        if (pipeline is null)
        {
            IsPipelineLoadFailed = true;
            LastSaveErrorMessage = "原自动化加载失败，已禁止保存。请返回自动化列表后重新打开。";
            RebuildSections();
            return;
        }

        LoadPipelineContent(pipeline);
        RebuildSections();
    }

    public void AddSyntaxNode()
    {
        Nodes.Add(CreateSyntaxNode(SyntaxOptions[0]));
        RebuildSections();
    }

    public void AddActionNode()
    {
        var defaultAction = ActionOptions[0];
        AddActionNode(defaultAction);
    }

    public void AddActionNode(string actionTitle)
    {
        if (string.IsNullOrWhiteSpace(actionTitle) || !ActionOptions.Any(option => string.Equals(option, actionTitle, StringComparison.Ordinal)))
        {
            AddActionNode();
            return;
        }

        var executeNode = Nodes.FirstOrDefault(node => node.Kind == AutomationNodeKind.Syntax && node.Title == "执行");
        InsertActionNodeAfter(executeNode, actionTitle);
    }

    public void InsertActionNodeAfter(AutomationNode? anchorNode, string? actionTitle = null)
    {
        var resolvedAction = string.IsNullOrWhiteSpace(actionTitle) || !ActionOptions.Any(option => string.Equals(option, actionTitle, StringComparison.Ordinal))
            ? ActionOptions[0]
            : actionTitle;

        var newNode = CreateActionNode(resolvedAction, GetDefaultParameter(resolvedAction));
        if (anchorNode is null)
        {
            Nodes.Add(newNode);
            RebuildSections();
            return;
        }

        var index = Nodes.IndexOf(anchorNode);
        if (index < 0)
        {
            Nodes.Add(newNode);
            RebuildSections();
            return;
        }

        var insertIndex = index + 1;
        while (insertIndex < Nodes.Count && Nodes[insertIndex].Kind != AutomationNodeKind.Syntax)
        {
            insertIndex++;
        }

        Nodes.Insert(insertIndex, newNode);
        RebuildSections();
    }

    public void AddTriggerNode()
    {
        AddTriggerNode(TriggerActionOptions[0]);
    }

    public void AddTriggerNode(string triggerTitle)
    {
        if (string.IsNullOrWhiteSpace(triggerTitle) || !TriggerActionOptions.Any(option => string.Equals(option, triggerTitle, StringComparison.Ordinal)))
        {
            triggerTitle = TriggerActionOptions[0];
        }

        var subtitle = GetDefaultParameter(triggerTitle);
        if (Nodes.Count > 1 &&
            Nodes[1].Kind == AutomationNodeKind.Action &&
            string.Equals(AutomationDisplayTextConverter.ToDisplayText(Nodes[1].Title), NotConfiguredText, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(Nodes[1].RawValue))
        {
            Nodes[1] = CreateTriggerNode(triggerTitle, subtitle);
            RebuildSections();
            return;
        }

        Nodes.Add(CreateTriggerNode(triggerTitle, subtitle));
        RebuildSections();
    }

    public void InsertTriggerNodeAfter(AutomationNode? anchorNode)
    {
        var triggerTitle = TriggerActionOptions[0];
        var newNode = CreateTriggerNode(triggerTitle, GetDefaultParameter(triggerTitle));
        if (anchorNode is null)
        {
            Nodes.Add(newNode);
            RebuildSections();
            return;
        }

        var index = Nodes.IndexOf(anchorNode);
        if (index < 0)
        {
            Nodes.Add(newNode);
            RebuildSections();
            return;
        }

        Nodes.Insert(index + 1, newNode);
        RebuildSections();
    }

    public void InsertConditionNodeAfter(AutomationNode? anchorNode)
    {
        var conditionTitle = "电源状态";
        var newNode = CreateTriggerNode(conditionTitle, GetDefaultParameter(conditionTitle));
        if (anchorNode is null)
        {
            Nodes.Add(newNode);
            RebuildSections();
            return;
        }

        var index = Nodes.IndexOf(anchorNode);
        if (index < 0)
        {
            Nodes.Add(newNode);
            RebuildSections();
            return;
        }

        var insertIndex = index + 1;
        while (insertIndex < Nodes.Count && Nodes[insertIndex].Kind != AutomationNodeKind.Syntax)
        {
            insertIndex++;
        }

        Nodes.Insert(insertIndex, newNode);
        RebuildSections();
    }

    public void RemoveNode(AutomationNode? node)
    {
        if (node is not null)
        {
            Nodes.Remove(node);
            RebuildSections();
        }
    }

    public void MoveNodeUp(AutomationNode? node) => MoveNode(node, -1);

    public void MoveNodeDown(AutomationNode? node) => MoveNode(node, 1);

    public void ReorderSectionActions(AutomationSectionViewModel? section)
    {
        if (section is null)
        {
            return;
        }

        var headerIndex = Nodes.IndexOf(section.HeaderNode);
        if (headerIndex < 0)
        {
            return;
        }

        var orderedActions = section.ActionNodes.ToList();
        var removeIndex = headerIndex + 1;
        while (removeIndex < Nodes.Count && Nodes[removeIndex].Kind != AutomationNodeKind.Syntax)
        {
            Nodes.RemoveAt(removeIndex);
        }

        for (var i = 0; i < orderedActions.Count; i++)
        {
            Nodes.Insert(headerIndex + 1 + i, orderedActions[i]);
        }

        RebuildSections();
    }

    public async Task<bool> SaveCurrentPipelineAsync()
    {
        LastSaveErrorMessage = string.Empty;

        if (IsPipelineLoadFailed)
        {
            LastSaveErrorMessage = "原自动化加载失败，不能按新建规则保存。请返回自动化列表后重新打开。";
            return false;
        }

        if (!TryBuildPipeline(out var pipeline))
        {
            if (string.IsNullOrWhiteSpace(LastSaveErrorMessage))
            {
                LastSaveErrorMessage = "当前自动化配置不完整，请检查触发条件和动作参数。";
            }
            return false;
        }

        var next = _pipelineRepository.Pipelines.ToList();
        var index = next.FindIndex(item => item.Id == pipeline.Id);
        if (index >= 0)
        {
            next[index] = pipeline;
        }
        else
        {
            next.Add(pipeline);
        }

        try
        {
            await _pipelineRepository.ReplaceAllAsync(next);
            _editingPipelineId = pipeline.Id;
            return true;
        }
        catch (Exception ex)
        {
            LastSaveErrorMessage = $"保存失败：{ex.Message}";
            return false;
        }
    }

    public async Task<bool> DeleteCurrentPipelineAsync()
    {
        if (_editingPipelineId is not Guid pipelineId)
        {
            return false;
        }

        var next = _pipelineRepository.Pipelines
            .Where(item => item.Id != pipelineId)
            .ToList();

        try
        {
            await _pipelineRepository.ReplaceAllAsync(next);
            _editingPipelineId = null;
            return true;
        }
        catch (Exception ex)
        {
            LastSaveErrorMessage = $"删除失败：{ex.Message}";
            return false;
        }
    }

    public async Task<bool> TestCurrentPipelineAsync()
    {
        if (!await SaveCurrentPipelineAsync())
        {
            return false;
        }

        if (!TryBuildPipeline(out var pipeline))
        {
            return false;
        }

        try
        {
            var skipReason = await _automationRuleEngine.ExecutePipelineNowAsync(pipeline).WaitAsync(TimeSpan.FromSeconds(30));
            if (!string.IsNullOrWhiteSpace(skipReason))
            {
                LastSaveErrorMessage = $"测试未执行：{skipReason}";
                return false;
            }

            return true;
        }
        catch (TimeoutException)
        {
            LastSaveErrorMessage = "测试超时：动作可能仍在系统后台执行，请检查执行日志。";
            return false;
        }
        catch (Exception ex)
        {
            LastSaveErrorMessage = $"测试失败：{ex.Message}";
            return false;
        }
    }

    public IReadOnlyList<string> GetParameterOptions(string actionTitle)
    {
        var normalizedTitle = AutomationDisplayTextConverter.ToDisplayText(actionTitle);

        if (normalizedTitle is "程序启动" or "程序退出")
        {
            var lists = GetAutomationProgramLists();
            return lists;
        }

        if (normalizedTitle is "启动程序")
        {
            var entries = GetAutomationGameEntries();
            return entries.Count == 0 ? [NotSelectedProgramText] : entries;
        }

        if (normalizedTitle == "关闭程序")
        {
            var entries = GetAutomationGameEntries();
            return entries.Count == 0 ? [NotSelectedProgramText] : entries;
        }

        if (normalizedTitle == "音频输出")
        {
            var entries = GetOutputDeviceDisplayOptions();
            return entries.Length == 0 ? [NotConfiguredText] : entries;
        }

        if (normalizedTitle == "输入音频")
        {
            var entries = GetInputDeviceDisplayOptions();
            return entries.Length == 0 ? [NotConfiguredText] : entries;
        }

        if (normalizedTitle == "蓝牙已连接")
        {
            var entries = BluetoothTriggerDevices.Select(device => device.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return entries.Length == 0 ? [NotConfiguredText] : entries;
        }

        if (normalizedTitle == "输出设备变化")
        {
            var entries = GetOutputDeviceDisplayOptions();
            return entries.Length == 0 ? [NotConfiguredText] : entries;
        }

        if (normalizedTitle is "到达日出" or "到达日落")
        {
            return EmptyParameterOptions;
        }

        if (normalizedTitle == PerformanceModeTriggerTitle)
        {
            return _performanceWindowModeOptions;
        }

        var lookupKey = ActionParameterMap.Keys.FirstOrDefault(key =>
            string.Equals(AutomationDisplayTextConverter.ToDisplayText(key), normalizedTitle, StringComparison.Ordinal));

        return lookupKey is not null && ActionParameterMap.TryGetValue(lookupKey, out var options)
            ? options
            : EmptyParameterOptions;
    }

    public void UpdateNodeExecutablePath(AutomationNode node, string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        node.ParameterOptions = [fileName];
        node.Subtitle = fileName;
        node.RawValue = executablePath;
    }

    public static bool RequiresExecutablePicker(AutomationNode? node)
        => node is not null &&
           node.Kind == AutomationNodeKind.Action &&
           IsExecutableActionTitle(node.Title);

    public static bool HasSecondarySelector(AutomationNode? node)
        => node is not null &&
           node.Kind == AutomationNodeKind.Action &&
           !IsExecutableActionTitle(node.Title);

    public static bool IsExecutableActionTitle(string? title)
    {
        var normalizedTitle = AutomationDisplayTextConverter.ToDisplayText(title);
        return normalizedTitle is "程序启动" or "程序退出" or "启动程序" or "关闭程序";
    }

    public static string GetSyntaxAccentKey(AutomationNode? node)
    {
        return node?.Title switch
        {
            "执行" => "SuccessFillColorDefaultBrush",
            "等待" => "SystemFillColorCautionBackgroundBrush",
            "停止" => "SystemFillColorCriticalBackgroundBrush",
            "结束" => "TextFillColorSecondaryBrush",
            _ => "AccentFillColorDefaultBrush"
        };
    }

    public IReadOnlyList<string> GetAutomationGameEntries()
    {
        return AutomationProgramListCatalog.Parse(_settingsService.AutomationGameList)
            .Select(AutomationProgramListCatalog.ToDisplayText)
            .ToArray();
    }

    public IReadOnlyList<string> GetAutomationProgramLists()
        => AutomationProgramListCatalog.ParseLists(
                _settingsService.AutomationGameList,
                _settingsService.BackgroundEfficiencyProcessBlacklist)
            .Select(list => list.Name)
            .ToArray();

    public string ResolveAutomationProgramListValue(string displayText)
        => AutomationProgramListCatalog.ResolveListId(displayText);

    public string ResolveAutomationProgramListDisplay(string? listId)
        => AutomationProgramListCatalog.ResolveListDisplayName(listId);

    public string ResolveAutomationGameEntryValue(string displayText)
        => AutomationProgramListCatalog.ResolveExecutable(
            AutomationProgramListCatalog.Parse(_settingsService.AutomationGameList),
            displayText);

    public string ResolveAutomationGameEntryDisplay(string executable)
        => AutomationProgramListCatalog.ResolveDisplayText(
            AutomationProgramListCatalog.Parse(_settingsService.AutomationGameList),
            executable);

    public Task RefreshOutputDevicesAsync()
    {
        return LoadOutputDevicesAsync();
    }

    public Task RefreshInputDevicesAsync()
    {
        return LoadBluetoothTriggerDevicesAsync();
    }

    public string GetOutputDeviceValue(string name)
    {
        return ResolveOutputDeviceId(name);
    }

    public string GetBluetoothTriggerDeviceValue(string name)
    {
        return BluetoothTriggerDevices.FirstOrDefault(device => string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase))?.Id ?? string.Empty;
    }

    public string GetInputDeviceValue(string name)
    {
        return ResolveInputDeviceId(name);
    }

    private async Task LoadOutputDevicesAsync()
    {
        var devices = await _audioRouteHotkeyService.GetOutputDevicesAsync();
        OutputDevices = devices;
        OnPropertyChanged(nameof(OutputDevices));

        foreach (var node in Nodes.Where(item => item.Kind == AutomationNodeKind.Action && item.Title == "音频输出"))
        {
            node.ParameterOptions = GetParameterOptions(node.Title);
            var displayName = ResolveOutputDeviceDisplay(node.RawValue);
            if (!string.IsNullOrWhiteSpace(displayName) &&
                node.ParameterOptions.Any(option => string.Equals(option, displayName, StringComparison.OrdinalIgnoreCase)))
            {
                node.Subtitle = displayName;
                continue;
            }

            if (!node.ParameterOptions.Any(option => string.Equals(option, node.Subtitle, StringComparison.OrdinalIgnoreCase)))
            {
                node.Subtitle = node.ParameterOptions.FirstOrDefault() ?? NotConfiguredText;
                node.RawValue = ResolveOutputDeviceId(node.Subtitle);
            }
        }

        foreach (var node in Nodes.Where(item => item.Kind == AutomationNodeKind.Action && item.Title == "输出设备变化"))
        {
            node.ParameterOptions = GetParameterOptions(node.Title);
            var displayName = ResolveOutputDeviceDisplay(node.RawValue);
            if (!string.IsNullOrWhiteSpace(displayName) &&
                node.ParameterOptions.Any(option => string.Equals(option, displayName, StringComparison.OrdinalIgnoreCase)))
            {
                node.Subtitle = displayName;
                continue;
            }

            if (!node.ParameterOptions.Any(option => string.Equals(option, node.Subtitle, StringComparison.OrdinalIgnoreCase)))
            {
                node.Subtitle = node.ParameterOptions.FirstOrDefault() ?? NotConfiguredText;
                node.RawValue = ResolveOutputDeviceId(node.Subtitle);
            }
        }

    }

    private async Task LoadBluetoothTriggerDevicesAsync()
    {
        var devices = (await _audioPlaybackConnectionService.FindAllInputRouteDevicesAsync())
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .Select(device => new BluetoothTriggerDeviceItem(device.Id, string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name))
            .DistinctBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        BluetoothTriggerDevices = devices;
        OnPropertyChanged(nameof(BluetoothTriggerDevices));

        foreach (var node in Nodes.Where(item => item.Kind == AutomationNodeKind.Action && item.Title == "蓝牙已连接"))
        {
            node.ParameterOptions = GetParameterOptions(node.Title);
            if (!node.ParameterOptions.Any(option => string.Equals(option, node.Subtitle, StringComparison.OrdinalIgnoreCase)))
            {
                node.Subtitle = node.ParameterOptions.FirstOrDefault() ?? NotConfiguredText;
            }
        }

        foreach (var node in Nodes.Where(item => item.Kind == AutomationNodeKind.Action && item.Title == "输入音频"))
        {
            node.ParameterOptions = GetParameterOptions(node.Title);
            var displayName = ResolveInputDeviceDisplay(node.RawValue);
            if (!string.IsNullOrWhiteSpace(displayName) &&
                node.ParameterOptions.Any(option => string.Equals(option, displayName, StringComparison.OrdinalIgnoreCase)))
            {
                node.Subtitle = displayName;
            }
        }
    }

    private void ResetEditorState()
    {
        AutomationName = string.Empty;
        StepIntervalSeconds = 1;
        CooldownSeconds = 30;
        NotifyOnSuccess = true;
        NotifyOnFailure = true;
        Priority = 0;
        AllowRepeatedExecution = true;
        ExecutionNotificationEnabled = true;
        RunOnStartupEnabled = false;
        RunTimeMode = "全天";
        OnceOnlyEnabled = false;
        FailurePolicy = AutomationFailurePolicy.Continue;
        Nodes.Clear();
        Nodes.Add(CreateSyntaxNode("如果"));
        var triggerNode = CreateTriggerNode(NotConfiguredText, NotConfiguredText);
        triggerNode.RawValue = string.Empty;
        Nodes.Add(triggerNode);
        Nodes.Add(CreateSyntaxNode("条件"));
        Nodes.Add(CreateSyntaxNode("执行"));
    }

    partial void OnIsPipelineLoadFailedChanged(bool value)
        => OnPropertyChanged(nameof(CanSavePipeline));

    private void LoadPipelineContent(AutomationPipeline pipeline)
    {
        AutomationName = AutomationDisplayTextConverter.ToDisplayText(pipeline.Name);
        StepIntervalSeconds = pipeline.Options.StepIntervalSeconds;
        CooldownSeconds = ResolveCooldownSeconds(pipeline.Options.CooldownSeconds);
        NotifyOnSuccess = pipeline.Options.NotifyOnSuccess;
        NotifyOnFailure = pipeline.Options.NotifyOnFailure;
        Priority = pipeline.Options.Priority;
        AllowRepeatedExecution = pipeline.Options.AllowRepeatedExecution;
        ExecutionNotificationEnabled = pipeline.Options.ExecutionNotificationEnabled;
        RunOnStartupEnabled = pipeline.Options.RunOnStartupEnabled;
        RunTimeMode = pipeline.Options.RunTimeMode switch
        {
            "day" => "仅白天",
            "night" => "仅夜晚",
            _ => "全天"
        };
        OnceOnlyEnabled = pipeline.Options.OnceOnlyEnabled;
        FailurePolicy = pipeline.Options.FailurePolicy;

        Nodes.Add(CreateSyntaxNode("如果"));
        AddTriggerNodeFromPipeline(pipeline);
        Nodes.Add(CreateSyntaxNode("条件"));
        AddConditionActionNodesFromPipeline(pipeline);
        Nodes.Add(CreateSyntaxNode("执行"));

        foreach (var step in pipeline.Steps)
        {
            foreach (var node in CreateNodesFromStep(step))
            {
                Nodes.Add(node);
            }
        }

    }

    private void AddTriggerNodeFromPipeline(AutomationPipeline pipeline)
    {
        var triggerType = AutomationTriggerCatalog.Normalize(pipeline.Trigger.TriggerType);

        if (string.Equals(triggerType, AutomationSupportCatalog.TriggerPowerStateChanged, StringComparison.OrdinalIgnoreCase))
        {
            var mode = pipeline.Trigger.Parameters.TryGetValue("mode", out var triggerMode) && triggerMode == "ac-disconnected"
                ? "电池"
                : "插电";
            Nodes.Add(CreateTriggerNode("电源状态", mode));
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.BluetoothConnected, StringComparison.OrdinalIgnoreCase))
        {
            var deviceId = pipeline.Trigger.Parameters.GetValueOrDefault("deviceId", string.Empty);
            var deviceName = BluetoothTriggerDevices.FirstOrDefault(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))?.Name ?? deviceId;
            var triggerNode = CreateTriggerNode("蓝牙已连接", string.IsNullOrWhiteSpace(deviceName) ? NotConfiguredText : deviceName);
            triggerNode.RawValue = deviceId;
            Nodes.Add(triggerNode);
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.OutputDeviceChanged, StringComparison.OrdinalIgnoreCase))
        {
            var deviceId = pipeline.Trigger.Parameters.GetValueOrDefault("currentDeviceId", string.Empty);
            var deviceName = OutputDevices.FirstOrDefault(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))?.Name ?? deviceId;
            var triggerNode = CreateTriggerNode("输出设备变化", string.IsNullOrWhiteSpace(deviceName) ? NotConfiguredText : deviceName);
            triggerNode.RawValue = deviceId;
            triggerNode.SecondaryValue = ResolveOutputTriggerStateDisplay(
                pipeline.Trigger.Parameters.GetValueOrDefault("state", "entered"));
            Nodes.Add(triggerNode);
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.SunriseReached, StringComparison.OrdinalIgnoreCase))
        {
            Nodes.Add(CreateTriggerNode("到达日出", NotConfiguredText));
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.SunsetReached, StringComparison.OrdinalIgnoreCase))
        {
            Nodes.Add(CreateTriggerNode("到达日落", NotConfiguredText));
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.PerformanceModeEntered, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(triggerType, AutomationTriggerCatalog.PerformanceModeExited, StringComparison.OrdinalIgnoreCase))
        {
            var performanceNode = CreateTriggerNode(
                PerformanceModeTriggerTitle,
                pipeline.Trigger.Parameters.GetValueOrDefault("windowMode") == "windowed" ? "非全屏" : "全屏");
            performanceNode.PerformanceWindowMode = performanceNode.Subtitle;
            performanceNode.PerformanceGpuComparison = pipeline.Trigger.Parameters.GetValueOrDefault("gpuComparison") == "less-than"
                ? "小于"
                : "大于";
            performanceNode.PerformanceGpuUsage = FormatGpuUsage(
                pipeline.Trigger.Parameters.GetValueOrDefault("gpuThreshold", "60"));
            Nodes.Add(performanceNode);
            return;
        }

        var actionTitle = string.Equals(triggerType, AutomationSupportCatalog.TriggerProcessStopped, StringComparison.OrdinalIgnoreCase)
            ? "程序退出"
            : "程序启动";
        if (pipeline.Trigger.Parameters.TryGetValue("programListId", out var programListId) &&
            !string.IsNullOrWhiteSpace(programListId))
        {
            var listNode = CreateTriggerNode(actionTitle, ResolveAutomationProgramListDisplay(programListId));
            listNode.RawValue = programListId;
            Nodes.Add(listNode);
            return;
        }

        var filePath = pipeline.Trigger.Parameters.TryGetValue("filePath", out var fullPath) && !string.IsNullOrWhiteSpace(fullPath)
            ? fullPath
            : $"{pipeline.Trigger.Parameters.GetValueOrDefault("processName", "notepad")}.exe";

        var node = CreateTriggerNode(actionTitle, ResolveAutomationGameEntryDisplay(Path.GetFileName(filePath)));
        node.RawValue = filePath;
        Nodes.Add(node);
    }

    private void AddConditionNodesFromPipeline(AutomationPipeline pipeline)
    {
        if (pipeline.Conditions.Count == 0)
        {
            return;
        }

        foreach (var condition in pipeline.Conditions)
        {
            Nodes.Add(CreateSyntaxNode(condition.OperatorType switch
            {
                "And" => "并且",
                "Or" => "或者",
                "Else" => "否则",
                _ => "如果"
            }));

            if (condition.ConditionType == "power-state")
            {
                Nodes.Add(CreateTriggerNode(
                    "电源状态",
                    condition.Parameters.TryGetValue("mode", out var mode) && mode == "ac-disconnected" ? "电池" : "插电"));
            }
        }

        Nodes.Add(CreateSyntaxNode("执行"));
    }

    private void AddConditionActionNodesFromPipeline(AutomationPipeline pipeline)
    {
        foreach (var condition in pipeline.Conditions)
        {
            if (condition.ConditionType == "power-state")
            {
                Nodes.Add(CreateTriggerNode(
                    "电源状态",
                    condition.Parameters.TryGetValue("mode", out var mode) && mode == "ac-disconnected" ? "电池" : "插电"));
            }
        }
    }

    private IReadOnlyList<AutomationNode> CreateNodesFromStep(AutomationStepDefinition step)
    {
        return step.ActionType switch
        {
            AutomationSupportCatalog.ActionDelaySeconds => [CreateActionNode("等待", NormalizeWaitLabel(step.ParametersJson))],
            AutomationSupportCatalog.ActionStopExecution =>
            [
                CreateSyntaxNode("停止")
            ],
            _ => CreateActionStepNode(step) is AutomationNode node ? [node] : []
        };
    }

    private AutomationNode? CreateActionStepNode(AutomationStepDefinition step)
    {
        return step.ActionType switch
        {
            AutomationSupportCatalog.ActionSetPowerMode => CreateActionNode("电源模式", ResolvePowerModeLabel(step.ParametersJson)),
            AutomationSupportCatalog.ActionDisableTouchpad => CreateActionNode("触摸板", "关闭"),
            AutomationSupportCatalog.ActionEnableTouchpad => CreateActionNode("触摸板", "开启"),
            AutomationSupportCatalog.ActionSetBrightness => CreateActionNode("调整亮度", NormalizeBrightnessLabel(step.ParametersJson)),
            AutomationSupportCatalog.ActionSetTheme => CreateActionNode("深色模式", ResolveThemeLabel(step.ParametersJson)),
            AutomationSupportCatalog.ActionSendGHelperHotkey => CreateActionNode(GHelperActionTitle, GHelperShortcutCatalog.NormalizeSelectionText(AutomationActionParameterHelper.ReadValue(step.ParametersJson))),
            AutomationSupportCatalog.ActionSendWindowsHotkey => CreateActionNode(WindowsHotkeyActionTitle, WindowsShortcutCatalog.NormalizeSelectionText(AutomationActionParameterHelper.ReadValue(step.ParametersJson))),
            AutomationSupportCatalog.ActionSendCustomHotkey => CreateActionNode(CustomHotkeyActionTitle, AutomationActionParameterHelper.ReadValue(step.ParametersJson)),
            AutomationSupportCatalog.ActionShowNotification => CreateActionNode("通知", ResolveNotificationLabel(step.ParametersJson)),
            AutomationSupportCatalog.ActionProcessControl => CreateProcessControlNode(step.ParametersJson),
            AutomationSupportCatalog.ActionSetInputDevice => CreateInputDeviceNodeFromStep(step.ParametersJson),
            AutomationSupportCatalog.ActionSetOutputDevice => CreateOutputNodeFromStep(step.ParametersJson),
            _ => null
        };
    }

    private AutomationNode? CreateProcessControlNode(string parameters)
    {
        var (operation, target) = AutomationActionParameterHelper.ReadProcessOperation(parameters);
        if (string.Equals(operation, "close", StringComparison.OrdinalIgnoreCase))
        {
            var processName = target.Trim().Trim('"');
            return CreateActionNode("关闭程序", string.IsNullOrWhiteSpace(processName) ? NotSelectedProgramText : ResolveAutomationGameEntryDisplay(processName));
        }

        if (string.Equals(operation, "launch", StringComparison.OrdinalIgnoreCase))
        {
            target = target.Trim().Trim('"');
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return CreateActionNode("打开网页", target);
            }

            var node = CreateActionNode("启动程序", Path.GetFileName(target));
            node.RawValue = target;
            return node;
        }

        return null;
    }

    private AutomationNode CreateOutputNodeFromStep(string deviceId)
    {
        deviceId = AutomationActionParameterHelper.ReadOutputDeviceId(deviceId);
        var deviceName = ResolveOutputDeviceDisplay(deviceId);
        var node = CreateActionNode("音频输出", deviceName);
        node.RawValue = deviceId;
        return node;
    }

    private AutomationNode CreateInputDeviceNodeFromStep(string deviceId)
    {
        var operation = AutomationActionParameterHelper.ReadInputDeviceOperation(deviceId);
        deviceId = AutomationActionParameterHelper.ReadInputDeviceId(deviceId);
        var deviceName = ResolveInputDeviceDisplay(deviceId);
        var node = CreateActionNode("输入音频", deviceName);
        node.RawValue = deviceId;
        node.Subtitle = deviceName;
        node.SecondaryValue = ResolveInputDeviceOperationDisplay(operation);
        return node;
    }

    private static string ResolveInputDeviceOperationValue(string? displayText)
        => string.Equals(displayText, "断开", StringComparison.OrdinalIgnoreCase) ? "disconnect" : "connect";

    private static string ResolveInputDeviceOperationDisplay(string? operation)
        => string.Equals(operation, "disconnect", StringComparison.OrdinalIgnoreCase) ? "断开" : "连接";

    private static string ResolveOutputTriggerStateValue(string? displayText)
        => string.Equals(displayText, OutputDeviceExitText, StringComparison.OrdinalIgnoreCase) ? "exited" : "entered";

    private static string ResolveOutputTriggerStateDisplay(string? state)
        => string.Equals(state, "exited", StringComparison.OrdinalIgnoreCase) ? OutputDeviceExitText : OutputDeviceEnterText;

    private string[] GetOutputDeviceDisplayOptions()
    {
        if (OutputDevices.Count == 0)
        {
            return [];
        }

        var duplicates = OutputDevices
            .GroupBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => true, StringComparer.OrdinalIgnoreCase);

        return OutputDevices
            .Select(device => FormatOutputDeviceDisplay(device, duplicates.ContainsKey(device.Name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] GetInputDeviceDisplayOptions()
    {
        if (BluetoothTriggerDevices.Count == 0)
        {
            return [];
        }

        return BluetoothTriggerDevices
            .Select(device => device.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ResolveOutputDeviceDisplay(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return NotConfiguredText;
        }

        var duplicates = OutputDevices
            .GroupBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => true, StringComparer.OrdinalIgnoreCase);

        var device = OutputDevices.FirstOrDefault(item => string.Equals(item.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        return device is null ? NotConfiguredText : FormatOutputDeviceDisplay(device, duplicates.ContainsKey(device.Name));
    }

    private string ResolveOutputDeviceId(string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText) || displayText == NotConfiguredText)
        {
            return string.Empty;
        }

        var direct = OutputDevices.FirstOrDefault(device =>
            string.Equals(FormatOutputDeviceDisplay(device, true), displayText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(FormatOutputDeviceDisplay(device, false), displayText, StringComparison.OrdinalIgnoreCase));

        return direct?.Id ?? string.Empty;
    }

    private string ResolveInputDeviceDisplay(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return NotConfiguredText;
        }

        var device = BluetoothTriggerDevices.FirstOrDefault(item => string.Equals(item.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        return device is null ? NotConfiguredText : device.Name;
    }

    private string ResolveInputDeviceId(string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText) || displayText == NotConfiguredText)
        {
            return string.Empty;
        }

        return BluetoothTriggerDevices.FirstOrDefault(device =>
            string.Equals(device.Id, displayText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.Name, displayText, StringComparison.OrdinalIgnoreCase))?.Id ?? string.Empty;
    }

    private static string FormatOutputDeviceDisplay(AudioOutputDeviceItem device, bool includeEndpoint)
    {
        if (!includeEndpoint || string.IsNullOrWhiteSpace(device.EndpointId))
        {
            return device.Name;
        }

        return $"{device.Name} ({device.EndpointId})";
    }

    private bool TryBuildPipeline(out AutomationPipeline pipeline)
    {
        pipeline = new AutomationPipeline();
        var triggerNode = Nodes.FirstOrDefault(node => node.Kind == AutomationNodeKind.Action && IsTriggerAction(node.Title));
        if (triggerNode is null)
        {
            LastSaveErrorMessage = "请先添加一个触发条件。";
            return false;
        }

        if ((triggerNode.Title is "程序启动" or "程序退出") && string.IsNullOrWhiteSpace(ResolveNodeValue(triggerNode)))
        {
            LastSaveErrorMessage = "请先为程序启动或程序退出选择一个程序清单。";
            return false;
        }

        if (AutomationDisplayTextConverter.ToDisplayText(triggerNode.Title) == PerformanceModeTriggerTitle &&
            !ValidatePerformanceTriggerNode(triggerNode))
        {
            return false;
        }

        if (string.Equals(AutomationDisplayTextConverter.ToDisplayText(triggerNode.Title), "输出设备变化", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(ResolveNodeValue(triggerNode)))
        {
            LastSaveErrorMessage = "请先为输出设备变化选择一个目标输出设备。";
            return false;
        }

        var conditions = new List<AutomationConditionDefinition>();
        var steps = new List<AutomationStepDefinition>();
        var currentOperator = "If";
        var inActionSection = !Nodes.Any(node => node.Kind == AutomationNodeKind.Syntax && node.Title == "执行");

        foreach (var node in Nodes)
        {
            if (ReferenceEquals(node, triggerNode))
            {
                continue;
            }

            if (node.Kind == AutomationNodeKind.Syntax)
            {
                switch (node.Title)
                {
                    case "如果":
                        currentOperator = "If";
                        break;
                    case "并且":
                        currentOperator = "And";
                        break;
                    case "或者":
                        currentOperator = "Or";
                        break;
                    case "否则":
                        currentOperator = "Else";
                        break;
                    case "执行":
                        inActionSection = true;
                        break;
                    case "停止":
                        steps.Add(new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionStopExecution, ParametersJson = AutomationActionParameterHelper.BuildStopExecution() });
                        inActionSection = true;
                        break;
                    case "结束":
                        inActionSection = false;
                        break;
                }

                continue;
            }

            if (!inActionSection && TryBuildCondition(node, currentOperator, out var condition))
            {
                conditions.Add(condition);
                continue;
            }

            var step = TryBuildStep(node);
            if (step is not null)
            {
                steps.Add(step);
            }
        }

        if (steps.Count == 0)
        {
            LastSaveErrorMessage = "请至少添加一个有效动作后再保存。";
            return false;
        }

        var existing = _editingPipelineId is Guid id
            ? _pipelineRepository.Pipelines.FirstOrDefault(item => item.Id == id)
            : null;

        var pipelineId = existing?.Id ?? Guid.NewGuid();

        pipeline = new AutomationPipeline
        {
            Id = pipelineId,
            Name = string.IsNullOrWhiteSpace(AutomationName) ? DefaultAutomationName : AutomationName.Trim(),
            Category = existing?.Category ?? "基础",
            IsEnabled = existing?.IsEnabled ?? true,
            Trigger = BuildTriggerDefinition(triggerNode),
            Conditions = conditions,
            Steps = steps,
            Options = existing?.Options ?? new AutomationPipelineOptions
            {
                Priority = Priority,
                FailurePolicy = FailurePolicy,
                StepIntervalSeconds = StepIntervalSeconds,
                CooldownSeconds = ResolveCooldownSeconds(CooldownSeconds),
                AllowRepeatedExecution = AllowRepeatedExecution,
                ExecutionNotificationEnabled = ExecutionNotificationEnabled,
                RunOnStartupEnabled = RunOnStartupEnabled,
                RunTimeMode = ResolveRunTimeModeValue(RunTimeMode),
                OnceOnlyEnabled = OnceOnlyEnabled,
                NotifyOnSuccess = NotifyOnSuccess,
                NotifyOnFailure = NotifyOnFailure
            }
        };

        pipeline.Options.StepIntervalSeconds = StepIntervalSeconds;
        pipeline.Options.CooldownSeconds = ResolveCooldownSeconds(CooldownSeconds);
        pipeline.Options.NotifyOnSuccess = NotifyOnSuccess;
        pipeline.Options.NotifyOnFailure = NotifyOnFailure;
        pipeline.Options.Priority = Priority;
        pipeline.Options.AllowRepeatedExecution = AllowRepeatedExecution;
        pipeline.Options.ExecutionNotificationEnabled = ExecutionNotificationEnabled;
        pipeline.Options.RunOnStartupEnabled = RunOnStartupEnabled;
        pipeline.Options.RunTimeMode = ResolveRunTimeModeValue(RunTimeMode);
        pipeline.Options.OnceOnlyEnabled = OnceOnlyEnabled;
        pipeline.Options.FailurePolicy = FailurePolicy;
        return true;
    }

    public static bool IsSupportedTriggerTitle(string title)
    {
        var normalizedTitle = AutomationDisplayTextConverter.ToDisplayText(title);
        var lookupKey = TriggerTitleToTypeMap.Keys.FirstOrDefault(key =>
            string.Equals(AutomationDisplayTextConverter.ToDisplayText(key), normalizedTitle, StringComparison.Ordinal));

        return lookupKey is not null &&
               TriggerTitleToTypeMap.TryGetValue(lookupKey, out var triggerType) &&
               AutomationSupportCatalog.IsSupportedTrigger(triggerType);
    }

    public static bool IsSupportedActionTitle(string title)
    {
        var normalizedTitle = AutomationDisplayTextConverter.ToDisplayText(title);
        var lookupKey = ActionTitleToTypeMap.Keys.FirstOrDefault(key =>
            string.Equals(AutomationDisplayTextConverter.ToDisplayText(key), normalizedTitle, StringComparison.Ordinal));

        return lookupKey is not null &&
               ActionTitleToTypeMap.TryGetValue(lookupKey, out var actionType) &&
               AutomationSupportCatalog.IsSupportedAction(actionType);
    }

    private static AutomationTriggerDefinition BuildTriggerDefinition(AutomationNode triggerNode)
    {
        var normalizedTitle = AutomationDisplayTextConverter.ToDisplayText(triggerNode.Title);

        if (normalizedTitle == "电源状态")
        {
            return new AutomationTriggerDefinition
            {
                TriggerType = AutomationSupportCatalog.TriggerPowerStateChanged,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mode"] = triggerNode.Subtitle == "电池" ? "ac-disconnected" : "ac-connected"
                }
            };
        }

        if (normalizedTitle == "蓝牙已连接")
        {
            return new AutomationTriggerDefinition
            {
                TriggerType = AutomationTriggerCatalog.BluetoothConnected,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["deviceId"] = ResolveNodeValue(triggerNode)
                }
            };
        }

        if (normalizedTitle == "输出设备变化")
        {
            return new AutomationTriggerDefinition
            {
                TriggerType = AutomationTriggerCatalog.OutputDeviceChanged,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["currentDeviceId"] = ResolveNodeValue(triggerNode)
                }
            };
        }

        if (normalizedTitle == "到达日出")
        {
            return new AutomationTriggerDefinition
            {
                TriggerType = AutomationTriggerCatalog.SunriseReached
            };
        }

        if (normalizedTitle == "到达日落")
        {
            return new AutomationTriggerDefinition
            {
                TriggerType = AutomationTriggerCatalog.SunsetReached
            };
        }

        var filePath = ResolveNodeValue(triggerNode);
        var programListId = normalizedTitle is "程序启动" or "程序退出"
            ? triggerNode.RawValue
            : string.Empty;

        if (string.Equals(normalizedTitle, PerformanceModeTriggerTitle, StringComparison.OrdinalIgnoreCase))
        {
            return new AutomationTriggerDefinition
            {
                TriggerType = AutomationTriggerCatalog.PerformanceModeEntered,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["windowMode"] = triggerNode.PerformanceWindowMode == "非全屏" ? "windowed" : "fullscreen",
                    ["gpuComparison"] = triggerNode.PerformanceGpuComparison == "小于" ? "less-than" : "greater-than",
                    ["gpuThreshold"] = ExtractDigits(triggerNode.PerformanceGpuUsage, "60"),
                    ["state"] = "entered"
                }
            };
        }

        if (string.Equals(normalizedTitle, "输出设备变化", StringComparison.OrdinalIgnoreCase))
        {
            return new AutomationTriggerDefinition
            {
                TriggerType = AutomationTriggerCatalog.OutputDeviceChanged,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["currentDeviceId"] = ResolveNodeValue(triggerNode),
                    ["state"] = ResolveOutputTriggerStateValue(triggerNode.SecondaryValue)
                }
            };
        }

        return new AutomationTriggerDefinition
        {
            TriggerType = normalizedTitle == "程序退出"
                ? AutomationSupportCatalog.TriggerProcessStopped
                : AutomationSupportCatalog.TriggerForegroundAppChanged,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["processName"] = NormalizeProcessName(filePath),
                ["filePath"] = filePath,
                ["programListId"] = programListId
            }
        };
    }

    private bool ValidatePerformanceTriggerNode(AutomationNode triggerNode)
    {
        if (!_performanceWindowModeOptions.Contains(triggerNode.PerformanceWindowMode, StringComparer.Ordinal))
        {
            LastSaveErrorMessage = "请为性能模式选择窗口类型。";
            return false;
        }

        if (!_performanceGpuComparisonOptions.Contains(triggerNode.PerformanceGpuComparison, StringComparer.Ordinal))
        {
            LastSaveErrorMessage = "请为性能模式选择 GPU 大于或小于。";
            return false;
        }

        if (!_performanceGpuUsageOptions.Contains(triggerNode.PerformanceGpuUsage, StringComparer.Ordinal))
        {
            LastSaveErrorMessage = "请为性能模式选择 GPU 百分比。";
            return false;
        }

        return true;
    }

    private static bool TryBuildCondition(
        AutomationNode node,
        string operatorType,
        out AutomationConditionDefinition condition)
    {
        if (AutomationDisplayTextConverter.ToDisplayText(node.Title) == "电源状态")
        {
            condition = new AutomationConditionDefinition
            {
                OperatorType = operatorType,
                ConditionType = AutomationSupportCatalog.ConditionPowerState,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mode"] = node.Subtitle == "电池" ? "ac-disconnected" : "ac-connected"
                }
            };
            return true;
        }

        condition = new AutomationConditionDefinition();
        return false;
    }

    private AutomationStepDefinition? TryBuildStep(AutomationNode node)
    {
        if (!IsSupportedActionTitle(node.Title))
        {
            return null;
        }

        return AutomationDisplayTextConverter.ToDisplayText(node.Title) switch
        {
            "调整亮度" => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionSetBrightness, ParametersJson = AutomationActionParameterHelper.BuildBrightness(int.Parse(ExtractDigits(node.Subtitle, "80"))) },
            "输入音频" => new AutomationStepDefinition
            {
                ActionType = AutomationSupportCatalog.ActionSetInputDevice,
                ParametersJson = AutomationActionParameterHelper.BuildInputDevice(
                    ResolveNodeValue(node),
                    ResolveInputDeviceOperationValue(node.SecondaryValue))
            },
            "启动程序" => BuildProcessControlStep(node),
            "关闭程序" => BuildCloseProcessStep(node),
            "打开网页" => BuildOpenUrlStep(node),
            "电源模式" => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionSetPowerMode, ParametersJson = AutomationActionParameterHelper.BuildPowerMode(node.Subtitle) },
            "深色模式" => new AutomationStepDefinition
            {
                ActionType = AutomationSupportCatalog.ActionSetTheme,
                ParametersJson = AutomationActionParameterHelper.BuildValue(node.Subtitle switch
                {
                    "浅色" => "light",
                    "跟随系统" => "system",
                    _ => "dark"
                })
            },
            "等待" => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionDelaySeconds, ParametersJson = AutomationActionParameterHelper.BuildDelaySeconds(int.Parse(ExtractDigits(node.Subtitle, "3"))) },
            GHelperActionTitle => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionSendGHelperHotkey, ParametersJson = AutomationActionParameterHelper.BuildValue(GHelperShortcutCatalog.NormalizeSelectionText(ResolveNodeValue(node))) },
            WindowsHotkeyActionTitle => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionSendWindowsHotkey, ParametersJson = AutomationActionParameterHelper.BuildValue(WindowsShortcutCatalog.NormalizeSelectionText(ResolveNodeValue(node))) },
            CustomHotkeyActionTitle => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionSendCustomHotkey, ParametersJson = AutomationActionParameterHelper.BuildValue(ResolveNodeValue(node)) },
            "通知" => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionShowNotification, ParametersJson = AutomationActionParameterHelper.BuildValue(ResolveNodeValue(node)) },
            "音频输出" => new AutomationStepDefinition { ActionType = AutomationSupportCatalog.ActionSetOutputDevice, ParametersJson = AutomationActionParameterHelper.BuildOutputDevice(ResolveNodeValue(node)) },
            "触摸板" => BuildTouchpadStep(node),
            _ => null
        };
    }

    private static AutomationStepDefinition? BuildProcessControlStep(AutomationNode node)
    {
        var filePath = ResolveNodeValue(node);
        if (string.IsNullOrWhiteSpace(filePath) || filePath == NotSelectedProgramText)
        {
            return null;
        }

        return new AutomationStepDefinition
        {
            ActionType = AutomationSupportCatalog.ActionProcessControl,
            ParametersJson = AutomationActionParameterHelper.BuildProcessLaunch(filePath)
        };
    }

    private static AutomationStepDefinition BuildTouchpadStep(AutomationNode node)
    {
        var value = ResolveNodeValue(node);
        var enable = string.Equals(value, "开启", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase);

        return new AutomationStepDefinition
        {
            ActionType = enable
                ? AutomationSupportCatalog.ActionEnableTouchpad
                : AutomationSupportCatalog.ActionDisableTouchpad,
            ParametersJson = AutomationActionParameterHelper.BuildValue(enable ? "enable" : "disable")
        };
    }

    private static AutomationStepDefinition? BuildOpenUrlStep(AutomationNode node)
    {
        var url = ResolveNodeValue(node);
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new AutomationStepDefinition
        {
            ActionType = AutomationSupportCatalog.ActionProcessControl,
            ParametersJson = AutomationActionParameterHelper.BuildProcessLaunch(url)
        };
    }

    private static AutomationStepDefinition? BuildCloseProcessStep(AutomationNode node)
    {
        var value = ResolveNodeValue(node);
        if (string.IsNullOrWhiteSpace(value) || value == NotSelectedProgramText)
        {
            return null;
        }

        return new AutomationStepDefinition
        {
            ActionType = AutomationSupportCatalog.ActionProcessControl,
            ParametersJson = AutomationActionParameterHelper.BuildProcessClose(NormalizeProcessName(value))
        };
    }

    private static bool IsTriggerAction(string title)
        => AutomationDisplayTextConverter.ToDisplayText(title) is "程序启动" or "程序退出" or PerformanceModeTriggerTitle or "电源状态" or "蓝牙已连接" or "输出设备变化" or "到达日出" or "到达日落";

    private static bool IsStartupScenarioPipeline(AutomationPipeline pipeline)
        => AutomationTriggerCatalog.Normalize(pipeline.Trigger.TriggerType) is
            AutomationSupportCatalog.TriggerForegroundAppChanged or
            AutomationTriggerCatalog.PerformanceModeEntered;

    private static string ResolveRunTimeModeValue(string? displayText)
        => displayText switch
        {
            "仅白天" => "day",
            "仅夜晚" => "night",
            _ => "any"
        };

    private static string ResolvePowerModeLabel(string parameters)
    {
        var displayName = AutomationActionParameterHelper.ReadPowerModeDisplayName(parameters);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return $"{AutomationActionParameterHelper.ReadPowerModeGuid(parameters)} | {displayName}".Trim();
        }

        if (parameters.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationOptionCatalog.GetPowerModeOptions().FirstOrDefault(item =>
                       AutomationOptionCatalog.ExtractPowerModeDisplayName(item).Contains("节能", StringComparison.OrdinalIgnoreCase) ||
                       AutomationOptionCatalog.ExtractPowerModeDisplayName(item).Contains("silent", StringComparison.OrdinalIgnoreCase))
                   ?? "节能";
        }

        if (parameters.Contains("performance", StringComparison.OrdinalIgnoreCase) ||
            parameters.Contains("high", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationOptionCatalog.GetPowerModeOptions().FirstOrDefault(item =>
                       AutomationOptionCatalog.ExtractPowerModeDisplayName(item).Contains("性能", StringComparison.OrdinalIgnoreCase) ||
                       AutomationOptionCatalog.ExtractPowerModeDisplayName(item).Contains("performance", StringComparison.OrdinalIgnoreCase) ||
                       AutomationOptionCatalog.ExtractPowerModeDisplayName(item).Contains("turbo", StringComparison.OrdinalIgnoreCase))
                   ?? "性能";
        }

        return AutomationOptionCatalog.GetPowerModeOptions().FirstOrDefault(item =>
                   AutomationOptionCatalog.ExtractPowerModeDisplayName(item).Contains("平衡", StringComparison.OrdinalIgnoreCase) ||
                   AutomationOptionCatalog.ExtractPowerModeDisplayName(item).Contains("balanced", StringComparison.OrdinalIgnoreCase))
               ?? "平衡";
    }

    private static string ResolveThemeLabel(string parameters)
    {
        var value = AutomationActionParameterHelper.ReadValue(parameters);
        if (value.Contains("light", StringComparison.OrdinalIgnoreCase))
        {
            return "浅色";
        }

        if (value.Contains("system", StringComparison.OrdinalIgnoreCase))
        {
            return "跟随系统";
        }

        return "深色";
    }

    private static string NormalizeBrightnessLabel(string parameters)
    {
        return $"{AutomationActionParameterHelper.ReadBrightness(parameters, 80)}%";
    }

    private static string NormalizeWaitLabel(string parameters)
    {
        return $"{AutomationActionParameterHelper.ReadDelaySeconds(parameters, 3)} 秒";
    }

    private static string ResolveNotificationLabel(string parameters)
    {
        var message = AutomationActionParameterHelper.ReadValue(parameters);
        return string.IsNullOrWhiteSpace(message) ? "已执行" : message;
    }

    private static string NormalizeProcessName(string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? executablePath.Trim() : fileName;
    }

    private static string ExtractDigits(string text, string fallback)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? fallback : digits;
    }

    private static string FormatGpuUsage(string value)
    {
        var digits = ExtractDigits(value, "60");
        return $"{digits}%";
    }

    private string GetDefaultParameter(string actionTitle)
        => GetParameterOptions(actionTitle).FirstOrDefault() ?? NotConfiguredText;

    private AutomationNode CreateSyntaxNode(string title)
        => new(
            AutomationNodeKind.Syntax,
            title,
            string.Empty,
            SyntaxOptions,
            EmptyParameterOptions,
            GetParameterOptions);

    private AutomationNode CreateTriggerNode(string title, string subtitle)
    {
        var node = new AutomationNode(
            AutomationNodeKind.Action,
            title,
            subtitle,
            TriggerActionOptions,
            GetParameterOptions(title),
            GetParameterOptions);

        if (!string.IsNullOrWhiteSpace(subtitle) && subtitle != NotSelectedProgramText)
        {
            node.RawValue = subtitle;
        }

        if (title is "到达日出" or "到达日落")
        {
            node.RawValue = string.Empty;
        }
        else if (title == "输出设备变化")
        {
            node.SecondaryValue = OutputDeviceEnterText;
        }
        else if (AutomationDisplayTextConverter.ToDisplayText(title) == PerformanceModeTriggerTitle &&
                 string.IsNullOrWhiteSpace(node.PerformanceWindowMode) &&
                 string.IsNullOrWhiteSpace(node.PerformanceGpuComparison) &&
                 string.IsNullOrWhiteSpace(node.PerformanceGpuUsage))
        {
            node.PerformanceWindowMode = _performanceWindowModeOptions[0];
            node.PerformanceGpuComparison = _performanceGpuComparisonOptions[0];
            node.PerformanceGpuUsage = _performanceGpuUsageOptions[2];
        }

        return node;
    }

    private AutomationNode CreateActionNode(string title, string subtitle)
    {
        var node = new AutomationNode(
            AutomationNodeKind.Action,
            title,
            subtitle,
            ActionOptions,
            GetParameterOptions(title),
            GetParameterOptions);

        if (title is "启动程序" or "关闭程序" && !string.IsNullOrWhiteSpace(subtitle) && subtitle != NotSelectedProgramText)
        {
            node.RawValue = subtitle;
        }
        else if (title is "打开网页" or GHelperActionTitle or WindowsHotkeyActionTitle or CustomHotkeyActionTitle or "通知")
        {
            node.RawValue = subtitle;
        }
        else if (title == "音频输出")
        {
            node.RawValue = GetOutputDeviceValue(subtitle);
        }
        else if (title == "输入音频")
        {
            node.RawValue = GetInputDeviceValue(subtitle);
            node.SecondaryValue = "连接";
        }
        return node;
    }

    private void MoveNode(AutomationNode? node, int offset)
    {
        if (node is null)
        {
            return;
        }

        var oldIndex = Nodes.IndexOf(node);
        if (oldIndex < 0)
        {
            return;
        }

        var newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= Nodes.Count)
        {
            return;
        }

        if (Nodes[newIndex].Kind == AutomationNodeKind.Syntax)
        {
            return;
        }

        Nodes.Move(oldIndex, newIndex);
        RebuildSections();
    }

    private void RebuildSections()
    {
        Sections.Clear();

        AutomationSectionViewModel? currentSection = null;
        foreach (var node in Nodes)
        {
            if (node.Kind == AutomationNodeKind.Syntax)
            {
                currentSection = new AutomationSectionViewModel(node);
                Sections.Add(currentSection);
                continue;
            }

            currentSection?.ActionNodes.Add(node);
        }
    }

    private static string ResolveNodeValue(AutomationNode node)
    {
        return string.IsNullOrWhiteSpace(node.RawValue) ? node.Subtitle : node.RawValue;
    }
}

public enum AutomationNodeKind
{
    Syntax,
    Action
}

public enum AutomationParameterKind
{
    None,
    Combo,
    Executable,
    Slider,
    Text,
    Hotkey
}

public sealed class AutomationNode : ObservableObject
{
    private const string NotConfiguredText = "未设置";
    private string _title;
    private string _subtitle;
    private string _secondaryValue = string.Empty;
    private IReadOnlyList<string> _parameterOptions;
    private string _rawValue = string.Empty;
    private string _performanceWindowMode = string.Empty;
    private string _performanceGpuComparison = string.Empty;
    private string _performanceGpuUsage = string.Empty;
    private readonly Func<string, IReadOnlyList<string>> _parameterOptionsFactory;

    public AutomationNode(
        AutomationNodeKind kind,
        string title,
        string subtitle,
        IReadOnlyList<string> titleOptions,
        IReadOnlyList<string> parameterOptions,
        Func<string, IReadOnlyList<string>>? parameterOptionsFactory = null)
    {
        Kind = kind;
        _title = title;
        TitleOptions = titleOptions;
        _subtitle = subtitle;
        _parameterOptions = parameterOptions;
        _parameterOptionsFactory = parameterOptionsFactory ?? (_ => parameterOptions);
    }

    public AutomationNodeKind Kind { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value) && Kind == AutomationNodeKind.Action)
            {
                ParameterOptions = _parameterOptionsFactory(value);
                var parameterKind = ParameterKind;
                Subtitle = parameterKind switch
                {
                    AutomationParameterKind.None => string.Empty,
                    _ => ParameterOptions.FirstOrDefault() ?? NotConfiguredText
                };
                RawValue = value switch
                {
                    "打开网页" => "https://",
                    "GHelper 快捷键" => ParameterOptions.FirstOrDefault() ?? NotConfiguredText,
                    "Win11 系统快捷键" => ParameterOptions.FirstOrDefault() ?? NotConfiguredText,
                    "自定义快捷键" => string.Empty,
                    "通知" => string.Empty,
                    "输入音频" => ParameterOptions.FirstOrDefault() ?? string.Empty,
                    "性能模式" => string.Empty,
                    _ => string.Empty
                };
                SecondaryValue = AutomationDisplayTextConverter.ToDisplayText(value) switch
                {
                    "输入音频" => "连接",
                    _ => string.Empty
                };
                if (AutomationDisplayTextConverter.ToDisplayText(value) == "性能模式")
                {
                    PerformanceWindowMode = "全屏";
                    PerformanceGpuComparison = "大于";
                    PerformanceGpuUsage = "60%";
                }
                OnPropertyChanged(nameof(ParameterKind));
                OnPropertyChanged(nameof(ShowsInlineTextBox));
                OnPropertyChanged(nameof(ShowsComboBox));
                OnPropertyChanged(nameof(ShowsSlider));
            }
        }
    }

    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public string RawValue
    {
        get => _rawValue;
        set => SetProperty(ref _rawValue, value);
    }

    public string SecondaryValue
    {
        get => _secondaryValue;
        set => SetProperty(ref _secondaryValue, value);
    }

    public string PerformanceWindowMode
    {
        get => _performanceWindowMode;
        set => SetProperty(ref _performanceWindowMode, value);
    }

    public string PerformanceGpuComparison
    {
        get => _performanceGpuComparison;
        set => SetProperty(ref _performanceGpuComparison, value);
    }

    public string PerformanceGpuUsage
    {
        get => _performanceGpuUsage;
        set => SetProperty(ref _performanceGpuUsage, value);
    }

    public IReadOnlyList<string> TitleOptions { get; }

    public IReadOnlyList<string> ParameterOptions
    {
        get => _parameterOptions;
        set => SetProperty(ref _parameterOptions, value);
    }

    public AutomationParameterKind ParameterKind
    {
        get
        {
            if (AutomationBuilderPageViewModel.IsExecutableActionTitle(Title))
            {
                return AutomationParameterKind.Executable;
            }

            return AutomationDisplayTextConverter.ToDisplayText(Title) switch
            {
                "调整亮度" => AutomationParameterKind.Combo,
                "打开网页" => AutomationParameterKind.Text,
                "电源模式" => AutomationParameterKind.Combo,
                "深色模式" => AutomationParameterKind.Combo,
                "等待" => AutomationParameterKind.Combo,
                "GHelper 快捷键" => AutomationParameterKind.Combo,
                "Win11 系统快捷键" => AutomationParameterKind.Combo,
                "自定义快捷键" => AutomationParameterKind.Text,
                "通知" => AutomationParameterKind.Text,
                "音频输出" => AutomationParameterKind.Combo,
                "输入音频" => AutomationParameterKind.Combo,
                "触摸板" => AutomationParameterKind.Combo,
                "性能模式" => AutomationParameterKind.Combo,
                "电源状态" => AutomationParameterKind.Combo,
                _ => AutomationParameterKind.None
            };
        }
    }

    public bool ShowsComboBox => ParameterKind == AutomationParameterKind.Combo;

    public bool ShowsSlider => ParameterKind == AutomationParameterKind.Slider;

    public bool ShowsInlineTextBox => ParameterKind == AutomationParameterKind.Text;

    public double SliderValue
    {
        get => double.TryParse(_subtitle, out var value) ? value : 0d;
        set
        {
            var normalized = ((int)Math.Round(value)).ToString();
            if (SetProperty(ref _subtitle, normalized, nameof(Subtitle)))
            {
                OnPropertyChanged(nameof(SliderValue));
            }
        }
    }
}

public sealed class AutomationSectionViewModel
{
    public AutomationSectionViewModel(AutomationNode headerNode)
    {
        HeaderNode = headerNode;
    }

    public AutomationNode HeaderNode { get; }

    public ObservableCollection<AutomationNode> ActionNodes { get; } = [];
}

public sealed record BluetoothTriggerDeviceItem(string Id, string Name);

public sealed record AutomationActionCategory(string Title, IReadOnlyList<string> Actions);

