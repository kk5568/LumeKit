using System;
using System.Collections.Generic;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutoBrightnessPage : Page
{
    public AutoBrightnessPageViewModel ViewModel { get; }
    private readonly IAudioRouteHotkeyService _hotkeyService;
    private readonly IAppSettingsService _appSettingsService;
    private Action<VirtualKey, VirtualKeyModifiers>? _shortcutCaptureHandler;
    private KeyEventHandler? _shortcutCaptureKeyDownHandler;
    private TypedEventHandler<UIElement, ProcessKeyboardAcceleratorEventArgs>? _shortcutCaptureAcceleratorHandler;
    private bool _isShortcutCaptureActive;

    public AutoBrightnessPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AutoBrightnessPageViewModel>();
        _hotkeyService = Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>();
        _appSettingsService = Ioc.Default.GetRequiredService<IAppSettingsService>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OnApplyShortcutClick(object sender, RoutedEventArgs e)
    {
        StartShortcutCapture();
    }

    private async void OnSaveShortcutClick(object sender, RoutedEventArgs e)
    {
        StopShortcutCapture();

        if (string.IsNullOrWhiteSpace(ViewModel.CustomShortcutText) ||
            string.Equals(ViewModel.CustomShortcutText, "未设置", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ViewModel.CustomShortcutText, "请按下快捷键", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.ShortcutStatusText = "快捷键不能为空";
            return;
        }

        var normalized = BluetoothPageViewModel.NormalizeHotkey(ViewModel.CustomShortcutText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            ViewModel.ShortcutStatusText = "快捷键不能为空";
            return;
        }

        ViewModel.CustomShortcutText = normalized;
        ViewModel.ShortcutStatusText = $"已保存：{normalized}";

        await _appSettingsService.SetAutoBrightnessHotkeyAsync(normalized);
        _hotkeyService.UpdateAutoBrightnessHotkey(normalized);
    }

    private void StartShortcutCapture()
    {
        if (_isShortcutCaptureActive)
        {
            return;
        }

        _isShortcutCaptureActive = true;
        ViewModel.ShortcutStatusText = "正在录制，请按下组合键";
        ShortcutCaptureTextBox.Text = "请按下快捷键";
        ShortcutCaptureTextBox.Focus(FocusState.Programmatic);
        ShortcutCaptureTextBox.SelectAll();

        _shortcutCaptureKeyDownHandler = OnShortcutCaptureTextBoxKeyDown;
        _shortcutCaptureAcceleratorHandler = OnShortcutCaptureAccelerator;
        ShortcutCaptureTextBox.KeyDown += _shortcutCaptureKeyDownHandler;
        ShortcutCaptureTextBox.ProcessKeyboardAccelerators += _shortcutCaptureAcceleratorHandler;

        _shortcutCaptureHandler = OnShortcutCaptureKeyDown;
        _hotkeyService.HotkeyCaptureKeyDown += _shortcutCaptureHandler;
        _hotkeyService.SetHotkeyCaptureSuspended(true);
    }

    private void StopShortcutCapture()
    {
        if (!_isShortcutCaptureActive)
        {
            return;
        }

        _isShortcutCaptureActive = false;

        if (_shortcutCaptureHandler is not null)
        {
            _hotkeyService.HotkeyCaptureKeyDown -= _shortcutCaptureHandler;
            _shortcutCaptureHandler = null;
        }

        if (_shortcutCaptureKeyDownHandler is not null)
        {
            ShortcutCaptureTextBox.KeyDown -= _shortcutCaptureKeyDownHandler;
            _shortcutCaptureKeyDownHandler = null;
        }

        if (_shortcutCaptureAcceleratorHandler is not null)
        {
            ShortcutCaptureTextBox.ProcessKeyboardAccelerators -= _shortcutCaptureAcceleratorHandler;
            _shortcutCaptureAcceleratorHandler = null;
        }

        _hotkeyService.SetHotkeyCaptureSuspended(false);
    }

    private void OnShortcutCaptureTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isShortcutCaptureActive)
        {
            e.Handled = true;
        }
    }

    private void OnShortcutCaptureAccelerator(UIElement sender, ProcessKeyboardAcceleratorEventArgs args)
    {
        if (_isShortcutCaptureActive)
        {
            args.Handled = true;
        }
    }

    private void OnShortcutCaptureKeyDown(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (!_isShortcutCaptureActive)
        {
            return;
        }

        if (key == VirtualKey.Escape)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.CustomShortcutText = "未设置";
                ViewModel.ShortcutStatusText = "快捷键已清空";
                ShortcutCaptureTextBox.Text = ViewModel.CustomShortcutText;
                StopShortcutCapture();
            });
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        var built = TryBuildHotkeyText(key, modifiers);
        if (built is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.CustomShortcutText = built;
            ViewModel.ShortcutStatusText = $"已录入：{built}";
            ShortcutCaptureTextBox.Text = built;
            StopShortcutCapture();
        });
    }

    private static string? TryBuildHotkeyText(VirtualKey key, VirtualKeyModifiers modifiers)
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

    private static string? ToHotkeyKeyText(VirtualKey key)
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
            _ => key.ToString().ToUpperInvariant()
        };
    }

    private static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;
    }
}
