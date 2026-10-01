namespace BluetoothAudioReceiver.Automation.Models;

public sealed class AutomationActionDefinition
{
    public string ActionType { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
}
