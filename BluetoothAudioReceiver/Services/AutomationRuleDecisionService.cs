using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationRuleDecisionService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationRuleDecisionService));
    private readonly IAutomationConditionRegistry _conditionRegistry;
    private readonly IAutomationActionRegistry _actionRegistry;

    public AutomationRuleDecisionService(
        IAutomationConditionRegistry conditionRegistry,
        IAutomationActionRegistry actionRegistry)
    {
        _conditionRegistry = conditionRegistry;
        _actionRegistry = actionRegistry;
    }

    public bool EvaluateConditions(
        IReadOnlyList<AutomationConditionDefinition> conditions,
        TriggerEvent triggerEvent,
        SystemContext context)
    {
        if (conditions.Count == 0)
        {
            return true;
        }

        var result = false;
        var initialized = false;
        foreach (var condition in conditions)
        {
            if (!_conditionRegistry.IsSupported(condition.ConditionType))
            {
                _log.Warning("Automation condition is unsupported. ConditionType={ConditionType}", condition.ConditionType);
                return false;
            }

            var current = _conditionRegistry.Evaluate(condition, triggerEvent, context);
            if (!initialized || string.Equals(condition.OperatorType, "If", StringComparison.OrdinalIgnoreCase))
            {
                result = current;
                initialized = true;
            }
            else if (string.Equals(condition.OperatorType, "And", StringComparison.OrdinalIgnoreCase))
            {
                result = result && current;
            }
            else if (string.Equals(condition.OperatorType, "Or", StringComparison.OrdinalIgnoreCase))
            {
                result = result || current;
            }
            else if (string.Equals(condition.OperatorType, "Else", StringComparison.OrdinalIgnoreCase))
            {
                result = !result && current;
            }
        }

        return result;
    }

    public bool ShouldSkip(
        AutomationPipeline pipeline,
        DateTimeOffset occurredAt,
        DateTimeOffset appStartedAt,
        IReadOnlyDictionary<Guid, DateTimeOffset> lastRunAt,
        out string result,
        out string message)
    {
        if (!IsWithinRunTime(pipeline.Options.RunTimeMode))
        {
            result = "outside-time-range";
            message = "当前不在规则允许的时间段";
            return true;
        }

        if (pipeline.Options.OnceOnlyEnabled && lastRunAt.ContainsKey(pipeline.Id))
        {
            result = "once-only";
            message = "规则只允许执行一次";
            return true;
        }

        if (!pipeline.Options.AllowRepeatedExecution && lastRunAt.ContainsKey(pipeline.Id))
        {
            result = "repeat-disabled";
            message = "规则不允许重复执行";
            return true;
        }

        var cooldownSeconds = pipeline.Options.CooldownSeconds;
        var cooldownStartedAt = lastRunAt.TryGetValue(pipeline.Id, out var lastRun)
            ? lastRun
            : appStartedAt;

        if (cooldownSeconds > 0 &&
            occurredAt - cooldownStartedAt < TimeSpan.FromSeconds(cooldownSeconds))
        {
            result = "cooldown";
            message = $"冷却中：{FormatCooldownDuration(cooldownSeconds)}";
            return true;
        }

        result = string.Empty;
        message = string.Empty;
        return false;
    }

    private static string FormatCooldownDuration(double seconds)
    {
        if (seconds >= 3600 && seconds % 3600 == 0)
        {
            return $"{seconds / 3600:0} 小时";
        }

        if (seconds >= 60 && seconds % 60 == 0)
        {
            return $"{seconds / 60:0} 分钟";
        }

        return $"{seconds:0} 秒";
    }

    public AutomationActionResourceScope GetResourceScope(AutomationPipeline pipeline)
    {
        var scope = AutomationActionResourceScope.None;
        foreach (var step in pipeline.Steps)
        {
            scope |= _actionRegistry.GetCapability(step.ActionType)?.ResourceScope ?? AutomationActionResourceScope.None;
        }

        return scope;
    }

    public bool ValidateActions(AutomationPipeline pipeline, out string message)
    {
        foreach (var step in pipeline.Steps)
        {
            if (!_actionRegistry.IsSupported(step.ActionType))
            {
                message = $"动作类型不受支持：{step.ActionType}";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    public static void NormalizeFailurePolicy(AutomationPipeline pipeline)
    {
        if ((int)pipeline.Options.FailurePolicy > (int)AutomationFailurePolicy.Abort)
        {
            pipeline.Options.FailurePolicy = AutomationFailurePolicy.Abort;
        }
    }

    private static bool IsWithinRunTime(string? runTimeMode)
    {
        var hour = DateTime.Now.Hour;
        return runTimeMode switch
        {
            "day" => hour >= 7 && hour < 19,
            "night" => hour < 7 || hour >= 19,
            _ => true
        };
    }
}
