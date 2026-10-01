namespace BluetoothAudioReceiver.Automation.Models;

public sealed record AutomationExecutionTrace(
    Guid ExecutionId,
    Guid PipelineId,
    string PipelineName,
    string TriggerId,
    string? ActionId,
    DateTimeOffset Timestamp,
    long? DurationMs,
    string Result,
    int? StepIndex = null,
    int? StepCount = null,
    string? Message = null,
    string? ParametersJson = null,
    string? TriggerPayloadJson = null,
    string? ExceptionType = null,
    string? ExceptionMessage = null,
    string? ExceptionStackTrace = null,
    Guid CorrelationId = default);
