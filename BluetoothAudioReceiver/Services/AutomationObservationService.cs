using System.Management;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
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
    private readonly AutoBrightnessService _autoBrightnessService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAutomationPipelineRepository _pipelineRepository;
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

    private CancellationTokenSource? _cts;
    private Task? _sunLoopTask;
    private Task? _performanceModeLoopTask;
    private Task? _proxyWatchTask;
    private volatile bool _isLocked;
    private AutomationDynamicContext _lastPublishedContext = new();
    private string? _lastObservedForegroundWindowTitle;
    private ManagementEventWatcher? _processStartedWatcher;
    private ManagementEventWatcher? _processStoppedWatcher;
    private bool _started;
    private static readonly TimeSpan PerformanceModeCandidatePollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PerformanceModeActivePollInterval = TimeSpan.FromSeconds(3);

    public AutomationObservationService(
        IAutomationSystemContextHub systemContextHub,
        IAutomationTriggerBus triggerBus,
        IAudioPlaybackConnectionService audioPlaybackConnectionService,
        BluetoothRadioMonitorService bluetoothRadioMonitorService,
        IBackgroundDispatchBus backgroundDispatchBus,
        IDisplayProvider displayProvider,
        AutoDayNightService autoDayNightService,
        AutoBrightnessService autoBrightnessService,
        IAppSettingsService appSettingsService,
        INotificationService notificationService,
        IAutomationPipelineRepository pipelineRepository,
        AutomationObservationGetterSelector getterSelector)
    {
        _systemContextHub = systemContextHub;
        _triggerBus = triggerBus;
        _audioPlaybackConnectionService = audioPlaybackConnectionService;
        _bluetoothRadioMonitorService = bluetoothRadioMonitorService;
        _displayProvider = displayProvider;
        _autoDayNightService = autoDayNightService;
        _autoBrightnessService = autoBrightnessService;
        _appSettingsService = appSettingsService;
        _pipelineRepository = pipelineRepository;
        _getterSelector = getterSelector;
        _observationPublisher = new AutomationObservationPublisher(
            _triggerBus,
            backgroundDispatchBus,
            _systemContextHub,
            _appSettingsService,
            notificationService);
        _observationCollector = new AutomationObservationCollector(
            _getterSelector,
            () => _cts?.Token ?? CancellationToken.None);
        _observationProcessor = new AutomationObservationStateProcessor(
            _systemContextHub,
            _triggerBus,
            _pipelineRepository,
            ForegroundWindowProbe.GetForegroundProcessName,
            ForegroundWindowProbe.GetForegroundProcessId,
            ForegroundWindowProbe.IsForegroundFullscreen);
        _observationDispatcher = new AutomationObservationEventDispatcher(DistributeInformation);
        _requestPlanner = new AutomationObservationRequestPlanner(
            _appSettingsService,
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
            value => _isLocked = value,
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

        _subscriptionManager.Subscribe();
        StartProcessLifecycleWatchers();

            await PublishCurrentStateAsync(_cts.Token, CollectInformationRequest("startup"));

        _sunLoopTask = _sunObservationSource.RunSunLoopAsync(_cts.Token);
        _performanceModeLoopTask = RunPerformanceModeLoopAsync(_cts.Token);
        _proxyWatchTask = _proxyObservationSource.RunProxyRegistryWatchAsync(_cts.Token);
        _log.Information("Automation observation service started with event-driven listeners");
    }

    public void InitializeForWindow(Views.Windows.MainWindow window)
    {
        _sessionObservationSource.InitializeForWindow(window);
    }

    private async Task RunPerformanceModeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_isLocked)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                    continue;
                }

                _observationProcessor.EvaluatePerformanceMode();
                var pollInterval = _observationProcessor.IsAnyModeActive()
                    ? PerformanceModeActivePollInterval
                    : PerformanceModeCandidatePollInterval;
                await Task.Delay(pollInterval, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Automation performance-mode loop failed");
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
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
        _observationPublisher.PublishMainlineSystemSignal(reason, correlationId);
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
                _observationPublisher.PublishMainlineSystemSignal($"system-power-{e.Mode}", correlationId);
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
        _observationPublisher.PublishMainlineSystemSignal("system-time-changed", correlationId);
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
            _observationPublisher.PublishMainlineSystemSignal(reason, correlationId);
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
        _autoBrightnessService.OnForegroundWindowChanged(hwnd);
        var correlationId = Guid.NewGuid();
        _observationPublisher.PublishMainlineForegroundWindowChanged(hwnd, correlationId);
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
        SchedulePerformanceModeEvaluation();
        await RequestObservationRefreshAsync("foreground-window-changed", correlationId);
    }

    private async Task RequestObservationRefreshAsync(string reason, Guid? correlationId = null)
    {
        if (_isLocked)
        {
            return;
        }

        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            var request = CollectInformationRequest(reason, correlationId);
            _log.Debug(
                "Automation observation refresh requested. Reason={Reason}, Scene={Scene}, Items={Items}",
                reason,
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
        SchedulePerformanceModeEvaluation();
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
        _observationProcessor.HandleProcessStopped(processName, occurredAt, processId);
    }

    public void Dispose()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _subscriptionManager.Unsubscribe();
        StopProcessLifecycleWatchers();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _sessionObservationSource.Dispose();

        _observationProcessor.Dispose();

        _observationPublisher.Dispose();
        _sunObservationSource.Dispose();
    }

    private void SchedulePerformanceModeEvaluation()
    {
        if (_started)
        {
            _observationProcessor.EvaluatePerformanceMode();
        }
    }

}
