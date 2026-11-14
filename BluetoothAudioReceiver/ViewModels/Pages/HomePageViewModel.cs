using System.Collections.ObjectModel;
using BluetoothAudioReceiver.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class HomePageViewModel : ObservableRecipient
{
    private readonly RuntimeHealthService _runtimeHealthService;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty]
    public partial string AppDisplayName { get; set; } = ConstantHelper.AppDisplayName;

    public BluetoothPageViewModel Bluetooth { get; }

    public HotkeyNotificationPageViewModel HotkeyNotifications { get; }

    public AutomationListPageViewModel Automations { get; }

    public ObservableCollection<RuntimeModuleStatusItem> RuntimeModules { get; } = [];

    public ObservableCollection<RuntimeHealthIssueItem> RuntimeIssues { get; } = [];

    public Visibility NoRuntimeIssuesVisibility => RuntimeIssues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RuntimeIssuesVisibility => RuntimeIssues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public HomePageViewModel(
        BluetoothPageViewModel bluetooth,
        HotkeyNotificationPageViewModel hotkeyNotifications,
        AutomationListPageViewModel automations,
        RuntimeHealthService runtimeHealthService,
        DispatcherQueue dispatcherQueue)
    {
        Bluetooth = bluetooth;
        HotkeyNotifications = hotkeyNotifications;
        Automations = automations;
        _runtimeHealthService = runtimeHealthService;
        _dispatcherQueue = dispatcherQueue;
        _runtimeHealthService.Changed += OnRuntimeHealthChanged;
        RefreshRuntimeHealth();
    }

    public void RefreshRuntimeHealth()
    {
        var snapshot = _runtimeHealthService.GetSnapshot();
        RuntimeModules.Clear();
        foreach (var health in snapshot.OrderBy(item => item.Module))
        {
            RuntimeModules.Add(RuntimeModuleStatusItem.From(health));
        }

        RuntimeIssues.Clear();
        foreach (var issue in snapshot
                     .Where(item => item.State is RuntimeHealthState.Warning or RuntimeHealthState.Error)
                     .OrderByDescending(item => item.OccurredAt))
        {
            RuntimeIssues.Add(RuntimeHealthIssueItem.From(issue));
        }

        OnPropertyChanged(nameof(NoRuntimeIssuesVisibility));
        OnPropertyChanged(nameof(RuntimeIssuesVisibility));
    }

    private void OnRuntimeHealthChanged(object? sender, EventArgs e)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            RefreshRuntimeHealth();
            return;
        }

        _dispatcherQueue.TryEnqueue(RefreshRuntimeHealth);
    }
}

public sealed record RuntimeModuleStatusItem(string Name, string StatusText, string StatusGlyph)
{
    internal static RuntimeModuleStatusItem From(RuntimeModuleHealth health)
        => new(GetName(health.Module), GetStateText(health.State), GetStateGlyph(health.State));

    private static string GetName(RuntimeModule module) => module switch
    {
        RuntimeModule.SystemAwareness => "系统感知",
        RuntimeModule.Scheduler => "调度器",
        RuntimeModule.AutomationManagement => "自动化管理",
        RuntimeModule.ActionExecution => "动作执行",
        RuntimeModule.InterfaceAndSettings => "界面与设置",
        RuntimeModule.BackgroundIntegration => "后台与系统集成",
        _ => "未知模块"
    };

    private static string GetStateText(RuntimeHealthState state) => state switch
    {
        RuntimeHealthState.Normal => "正常",
        RuntimeHealthState.Warning => "警告",
        RuntimeHealthState.Error => "异常",
        _ => "未运行"
    };

    private static string GetStateGlyph(RuntimeHealthState state) => state switch
    {
        RuntimeHealthState.Normal => "\uE73E",
        RuntimeHealthState.Warning => "\uE7BA",
        RuntimeHealthState.Error => "\uEA39",
        _ => "\uE769"
    };

    internal static string GetModuleName(RuntimeModule module) => GetName(module);
}

public sealed record RuntimeHealthIssueItem(string ModuleName, string Message, string OccurredAtText)
{
    internal static RuntimeHealthIssueItem From(RuntimeModuleHealth health)
        => new(
            RuntimeModuleStatusItem.GetModuleName(health.Module),
            health.Message ?? "运行状态异常",
            health.OccurredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);
}
