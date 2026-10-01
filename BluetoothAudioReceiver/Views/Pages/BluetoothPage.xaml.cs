using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class BluetoothPage : Page
{
    public BluetoothPageViewModel ViewModel { get; }

    public BluetoothPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<BluetoothPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void OnAddHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        await AddOrEditHotkeyCardAsync(existingCard: null);
    }

    private async void OnEditHotkeyCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await AddOrEditHotkeyCardAsync(card);
    }

    private async void OnDeleteHotkeyCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await ViewModel.DeleteHotkeyCardAsync(card);
    }


    private async Task AddOrEditHotkeyCardAsync(HotkeyBindingCard? existingCard)
    {
        if (existingCard == null)
        {
            var hotkeyResult = await ShowHotkeyCaptureDialogAsync(string.Empty);
            if (!hotkeyResult.Saved)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(hotkeyResult.Hotkey))
            {
                ViewModel.BindingStatusText = "快捷键不能为空";
                return;
            }

            await ViewModel.AddHotkeyCardAsync(true, hotkeyResult.Hotkey, []);
            return;
        }

        var existingIds = existingCard.SelectedDeviceIds;
        var deviceResult = await ShowDeviceSelectionDialogAsync(existingIds);
        if (!deviceResult.Saved)
        {
            return;
        }

        if (!ViewModel.TryResolveBindingType(deviceResult.SelectedIds, out var isInput, out var error))
        {
            ViewModel.BindingStatusText = error;
            return;
        }

        if (existingCard.IsInputBinding == isInput)
        {
            await ViewModel.UpdateHotkeyCardAsync(existingCard, existingCard.Hotkey, deviceResult.SelectedIds);
            return;
        }

        await ViewModel.DeleteHotkeyCardAsync(existingCard);
        await ViewModel.AddHotkeyCardAsync(isInput, existingCard.Hotkey, deviceResult.SelectedIds);
    }

    private async Task<(bool Saved, string Hotkey)> ShowHotkeyCaptureDialogAsync(string initialHotkey)
    {
        var hotkey = initialHotkey;
        var hotkeyTextBox = new TextBox
        {
            Text = initialHotkey,
            PlaceholderText = "点击后按组合键，例如 Alt+1",
            IsReadOnly = true,
            Margin = new Thickness(0, 8, 0, 8)
        };
        var tipText = new TextBlock
        {
            Text = "按 Esc 可取消录制",
            Opacity = 0.7
        };

        var content = new StackPanel { Spacing = 8 };
        content.RequestedTheme = ActualTheme;
        content.Children.Add(new TextBlock
        {
            Text = "快捷键",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(hotkeyTextBox);
        content.Children.Add(tipText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "设置快捷键",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme
        };

        void CaptureByKey(VirtualKey key, VirtualKeyModifiers modifiers, Action<bool> markHandled)
        {
            markHandled(true);

            if (key == VirtualKey.Escape)
            {
                hotkey = string.Empty;
                hotkeyTextBox.Text = string.Empty;
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

            hotkey = built;
            hotkeyTextBox.Text = built;
        }

        KeyEventHandler keyDownHandler = (_, args) =>
        {
            var modifiers = GetCurrentModifiers(args);
            CaptureByKey(args.Key, modifiers, handled => args.Handled = handled);
        };
        TypedEventHandler<UIElement, ProcessKeyboardAcceleratorEventArgs> acceleratorHandler = (_, args) =>
        {
            CaptureByKey(args.Key, args.Modifiers, handled => args.Handled = handled);
        };

        content.KeyDown += keyDownHandler;
        content.ProcessKeyboardAccelerators += acceleratorHandler;
        dialog.Opened += (_, _) => hotkeyTextBox.Focus(FocusState.Programmatic);

        ViewModel.SetHotkeyCaptureSuspended(true);
        try
        {
            var result = await dialog.ShowAsync();
            return (result == ContentDialogResult.Primary, BluetoothPageViewModel.NormalizeHotkey(hotkey));
        }
        finally
        {
            content.KeyDown -= keyDownHandler;
            content.ProcessKeyboardAccelerators -= acceleratorHandler;
            ViewModel.SetHotkeyCaptureSuspended(false);
        }
    }

    private async Task<(bool Saved, List<string> SelectedIds)> ShowDeviceSelectionDialogAsync(IReadOnlyList<string> preselectedIds)
    {
        await ViewModel.EnsureAllDevicesLoadedAsync();
        var choices = ViewModel.GetUnifiedDeviceChoices();

        var selectedSet = new HashSet<string>(
            preselectedIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var checkboxes = new List<CheckBox>();

        var listPanel = new StackPanel { Spacing = 6 };

        foreach (var choice in choices)
        {
            var devicePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Opacity = choice.IsAvailable ? 1.0 : 0.45
            };
            devicePanel.Children.Add(new FontIcon
            {
                Glyph = DeviceVisualHelper.ResolveGlyph(choice.Name),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                VerticalAlignment = VerticalAlignment.Center
            });
            devicePanel.Children.Add(new TextBlock
            {
                Text = choice.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var checkbox = new CheckBox
            {
                Content = devicePanel,
                Tag = choice.Id,
                IsChecked = selectedSet.Contains(choice.Id)
            };
            checkboxes.Add(checkbox);
            listPanel.Children.Add(checkbox);
        }

        var errorText = new TextBlock
        {
            Text = string.Empty,
            Foreground = CreateCriticalBrush()
        };

        var content = new StackPanel { Spacing = 10 };
        content.RequestedTheme = ActualTheme;
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            Content = listPanel
        });
        content.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "选择设备",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme
        };

        dialog.PrimaryButtonClick += (dialogSender, args) =>
        {
            var selected = checkboxes
                .Where(box => box.IsChecked == true)
                .Select(box => box.Tag?.ToString() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!ViewModel.TryResolveBindingType(selected, out _, out var error))
            {
                args.Cancel = true;
                errorText.Text = error;
            }
        };

        var result = await dialog.ShowAsync();
        var selectedIds = checkboxes
            .Where(box => box.IsChecked == true)
            .Select(box => box.Tag?.ToString() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (result == ContentDialogResult.Primary, selectedIds);
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

    private static bool IsCtrlDown() => IsDown(VirtualKey.Control) || IsDown(VirtualKey.LeftControl) || IsDown(VirtualKey.RightControl);
    private static bool IsAltDown() => IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftMenu) || IsDown(VirtualKey.RightMenu);
    private static bool IsShiftDown() => IsDown(VirtualKey.Shift) || IsDown(VirtualKey.LeftShift) || IsDown(VirtualKey.RightShift);
    private static bool IsWinDown() => IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows);

    private static bool IsDown(VirtualKey key)
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush CreateCriticalBrush()
    {
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 164, 38, 44));
    }

}
