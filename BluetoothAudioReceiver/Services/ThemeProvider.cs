using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.Services;

internal sealed class ThemeProvider : IThemeProvider
{
    private readonly IThemeSelectorService _themeSelectorService;

    public ThemeProvider(IThemeSelectorService themeSelectorService)
    {
        _themeSelectorService = themeSelectorService;
    }

    public ElementTheme CurrentTheme => _themeSelectorService.Theme;

    public Task SetThemeAsync(ElementTheme theme, CancellationToken cancellationToken = default)
        => _themeSelectorService.SetThemeAsync(theme);
}
