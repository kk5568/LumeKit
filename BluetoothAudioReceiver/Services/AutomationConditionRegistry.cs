using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationConditionRegistry : IAutomationConditionRegistry
{
    private static readonly AutomationConditionDescriptor[] SupportedConditions =
    [
        new(
            AutomationSupportCatalog.ConditionPowerState,
            "电源状态",
            AutomationConditionProviderKind.System,
            SupportsParameters: true)
    ];

    private readonly IReadOnlyDictionary<string, AutomationConditionDescriptor> _descriptors;

    public AutomationConditionRegistry()
    {
        _descriptors = SupportedConditions.ToDictionary(item => item.ConditionId, StringComparer.OrdinalIgnoreCase);
        Conditions = SupportedConditions;
    }

    public IReadOnlyList<AutomationConditionDescriptor> Conditions { get; }

    public AutomationConditionDescriptor? GetDescriptor(string? conditionType)
    {
        if (string.IsNullOrWhiteSpace(conditionType))
        {
            return null;
        }

        return _descriptors.TryGetValue(conditionType.Trim(), out var descriptor)
            ? descriptor
            : null;
    }

    public bool IsSupported(string? conditionType)
        => GetDescriptor(conditionType) is not null;

    public bool Evaluate(AutomationConditionDefinition condition, TriggerEvent triggerEvent, SystemContext context)
    {
        if (!IsSupported(condition.ConditionType))
        {
            return false;
        }

        return condition.ConditionType switch
        {
            AutomationSupportCatalog.ConditionPowerState => MatchPowerCondition(condition, triggerEvent, context),
            _ => false
        };
    }

    private static bool MatchPowerCondition(
        AutomationConditionDefinition condition,
        TriggerEvent triggerEvent,
        SystemContext context)
    {
        if (!condition.Parameters.TryGetValue("mode", out var mode) || string.IsNullOrWhiteSpace(mode))
        {
            return true;
        }

        var isOnBattery = context.IsOnBattery;
        if (triggerEvent.Payload.TryGetValue("isOnBattery", out var payloadValue) &&
            bool.TryParse(payloadValue, out var parsed))
        {
            isOnBattery = parsed;
        }

        return mode switch
        {
            "ac-connected" => isOnBattery == false,
            "ac-disconnected" => isOnBattery == true,
            _ => false
        };
    }
}
