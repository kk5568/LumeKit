using System.Collections.ObjectModel;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Navigation;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class AutoBrightnessPageViewModel : ObservableRecipient, INavigationAware
{
    private readonly ObservableCollection<string> _brightnessMinimumOptions = new(
        [
            "0%",
            "5%",
            "10%",
            "15%",
            "20%",
            "25%",
            "30%",
            "35%",
            "40%",
            "45%",
            "50%"
        ]);

    private readonly ObservableCollection<string> _brightnessMaximumOptions = new(
        [
            "50%",
            "55%",
            "60%",
            "65%",
            "70%",
            "75%",
            "80%",
            "85%",
            "90%",
            "95%",
            "100%"
        ]);

    private readonly ObservableCollection<string> _colorTemperatureMinimumOptions = new(
        [
            "2000K",
            "2500K",
            "3000K",
            "3500K",
            "4000K",
            "4500K",
            "5000K",
            "5500K",
            "6000K",
            "6500K",
            "7000K",
            "7500K",
            "8000K",
            "8500K"
        ]);

    private readonly ObservableCollection<string> _colorTemperatureMaximumOptions = new(
        [
            "2500K",
            "3000K",
            "3500K",
            "4000K",
            "4500K",
            "5000K",
            "5500K",
            "6000K",
            "6500K",
            "7000K",
            "7500K",
            "8000K",
            "8500K",
            "9000K"
        ]);

    private readonly ObservableCollection<string> _foregroundCooldownOptions = new(
        [
            "不冷却",
            "1分钟",
            "5分钟",
            "10分钟",
            "30分钟",
            "1小时"
        ]);

    private readonly BrightnessSettings _brightnessSettings;
    private readonly IAppSettingsService _appSettingsService;
    private readonly AutoBrightnessService _autoBrightnessService;

    private bool _brightnessEnabled = true;
    private bool _colorTemperatureEnabled = true;
    private int _selectedMinimumBrightnessIndex;
    private int _selectedMaximumBrightnessIndex;
    private int _selectedMinimumColorTemperatureIndex;
    private int _selectedMaximumColorTemperatureIndex;
    private string _customShortcutText = "未设置";
    private bool _autoBrightnessNotificationEnabled = true;
    private bool _autoBrightnessOnUnlockEnabled = true;
    private bool _autoBrightnessOnUnlockNotificationEnabled = true;
    private bool _autoBrightnessOnForegroundEnabled;
    private bool _autoBrightnessOnForegroundNotificationEnabled = true;
    private string _selectedForegroundCooldownText = "10分钟";
    private bool _disableNightLightEnabled = false;
    private bool _disableNightLightAtNightEnabled = false;
    private string _shortcutStatusText = string.Empty;

    public AutoBrightnessPageViewModel(
        BrightnessSettings brightnessSettings,
        IAppSettingsService appSettingsService,
        AutoBrightnessService autoBrightnessService)
    {
        _brightnessSettings = brightnessSettings;
        _appSettingsService = appSettingsService;
        _autoBrightnessService = autoBrightnessService;

        LoadFromSettings();
    }

    public ObservableCollection<string> BrightnessMinimumOptions => _brightnessMinimumOptions;
    public ObservableCollection<string> BrightnessMaximumOptions => _brightnessMaximumOptions;
    public ObservableCollection<string> ColorTemperatureMinimumOptions => _colorTemperatureMinimumOptions;
    public ObservableCollection<string> ColorTemperatureMaximumOptions => _colorTemperatureMaximumOptions;
    public ObservableCollection<string> ForegroundCooldownOptions => _foregroundCooldownOptions;

    public bool BrightnessEnabled
    {
        get => _brightnessEnabled;
        set
        {
            if (SetProperty(ref _brightnessEnabled, value))
            {
                _ = _autoBrightnessService.SetEnabledAsync(value, "auto-brightness-page");
            }
        }
    }

    public bool ColorTemperatureEnabled
    {
        get => _colorTemperatureEnabled;
        set => SetProperty(ref _colorTemperatureEnabled, value);
    }

    public int SelectedMinimumBrightnessIndex
    {
        get => _selectedMinimumBrightnessIndex;
        set
        {
            var nextValue = Math.Clamp(value, 0, BrightnessMinimumOptions.Count - 1);
            if (SetProperty(ref _selectedMinimumBrightnessIndex, nextValue))
            {
                _brightnessSettings.MinBrightness = (byte)(nextValue * 5);
                if (_selectedMaximumBrightnessIndex < nextValue)
                {
                    SelectedMaximumBrightnessIndex = nextValue;
                }
            }
        }
    }

    public int SelectedMaximumBrightnessIndex
    {
        get => _selectedMaximumBrightnessIndex;
        set
        {
            var nextValue = Math.Clamp(value, SelectedMinimumBrightnessIndex, BrightnessMaximumOptions.Count - 1);
            if (SetProperty(ref _selectedMaximumBrightnessIndex, nextValue))
            {
                _brightnessSettings.MaxBrightness = (byte)(50 + nextValue * 5);
            }
        }
    }

    public int SelectedMinimumColorTemperatureIndex
    {
        get => _selectedMinimumColorTemperatureIndex;
        set
        {
            var nextValue = Math.Clamp(value, 0, ColorTemperatureMinimumOptions.Count - 1);
            if (SetProperty(ref _selectedMinimumColorTemperatureIndex, nextValue))
            {
                _brightnessSettings.MinColorTemp = 2000 + nextValue * 500;
                if (_selectedMaximumColorTemperatureIndex < nextValue)
                {
                    SelectedMaximumColorTemperatureIndex = nextValue;
                }
            }
        }
    }

    public int SelectedMaximumColorTemperatureIndex
    {
        get => _selectedMaximumColorTemperatureIndex;
        set
        {
            var nextValue = Math.Clamp(value, SelectedMinimumColorTemperatureIndex, ColorTemperatureMaximumOptions.Count - 1);
            if (SetProperty(ref _selectedMaximumColorTemperatureIndex, nextValue))
            {
                _brightnessSettings.MaxColorTemp = 2500 + nextValue * 500;
            }
        }
    }

    public string CustomShortcutText
    {
        get => _customShortcutText;
        set => SetProperty(ref _customShortcutText, string.IsNullOrWhiteSpace(value) ? "未设置" : value.Trim());
    }

    public bool AutoBrightnessNotificationEnabled
    {
        get => _autoBrightnessNotificationEnabled;
        set
        {
            if (SetProperty(ref _autoBrightnessNotificationEnabled, value))
            {
                _appSettingsService.SetAutoBrightnessNotificationEnabledAsync(value).Forget(nameof(AutoBrightnessPageViewModel));
            }
        }
    }

    public bool AutoBrightnessOnUnlockEnabled
    {
        get => _autoBrightnessOnUnlockEnabled;
        set
        {
            if (SetProperty(ref _autoBrightnessOnUnlockEnabled, value))
            {
                _appSettingsService.SetAutoBrightnessOnUnlockEnabledAsync(value).Forget(nameof(AutoBrightnessPageViewModel));
            }
        }
    }

    public bool AutoBrightnessOnUnlockNotificationEnabled
    {
        get => _autoBrightnessOnUnlockNotificationEnabled;
        set
        {
            if (SetProperty(ref _autoBrightnessOnUnlockNotificationEnabled, value))
            {
                _appSettingsService.SetAutoBrightnessOnUnlockNotificationEnabledAsync(value).Forget(nameof(AutoBrightnessPageViewModel));
            }
        }
    }

    public bool DisableNightLightEnabled
    {
        get => _disableNightLightEnabled;
        set
        {
            if (SetProperty(ref _disableNightLightEnabled, value))
            {
                _appSettingsService.SetDisableNightLightEnabledAsync(value).Forget(nameof(AutoBrightnessPageViewModel));
                _autoBrightnessService.UpdateNightLightState();
            }
        }
    }

    public bool DisableNightLightAtNightEnabled
    {
        get => _disableNightLightAtNightEnabled;
        set
        {
            if (SetProperty(ref _disableNightLightAtNightEnabled, value))
            {
                _appSettingsService.SetDisableNightLightAtNightEnabledAsync(value).Forget(nameof(AutoBrightnessPageViewModel));
                _autoBrightnessService.UpdateNightLightState();
            }
        }
    }

    public bool AutoBrightnessOnForegroundEnabled
    {
        get => _autoBrightnessOnForegroundEnabled;
        set
        {
            if (SetProperty(ref _autoBrightnessOnForegroundEnabled, value))
            {
                _appSettingsService.SetAutoBrightnessOnForegroundEnabledAsync(value).Forget(nameof(AutoBrightnessPageViewModel));
            }
        }
    }

    public string SelectedForegroundCooldownText
    {
        get => _selectedForegroundCooldownText;
        set
        {
            var nextValue = ForegroundCooldownOptions.Contains(value)
                ? value
                : "10分钟";
            if (SetProperty(ref _selectedForegroundCooldownText, nextValue))
            {
                _appSettingsService.SetAutoBrightnessOnForegroundCooldownMinutesAsync(ParseForegroundCooldownMinutes(nextValue))
                    .Forget(nameof(AutoBrightnessPageViewModel));
            }
        }
    }

    public bool AutoBrightnessOnForegroundNotificationEnabled
    {
        get => _autoBrightnessOnForegroundNotificationEnabled;
        set
        {
            if (SetProperty(ref _autoBrightnessOnForegroundNotificationEnabled, value))
            {
                OnPropertyChanged(nameof(ForegroundNotificationStatusText));
                _appSettingsService.SetAutoBrightnessOnForegroundNotificationEnabledAsync(value)
                    .Forget(nameof(AutoBrightnessPageViewModel));
            }
        }
    }

    public string ForegroundNotificationStatusText => _autoBrightnessOnForegroundNotificationEnabled ? "开" : "关";

    public string ShortcutStatusText
    {
        get => _shortcutStatusText;
        set => SetProperty(ref _shortcutStatusText, value ?? string.Empty);
    }

    public int MinimumBrightnessPercent => SelectedMinimumBrightnessIndex * 5;
    public int MaximumBrightnessPercent => 50 + SelectedMaximumBrightnessIndex * 5;
    public int MinimumColorTemperatureKelvin => 2000 + SelectedMinimumColorTemperatureIndex * 500;
    public int MaximumColorTemperatureKelvin => 2500 + SelectedMaximumColorTemperatureIndex * 500;
    public Task OnNavigatedToAsync(object parameter)
    {
        LoadFromSettings();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    private void LoadFromSettings()
    {
        _brightnessEnabled = _brightnessSettings.AutoEnabled;
        _selectedMinimumBrightnessIndex = Math.Clamp(_brightnessSettings.MinBrightness / 5, 0, BrightnessMinimumOptions.Count - 1);
        _selectedMaximumBrightnessIndex = Math.Clamp((_brightnessSettings.MaxBrightness - 50) / 5, 0, BrightnessMaximumOptions.Count - 1);
        _selectedMinimumColorTemperatureIndex = Math.Clamp(((int)_brightnessSettings.MinColorTemp - 2000) / 500, 0, ColorTemperatureMinimumOptions.Count - 1);
        _selectedMaximumColorTemperatureIndex = Math.Clamp(((int)_brightnessSettings.MaxColorTemp - 2500) / 500, 0, ColorTemperatureMaximumOptions.Count - 1);
        _customShortcutText = string.IsNullOrWhiteSpace(_appSettingsService.AutoBrightnessHotkey)
            ? "未设置"
            : _appSettingsService.AutoBrightnessHotkey;
        _autoBrightnessNotificationEnabled = _appSettingsService.AutoBrightnessNotificationEnabled;
        _autoBrightnessOnUnlockEnabled = _appSettingsService.AutoBrightnessOnUnlockEnabled;
        _autoBrightnessOnUnlockNotificationEnabled = _appSettingsService.AutoBrightnessOnUnlockNotificationEnabled;
        _autoBrightnessOnForegroundEnabled = _appSettingsService.AutoBrightnessOnForegroundEnabled;
        _autoBrightnessOnForegroundNotificationEnabled = _appSettingsService.AutoBrightnessOnForegroundNotificationEnabled;
        _selectedForegroundCooldownText = FormatForegroundCooldown(_appSettingsService.AutoBrightnessOnForegroundCooldownMinutes);
        _disableNightLightEnabled = _appSettingsService.DisableNightLightEnabled;
        _disableNightLightAtNightEnabled = _appSettingsService.DisableNightLightAtNightEnabled;
    }

    private static int ParseForegroundCooldownMinutes(string value)
    {
        return value switch
        {
            "不冷却" => 0,
            "1分钟" => 1,
            "5分钟" => 5,
            "30分钟" => 30,
            "1小时" => 60,
            _ => 10
        };
    }

    private static string FormatForegroundCooldown(int minutes)
    {
        return minutes switch
        {
            0 => "不冷却",
            1 => "1分钟",
            5 => "5分钟",
            30 => "30分钟",
            60 => "1小时",
            _ => "10分钟"
        };
    }
}
