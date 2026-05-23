using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.Services;

internal class AppSettingsService(ILocalSettingsService localSettingsService, LocalSettingsKeys localSettingsKeys) : IAppSettingsService
{
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly LocalSettingsKeys _localSettingsKeys = localSettingsKeys;

    private bool _isInitialized;

    public void Initialize()
    {
        if (!_isInitialized)
        {
            // initialize local settings
            Language = GetLanguage();
            Theme = GetTheme();
            BackdropType = GetBackdropType();
            AutoConnect = GetAutoConnect();
            NotificationEnabled = GetNotificationEnabled();
            DisableNotificationInGame = GetDisableNotificationInGame();
            DisableNotificationInVideo = GetDisableNotificationInVideo();
            DisableNotificationInFullscreen = GetDisableNotificationInFullscreen();
            DisableNotificationInRecordingOrLive = GetDisableNotificationInRecordingOrLive();
            HideNotificationWhenTrayPopupVisible = GetHideNotificationWhenTrayPopupVisible();
            PreferredInputDeviceId = GetPreferredInputDeviceId();
            PreferredOutputDeviceId = GetPreferredOutputDeviceId();
            InputHotkey = GetInputHotkey();
            OutputHotkey = GetOutputHotkey();

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

    #region AutoConnect

    private bool autoConnect = DefaultAutoConnect;
    public bool AutoConnect
    {
        get => autoConnect;
        private set
        {
            if (autoConnect != value)
            {
                autoConnect = value;
            }
        }
    }

    private const bool DefaultAutoConnect = true;

    private bool GetAutoConnect()
    {
        var data = GetDataFromSettings(_localSettingsKeys.AutoConnectKey, DefaultAutoConnect);
        return data;
    }

    public async Task SetAutoConnectAsync(bool autoConnect)
    {
        await SaveDataInSettingsAsync(_localSettingsKeys.AutoConnectKey, autoConnect);
        AutoConnect = autoConnect;
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

    private const bool DefaultNotificationEnabled = true;

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

    private async Task SaveDataInSettingsAsync<T>(string settingsKey, T data)
    {
        await _localSettingsService.SaveSettingAsync(settingsKey, data);
    }

    private static string NormalizeSettingsValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value[1..^1];
        }

        return value;
    }

    #endregion
}
