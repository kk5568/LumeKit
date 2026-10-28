using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using BluetoothAudioReceiver.Models;
using BluetoothAudioReceiver.Services;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class SettingsPageViewModel : ObservableRecipient, INavigationAware
{
    #region View Properties

    public ObservableCollection<AppLanguageItem> AppLanguages = AppLanguageHelper.SupportedLanguages;

    public Visibility NonlogonTaskCardVisibility = RuntimeHelper.IsMSIX ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LogonTaskExpanderVisibility = RuntimeHelper.IsMSIX ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    public partial int LanguageIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowRestartTip { get; set; }

    [ObservableProperty]
    public partial bool RunStartup { get; set; }

    [ObservableProperty]
    public partial bool LogonTask { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial int BackdropTypeIndex { get; set; }

    [ObservableProperty]
    public partial string AppDisplayName { get; set; } = ConstantHelper.AppDisplayName;

    [ObservableProperty]
    public partial string Version { get; set; } = $"v{InfoHelper.GetDisplayVersion()}";

    [ObservableProperty]
    public partial string CopyRight { get; set; } = $"{InfoHelper.GetCopyright()}";

    #endregion

    #region Auto Brightness Properties

    [ObservableProperty]
    public partial bool AutoBrightnessEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutoDayNightEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutoDayNightUseSystemLocation { get; set; }

    [ObservableProperty]
    public partial byte MinBrightness { get; set; }

    [ObservableProperty]
    public partial byte MaxBrightness { get; set; }

    [ObservableProperty]
    public partial double MinColorTemp { get; set; }

    [ObservableProperty]
    public partial double MaxColorTemp { get; set; }

    #endregion

    private readonly IAppSettingsService _appSettingsService;
    private readonly IBackdropSelectorService _backdropSelectorService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly BrightnessSettings _brightnessSettings;
    private readonly AutoBrightnessService _autoBrightnessService;

    private bool _isInitialized;
    private bool _startupStateLoaded;
    private bool _updatingStartup;
    private bool _runStartupSnapshot;
    private bool _logonTaskSnapshot;

    public SettingsPageViewModel(
        IAppSettingsService appSettingsService, 
        IBackdropSelectorService backdropSelectorService, 
        IThemeSelectorService themeSelectorService,
        BrightnessSettings brightnessSettings,
        AutoBrightnessService autoBrightnessService)
    {
        _appSettingsService = appSettingsService;
        _backdropSelectorService = backdropSelectorService;
        _themeSelectorService = themeSelectorService;
        _brightnessSettings = brightnessSettings;
        _autoBrightnessService = autoBrightnessService;

        InitializeSettings();
        InitializeAutoBrightnessSettings();
    }

    private void InitializeSettings()
    {
        ThemeIndex = 0;
        BackdropTypeIndex = (int)_appSettingsService.BackdropType;
        AutoDayNightEnabled = _appSettingsService.AutoDayNightEnabled;
        AutoDayNightUseSystemLocation = _appSettingsService.AutoDayNightUseSystemLocation;

        _isInitialized = true;
    }

    private void InitializeAutoBrightnessSettings()
    {
        AutoBrightnessEnabled = _brightnessSettings.AutoEnabled;
        MinBrightness = _brightnessSettings.MinBrightness;
        MaxBrightness = _brightnessSettings.MaxBrightness;
        MinColorTemp = _brightnessSettings.MinColorTemp;
        MaxColorTemp = _brightnessSettings.MaxColorTemp;
    }

    #region INavigationAware

    public async Task OnNavigatedToAsync(object parameter)
    {
        var wasInitialized = _isInitialized;
        _isInitialized = false;

        try
        {
            LanguageIndex = AppLanguageHelper.SupportedLanguages.IndexOf(AppLanguageHelper.PreferredLanguage);

            if (!_startupStateLoaded)
            {
                var logonTaskQuery = StartupHelper.GetStartupAsync(logon: true);
                var startupEntryQuery = StartupHelper.GetStartupAsync();
                await Task.WhenAll(logonTaskQuery, startupEntryQuery);

                _logonTaskSnapshot = await logonTaskQuery;
                _runStartupSnapshot = _logonTaskSnapshot || await startupEntryQuery;
                _startupStateLoaded = true;
            }

            RunStartup = _runStartupSnapshot;
            LogonTask = _logonTaskSnapshot;
        }
        catch
        {
            // Some environments may fail to query startup task/registry state.
            // Keep settings page alive and show safe defaults instead of crashing.
            RunStartup = false;
            LogonTask = false;
            _runStartupSnapshot = false;
            _logonTaskSnapshot = false;
        }
        finally
        {
            _isInitialized = wasInitialized;
            ShowRestartTip = false;
        }
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void RestartApplication()
    {
        App.RestartApplication();
    }

    [RelayCommand]
    private void CancelRestart()
    {
        ShowRestartTip = false;
    }

    #endregion

    #region Property Events

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_isInitialized || value < 0 || value >= AppLanguages.Count)
        {
            return;
        }

        var changed = false;
        if (RuntimeHelper.IsMSIX)
        {
            changed = AppLanguageHelper.TryChange(value);
        }
        else
        {
            _appSettingsService.SetLanguageAsync(AppLanguageHelper.GetLanguageCode(value)).Forget(nameof(SettingsPageViewModel));
            changed = AppLanguageHelper.TryChange(value);
        }

        ShowRestartTip = changed;
    }

    partial void OnRunStartupChanged(bool value)
    {
        if (!_isInitialized || _updatingStartup)
        {
            return;
        }

        _ = ApplyRunStartupAsync(value);
    }

    partial void OnLogonTaskChanged(bool value)
    {
        if (!_isInitialized || _updatingStartup || !RunStartup)
        {
            return;
        }

        _ = ApplyLogonTaskAsync(value);
    }

    private async Task ApplyRunStartupAsync(bool value)
    {
        _updatingStartup = true;
        try
        {
            bool applied;
            if (value)
            {
                applied = await StartupHelper.SetStartupAsync(true, logon: LogonTask);
            }
            else
            {
                await StartupHelper.SetStartupAsync(false, logon: true);
                applied = await StartupHelper.SetStartupAsync(false);
            }

            if (!applied)
            {
                // The OS rejected the change; re-read and show the real state.
                await RestoreStartupStateAsync();
                return;
            }

            _runStartupSnapshot = value;
            if (!value)
            {
                _logonTaskSnapshot = false;
                LogonTask = false;
            }
        }
        finally
        {
            _updatingStartup = false;
        }
    }

    private async Task ApplyLogonTaskAsync(bool value)
    {
        _updatingStartup = true;
        try
        {
            await StartupHelper.SetStartupAsync(false, logon: !value);
            var applied = await StartupHelper.SetStartupAsync(true, logon: value);
            if (!applied)
            {
                // The OS rejected the change; re-read and show the real state.
                await RestoreStartupStateAsync();
                return;
            }

            _runStartupSnapshot = true;
            _logonTaskSnapshot = value;
        }
        finally
        {
            _updatingStartup = false;
        }
    }

    private async Task RestoreStartupStateAsync()
    {
        try
        {
            var logonTaskQuery = StartupHelper.GetStartupAsync(logon: true);
            var startupEntryQuery = StartupHelper.GetStartupAsync();
            await Task.WhenAll(logonTaskQuery, startupEntryQuery);
            _logonTaskSnapshot = await logonTaskQuery;
            _runStartupSnapshot = _logonTaskSnapshot || await startupEntryQuery;
        }
        catch
        {
            _runStartupSnapshot = false;
            _logonTaskSnapshot = false;
        }

        RunStartup = _runStartupSnapshot;
        LogonTask = _logonTaskSnapshot;
    }

    partial void OnBackdropTypeIndexChanged(int value)
    {
        if (_isInitialized)
        {
            _backdropSelectorService.SetBackdropTypeAsync((BackdropType)value).Forget(nameof(SettingsPageViewModel));
        }
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_isInitialized)
        {
            if (value != 0)
            {
                ThemeIndex = 0;
                return;
            }

            _themeSelectorService.SetThemeAsync(ElementTheme.Default).Forget(nameof(SettingsPageViewModel));
        }
    }

    partial void OnAutoDayNightEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _appSettingsService.SetAutoDayNightEnabledAsync(value).Forget(nameof(SettingsPageViewModel));
        }
    }

    partial void OnAutoDayNightUseSystemLocationChanged(bool value)
    {
        if (_isInitialized)
        {
            _appSettingsService.SetAutoDayNightUseSystemLocationAsync(value).Forget(nameof(SettingsPageViewModel));
        }
    }

    partial void OnAutoBrightnessEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = _autoBrightnessService.SetEnabledAsync(value, "settings-page");
        }
    }

    partial void OnMinBrightnessChanged(byte value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.MinBrightness = value;
        }
    }

    partial void OnMaxBrightnessChanged(byte value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.MaxBrightness = value;
        }
    }

    partial void OnMinColorTempChanged(double value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.MinColorTemp = value;
        }
    }

    partial void OnMaxColorTempChanged(double value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.MaxColorTemp = value;
        }
    }

    #endregion
}
