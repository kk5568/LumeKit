namespace BluetoothAudioReceiver.Contracts.Services;

public interface IBrightnessService
{
    Task InitializeAsync();

    Task AdjustBrightnessAsync(byte target);

    Task<byte> GetCurrentBrightnessAsync();
}
