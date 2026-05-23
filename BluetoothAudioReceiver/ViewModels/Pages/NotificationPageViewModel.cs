using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class NotificationPageViewModel : ObservableRecipient, INavigationAware
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly INotificationService _notificationService;

    [ObservableProperty]
    public partial bool NotificationEnabled { get; set; } = true;
    public string NotificationEnabledText => NotificationEnabled ? "开" : "关";

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
    public partial string Message { get; set; } = "控制连接/断开时的悬浮通知。";

    public NotificationPageViewModel(IAppSettingsService appSettingsService, INotificationService notificationService)
    {
        _appSettingsService = appSettingsService;
        _notificationService = notificationService;
    }

    public void OnNavigatedTo(object parameter)
    {
        NotificationEnabled = _appSettingsService.NotificationEnabled;
        DisableNotificationInGame = _appSettingsService.DisableNotificationInGame;
        DisableNotificationInVideo = _appSettingsService.DisableNotificationInVideo;
        DisableNotificationInFullscreen = _appSettingsService.DisableNotificationInFullscreen;
        DisableNotificationInRecordingOrLive = _appSettingsService.DisableNotificationInRecordingOrLive;
        HideNotificationWhenTrayPopupVisible = _appSettingsService.HideNotificationWhenTrayPopupVisible;
    }

    public void OnNavigatedFrom() { }

    partial void OnNotificationEnabledChanged(bool value)
    {
        _ = _appSettingsService.SetNotificationEnabledAsync(value);
        OnPropertyChanged(nameof(NotificationEnabledText));
    }

    partial void OnDisableNotificationInGameChanged(bool value)
    {
        _ = _appSettingsService.SetDisableNotificationInGameAsync(value);
        OnPropertyChanged(nameof(DisableNotificationInGameText));
    }

    partial void OnDisableNotificationInVideoChanged(bool value)
    {
        _ = _appSettingsService.SetDisableNotificationInVideoAsync(value);
        OnPropertyChanged(nameof(DisableNotificationInVideoText));
    }

    partial void OnDisableNotificationInFullscreenChanged(bool value)
    {
        _ = _appSettingsService.SetDisableNotificationInFullscreenAsync(value);
        OnPropertyChanged(nameof(DisableNotificationInFullscreenText));
    }

    partial void OnDisableNotificationInRecordingOrLiveChanged(bool value)
    {
        _ = _appSettingsService.SetDisableNotificationInRecordingOrLiveAsync(value);
        OnPropertyChanged(nameof(DisableNotificationInRecordingOrLiveText));
    }

    partial void OnHideNotificationWhenTrayPopupVisibleChanged(bool value)
    {
        _ = _appSettingsService.SetHideNotificationWhenTrayPopupVisibleAsync(value);
        OnPropertyChanged(nameof(HideNotificationWhenTrayPopupVisibleText));
    }

    [RelayCommand]
    private void ShowConnectedSample()
    {
        _notificationService.Show("\uE8B6", "示例：蓝牙音频已连接", "启动");
    }

    [RelayCommand]
    private void ShowDisconnectedSample()
    {
        _notificationService.Show("\uE7C9", "示例：蓝牙音频已断开", "停止");
    }
}
