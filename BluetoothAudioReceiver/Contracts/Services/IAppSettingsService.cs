using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAppSettingsService
{
    void Initialize();

    string Language { get; }

    Task SetLanguageAsync(string language);

    ElementTheme Theme { get; }

    Task SetThemeAsync(ElementTheme theme);

    BackdropType BackdropType { get; }

    Task SetBackdropAsync(BackdropType type);

    bool AutoConnect { get; }

    Task SetAutoConnectAsync(bool autoConnect);

    bool NotificationEnabled { get; }

    Task SetNotificationEnabledAsync(bool enabled);
    
    bool DisableNotificationInGame { get; }
    
    Task SetDisableNotificationInGameAsync(bool enabled);
    
    bool DisableNotificationInVideo { get; }
    
    Task SetDisableNotificationInVideoAsync(bool enabled);
    
    bool DisableNotificationInFullscreen { get; }
    
    Task SetDisableNotificationInFullscreenAsync(bool enabled);
    
    bool DisableNotificationInRecordingOrLive { get; }
    
    Task SetDisableNotificationInRecordingOrLiveAsync(bool enabled);

    bool HideNotificationWhenTrayPopupVisible { get; }

    Task SetHideNotificationWhenTrayPopupVisibleAsync(bool enabled);

    string PreferredInputDeviceId { get; }

    Task SetPreferredInputDeviceIdAsync(string deviceId);

    string PreferredOutputDeviceId { get; }

    Task SetPreferredOutputDeviceIdAsync(string deviceId);

    string InputHotkey { get; }

    Task SetInputHotkeyAsync(string hotkey);

    string OutputHotkey { get; }

    Task SetOutputHotkeyAsync(string hotkey);
}
