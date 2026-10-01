using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.DeskBoxFloating.Models;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class NotificationPageViewModel : ObservableRecipient, INavigationAware
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly IDeskBoxFloatingService _deskBoxFloatingService;

    [ObservableProperty]
    public partial bool AudioSwitchNotificationEnabled { get; set; }
    public string AudioSwitchNotificationEnabledText => AudioSwitchNotificationEnabled ? "开" : "关";

    [ObservableProperty]
    public partial bool AutoBrightnessNotificationEnabled { get; set; }
    public string AutoBrightnessNotificationEnabledText => AutoBrightnessNotificationEnabled ? "开" : "关";

    [ObservableProperty]
    public partial bool AutoDayNightNotificationEnabled { get; set; }
    public string AutoDayNightNotificationEnabledText => AutoDayNightNotificationEnabled ? "开" : "关";

    [ObservableProperty]
    public partial bool AutomationExecutionNotificationEnabled { get; set; }
    public string AutomationExecutionNotificationEnabledText => AutomationExecutionNotificationEnabled ? "开" : "关";

    [ObservableProperty]
    public partial bool ProxyStatusNotificationEnabled { get; set; }
    public string ProxyStatusNotificationEnabledText => ProxyStatusNotificationEnabled ? "开" : "关";

    [ObservableProperty]
    public partial bool DisableNotificationInGame { get; set; }
    public string DisableNotificationInGameText => DisableNotificationInGame ? "开" : "关";

    [ObservableProperty]
    public partial bool DisableNotificationInVideo { get; set; }
    public string DisableNotificationInVideoText => DisableNotificationInVideo ? "开" : "关";

    [ObservableProperty]
    public partial bool DisableNotificationInFullscreen { get; set; }
    public string DisableNotificationInFullscreenText => DisableNotificationInFullscreen ? "开" : "关";

    [ObservableProperty]
    public partial bool DisableNotificationInRecordingOrLive { get; set; }
    public string DisableNotificationInRecordingOrLiveText => DisableNotificationInRecordingOrLive ? "开" : "关";

    [ObservableProperty]
    public partial bool HideNotificationWhenTrayPopupVisible { get; set; }
    public string HideNotificationWhenTrayPopupVisibleText => HideNotificationWhenTrayPopupVisible ? "开" : "关";

    [ObservableProperty]
    public partial string DeskBoxFloatingTestDuration { get; set; } = "2.5 秒";

    public IReadOnlyList<string> DeskBoxFloatingTestDurationOptions { get; } = ["2.5 秒", "5 秒", "10 秒"];

    [ObservableProperty]
    public partial string DeskBoxFloatingPreviewType { get; set; } = "通知";

    public IReadOnlyList<string> DeskBoxFloatingPreviewTypeOptions { get; } =
        ["通知", "紧凑通知", "自动化通知", "合并通知", "快捷键通知", "托盘面板", "右键菜单"];

    public NotificationPageViewModel(
        IAppSettingsService appSettingsService,
        IDeskBoxFloatingService deskBoxFloatingService)
    {
        _appSettingsService = appSettingsService;
        _deskBoxFloatingService = deskBoxFloatingService;
    }

    [RelayCommand]
    private void TestNotification()
    {
        _deskBoxFloatingService.Show(BuildDeskBoxFloatingPreviewRequest());
    }

    private DeskBoxFloatingRequest BuildDeskBoxFloatingPreviewRequest()
    {
        var duration = ParseDeskBoxFloatingDuration(DeskBoxFloatingTestDuration);
        return DeskBoxFloatingPreviewType switch
        {
            "托盘面板" => new DeskBoxFloatingRequest
            {
                ProfileId = "Tray.QuickPanel",
                Kind = DeskBoxFloatingKind.Panel,
                IconGlyph = "\uE713",
                Title = "托盘面板预览",
                Status = "新设计",
                Description = "音频输出 · 快捷状态 · 常用操作",
                Items = ["扬声器 · 已连接", "蓝牙耳机 · 可切换", "快捷键：Ctrl + Alt + A"],
                AutoDismissOverride = true,
                DurationMilliseconds = duration
            },
            "右键菜单" => new DeskBoxFloatingRequest
            {
                ProfileId = "Tray.ContextMenu",
                Kind = DeskBoxFloatingKind.Menu,
                IconGlyph = "\uE700",
                Title = "右键菜单预览",
                Status = "DeskBox",
                Description = "主界面 / 重启 / 退出",
                Items = ["打开主界面", "打开设置", "重启", "退出"],
                AutoDismissOverride = true,
                DurationMilliseconds = duration
            },
            "紧凑通知" => new DeskBoxFloatingRequest
            {
                ProfileId = "Notification.Compact",
                Title = "紧凑通知",
                Status = "已触发",
                DurationMilliseconds = duration
            },
            "自动化通知" => new DeskBoxFloatingRequest
            {
                ProfileId = "Notification.Automation",
                Title = "自动化已执行",
                Description = "切换音频设备并调整系统音量",
                Items = ["步骤 1：选择蓝牙耳机", "步骤 2：音量设置为 60%"],
                DurationMilliseconds = duration
            },
            "合并通知" => new DeskBoxFloatingRequest
            {
                ProfileId = "Notification.Merged",
                Title = "状态更新",
                Status = "3 项",
                Items = ["蓝牙设备已连接", "音频输出已切换", "通知设置已保存"],
                DurationMilliseconds = duration
            },
            "快捷键通知" => new DeskBoxFloatingRequest
            {
                ProfileId = "Notification.Hotkey",
                Title = "Ctrl + Alt + A",
                Status = "已执行",
                DurationMilliseconds = duration
            },
            _ => new DeskBoxFloatingRequest
            {
                ProfileId = "Notification.Default",
                Kind = DeskBoxFloatingKind.Notification,
                IconGlyph = "\uE765",
                Title = "测试",
                Status = "已触发",
                DurationMilliseconds = duration
            }
        };
    }

    public Task OnNavigatedToAsync(object parameter)
    {
        AudioSwitchNotificationEnabled = _appSettingsService.AudioSwitchNotificationEnabled;
        AutoBrightnessNotificationEnabled = _appSettingsService.AutoBrightnessNotificationEnabled;
        AutoDayNightNotificationEnabled = _appSettingsService.AutoDayNightNotificationEnabled;
        AutomationExecutionNotificationEnabled = _appSettingsService.AutomationExecutionNotificationEnabled;
        ProxyStatusNotificationEnabled = _appSettingsService.ProxyStatusNotificationEnabled;
        DisableNotificationInGame = _appSettingsService.DisableNotificationInGame;
        DisableNotificationInVideo = _appSettingsService.DisableNotificationInVideo;
        DisableNotificationInFullscreen = _appSettingsService.DisableNotificationInFullscreen;
        DisableNotificationInRecordingOrLive = _appSettingsService.DisableNotificationInRecordingOrLive;
        HideNotificationWhenTrayPopupVisible = _appSettingsService.HideNotificationWhenTrayPopupVisible;
        DeskBoxFloatingTestDuration = FormatDeskBoxFloatingDuration(
            _appSettingsService.DeskBoxFloatingTestDurationMilliseconds);
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    partial void OnAudioSwitchNotificationEnabledChanged(bool value)
    {
        _appSettingsService.SetAudioSwitchNotificationEnabledAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(AudioSwitchNotificationEnabledText));
    }

    partial void OnAutoBrightnessNotificationEnabledChanged(bool value)
    {
        _appSettingsService.SetAutoBrightnessNotificationEnabledAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(AutoBrightnessNotificationEnabledText));
    }

    partial void OnAutoDayNightNotificationEnabledChanged(bool value)
    {
        _appSettingsService.SetAutoDayNightNotificationEnabledAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(AutoDayNightNotificationEnabledText));
    }

    partial void OnAutomationExecutionNotificationEnabledChanged(bool value)
    {
        _appSettingsService.SetAutomationExecutionNotificationEnabledAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(AutomationExecutionNotificationEnabledText));
    }

    partial void OnProxyStatusNotificationEnabledChanged(bool value)
    {
        _appSettingsService.SetProxyStatusNotificationEnabledAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(ProxyStatusNotificationEnabledText));
    }

    partial void OnDisableNotificationInGameChanged(bool value)
    {
        _appSettingsService.SetDisableNotificationInGameAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(DisableNotificationInGameText));
    }

    partial void OnDisableNotificationInVideoChanged(bool value)
    {
        _appSettingsService.SetDisableNotificationInVideoAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(DisableNotificationInVideoText));
    }

    partial void OnDisableNotificationInFullscreenChanged(bool value)
    {
        _appSettingsService.SetDisableNotificationInFullscreenAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(DisableNotificationInFullscreenText));
    }

    partial void OnDisableNotificationInRecordingOrLiveChanged(bool value)
    {
        _appSettingsService.SetDisableNotificationInRecordingOrLiveAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(DisableNotificationInRecordingOrLiveText));
    }

    partial void OnHideNotificationWhenTrayPopupVisibleChanged(bool value)
    {
        _appSettingsService.SetHideNotificationWhenTrayPopupVisibleAsync(value).Forget(nameof(NotificationPageViewModel));
        OnPropertyChanged(nameof(HideNotificationWhenTrayPopupVisibleText));
    }

    partial void OnDeskBoxFloatingTestDurationChanged(string value)
    {
        _appSettingsService.SetDeskBoxFloatingTestDurationMillisecondsAsync(
            ParseDeskBoxFloatingDuration(value)).Forget(nameof(NotificationPageViewModel));
    }

    private static int ParseDeskBoxFloatingDuration(string value)
        => value switch
        {
            "5 秒" => 5000,
            "10 秒" => 10000,
            _ => 2500
        };

    private static string FormatDeskBoxFloatingDuration(int milliseconds)
        => milliseconds switch
        {
            5000 => "5 秒",
            10000 => "10 秒",
            _ => "2.5 秒"
        };

}
