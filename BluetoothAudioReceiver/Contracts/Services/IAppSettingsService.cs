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

    int BatteryChargeLimit { get; }

    Task SetBatteryChargeLimitAsync(int limitPercent);

    bool NotificationEnabled { get; }

    Task SetNotificationEnabledAsync(bool enabled);

    bool AudioSwitchNotificationEnabled { get; }

    Task SetAudioSwitchNotificationEnabledAsync(bool enabled);

    bool AutoDayNightNotificationEnabled { get; }

    Task SetAutoDayNightNotificationEnabledAsync(bool enabled);

    bool AutomationExecutionNotificationEnabled { get; }

    Task SetAutomationExecutionNotificationEnabledAsync(bool enabled);

    bool ProxyStatusNotificationEnabled { get; }

    Task SetProxyStatusNotificationEnabledAsync(bool enabled);
     
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

    int DeskBoxFloatingTestDurationMilliseconds { get; }

    Task SetDeskBoxFloatingTestDurationMillisecondsAsync(int milliseconds);

    string DeskBoxFloatingAnimationTier { get; }

    Task SetDeskBoxFloatingAnimationTierAsync(string tier);

    bool DeskBoxFloatingUnifiedLayout { get; }

    Task SetDeskBoxFloatingUnifiedLayoutAsync(bool enabled);

    string DeskBoxFloatingMaterialType { get; }

    Task SetDeskBoxFloatingMaterialTypeAsync(string materialType);


    string PreferredInputDeviceId { get; }

    Task SetPreferredInputDeviceIdAsync(string deviceId);

    string PreferredOutputDeviceId { get; }

    Task SetPreferredOutputDeviceIdAsync(string deviceId);

    string InputHotkey { get; }

    Task SetInputHotkeyAsync(string hotkey);

    string OutputHotkey { get; }

    Task SetOutputHotkeyAsync(string hotkey);

    string AutoBrightnessHotkey { get; }

    Task SetAutoBrightnessHotkeyAsync(string hotkey);

    bool AutoBrightnessNotificationEnabled { get; }

    Task SetAutoBrightnessNotificationEnabledAsync(bool enabled);

    bool AutoBrightnessOnUnlockEnabled { get; }

    Task SetAutoBrightnessOnUnlockEnabledAsync(bool enabled);

    bool AutoBrightnessOnUnlockNotificationEnabled { get; }

    Task SetAutoBrightnessOnUnlockNotificationEnabledAsync(bool enabled);

    bool AutoBrightnessOnForegroundEnabled { get; }

    Task SetAutoBrightnessOnForegroundEnabledAsync(bool enabled);

    bool AutoBrightnessOnForegroundNotificationEnabled { get; }

    Task SetAutoBrightnessOnForegroundNotificationEnabledAsync(bool enabled);

    int AutoBrightnessOnForegroundCooldownMinutes { get; }

    Task SetAutoBrightnessOnForegroundCooldownMinutesAsync(int minutes);

    bool DisableNightLightEnabled { get; }

    Task SetDisableNightLightEnabledAsync(bool enabled);

    bool DisableNightLightAtNightEnabled { get; }

    Task SetDisableNightLightAtNightEnabledAsync(bool enabled);

    bool AutoDayNightEnabled { get; }

    Task SetAutoDayNightEnabledAsync(bool enabled);

    string AutoDayNightCity { get; }

    Task SetAutoDayNightCityAsync(string city);

    bool AutoDayNightUseCity { get; }

    Task SetAutoDayNightUseCityAsync(bool enabled);

    bool AutoDayNightUseSystemLocation { get; }

    Task SetAutoDayNightUseSystemLocationAsync(bool enabled);

    bool AutoDayNightUseCustomNightSchedule { get; }

    Task SetAutoDayNightUseCustomNightScheduleAsync(bool enabled);

    string AutoDayNightNightStartTime { get; }

    Task SetAutoDayNightNightStartTimeAsync(TimeSpan? time);

    string AutoDayNightNightEndTime { get; }

    Task SetAutoDayNightNightEndTimeAsync(TimeSpan? time);

    string BackgroundEfficiencyProcessWhitelist { get; }

    Task SetBackgroundEfficiencyProcessWhitelistAsync(string value);

    string BackgroundEfficiencyProcessBlacklist { get; }

    Task SetBackgroundEfficiencyProcessBlacklistAsync(string value);

    string AutomationGameList { get; }

    Task SetAutomationGameListAsync(string value);

    string AutomationSceneList { get; }

    Task SetAutomationSceneListAsync(string value);

    string AutomationProgramListLastScanDate { get; }

    Task SetAutomationProgramListLastScanDateAsync(string value);

}
