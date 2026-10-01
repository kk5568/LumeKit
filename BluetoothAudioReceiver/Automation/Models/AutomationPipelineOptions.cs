namespace BluetoothAudioReceiver.Automation.Models;

public sealed class AutomationPipelineOptions
{
    public int Priority { get; set; }
    public AutomationFailurePolicy FailurePolicy { get; set; } = AutomationFailurePolicy.Continue;
    public double StepIntervalSeconds { get; set; }
    public double CooldownSeconds { get; set; } = 30;
    public bool AllowRepeatedExecution { get; set; } = true;
    public bool ExecutionNotificationEnabled { get; set; } = true;
    public bool RunOnStartupEnabled { get; set; }
    public string RunTimeMode { get; set; } = "any";
    public bool OnceOnlyEnabled { get; set; }
    public bool NotifyOnSuccess { get; set; } = true;
    public bool NotifyOnFailure { get; set; } = true;
}
