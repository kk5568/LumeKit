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
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _cts;
    private bool _started;
    private bool _hasShownMissingTaskNotification;

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

    public AutoDayNightService(
        IAppSettingsService appSettingsService,
        IAutomationTriggerBus triggerBus,
        IThemeSelectorService themeSelectorService,
        INotificationService notificationService,
        SolarCalculator solarCalculator,
        TaskSchedulerManager taskSchedulerManager,
        ThemeManager themeManager)
    {
        _appSettingsService = appSettingsService;
        _triggerBus = triggerBus;
        _themeSelectorService = themeSelectorService;
        _notificationService = notificationService;
        _solarCalculator = solarCalculator;
        _taskSchedulerManager = taskSchedulerManager;
        _themeManager = themeManager;
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
                await _taskSchedulerManager.RemoveThemeTasksAsync();
                SetState(false, null, null, null, null, "未修正", "未启用");
                return;
            }

            if (!_appSettingsService.AutoDayNightUseSystemLocation)
            {
                _log.Information("Auto day/night skipped: system location disabled");
                await _taskSchedulerManager.RemoveThemeTasksAsync();
                SetState(false, null, null, null, null, "未修正", "系统定位未开启");
                return;
            }

            var location = await _solarCalculator.ResolveSystemLocationAsync(token);
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

            _calculatedSunrise = resolvedSunTimes.Sunrise;
            _calculatedSunset = resolvedSunTimes.Sunset;
            await CorrectThemeAsync(resolvedSunTimes.Sunrise, resolvedSunTimes.Sunset);
            await _taskSchedulerManager.ScheduleThemeTasksAsync(resolvedSunTimes.Sunrise, resolvedSunTimes.Sunset, token);
            var tasksCreated = _taskSchedulerManager.AreThemeTasksCreated();
            RefreshTaskRuntimeInfo();
            NotifyIfThemeTasksMissing(tasksCreated);
            var schedulerStatus = tasksCreated ? "已创建" : $"创建失败：{GetTaskSchedulerErrorText()}";
            SetState(tasksCreated, resolvedSunTimes.Sunrise, resolvedSunTimes.Sunset, resolvedSunTimes.Sunrise, resolvedSunTimes.Sunset, _correctionStatus, schedulerStatus);
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
        return _solarCalculator.ResolveSystemLocationAsync(token);
    }

    private async Task CorrectThemeAsync(TimeSpan sunrise, TimeSpan sunset)
    {
        var now = DateTime.Now.TimeOfDay;
        var shouldUseLight = now >= sunrise && now < sunset;
        var targetTheme = shouldUseLight ? ElementTheme.Light : ElementTheme.Dark;
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
        _triggerBus.TriggerPublished -= OnTriggerPublished;
        _cts?.Cancel();
        _cts?.Dispose();
        _gate.Dispose();
    }
}
