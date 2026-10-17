using System.Management;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Serilog;
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
    private readonly PerformanceObservationSampler _performanceSampler;
    private readonly AutomationObservationGetterSelector _getterSelector;
    private readonly IAutomationObservationCollector _observationCollector;
    private readonly AutomationObservationStateProcessor _observationProcessor;
    private readonly IAutomationObservationDispatcher _observationDispatcher;
    private readonly AutomationObservationRequestPlanner _requestPlanner;
    private readonly AutomationObservationSubscriptionManager _subscriptionManager;
    private readonly AutomationObservationPublisher _observationPublisher;
    private readonly AutomationSunObservationSource _sunObservationSource;
    private readonly AutomationProxyObservationSource _proxyObservationSource;
    private readonly AutomationSessionObservationSource _sessionObservationSource;
    private readonly ObservationDemandRegistry _demandRegistry;

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
    private static readonly TimeSpan ObservationCoalesceDelay = TimeSpan.FromMilliseconds(200);
    private readonly object _refreshSync = new();
    private readonly List<string> _pendingReasons = [];
    private Guid? _pendingCorrelationId;
    private bool _refreshPending;

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
        PerformanceObservationSampler performanceSampler,
        AutomationObservationGetterSelector getterSelector,
        ObservationDemandRegistry demandRegistry)
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
        _gameModeDecisionService = gameModeDecisionService;
        _performanceSampler = performanceSampler;
        _getterSelector = getterSelector;
        _observationPublisher = new AutomationObservationPublisher(
            _triggerBus,
            backgroundDispatchBus,
            _systemContextHub);
        _observationCollector = new AutomationObservationCollector(
            _getterSelector,
            () => _cts?.Token ?? CancellationToken.None);
        _observationProcessor = new AutomationObservationStateProcessor(
            _systemContextHub,
            ForegroundWindowProbe.GetForegroundProcessName,
            ForegroundWindowProbe.GetForegroundProcessId,
            ForegroundWindowProbe.IsForegroundFullscreen);
        _observationDispatcher = new AutomationObservationEventDispatcher(DistributeInformation);
        _requestPlanner = new AutomationObservationRequestPlanner(
            demandRegistry,
            ForegroundWindowProbe.GetForegroundProcessName,
            ForegroundWindowProbe.GetForegroundWindowTitle,
            ForegroundWindowProbe.IsForegroundFullscreen,
            ForegroundWindowProbe.IsLikelyGame);
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
            () => _isLocked,
            token => PublishCurrentStateAsync(token, CollectInformationRequest("sun-loop")));
        _proxyObservationSource = new AutomationProxyObservationSource(
            () => _isLocked,
            (reason, correlationId) => RequestObservationRefreshAsync(reason, correlationId));
        _sessionObservationSource = new AutomationSessionObservationSource(
            _observationPublisher,
            () => _isLocked,
            value =>
            {
                _isLocked = value;
                _performanceSampler.SetPaused(value);
            },
            (reason, correlationId) => RequestObservationRefreshAsync(reason, correlationId));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 先按各模块登记的需求把事件源和后台循环挂上，避免启动阶段漏事件。
        // 启动时的全量采集紧接着做，所以这里不需要再补一次刷新。
        SyncObservationSources(requestRefreshWhenStarted: false);

        await PublishCurrentStateAsync(_cts.Token, CollectInformationRequest("startup"));

        _pipelineRepository.PipelinesChanged += OnPipelinesChanged;

        _performanceSampler.Start(_observationProcessor.CapturePerformanceSample);
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

        _subscriptionManager.Apply(sources);
        ApplyProcessTraceDemand(sources.Contains(ObservationSourceKind.ProcessLifecycle), requestRefreshWhenStarted);
        ApplySunScheduleDemand(sources.Contains(ObservationSourceKind.SunScheduleLoop), requestRefreshWhenStarted);
        ApplyProxyWatchDemand(sources.Contains(ObservationSourceKind.ProxyRegistryWatch));
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
            _sunLoopTask = _sunObservationSource.RunSunLoopAsync(cts.Token);
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
        _sunLoopTask = null;
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

    private void RequestRefreshInBackground(string reason)
        => _ = RequestObservationRefreshAsync(reason, Guid.NewGuid());

    private void OnPipelinesChanged(object? sender, EventArgs e)
    {
        CancellationTokenSource debounceCts;
        lock (_sourceSyncLock)
        {
            _sourceSyncDebounceCts?.Cancel();
            _sourceSyncDebounceCts?.Dispose();
            debounceCts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            _sourceSyncDebounceCts = debounceCts;
        }

        _ = RunDebouncedSourceSyncAsync(debounceCts.Token);
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
            SyncObservationSources();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to sync demand-driven observation sources");
        }
    }

    public void InitializeForWindow(Views.Windows.MainWindow window)
    {
        _sessionObservationSource.InitializeForWindow(window);

        // 用户改完设置回到主窗口时重算一次需求：模块开关（比如代理通知）变了，
        // 对应的事件源该挂就挂、该摘就摘。防抖后只算最后那次。
        window.Activated += OnMainWindowActivated;
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
        if (_isLocked)
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
        await RequestObservationRefreshAsync("power-changed", Guid.NewGuid());
    }

    private async void OnDefaultAudioRenderDeviceChanged(object? sender, DefaultAudioRenderDeviceChangedEventArgs e)
    {
        await RequestObservationRefreshAsync("default-output-changed", Guid.NewGuid());
    }

    private async void OnNetworkStatusChanged(object sender)
    {
        if (_isLocked)
        {
            return;
        }

        await RequestObservationRefreshAsync("network-state-changed", Guid.NewGuid());
    }

    private async void OnSystemPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (_isLocked)
        {
            return;
        }

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
        if (_isLocked)
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
        if (_isLocked)
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
        if (_isLocked)
        {
            return;
        }

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

    private async Task RequestObservationRefreshAsync(string reason, Guid? correlationId = null)
    {
        if (_isLocked)
        {
            return;
        }

        // 合并：200 毫秒内来的多个事件只采一次，避免快速切窗时连采好几轮。
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
        }

        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            await Task.Delay(ObservationCoalesceDelay, token);
        }
        catch (OperationCanceledException)
        {
            lock (_refreshSync)
            {
                _refreshPending = false;
                _pendingReasons.Clear();
                _pendingCorrelationId = null;
            }

            return;
        }

        string mergedReason;
        Guid? mergedCorrelationId;
        lock (_refreshSync)
        {
            _refreshPending = false;
            mergedReason = string.Join("+", _pendingReasons.Distinct(StringComparer.OrdinalIgnoreCase));
            _pendingReasons.Clear();
            mergedCorrelationId = _pendingCorrelationId;
            _pendingCorrelationId = null;
        }

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
    }

    private ObservationRequest CollectInformationRequest(string reason, Guid? correlationId = null)
        => _requestPlanner.Create(reason, correlationId, _isLocked);

    private async Task PublishCurrentStateAsync(CancellationToken token, ObservationRequest request)
    {
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
        await _observationPublisher.PublishGate.WaitAsync(token);
        try
        {
            var previous = _lastPublishedContext;
            var context = await _observationCollector.CollectAsync(previous, request, token);
            if (_isLocked)
            {
                return;
            }
            await _observationProcessor.ProcessAsync(context, token);
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
            _observationPublisher.PublishGate.Release();
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
        if (_isLocked)
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
        if (_isLocked)
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
        if (!_started)
        {
            return;
        }

        _started = false;
        _subscriptionManager.UnsubscribeAll();
        _pipelineRepository.PipelinesChanged -= OnPipelinesChanged;

        lock (_sourceSyncLock)
        {
            _sourceSyncDebounceCts?.Cancel();
            _sourceSyncDebounceCts?.Dispose();
            _sourceSyncDebounceCts = null;
        }

        _processWatchersRunning = false;
        StopProcessLifecycleWatchers();

        _sunLoopRunning = false;
        _sunLoopCts?.Cancel();
        _sunLoopCts = null;

        _proxyWatchRunning = false;
        _proxyWatchCts?.Cancel();
        _proxyWatchCts = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _sessionObservationSource.Dispose();

        _observationProcessor.Dispose();

        _observationPublisher.Dispose();
        _sunObservationSource.Dispose();
        _performanceSampler.Dispose();
    }

}
