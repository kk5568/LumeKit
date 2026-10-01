using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class ShortcutProvider : IShortcutProvider
{
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;

    public ShortcutProvider(IAudioRouteHotkeyService audioRouteHotkeyService)
    {
        _audioRouteHotkeyService = audioRouteHotkeyService;
    }

    public Task<bool> TriggerAsync(string shortcutText, CancellationToken cancellationToken = default)
        => _audioRouteHotkeyService.TriggerCustomHotkeyAsync(shortcutText);
}
