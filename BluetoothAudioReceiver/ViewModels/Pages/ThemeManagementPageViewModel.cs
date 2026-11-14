using BluetoothAudioReceiver.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public enum NightScheduleSource
{
    SystemLocation,
    City,
    CustomSchedule,
    NightLight
}

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
    private string _currentDataSourceText = "未选择";
    private string _taskSchedulerStatusText = "未创建";
    private string _currentSystemThemeText = "未知";
    private string _recentTaskExecutionText = "最近3天无执行记录";
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
    public partial bool AutoDayNightUseNightLight { get; set; }

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

    public string CurrentDataSourceText
    {
        get => _currentDataSourceText;
        set => SetProperty(ref _currentDataSourceText, value);
    }

    public string TaskSchedulerStatusText
    {
        get => _taskSchedulerStatusText;
        set => SetProperty(ref _taskSchedulerStatusText, value);
    }

    public string CurrentSystemThemeText
    {
        get => _currentSystemThemeText;
        set => SetProperty(ref _currentSystemThemeText, value);
    }

    public string RecentTaskExecutionText
    {
        get => _recentTaskExecutionText;
        set => SetProperty(ref _recentTaskExecutionText, value);
    }

    private void LoadFromSettings()
    {
        AutoDayNightEnabled = _appSettingsService.AutoDayNightEnabled;
        AutoDayNightUseNightLight = _appSettingsService.AutoDayNightUseNightLight;
        AutoDayNightUseCustomNightSchedule = !AutoDayNightUseNightLight && _appSettingsService.AutoDayNightUseCustomNightSchedule;
        AutoDayNightUseCity = !AutoDayNightUseNightLight && !AutoDayNightUseCustomNightSchedule && _appSettingsService.AutoDayNightUseCity;
        AutoDayNightUseSystemLocation = !AutoDayNightUseNightLight && !AutoDayNightUseCustomNightSchedule && !AutoDayNightUseCity && _appSettingsService.AutoDayNightUseSystemLocation;
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
        TaskSchedulerStatusText = _autoDayNightService.TaskSchedulerStatusText;
        CurrentSystemThemeText = _themeSelectorService.GetActualTheme() == ElementTheme.Dark ? "深色" : "浅色";
        RecentTaskExecutionText = _autoDayNightService.RecentExecutionSummaryText;
        CurrentDataSourceText = AutoDayNightUseSystemLocation
            ? "系统定位"
            : AutoDayNightUseCity
                ? "城市"
                : AutoDayNightUseCustomNightSchedule
                    ? "自定义夜间时间"
                    : AutoDayNightUseNightLight
                        ? $"Windows Night light（{_autoDayNightService.NightLightStateText}）"
                        : "未选择";
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
        PersistAndRefreshAsync(_appSettingsService.SetAutoDayNightEnabledAsync(value)).Forget(nameof(ThemeManagementPageViewModel));
    }

    partial void OnAutoDayNightUseSystemLocationChanged(bool value)
    {
        if (!_isInitialized) return;
        if (value)
        {
            SelectNightScheduleSource(NightScheduleSource.SystemLocation);
            return;
        }
        PersistAndRefreshAsync(_appSettingsService.SetAutoDayNightUseSystemLocationAsync(value), refreshSunTimes: true)
            .Forget(nameof(ThemeManagementPageViewModel));
    }

    partial void OnAutoDayNightUseCityChanged(bool value)
    {
        if (!_isInitialized) return;
        if (value)
        {
            SelectNightScheduleSource(NightScheduleSource.City);
            return;
        }
        PersistAndRefreshAsync(_appSettingsService.SetAutoDayNightUseCityAsync(value), refreshSunTimes: true)
            .Forget(nameof(ThemeManagementPageViewModel));
    }

    partial void OnAutoDayNightUseNightLightChanged(bool value)
    {
        if (!_isInitialized) return;
        if (value)
        {
            SelectNightScheduleSource(NightScheduleSource.NightLight);
            return;
        }
        PersistAndRefreshAsync(_appSettingsService.SetAutoDayNightUseNightLightAsync(false))
            .Forget(nameof(ThemeManagementPageViewModel));
    }

    public void SelectNightScheduleSource(NightScheduleSource source)
    {
        if (!_isInitialized)
        {
            return;
        }

        var wasInitialized = _isInitialized;
        _isInitialized = false;
        try
        {
            AutoDayNightUseSystemLocation = source == NightScheduleSource.SystemLocation;
            AutoDayNightUseCity = source == NightScheduleSource.City;
            AutoDayNightUseCustomNightSchedule = source == NightScheduleSource.CustomSchedule;
            AutoDayNightUseNightLight = source == NightScheduleSource.NightLight;
        }
        finally
        {
            _isInitialized = wasInitialized;
        }

        SaveNightScheduleSourceAsync().Forget(nameof(ThemeManagementPageViewModel));
    }

    private async Task SaveNightScheduleSourceAsync()
    {
        await _appSettingsService.SetAutoDayNightUseSystemLocationAsync(AutoDayNightUseSystemLocation);
        await _appSettingsService.SetAutoDayNightUseCityAsync(AutoDayNightUseCity);
        await _appSettingsService.SetAutoDayNightUseCustomNightScheduleAsync(AutoDayNightUseCustomNightSchedule);
        await _appSettingsService.SetAutoDayNightUseNightLightAsync(AutoDayNightUseNightLight);
        await _autoDayNightService.RefreshAsync();
        await UpdateSunTimesTextAsync();
    }

    private async Task PersistAndRefreshAsync(Task persist, bool refreshSunTimes = false)
    {
        await persist;
        await _autoDayNightService.RefreshAsync();
        if (refreshSunTimes)
        {
            await UpdateSunTimesTextAsync();
        }
    }

    [RelayCommand]
    private async Task SaveCityAsync()
    {
        if (!_isInitialized) return;

        var city = AutoDayNightCityText.Trim();
        if (!CityCoordinateCatalog.TryResolve(city, out _))
        {
            GetNotificationService().Show("城市不可用", "未找到该城市", "请使用内置主要城市名称，例如上海或北京市。");
            return;
        }

        await _appSettingsService.SetAutoDayNightCityAsync(city);
        AutoDayNightCityText = city;

        await UpdateSunTimesTextAsync();
        await _autoDayNightService.RefreshAsync();
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
        await _autoDayNightService.RefreshAsync();
    }

    private async Task UpdateSunTimesTextAsync()
    {
        if (AutoDayNightUseCustomNightSchedule && AutoDayNightNightStartTime is not null && AutoDayNightNightEndTime is not null)
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
