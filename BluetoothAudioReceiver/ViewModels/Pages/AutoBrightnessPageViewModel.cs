using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BluetoothAudioReceiver.Models;
using BluetoothAudioReceiver.Services;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class AutoBrightnessPageViewModel : ObservableRecipient, INavigationAware
{
    [ObservableProperty]
    public partial string Message { get; set; } = "自动亮度";

    [ObservableProperty]
    private bool _autoBrightnessEnabled;
    public string AutoBrightnessEnabledText => AutoBrightnessEnabled ? "开" : "关";

    [ObservableProperty]
    private bool _usePreferenceEnabled;
    public string UsePreferenceEnabledText => UsePreferenceEnabled ? "开" : "关";

    [ObservableProperty]
    private byte _minBrightness;

    [ObservableProperty]
    private byte _maxBrightness;

    [ObservableProperty]
    private double _minColorTemp;

    [ObservableProperty]
    private double _maxColorTemp;

    private readonly BrightnessSettings _brightnessSettings;
    private readonly CameraReaderService _cameraReader;
    private bool _isInitialized;

    public AutoBrightnessPageViewModel(BrightnessSettings brightnessSettings, CameraReaderService cameraReader)
    {
        _brightnessSettings = brightnessSettings;
        _cameraReader = cameraReader;
    }

    public void OnNavigatedTo(object parameter)
    {
        AutoBrightnessEnabled = _brightnessSettings.AutoEnabled;
        UsePreferenceEnabled = _brightnessSettings.UsePreference;
        MinBrightness = _brightnessSettings.MinBrightness;
        MaxBrightness = _brightnessSettings.MaxBrightness;
        MinColorTemp = _brightnessSettings.MinColorTemp;
        MaxColorTemp = _brightnessSettings.MaxColorTemp;
        _isInitialized = true;
    }

    public void OnNavigatedFrom() { }

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

    partial void OnAutoBrightnessEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.AutoEnabled = value;
        }
        OnPropertyChanged(nameof(AutoBrightnessEnabledText));
    }

    partial void OnUsePreferenceEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _brightnessSettings.UsePreference = value;
        }
        OnPropertyChanged(nameof(UsePreferenceEnabledText));
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
}
