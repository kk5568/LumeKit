namespace BluetoothAudioReceiver.Automation.Models;

public sealed class AutomationConditionDefinition
{
    public string OperatorType { get; set; } = "If";
    public string ConditionType { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
