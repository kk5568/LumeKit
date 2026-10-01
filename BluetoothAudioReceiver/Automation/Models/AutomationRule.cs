namespace BluetoothAudioReceiver.Automation.Models;

public sealed class AutomationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string TriggerPayloadJson { get; set; } = "{}";
    public string ConditionJson { get; set; } = "{}";
    public string ActionPlanJson { get; set; } = "[]";
    public bool StopAfterMatch { get; set; }
}
