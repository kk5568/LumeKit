using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IThemeProvider
{
    ElementTheme CurrentTheme { get; }

    Task SetThemeAsync(ElementTheme theme, CancellationToken cancellationToken = default);
}
