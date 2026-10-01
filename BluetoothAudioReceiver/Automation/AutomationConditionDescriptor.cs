namespace BluetoothAudioReceiver.Automation;

public enum AutomationConditionProviderKind
{
    System,
    Application,
    Device
}

public sealed record AutomationConditionDescriptor(
    string ConditionId,
    string DisplayName,
    AutomationConditionProviderKind ProviderKind,
    bool SupportsParameters);
