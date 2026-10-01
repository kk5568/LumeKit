using BluetoothAudioReceiver.Automation;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationTriggerRegistry
{
    IReadOnlyList<AutomationTriggerDescriptor> Triggers { get; }

    AutomationTriggerDescriptor? GetDescriptor(string? triggerId);

    string Normalize(string? triggerId);

    string GetDisplayName(string? triggerId);

    bool IsSupported(string? triggerId);
}
