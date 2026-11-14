using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class GHelperPerformanceModeController : IGHelperPerformanceModeController
{
    private readonly IShortcutProvider _shortcutProvider;

    public GHelperPerformanceModeController(IShortcutProvider shortcutProvider)
    {
        _shortcutProvider = shortcutProvider;
    }

    public Task<bool> SetModeAsync(GHelperPerformanceMode mode, CancellationToken cancellationToken = default)
    {
        var shortcut = mode switch
        {
            GHelperPerformanceMode.Quiet => GHelperShortcutCatalog.QuietModeHotkey,
            GHelperPerformanceMode.Balanced => GHelperShortcutCatalog.BalancedModeHotkey,
            GHelperPerformanceMode.Enhanced => GHelperShortcutCatalog.EnhancedModeHotkey,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported G-Helper performance mode.")
        };

        return _shortcutProvider.TriggerAsync(shortcut, cancellationToken);
    }
}
