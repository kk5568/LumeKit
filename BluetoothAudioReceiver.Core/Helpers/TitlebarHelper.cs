using System;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using BluetoothAudioReceiver.Core.Contracts.Services;
using WinUIEx;

namespace BluetoothAudioReceiver.Core.Helpers;

// Helper class to workaround custom title bar bugs.
// DISCLAIMER: The resource key names and color values used below are subject to change. Do not depend on them.
// https://github.com/microsoft/TemplateStudio/issues/4516
public class TitleBarHelper
{
    public static unsafe void UpdateTitleBar(Window? window = null, ElementTheme? theme = null)
    {
        window ??= Ioc.Default.GetRequiredService<Window>();
        theme ??= Ioc.Default.GetRequiredService<IThemeSelectorService>().GetActualTheme();
        theme = ResolveConcreteTheme(theme.Value);

        if (window.ExtendsContentIntoTitleBar)
        {
            window.AppWindow.TitleBar.ButtonForegroundColor = theme switch
            {
                ElementTheme.Dark => Colors.White,
                ElementTheme.Light => Colors.Black,
                _ => Colors.Transparent
            };

            window.AppWindow.TitleBar.ButtonHoverForegroundColor = theme switch
            {
                ElementTheme.Dark => Colors.White,
                ElementTheme.Light => Colors.Black,
                _ => Colors.Transparent
            };

            window.AppWindow.TitleBar.ButtonHoverBackgroundColor = theme switch
            {
                ElementTheme.Dark => Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
                ElementTheme.Light => Color.FromArgb(0x33, 0x00, 0x00, 0x00),
                _ => Colors.Transparent
            };

            window.AppWindow.TitleBar.ButtonPressedBackgroundColor = theme switch
            {
                ElementTheme.Dark => Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
                ElementTheme.Light => Color.FromArgb(0x66, 0x00, 0x00, 0x00),
                _ => Colors.Transparent
            };

            window.AppWindow.TitleBar.BackgroundColor = Colors.Transparent;

            var hwnd = new HWND(window.GetWindowHandle());
            if (hwnd == PInvoke.GetActiveWindow())
            {
                PInvoke.SendMessage(hwnd, PInvoke.WM_ACTIVATE, PInvoke.WA_INACTIVE, IntPtr.Zero);
                PInvoke.SendMessage(hwnd, PInvoke.WM_ACTIVATE, PInvoke.WA_ACTIVE, IntPtr.Zero);
            }
            else
            {
                PInvoke.SendMessage(hwnd, PInvoke.WM_ACTIVATE, PInvoke.WA_ACTIVE, IntPtr.Zero);
                PInvoke.SendMessage(hwnd, PInvoke.WM_ACTIVATE, PInvoke.WA_INACTIVE, IntPtr.Zero);
            }
        }

        // DWM owns non-client surfaces even when WinUI owns the content theme.
        // Keep the shell-visible window state in sync for title bar/taskbar previews.
        var windowHwnd = new HWND(window.GetWindowHandle());
        var isDarkModeInt = theme == ElementTheme.Dark ? 1 : 0;
        PInvoke.DwmSetWindowAttribute(windowHwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &isDarkModeInt, (uint)Marshal.SizeOf<int>());
    }

    public static void ApplySystemThemeToCaptionButtons(Window? window, UIElement? customTitleBar)
    {
        var frame = customTitleBar as FrameworkElement;
        UpdateTitleBar(window, frame?.ActualTheme);
    }

    private static ElementTheme ResolveConcreteTheme(ElementTheme theme)
    {
        if (theme != ElementTheme.Default)
        {
            return theme;
        }

        var uiSettings = new UISettings();
        var background = uiSettings.GetColorValue(UIColorType.Background);
        if (background == Colors.White)
        {
            return ElementTheme.Light;
        }

        if (background == Colors.Black)
        {
            return ElementTheme.Dark;
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
    }
}
