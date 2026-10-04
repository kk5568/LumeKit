using BluetoothAudioReceiver.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class ThemeManagementPageViewModel : ObservableRecipient, INavigationAware
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly AutoDayNightService _autoDayNightService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ThemeTestTimerService _themeTestTimerService;
    private string _scheduledSunriseText = "未创建";
    private string _scheduledSunsetText = "未创建";
    private string _scheduledTimesText = "未创建";
    private string _timeCorrectionText = "未修正";
    private string _lastCorrectionTimeText = "未修正";
    private bool _isInitialized;
    private bool _isListening;

    public ThemeManagementPageViewModel(
        IAppSettingsService appSettingsService,
        AutoDayNightService autoDayNightService,
        IThemeSelectorService themeSelectorService,
        ThemeTestTimerService themeTestTimerService)
    {
        _appSettingsService = appSettingsService;
        _autoDayNightService = autoDayNightService;
        _themeSelectorService = themeSelectorService;
        _themeTestTimerService = themeTestTimerService;

        LoadFromSettings();
    }

    [ObservableProperty]
    public partial bool AutoDayNightEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutoDayNightUseSystemLocation { get; set; }

    [ObservableProperty]
    public partial string AutoDayNightCityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AutoDayNightUseCity { get; set; }

    [ObservableProperty]
    public partial string AutoDayNightSunTimesText { get; set; } = string.Empty;

    public string SystemThemeText => _themeSelectorService.GetSystemTheme() == ElementTheme.Dark ? "当前系统为深色" : "当前系统为浅色";

    public string ScheduledSunriseText
    {
        get => _scheduledSunriseText;
        set => SetProperty(ref _scheduledSunriseText, value);
    }

    public string ScheduledTimesText
    {
        get => _scheduledTimesText;
        set => SetProperty(ref _scheduledTimesText, value);
    }

    public string ScheduledSunsetText
    {
        get => _scheduledSunsetText;
        set => SetProperty(ref _scheduledSunsetText, value);
    }

    public string TimeCorrectionText
    {
        get => _timeCorrectionText;
        set => SetProperty(ref _timeCorrectionText, value);
    }

    public string LastCorrectionTimeText
    {
        get => _lastCorrectionTimeText;
        set => SetProperty(ref _lastCorrectionTimeText, value);
    }

    private void LoadFromSettings()
    {
        AutoDayNightEnabled = _appSettingsService.AutoDayNightEnabled;
        AutoDayNightUseSystemLocation = _appSettingsService.AutoDayNightUseSystemLocation;
        AutoDayNightUseCity = _appSettingsService.AutoDayNightUseCity;
        AutoDayNightCityText = _appSettingsService.AutoDayNightCity;
        LoadNightScheduleFromSettings();
        ThemeTestTime = _themeTestTimerService.ScheduledTime;
        ThemeTestUseDarkMode = _themeTestTimerService.UseDarkMode;
        ThemeTestEnabled = _themeTestTimerService.Enabled;
        ThemeTestStatusText = _themeTestTimerService.StatusText;
        AutoDayNightSunTimesText = "昼夜时间：计算中...";
        UpdateSunTimesTextAsync().Forget(nameof(ThemeManagementPageViewModel));
        RefreshAutoDayNightDashboard();
        _isInitialized = true;
    }

    private void OnAutoDayNightStateChanged(object? sender, EventArgs e)
    {
        if (App.MainWindow?.DispatcherQueue is { } dispatcherQueue && !dispatcherQueue.HasThreadAccess)
        {
            dispatcherQueue.TryEnqueue(RefreshAutoDayNightDashboard);
            return;
        }

        RefreshAutoDayNightDashboard();
    }

    private void OnThemeTestTimerStateChanged(object? sender, EventArgs e)
    {
        if (App.MainWindow?.DispatcherQueue is { } dispatcherQueue && !dispatcherQueue.HasThreadAccess)
        {
            dispatcherQueue.TryEnqueue(RefreshThemeTestTimerState);
            return;
        }

        RefreshThemeTestTimerState();
    }

    private void RefreshThemeTestTimerState()
    {
        ThemeTestEnabled = _themeTestTimerService.Enabled;
        ThemeTestStatusText = _themeTestTimerService.StatusText;
        ThemeTestTime = _themeTestTimerService.ScheduledTime ?? ThemeTestTime;
        ThemeTestUseDarkMode = _themeTestTimerService.UseDarkMode;
        ThemeTestTargetThemeIndex = ThemeTestUseDarkMode ? 1 : 0;
    }

    private void RefreshAutoDayNightDashboard()
    {
        ScheduledSunriseText = FormatDashboardTime(_autoDayNightService.ScheduledSunrise);
        ScheduledSunsetText = FormatDashboardTime(_autoDayNightService.ScheduledSunset);
        ScheduledTimesText = $"{ScheduledSunriseText} / {ScheduledSunsetText}";
        TimeCorrectionText = _autoDayNightService.CorrectionStatus;
        LastCorrectionTimeText = _autoDayNightService.LastCorrectedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未修正";
    }

    private static string FormatDashboardTime(TimeSpan? time)
    {
        return time is null ? "未创建" : time.Value.ToString(@"hh\:mm");
    }

    public Task OnNavigatedToAsync(object parameter)
    {
        StartListening();
        LoadFromSettings();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        StopListening();
        return Task.CompletedTask;
    }

    private void StartListening()
    {
        if (_isListening)
        {
            return;
        }

        _autoDayNightService.StateChanged += OnAutoDayNightStateChanged;
        _themeTestTimerService.StateChanged += OnThemeTestTimerStateChanged;
        _isListening = true;
    }

    private void StopListening()
    {
        if (!_isListening)
        {
            return;
        }

        _autoDayNightService.StateChanged -= OnAutoDayNightStateChanged;
        _themeTestTimerService.StateChanged -= OnThemeTestTimerStateChanged;
        _isListening = false;
    }

    partial void OnAutoDayNightEnabledChanged(bool value)
    {
        if (!_isInitialized) return;
        _appSettingsService.SetAutoDayNightEnabledAsync(value).Forget(nameof(ThemeManagementPageViewModel));
        _ = _autoDayNightService.RefreshAsync();
    }

    partial void OnAutoDayNightUseSystemLocationChanged(bool value)
    {
        if (!_isInitialized) return;
        _appSettingsService.SetAutoDayNightUseSystemLocationAsync(value).Forget(nameof(ThemeManagementPageViewModel));
        _ = _autoDayNightService.RefreshAsync();
        UpdateSunTimesTextAsync().Forget(nameof(ThemeManagementPageViewModel));
    }

    partial void OnAutoDayNightUseCityChanged(bool value)
    {
        if (!_isInitialized) return;
        _appSettingsService.SetAutoDayNightUseCityAsync(value).Forget(nameof(ThemeManagementPageViewModel));
        _ = _autoDayNightService.RefreshAsync();
        UpdateSunTimesTextAsync().Forget(nameof(ThemeManagementPageViewModel));
    }

    [RelayCommand]
    private async Task SaveCityAsync()
    {
        if (!_isInitialized) return;

        var city = AutoDayNightCityText.Trim();
        await _appSettingsService.SetAutoDayNightCityAsync(city);
        AutoDayNightCityText = city;

        await UpdateSunTimesTextAsync();
    }

    [RelayCommand]
    private async Task ClearCityAsync()
    {
        if (!_isInitialized) return;
        _isInitialized = false;
        try
        {
            AutoDayNightCityText = string.Empty;
            await _appSettingsService.SetAutoDayNightCityAsync(string.Empty);
        }
        finally
        {
            _isInitialized = true;
        }

        await UpdateSunTimesTextAsync();
    }

    private async Task UpdateSunTimesTextAsync()
    {
        if (AutoDayNightNightStartTime is not null && AutoDayNightNightEndTime is not null)
        {
            var nightStart = AutoDayNightNightStartTime.Value;
            var nightEnd = AutoDayNightNightEndTime.Value;
            AutoDayNightSunTimesText = $"昼夜时间：自定义 {nightStart:hh\\:mm} - {nightEnd:hh\\:mm}";
            return;
        }

        var location = await _autoDayNightService.ResolveLocationAsync(CancellationToken.None);
        if (location is null)
        {
            AutoDayNightSunTimesText = string.IsNullOrWhiteSpace(AutoDayNightCityText)
                ? "昼夜时间：计算失败，请检查城市"
                : $"昼夜时间：计算失败，请检查城市 {AutoDayNightCityText}";
            return;
        }

        var (sunrise, sunset) = SolarCalculator.CalculateSunTimes(DateTime.Now.Date, location.Value.Latitude, location.Value.Longitude);
        AutoDayNightSunTimesText = $"昼夜时间：日出 {sunrise:hh\\:mm} · 日落 {sunset:hh\\:mm} · 经纬度 {location.Value.Latitude:0.######}, {location.Value.Longitude:0.######}";
    }
}
