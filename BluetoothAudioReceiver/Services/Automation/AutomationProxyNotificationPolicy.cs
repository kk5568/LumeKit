using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 决策层：代理状态变化时是否弹提示、弹什么内容。
/// 观察层只负责发布 proxy 变化事件，不再自己决定通知。
/// </summary>
internal sealed class AutomationProxyNotificationPolicy : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationProxyNotificationPolicy));

    private readonly IAutomationTriggerBus _triggerBus;
    private readonly IAppSettingsService _appSettingsService;
    private readonly INotificationService _notificationService;
    private bool _disposed;

    public AutomationProxyNotificationPolicy(
        IAutomationTriggerBus triggerBus,
        IAppSettingsService appSettingsService,
        INotificationService notificationService)
    {
        _triggerBus = triggerBus;
        _appSettingsService = appSettingsService;
        _notificationService = notificationService;
        _triggerBus.TriggerPublished += OnTriggerPublished;
    }

    private void OnTriggerPublished(object? sender, TriggerEvent triggerEvent)
    {
        if (_disposed)
        {
            return;
        }

        if (!string.Equals(triggerEvent.TriggerType, AutomationSupportCatalog.TriggerSystemSignal, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!triggerEvent.Payload.TryGetValue("reason", out var reason) ||
            !string.Equals(reason, "network-proxy-changed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!triggerEvent.Payload.TryGetValue("currentProxyEnabled", out var currentText) ||
            !bool.TryParse(currentText, out var currentEnabled))
        {
            return;
        }

        bool? previousEnabled = null;
        if (triggerEvent.Payload.TryGetValue("previousProxyEnabled", out var previousText) &&
            bool.TryParse(previousText, out var parsedPreviousEnabled))
        {
            previousEnabled = parsedPreviousEnabled;
        }

        var previousMode = ReadPayload(triggerEvent, "previousProxyMode");
        var currentMode = ReadPayload(triggerEvent, "currentProxyMode");

        if (previousEnabled == currentEnabled &&
            string.Equals(previousMode, currentMode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_appSettingsService.ProxyStatusNotificationEnabled)
        {
            return;
        }

        if (string.Equals(ReadPayload(triggerEvent, "observationReason"), "startup", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var title = GetProxyModeName(currentEnabled ? currentMode : previousMode);
        var status = GetProxyStatusText(currentEnabled);
        _log.Information(
            "Automation proxy notification policy accepted proxy status change. PreviousEnabled={PreviousEnabled}, CurrentEnabled={CurrentEnabled}, Title={Title}, CorrelationId={CorrelationId}",
            previousEnabled,
            currentEnabled,
            title,
            triggerEvent.CorrelationId);

        _notificationService.Show(NotificationRequest.Standard(
            "\uE128",
            title,
            status,
            bypassSuppression: true,
            category: "proxy-status"));
    }

    private static string? ReadPayload(TriggerEvent triggerEvent, string key)
        => triggerEvent.Payload.TryGetValue(key, out var value) ? value : null;

    private static string GetProxyStatusText(bool current)
    {
        return current ? "已开启" : "已关闭";
    }

    private static string GetProxyModeName(string? mode)
        => string.Equals(mode, "tun", StringComparison.OrdinalIgnoreCase) ? "TUN 模式" : "系统代理";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _triggerBus.TriggerPublished -= OnTriggerPublished;
        _log.Information("Automation proxy notification policy unsubscribed from automation trigger bus");
    }
}
