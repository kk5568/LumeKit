namespace BluetoothAudioReceiver.Automation.Models;

public sealed class AutomationPipeline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string Category { get; set; } = "基础";
    public AutomationTriggerDefinition Trigger { get; set; } = new();
    public List<AutomationConditionDefinition> Conditions { get; set; } = [];
    public List<AutomationStepDefinition> Steps { get; set; } = [];
    public AutomationPipelineOptions Options { get; set; } = new();
}
