using BluetoothAudioReceiver.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using System.Text;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class ThemeManagementPageViewModel : ObservableRecipient, INavigationAware
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(ThemeManagementPageViewModel));
    private readonly IAppSettingsService _appSettingsService;
    private readonly AutoDayNightService _autoDayNightService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ThemeTestTimerService _themeTestTimerService;
    private string _scheduledSunriseText = "未创建";
    private string _scheduledSunsetText = "未创建";
    private string _scheduledTimesText = "未创建";
    private string _timeCorrectionText = "未修正";
    private string _lastCorrectionTimeText = "未修正";
    private readonly WallpaperDimService _wallpaperDimService;
    private bool _isInitialized;
    private bool _isListening;

    public ThemeManagementPageViewModel(
        IAppSettingsService appSettingsService,
        AutoDayNightService autoDayNightService,
        IThemeSelectorService themeSelectorService,
        ThemeTestTimerService themeTestTimerService,
        WallpaperDimService wallpaperDimService)
    {
        _appSettingsService = appSettingsService;
        _autoDayNightService = autoDayNightService;
        _themeSelectorService = themeSelectorService;
        _themeTestTimerService = themeTestTimerService;
        _wallpaperDimService = wallpaperDimService;
        _themeSelectorService.ThemeChanged += OnThemeChanged;

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

    [ObservableProperty]
    public partial bool WallpaperDimEnabled { get; set; }

    [ObservableProperty]
    public partial bool WallpaperDimFollowTheme { get; set; }

    [ObservableProperty]
    public partial bool WallpaperDimRestoreOnExit { get; set; }

    [ObservableProperty]
    public partial string WallpaperDimOpacityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource? WallpaperPreviewSource { get; set; }

    [ObservableProperty]
    public partial double WallpaperDimOpacityValue { get; set; }

    [ObservableProperty]
    public partial bool IsWallpaperPreviewLoading { get; set; }

    [ObservableProperty]
    public partial string WallpaperPreviewStatusText { get; set; } = "当前壁纸加载中...";

    public IReadOnlyList<string> WallpaperDimOpacityOptions { get; } =
        ["10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%"];

    [ObservableProperty]
    public partial int SelectedWallpaperDimOpacityIndex { get; set; }

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
        WallpaperDimEnabled = _appSettingsService.WallpaperDimEnabled;
        WallpaperDimFollowTheme = _appSettingsService.WallpaperDimFollowTheme;
        WallpaperDimRestoreOnExit = _appSettingsService.WallpaperDimRestoreOnExit;
        WallpaperDimOpacityText = _appSettingsService.WallpaperDimOpacity.ToString("0.##");
        WallpaperDimOpacityValue = _appSettingsService.WallpaperDimOpacity;
        SelectedWallpaperDimOpacityIndex = ResolveWallpaperOpacityIndex(_appSettingsService.WallpaperDimOpacity);
        _ = RefreshWallpaperPreviewAsync();
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

    partial void OnWallpaperDimEnabledChanged(bool value)
    {
        if (!_isInitialized) return;
        _appSettingsService.SetWallpaperDimEnabledAsync(value).Forget(nameof(ThemeManagementPageViewModel));
        _ = SyncWallpaperProcessingToThemeAsync(_themeSelectorService.GetActualTheme());
    }

    partial void OnWallpaperDimFollowThemeChanged(bool value)
    {
        if (!_isInitialized) return;
        _appSettingsService.SetWallpaperDimFollowThemeAsync(value).Forget(nameof(ThemeManagementPageViewModel));
        _ = SyncWallpaperProcessingToThemeAsync(_themeSelectorService.GetActualTheme());
    }

    partial void OnWallpaperDimRestoreOnExitChanged(bool value)
    {
        if (!_isInitialized) return;
        _appSettingsService.SetWallpaperDimRestoreOnExitAsync(value).Forget(nameof(ThemeManagementPageViewModel));
    }

    partial void OnWallpaperDimOpacityTextChanged(string value)
    {
        if (_isInitialized && double.TryParse(value, out var opacity))
        {
            _appSettingsService.SetWallpaperDimOpacityAsync(opacity).Forget(nameof(ThemeManagementPageViewModel));
            WallpaperDimOpacityValue = opacity;
            SelectedWallpaperDimOpacityIndex = ResolveWallpaperOpacityIndex(opacity);
            _ = SyncWallpaperProcessingToThemeAsync(_themeSelectorService.GetActualTheme());
        }
    }

    partial void OnSelectedWallpaperDimOpacityIndexChanged(int value)
    {
        if (!_isInitialized || value < 0 || value >= WallpaperDimOpacityOptions.Count)
        {
            return;
        }

        var opacity = ResolveWallpaperOpacityFromIndex(value);
        WallpaperDimOpacityText = opacity.ToString("0.##");
        WallpaperDimOpacityValue = opacity;
        _appSettingsService.SetWallpaperDimOpacityAsync(opacity).Forget(nameof(ThemeManagementPageViewModel));
        _ = SyncWallpaperProcessingToThemeAsync(_themeSelectorService.GetActualTheme());
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

    [RelayCommand]
    private async Task RefreshWallpaperPreviewAsync()
    {
        if (TryEnqueueOnUiThread(() => RefreshWallpaperPreviewAsync(), out var queuedTask))
        {
            await queuedTask;
            return;
        }

        IsWallpaperPreviewLoading = true;
        WallpaperPreviewStatusText = "当前壁纸加载中...";

        try
        {
            var path = await _wallpaperDimService.GetPreviewWallpaperPathAsync();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                WallpaperPreviewSource = null;
                WallpaperPreviewStatusText = "未获取到壁纸";
                return;
            }

            WallpaperPreviewSource = CreateBitmapImage(path);
            WallpaperPreviewStatusText = ShouldEnableWallpaperOverlay(_themeSelectorService.GetActualTheme())
                ? "当前壁纸，遮罩已启用"
                : "当前壁纸，遮罩未启用";
        }
        catch
        {
            WallpaperPreviewSource = null;
            WallpaperPreviewStatusText = "未获取到壁纸";
        }
        finally
        {
            IsWallpaperPreviewLoading = false;
        }
    }

    [RelayCommand]
    public async Task ApplyWallpaperPreviewAsync()
    {
        if (TryEnqueueOnUiThread(() => ApplyWallpaperPreviewAsync(), out var queuedTask))
        {
            await queuedTask;
            return;
        }

        if (!_isInitialized)
        {
            AppendWallpaperDebugLog("ApplyWallpaperPreview skipped because view model is not initialized.");
            return;
        }

        try
        {
            AppendWallpaperDebugLog($"ApplyWallpaperPreview started. opacity={WallpaperDimOpacityValue:0.##}");
            WallpaperPreviewStatusText = "正在应用遮罩...";
            var wallpaperChanged = await _wallpaperDimService.ApplyDimmedAsync(WallpaperDimOpacityValue);
            await RefreshWallpaperPreviewAsync();
            WallpaperPreviewStatusText = "遮罩已启用";
            AppendWallpaperDebugLog(wallpaperChanged
                ? "ApplyWallpaperPreview completed with dimmed wallpaper switch."
                : "ApplyWallpaperPreview skipped because current wallpaper name is already dimmed.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Wallpaper apply failed.");
            WriteWallpaperDebugLog(ex, string.Empty);
            WallpaperPreviewStatusText = "遮罩启用失败";
        }
    }

    [RelayCommand]
    public async Task RestoreWallpaperAsync()
    {
        if (TryEnqueueOnUiThread(() => RestoreWallpaperAsync(), out var queuedTask))
        {
            await queuedTask;
            return;
        }

        try
        {
            AppendWallpaperDebugLog("RestoreWallpaper started.");
            await _wallpaperDimService.RestoreAsync();
            await RefreshWallpaperPreviewAsync();
            WallpaperPreviewStatusText = "遮罩已关闭";
            AppendWallpaperDebugLog("RestoreWallpaper completed.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to restore wallpaper");
            AppendWallpaperDebugLog("RestoreWallpaper threw an exception.");
            WallpaperPreviewStatusText = "遮罩关闭失败";
        }
    }

    private static BitmapImage CreateBitmapImage(string path)
    {
        var image = new BitmapImage();
        image.UriSource = new Uri(path, UriKind.Absolute);
        return image;
    }

    private static int ResolveWallpaperOpacityIndex(double opacity)
    {
        var normalized = Math.Clamp(opacity, 0.1, 0.8);
        var index = (int)Math.Round((normalized - 0.1) / 0.1);
        return Math.Clamp(index, 0, 7);
    }

    private static double ResolveWallpaperOpacityFromIndex(int index)
    {
        return 0.1 + (Math.Clamp(index, 0, 7) * 0.1);
    }

    public async Task SyncWallpaperProcessingToThemeAsync(ElementTheme theme)
    {
        if (TryEnqueueOnUiThread(() => SyncWallpaperProcessingToThemeAsync(theme), out var queuedTask))
        {
            await queuedTask;
            return;
        }

        if (!_isInitialized)
        {
            AppendWallpaperDebugLog(
                $"SyncWallpaperProcessingToTheme skipped. initialized={_isInitialized}, enabled={WallpaperDimEnabled}, followTheme={WallpaperDimFollowTheme}, theme={theme}");
            return;
        }

        if (!ShouldEnableWallpaperOverlay(theme))
        {
            AppendWallpaperDebugLog("SyncWallpaperProcessingToTheme chose RestoreWallpaperAsync.");
            await RestoreWallpaperAsync();
            return;
        }

        AppendWallpaperDebugLog(
            $"SyncWallpaperProcessingToTheme started. initialized={_isInitialized}, enabled={WallpaperDimEnabled}, followTheme={WallpaperDimFollowTheme}, theme={theme}");
        await ApplyWallpaperPreviewAsync();
    }

    private bool ShouldEnableWallpaperOverlay(ElementTheme theme)
    {
        if (!WallpaperDimEnabled)
        {
            return false;
        }

        if (!WallpaperDimFollowTheme)
        {
            return true;
        }

        return theme == ElementTheme.Dark;
    }

    private void OnThemeChanged(object? sender, ElementTheme theme)
    {
        if (App.MainWindow?.DispatcherQueue is { } dispatcherQueue && !dispatcherQueue.HasThreadAccess)
        {
            dispatcherQueue.TryEnqueue(async () => await HandleThemeChangedAsync(theme));
            return;
        }

        _ = HandleThemeChangedAsync(theme);
    }

    private async Task HandleThemeChangedAsync(ElementTheme theme)
    {
        try
        {
            await SyncWallpaperProcessingToThemeAsync(theme);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to sync wallpaper processing after theme change");
        }
    }

    private static bool TryEnqueueOnUiThread(Func<Task> action, out Task queuedTask)
    {
        queuedTask = Task.CompletedTask;
        var dispatcherQueue = App.MainWindow?.DispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            return false;
        }

        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completionSource.SetResult();
                }
                catch (Exception ex)
                {
                    completionSource.SetException(ex);
                }
            }))
        {
            completionSource.SetException(new InvalidOperationException("无法切回 UI 线程更新壁纸状态。"));
        }

        queuedTask = completionSource.Task;
        return true;
    }


    private static void WriteWallpaperDebugLog(Exception ex, string sourcePath)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BluetoothAudioReceiver");
            Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, "wallpaper-debug.log");
            var message = new StringBuilder()
                .AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"Source: {sourcePath}")
                .AppendLine($"Message: {ex.Message}")
                .AppendLine($"Stack: {ex.StackTrace}")
                .AppendLine(new string('-', 60))
                .ToString();
            File.AppendAllText(logPath, message, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void AppendWallpaperDebugLog(string message)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BluetoothAudioReceiver");
            Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, "wallpaper-debug.log");
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
