using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationObservationPublisher : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationObservationPublisher));

    private readonly IAutomationTriggerBus _triggerBus;
    private readonly IBackgroundDispatchBus _backgroundDispatchBus;
    private readonly IAutomationSystemContextHub _systemContextHub;
    private readonly IAppSettingsService _appSettingsService;
    private readonly INotificationService _notificationService;

    public AutomationObservationPublisher(
        IAutomationTriggerBus triggerBus,
        IBackgroundDispatchBus backgroundDispatchBus,
        IAutomationSystemContextHub systemContextHub,
        IAppSettingsService appSettingsService,
        INotificationService notificationService)
    {
        _triggerBus = triggerBus;
        _backgroundDispatchBus = backgroundDispatchBus;
        _systemContextHub = systemContextHub;
        _appSettingsService = appSettingsService;
        _notificationService = notificationService;
    }

    internal SemaphoreSlim PublishGate { get; } = new(1, 1);

    public void PublishNetworkEvents(AutomationDynamicContext previous, AutomationDynamicContext current, ObservationRequest request)
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

    public void PublishPowerStateEvent(
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

    public void PublishBluetoothEvents(
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

    public void PublishAudioEvents(
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

    public void PublishThemeChangedEvent(
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

    public void PublishMainlineSystemSignal(string reason, Guid correlationId = default)
    {
        _log.Debug(
            "Automation observation publishing mainline system signal. Reason={Reason}, CorrelationId={CorrelationId}",
            reason,
            correlationId);
        PublishSystemSignal(reason, correlationId);
    }

    public void PublishMainlineForegroundWindowChanged(IntPtr hwnd, Guid correlationId = default)
    {
        var processName = ForegroundWindowProbe.GetForegroundProcessName();
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

    public void PublishSystemSignal(
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

    public void PublishBackgroundDispatchMessage(
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

    public void PublishProxyStatusNotificationIfNeeded(
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

    public void Dispose()
    {
        PublishGate.Dispose();
    }

    private static string GetProxyStatusText(bool current)
    {
        return current ? "已开启" : "已关闭";
    }

    private static string GetProxyModeName(string? mode)
        => string.Equals(mode, "tun", StringComparison.OrdinalIgnoreCase) ? "TUN 模式" : "系统代理";
}
