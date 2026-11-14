using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.DeskBoxFloating.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class IslandPageViewModel : ObservableRecipient, INavigationAware
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly IDeskBoxFloatingService _floatingService;

    [ObservableProperty]
    public partial bool IslandEnabled { get; set; }

    [ObservableProperty]
    public partial string IslandMaterialType { get; set; } =
        DeskBoxFloatingMaterialTypes.ToDisplay(DeskBoxFloatingMaterialTypes.AcrylicBase);

    public IReadOnlyList<string> IslandMaterialTypeOptions { get; } = DeskBoxFloatingMaterialTypes.All;

    [ObservableProperty]
    public partial string IslandLayoutPreset { get; set; } = "Standard";

    [ObservableProperty]
    public partial double IslandWidth { get; set; }

    [ObservableProperty]
    public partial double IslandHeight { get; set; }

    [ObservableProperty]
    public partial double IslandTopMargin { get; set; }

    [ObservableProperty]
    public partial string IslandHorizontalPosition { get; set; } = "Center";

    [ObservableProperty]
    public partial double IslandHorizontalOffset { get; set; }

    [ObservableProperty]
    public partial bool IslandAutoBrightnessStatusEnabled { get; set; }

    [ObservableProperty]
    public partial bool IslandAutoBrightnessDetailsEnabled { get; set; }

    private bool _applyingPreset;
    private bool _isLoading;

    public string IslandWidthText => $"{IslandWidth:0} px";
    public string IslandHeightText => $"{IslandHeight:0} px";
    public string IslandTopMarginText => $"{IslandTopMargin:0} px";
    public string IslandHorizontalOffsetText => $"{IslandHorizontalOffset:0} px";
    public bool IsCustomLayout => IslandLayoutPreset == "Custom";

    public IslandPageViewModel(IAppSettingsService appSettingsService, IDeskBoxFloatingService floatingService)
    {
        _appSettingsService = appSettingsService;
        _floatingService = floatingService;
    }

    [RelayCommand]
    private void TestIsland()
    {
        if (IslandEnabled)
        {
            _floatingService.ToggleIslandPrototype();
        }
    }

    public Task OnNavigatedToAsync(object parameter)
    {
        _isLoading = true;
        IslandEnabled = _appSettingsService.IslandEnabled;
        IslandMaterialType = DeskBoxFloatingMaterialTypes.ToDisplay(_appSettingsService.IslandMaterialType);
        IslandLayoutPreset = _appSettingsService.IslandLayoutPreset;
        IslandWidth = _appSettingsService.IslandWidth;
        IslandHeight = _appSettingsService.IslandHeight;
        IslandTopMargin = _appSettingsService.IslandTopMargin;
        IslandHorizontalPosition = _appSettingsService.IslandHorizontalPosition;
        IslandHorizontalOffset = _appSettingsService.IslandHorizontalOffset;
        IslandAutoBrightnessStatusEnabled = _appSettingsService.IslandAutoBrightnessStatusEnabled;
        IslandAutoBrightnessDetailsEnabled = _appSettingsService.IslandAutoBrightnessDetailsEnabled;
        _isLoading = false;
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    partial void OnIslandEnabledChanged(bool value)
    {
        if (!_isLoading)
        {
            _appSettingsService.SetIslandEnabledAsync(value).Forget(nameof(IslandPageViewModel));
        }
        App.MainWindow.RefreshIslandPreviewVisibility();
    }

    partial void OnIslandMaterialTypeChanged(string value)
    {
        if (!_isLoading)
        {
            _appSettingsService.SetIslandMaterialTypeAsync(
                DeskBoxFloatingMaterialTypes.FromDisplay(value)).Forget(nameof(IslandPageViewModel));
        }
    }

    partial void OnIslandLayoutPresetChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomLayout));
        if (!_isLoading)
        {
            _appSettingsService.SetIslandLayoutPresetAsync(value).Forget(nameof(IslandPageViewModel));
            if (value != "Custom")
            {
                ApplyPreset(value);
            }
        }
    }

    partial void OnIslandWidthChanged(double value)
    {
        OnPropertyChanged(nameof(IslandWidthText));
        if (!_isLoading) _appSettingsService.SetIslandWidthAsync((int)value).Forget(nameof(IslandPageViewModel));
        EnsureCustomLayout();
    }

    partial void OnIslandHeightChanged(double value)
    {
        OnPropertyChanged(nameof(IslandHeightText));
        if (!_isLoading) _appSettingsService.SetIslandHeightAsync((int)value).Forget(nameof(IslandPageViewModel));
        EnsureCustomLayout();
    }

    partial void OnIslandTopMarginChanged(double value)
    {
        OnPropertyChanged(nameof(IslandTopMarginText));
        if (!_isLoading) _appSettingsService.SetIslandTopMarginAsync((int)value).Forget(nameof(IslandPageViewModel));
        EnsureCustomLayout();
    }

    partial void OnIslandHorizontalPositionChanged(string value)
    {
        if (!_isLoading) _appSettingsService.SetIslandHorizontalPositionAsync(value).Forget(nameof(IslandPageViewModel));
        EnsureCustomLayout();
    }

    partial void OnIslandHorizontalOffsetChanged(double value)
    {
        OnPropertyChanged(nameof(IslandHorizontalOffsetText));
        if (!_isLoading) _appSettingsService.SetIslandHorizontalOffsetAsync((int)value).Forget(nameof(IslandPageViewModel));
        EnsureCustomLayout();
    }

    partial void OnIslandAutoBrightnessStatusEnabledChanged(bool value)
    {
        if (!_isLoading)
        {
            _appSettingsService.SetIslandAutoBrightnessStatusEnabledAsync(value).Forget(nameof(IslandPageViewModel));
        }
    }

    partial void OnIslandAutoBrightnessDetailsEnabledChanged(bool value)
    {
        if (!_isLoading)
        {
            _appSettingsService.SetIslandAutoBrightnessDetailsEnabledAsync(value).Forget(nameof(IslandPageViewModel));
        }
    }

    private void ApplyPreset(string preset)
    {
        _applyingPreset = true;
        (IslandWidth, IslandHeight, IslandTopMargin, IslandHorizontalPosition, IslandHorizontalOffset) = preset switch
        {
            "Compact" => (220, 48, 40, "Center", 0),
            "Wide" => (360, 64, 60, "Center", 0),
            _ => (280, 55, 50, "Center", 0)
        };
        _applyingPreset = false;
    }

    private void EnsureCustomLayout()
    {
        if (!_isLoading && !_applyingPreset && IslandLayoutPreset != "Custom")
        {
            IslandLayoutPreset = "Custom";
        }
    }
}
