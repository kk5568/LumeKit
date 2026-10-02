using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
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
using Windows.Networking.Connectivity;
using Windows.System.Power;
using WinUIEx.Messaging;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationObservationService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationObservationService));

    private readonly IAutomationSystemContextHub _systemContextHub;
    private readonly IAutomationTriggerBus _triggerBus;
    private readonly IAudioPlaybackConnectionService _audioPlaybackConnectionService;
    private readonly BluetoothRadioMonitorService _bluetoothRadioMonitorService;
    private readonly IBackgroundDispatchBus _backgroundDispatchBus;
    private readonly IDisplayProvider _displayProvider;
    private readonly AutoDayNightService _autoDayNightService;
    private readonly AutoBrightnessService _autoBrightnessService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly INotificationService _notificationService;
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly AutomationObservationGetterSelector _getterSelector;
    private readonly IAutomationObservationCollector _observationCollector;
    private readonly AutomationObservationStateProcessor _observationProcessor;
    private readonly IAutomationObservationDispatcher _observationDispatcher;
    private readonly AutomationObservationRequestPlanner _requestPlanner;
    private readonly AutomationObservationSubscriptionManager _subscriptionManager;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly SemaphoreSlim _sunLoopGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _sunLoopTask;
    private Task? _performanceModeLoopTask;
    private Task? _proxyWatchTask;
    private volatile bool _isLocked;
    private AutomationDynamicContext _lastPublishedContext = new();
    private string? _lastObservedForegroundWindowTitle;
    private DateOnly? _lastPublishedSunriseDate;
    private DateOnly? _lastPublishedSunsetDate;
    private ManagementEventWatcher? _processStartedWatcher;
    private ManagementEventWatcher? _processStoppedWatcher;
    private WindowMessageMonitor? _sessionMessageMonitor;
    private IntPtr _sessionWindowHandle;
    private DateTimeOffset _lastSessionSignalAt = DateTimeOffset.MinValue;
    private string? _lastSessionSignalReason;
    private bool _started;
    private static readonly TimeSpan PerformanceModeCandidatePollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PerformanceModeActivePollInterval = TimeSpan.FromSeconds(3);
    private const string InternetSettingsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string InternetSettingsConnectionsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";

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
        _backgroundDispatchBus = backgroundDispatchBus;
        _displayProvider = displayProvider;
        _autoDayNightService = autoDayNightService;
        _autoBrightnessService = autoBrightnessService;
        _appSettingsService = appSettingsService;
        _notificationService = notificationService;
        _pipelineRepository = pipelineRepository;
        _getterSelector = getterSelector;
        _observationCollector = new AutomationObservationCollector(
            _getterSelector,
            () => _cts?.Token ?? CancellationToken.None);
        _observationProcessor = new AutomationObservationStateProcessor(
            _systemContextHub,
            _triggerBus,
            _pipelineRepository,
            GetForegroundProcessName,
            GetForegroundProcessId,
            IsForegroundFullscreen);
        _observationDispatcher = new AutomationObservationEventDispatcher(DistributeInformation);
        _requestPlanner = new AutomationObservationRequestPlanner(
            _appSettingsService,
            GetForegroundProcessName,
            GetForegroundWindowTitle,
            IsForegroundFullscreen,
            IsLikelyGame);
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

        _sunLoopTask = RunSunLoopAsync(_cts.Token);
        _performanceModeLoopTask = RunPerformanceModeLoopAsync(_cts.Token);
        _proxyWatchTask = RunProxyRegistryWatchAsync(_cts.Token);
        _log.Information("Automation observation service started with event-driven listeners");
    }

    public void InitializeForWindow(Views.Windows.MainWindow window)
    {
        if (_sessionMessageMonitor != null)
        {
            return;
        }

        _sessionWindowHandle = window.WindowHandle;
        _sessionMessageMonitor = new WindowMessageMonitor(window);
        _sessionMessageMonitor.WindowMessageReceived += OnSessionWindowMessageReceived;

        if (WTSRegisterSessionNotification(_sessionWindowHandle, NotifyForThisSession))
        {
            _log.Information(
                "Native session notification registered. WindowHandle={WindowHandle}",
                _sessionWindowHandle);
        }
        else
        {
            _log.Warning(
                "Native session notification registration failed. WindowHandle={WindowHandle}, Error={Error}",
                _sessionWindowHandle,
                Marshal.GetLastWin32Error());
        }
    }

    private async Task RunSunLoopAsync(CancellationToken token)
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

                var now = DateTimeOffset.Now;
                var next = new DateTimeOffset(
                    now.Year,
                    now.Month,
                    now.Day,
                    now.Hour,
                    now.Minute,
                    0,
                    now.Offset).AddMinutes(1);

                var delay = next - now;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token);
                }

                await RefreshSunScheduleAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Automation sun schedule loop failed");
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
    }

    private async Task RefreshSunScheduleAsync(CancellationToken token)
    {
        await _sunLoopGate.WaitAsync(token);
        try
        {
                await PublishCurrentStateAsync(token, CollectInformationRequest("sun-loop"));
        }
        finally
        {
            _sunLoopGate.Release();
        }
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

    private async Task RunProxyRegistryWatchAsync(CancellationToken token)
    {
        await Task.WhenAll(
            RunProxyRegistryWatchPathAsync(InternetSettingsRegistryPath, token),
            RunProxyRegistryWatchPathAsync(InternetSettingsConnectionsRegistryPath, token));
    }

    private async Task RunProxyRegistryWatchPathAsync(string registryPath, CancellationToken token)
    {
        IntPtr keyHandle = IntPtr.Zero;
        IntPtr eventHandle = IntPtr.Zero;

        try
        {
            if (!TryOpenRegistryKey(registryPath, out keyHandle))
            {
                _log.Warning("Automation proxy registry watch failed to open key. RegistryPath={RegistryPath}", registryPath);
                return;
            }

            eventHandle = CreateEvent(IntPtr.Zero, false, false, null);
            if (eventHandle == IntPtr.Zero)
            {
                _log.Warning("Automation proxy registry watch failed to create event handle");
                return;
            }

            while (!token.IsCancellationRequested)
            {
                var notifyResult = RegNotifyChangeKeyValue(
                        keyHandle,
                        true,
                        RegChangeNotifyFilter.Key | RegChangeNotifyFilter.Attribute | RegChangeNotifyFilter.Value,
                        eventHandle,
                        true);
                if (notifyResult != 0)
                {
                    _log.Warning("Automation proxy registry watch failed to arm notification. RegistryPath={RegistryPath}, ErrorCode={ErrorCode}", registryPath, notifyResult);
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    continue;
                }

                var waitTask = Task.Run(() => WaitForSingleObject(eventHandle, Timeout.Infinite), token);
                var completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout.InfiniteTimeSpan, token));
                if (completedTask != waitTask)
                {
                    break;
                }

                var waitResult = await waitTask;
                if (waitResult == WaitObjectResult.WaitFailed)
                {
                    _log.Warning("Automation proxy registry watch wait failed. RegistryPath={RegistryPath}, ErrorCode={ErrorCode}", registryPath, Marshal.GetLastWin32Error());
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    continue;
                }

                if (_isLocked)
                {
                    continue;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), token);
                await RequestObservationRefreshAsync("proxy-registry-changed", Guid.NewGuid());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Automation proxy registry watch failed. RegistryPath={RegistryPath}", registryPath);
        }
        finally
        {
            if (eventHandle != IntPtr.Zero)
            {
                CloseHandle(eventHandle);
            }

            if (keyHandle != IntPtr.Zero)
            {
                RegCloseKey(keyHandle);
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
        PublishMainlineSystemSignal(reason, correlationId);
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
                PublishMainlineSystemSignal($"system-power-{e.Mode}", correlationId);
                if (e.Mode == PowerModes.Resume)
                {
                    PublishBackgroundDispatchMessage(
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

    private void OnSessionWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if ((int)e.Message.MessageId != WmWtsSessionChange)
        {
            return;
        }

        var sessionEvent = (int)e.Message.WParam;
        var sessionId = (int)e.Message.LParam;
        _log.Information(
            "Received native session notification. Event={Event}, SessionId={SessionId}",
            sessionEvent,
            sessionId);

        if (sessionEvent == WtsSessionLock)
        {
            _isLocked = true;
            _log.Information("Processed native SessionLock notification. IsLockedAfter={IsLockedAfter}", _isLocked);
            return;
        }

        if (sessionEvent != WtsSessionUnlock)
        {
            return;
        }

        const string reason = "session-SessionUnlock";
        if (!TryAcceptSessionSignal(reason))
        {
            return;
        }

        _isLocked = false;
        var correlationId = Guid.NewGuid();
        _log.Information(
            "Processed native SessionUnlock notification. IsLockedAfter={IsLockedAfter}, CorrelationId={CorrelationId}",
            _isLocked,
            correlationId);
        PublishMainlineSystemSignal(reason, correlationId);
        PublishBackgroundDispatchMessage(
            BackgroundDispatchMessageType.SystemSignal,
            reason,
            BackgroundDispatchScene.Unlock,
            new Dictionary<string, string?>
            {
                ["reason"] = reason
            },
            correlationId);
        _ = RequestObservationRefreshAsync(reason, correlationId);
    }

    private bool TryAcceptSessionSignal(string reason)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastSessionSignalReason == reason &&
            now - _lastSessionSignalAt < TimeSpan.FromSeconds(2))
        {
            _log.Debug(
                "Ignored duplicate session signal. Reason={Reason}, ElapsedMs={ElapsedMs}",
                reason,
                (now - _lastSessionSignalAt).TotalMilliseconds);
            return false;
        }

        _lastSessionSignalReason = reason;
        _lastSessionSignalAt = now;
        return true;
    }

    private async void OnSystemTimeChanged(object? sender, EventArgs e)
    {
        if (_isLocked)
        {
            return;
        }

        _log.Debug("Automation observation received system time change");
        var correlationId = Guid.NewGuid();
        PublishMainlineSystemSignal("system-time-changed", correlationId);
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
            PublishMainlineSystemSignal(reason, correlationId);
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
        PublishMainlineForegroundWindowChanged(hwnd, correlationId);
        PublishBackgroundDispatchMessage(
            BackgroundDispatchMessageType.ForegroundWindowChanged,
            "foreground-window-changed",
            BackgroundDispatchScene.HeavyWork,
            new Dictionary<string, string?>
            {
                ["windowHandle"] = hwnd.ToInt64().ToString(),
                ["processName"] = GetForegroundProcessName()
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
        await _publishGate.WaitAsync(token);
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
            _publishGate.Release();
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
        PublishPowerStateEvent(previous, current, request.CorrelationId);
        PublishBluetoothEvents(previous, current, request.CorrelationId);
        PublishAudioEvents(previous, current, request.CorrelationId);
        PublishNetworkEvents(previous, current, request);
        PublishSunEvents(previous, current, request.CorrelationId);
        PublishThemeChangedEvent(previous, current, request.CorrelationId);

        _lastObservedForegroundWindowTitle = current.ForegroundWindowTitle;
    }

    private void PublishNetworkEvents(AutomationDynamicContext previous, AutomationDynamicContext current, ObservationRequest request)
    {
        if (!string.Equals(previous.NetworkName, current.NetworkName, StringComparison.OrdinalIgnoreCase) ||
            previous.IsProxyEnabled != current.IsProxyEnabled ||
            !string.Equals(previous.ProxyMode, current.ProxyMode, StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug(
                "Automation observation received network/proxy state change. PreviousNetwork={PreviousNetwork}, CurrentNetwork={CurrentNetwork}, PreviousProxyEnabled={PreviousProxyEnabled}, CurrentProxyEnabled={CurrentProxyEnabled}, PreviousProxyMode={PreviousProxyMode}, CurrentProxyMode={CurrentProxyMode}",
                previous.NetworkName,
                current.NetworkName,
                previous.IsProxyEnabled,
                current.IsProxyEnabled,
                previous.ProxyMode,
                current.ProxyMode);

            PublishMainlineSystemSignal("network-proxy-changed", request.CorrelationId);

            if (current.IsProxyEnabled is bool currentProxyEnabled &&
                (previous.IsProxyEnabled != current.IsProxyEnabled ||
                 !string.Equals(previous.ProxyMode, current.ProxyMode, StringComparison.OrdinalIgnoreCase)))
            {
                PublishProxyStatusNotificationIfNeeded(
                    previous.IsProxyEnabled,
                    currentProxyEnabled,
                    previous.ProxyMode,
                    current.ProxyMode,
                    request);
            }
        }
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

    private void PublishPowerStateEvent(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        Guid correlationId)
    {
        if (previous.IsOnBattery != current.IsOnBattery ||
            previous.IsCharging != current.IsCharging ||
            previous.BatteryPercent != current.BatteryPercent)
        {
            _triggerBus.Publish(new TriggerEvent(
                AutomationTriggerCatalog.PowerChanged,
                current.CapturedAt,
                new Dictionary<string, string?>
                {
                    ["isOnBattery"] = current.IsOnBattery?.ToString(),
                    ["isCharging"] = current.IsCharging?.ToString(),
                    ["batteryPercent"] = current.BatteryPercent?.ToString()
                },
                correlationId));
        }
    }

    private void PublishBluetoothEvents(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        Guid correlationId)
    {
        if (previous.IsBluetoothRadioOn != current.IsBluetoothRadioOn)
        {
            _log.Debug(
                "Automation observation received bluetooth radio state change. PreviousState={PreviousState}, CurrentState={CurrentState}",
                previous.IsBluetoothRadioOn,
                current.IsBluetoothRadioOn);
        }

        var previousIds = previous.ConnectedBluetoothDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentIds = current.ConnectedBluetoothDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var connectedDeviceId in currentIds.Except(previousIds, StringComparer.OrdinalIgnoreCase))
        {
            _triggerBus.Publish(new TriggerEvent(
                AutomationTriggerCatalog.BluetoothConnected,
                current.CapturedAt,
                new Dictionary<string, string?>
                {
                    ["deviceId"] = connectedDeviceId,
                    ["connectedCount"] = currentIds.Count.ToString()
                },
                correlationId));
        }
    }

    private void PublishAudioEvents(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        Guid correlationId)
    {
        if (!string.Equals(previous.DefaultOutputAudioDeviceId, current.DefaultOutputAudioDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            _triggerBus.Publish(new TriggerEvent(
                AutomationTriggerCatalog.OutputDeviceChanged,
                current.CapturedAt,
                new Dictionary<string, string?>
                {
                    ["previousDeviceId"] = previous.DefaultOutputAudioDeviceId,
                    ["currentDeviceId"] = current.DefaultOutputAudioDeviceId
                },
                correlationId));
        }
    }

    private void PublishSunEvents(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        Guid correlationId)
    {
        var localDate = DateOnly.FromDateTime(current.CapturedAt.LocalDateTime.Date);
        var currentTime = current.CapturedAt.LocalDateTime.TimeOfDay;

        if (current.Sunrise is TimeSpan sunrise &&
            currentTime >= sunrise &&
            currentTime < sunrise.Add(TimeSpan.FromMinutes(1)) &&
            _lastPublishedSunriseDate != localDate)
        {
            _lastPublishedSunriseDate = localDate;
            _triggerBus.Publish(new TriggerEvent(
                AutomationTriggerCatalog.SunriseReached,
                current.CapturedAt,
                new Dictionary<string, string?>
                {
                    ["sunrise"] = sunrise.ToString(@"hh\:mm"),
                    ["source"] = current.SunTimeSource
                },
                correlationId));
        }

        if (current.Sunset is TimeSpan sunset &&
            currentTime >= sunset &&
            currentTime < sunset.Add(TimeSpan.FromMinutes(1)) &&
            _lastPublishedSunsetDate != localDate)
        {
            _lastPublishedSunsetDate = localDate;
            _triggerBus.Publish(new TriggerEvent(
                AutomationTriggerCatalog.SunsetReached,
                current.CapturedAt,
                new Dictionary<string, string?>
                {
                    ["sunset"] = sunset.ToString(@"hh\:mm"),
                    ["source"] = current.SunTimeSource
                },
                correlationId));
        }

        if (previous.Sunrise == current.Sunrise && previous.Sunset == current.Sunset)
        {
            return;
        }

        _log.Information(
            "Automation sun schedule refreshed. Sunrise={Sunrise}, Sunset={Sunset}, Source={Source}",
            current.Sunrise,
            current.Sunset,
            current.SunTimeSource);
    }

    private void PublishThemeChangedEvent(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        Guid correlationId)
    {
        if (previous.IsDarkMode == current.IsDarkMode)
        {
            return;
        }

        PublishSystemSignal(
            "theme-changed",
            correlationId,
            new Dictionary<string, string?>
            {
                ["theme"] = current.IsDarkMode == true ? "dark" : "light"
            });
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

        if (_sessionMessageMonitor != null)
        {
            _sessionMessageMonitor.WindowMessageReceived -= OnSessionWindowMessageReceived;
            _sessionMessageMonitor.Dispose();
            _sessionMessageMonitor = null;
        }

        if (_sessionWindowHandle != IntPtr.Zero)
        {
            if (!WTSUnRegisterSessionNotification(_sessionWindowHandle))
            {
                _log.Debug(
                    "Native session notification unregister failed. WindowHandle={WindowHandle}, Error={Error}",
                    _sessionWindowHandle,
                    Marshal.GetLastWin32Error());
            }

            _sessionWindowHandle = IntPtr.Zero;
        }

        _observationProcessor.Dispose();

        _publishGate.Dispose();
        _sunLoopGate.Dispose();
    }

    private void PublishMainlineSystemSignal(string reason, Guid correlationId = default)
    {
        _log.Debug(
            "Automation observation publishing mainline system signal. Reason={Reason}, CorrelationId={CorrelationId}",
            reason,
            correlationId);
        PublishSystemSignal(reason, correlationId);
    }

    private void PublishMainlineForegroundWindowChanged(IntPtr hwnd, Guid correlationId = default)
    {
        var processName = GetForegroundProcessName();
        _log.Debug(
            "Automation observation publishing mainline foreground change. ProcessName={ProcessName}, WindowHandle={WindowHandle}",
            processName,
            hwnd);

        PublishSystemSignal(
            "foreground-window-changed",
            correlationId,
            new Dictionary<string, string?>
            {
                ["processName"] = processName,
                ["windowHandle"] = hwnd.ToString()
            });
    }

    private void PublishSystemSignal(
        string reason,
        Guid correlationId = default,
        IReadOnlyDictionary<string, string?>? additionalPayload = null)
    {
        var payload = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["reason"] = reason
        };

        if (additionalPayload is not null)
        {
            foreach (var pair in additionalPayload)
            {
                payload[pair.Key] = pair.Value;
            }
        }

        _triggerBus.Publish(new TriggerEvent(
            AutomationSupportCatalog.TriggerSystemSignal,
            DateTimeOffset.Now,
            payload,
            correlationId));
    }

    private void PublishBackgroundDispatchMessage(
        BackgroundDispatchMessageType messageType,
        string reason,
        BackgroundDispatchScene scene,
        IReadOnlyDictionary<string, string?> payload,
        Guid correlationId = default)
    {
        _ = _backgroundDispatchBus.PublishAsync(new BackgroundDispatchMessage(
            messageType,
            reason,
            scene,
            DateTimeOffset.Now,
            _systemContextHub.Current,
            payload,
            correlationId == Guid.Empty ? Guid.NewGuid() : correlationId));
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            return Process.GetProcessById((int)pid).ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetForegroundWindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new string('\0', length + 1);
            _ = GetWindowText(hwnd, buffer, buffer.Length);
            return buffer.TrimEnd('\0');
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetCurrentNetworkName()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                return string.Empty;
            }

            return profile.ProfileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void PublishProxyStatusNotificationIfNeeded(
        bool? previous,
        bool current,
        string? previousMode,
        string? currentMode,
        ObservationRequest request)
    {
        if (!_appSettingsService.ProxyStatusNotificationEnabled)
        {
            return;
        }

        if (string.Equals(request.Reason, "startup", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var title = GetProxyModeName(current ? currentMode : previousMode);
        var status = GetProxyStatusText(current);
        _notificationService.Show(NotificationRequest.Standard(
            "\uE128",
            title,
            status,
            bypassSuppression: true,
            category: "proxy-status"));
    }

    private static string GetProxyStatusText(bool current)
    {
        return current ? "已开启" : "已关闭";
    }

    private static string GetProxyModeName(string? mode)
        => string.Equals(mode, "tun", StringComparison.OrdinalIgnoreCase) ? "TUN 模式" : "系统代理";

    private static bool GetSystemProxyEnabled()
    {
        try
        {
            if (!WinHttpGetIEProxyConfigForCurrentUser(out var proxyConfig))
            {
                return false;
            }

            try
            {
                var autoConfigUrl = Marshal.PtrToStringUni(proxyConfig.lpszAutoConfigUrl);
                var proxy = Marshal.PtrToStringUni(proxyConfig.lpszProxy);
                return proxyConfig.fAutoDetect ||
                       !string.IsNullOrWhiteSpace(autoConfigUrl) ||
                       !string.IsNullOrWhiteSpace(proxy);
            }
            finally
            {
                FreeProxyConfig(proxyConfig);
            }
        }
        catch
        {
            return false;
        }
    }

    private static void FreeProxyConfig(WINHTTP_CURRENT_USER_IE_PROXY_CONFIG proxyConfig)
    {
        if (proxyConfig.lpszAutoConfigUrl != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(proxyConfig.lpszAutoConfigUrl);
        }

        if (proxyConfig.lpszProxy != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(proxyConfig.lpszProxy);
        }

        if (proxyConfig.lpszProxyBypass != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(proxyConfig.lpszProxyBypass);
        }
    }

    [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpGetIEProxyConfigForCurrentUser(out WINHTTP_CURRENT_USER_IE_PROXY_CONFIG pProxyConfig);

    private static bool TryOpenRegistryKey(string registryPath, out IntPtr keyHandle)
    {
        const int KEY_NOTIFY = 0x0010;
        var result = RegOpenKeyEx(HKEY_CURRENT_USER, registryPath, 0, KEY_NOTIFY, out keyHandle);
        return result == 0 && keyHandle != IntPtr.Zero;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyEx(UIntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        IntPtr hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        RegChangeNotifyFilter dwNotifyFilter,
        IntPtr hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern WaitObjectResult WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly UIntPtr HKEY_CURRENT_USER = (UIntPtr)0x80000001u;

    [Flags]
    private enum RegChangeNotifyFilter : uint
    {
        Name = 0x00000001,
        Attribute = 0x00000002,
        Value = 0x00000004,
        Security = 0x00000008,
        Key = Name
    }

    private enum WaitObjectResult : uint
    {
        WaitObject0 = 0x00000000,
        WaitTimeout = 0x00000102,
        WaitFailed = 0xFFFFFFFF
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINHTTP_CURRENT_USER_IE_PROXY_CONFIG
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAutoDetect;

        public IntPtr lpszAutoConfigUrl;
        public IntPtr lpszProxy;
        public IntPtr lpszProxyBypass;
    }

    private static bool IsForegroundFullscreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return false;
            }

            var hMonitor = MonitorFromWindow(hwnd, 2u);
            if (hMonitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MONITORINFO
            {
                cbSize = Marshal.SizeOf<MONITORINFO>()
            };

            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                return false;
            }

            var windowWidth = rect.Right - rect.Left;
            var windowHeight = rect.Bottom - rect.Top;
            var monitorWidth = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left;
            var monitorHeight = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;

            return Math.Abs(windowWidth - monitorWidth) <= 2 &&
                   Math.Abs(windowHeight - monitorHeight) <= 2 &&
                   rect.Left == monitorInfo.rcMonitor.Left &&
                   rect.Top == monitorInfo.rcMonitor.Top;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyGame(string? foregroundProcessName, string? foregroundWindowTitle)
    {
        return AutomationGameRecognitionCatalog.MatchesKnownGame(foregroundProcessName) ||
               AutomationGameRecognitionCatalog.MatchesKnownGame(foregroundWindowTitle);
    }

    private void SchedulePerformanceModeEvaluation()
    {
        if (_started)
        {
            _observationProcessor.EvaluatePerformanceMode();
        }
    }

    private static int? GetForegroundProcessId()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out var processId);
            return processId == 0 ? null : (int)processId;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, string lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private const int WmWtsSessionChange = 0x02B1;
    private const int WtsSessionLock = 0x0007;
    private const int WtsSessionUnlock = 0x0008;
    private const int NotifyForThisSession = 0;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

}
