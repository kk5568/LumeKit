using BluetoothAudioReceiver.Models.Application;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAudioProvider
{
    int CurrentInputVolumeIndex { get; }

    Task SetInputDeviceAsync(string deviceId, bool disconnect = false, CancellationToken cancellationToken = default);

    Task SetInputVolumeAsync(int index, CancellationToken cancellationToken = default);

    Task<string?> GetCurrentOutputDeviceIdAsync(CancellationToken cancellationToken = default);

    Task SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudioOutputDeviceItem>> GetOutputDevicesAsync(CancellationToken cancellationToken = default);
}
