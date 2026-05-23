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
    public partial string Version { get; set; } = $"v{InfoHelper.GetVersion()}";

    [ObservableProperty]
    public partial string CopyRight { get; set; } = $"{InfoHelper.GetCopyright()}";

    #endregion

    #region Auto Brightness Properties

    [ObservableProperty]
    public partial bool AutoBrightnessEnabled { get; set; }

    [ObservableProperty]
    public partial bool UsePreferenceEnabled { get; set; }

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
    private readonly CameraReaderService _cameraReader;
    private readonly AutoBrightnessService _autoBrightnessService;

    private bool _isInitialized;

    public SettingsPageViewModel(
        IAppSettingsService appSettingsService, 
        IBackdropSelectorService backdropSelectorService, 
        IThemeSelectorService themeSelectorService,
        BrightnessSettings brightnessSettings,
        CameraReaderService cameraReader,
        AutoBrightnessService autoBrightnessService)
    {
        _appSettingsService = appSettingsService;
        _backdropSelectorService = backdropSelectorService;
        _themeSelectorService = themeSelectorService;
        _brightnessSettings = brightnessSettings;
        _cameraReader = cameraReader;
        _autoBrightnessService = autoBrightnessService;

        InitializeSettings();
        InitializeAutoBrightnessSettings();
    }

    private void InitializeSettings()
    {
        ThemeIndex = (int)_themeSelectorService.Theme;
        BackdropTypeIndex = (int)_appSettingsService.BackdropType;

        _isInitialized = true;
    }

    private void InitializeAutoBrightnessSettings()
    {
        AutoBrightnessEnabled = _brightnessSettings.AutoEnabled;
        UsePreferenceEnabled = _brightnessSettings.UsePreference;
        MinBrightness = _brightnessSettings.MinBrightness;
        MaxBrightness = _brightnessSettings.MaxBrightness;
        MinColorTemp = _brightnessSettings.MinColorTemp;
        MaxColorTemp = _brightnessSettings.MaxColorTemp;
    }

    #region INavigationAware

    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            LanguageIndex = AppLanguageHelper.SupportedLanguages.IndexOf(AppLanguageHelper.PreferredLanguage);

            var logonTask = await StartupHelper.GetStartupAsync(logon: true);
            var startupEntry = await StartupHelper.GetStartupAsync();
            RunStartup = logonTask || startupEntry;
            LogonTask = logonTask;
        }
        catch
        {
            // Some environments may fail to query startup task/registry state.
            // Keep settings page alive and show safe defaults instead of crashing.
            RunStartup = false;
            LogonTask = false;
        }
        finally
        {
            ShowRestartTip = false;
        }
    }

    public void OnNavigatedFrom()
    {
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

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        var (brightness, colorTemp) = await _cameraReader.GetAverageBrightnessAndColorTempAsync();

        _brightnessSettings.BrightnessBias = brightness / 255.0 - 0.5;
        _brightnessSettings.ColorTempBias = (colorTemp - 4000) / 500.0;

        var newMinBrightness = (byte)Math.Clamp(_brightnessSettings.MinBrightness + (byte)(_brightnessSettings.BrightnessBias * 20), 0, 100);
        var newMaxBrightness = (byte)Math.Clamp(_brightnessSettings.MaxBrightness + (byte)(_brightnessSettings.BrightnessBias * 20), 0, 100);
        var newMinColorTemp = Math.Clamp(_brightnessSettings.MinColorTemp + _brightnessSettings.ColorTempBias * 200, 2000, 7500);
        var newMaxColorTemp = Math.Clamp(_brightnessSettings.MaxColorTemp + _brightnessSettings.ColorTempBias * 200, 2000, 7500);

        MinBrightness = newMinBrightness;
        MaxBrightness = newMaxBrightness;
        MinColorTemp = newMinColorTemp;
        MaxColorTemp = newMaxColorTemp;
    }

    #endregion

    #region Property Events

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_isInitialized || value < 0 || value >= AppLanguages.Count)
        {
            return;
        }

        if (RuntimeHelper.IsMSIX)
        {
            AppLanguageHelper.TryChange(value);
        }
        else
        {
            _ = _appSettingsService.SetLanguageAsync(AppLanguageHelper.GetLanguageCode(value));
            AppLanguageHelper.TryChange(value);
        }

        ShowRestartTip = true;
    }

    partial void OnRunStartupChanged(bool value)
    {
        if (_isInitialized)
        {
            if (value)
            {
                _ = StartupHelper.SetStartupAsync(true, logon: LogonTask);
            }
            else
            {
                _ = StartupHelper.SetStartupAsync(false, logon: true);
                _ = StartupHelper.SetStartupAsync(false);
            }
        }
    }

    partial void OnLogonTaskChanged(bool value)
    {
        if (_isInitialized)
        {
            if (RunStartup)
            {
                _ = StartupHelper.SetStartupAsync(false, logon: !value);
                _ = StartupHelper.SetStartupAsync(true, logon: value);
            }
        }
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_isInitialized)
        {
            _themeSelectorService.SetThemeAsync((ElementTheme)value);
        }
    }

    partial void OnBackdropTypeIndexChanged(int value)
    {
        if (_isInitialized)
        {
            _backdropSelectorService.SetBackdropTypeAsync((BackdropType)value);
        }
    }

    partial void OnAutoBrightnessEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.AutoEnabled = value;
        }
    }

    partial void OnUsePreferenceEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.UsePreference = value;
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
