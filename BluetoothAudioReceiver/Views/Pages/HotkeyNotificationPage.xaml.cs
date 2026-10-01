using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class HotkeyNotificationPage : Page
{
    public HotkeyNotificationPageViewModel ViewModel { get; }
    private readonly IAudioRouteHotkeyService _hotkeyService;

    public HotkeyNotificationPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<HotkeyNotificationPageViewModel>();
        _hotkeyService = Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void OnAddCardClick(object sender, RoutedEventArgs e)
    {
        var result = await ShowCardEditorDialogAsync(
            "添加按键卡片",
            HotkeyNotificationCard.DefaultHotkey,
            HotkeyNotificationCard.DefaultStatus);

        if (result is null)
        {
            return;
        }

        await ViewModel.AddCardAsync(result.Value.Hotkey, result.Value.Status);
    }

    private async void OnEditCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyNotificationCard card)
        {
            return;
        }

        var result = await ShowCardEditorDialogAsync(
            "编辑按键卡片",
            card.Hotkey,
            card.StatusText,
            card.Id);

        if (result is null)
        {
            return;
        }

        await ViewModel.UpdateCardAsync(card, result.Value.Hotkey, result.Value.Status);
    }

    private async void OnDeleteCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyNotificationCard card)
        {
            return;
        }

        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除按键卡片",
            Content = $"确认删除 {card.HotkeyDisplay} 这张卡片吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme
        };

        if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCardAsync(card);
        }
    }

    private async Task<(string Hotkey, string Status)?> ShowCardEditorDialogAsync(string title, string initialHotkey, string initialStatus, string? ignoreCardId = null)
    {
        var hotkey = initialHotkey;
        var status = initialStatus;
        var captureHotkeyInput = true;

        var hotkeyTextBox = new TextBox
        {
            Text = initialHotkey,
            PlaceholderText = "点击后按下单键或组合键，例如 A、F3、Ctrl+Alt+K",
            IsReadOnly = true,
            Margin = new Thickness(0, 8, 0, 8)
        };

        var clearHotkeyButton = new Button
        {
            Content = "清空",
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var saveHotkeyButton = new Button
        {
            Content = "保存",
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var hotkeyHintText = new TextBlock
        {
            Text = "直接按下新按键即可覆盖当前值，支持单键、组合键和功能键。",
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };

        var validationText = new TextBlock
        {
            Foreground = CreateCriticalBrush(),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var statusTextBox = new TextBox
        {
            Text = initialStatus,
            PlaceholderText = "例如：已执行 / 执行失败",
            Margin = new Thickness(0, 8, 0, 8)
        };

        var hotkeyRow = new Grid();
        hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hotkeyRow.Children.Add(hotkeyTextBox);
        Grid.SetColumn(clearHotkeyButton, 1);
        hotkeyRow.Children.Add(clearHotkeyButton);
        Grid.SetColumn(saveHotkeyButton, 2);
        hotkeyRow.Children.Add(saveHotkeyButton);

        var content = new StackPanel
        {
            Spacing = 8
        };
        content.RequestedTheme = ActualTheme;

        content.Children.Add(new TextBlock
        {
            Text = "按键",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(hotkeyRow);
        content.Children.Add(hotkeyHintText);
        content.Children.Add(validationText);
        content.Children.Add(new TextBlock
        {
            Text = "状态文案",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(statusTextBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme
        };
        var saved = false;

        hotkeyTextBox.GotFocus += (_, _) => captureHotkeyInput = true;
        statusTextBox.GotFocus += (_, _) => captureHotkeyInput = false;

        clearHotkeyButton.Click += (_, _) =>
        {
            hotkey = string.Empty;
            hotkeyTextBox.Text = string.Empty;
            validationText.Text = string.Empty;
            validationText.Visibility = Visibility.Collapsed;
            hotkeyHintText.Text = "已清空，请直接按下新的单键或组合键。";
            captureHotkeyInput = true;
            hotkeyTextBox.Focus(FocusState.Programmatic);
        };
        saveHotkeyButton.Click += (_, _) =>
        {
            var validation = ViewModel.ValidateHotkeyCandidate(hotkey, ignoreCardId);
            if (!validation.IsValid)
            {
                validationText.Text = validation.ErrorMessage;
                validationText.Visibility = Visibility.Visible;
                return;
            }

            hotkey = BluetoothPageViewModel.NormalizeHotkey(hotkey);
            if (string.IsNullOrWhiteSpace(hotkey))
            {
                validationText.Text = "按键不能为空";
                validationText.Visibility = Visibility.Visible;
                return;
            }

            validationText.Text = string.Empty;
            validationText.Visibility = Visibility.Collapsed;
            saved = true;
            dialog.Hide();
        };
        dialog.Opened += (_, _) => hotkeyTextBox.Focus(FocusState.Programmatic);

        KeyEventHandler keyDownHandler = (_, args) =>
        {
            if (captureHotkeyInput)
            {
                args.Handled = true;
            }
        };
        TypedEventHandler<UIElement, ProcessKeyboardAcceleratorEventArgs> acceleratorHandler = (_, args) =>
        {
            if (captureHotkeyInput)
            {
                args.Handled = true;
            }
        };

        content.KeyDown += keyDownHandler;
        content.ProcessKeyboardAccelerators += acceleratorHandler;

        void OnHotkeyCaptureKeyDown(VirtualKey key, VirtualKeyModifiers modifiers)
        {
            if (!captureHotkeyInput)
            {
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

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                hotkey = built;
                hotkeyTextBox.Text = built;

                var validation = ViewModel.ValidateHotkeyCandidate(built, ignoreCardId);
                if (!validation.IsValid)
                {
                    validationText.Text = validation.ErrorMessage;
                    validationText.Visibility = Visibility.Visible;
                }
                else
                {
                    validationText.Text = string.Empty;
                    validationText.Visibility = Visibility.Collapsed;
                    hotkeyHintText.Text = $"已录入：{built}，可以继续修改或点击保存。";
                }
            });
        }

        _hotkeyService.HotkeyCaptureKeyDown += OnHotkeyCaptureKeyDown;
        ViewModel.SetHotkeyCaptureSuspended(true);
        try
        {
            _ = await dialog.ShowAsync();
            if (!saved)
            {
                return null;
            }

            status = NormalizeText(statusTextBox.Text, HotkeyNotificationCard.DefaultStatus);
            return string.IsNullOrWhiteSpace(hotkey) ? null : (hotkey, status);
        }
        finally
        {
            content.KeyDown -= keyDownHandler;
            content.ProcessKeyboardAccelerators -= acceleratorHandler;
            _hotkeyService.HotkeyCaptureKeyDown -= OnHotkeyCaptureKeyDown;
            ViewModel.SetHotkeyCaptureSuspended(false);
        }
    }

    private static string NormalizeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
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

    private static VirtualKeyModifiers GetCurrentModifiers(KeyRoutedEventArgs e)
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsCtrlDown()) modifiers |= VirtualKeyModifiers.Control;
        if (e.KeyStatus.IsMenuKeyDown || IsAltDown()) modifiers |= VirtualKeyModifiers.Menu;
        if (IsShiftDown()) modifiers |= VirtualKeyModifiers.Shift;
        if (IsWinDown()) modifiers |= VirtualKeyModifiers.Windows;
        return modifiers;
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

    private static bool IsModifierKey(VirtualKey key)
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

    private static SolidColorBrush CreateCriticalBrush()
    {
        return new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 164, 38, 44));
    }
}
