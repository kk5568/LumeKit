using Microsoft.UI.Xaml;
using BluetoothAudioReceiver.Core.Extensions;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BluetoothAudioReceiver.Services;

internal class AppSettingsService(ILocalSettingsService localSettingsService, LocalSettingsKeys localSettingsKeys) : IAppSettingsService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AppSettingsService));
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly LocalSettingsKeys _localSettingsKeys = localSettingsKeys;

    private bool _isInitialized;
    public void Initialize()
    {
        if (!_isInitialized)
        {
            var snapshot = _localSettingsService.GetSettingsSnapshot();

            Language = GetDataFromSettings(snapshot, _localSettingsKeys.LanguageKey, DefaultLanguage);
            Theme = GetDataFromSettings(snapshot, _localSettingsKeys.ThemeKey, DefaultTheme);
            BackdropType = GetDataFromSettings(snapshot, _localSettingsKeys.BackdropTypeKey, DefaultBackdropType);
            InputVolumeIndex = Math.Clamp(GetDataFromSettings(snapshot, _localSettingsKeys.InputVolumeIndexKey, DefaultInputVolumeIndex), 0, 3);
            BatteryChargeLimit = NormalizeBatteryChargeLimit(GetDataFromSettings(snapshot, _localSettingsKeys.BatteryChargeLimitKey, DefaultBatteryChargeLimit));
            NotificationEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.NotificationEnabledKey, DefaultNotificationEnabled);
            AudioSwitchNotificationEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AudioSwitchNotificationEnabledKey, DefaultAudioSwitchNotificationEnabled);
            AutoDayNightNotificationEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightNotificationEnabledKey, DefaultAutoDayNightNotificationEnabled);
            AutomationExecutionNotificationEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AutomationExecutionNotificationEnabledKey, DefaultAutomationExecutionNotificationEnabled);
            ProxyStatusNotificationEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.ProxyStatusNotificationEnabledKey, DefaultProxyStatusNotificationEnabled);
            DisableNotificationInGame = GetDataFromSettings(snapshot, _localSettingsKeys.DisableNotificationInGameKey, false);
            DisableNotificationInVideo = GetDataFromSettings(snapshot, _localSettingsKeys.DisableNotificationInVideoKey, false);
            DisableNotificationInFullscreen = GetDataFromSettings(snapshot, _localSettingsKeys.DisableNotificationInFullscreenKey, false);
            DisableNotificationInRecordingOrLive = GetDataFromSettings(snapshot, _localSettingsKeys.DisableNotificationInRecordingOrLiveKey, false);
            HideNotificationWhenTrayPopupVisible = GetDataFromSettings(snapshot, _localSettingsKeys.HideNotificationWhenTrayPopupVisibleKey, false);
            DeskBoxFloatingTestDurationMilliseconds = NormalizeDeskBoxFloatingTestDurationMilliseconds(
                GetDataFromSettings(
                    snapshot,
                    _localSettingsKeys.DeskBoxFloatingTestDurationMillisecondsKey,
                    DefaultDeskBoxFloatingTestDurationMilliseconds));
            PreferredInputDeviceId = GetDataFromSettings(snapshot, _localSettingsKeys.PreferredInputDeviceIdKey, DefaultPreferredInputDeviceId);
            PreferredOutputDeviceId = GetDataFromSettings(snapshot, _localSettingsKeys.PreferredOutputDeviceIdKey, DefaultPreferredOutputDeviceId);
            InputHotkey = GetDataFromSettings(snapshot, _localSettingsKeys.InputHotkeyKey, DefaultInputHotkey);
            OutputHotkey = GetDataFromSettings(snapshot, _localSettingsKeys.OutputHotkeyKey, DefaultOutputHotkey);
            AutoBrightnessHotkey = GetDataFromSettings(snapshot, _localSettingsKeys.AutoBrightnessHotkeyKey, DefaultAutoBrightnessHotkey);
            AutoBrightnessNotificationEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AutoBrightnessNotificationEnabledKey, DefaultAutoBrightnessNotificationEnabled);
            AutoBrightnessOnUnlockEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AutoBrightnessOnUnlockEnabledKey, DefaultAutoBrightnessOnUnlockEnabled);
            AutoBrightnessOnUnlockNotificationEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AutoBrightnessOnUnlockNotificationEnabledKey, DefaultAutoBrightnessOnUnlockNotificationEnabled);
            AutoBrightnessOnForegroundEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AutoBrightnessOnForegroundEnabledKey, DefaultAutoBrightnessOnForegroundEnabled);
            AutoBrightnessOnForegroundNotificationEnabled = GetDataFromSettings(
                snapshot,
                _localSettingsKeys.AutoBrightnessOnForegroundNotificationEnabledKey,
                DefaultAutoBrightnessOnForegroundNotificationEnabled);
            AutoBrightnessOnForegroundCooldownMinutes = NormalizeAutoBrightnessOnForegroundCooldownMinutes(
                GetDataFromSettings(
                    snapshot,
                    _localSettingsKeys.AutoBrightnessOnForegroundCooldownMinutesKey,
                    DefaultAutoBrightnessOnForegroundCooldownMinutes));
            DisableNightLightEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.DisableNightLightEnabledKey, DefaultDisableNightLightEnabled);
            DisableNightLightAtNightEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.DisableNightLightAtNightEnabledKey, DefaultDisableNightLightAtNightEnabled);
            AutoDayNightEnabled = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightEnabledKey, DefaultAutoDayNightEnabled);
            AutoDayNightCity = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightCityKey, DefaultAutoDayNightCity);
            AutoDayNightUseCity = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightUseCityKey, DefaultAutoDayNightUseCity);
            AutoDayNightUseSystemLocation = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightUseSystemLocationKey, DefaultAutoDayNightUseSystemLocation);
            AutoDayNightUseCustomNightSchedule = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightUseCustomNightScheduleKey, DefaultAutoDayNightUseCustomNightSchedule);
            AutoDayNightNightStartTime = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightNightStartTimeKey, DefaultAutoDayNightNightStartTime);
            AutoDayNightNightEndTime = GetDataFromSettings(snapshot, _localSettingsKeys.AutoDayNightNightEndTimeKey, DefaultAutoDayNightNightEndTime);
            BackgroundEfficiencyProcessWhitelist = GetDataFromSettings(snapshot, _localSettingsKeys.BackgroundEfficiencyProcessWhitelistKey, DefaultBackgroundEfficiencyProcessWhitelist);
            BackgroundEfficiencyProcessBlacklist = GetDataFromSettings(snapshot, _localSettingsKeys.BackgroundEfficiencyProcessBlacklistKey, DefaultBackgroundEfficiencyProcessBlacklist);
            AutomationGameList = GetDataFromSettings(snapshot, _localSettingsKeys.AutomationGameListKey, DefaultAutomationGameList);
            AutomationSceneList = GetDataFromSettings(snapshot, _localSettingsKeys.AutomationSceneListKey, DefaultAutomationSceneList);
            AutomationProgramListLastScanDate = GetDataFromSettings(snapshot, _localSettingsKeys.AutomationProgramListLastScanDateKey, DefaultAutomationProgramListLastScanDate);
            _isInitialized = true;
        }
    }

    #region Language

    private string language = DefaultLanguage;
    public string Language
    {
        get => language;
        private set
        {
            if (language != value)
            {
                language = value;
            }
        }
    }

    private static readonly string DefaultLanguage = AppLanguageHelper.DefaultCode;

    private string GetLanguage()
    {
        var data = GetDataFromSettings(_localSettingsKeys.LanguageKey, DefaultLanguage);
        return data;
    }

    public async Task SetLanguageAsync(string language)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.LanguageKey, language);
        Language = language;
    }

    #endregion

    #region Theme

    private ElementTheme theme = DefaultTheme;
    public ElementTheme Theme
    {
        get => theme;
        private set
        {
            if (theme != value)
            {
                theme = value;
            }
        }
    }

    private const ElementTheme DefaultTheme = ElementTheme.Default;

    private ElementTheme GetTheme()
    {
        var data = GetDataFromSettings(_localSettingsKeys.ThemeKey, DefaultTheme);
        return data;
    }

    public async Task SetThemeAsync(ElementTheme theme)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.ThemeKey, theme);
        Theme = theme;
    }

    #endregion

    #region Backdrop

    private BackdropType backdropType = DefaultBackdropType;
    public BackdropType BackdropType
    {
        get => backdropType;
        private set
        {
            if (backdropType != value)
            {
                backdropType = value;
            }
        }
    }

    private const BackdropType DefaultBackdropType = BackdropType.Mica;

    private BackdropType GetBackdropType()
    {
        var data = GetDataFromSettings(_localSettingsKeys.BackdropTypeKey, DefaultBackdropType);
        return data;
    }

    public async Task SetBackdropAsync(BackdropType type)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.BackdropTypeKey, type);
        BackdropType = type;
    }

    #endregion

    #region InputVolumeIndex

    private int inputVolumeIndex = DefaultInputVolumeIndex;
    public int InputVolumeIndex
    {
        get => inputVolumeIndex;
        private set
        {
            if (inputVolumeIndex != value)
            {
                inputVolumeIndex = value;
            }
        }
    }

    private const int DefaultInputVolumeIndex = 3;

    private int GetInputVolumeIndex()
    {
        var data = GetDataFromSettings(_localSettingsKeys.InputVolumeIndexKey, DefaultInputVolumeIndex);
        return Math.Clamp(data, 0, 3);
    }

    public async Task SetInputVolumeIndexAsync(int index)
    {
        var normalizedIndex = Math.Clamp(index, 0, 3);
        await SaveDataInSettingsAsync(_localSettingsKeys.InputVolumeIndexKey, normalizedIndex);
        InputVolumeIndex = normalizedIndex;
    }

    #endregion

    #region BatteryChargeLimit

    private int batteryChargeLimit = DefaultBatteryChargeLimit;
    public int BatteryChargeLimit
    {
        get => batteryChargeLimit;
        private set
        {
            if (batteryChargeLimit != value)
            {
                batteryChargeLimit = value;
            }
        }
    }

    private const int DefaultBatteryChargeLimit = 80;

    public async Task SetBatteryChargeLimitAsync(int limitPercent)
    {
        var normalizedLimit = NormalizeBatteryChargeLimit(limitPercent);
        await SaveDataInSettingsAsync(_localSettingsKeys.BatteryChargeLimitKey, normalizedLimit);
        BatteryChargeLimit = normalizedLimit;
    }

    private static int NormalizeBatteryChargeLimit(int limitPercent)
    {
        return limitPercent >= 100 ? 100 : 80;
    }

    #endregion

    #region NotificationEnabled

    private bool notificationEnabled = DefaultNotificationEnabled;
    public bool NotificationEnabled
    {
        get => notificationEnabled;
        private set
        {
            if (notificationEnabled != value)
            {
                notificationEnabled = value;
            }
        }
    }

    private const bool DefaultNotificationEnabled = false;
    private bool GetNotificationEnabled()
    {
        var data = GetDataFromSettings(_localSettingsKeys.NotificationEnabledKey, DefaultNotificationEnabled);
        return data;
    }

    public async Task SetNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.NotificationEnabledKey, enabled);
        NotificationEnabled = enabled;
    }

    #endregion

    #region AudioSwitchNotificationEnabled

    private bool audioSwitchNotificationEnabled = DefaultAudioSwitchNotificationEnabled;
    public bool AudioSwitchNotificationEnabled
    {
        get => audioSwitchNotificationEnabled;
        private set
        {
            if (audioSwitchNotificationEnabled != value)
            {
                audioSwitchNotificationEnabled = value;
            }
        }
    }

    private const bool DefaultAudioSwitchNotificationEnabled = true;

    public async Task SetAudioSwitchNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AudioSwitchNotificationEnabledKey, enabled);
        AudioSwitchNotificationEnabled = enabled;
    }

    #endregion

    #region AutoDayNightNotificationEnabled

    private bool autoDayNightNotificationEnabled = DefaultAutoDayNightNotificationEnabled;
    public bool AutoDayNightNotificationEnabled
    {
        get => autoDayNightNotificationEnabled;
        private set
        {
            if (autoDayNightNotificationEnabled != value)
            {
                autoDayNightNotificationEnabled = value;
            }
        }
    }

    private const bool DefaultAutoDayNightNotificationEnabled = true;

    public async Task SetAutoDayNightNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightNotificationEnabledKey, enabled);
        AutoDayNightNotificationEnabled = enabled;
    }

    #endregion

    #region AutomationExecutionNotificationEnabled

    private bool automationExecutionNotificationEnabled = DefaultAutomationExecutionNotificationEnabled;
    public bool AutomationExecutionNotificationEnabled
    {
        get => automationExecutionNotificationEnabled;
        private set
        {
            if (automationExecutionNotificationEnabled != value)
            {
                automationExecutionNotificationEnabled = value;
            }
        }
    }

    private const bool DefaultAutomationExecutionNotificationEnabled = true;

    public async Task SetAutomationExecutionNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutomationExecutionNotificationEnabledKey, enabled);
        AutomationExecutionNotificationEnabled = enabled;
    }

    #endregion

    #region ProxyStatusNotificationEnabled

    private bool proxyStatusNotificationEnabled = DefaultProxyStatusNotificationEnabled;
    public bool ProxyStatusNotificationEnabled
    {
        get => proxyStatusNotificationEnabled;
        private set
        {
            if (proxyStatusNotificationEnabled != value)
            {
                proxyStatusNotificationEnabled = value;
            }
        }
    }

    private const bool DefaultProxyStatusNotificationEnabled = false;

    public async Task SetProxyStatusNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.ProxyStatusNotificationEnabledKey, enabled);
        ProxyStatusNotificationEnabled = enabled;
    }

    #endregion

    #region Notification Scene Disable

    private bool disableNotificationInGame = false;
    public bool DisableNotificationInGame
    {
        get => disableNotificationInGame;
        private set
        {
            if (disableNotificationInGame != value)
            {
                disableNotificationInGame = value;
            }
        }
    }

    private bool GetDisableNotificationInGame()
    {
        return GetDataFromSettings(_localSettingsKeys.DisableNotificationInGameKey, false);
    }

    public async Task SetDisableNotificationInGameAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.DisableNotificationInGameKey, enabled);
        DisableNotificationInGame = enabled;
    }

    private bool disableNotificationInVideo = false;
    public bool DisableNotificationInVideo
    {
        get => disableNotificationInVideo;
        private set
        {
            if (disableNotificationInVideo != value)
            {
                disableNotificationInVideo = value;
            }
        }
    }

    private bool GetDisableNotificationInVideo()
    {
        return GetDataFromSettings(_localSettingsKeys.DisableNotificationInVideoKey, false);
    }

    public async Task SetDisableNotificationInVideoAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.DisableNotificationInVideoKey, enabled);
        DisableNotificationInVideo = enabled;
    }

    private bool disableNotificationInFullscreen = false;
    public bool DisableNotificationInFullscreen
    {
        get => disableNotificationInFullscreen;
        private set
        {
            if (disableNotificationInFullscreen != value)
            {
                disableNotificationInFullscreen = value;
            }
        }
    }

    private bool GetDisableNotificationInFullscreen()
    {
        return GetDataFromSettings(_localSettingsKeys.DisableNotificationInFullscreenKey, false);
    }

    public async Task SetDisableNotificationInFullscreenAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.DisableNotificationInFullscreenKey, enabled);
        DisableNotificationInFullscreen = enabled;
    }

    private bool disableNotificationInRecordingOrLive = false;
    public bool DisableNotificationInRecordingOrLive
    {
        get => disableNotificationInRecordingOrLive;
        private set
        {
            if (disableNotificationInRecordingOrLive != value)
            {
                disableNotificationInRecordingOrLive = value;
            }
        }
    }

    private bool GetDisableNotificationInRecordingOrLive()
    {
        return GetDataFromSettings(_localSettingsKeys.DisableNotificationInRecordingOrLiveKey, false);
    }

    public async Task SetDisableNotificationInRecordingOrLiveAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.DisableNotificationInRecordingOrLiveKey, enabled);
        DisableNotificationInRecordingOrLive = enabled;
    }

    private bool hideNotificationWhenTrayPopupVisible = false;
    public bool HideNotificationWhenTrayPopupVisible
    {
        get => hideNotificationWhenTrayPopupVisible;
        private set
        {
            if (hideNotificationWhenTrayPopupVisible != value)
            {
                hideNotificationWhenTrayPopupVisible = value;
            }
        }
    }

    private bool GetHideNotificationWhenTrayPopupVisible()
    {
        return GetDataFromSettings(_localSettingsKeys.HideNotificationWhenTrayPopupVisibleKey, false);
    }

    public async Task SetHideNotificationWhenTrayPopupVisibleAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.HideNotificationWhenTrayPopupVisibleKey, enabled);
        HideNotificationWhenTrayPopupVisible = enabled;
    }

    private int deskBoxFloatingTestDurationMilliseconds = DefaultDeskBoxFloatingTestDurationMilliseconds;
    public int DeskBoxFloatingTestDurationMilliseconds
    {
        get => deskBoxFloatingTestDurationMilliseconds;
        private set => deskBoxFloatingTestDurationMilliseconds =
            NormalizeDeskBoxFloatingTestDurationMilliseconds(value);
    }

    private const int DefaultDeskBoxFloatingTestDurationMilliseconds = 2500;

    public async Task SetDeskBoxFloatingTestDurationMillisecondsAsync(int milliseconds)
    {
        var normalized = NormalizeDeskBoxFloatingTestDurationMilliseconds(milliseconds);
        await SaveDataInSettingsAsync(
            _localSettingsKeys.DeskBoxFloatingTestDurationMillisecondsKey,
            normalized);
        DeskBoxFloatingTestDurationMilliseconds = normalized;
    }

    private static int NormalizeDeskBoxFloatingTestDurationMilliseconds(int milliseconds)
        => milliseconds is 5000 or 10000 ? milliseconds : DefaultDeskBoxFloatingTestDurationMilliseconds;

    #endregion

    #region PreferredInputDeviceId

    private string preferredInputDeviceId = DefaultPreferredInputDeviceId;
    public string PreferredInputDeviceId
    {
        get => preferredInputDeviceId;
        private set
        {
            if (preferredInputDeviceId != value)
            {
                preferredInputDeviceId = value;
            }
        }
    }

    private const string DefaultPreferredInputDeviceId = "";

    private string GetPreferredInputDeviceId()
    {
        var data = GetDataFromSettings(_localSettingsKeys.PreferredInputDeviceIdKey, DefaultPreferredInputDeviceId);
        return data;
    }

    public async Task SetPreferredInputDeviceIdAsync(string deviceId)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.PreferredInputDeviceIdKey, deviceId);
        PreferredInputDeviceId = deviceId;
    }

    #endregion

    #region PreferredOutputDeviceId

    private string preferredOutputDeviceId = DefaultPreferredOutputDeviceId;
    public string PreferredOutputDeviceId
    {
        get => preferredOutputDeviceId;
        private set
        {
            if (preferredOutputDeviceId != value)
            {
                preferredOutputDeviceId = value;
            }
        }
    }

    private const string DefaultPreferredOutputDeviceId = "";

    private string GetPreferredOutputDeviceId()
    {
        var data = GetDataFromSettings(_localSettingsKeys.PreferredOutputDeviceIdKey, DefaultPreferredOutputDeviceId);
        return data;
    }

    public async Task SetPreferredOutputDeviceIdAsync(string deviceId)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.PreferredOutputDeviceIdKey, deviceId);
        PreferredOutputDeviceId = deviceId;
    }

    #endregion

    #region InputHotkey

    private string inputHotkey = DefaultInputHotkey;
    public string InputHotkey
    {
        get => inputHotkey;
        private set
        {
            if (inputHotkey != value)
            {
                inputHotkey = value;
            }
        }
    }

    private const string DefaultInputHotkey = "Ctrl+Alt+I";

    private string GetInputHotkey()
    {
        var data = GetDataFromSettings(_localSettingsKeys.InputHotkeyKey, DefaultInputHotkey);
        return data;
    }

    public async Task SetInputHotkeyAsync(string hotkey)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.InputHotkeyKey, hotkey);
        InputHotkey = hotkey;
    }

    #endregion

    #region OutputHotkey

    private string outputHotkey = DefaultOutputHotkey;
    public string OutputHotkey
    {
        get => outputHotkey;
        private set
        {
            if (outputHotkey != value)
            {
                outputHotkey = value;
            }
        }
    }

    private const string DefaultOutputHotkey = "Ctrl+Alt+O";

    private string GetOutputHotkey()
    {
        var data = GetDataFromSettings(_localSettingsKeys.OutputHotkeyKey, DefaultOutputHotkey);
        return data;
    }

    public async Task SetOutputHotkeyAsync(string hotkey)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.OutputHotkeyKey, hotkey);
        OutputHotkey = hotkey;
    }

    #endregion

    #region AutoBrightnessHotkey

    private string autoBrightnessHotkey = DefaultAutoBrightnessHotkey;
    public string AutoBrightnessHotkey
    {
        get => autoBrightnessHotkey;
        private set
        {
            if (autoBrightnessHotkey != value)
            {
                autoBrightnessHotkey = value;
            }
        }
    }

    private const string DefaultAutoBrightnessHotkey = "";

    private string GetAutoBrightnessHotkey()
    {
        var data = GetDataFromSettings(_localSettingsKeys.AutoBrightnessHotkeyKey, DefaultAutoBrightnessHotkey);
        return data;
    }

    public async Task SetAutoBrightnessHotkeyAsync(string hotkey)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoBrightnessHotkeyKey, hotkey);
        AutoBrightnessHotkey = hotkey;
    }

    #endregion

    #region AutoBrightnessNotificationEnabled

    private bool autoBrightnessNotificationEnabled = DefaultAutoBrightnessNotificationEnabled;
    public bool AutoBrightnessNotificationEnabled
    {
        get => autoBrightnessNotificationEnabled;
        private set
        {
            if (autoBrightnessNotificationEnabled != value)
            {
                autoBrightnessNotificationEnabled = value;
            }
        }
    }

    private const bool DefaultAutoBrightnessNotificationEnabled = true;

    private bool GetAutoBrightnessNotificationEnabled()
    {
        return GetDataFromSettings(_localSettingsKeys.AutoBrightnessNotificationEnabledKey, DefaultAutoBrightnessNotificationEnabled);
    }

    public async Task SetAutoBrightnessNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoBrightnessNotificationEnabledKey, enabled);
        AutoBrightnessNotificationEnabled = enabled;
    }

    #endregion

    #region AutoBrightnessOnUnlockEnabled

    private bool autoBrightnessOnUnlockEnabled = DefaultAutoBrightnessOnUnlockEnabled;
    public bool AutoBrightnessOnUnlockEnabled
    {
        get => autoBrightnessOnUnlockEnabled;
        private set
        {
            if (autoBrightnessOnUnlockEnabled != value)
            {
                autoBrightnessOnUnlockEnabled = value;
            }
        }
    }

    private const bool DefaultAutoBrightnessOnUnlockEnabled = true;

    private bool GetAutoBrightnessOnUnlockEnabled()
    {
        return GetDataFromSettings(_localSettingsKeys.AutoBrightnessOnUnlockEnabledKey, DefaultAutoBrightnessOnUnlockEnabled);
    }

    public async Task SetAutoBrightnessOnUnlockEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoBrightnessOnUnlockEnabledKey, enabled);
        AutoBrightnessOnUnlockEnabled = enabled;
    }

    #endregion

    #region AutoBrightnessOnUnlockNotificationEnabled

    private bool autoBrightnessOnUnlockNotificationEnabled = DefaultAutoBrightnessOnUnlockNotificationEnabled;
    public bool AutoBrightnessOnUnlockNotificationEnabled
    {
        get => autoBrightnessOnUnlockNotificationEnabled;
        private set
        {
            if (autoBrightnessOnUnlockNotificationEnabled != value)
            {
                autoBrightnessOnUnlockNotificationEnabled = value;
            }
        }
    }

    private const bool DefaultAutoBrightnessOnUnlockNotificationEnabled = true;

    public async Task SetAutoBrightnessOnUnlockNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoBrightnessOnUnlockNotificationEnabledKey, enabled);
        AutoBrightnessOnUnlockNotificationEnabled = enabled;
    }

    #endregion

    #region AutoBrightnessOnForeground

    private bool autoBrightnessOnForegroundEnabled = DefaultAutoBrightnessOnForegroundEnabled;
    public bool AutoBrightnessOnForegroundEnabled
    {
        get => autoBrightnessOnForegroundEnabled;
        private set => autoBrightnessOnForegroundEnabled = value;
    }

    private const bool DefaultAutoBrightnessOnForegroundEnabled = false;

    public async Task SetAutoBrightnessOnForegroundEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoBrightnessOnForegroundEnabledKey, enabled);
        AutoBrightnessOnForegroundEnabled = enabled;
    }

    private bool autoBrightnessOnForegroundNotificationEnabled = DefaultAutoBrightnessOnForegroundNotificationEnabled;
    public bool AutoBrightnessOnForegroundNotificationEnabled
    {
        get => autoBrightnessOnForegroundNotificationEnabled;
        private set => autoBrightnessOnForegroundNotificationEnabled = value;
    }

    private const bool DefaultAutoBrightnessOnForegroundNotificationEnabled = true;

    public async Task SetAutoBrightnessOnForegroundNotificationEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoBrightnessOnForegroundNotificationEnabledKey, enabled);
        AutoBrightnessOnForegroundNotificationEnabled = enabled;
    }

    private int autoBrightnessOnForegroundCooldownMinutes = DefaultAutoBrightnessOnForegroundCooldownMinutes;
    public int AutoBrightnessOnForegroundCooldownMinutes
    {
        get => autoBrightnessOnForegroundCooldownMinutes;
        private set => autoBrightnessOnForegroundCooldownMinutes = NormalizeAutoBrightnessOnForegroundCooldownMinutes(value);
    }

    private const int DefaultAutoBrightnessOnForegroundCooldownMinutes = 10;

    public async Task SetAutoBrightnessOnForegroundCooldownMinutesAsync(int minutes)
    {
        var normalized = NormalizeAutoBrightnessOnForegroundCooldownMinutes(minutes);
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoBrightnessOnForegroundCooldownMinutesKey, normalized);
        AutoBrightnessOnForegroundCooldownMinutes = normalized;
    }

    private static int NormalizeAutoBrightnessOnForegroundCooldownMinutes(int minutes)
    {
        return minutes switch
        {
            0 or 1 or 5 or 10 or 30 or 60 => minutes,
            _ => DefaultAutoBrightnessOnForegroundCooldownMinutes
        };
    }

    #endregion

    #region DisableNightLightEnabled

    private bool disableNightLightEnabled = DefaultDisableNightLightEnabled;
    public bool DisableNightLightEnabled
    {
        get => disableNightLightEnabled;
        private set
        {
            if (disableNightLightEnabled != value)
            {
                disableNightLightEnabled = value;
            }
        }
    }

    private const bool DefaultDisableNightLightEnabled = false;

    private bool GetDisableNightLightEnabled()
    {
        return GetDataFromSettings(_localSettingsKeys.DisableNightLightEnabledKey, DefaultDisableNightLightEnabled);
    }

    public async Task SetDisableNightLightEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.DisableNightLightEnabledKey, enabled);
        DisableNightLightEnabled = enabled;
    }

    #endregion

    #region DisableNightLightAtNightEnabled

    private bool disableNightLightAtNightEnabled = DefaultDisableNightLightAtNightEnabled;
    public bool DisableNightLightAtNightEnabled
    {
        get => disableNightLightAtNightEnabled;
        private set
        {
            if (disableNightLightAtNightEnabled != value)
            {
                disableNightLightAtNightEnabled = value;
            }
        }
    }

    private const bool DefaultDisableNightLightAtNightEnabled = false;

    private bool GetDisableNightLightAtNightEnabled()
    {
        return GetDataFromSettings(_localSettingsKeys.DisableNightLightAtNightEnabledKey, DefaultDisableNightLightAtNightEnabled);
    }

    public async Task SetDisableNightLightAtNightEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.DisableNightLightAtNightEnabledKey, enabled);
        DisableNightLightAtNightEnabled = enabled;
    }

    #endregion

    #region AutoDayNightEnabled

    private bool autoDayNightEnabled = DefaultAutoDayNightEnabled;
    public bool AutoDayNightEnabled
    {
        get => autoDayNightEnabled;
        private set
        {
            if (autoDayNightEnabled != value)
            {
                autoDayNightEnabled = value;
            }
        }
    }

    private const bool DefaultAutoDayNightEnabled = false;

    public async Task SetAutoDayNightEnabledAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightEnabledKey, enabled);
        AutoDayNightEnabled = enabled;
    }

    #endregion

    #region AutoDayNightCity

    private string autoDayNightCity = DefaultAutoDayNightCity;
    public string AutoDayNightCity
    {
        get => autoDayNightCity;
        private set
        {
            if (autoDayNightCity != value)
            {
                autoDayNightCity = value;
            }
        }
    }

    private const string DefaultAutoDayNightCity = "";

    public async Task SetAutoDayNightCityAsync(string city)
    {
        var normalized = city?.Trim() ?? string.Empty;
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightCityKey, normalized);
        AutoDayNightCity = normalized;
    }

    #endregion

    #region AutoDayNightUseCity

    private bool autoDayNightUseCity = DefaultAutoDayNightUseCity;
    public bool AutoDayNightUseCity
    {
        get => autoDayNightUseCity;
        private set
        {
            if (autoDayNightUseCity != value)
            {
                autoDayNightUseCity = value;
            }
        }
    }

    private const bool DefaultAutoDayNightUseCity = true;

    public async Task SetAutoDayNightUseCityAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightUseCityKey, enabled);
        AutoDayNightUseCity = enabled;
    }

    #endregion

    #region AutoDayNightUseSystemLocation

    private bool autoDayNightUseSystemLocation = DefaultAutoDayNightUseSystemLocation;
    public bool AutoDayNightUseSystemLocation
    {
        get => autoDayNightUseSystemLocation;
        private set
        {
            if (autoDayNightUseSystemLocation != value)
            {
                autoDayNightUseSystemLocation = value;
            }
        }
    }

    private const bool DefaultAutoDayNightUseSystemLocation = false;

    public async Task SetAutoDayNightUseSystemLocationAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightUseSystemLocationKey, enabled);
        AutoDayNightUseSystemLocation = enabled;
    }

    #endregion

    #region AutoDayNightUseCustomNightSchedule

    private bool autoDayNightUseCustomNightSchedule = DefaultAutoDayNightUseCustomNightSchedule;
    public bool AutoDayNightUseCustomNightSchedule
    {
        get => autoDayNightUseCustomNightSchedule;
        private set
        {
            if (autoDayNightUseCustomNightSchedule != value)
            {
                autoDayNightUseCustomNightSchedule = value;
            }
        }
    }

    private const bool DefaultAutoDayNightUseCustomNightSchedule = true;

    public async Task SetAutoDayNightUseCustomNightScheduleAsync(bool enabled)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightUseCustomNightScheduleKey, enabled);
        AutoDayNightUseCustomNightSchedule = enabled;
    }

    #endregion

    #region AutoDayNightNightStartTime

    private string autoDayNightNightStartTime = DefaultAutoDayNightNightStartTime;
    public string AutoDayNightNightStartTime
    {
        get => autoDayNightNightStartTime;
        private set
        {
            var normalized = NormalizeNightTimeString(value);

            if (autoDayNightNightStartTime != normalized)
            {
                autoDayNightNightStartTime = normalized;
            }
        }
    }

    private const string DefaultAutoDayNightNightStartTime = "";

    public async Task SetAutoDayNightNightStartTimeAsync(TimeSpan? time)
    {
        var normalized = FormatNightTimeString(time);
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightNightStartTimeKey, normalized);
        AutoDayNightNightStartTime = normalized;
    }

    #endregion

    #region AutoDayNightNightEndTime

    private string autoDayNightNightEndTime = DefaultAutoDayNightNightEndTime;
    public string AutoDayNightNightEndTime
    {
        get => autoDayNightNightEndTime;
        private set
        {
            var normalized = NormalizeNightTimeString(value);

            if (autoDayNightNightEndTime != normalized)
            {
                autoDayNightNightEndTime = normalized;
            }
        }
    }

    private const string DefaultAutoDayNightNightEndTime = "";

    public async Task SetAutoDayNightNightEndTimeAsync(TimeSpan? time)
    {
        var normalized = FormatNightTimeString(time);
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoDayNightNightEndTimeKey, normalized);
        AutoDayNightNightEndTime = normalized;
    }

    #endregion

    #region BackgroundEfficiency

    private string backgroundEfficiencyProcessWhitelist = DefaultBackgroundEfficiencyProcessWhitelist;
    public string BackgroundEfficiencyProcessWhitelist
    {
        get => backgroundEfficiencyProcessWhitelist;
        private set
        {
            var normalizedValue = NormalizeEscapedText(value ?? string.Empty);

            if (backgroundEfficiencyProcessWhitelist != normalizedValue)
            {
                backgroundEfficiencyProcessWhitelist = normalizedValue;
            }
        }
    }

    private static readonly string DefaultBackgroundEfficiencyProcessWhitelist = "DefaultProcessWhitelist".GetLocalizedString();

    public async Task SetBackgroundEfficiencyProcessWhitelistAsync(string value)
    {
        var normalizedValue = NormalizeEscapedText(value ?? string.Empty);
        await SaveDataInSettingsAsync(_localSettingsKeys.BackgroundEfficiencyProcessWhitelistKey, normalizedValue);
        BackgroundEfficiencyProcessWhitelist = normalizedValue;
    }

    private string backgroundEfficiencyProcessBlacklist = DefaultBackgroundEfficiencyProcessBlacklist;
    public string BackgroundEfficiencyProcessBlacklist
    {
        get => backgroundEfficiencyProcessBlacklist;
        private set
        {
            var normalizedValue = NormalizeEscapedText(value ?? string.Empty);

            if (backgroundEfficiencyProcessBlacklist != normalizedValue)
            {
                backgroundEfficiencyProcessBlacklist = normalizedValue;
            }
        }
    }

    private static readonly string DefaultBackgroundEfficiencyProcessBlacklist = "DefaultProcessBlacklist".GetLocalizedString();

    public async Task SetBackgroundEfficiencyProcessBlacklistAsync(string value)
    {
        var normalizedValue = NormalizeEscapedText(value ?? string.Empty);
        await SaveDataInSettingsAsync(_localSettingsKeys.BackgroundEfficiencyProcessBlacklistKey, normalizedValue);
        BackgroundEfficiencyProcessBlacklist = normalizedValue;
    }

    #endregion

    #region AutomationGameList

    private string automationGameList = DefaultAutomationGameList;
    public string AutomationGameList
    {
        get => automationGameList;
        private set
        {
            var normalizedValue = NormalizeEscapedText(value ?? string.Empty);

            if (automationGameList != normalizedValue)
            {
                automationGameList = normalizedValue;
            }
        }
    }

    private static readonly string DefaultAutomationGameList = "notepad.exe";

    public async Task SetAutomationGameListAsync(string value)
    {
        var normalizedValue = NormalizeEscapedText(value ?? string.Empty);
        await SaveDataInSettingsAsync(_localSettingsKeys.AutomationGameListKey, normalizedValue);
        AutomationGameList = normalizedValue;
    }

    #endregion

    #region AutomationSceneList

    private string automationSceneList = DefaultAutomationSceneList;
    public string AutomationSceneList
    {
        get => automationSceneList;
        private set
        {
            var normalizedValue = NormalizeEscapedText(value ?? string.Empty);

            if (automationSceneList != normalizedValue)
            {
                automationSceneList = normalizedValue;
            }
        }
    }

    private const string DefaultAutomationSceneList = "";

    public async Task SetAutomationSceneListAsync(string value)
    {
        var normalizedValue = NormalizeEscapedText(value ?? string.Empty);
        await SaveDataInSettingsAsync(_localSettingsKeys.AutomationSceneListKey, normalizedValue);
        AutomationSceneList = normalizedValue;
    }

    #endregion

    #region AutomationProgramListLastScanDate

    private string automationProgramListLastScanDate = DefaultAutomationProgramListLastScanDate;
    public string AutomationProgramListLastScanDate
    {
        get => automationProgramListLastScanDate;
        private set
        {
            var normalizedValue = NormalizeEscapedText(value ?? string.Empty);

            if (automationProgramListLastScanDate != normalizedValue)
            {
                automationProgramListLastScanDate = normalizedValue;
            }
        }
    }

    private const string DefaultAutomationProgramListLastScanDate = "";

    public async Task SetAutomationProgramListLastScanDateAsync(string value)
    {
        var normalizedValue = NormalizeEscapedText(value ?? string.Empty);
        await SaveDataInSettingsAsync(_localSettingsKeys.AutomationProgramListLastScanDateKey, normalizedValue);
        AutomationProgramListLastScanDate = normalizedValue;
    }

    #endregion
    #region Helper Methods

    private T GetDataFromSettings<T>(string settingsKey, T defaultData)
    {
        var data = _localSettingsService.ReadSetting<string>(settingsKey);
        var normalizedData = NormalizeSettingsValue(data);

        if (typeof(T) == typeof(bool) && bool.TryParse(normalizedData, out var cacheBoolData))
        {
            return (T)(object)cacheBoolData;
        }
        else if (typeof(T) == typeof(int) && int.TryParse(normalizedData, out var cacheIntData))
        {
            return (T)(object)cacheIntData;
        }
        else if (typeof(T) == typeof(DateTime) && DateTime.TryParse(normalizedData, out var cacheDateTimeData))
        {
            return (T)(object)cacheDateTimeData;
        }
        else if (typeof(T) == typeof(double) && double.TryParse(normalizedData, out var cacheDoubleData))
        {
            return (T)(object)cacheDoubleData;
        }
        else if (typeof(T) == typeof(string) && normalizedData is string cacheStringData)
        {
            return (T)(object)cacheStringData;
        }
        else if (typeof(T).IsEnum && Enum.TryParse(typeof(T), normalizedData, out var cacheEnumData))
        {
            return (T)cacheEnumData;
        }

        return defaultData;
    }

    private static T GetDataFromSettings<T>(IReadOnlyDictionary<string, object> snapshot, string settingsKey, T defaultData)
    {
        if (!snapshot.TryGetValue(settingsKey, out var rawValue))
        {
            return defaultData;
        }

        var normalizedData = NormalizeSettingsValue(ExtractSettingText(rawValue));

        if (typeof(T) == typeof(bool) && bool.TryParse(normalizedData, out var cacheBoolData))
        {
            return (T)(object)cacheBoolData;
        }
        else if (typeof(T) == typeof(int) && int.TryParse(normalizedData, out var cacheIntData))
        {
            return (T)(object)cacheIntData;
        }
        else if (typeof(T) == typeof(DateTime) && DateTime.TryParse(normalizedData, out var cacheDateTimeData))
        {
            return (T)(object)cacheDateTimeData;
        }
        else if (typeof(T) == typeof(double) && double.TryParse(normalizedData, out var cacheDoubleData))
        {
            return (T)(object)cacheDoubleData;
        }
        else if (typeof(T) == typeof(string) && normalizedData is string cacheStringData)
        {
            return (T)(object)cacheStringData;
        }
        else if (typeof(T).IsEnum && Enum.TryParse(typeof(T), normalizedData, out var cacheEnumData))
        {
            return (T)cacheEnumData;
        }

        return defaultData;
    }

    private static string? ExtractSettingText(object? rawValue)
    {
        if (rawValue is null)
        {
            return null;
        }

        if (rawValue is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.String
                ? jsonElement.GetString()
                : jsonElement.ToString();
        }

        return rawValue.ToString();
    }

    private async Task SaveDataInSettingsAsync<T>(string settingsKey, T data)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync(settingsKey, data);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to save app setting {SettingsKey}", settingsKey);
            throw;
        }
    }

    private static string NormalizeSettingsValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();
        value = value.Replace("\\u002B", "+", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("\\uFF0B", "+", StringComparison.OrdinalIgnoreCase);
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value[1..^1];
        }

        return value;
    }

    private static string NormalizeEscapedText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains("\\u", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("\\r", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("\\n", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        try
        {
            return Regex.Unescape(value);
        }
        catch
        {
            return value;
        }
    }

    private static string NormalizeNightTimeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return TimeSpan.TryParse(value, out var time)
            ? FormatNightTimeString(time)
            : string.Empty;
    }

    private static string FormatNightTimeString(TimeSpan? time)
    {
        return time is null ? string.Empty : FormatNightTimeString(time.Value);
    }

    private static string FormatNightTimeString(TimeSpan time)
    {
        var totalMinutes = ((int)time.TotalMinutes % (24 * 60) + (24 * 60)) % (24 * 60);
        return TimeSpan.FromMinutes(totalMinutes).ToString(@"hh\:mm");
    }

    #endregion
}





