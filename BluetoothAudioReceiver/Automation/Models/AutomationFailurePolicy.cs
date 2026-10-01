namespace BluetoothAudioReceiver.Automation.Models;

public enum AutomationFailurePolicy
{
    Continue = 0,
    Retry = 1,
    Rollback = 2,
    Abort = 3
}
