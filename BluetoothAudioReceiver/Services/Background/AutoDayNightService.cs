using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Microsoft.UI.Xaml;
using Serilog;
using System.Threading;

namespace BluetoothAudioReceiver.Services;

public sealed class AutoDayNightService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutoDayNightService));

    private readonly IAppSettingsService _appSettingsService;
    private readonly IAutomationTriggerBus _triggerBus;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly INotificationService _notificationService;
    private readonly SolarCalculator _solarCalculator;
    private readonly TaskSchedulerManager _taskSchedulerManager;
    private readonly ThemeManager _themeManager;
    private readonly NightLightStateService _nightLightStateService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Timer? _nightLightPollTimer;
    private readonly object _pollSync = new();
    private volatile bool _observationPaused;
    private bool _started;
    private bool _hasShownMissingTaskNotification;
    private bool? _nightLightActive;

    private bool _taskSchedulerCreated;
    private TimeSpan? _scheduledSunrise;
    private TimeSpan? _scheduledSunset;
    private TimeSpan? _calculatedSunrise;
    private TimeSpan? _calculatedSunset;
    private DateTimeOffset? _lastCorrectedAt;
    private string _recentExecutionSummaryText = "最近3天无执行记录";
    private string _correctionStatus = "未修正";
    private string _taskSchedulerStatusText = "未创建";

    public event EventHandler? StateChanged;

    public bool TaskSchedulerCreated => _taskSchedulerCreated;

    public TimeSpan? ScheduledSunrise => _scheduledSunrise;

    public TimeSpan? ScheduledSunset => _scheduledSunset;

    public TimeSpan? CalculatedSunrise => _calculatedSunrise;

    public TimeSpan? CalculatedSunset => _calculatedSunset;

    public DateTimeOffset? LastCorrectedAt => _lastCorrectedAt;

    public string RecentExecutionSummaryText => _recentExecutionSummaryText;

    public string CorrectionStatus => _correctionStatus;

    public string TaskSchedulerStatusText => _taskSchedulerStatusText;

    public string NightLightStateText => _nightLightActive is null ? "未读取" : _nightLightActive.Value ? "开启" : "关闭";

    public AutoDayNightService(
        IAppSettingsService appSettingsService,
        IAutomationTriggerBus triggerBus,
        IThemeSelectorService themeSelectorService,
        INotificationService notificationService,
        SolarCalculator solarCalculator,
        TaskSchedulerManager taskSchedulerManager,
        ThemeManager themeManager,
        NightLightStateService nightLightStateService)
    {
        _appSettingsService = appSettingsService;
        _triggerBus = triggerBus;
        _themeSelectorService = themeSelectorService;
        _notificationService = notificationService;
        _solarCalculator = solarCalculator;
        _taskSchedulerManager = taskSchedulerManager;
        _themeManager = themeManager;
        _nightLightStateService = nightLightStateService;
        _triggerBus.TriggerPublished += OnTriggerPublished;
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _cts = new CancellationTokenSource();
        await RefreshAsync();
    }

    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        StopNightLightPolling();
        await _taskSchedulerManager.RemoveThemeTasksAsync();
        _hasShownMissingTaskNotification = false;
        SetState(false, null, null, null, null, "未修正", "未创建");
    }

    public async Task RefreshAsync()
    {
        var token = _cts?.Token ?? CancellationToken.None;
        await _gate.WaitAsync(token);
        try
        {
            if (!_appSettingsService.AutoDayNightEnabled)
            {
                StopNightLightPolling();
                await _taskSchedulerManager.RemoveThemeTasksAsync();
                SetState(false, null, null, null, null, "未修正", "未启用");
                return;
            }

            if (_appSettingsService.AutoDayNightUseNightLight)
            {
                if (_observationPaused) return;
                await _taskSchedulerManager.RemoveThemeTasksAsync();
                StartNightLightPolling();
                await ApplyNightLightStateAsync();
                SetState(false, null, null, null, null, _correctionStatus, "不需要计划任务（Night light 状态监听）");
                return;
            }

            StopNightLightPolling();

            if (_appSettingsService.AutoDayNightUseCustomNightSchedule)
            {
                if (!TryGetCustomSchedule(out var lightTime, out var darkTime))
                {
                    await _taskSchedulerManager.RemoveThemeTasksAsync();
                    SetState(false, null, null, null, null, "未修正", "自定义时间未设置或无效");
                    return;
                }

                await ApplyScheduleAsync(lightTime, darkTime, token);
                return;
            }

            var location = await ResolveConfiguredLocationAsync(token);
            if (_appSettingsService.AutoDayNightUseCity && location is null)
            {
                await _taskSchedulerManager.RemoveThemeTasksAsync();
                SetState(false, null, null, null, null, "未修正", "未找到城市");
                return;
            }

            var resolvedSunTimes = await _solarCalculator.ResolveBestSunTimesAsync(location, token);
            if (resolvedSunTimes is null)
            {
                if (_appSettingsService.AutoDayNightNotificationEnabled)
                {
                    _notificationService.Show("自动深浅切换失败", "无法获取日出日落时间", "已尝试历史记录和本地兜底");
                }
                SetState(false, null, null, null, null, "未修正", "日出日落获取失败");
                return;
            }

            await ApplyScheduleAsync(resolvedSunTimes.Sunrise, resolvedSunTimes.Sunset, token);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Auto day/night refresh failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<(double Latitude, double Longitude)?> ResolveLocationAsync(CancellationToken token)
    {
        return ResolveConfiguredLocationAsync(token);
    }

    private async Task<(double Latitude, double Longitude)?> ResolveConfiguredLocationAsync(CancellationToken token)
    {
        if (_appSettingsService.AutoDayNightUseCity)
        {
            return CityCoordinateCatalog.TryResolve(_appSettingsService.AutoDayNightCity, out var city)
                ? (city.Latitude, city.Longitude)
                : null;
        }

        return _appSettingsService.AutoDayNightUseSystemLocation
            ? await _solarCalculator.ResolveSystemLocationAsync(token)
            : null;
    }

    private bool TryGetCustomSchedule(out TimeSpan lightTime, out TimeSpan darkTime)
    {
        lightTime = default;
        darkTime = default;
        if (!TimeSpan.TryParse(_appSettingsService.AutoDayNightNightStartTime, out var nightStart) ||
            !TimeSpan.TryParse(_appSettingsService.AutoDayNightNightEndTime, out var nightEnd) ||
            nightStart == nightEnd)
        {
            return false;
        }

        darkTime = nightStart;
        lightTime = nightEnd;
        return true;
    }

    private async Task ApplyScheduleAsync(TimeSpan lightTime, TimeSpan darkTime, CancellationToken token)
    {
        _calculatedSunrise = lightTime;
        _calculatedSunset = darkTime;
        await CorrectThemeAsync(lightTime, darkTime);
        await _taskSchedulerManager.ScheduleThemeTasksAsync(lightTime, darkTime, token);
        var tasksCreated = _taskSchedulerManager.AreThemeTasksCreated();
        RefreshTaskRuntimeInfo();
        NotifyIfThemeTasksMissing(tasksCreated);
        var schedulerStatus = tasksCreated ? "已创建" : $"创建失败：{GetTaskSchedulerErrorText()}";
        SetState(tasksCreated, lightTime, darkTime, lightTime, darkTime, _correctionStatus, schedulerStatus);
    }

    private async Task CorrectThemeAsync(TimeSpan lightTime, TimeSpan darkTime)
    {
        var now = DateTime.Now.TimeOfDay;
        var shouldUseDark = IsTimeInRange(now, darkTime, lightTime);
        var targetTheme = shouldUseDark ? ElementTheme.Dark : ElementTheme.Light;
        var currentTheme = _themeManager.GetCurrentTheme();

        if (currentTheme == targetTheme)
        {
            _lastCorrectedAt = DateTimeOffset.Now;
            _correctionStatus = "已修正";
            RaiseStateChanged();
            return;
        }

        await _themeSelectorService.SetSystemThemeAsync(targetTheme);
        _lastCorrectedAt = DateTimeOffset.Now;
        _correctionStatus = "已修正";
        RaiseStateChanged();
    }

    private static bool IsTimeInRange(TimeSpan time, TimeSpan start, TimeSpan end)
    {
        return start < end
            ? time >= start && time < end
            : time >= start || time < end;
    }

    private async Task ApplyNightLightStateAsync()
    {
        if (_observationPaused) return;
        var isNightLightActive = _nightLightStateService.IsNightLightActive();
        if (_observationPaused) return;
        var targetTheme = isNightLightActive ? ElementTheme.Dark : ElementTheme.Light;
        var previousState = _nightLightActive;
        _nightLightActive = isNightLightActive;

        if (_themeManager.GetCurrentTheme() != targetTheme)
        {
            await _themeSelectorService.SetSystemThemeAsync(targetTheme);
            _lastCorrectedAt = DateTimeOffset.Now;
            _correctionStatus = "已修正";
        }
        else if (previousState is null || previousState != isNightLightActive)
        {
            _correctionStatus = "已同步";
        }
    }

    private void StartNightLightPolling()
    {
        lock (_pollSync)
        {
            if (_observationPaused || !_started) return;
            _nightLightPollTimer ??= new Timer(
                _ => _ = RefreshNightLightSafelyAsync(), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    internal void SetObservationPaused(bool paused)
    {
        lock (_pollSync)
        {
            if (_observationPaused == paused) return;
            _observationPaused = paused;
            if (paused)
            {
                _nightLightPollTimer?.Dispose();
                _nightLightPollTimer = null;
            }
        }
        if (!paused && _started && _appSettingsService.AutoDayNightEnabled && _appSettingsService.AutoDayNightUseNightLight)
        {
            StartNightLightPolling();
            _ = Task.Run(RefreshNightLightSafelyAsync);
        }
    }

    private async Task RefreshNightLightSafelyAsync()
    {
        if (_observationPaused || !_started) return;
        try
        {
            if (!await _gate.WaitAsync(0, _cts?.Token ?? CancellationToken.None)) return;
            try
            {
                if (_observationPaused || !_started) return;
                if (!_appSettingsService.AutoDayNightEnabled || !_appSettingsService.AutoDayNightUseNightLight)
                {
                    StopNightLightPolling();
                    return;
                }
                // 周期检查只读夜灯，不重复查询/删除系统主题计划任务。
                var previous = (_nightLightActive, _lastCorrectedAt, _correctionStatus);
                await ApplyNightLightStateAsync();
                if (previous != (_nightLightActive, _lastCorrectedAt, _correctionStatus)) RaiseStateChanged();
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { _log.Warning(ex, "Night light observation refresh failed"); }
    }

    private void StopNightLightPolling()
    {
        lock (_pollSync)
        {
            _nightLightPollTimer?.Dispose();
            _nightLightPollTimer = null;
            _nightLightActive = null;
        }
    }

    private void SetState(
        bool taskSchedulerCreated,
        TimeSpan? scheduledSunrise,
        TimeSpan? scheduledSunset,
        TimeSpan? calculatedSunrise,
        TimeSpan? calculatedSunset,
        string correctionStatus,
        string taskSchedulerStatusText)
    {
        _taskSchedulerCreated = taskSchedulerCreated;
        _scheduledSunrise = scheduledSunrise;
        _scheduledSunset = scheduledSunset;
        _calculatedSunrise = calculatedSunrise;
        _calculatedSunset = calculatedSunset;
        _correctionStatus = correctionStatus;
        _taskSchedulerStatusText = taskSchedulerStatusText;
        RaiseStateChanged();
    }

    private void RefreshTaskRuntimeInfo()
    {
        var runtimeInfo = _taskSchedulerManager.GetRuntimeInfo();
        _recentExecutionSummaryText = _taskSchedulerManager.GetRecentExecutionSummary(3);
        if (!runtimeInfo.SunriseTask.Exists || !runtimeInfo.SunsetTask.Exists)
        {
            _recentExecutionSummaryText = $"任务状态异常：{runtimeInfo.SunriseTask.ErrorMessage} {runtimeInfo.SunsetTask.ErrorMessage}".Trim();
        }
    }

    private void NotifyIfThemeTasksMissing(bool tasksCreated)
    {
        if (tasksCreated)
        {
            _hasShownMissingTaskNotification = false;
            return;
        }

        if (_hasShownMissingTaskNotification)
        {
            return;
        }

        _hasShownMissingTaskNotification = true;
        if (_appSettingsService.AutoDayNightNotificationEnabled)
        {
            _notificationService.Show("\uE783", "任务未创建", GetShortTaskSchedulerErrorText(), compactLayout: true);
        }
    }

    private string GetTaskSchedulerErrorText()
    {
        return string.IsNullOrWhiteSpace(_taskSchedulerManager.LastError)
            ? "请打开主题页检查任务计划程序状态"
            : _taskSchedulerManager.LastError;
    }

    private string GetShortTaskSchedulerErrorText()
    {
        if (string.IsNullOrWhiteSpace(_taskSchedulerManager.LastError))
        {
            return "请到主题页检查";
        }

        var errorText = _taskSchedulerManager.LastError
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal)
            .Trim();

        return errorText.Length <= 22 ? errorText : $"{errorText[..22]}...";
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTriggerPublished(object? sender, TriggerEvent triggerEvent)
    {
        if (!_started || !_appSettingsService.AutoDayNightEnabled)
        {
            _log.Debug(
                "Auto day/night ignored trigger. Started={Started}, Enabled={Enabled}, TriggerType={TriggerType}, Reason={Reason}",
                _started,
                _appSettingsService.AutoDayNightEnabled,
                triggerEvent.TriggerType,
                triggerEvent.Payload.TryGetValue("reason", out var ignoredReason) ? ignoredReason : null);
            return;
        }

        if (!string.Equals(triggerEvent.TriggerType, AutomationSupportCatalog.TriggerSystemSignal, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!triggerEvent.Payload.TryGetValue("reason", out var reason) ||
            string.IsNullOrWhiteSpace(reason))
        {
            _log.Debug("Auto day/night ignored system signal because reason is empty");
            return;
        }

        if (reason.StartsWith("session-", StringComparison.OrdinalIgnoreCase) ||
            reason.StartsWith("system-time-", StringComparison.OrdinalIgnoreCase) ||
            reason.StartsWith("system-power-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reason, "network-proxy-changed", StringComparison.OrdinalIgnoreCase) ||
            reason.StartsWith("user-preference-", StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug("Auto day/night accepted system signal. Reason={Reason}", reason);
            _ = RefreshAsync();
        }
    }

    public void Dispose()
    {
        _started = false;
        _observationPaused = true;
        _triggerBus.TriggerPublished -= OnTriggerPublished;
        _cts?.Cancel();
        _cts?.Dispose();
        StopNightLightPolling();
        _gate.Dispose();
    }
}
