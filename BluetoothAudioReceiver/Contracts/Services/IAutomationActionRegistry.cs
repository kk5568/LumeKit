using BluetoothAudioReceiver.Automation;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationActionRegistry
{
    IReadOnlyList<AutomationActionCapability> Capabilities { get; }

    AutomationActionCapability? GetCapability(string? actionType);

    IAutomationActionProvider? GetProvider(string? actionType);

    bool IsSupported(string? actionType);
}
