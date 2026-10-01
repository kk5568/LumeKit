namespace BluetoothAudioReceiver.Contracts.Services;

public interface IInputAudioVolumeService
{
    Task ApplyCurrentVolumeAsync();

    Task ApplyVolumeAsync(int index);
}
