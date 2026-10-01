namespace BluetoothAudioReceiver.Automation.Models;

public sealed record AutomationExecutionContext(
    Guid ExecutionId,
    Guid PipelineId,
    string PipelineName,
    string TriggerId,
    DateTimeOffset TriggeredAt,
    Guid CorrelationId = default);
