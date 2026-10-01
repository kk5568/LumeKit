using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationActionProvider
{
    AutomationActionCapability Capability { get; }

    string ActionType { get; }

    Task ExecuteAsync(
        AutomationActionDefinition action,
        TriggerEvent triggerEvent,
        AutomationExecutionContext executionContext,
        SystemContext context,
        CancellationToken cancellationToken = default);
}
