using System.Diagnostics;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationPipelineExecutor
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationPipelineExecutor));
    private readonly IAutomationSystemContextHub _systemContextHub;
    private readonly INotificationService _notificationService;
    private readonly IAutomationActionRegistry _actionRegistry;
    private readonly IAutomationTraceStore _traceStore;
    private readonly IAppSettingsService _settingsService;

    public AutomationPipelineExecutor(
        IAutomationSystemContextHub systemContextHub,
        INotificationService notificationService,
        IAutomationActionRegistry actionRegistry,
        IAutomationTraceStore traceStore,
        IAppSettingsService settingsService)
    {
        _systemContextHub = systemContextHub;
        _notificationService = notificationService;
        _actionRegistry = actionRegistry;
        _traceStore = traceStore;
        _settingsService = settingsService;
    }

    public async Task<AutomationPipelineExecutionResult> ExecuteAsync(
        AutomationPipeline pipeline,
        TriggerEvent triggerEvent,
        AutomationExecutionContext executionContext)
    {
        var context = _systemContextHub.Current with { SuppressActionNotifications = true };
        var succeeded = 0;
        var failed = 0;
        var pipelineStopwatch = Stopwatch.StartNew();
        var executionLogger = AutomationTraceLogger.CreateExecutionLogger(_log, executionContext);
        var rollbackKeys = new Stack<string>();
        var succeededResourceScope = AutomationActionResourceScope.None;
        var finalResult = "completed";

        executionLogger.Information(
            "Automation pipeline execution started. StepCount={StepCount}, TriggerOccurredAt={TriggerOccurredAt}",
            pipeline.Steps.Count,
            triggerEvent.OccurredAt);
        AppendTrace(executionContext, triggerEvent, "started", "规则开始执行");

        for (var stepIndex = 0; stepIndex < pipeline.Steps.Count; stepIndex++)
        {
            var step = pipeline.Steps[stepIndex];
            var currentStep = stepIndex + 1;
            var stepLogger = AutomationTraceLogger.CreateStepLogger(_log, executionContext, step.ActionType, currentStep, pipeline.Steps.Count);

            if (!_actionRegistry.IsSupported(step.ActionType))
            {
                AppendStepTrace(executionContext, triggerEvent, step, currentStep, pipeline.Steps.Count, "unsupported", "动作类型不受支持");
                stepLogger.Warning("Automation step skipped because action type is unsupported. ActionType={ActionType}", step.ActionType);
                continue;
            }

            if (string.Equals(step.ActionType, AutomationSupportCatalog.ActionDelaySeconds, StringComparison.OrdinalIgnoreCase))
            {
                var delaySeconds = AutomationActionParameterHelper.ReadDelaySeconds(step.ParametersJson, 0);
                var delayStopwatch = Stopwatch.StartNew();
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }

                delayStopwatch.Stop();
                succeeded++;
                AppendStepTrace(
                    executionContext,
                    triggerEvent,
                    step,
                    currentStep,
                    pipeline.Steps.Count,
                    "completed",
                    $"等待 {delaySeconds} 秒完成",
                    delayStopwatch.ElapsedMilliseconds);
                continue;
            }

            if (string.Equals(step.ActionType, AutomationSupportCatalog.ActionStopExecution, StringComparison.OrdinalIgnoreCase))
            {
                AppendStepTrace(executionContext, triggerEvent, step, currentStep, pipeline.Steps.Count, "stopped", "执行到停止动作");
                succeeded++;
                break;
            }

            var provider = _actionRegistry.GetProvider(step.ActionType);
            if (provider is null)
            {
                AppendStepTrace(executionContext, triggerEvent, step, currentStep, pipeline.Steps.Count, "provider-missing", "动作提供者不存在");
                failed++;
                finalResult = "failed";

                var policyOutcome = await HandleFailurePolicyAsync(
                    pipeline,
                    step,
                    triggerEvent,
                    executionContext,
                    context,
                    rollbackKeys,
                    currentStep,
                    pipeline.Steps.Count,
                    stepLogger,
                    new InvalidOperationException($"Automation action provider is missing: {step.ActionType}"));

                if (policyOutcome.ShouldCountAsSuccess)
                {
                    TrackRollbackKey(step, rollbackKeys);
                    succeeded++;
                    succeededResourceScope |= _actionRegistry.GetCapability(step.ActionType)?.ResourceScope ?? AutomationActionResourceScope.None;
                    failed--;
                    finalResult = "completed";
                }
                else if (policyOutcome.Outcome is AutomationFailurePolicy.Abort or AutomationFailurePolicy.Rollback)
                {
                    finalResult = policyOutcome.Outcome == AutomationFailurePolicy.Rollback ? "rolled-back" : "aborted";
                    break;
                }

                continue;
            }

            try
            {
                var stepStopwatch = Stopwatch.StartNew();
                await provider.ExecuteAsync(
                    new AutomationActionDefinition
                    {
                        ActionType = step.ActionType,
                        ParametersJson = step.ParametersJson
                    },
                    triggerEvent,
                    executionContext,
                    context);

                stepStopwatch.Stop();
                TrackRollbackKey(step, rollbackKeys);
                succeeded++;
                succeededResourceScope |= _actionRegistry.GetCapability(step.ActionType)?.ResourceScope ?? AutomationActionResourceScope.None;
                AppendStepTrace(
                    executionContext,
                    triggerEvent,
                    step,
                    currentStep,
                    pipeline.Steps.Count,
                    "succeeded",
                    "动作执行成功",
                    stepStopwatch.ElapsedMilliseconds);

                if (pipeline.Options.StepIntervalSeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(pipeline.Options.StepIntervalSeconds));
                }
            }
            catch (Exception ex)
            {
                AppendStepTrace(executionContext, triggerEvent, step, currentStep, pipeline.Steps.Count, "failed", "动作执行失败", exception: ex);
                failed++;
                finalResult = "failed";

                var policyOutcome = await HandleFailurePolicyAsync(
                    pipeline,
                    step,
                    triggerEvent,
                    executionContext,
                    context,
                    rollbackKeys,
                    currentStep,
                    pipeline.Steps.Count,
                    stepLogger,
                    ex);

                if (policyOutcome.ShouldCountAsSuccess)
                {
                    TrackRollbackKey(step, rollbackKeys);
                    succeeded++;
                    succeededResourceScope |= _actionRegistry.GetCapability(step.ActionType)?.ResourceScope ?? AutomationActionResourceScope.None;
                    failed--;
                    finalResult = "completed";
                    continue;
                }

                if (policyOutcome.Outcome is AutomationFailurePolicy.Abort or AutomationFailurePolicy.Rollback)
                {
                    finalResult = policyOutcome.Outcome == AutomationFailurePolicy.Rollback ? "rolled-back" : "aborted";
                    break;
                }
            }
        }

        pipelineStopwatch.Stop();
        var result = finalResult == "completed" && succeeded != pipeline.Steps.Count ? "partial" : finalResult;
        AppendTrace(
            executionContext,
            triggerEvent,
            result,
            $"执行结束：成功 {succeeded}，失败 {failed}",
            durationMs: pipelineStopwatch.ElapsedMilliseconds,
            stepCount: pipeline.Steps.Count);

        NotifyPipelineResult(pipeline, failed, finalResult, executionContext);
        var blockedResourceScope = finalResult == "rolled-back"
            ? AutomationActionResourceScope.None
            : succeededResourceScope;

        return new AutomationPipelineExecutionResult(blockedResourceScope, finalResult, succeeded, failed);
    }

    private void NotifyPipelineResult(
        AutomationPipeline pipeline,
        int failed,
        string finalResult,
        AutomationExecutionContext executionContext)
    {
        if (failed == 0 && !pipeline.Options.NotifyOnSuccess)
        {
            return;
        }

        if (failed > 0 && !pipeline.Options.NotifyOnFailure)
        {
            return;
        }

        if (!_settingsService.AutomationExecutionNotificationEnabled)
        {
            return;
        }

        _notificationService.ShowAutomation(
            pipeline.Name,
            AutomationNotificationFormatter.ResolveAutomationStatusText(finalResult),
            AutomationNotificationFormatter.BuildAutomationDetailLines(pipeline, finalResult),
            executionContext.CorrelationId);
    }

    private async Task<AutomationFailureHandlingResult> HandleFailurePolicyAsync(
        AutomationPipeline pipeline,
        AutomationStepDefinition failedStep,
        TriggerEvent triggerEvent,
        AutomationExecutionContext executionContext,
        SystemContext context,
        Stack<string> rollbackKeys,
        int stepIndex,
        int stepCount,
        ILogger stepLogger,
        Exception exception)
    {
        switch (pipeline.Options.FailurePolicy)
        {
            case AutomationFailurePolicy.Continue:
                return new AutomationFailureHandlingResult(AutomationFailurePolicy.Continue, false);

            case AutomationFailurePolicy.Retry:
                AppendStepTrace(executionContext, triggerEvent, failedStep, stepIndex, stepCount, "retry-started", "开始重试", exception: exception);
                if (await RetryFailedStepAsync(failedStep, triggerEvent, executionContext, context, stepLogger))
                {
                    AppendStepTrace(executionContext, triggerEvent, failedStep, stepIndex, stepCount, "retry-succeeded", "重试成功");
                    return new AutomationFailureHandlingResult(AutomationFailurePolicy.Continue, true);
                }

                AppendStepTrace(executionContext, triggerEvent, failedStep, stepIndex, stepCount, "retry-failed", "重试仍然失败", exception: exception);
                stepLogger.Warning(exception, "Automation failure policy retry failed again. StepIndex={StepIndex}/{StepCount}", stepIndex, stepCount);
                return new AutomationFailureHandlingResult(AutomationFailurePolicy.Abort, false);

            case AutomationFailurePolicy.Rollback:
                AppendStepTrace(executionContext, triggerEvent, failedStep, stepIndex, stepCount, "rollback-started", "开始回滚", exception: exception);
                var rollbackSucceeded = await RollbackAsync(triggerEvent, executionContext, context, rollbackKeys, stepLogger);
                AppendStepTrace(
                    executionContext,
                    triggerEvent,
                    failedStep,
                    stepIndex,
                    stepCount,
                    rollbackSucceeded ? "rollback-completed" : "rollback-failed",
                    rollbackSucceeded ? "回滚完成" : "回滚部分失败",
                    exception: exception);
                return new AutomationFailureHandlingResult(AutomationFailurePolicy.Rollback, false);

            case AutomationFailurePolicy.Abort:
            default:
                return new AutomationFailureHandlingResult(AutomationFailurePolicy.Abort, false);
        }
    }

    private async Task<bool> RetryFailedStepAsync(
        AutomationStepDefinition failedStep,
        TriggerEvent triggerEvent,
        AutomationExecutionContext executionContext,
        SystemContext context,
        ILogger stepLogger)
    {
        var provider = _actionRegistry.GetProvider(failedStep.ActionType);
        if (provider is null)
        {
            return false;
        }

        try
        {
            await provider.ExecuteAsync(
                new AutomationActionDefinition
                {
                    ActionType = failedStep.ActionType,
                    ParametersJson = failedStep.ParametersJson
                },
                triggerEvent,
                executionContext,
                context);
            return true;
        }
        catch (Exception retryEx)
        {
            stepLogger.Warning(retryEx, "Automation retry execution failed.");
            return false;
        }
    }

    private async Task<bool> RollbackAsync(
        TriggerEvent triggerEvent,
        AutomationExecutionContext executionContext,
        SystemContext context,
        Stack<string> rollbackKeys,
        ILogger stepLogger)
    {
        var restoreProvider = _actionRegistry.GetProvider(AutomationSupportCatalog.ActionRestoreState);
        if (restoreProvider is null)
        {
            stepLogger.Warning("Automation rollback skipped because restore provider is unavailable.");
            return false;
        }

        var succeeded = true;
        while (rollbackKeys.Count > 0)
        {
            var rollbackKey = rollbackKeys.Pop();
            try
            {
                await restoreProvider.ExecuteAsync(
                    new AutomationActionDefinition
                    {
                        ActionType = AutomationSupportCatalog.ActionRestoreState,
                        ParametersJson = AutomationActionParameterHelper.BuildSnapshotKey(rollbackKey)
                    },
                    triggerEvent,
                    executionContext,
                    context);
                _log.Information(
                    "Automation rollback item completed. SnapshotKey={SnapshotKey}, ExecutionId={ExecutionId}, CorrelationId={CorrelationId}",
                    rollbackKey,
                    executionContext.ExecutionId,
                    executionContext.CorrelationId);
            }
            catch (Exception rollbackEx)
            {
                succeeded = false;
                stepLogger.Warning(rollbackEx, "Automation rollback restore failed. SnapshotKey={SnapshotKey}", rollbackKey);
            }
        }

        return succeeded;
    }

    private void AppendStepTrace(
        AutomationExecutionContext executionContext,
        TriggerEvent triggerEvent,
        AutomationStepDefinition step,
        int stepIndex,
        int stepCount,
        string result,
        string message,
        long? durationMs = null,
        Exception? exception = null)
        => AppendTrace(
            executionContext,
            triggerEvent,
            result,
            message,
            step.ActionType,
            stepIndex,
            stepCount,
            durationMs,
            step.ParametersJson,
            exception);

    private void AppendTrace(
        AutomationExecutionContext executionContext,
        TriggerEvent triggerEvent,
        string result,
        string message,
        string? actionId = null,
        int? stepIndex = null,
        int? stepCount = null,
        long? durationMs = null,
        string? parametersJson = null,
        Exception? exception = null)
    {
        _traceStore.Append(AutomationTraceLogger.CreateTrace(
            executionContext,
            result,
            DateTimeOffset.Now,
            durationMs,
            actionId,
            stepIndex,
            stepCount,
            message,
            parametersJson,
            triggerEvent.Payload,
            exception));
    }

    private static void TrackRollbackKey(AutomationStepDefinition step, Stack<string> rollbackKeys)
    {
        if (!string.Equals(step.ActionType, AutomationSupportCatalog.ActionCaptureState, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = AutomationActionParameterHelper.ReadValue(step.ParametersJson);
        if (!string.IsNullOrWhiteSpace(key))
        {
            rollbackKeys.Push(key);
        }
    }
}

internal sealed record AutomationPipelineExecutionResult(
    AutomationActionResourceScope BlockedResourceScope,
    string Result,
    int SucceededStepCount,
    int FailedStepCount);

internal sealed record AutomationFailureHandlingResult(
    AutomationFailurePolicy Outcome,
    bool ShouldCountAsSuccess);
