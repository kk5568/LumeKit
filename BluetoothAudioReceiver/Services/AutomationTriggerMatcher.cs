using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationTriggerMatcher
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationTriggerMatcher));
    private readonly IAutomationTriggerRegistry _triggerRegistry;
    private readonly IAppSettingsService _settingsService;

    public AutomationTriggerMatcher(
        IAutomationTriggerRegistry triggerRegistry,
        IAppSettingsService settingsService)
    {
        _triggerRegistry = triggerRegistry;
        _settingsService = settingsService;
    }

    public bool Matches(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent)
        => TryMatch(trigger, triggerEvent, out _);

    public bool TryMatch(
        AutomationTriggerDefinition trigger,
        TriggerEvent triggerEvent,
        out string reason)
    {
        reason = string.Empty;
        var normalizedTrigger = _triggerRegistry.Normalize(trigger.TriggerType);
        var normalizedIncomingTrigger = _triggerRegistry.Normalize(triggerEvent.TriggerType);
        if (!string.Equals(normalizedTrigger, normalizedIncomingTrigger, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"触发类型不匹配：规则={normalizedTrigger}，事件={normalizedIncomingTrigger}";
            return false;
        }

        var matched = normalizedTrigger switch
        {
            AutomationTriggerCatalog.AppForegroundChanged => MatchProcessTrigger(trigger, triggerEvent, out reason),
            AutomationTriggerCatalog.AppStopped => MatchProcessTrigger(trigger, triggerEvent, out reason),
            AutomationTriggerCatalog.PerformanceModeEntered => MatchPerformanceModeTrigger(trigger, triggerEvent, "entered", out reason),
            AutomationTriggerCatalog.PerformanceModeExited => MatchPerformanceModeTrigger(trigger, triggerEvent, "exited", out reason),
            AutomationTriggerCatalog.PowerChanged => MatchPowerStateTrigger(trigger, triggerEvent, out reason),
            AutomationTriggerCatalog.BluetoothConnected => MatchPayloadValue(trigger, triggerEvent, "deviceId", out reason),
            AutomationTriggerCatalog.OutputDeviceChanged => AutomationOutputDeviceTriggerMatcher.Matches(trigger, triggerEvent),
            AutomationTriggerCatalog.SunriseReached => true,
            AutomationTriggerCatalog.SunsetReached => true,
            _ => true
        };

        if (matched)
        {
            reason = "触发条件匹配";
        }
        else if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "触发参数不匹配";
        }

        return matched;
    }

    private bool MatchProcessTrigger(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent, out string reason)
    {
        reason = string.Empty;
        var expectedEventKind = _triggerRegistry.Normalize(trigger.TriggerType) switch
        {
            AutomationTriggerCatalog.AppForegroundChanged => "process-started",
            AutomationTriggerCatalog.AppStopped => "process-stopped",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(expectedEventKind))
        {
            if (!triggerEvent.Payload.TryGetValue("eventKind", out var actualEventKind) ||
                !string.Equals(actualEventKind, expectedEventKind, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"事件类型不匹配：需要={expectedEventKind}，实际={actualEventKind ?? "缺失"}";
                return false;
            }
        }

        if (trigger.Parameters.TryGetValue("programListId", out var programListId) &&
            !string.IsNullOrWhiteSpace(programListId))
        {
            if (!triggerEvent.Payload.TryGetValue("processName", out var listProcessName) ||
                string.IsNullOrWhiteSpace(listProcessName))
            {
                reason = "事件没有提供进程名";
                return false;
            }

            var entries = AutomationProgramListCatalog.GetEntriesForList(
                _settingsService.AutomationGameList,
                programListId,
                _settingsService.BackgroundEfficiencyProcessBlacklist);
            var normalizedEventProcessName = NormalizeProcessNameForMatch(listProcessName);
            var matched = entries.Any(entry =>
                string.Equals(NormalizeProcessNameForMatch(entry.Executable), normalizedEventProcessName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeProcessNameForMatch(entry.Name), normalizedEventProcessName, StringComparison.OrdinalIgnoreCase));
            reason = matched ? string.Empty : $"进程不在程序列表中：{listProcessName}";
            return matched;
        }

        if (!trigger.Parameters.TryGetValue("processName", out var expectedProcessName) ||
            string.IsNullOrWhiteSpace(expectedProcessName))
        {
            return true;
        }

        var matchedProcess = triggerEvent.Payload.TryGetValue("processName", out var processName) &&
               string.Equals(
                   NormalizeProcessNameForMatch(processName),
                   NormalizeProcessNameForMatch(expectedProcessName),
                   StringComparison.OrdinalIgnoreCase);
        reason = matchedProcess ? string.Empty : $"进程不匹配：需要={expectedProcessName}，实际={processName ?? "缺失"}";
        return matchedProcess;
    }

    private static string NormalizeProcessNameForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var fileName = Path.GetFileName(trimmed);
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(withoutExtension) ? trimmed : withoutExtension;
    }

    private static bool MatchPowerStateTrigger(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent, out string reason)
    {
        reason = string.Empty;
        if (!trigger.Parameters.TryGetValue("mode", out var mode) || string.IsNullOrWhiteSpace(mode))
        {
            return true;
        }

        if (!triggerEvent.Payload.TryGetValue("isOnBattery", out var isOnBatteryValue))
        {
            reason = "事件没有提供电源状态";
            return false;
        }

        var matched = mode switch
        {
            "ac-connected" => string.Equals(isOnBatteryValue, bool.FalseString, StringComparison.OrdinalIgnoreCase),
            "ac-disconnected" => string.Equals(isOnBatteryValue, bool.TrueString, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        reason = matched ? string.Empty : $"电源状态不匹配：需要={mode}，实际={isOnBatteryValue}";
        return matched;
    }

    private static bool MatchPerformanceModeTrigger(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent, string expectedState, out string reason)
    {
        reason = string.Empty;
        if (trigger.Parameters.TryGetValue("state", out var configuredState) &&
            !string.IsNullOrWhiteSpace(configuredState) &&
            !string.Equals(configuredState, expectedState, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"规则状态不匹配：需要={expectedState}，配置={configuredState}";
            return false;
        }

        if (!MatchOptionalPayload(trigger, triggerEvent, "windowMode") ||
            !MatchOptionalPayload(trigger, triggerEvent, "gpuThreshold") ||
            !MatchOptionalPayload(trigger, triggerEvent, "gpuComparison"))
        {
            reason = "性能模式参数不匹配";
            return false;
        }

        var matched = !triggerEvent.Payload.TryGetValue("state", out var actualState) ||
               string.IsNullOrWhiteSpace(actualState) ||
               string.Equals(actualState, expectedState, StringComparison.OrdinalIgnoreCase);
        reason = matched ? string.Empty : $"模式状态不匹配：需要={expectedState}，实际={actualState}";
        return matched;
    }

    private static bool MatchOptionalPayload(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent, string key)
    {
        if (!trigger.Parameters.TryGetValue(key, out var configuredValue) ||
            string.IsNullOrWhiteSpace(configuredValue))
        {
            return true;
        }

        return triggerEvent.Payload.TryGetValue(key, out var actualValue) &&
               string.Equals(actualValue, configuredValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchPayloadValue(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent, string key, out string reason)
    {
        reason = string.Empty;
        if (!trigger.Parameters.TryGetValue(key, out var expectedValue) || string.IsNullOrWhiteSpace(expectedValue))
        {
            return true;
        }

        var matched = triggerEvent.Payload.TryGetValue(key, out var actualValue) &&
                      string.Equals(
                          DeviceIdentityHelper.NormalizeOutputEndpointId(actualValue),
                          DeviceIdentityHelper.NormalizeOutputEndpointId(expectedValue),
                          StringComparison.OrdinalIgnoreCase);

        _log.Information(
            "Automation payload match checked. TriggerType={TriggerType}, Key={Key}, ExpectedValue={ExpectedValue}, ActualValue={ActualValue}, Matched={Matched}",
            trigger.TriggerType,
            key,
            expectedValue,
            actualValue,
            matched);

        reason = matched ? string.Empty : $"{key} 不匹配：需要={expectedValue}，实际={actualValue ?? "缺失"}";
        return matched;
    }
}
