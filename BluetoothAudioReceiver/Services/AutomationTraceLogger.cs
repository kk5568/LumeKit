using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Infrastructure.Helpers;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal static class AutomationTraceLogger
{
    public static ILogger CreateExecutionLogger(ILogger logger, AutomationExecutionContext executionContext)
    {
        return logger
            .ForContext("ExecutionId", executionContext.ExecutionId)
            .ForContext("PipelineId", executionContext.PipelineId)
            .ForContext("PipelineName", executionContext.PipelineName)
            .ForContext("TriggerId", executionContext.TriggerId)
            .ForContext("CorrelationId", executionContext.CorrelationId);
    }

    public static ILogger CreateStepLogger(ILogger logger, AutomationExecutionContext executionContext, string? actionId, int stepIndex, int stepCount)
    {
        return CreateExecutionLogger(logger, executionContext)
            .ForContext("ActionId", actionId)
            .ForContext("StepIndex", stepIndex)
            .ForContext("StepCount", stepCount);
    }

    public static AutomationExecutionTrace CreateTrace(
        AutomationExecutionContext executionContext,
        string result,
        DateTimeOffset timestamp,
        long? durationMs = null,
        string? actionId = null,
        int? stepIndex = null,
        int? stepCount = null,
        string? message = null,
        string? parametersJson = null,
        IReadOnlyDictionary<string, string?>? triggerPayload = null,
        Exception? exception = null)
    {
        return new AutomationExecutionTrace(
            executionContext.ExecutionId,
            executionContext.PipelineId,
            executionContext.PipelineName,
            executionContext.TriggerId,
            actionId,
            timestamp,
            durationMs,
            result,
            stepIndex,
            stepCount,
            message,
            parametersJson,
            triggerPayload is null ? null : JsonHelper.Stringify(triggerPayload),
            exception?.GetType().FullName,
            exception?.Message,
            exception?.ToString(),
            executionContext.CorrelationId);
    }
}
