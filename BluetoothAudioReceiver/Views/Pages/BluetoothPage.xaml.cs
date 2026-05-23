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
        if (sender is not Button button || button.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await AddOrEditHotkeyCardAsync(card);
    }

    private async void OnDeleteHotkeyCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await ViewModel.DeleteHotkeyCardAsync(card);
    }

    private async Task AddOrEditHotkeyCardAsync(HotkeyBindingCard? existingCard)
    {
        var initialHotkey = existingCard?.Hotkey ?? string.Empty;
        var existingIds = existingCard?.SelectedDeviceIds ?? [];

        var hotkeyResult = await ShowHotkeyCaptureDialogAsync(initialHotkey);
        if (!hotkeyResult.Saved)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(hotkeyResult.Hotkey))
        {
            ViewModel.BindingStatusText = "快捷键不能为空";
            return;
        }

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

        if (existingCard == null)
        {
            await ViewModel.AddHotkeyCardAsync(isInput, hotkeyResult.Hotkey, deviceResult.SelectedIds);
            return;
        }

        if (existingCard.IsInputBinding == isInput)
        {
            await ViewModel.UpdateHotkeyCardAsync(existingCard, hotkeyResult.Hotkey, deviceResult.SelectedIds);
            return;
        }

        await ViewModel.DeleteHotkeyCardAsync(existingCard);
        await ViewModel.AddHotkeyCardAsync(isInput, hotkeyResult.Hotkey, deviceResult.SelectedIds);
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
            Text = "按 Esc 可取消录制。",
            Opacity = 0.7
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "快捷键",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(hotkeyTextBox);
        content.Children.Add(new Rectangle
        {
            Height = 1,
            Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
        });
        content.Children.Add(tipText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "设置快捷键",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        void CaptureByKey(VirtualKey key, VirtualKeyModifiers modifiers, Action<bool> markHandled)
        {
            if (key == VirtualKey.Escape)
            {
                hotkey = string.Empty;
                hotkeyTextBox.Text = string.Empty;
                markHandled(true);
                return;
            }

            if (IsModifierKey(key))
            {
                markHandled(true);
                return;
            }

            var built = TryBuildHotkeyText(key, modifiers);
            if (built is null)
            {
                return;
            }

            hotkey = built;
            hotkeyTextBox.Text = built;
            markHandled(true);
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
        await ViewModel.EnsureDevicesLoadedAsync(isInput: true);
        await ViewModel.EnsureDevicesLoadedAsync(isInput: false);
        var choices = ViewModel.GetUnifiedDeviceChoices();

        var selectedSet = new HashSet<string>(
            preselectedIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var checkboxes = new List<CheckBox>();

        var listPanel = new StackPanel { Spacing = 6 };
        var comboModeLabel = new TextBlock
        {
            Text = "组合选项",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.85
        };
        var comboModeBox = new ComboBox
        {
            SelectedIndex = 0,
            Margin = new Thickness(0, 2, 0, 6)
        };
        comboModeBox.Items.Add("全连/全断（推荐）");
        comboModeBox.Items.Add("仅切换主设备");
        listPanel.Children.Add(comboModeLabel);
        listPanel.Children.Add(comboModeBox);

        foreach (var choice in choices)
        {
            var devicePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            devicePanel.Children.Add(new FontIcon
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = ResolveDeviceGlyph(choice.Name),
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
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed)
        };

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "选择设备",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            Content = listPanel
        });
        content.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "编辑设备",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (dialogSender, args) =>
        {
            var selected = checkboxes
                .Where(box => box.IsChecked == true)
                .Select(box => box.Tag?.ToString() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!ViewModel.TryResolveBindingType(selected, out var resolvedIsInput, out var error))
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
        if (parts.Count == 0) return null;

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
            _ => null
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

    private static string ResolveDeviceGlyph(string rawName)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "Unknown Device" : rawName.Trim();
        var lower = name.ToLowerInvariant();
        var isTablet = name.Contains("平板", StringComparison.OrdinalIgnoreCase) || lower.Contains("pad") || lower.Contains("tablet");
        var isPhoneKeyword = name.Contains("手机", StringComparison.OrdinalIgnoreCase) || lower.Contains("phone") || lower.Contains("iphone");
        var isPhoneBrandModel = (lower.Contains("xiaomi") || lower.Contains("redmi") || lower.Contains("huawei") || lower.Contains("honor") || lower.Contains("oppo") || lower.Contains("vivo"))
            && !isTablet;

        if (isPhoneKeyword || isPhoneBrandModel)
        {
            return "\uE8EA";
        }

        if (isTablet)
        {
            return "\uE70A";
        }

        if (name.Contains("耳机", StringComparison.OrdinalIgnoreCase) || lower.Contains("headphone") || lower.Contains("headset"))
        {
            return "\uE7F6";
        }

        if (name.Contains("显示器", StringComparison.OrdinalIgnoreCase) || lower.Contains("hdmi") || lower.Contains("monitor") || lower.Contains("display"))
        {
            return "\uE7F8";
        }

        return "\uE7F5";
    }
}
