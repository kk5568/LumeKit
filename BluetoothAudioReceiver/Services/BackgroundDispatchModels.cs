using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Services;

public enum BackgroundDispatchMessageType
{
    StartupSnapshot,
    SystemSignal,
    ForegroundWindowChanged,
    ThemeChanged,
    ProgramListRefresh,
    GameModeChanged
}

public enum BackgroundDispatchScene
{
    Startup,
    Unlock,
    LightWork,
    HeavyWork,
    VideoPlayback,
    Game,
    LowBattery,
    Locked
}

public enum BackgroundDispatchInfoPriority
{
    P0,
    P1,
    P2,
    P3
}

public sealed record BackgroundDispatchMessage(
    BackgroundDispatchMessageType MessageType,
    string Reason,
    BackgroundDispatchScene Scene,
    DateTimeOffset OccurredAt,
    SystemContext Context,
    IReadOnlyDictionary<string, string?> Payload,
    Guid CorrelationId);

public interface IBackgroundDispatchMessageHandler
{
    Task HandleDispatchMessageAsync(BackgroundDispatchMessage message, CancellationToken cancellationToken = default);
}

public interface IBackgroundDispatchBus
{
    Task PublishAsync(BackgroundDispatchMessage message, CancellationToken cancellationToken = default);
}

public sealed class BackgroundDispatchBus : IBackgroundDispatchBus
{
    private static readonly Serilog.ILogger _log = Serilog.Log.ForContext("SourceContext", nameof(BackgroundDispatchBus));
    private readonly IEnumerable<IBackgroundDispatchMessageHandler> _handlers;

    public BackgroundDispatchBus(IEnumerable<IBackgroundDispatchMessageHandler> handlers)
    {
        _handlers = handlers;
    }

    public async Task PublishAsync(BackgroundDispatchMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _log.Debug(
            "Background dispatch message published. Type={MessageType}, Reason={Reason}, Scene={Scene}, CorrelationId={CorrelationId}, PayloadKeys={PayloadKeys}",
            message.MessageType,
            message.Reason,
            message.Scene,
            message.CorrelationId,
            string.Join(",", message.Payload.Keys));

        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleDispatchMessageAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warning(
                    ex,
                    "Background dispatch handler failed. Handler={Handler}, Type={MessageType}, Reason={Reason}, CorrelationId={CorrelationId}",
                    handler.GetType().Name,
                    message.MessageType,
                    message.Reason,
                    message.CorrelationId);
            }
        }
    }
}
