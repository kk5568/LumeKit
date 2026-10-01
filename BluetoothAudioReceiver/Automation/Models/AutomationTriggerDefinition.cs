namespace BluetoothAudioReceiver.Automation.Models;

public sealed class AutomationTriggerDefinition
{
    public string TriggerType { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
