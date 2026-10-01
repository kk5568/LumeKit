namespace BluetoothAudioReceiver.Automation.Models;

public sealed record TriggerEvent(
    string TriggerType,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string?> Payload,
    Guid CorrelationId = default);
