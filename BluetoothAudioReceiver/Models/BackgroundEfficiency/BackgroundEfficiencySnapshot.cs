namespace BluetoothAudioReceiver.Models.BackgroundEfficiency;

public sealed record BackgroundEfficiencySnapshot(
    DateTimeOffset LastUpdated,
    int CurrentSessionProcessCount,
    int BackgroundCandidateCount,
    uint CurrentForegroundProcessId,
    string CurrentForegroundProcessName,
    string CurrentForegroundWindowTitle,
    uint PreviousForegroundProcessId,
    string PreviousForegroundProcessName,
    string MonitoringStatusText,
    string LastFailureReason,
    IReadOnlyList<BackgroundEfficiencyProcessInfo> RecentProcesses)
{
    public static BackgroundEfficiencySnapshot Empty { get; } = new(
        LastUpdated: DateTimeOffset.MinValue,
        CurrentSessionProcessCount: 0,
        BackgroundCandidateCount: 0,
        CurrentForegroundProcessId: 0,
        CurrentForegroundProcessName: string.Empty,
        CurrentForegroundWindowTitle: string.Empty,
        PreviousForegroundProcessId: 0,
        PreviousForegroundProcessName: string.Empty,
        MonitoringStatusText: "未启动",
        LastFailureReason: "无",
        RecentProcesses: Array.Empty<BackgroundEfficiencyProcessInfo>());
}

public sealed record BackgroundEfficiencyProcessInfo(
    int ProcessId,
    string ProcessName,
    int SessionId,
    bool HasMainWindow,
    bool IsForeground,
    string WindowTitle,
    string StatusText);
