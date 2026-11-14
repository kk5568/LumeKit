using Microsoft.UI.Xaml;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.Services;

public sealed class ThemeManager
{
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";
    private const string SystemUsesLightThemeValueName = "SystemUsesLightTheme";

    public Task ApplyThemeAsync(ElementTheme theme)
    {
        if (theme == ElementTheme.Default)
        {
            return Task.CompletedTask;
        }

        if (IsRegistryThemeApplied(theme))
        {
            return Task.CompletedTask;
        }

        ApplyRegistry(theme);
        BroadcastThemeChangeOnce();
        return Task.CompletedTask;
    }

    public ElementTheme GetCurrentTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath, writable: false);
            var value = ReadThemeValue(key, AppsUseLightThemeValueName)
                ?? ReadThemeValue(key, SystemUsesLightThemeValueName);

            if (value is not null)
            {
                return value == 0 ? ElementTheme.Dark : ElementTheme.Light;
            }
        }
        catch
        {
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    private static void ApplyRegistry(ElementTheme theme)
    {
        var value = theme == ElementTheme.Dark ? 0 : 1;

        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeRegistryPath, writable: true);
        if (key is null)
        {
            return;
        }

        key.SetValue(AppsUseLightThemeValueName, value, RegistryValueKind.DWord);
        key.SetValue(SystemUsesLightThemeValueName, value, RegistryValueKind.DWord);
    }

    private static bool IsRegistryThemeApplied(ElementTheme theme)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath, writable: false);
            var expectedValue = theme == ElementTheme.Dark ? 0 : 1;
            var appsValue = ReadThemeValue(key, AppsUseLightThemeValueName);
            var systemValue = ReadThemeValue(key, SystemUsesLightThemeValueName);
            return appsValue == expectedValue && systemValue == expectedValue;
        }
        catch
        {
            return false;
        }
    }

    private static void BroadcastThemeChangeOnce()
    {
        const int hwndBroadcast = -1;
        const uint wmSettingChange = 0x001A;
        const uint wmDwmColorizationColorChanged = 0x0320;
        const uint smtoAbortIfHung = 0x0002;
        var hwnd = new IntPtr(hwndBroadcast);
        SendMessageTimeout(hwnd, wmSettingChange, IntPtr.Zero, "ImmersiveColorSet", smtoAbortIfHung, 1000, out _);
        SendMessageTimeout(hwnd, wmSettingChange, IntPtr.Zero, "WindowsThemeElement", smtoAbortIfHung, 1000, out _);
        SendNotifyMessage(hwnd, wmSettingChange, IntPtr.Zero, "ImmersiveColorSet");
        SendNotifyMessage(hwnd, wmSettingChange, IntPtr.Zero, "WindowsThemeElement");
        SendNotifyMessage(hwnd, wmDwmColorizationColorChanged, IntPtr.Zero, IntPtr.Zero);
    }

    private static int? ReadThemeValue(RegistryKey? key, string valueName)
    {
        var rawValue = key?.GetValue(valueName);
        return rawValue switch
        {
            int value => value,
            string text when int.TryParse(text, out var value) => value,
            _ => null
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);
}
