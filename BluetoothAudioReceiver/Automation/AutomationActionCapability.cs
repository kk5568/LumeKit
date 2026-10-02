namespace BluetoothAudioReceiver.Automation;

[Flags]
public enum AutomationActionResourceScope
{
    None = 0,
    Power = 1 << 0,
    Theme = 1 << 1,
    Brightness = 1 << 2,
    Audio = 1 << 3,
    Process = 1 << 4,
    Input = 1 << 5,
    Notification = 1 << 6,
}

public enum AutomationActionProviderKind
{
    System,
    Device,
    Input,
    Process,
    Notification
}

public sealed record AutomationActionCapability(
    string ActionId,
    string DisplayName,
    AutomationActionProviderKind ProviderKind,
    AutomationActionResourceScope ResourceScope,
    bool SupportsRestore,
    bool RequiresAdministrator,
    bool IsAsync,
    bool AllowsConcurrent);
