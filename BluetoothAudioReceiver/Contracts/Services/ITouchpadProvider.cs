namespace BluetoothAudioReceiver.Contracts.Services;

public interface ITouchpadProvider
{
    Task<bool?> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task<bool> EnableAsync(CancellationToken cancellationToken = default);

    Task<bool> DisableAsync(CancellationToken cancellationToken = default);
}
