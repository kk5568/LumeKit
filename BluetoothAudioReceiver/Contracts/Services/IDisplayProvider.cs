namespace BluetoothAudioReceiver.Contracts.Services;

public interface IDisplayProvider
{
    Task<byte?> GetBrightnessAsync(CancellationToken cancellationToken = default);

    Task SetBrightnessAsync(byte brightness, CancellationToken cancellationToken = default);
}
