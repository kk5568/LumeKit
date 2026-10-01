using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationConditionRegistry
{
    IReadOnlyList<AutomationConditionDescriptor> Conditions { get; }

    AutomationConditionDescriptor? GetDescriptor(string? conditionType);

    bool IsSupported(string? conditionType);

    bool Evaluate(AutomationConditionDefinition condition, TriggerEvent triggerEvent, SystemContext context);
}
