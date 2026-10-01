using Microsoft.UI.Xaml;
using Serilog;

namespace BluetoothAudioReceiver.Services;

public enum AutomationMainlineMessageKind
{
    SystemSignal,
    ForegroundWindowChanged,
    ThemeChanged
}

public sealed record AutomationMainlineMessage(
    AutomationMainlineMessageKind Kind,
    DateTimeOffset Timestamp,
    string? Reason = null,
    string? ProcessName = null,
    IntPtr WindowHandle = default,
    ElementTheme? Theme = null,
    Guid CorrelationId = default);

public interface IAutomationMainlineBus
{
    event EventHandler<AutomationMainlineMessage>? MessagePublished;

    void Publish(AutomationMainlineMessage message);
}

public sealed class AutomationMainlineBus : IAutomationMainlineBus
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationMainlineBus));

    public event EventHandler<AutomationMainlineMessage>? MessagePublished;

    public void Publish(AutomationMainlineMessage message)
    {
        if (message.CorrelationId == Guid.Empty)
        {
            message = message with { CorrelationId = Guid.NewGuid() };
        }

        _log.Debug(
            "Mainline message published. Kind={Kind}, Reason={Reason}, ProcessName={ProcessName}, WindowHandle={WindowHandle}, Theme={Theme}, CorrelationId={CorrelationId}",
            message.Kind,
            message.Reason,
            message.ProcessName,
            message.WindowHandle,
            message.Theme,
            message.CorrelationId);

        MessagePublished?.Invoke(this, message);
    }
}
