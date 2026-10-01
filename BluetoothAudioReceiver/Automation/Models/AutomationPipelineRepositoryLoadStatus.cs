namespace BluetoothAudioReceiver.Automation.Models;

public sealed record AutomationPipelineRepositoryLoadStatus(
    bool Success,
    int StoredCount,
    int LoadedCount,
    bool NormalizedAndSaved,
    string? ErrorMessage)
{
    public static AutomationPipelineRepositoryLoadStatus Empty { get; } =
        new(
            Success: false,
            StoredCount: 0,
            LoadedCount: 0,
            NormalizedAndSaved: false,
            ErrorMessage: "未开始加载");
}
