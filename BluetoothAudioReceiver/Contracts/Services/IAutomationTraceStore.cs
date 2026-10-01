using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationTraceStore
{
    IReadOnlyList<AutomationExecutionTrace> GetRecent(int maxCount = 200);

    IReadOnlyList<AutomationExecutionTrace> GetExecution(Guid executionId);

    string GetLogFilePath();

    Task FlushAsync();

    event EventHandler<AutomationExecutionTrace>? TraceAppended;

    void Append(AutomationExecutionTrace trace);

    void Clear();
}
