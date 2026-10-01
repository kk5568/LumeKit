using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.Core.Contracts.Services;

public interface IThemeSelectorService
{
    ElementTheme Theme { get; }

    /// <summary>
    /// Occurs when the theme has changed, either due to user selection or the system theme changing.
    /// </summary>
    public event EventHandler<ElementTheme>? ThemeChanged;

    Task SetRequestedThemeAsync(Window window);

    Task SetThemeAsync(ElementTheme theme);

    Task SetSystemThemeAsync(ElementTheme theme);

    Task RefreshThemeFromSystemAsync();

    void InitializeSystemThemeMonitoring(DispatcherQueue dispatcherQueue);

    /// <summary>
    /// Checks if the <see cref="Theme"/> value resolves to dark
    /// </summary>
    /// <returns>True if the current theme is dark</returns>
    bool IsDarkTheme();

    ElementTheme GetSystemTheme();

    ElementTheme GetActualTheme();
}
