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
    private readonly IAppSettingsService _settingsService;

    public AutomationPipelineExecutor(
        IAutomationSystemContextHub systemContextHub,
        INotificationService notificationService,
        IAutomationActionRegistry actionRegistry,
        IAppSettingsService settingsService)
    {
        _systemContextHub = systemContextHub;
        _notificationService = notificationService;
        _actionRegistry = actionRegistry;
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
        var succeededResourceScope = AutomationActionResourceScope.None;
        var finalResult = "completed";
        var stopwatch = Stopwatch.StartNew();

        _log.Information(
            "Automation pipeline execution started. Pipeline={PipelineName}, StepCount={StepCount}, CorrelationId={CorrelationId}",
            pipeline.Name,
            pipeline.Steps.Count,
            executionContext.CorrelationId);

        for (var index = 0; index < pipeline.Steps.Count; index++)
        {
            var step = pipeline.Steps[index];
            if (string.Equals(step.ActionType, AutomationSupportCatalog.ActionDelaySeconds, StringComparison.OrdinalIgnoreCase))
            {
                var seconds = AutomationActionParameterHelper.ReadDelaySeconds(step.ParametersJson, 0);
                if (seconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                }

                succeeded++;
                continue;
            }

            if (string.Equals(step.ActionType, AutomationSupportCatalog.ActionStopExecution, StringComparison.OrdinalIgnoreCase))
            {
                succeeded++;
                break;
            }

            var provider = _actionRegistry.GetProvider(step.ActionType);
            if (provider is null)
            {
                failed++;
                finalResult = "failed";
                _log.Warning("Automation action provider is missing. ActionType={ActionType}", step.ActionType);
                if (pipeline.Options.FailurePolicy != AutomationFailurePolicy.Continue)
                {
                    finalResult = "aborted";
                    break;
                }

                continue;
            }

            try
            {
                await provider.ExecuteAsync(
                    new AutomationActionDefinition
                    {
                        ActionType = step.ActionType,
                        ParametersJson = step.ParametersJson
                    },
                    triggerEvent,
                    executionContext,
                    context);

                succeeded++;
                succeededResourceScope |= _actionRegistry.GetCapability(step.ActionType)?.ResourceScope
                    ?? AutomationActionResourceScope.None;
                _log.Information(
                    "Automation action completed. ActionType={ActionType}, Step={Step}/{StepCount}, CorrelationId={CorrelationId}",
                    step.ActionType,
                    index + 1,
                    pipeline.Steps.Count,
                    executionContext.CorrelationId);
            }
            catch (Exception ex)
            {
                failed++;
                finalResult = "failed";
                _log.Warning(ex, "Automation action failed. ActionType={ActionType}, Step={Step}/{StepCount}", step.ActionType, index + 1, pipeline.Steps.Count);

                if (pipeline.Options.FailurePolicy == AutomationFailurePolicy.Retry)
                {
                    try
                    {
                        await provider.ExecuteAsync(
                            new AutomationActionDefinition
                            {
                                ActionType = step.ActionType,
                                ParametersJson = step.ParametersJson
                            },
                            triggerEvent,
                            executionContext,
                            context);

                        failed--;
                        succeeded++;
                        finalResult = "completed";
                        succeededResourceScope |= _actionRegistry.GetCapability(step.ActionType)?.ResourceScope
                            ?? AutomationActionResourceScope.None;
                    }
                    catch (Exception retryException)
                    {
                        _log.Warning(retryException, "Automation action retry failed. ActionType={ActionType}", step.ActionType);
                        finalResult = "aborted";
                        break;
                    }
                }
                else if (pipeline.Options.FailurePolicy == AutomationFailurePolicy.Abort)
                {
                    finalResult = "aborted";
                    break;
                }
            }

            if (pipeline.Options.StepIntervalSeconds > 0 && index < pipeline.Steps.Count - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(pipeline.Options.StepIntervalSeconds));
            }
        }

        stopwatch.Stop();
        if (finalResult == "completed" && succeeded != pipeline.Steps.Count)
        {
            finalResult = "partial";
        }

        _log.Information(
            "Automation pipeline execution finished. Result={Result}, Succeeded={Succeeded}, Failed={Failed}, DurationMs={DurationMs}, CorrelationId={CorrelationId}",
            finalResult,
            succeeded,
            failed,
            stopwatch.ElapsedMilliseconds,
            executionContext.CorrelationId);
        NotifyPipelineResult(pipeline, failed, finalResult, executionContext);
        return new AutomationPipelineExecutionResult(succeededResourceScope, finalResult, succeeded, failed);
    }

    private void NotifyPipelineResult(
        AutomationPipeline pipeline,
        int failed,
        string finalResult,
        AutomationExecutionContext executionContext)
    {
        if ((failed == 0 && !pipeline.Options.NotifyOnSuccess) ||
            (failed > 0 && !pipeline.Options.NotifyOnFailure) ||
            !_settingsService.AutomationExecutionNotificationEnabled)
        {
            return;
        }

        _notificationService.ShowAutomation(
            pipeline.Name,
            AutomationNotificationFormatter.ResolveAutomationStatusText(finalResult),
            AutomationNotificationFormatter.BuildAutomationDetailLines(pipeline, finalResult),
            executionContext.CorrelationId);
    }
}

internal sealed record AutomationPipelineExecutionResult(
    AutomationActionResourceScope BlockedResourceScope,
    string Result,
    int SucceededStepCount,
    int FailedStepCount);
