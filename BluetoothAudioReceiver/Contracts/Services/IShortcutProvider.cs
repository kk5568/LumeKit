namespace BluetoothAudioReceiver.Contracts.Services;

public interface IShortcutProvider
{
    Task<bool> TriggerAsync(string shortcutText, CancellationToken cancellationToken = default);
}
