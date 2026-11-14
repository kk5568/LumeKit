using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.DeskBoxFloating.Views;

namespace BluetoothAudioReceiver.Services;

internal sealed class IslandStatusService(
    IAppSettingsService appSettingsService,
    IDeskBoxFloatingService floatingService) : IIslandStatusService
{
    private const int ShortStatusDurationMilliseconds = 2500;

    public void ShowShortStatus(string iconGlyph, string title, string summary)
    {
        if (!appSettingsService.IslandEnabled || NotificationService.ShouldSuppressByScene(appSettingsService))
        {
            return;
        }

        floatingService.ShowIslandStatus(
            new DeskBoxIslandStatusContent(new DeskBoxIslandStatus(iconGlyph, title, summary)),
            ShortStatusDurationMilliseconds);
    }
}
