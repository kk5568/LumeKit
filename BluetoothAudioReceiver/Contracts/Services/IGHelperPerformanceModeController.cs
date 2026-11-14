namespace BluetoothAudioReceiver.Contracts.Services;

public enum GHelperPerformanceMode
{
    Quiet,
    Balanced,
    Enhanced
}

public interface IGHelperPerformanceModeController
{
    Task<bool> SetModeAsync(GHelperPerformanceMode mode, CancellationToken cancellationToken = default);
}
