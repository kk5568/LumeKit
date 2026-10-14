using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace BluetoothAudioReceiver.Helpers;

internal static class HotkeyTextHelper
{
    public static string? TryBuildHotkeyText(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        var keyText = ToHotkeyKeyText(key);
        if (keyText is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(VirtualKeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(VirtualKeyModifiers.Menu)) parts.Add("Alt");
        if (modifiers.HasFlag(VirtualKeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(VirtualKeyModifiers.Windows)) parts.Add("Win");
        parts.Add(keyText);
        return string.Join("+", parts);
    }

    public static VirtualKeyModifiers GetCurrentModifiers(KeyRoutedEventArgs e)
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsCtrlDown()) modifiers |= VirtualKeyModifiers.Control;
        if (e.KeyStatus.IsMenuKeyDown || IsAltDown()) modifiers |= VirtualKeyModifiers.Menu;
        if (IsShiftDown()) modifiers |= VirtualKeyModifiers.Shift;
        if (IsWinDown()) modifiers |= VirtualKeyModifiers.Windows;
        return modifiers;
    }

    public static string? ToHotkeyKeyText(VirtualKey key)
    {
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            var digit = (char)('0' + (int)(key - VirtualKey.Number0));
            return digit.ToString();
        }

        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key >= VirtualKey.F1 && key <= VirtualKey.F24)
        {
            return key.ToString().ToUpperInvariant();
        }

        return key switch
        {
            VirtualKey.Escape => "Esc",
            VirtualKey.Back => "Backspace",
            VirtualKey.Space => "Space",
            VirtualKey.Tab => "Tab",
            VirtualKey.Enter => "Enter",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.NumberPad0 => "NumPad0",
            VirtualKey.NumberPad1 => "NumPad1",
            VirtualKey.NumberPad2 => "NumPad2",
            VirtualKey.NumberPad3 => "NumPad3",
            VirtualKey.NumberPad4 => "NumPad4",
            VirtualKey.NumberPad5 => "NumPad5",
            VirtualKey.NumberPad6 => "NumPad6",
            VirtualKey.NumberPad7 => "NumPad7",
            VirtualKey.NumberPad8 => "NumPad8",
            VirtualKey.NumberPad9 => "NumPad9",
            (VirtualKey)0x14 => "CapsLock",
            (VirtualKey)0x2C => "PrintScreen",
            (VirtualKey)0x5D => "Application",
            (VirtualKey)0x13 => "Pause",
            _ => key.ToString().ToUpperInvariant()
        };
    }

    public static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;
    }

    private static bool IsCtrlDown() => IsDown(VirtualKey.Control) || IsDown(VirtualKey.LeftControl) || IsDown(VirtualKey.RightControl);
    private static bool IsAltDown() => IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftMenu) || IsDown(VirtualKey.RightMenu);
    private static bool IsShiftDown() => IsDown(VirtualKey.Shift) || IsDown(VirtualKey.LeftShift) || IsDown(VirtualKey.RightShift);
    private static bool IsWinDown() => IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows);

    private static bool IsDown(VirtualKey key)
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }
}
