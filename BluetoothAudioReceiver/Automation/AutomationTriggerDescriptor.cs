namespace BluetoothAudioReceiver.Automation;

public enum AutomationTriggerProviderKind
{
    System,
    Application,
    Device,
    Schedule
}

public sealed record AutomationTriggerDescriptor(
    string TriggerId,
    string DisplayName,
    AutomationTriggerProviderKind ProviderKind,
    bool SupportsParameters,
    bool EmitsDynamicPayload,
    IReadOnlyList<string>? LegacyAliases = null);
