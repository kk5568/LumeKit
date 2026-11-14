namespace BluetoothAudioReceiver.Contracts.Services;

/// <summary>
/// Publishes LumeKit status updates to the Super Island without changing normal notifications.
/// </summary>
public interface IIslandStatusService
{
    void ShowShortStatus(string iconGlyph, string title, string summary);
}
