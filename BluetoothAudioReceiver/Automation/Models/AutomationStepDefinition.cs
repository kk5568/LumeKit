namespace BluetoothAudioReceiver.Automation.Models;

public sealed class AutomationStepDefinition
{
    public string ActionType { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
}
