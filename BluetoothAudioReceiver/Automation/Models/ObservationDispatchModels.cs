namespace BluetoothAudioReceiver.Automation.Models;

internal enum ObservationUsageScene
{
    Startup,
    Unlock,
    LightWork,
    HeavyWork,
    VideoPlayback,
    Game,
    LowBattery,
    Locked
}

internal enum ObservationInfoKind
{
    PowerState,
    ForegroundWindow,
    BluetoothState,
    BluetoothRadioState,
    AudioRoute,
    ThemeMode,
    Brightness,
    NetworkState,
    ProxyState
}

internal sealed record ObservationRequest(
    ObservationUsageScene Scene,
    string Reason,
    IReadOnlyList<ObservationInfoKind> Items,
    Guid CorrelationId = default)
{
    public bool Includes(ObservationInfoKind kind) => Items.Contains(kind);
}
