using System.Management;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Serilog;
using BluetoothAudioReceiver.Models;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Media.Devices;
using Windows.System.Power;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationObservationService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationObservationService));

    private readonly IAutomationSystemContextHub _systemContextHub;
    private readonly IAutomationTriggerBus _triggerBus;
    private readonly IAudioPlaybackConnectionService _audioPlaybackConnectionService;
    private readonly BluetoothRadioMonitorService _bluetoothRadioMonitorService;
    private readonly IDisplayProvider _displayProvider;
    private readonly AutoDayNightService _autoDayNightService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly AutomationGameModeDecisionService _gameModeDecisionService;
    private readonly DynamicPerformanceService _dynamicPerformanceService;
    private readonly PerformanceObservationSampler _performanceSampler;
    private readonly AutomationObservationGetterSelector _getterSelector;
    private readonly AutomationObservationCollector _observationCollector;
    private readonly AutomationObservationStateProcessor _observationProcessor;
    private readonly AutomationObservationEventDispatcher _observationDispatcher;
    private readonly AutomationObservationRequestPlanner _requestPlanner;
    private readonly AutomationObservationSubscriptionManager _subscriptionManager;
    private readonly AutomationObservationPublisher _observationPublisher;
    private readonly AutomationSunObservationSource _sunObservationSource;
    private readonly AutomationProxyObservationSource _proxyObservationSource;
    private readonly AutomationSessionObservationSource _sessionObservationSource;
    private readonly ObservationDemandRegistry _demandRegistry;
    private readonly BrightnessSettings _brightnessSettings;
    private readonly ObservationScenePolicy _scenePolicy = new();
    private readonly object _sceneSync = new();
    private readonly SemaphoreSlim _sceneCheckSignal = new(0, 1);
    private CancellationTokenSource _planCts = new();
    private Task? _sceneCheckTask;
    private long _planVersion;
    private string? _planSignature;
    private bool _planRefreshPending;
    private volatile bool _isSuspended;
    private Views.Windows.MainWindow? _mainWindow;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _sunLoopCts;
    private Task? _sunLoopTask;
    private Task? _proxyWatchTask;
    private CancellationTokenSource? _proxyWatchCts;
    private bool _proxyWatchRunning;
    private bool _processWatchersRunning;
    private bool _sunLoopRunning;
    private readonly object _sourceSyncLock = new();
    private CancellationTokenSource? _sourceSyncDebounceCts;
    private volatile bool _isLocked;
    private AutomationDynamicContext _lastPublishedContext = new();
    private string? _lastObservedForegroundWindowTitle;
    private ManagementEventWatcher? _processStartedWatcher;
    private ManagementEventWatcher? _processStoppedWatcher;
    private bool _started;
    private volatile bool _disposed;
    private static readonly TimeSpan ObservationCoalesceDelay = TimeSpan.FromMilliseconds(200);
    private readonly object _refreshSync = new();
    private readonly List<string> _pendingReasons = [];
    private Guid? _pendingCorrelationId;
    private bool _refreshPending;
    private CancellationTokenSource? _refreshBatchCts;

    public AutomationObservationService(
        IAutomationSystemContextHub systemContextHub,
        IAutomationTriggerBus triggerBus,
        IAudioPlaybackConnectionService audioPlaybackConnectionService,
        BluetoothRadioMonitorService bluetoothRadioMonitorService,
        IBackgroundDispatchBus backgroundDispatchBus,
        IDisplayProvider displayProvider,
        AutoDayNightService autoDayNightService,
        IAppSettingsService appSettingsService,
        IAutomationPipelineRepository pipelineRepository,
        AutomationGameModeDecisionService gameModeDecisionService,
        DynamicPerformanceService dynamicPerformanceService,
        PerformanceObservationSampler performanceSampler,
        AutomationObservationGetterSelector getterSelector,
        ObservationDemandRegistry demandRegistry,
        BrightnessSettings brightnessSettings)
    {
        _systemContextHub = systemContextHub;
        _triggerBus = triggerBus;
        _audioPlaybackConnectionService = audioPlaybackConnectionService;
        _bluetoothRadioMonitorService = bluetoothRadioMonitorService;
        _displayProvider = displayProvider;
        _autoDayNightService = autoDayNightService;
        _appSettingsService = appSettingsService;
        _pipelineRepository = pipelineRepository;
        _demandRegistry = demandRegistry;
        _brightnessSettings = brightnessSettings;
        _gameModeDecisionService = gameModeDecisionService;
        _dynamicPerformanceService = dynamicPerformanceService;
        _performanceSampler = performanceSampler;
        _getterSelector = getterSelector;
        _observationPublisher = new AutomationObservationPublisher(
            _triggerBus,
            backgroundDispatchBus,
            _systemContextHub);
        _observationCollector = new AutomationObservationCollector(
            _getterSelector,
            IsRequestCurrent);
        _observationProcessor = new AutomationObservationStateProcessor(
            _systemContextHub,
            ForegroundWindowProbe.GetForegroundProcessName,
            ForegroundWindowProbe.GetForegroundProcessId,
            ForegroundWindowProbe.IsForegroundFullscreen);
        _observationDispatcher = new AutomationObservationEventDispatcher(DistributeInformation);
        _requestPlanner = new AutomationObservationRequestPlanner(
            demandRegistry,
            _scenePolicy,
            () => ObservationDemandCatalog.GetSunScheduleDemand(_pipelineRepository));
        _subscriptionManager = new AutomationObservationSubscriptionManager(
            _audioPlaybackConnectionService,
            _bluetoothRadioMonitorService,
            _autoDayNightService,
            OnAudioPlaybackDeviceConnected,
            OnAudioPlaybackDeviceDisconnected,
            OnBluetoothRadioStateChanged,
            OnAutoDayNightStateChanged,
            OnPowerSnapshotChanged,
            OnDefaultAudioRenderDeviceChanged,
            OnNetworkStatusChanged,
            OnSystemPowerModeChanged,
            OnSystemTimeChanged,
            OnUserPreferenceChanged,
            OnSystemForegroundWindowChanged);
        _sunObservationSource = new AutomationSunObservationSource(
            _triggerBus,
            () => _isLocked || _isSuspended,
            token => PublishCurrentStateAsync(token, CollectInformationRequest("sun-loop")));
        _proxyObservationSource = new AutomationProxyObservationSource(
            () => ObservationPaused,
            (reason, correlationId) => RequestObservationRefreshAsync(reason, correlationId));
        _sessionObservationSource = new AutomationSessionObservationSource(
            _observationPublisher,
            () => _isLocked,
            value =>
            {
                _isLocked = value;
                _performanceSampler.SetPaused(value || _isSuspended);
                UpdateCollectionPlan(refreshPower: !value);
                SyncObservationSources(requestRefreshWhenStarted: false);
                SignalSceneCheck();
            },
            (reason, correlationId) => RequestObservationRefreshAsync(reason, correlationId));
        _dynamicPerformanceService.StageChanged += OnDynamicPerformanceStageChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        UpdateCollectionPlan();

        // 先按各模块登记的需求把事件源和后台循环挂上，避免启动阶段漏事件。
        // 启动时的全量采集紧接着做，所以这里不需要再补一次刷新。
        SyncObservationSources(requestRefreshWhenStarted: false);

        await PublishCurrentStateAsync(_cts.Token, CollectInformationRequest("startup"));

        _pipelineRepository.PipelinesChanged += OnPipelinesChanged;
        if (_appSettingsService is AppSettingsService settings) settings.ObservationDemandChanged += OnPipelinesChanged;
        _brightnessSettings.PropertyChanged += OnBrightnessSettingsChanged;

        _performanceSampler.Start(_observationProcessor.CapturePerformanceSample);
        _sceneCheckTask = RunSceneChecksAsync(_cts.Token);
        _log.Information("Automation observation service started with event-driven listeners");
    }

    /// <summary>
    /// 把事件订阅和后台循环调整到「当前所有模块登记的并集」。
    /// 规则、模块开关变了就再调一次，只会对差异做挂载和卸载。
    /// </summary>
    private void SyncObservationSources(bool requestRefreshWhenStarted = true)
    {
        IReadOnlyCollection<ObservationSourceKind> sources;
        try
        {
            sources = _demandRegistry.GetSourceUnion();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to collect observation source demand union");
            return;
        }

        lock (_sourceSyncLock)
        {
            if (_disposed) return;
            _subscriptionManager.Apply(sources);
            // 锁屏/睡眠只保留恢复所依赖的系统通知；进程、太阳和代理观察等恢复后再挂回。
            var active = !_scenePolicy.Current.Pause;
            ApplyProcessTraceDemand(sources.Contains(ObservationSourceKind.ProcessLifecycle) && active, requestRefreshWhenStarted);
            ApplySunScheduleDemand(sources.Contains(ObservationSourceKind.SunScheduleLoop) && !_scenePolicy.Current.Pause, requestRefreshWhenStarted);
            ApplyProxyWatchDemand(sources.Contains(ObservationSourceKind.ProxyRegistryWatch) && active);
        }
    }

    private void ApplyProxyWatchDemand(bool needed)
    {
        if (needed == _proxyWatchRunning)
        {
            return;
        }

        if (needed)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            _proxyWatchCts = cts;
            _proxyWatchRunning = true;
            _proxyWatchTask = _proxyObservationSource.RunProxyRegistryWatchAsync(cts.Token);
            _log.Information("Automation proxy registry watch started on demand");
            return;
        }

        _proxyWatchRunning = false;

        var watchTask = _proxyWatchTask;
        var watchCts = _proxyWatchCts;
        _proxyWatchTask = null;
        _proxyWatchCts = null;

        watchCts?.Cancel();
        if (watchCts is not null)
        {
            if (watchTask is null)
            {
                watchCts.Dispose();
            }
            else
            {
                _ = watchTask.ContinueWith(
                    _ => watchCts.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        _log.Information("Automation proxy registry watch stopped: no module needs it");
    }

    private void ApplyProcessTraceDemand(bool needed, bool requestRefreshWhenStarted)
    {
        if (needed == _processWatchersRunning)
        {
            return;
        }

        if (needed)
        {
            StartProcessLifecycleWatchers();
            _processWatchersRunning = _processStartedWatcher is not null;

            if (_processWatchersRunning)
            {
                _log.Information("Automation process lifecycle watchers started on demand");

                if (requestRefreshWhenStarted)
                {
                    // 关着的这段时间可能漏了事件，补一次全量采集。
                    RequestRefreshInBackground("observer-resumed");
                }
            }

            return;
        }

        StopProcessLifecycleWatchers();
        _processWatchersRunning = false;
        _log.Information("Automation process lifecycle watchers stopped: no enabled pipeline needs them");
    }

    private void ApplySunScheduleDemand(bool needed, bool requestRefreshWhenStarted)
    {
        if (needed == _sunLoopRunning)
        {
            return;
        }

        if (needed)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            _sunLoopCts = cts;
            _sunLoopRunning = true;
            var previous = _sunLoopTask is { IsCompleted: false } ? _sunLoopTask : null;
            _sunLoopTask = RunSunLoopAfterAsync(previous, cts.Token);
            _log.Information("Automation sun schedule loop started on demand");

            if (requestRefreshWhenStarted)
            {
                RequestRefreshInBackground("observer-resumed");
            }

            return;
        }

        _sunLoopRunning = false;

        var loopTask = _sunLoopTask;
        var loopCts = _sunLoopCts;
        _sunLoopCts = null;

        loopCts?.Cancel();

        if (loopCts is not null)
        {
            if (loopTask is null)
            {
                loopCts.Dispose();
            }
            else
            {
                _ = loopTask.ContinueWith(
                    _ => loopCts.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        _log.Information("Automation sun schedule loop stopped: no enabled pipeline needs it");
    }

    private async Task RunSunLoopAfterAsync(Task? previous, CancellationToken token)
    {
        try
        {
            if (previous is not null) await previous;
            token.ThrowIfCancellationRequested();
            await _sunObservationSource.RunSunLoopAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void RequestRefreshInBackground(string reason)
        => _ = RequestObservationRefreshAsync(reason, Guid.NewGuid());

    private bool ObservationPaused => _isLocked || _isSuspended;

    private bool IsRequestCurrent(ObservationRequest request)
        => !_disposed && !ObservationPaused && request.PlanVersion == Interlocked.Read(ref _planVersion);

    private bool HasPendingPlanRefresh
    {
        get { lock (_sceneSync) return _planRefreshPending; }
    }

    private bool UpdateCollectionPlan(bool refreshPower = false)
    {
        lock (_sceneSync)
        {
            if (_disposed) return false;
            _scenePolicy.SetLocked(_isLocked);
            _scenePolicy.SetSuspended(_isSuspended);
            _scenePolicy.Refresh(refreshPower || _planSignature is null);
            var scene = _scenePolicy.Current;
            var signature = $"{scene}|" + string.Join("|", Enum.GetValues<ObservationDemandPurpose>()
                .Select(purpose => $"{purpose}:{string.Join(",", _demandRegistry.GetUnion(purpose).OrderBy(item => item))}"))
                + "|" + string.Join(",", _demandRegistry.GetSourceUnion().OrderBy(item => item));
            if (signature == _planSignature) return false;
            _planSignature = signature;
            _planRefreshPending = true;
            Interlocked.Increment(ref _planVersion);
            CancelPendingRefresh();
            var previous = _planCts;
            _planCts = new CancellationTokenSource();
            previous.Cancel();
            previous.Dispose();
            _performanceSampler.SetPaused(scene.Pause);
            _autoDayNightService.SetObservationPaused(scene.Pause);
            _log.Information("Observation plan updated. Version={Version}, Scene={Scene}, Paused={Paused}, DeferredInterval={DeferredInterval}",
                _planVersion, scene, scene.Pause, scene.Reduce ? ObservationScenePolicy.ReducedInterval : ObservationScenePolicy.SceneCheckInterval);
            return true;
        }
    }

    private void SignalSceneCheck()
    {
        try { _sceneCheckSignal.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task RunSceneChecksAsync(CancellationToken token)
    {
        var lastCacheRefresh = Environment.TickCount64;
        while (!token.IsCancellationRequested)
        {
            try
            {
                // 锁屏/睡眠只等待事件唤醒，不再每 30 秒醒来读取状态。
                if (ObservationPaused)
                {
                    await _sceneCheckSignal.WaitAsync(token);
                    continue;
                }
                var changed = UpdateCollectionPlan();
                if (changed || HasPendingPlanRefresh)
                {
                    SyncObservationSources(requestRefreshWhenStarted: false);
                    await RequestObservationRefreshAsync("scene-changed");
                }

                var interval = _scenePolicy.Current.Reduce ? ObservationScenePolicy.ReducedInterval : ObservationScenePolicy.SceneCheckInterval;
                if (_demandRegistry.GetUnion(ObservationDemandPurpose.BackgroundCache).Count > 0 &&
                    Environment.TickCount64 - lastCacheRefresh >= interval.TotalMilliseconds)
                {
                    lastCacheRefresh = Environment.TickCount64;
                    await RequestObservationRefreshAsync("cache-refresh");
                }
                await _sceneCheckSignal.WaitAsync(ObservationScenePolicy.SceneCheckInterval, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.Warning(ex, "Observation scene check failed");
                try { await Task.Delay(ObservationScenePolicy.SceneCheckInterval, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void OnPipelinesChanged(object? sender, EventArgs e)
    {
        if (_disposed || !_started) return;
        CancellationTokenSource debounceCts;
        lock (_sourceSyncLock)
        {
            if (_disposed || !_started) return;
            _sourceSyncDebounceCts?.Cancel();
            _sourceSyncDebounceCts?.Dispose();
            debounceCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            _sourceSyncDebounceCts = debounceCts;
        }

        _ = RunDebouncedSourceSyncAsync(debounceCts.Token);
    }

    private void OnBrightnessSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(BrightnessSettings.AutoEnabled), StringComparison.Ordinal))
            OnPipelinesChanged(sender, EventArgs.Empty);
    }

    private async Task RunDebouncedSourceSyncAsync(CancellationToken token)
    {
        try
        {
            // 连续增删规则时只重算最后一次。
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var changed = UpdateCollectionPlan();
            SyncObservationSources();
            if (changed) RequestRefreshInBackground("demand-changed");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to sync demand-driven observation sources");
        }
    }

    public void InitializeForWindow(Views.Windows.MainWindow window)
    {
        if (_mainWindow == window) return;
        if (_mainWindow is not null) _mainWindow.Activated -= OnMainWindowActivated;
        _mainWindow = window;
        _scenePolicy.SetMainWindow(window.WindowHandle);
        _sessionObservationSource.InitializeForWindow(window);

        // 用户改完设置回到主窗口时重算一次需求：模块开关（比如代理通知）变了，
        // 对应的事件源该挂就挂、该摘就摘。防抖后只算最后那次。
        window.Activated += OnMainWindowActivated;
        if (_started)
        {
            UpdateCollectionPlan();
            SignalSceneCheck();
        }
    }

    private void OnMainWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_started)
        {
            return;
        }

        OnPipelinesChanged(this, EventArgs.Empty);
    }

    private async void OnAudioPlaybackDeviceConnected(object? sender, DeviceInformation device)
    {
        await RequestObservationRefreshAsync("audio-playback-connected", Guid.NewGuid());
    }

    private async void OnAudioPlaybackDeviceDisconnected(object? sender, DeviceInformation device)
    {
        await RequestObservationRefreshAsync("audio-playback-disconnected", Guid.NewGuid());
    }

    private async void OnBluetoothRadioStateChanged(RadioState state)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        var correlationId = Guid.NewGuid();
        var reason = $"bluetooth-radio-{state}";
        _observationPublisher.PublishSystemSignal(reason, correlationId);
        await RequestObservationRefreshAsync(reason, correlationId);
    }

    private async void OnAutoDayNightStateChanged(object? sender, EventArgs e)
    {
        await RequestObservationRefreshAsync("sun-time-updated", Guid.NewGuid());
    }

    private async void OnPowerSnapshotChanged(object? sender, object e)
    {
        if (_disposed) return;
        UpdateCollectionPlan(refreshPower: true);
        _performanceSampler.RequestImmediateSample();
        await RequestObservationRefreshAsync("power-changed", Guid.NewGuid());
    }

    private void OnDynamicPerformanceStageChanged(object? sender, DynamicPerformanceChangedEventArgs e)
    {
        if (ObservationPaused || _disposed) return;
        var correlationId = Guid.NewGuid();
        _triggerBus.Publish(new TriggerEvent(
            AutomationTriggerCatalog.DynamicPerformanceChanged,
            e.OccurredAt,
            new Dictionary<string, string?>
            {
                ["state"] = "changed",
                ["stage"] = e.Stage,
                ["mode"] = e.Mode.ToString(),
                ["powerSource"] = e.IsOnBattery ? "battery" : "ac",
                ["cpuUsage"] = e.CpuUsagePercent.ToString("0.0"),
                ["gpuUsage"] = e.GpuUsagePercent.ToString("0.0")
            },
            correlationId));
    }

    private async void OnDefaultAudioRenderDeviceChanged(object? sender, DefaultAudioRenderDeviceChangedEventArgs e)
    {
        await RequestObservationRefreshAsync("default-output-changed", Guid.NewGuid());
    }

    private async void OnNetworkStatusChanged(object sender)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        await RequestObservationRefreshAsync("network-state-changed", Guid.NewGuid());
    }

    private async void OnSystemPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.Mode == PowerModes.Suspend)
        {
            _isSuspended = true;
            _performanceSampler.SetPaused(true);
            UpdateCollectionPlan();
            SyncObservationSources(requestRefreshWhenStarted: false);
            SignalSceneCheck();
            return;
        }
        if (e.Mode == PowerModes.Resume) _isSuspended = false;
        UpdateCollectionPlan(refreshPower: true);
        SyncObservationSources(requestRefreshWhenStarted: false);
        SignalSceneCheck();
        if (ObservationPaused) return;

        if (e.Mode is PowerModes.Resume or PowerModes.StatusChange)
        {
            _log.Debug("Automation observation received power mode change. Mode={Mode}", e.Mode);
            var correlationId = Guid.NewGuid();
            _observationPublisher.PublishSystemSignal($"system-power-{e.Mode}", correlationId);
            if (e.Mode == PowerModes.Resume)
            {
                _observationPublisher.PublishBackgroundDispatchMessage(
                    BackgroundDispatchMessageType.SystemSignal,
                    $"system-power-{e.Mode}",
                    BackgroundDispatchScene.Unlock,
                    new Dictionary<string, string?>
                    {
                        ["reason"] = $"system-power-{e.Mode}"
                    },
                    correlationId);
            }
            await RequestObservationRefreshAsync($"system-power-{e.Mode}", correlationId);
        }
    }

    private async void OnSystemTimeChanged(object? sender, EventArgs e)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        _log.Debug("Automation observation received system time change");
        var correlationId = Guid.NewGuid();
        _observationPublisher.PublishSystemSignal("system-time-changed", correlationId);
        await RequestObservationRefreshAsync("system-time-changed", correlationId);
    }

    private async void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        if (e.Category is UserPreferenceCategory.Locale or UserPreferenceCategory.General)
        {
            _log.Debug("Automation observation received user preference change. Category={Category}", e.Category);
            var correlationId = Guid.NewGuid();
            var reason = $"user-preference-{e.Category}";
            _observationPublisher.PublishSystemSignal(reason, correlationId);
            await RequestObservationRefreshAsync(reason, correlationId);
        }
    }

    private async void OnSystemForegroundWindowChanged(object? sender, IntPtr hwnd)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        try
        {
            UpdateCollectionPlan();
            _performanceSampler.RequestImmediateSample();
            _log.Debug("Automation observation received foreground window change. WindowHandle={WindowHandle}", hwnd);
            var correlationId = Guid.NewGuid();
            _observationPublisher.PublishForegroundWindowChanged(hwnd, correlationId);
            _observationPublisher.PublishBackgroundDispatchMessage(
                BackgroundDispatchMessageType.ForegroundWindowChanged,
                "foreground-window-changed",
                BackgroundDispatchScene.HeavyWork,
                new Dictionary<string, string?>
                {
                    ["windowHandle"] = hwnd.ToInt64().ToString(),
                    ["processName"] = ForegroundWindowProbe.GetForegroundProcessName()
                },
                correlationId);
            await RequestObservationRefreshAsync("foreground-window-changed", correlationId);
        }
        catch (Exception ex)
        {
            // 事件回调里的异常没人接，会一路走到全局 FailFast 把进程杀掉，这里必须兜住。
            _log.Warning(ex, "Automation foreground window change handling failed");
        }
    }

    private async Task RequestObservationRefreshAsync(string reason, Guid? correlationId = null)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        // 合并：200 毫秒内来的多个事件只采一次，避免快速切窗时连采好几轮。
        CancellationTokenSource batchCts;
        lock (_refreshSync)
        {
            _pendingReasons.Add(reason);
            if (correlationId.HasValue)
            {
                _pendingCorrelationId = correlationId;
            }

            if (_refreshPending)
            {
                return;
            }

            _refreshPending = true;
            batchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            _refreshBatchCts = batchCts;
        }

        try
        {
            await Task.Delay(ObservationCoalesceDelay, batchCts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_refreshSync)
            {
                if (ReferenceEquals(_refreshBatchCts, batchCts))
                {
                    _refreshBatchCts = null;
                    _refreshPending = false;
                    _pendingReasons.Clear();
                    _pendingCorrelationId = null;
                }
            }
            batchCts.Dispose();
            return;
        }

        string mergedReason;
        Guid? mergedCorrelationId;
        lock (_refreshSync)
        {
            if (!ReferenceEquals(_refreshBatchCts, batchCts))
            {
                batchCts.Dispose();
                return;
            }
            _refreshBatchCts = null;
            _refreshPending = false;
            mergedReason = string.Join("+", _pendingReasons.Distinct(StringComparer.OrdinalIgnoreCase));
            _pendingReasons.Clear();
            mergedCorrelationId = _pendingCorrelationId;
            _pendingCorrelationId = null;
        }
        batchCts.Dispose();
        var token = _cts?.Token ?? CancellationToken.None;

        try
        {
            var request = CollectInformationRequest(mergedReason, mergedCorrelationId);
            _log.Debug(
                "Automation observation refresh requested. Reason={Reason}, Scene={Scene}, Items={Items}",
                mergedReason,
                request.Scene,
                string.Join(",", request.Items));
            await PublishCurrentStateAsync(token, request);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // 这条路径由 async void 事件回调驱动，异常逃出去会被全局处理器当成致命错误。
            _log.Warning(ex, "Automation observation refresh failed. Reason={Reason}", mergedReason);
        }
    }

    private void CancelPendingRefresh()
    {
        CancellationTokenSource? batchCts;
        lock (_refreshSync)
        {
            batchCts = _refreshBatchCts;
            _refreshBatchCts = null;
            _refreshPending = false;
            _pendingReasons.Clear();
            _pendingCorrelationId = null;
        }
        try { batchCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private ObservationRequest CollectInformationRequest(string reason, Guid? correlationId = null)
    {
        lock (_sceneSync)
        {
            // 旧计划可能已经丢弃了一次设备/网络事件，补齐新计划的必需状态，避免等不到下次变化。
            if (_planRefreshPending && !ObservationPaused)
            {
                reason += "+scene-changed";
                _planRefreshPending = false;
            }
            return _requestPlanner.Create(reason, correlationId, ObservationPaused) with { PlanVersion = _planVersion };
        }
    }

    private async Task PublishCurrentStateAsync(CancellationToken token, ObservationRequest request)
    {
        if (!IsRequestCurrent(request))
        {
            return;
        }

        if (request.Includes(ObservationInfoKind.ThemeMode) &&
            ObservationDemandCatalog.GetSunScheduleDemand(_pipelineRepository).Count > 0)
        {
            // 太阳触发与条件使用同一轮快照，不能在慢条件补齐前先发触发器。
            await CollectAndPublishAsync(token, request);
            return;
        }

        // 快速项（进程、窗口、蓝牙缓存、电源、主题）先采先发，让事件尽快进自动化；
        // 慢项（亮度、网络、代理、音频路由）随后补采再发一次，发布层只发真正变了的部分。
        var fastItems = request.Items.Where(item => !AutomationObservationGetterSelector.IsSlowKind(item)).ToArray();
        var slowItems = request.Items.Where(AutomationObservationGetterSelector.IsSlowKind).ToArray();

        if (fastItems.Length > 0)
        {
            await CollectAndPublishAsync(token, request with { Items = fastItems });
        }

        if (slowItems.Length > 0)
        {
            ScheduleSlowCollection(token, request with { Items = slowItems });
        }
    }

    private async Task CollectAndPublishAsync(CancellationToken token, ObservationRequest request)
    {
        if (!IsRequestCurrent(request))
        {
            return;
        }
        using var collectionCts = CreateCollectionCancellation(token);
        token = collectionCts.Token;

        try
        {
            await _observationPublisher.PublishGate.WaitAsync(token);
        }
        catch (ObjectDisposedException)
        {
            // 退出过程中发布器先被释放了，这次刷新直接作废。
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (!IsRequestCurrent(request)) return;
            var previous = _lastPublishedContext;
            var context = await _observationCollector.CollectAsync(previous, request, token);
            if (!IsRequestCurrent(request) || token.IsCancellationRequested)
            {
                return;
            }
            await _observationProcessor.ProcessAsync(context, token);
            if (!IsRequestCurrent(request) || token.IsCancellationRequested) return;
            _observationDispatcher.Dispatch(_lastPublishedContext, context, request);
            _lastPublishedContext = context;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Automation observation publish failed");
        }
        finally
        {
            try
            {
                _observationPublisher.PublishGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // 退出时发布器已释放，这次释放没有意义，忽略。
            }
        }
    }

    private CancellationTokenSource CreateCollectionCancellation(CancellationToken token)
    {
        lock (_sceneSync)
        {
            if (_disposed)
            {
                var canceled = new CancellationTokenSource();
                canceled.Cancel();
                return canceled;
            }
            return CancellationTokenSource.CreateLinkedTokenSource(token, _planCts.Token);
        }
    }

    private readonly object _slowCollectionSync = new();
    private readonly HashSet<ObservationInfoKind> _pendingSlowItems = [];
    private ObservationRequest? _pendingSlowRequest;
    private bool _slowCollectionRunning;

    private void ScheduleSlowCollection(CancellationToken token, ObservationRequest request)
    {
        lock (_slowCollectionSync)
        {
            if (_pendingSlowRequest?.PlanVersion != request.PlanVersion) _pendingSlowItems.Clear();
            foreach (var item in request.Items)
            {
                _pendingSlowItems.Add(item);
            }

            _pendingSlowRequest = request;

            if (_slowCollectionRunning)
            {
                return;
            }

            _slowCollectionRunning = true;
        }

        _ = Task.Run(() => RunSlowCollectionAsync(token), CancellationToken.None);
    }

    private async Task RunSlowCollectionAsync(CancellationToken token)
    {
        while (true)
        {
            ObservationRequest request;
            lock (_slowCollectionSync)
            {
                if (_pendingSlowItems.Count == 0 || _pendingSlowRequest is null)
                {
                    _pendingSlowItems.Clear();
                    _pendingSlowRequest = null;
                    _slowCollectionRunning = false;
                    return;
                }

                request = _pendingSlowRequest with { Items = _pendingSlowItems.ToArray() };
                _pendingSlowItems.Clear();
                _pendingSlowRequest = null;
            }

            try
            {
                await CollectAndPublishAsync(token, request);
            }
            catch (OperationCanceledException)
            {
                lock (_slowCollectionSync)
                {
                    _pendingSlowItems.Clear();
                    _pendingSlowRequest = null;
                    _slowCollectionRunning = false;
                }

                return;
            }
        }
    }

    private async Task<double?> TryGetScreenBrightnessAsync(CancellationToken token)
    {
        try
        {
            return await _displayProvider.GetBrightnessAsync(token);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to capture screen brightness for system context snapshot");
            return null;
        }
    }

    private void DistributeInformation(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        ObservationRequest request)
    {
        _observationPublisher.PublishPowerStateEvent(previous, current, request.CorrelationId);
        _observationPublisher.PublishBluetoothEvents(previous, current, request.CorrelationId);
        _observationPublisher.PublishAudioEvents(previous, current, request.CorrelationId);
        _observationPublisher.PublishNetworkEvents(previous, current, request);
        if (request.Includes(ObservationInfoKind.ThemeMode))
            _sunObservationSource.PublishSunEvents(previous, current, request.CorrelationId);
        _observationPublisher.PublishThemeChangedEvent(previous, current, request.CorrelationId);

        _lastObservedForegroundWindowTitle = current.ForegroundWindowTitle;
    }

    private void StartProcessLifecycleWatchers()
    {
        try
        {
            _processStartedWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _processStartedWatcher.EventArrived += OnProcessStarted;
            _processStartedWatcher.Start();

            _processStoppedWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _processStoppedWatcher.EventArrived += OnProcessStopped;
            _processStoppedWatcher.Start();

            _log.Information("Automation process lifecycle watchers started with WMI process trace events");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to start WMI process lifecycle watchers");
            StopProcessLifecycleWatchers();
        }
    }

    private void StopProcessLifecycleWatchers()
    {
        if (_processStartedWatcher is not null)
        {
            try
            {
                _processStartedWatcher.EventArrived -= OnProcessStarted;
                _processStartedWatcher.Stop();
                _processStartedWatcher.Dispose();
            }
            catch
            {
            }
            finally
            {
                _processStartedWatcher = null;
            }
        }

        if (_processStoppedWatcher is not null)
        {
            try
            {
                _processStoppedWatcher.EventArrived -= OnProcessStopped;
                _processStoppedWatcher.Stop();
                _processStoppedWatcher.Dispose();
            }
            catch
            {
            }
            finally
            {
                _processStoppedWatcher = null;
            }
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        var occurredAt = DateTimeOffset.Now;
        var processName = AutomationObservationStateProcessor.NormalizeProcessName(e.NewEvent?["ProcessName"]?.ToString());
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        if (_observationProcessor.ShouldSkipDuplicateProcessStart(processName, occurredAt))
        {
            _log.Information(
                "Automation process-started event skipped as duplicate. ProcessName={ProcessName}, OccurredAt={OccurredAt}",
                processName,
                occurredAt);
            return;
        }

        var processId = e.NewEvent?["ProcessID"]?.ToString();
        var correlationId = Guid.NewGuid();
        _triggerBus.Publish(new TriggerEvent(
            AutomationTriggerCatalog.AppForegroundChanged,
            occurredAt,
            new Dictionary<string, string?>
            {
                ["processName"] = processName,
                ["processId"] = processId,
                ["eventKind"] = "process-started"
            },
            correlationId));

        _log.Information(
            "Automation process-started event published from WMI watcher. ProcessName={ProcessName}, ProcessId={ProcessId}, OccurredAt={OccurredAt}",
            processName,
            processId,
            occurredAt);

        _ = RequestObservationRefreshAsync("process-started", correlationId);
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        if (ObservationPaused || _disposed)
        {
            return;
        }

        var occurredAt = DateTimeOffset.Now;
        var processName = AutomationObservationStateProcessor.NormalizeProcessName(e.NewEvent?["ProcessName"]?.ToString());
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        var processId = e.NewEvent?["ProcessID"]?.ToString();
        var correlationId = Guid.NewGuid();
        _triggerBus.Publish(new TriggerEvent(
            AutomationTriggerCatalog.AppStopped,
            occurredAt,
            new Dictionary<string, string?>
            {
                ["processName"] = processName,
                ["processId"] = processId,
                ["windowTitle"] = _lastObservedForegroundWindowTitle,
                ["eventKind"] = "process-stopped"
            },
            correlationId));

        _log.Information(
            "Automation process-stopped event published from WMI watcher. ProcessName={ProcessName}, ProcessId={ProcessId}, OccurredAt={OccurredAt}",
            processName,
            processId,
            occurredAt);

        _ = RequestObservationRefreshAsync("process-stopped", correlationId);
        _gameModeDecisionService.HandleProcessStopped(processName, occurredAt, processId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // 先立起"已释放"标记，后续进来的事件回调会直接跳过，
        // 不会再摸到下面即将释放的信号量。
        _disposed = true;
        CancelPendingRefresh();
        _isSuspended = true;
        _performanceSampler.SetPaused(true);
        _autoDayNightService.SetObservationPaused(true);
        if (_mainWindow is not null)
        {
            _mainWindow.Activated -= OnMainWindowActivated;
            _mainWindow = null;
        }
        if (_appSettingsService is AppSettingsService settings) settings.ObservationDemandChanged -= OnPipelinesChanged;
        _brightnessSettings.PropertyChanged -= OnBrightnessSettingsChanged;
        lock (_sceneSync)
        {
            _planCts.Cancel();
            _planCts.Dispose();
        }

        _started = false;
        _pipelineRepository.PipelinesChanged -= OnPipelinesChanged;

        lock (_sourceSyncLock)
        {
            _sourceSyncDebounceCts?.Cancel();
            _sourceSyncDebounceCts?.Dispose();
            _sourceSyncDebounceCts = null;
            _subscriptionManager.UnsubscribeAll();
            _processWatchersRunning = false;
            StopProcessLifecycleWatchers();
            ApplySunScheduleDemand(false, requestRefreshWhenStarted: false);
            ApplyProxyWatchDemand(false);
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _sessionObservationSource.Dispose();
        _performanceSampler.Dispose();
        _observationProcessor.Dispose();

        _observationPublisher.Dispose();
        _ = (_sunLoopTask ?? Task.CompletedTask).ContinueWith(_ => _sunObservationSource.Dispose(),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _dynamicPerformanceService.StageChanged -= OnDynamicPerformanceStageChanged;
        var sceneTask = _sceneCheckTask ?? Task.CompletedTask;
        _ = sceneTask.ContinueWith(_ => _sceneCheckSignal.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

}
