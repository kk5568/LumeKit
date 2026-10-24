using BluetoothAudioReceiver.Helpers;
using BluetoothAudioReceiver.Helpers.Application;
using BluetoothAudioReceiver.ViewModels.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace BluetoothAudioReceiver.Views.Dialogs;

internal static class AudioHotkeyDialogs
{
    public static async Task AddOrEditAsync(
        XamlRoot xamlRoot,
        ElementTheme theme,
        BluetoothPageViewModel viewModel,
        HotkeyBindingCard? existingCard)
    {
        if (existingCard == null)
        {
            var hotkeyResult = await ShowHotkeyCaptureAsync(xamlRoot, theme, viewModel, string.Empty);
            if (!hotkeyResult.Saved)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(hotkeyResult.Hotkey))
            {
                viewModel.BindingStatusText = "快捷键不能为空";
                return;
            }

            await viewModel.AddHotkeyCardAsync(true, hotkeyResult.Hotkey, []);
            return;
        }

        var existingIds = existingCard.SelectedDeviceIds;
        var deviceResult = await ShowDeviceSelectionAsync(xamlRoot, theme, viewModel, existingIds);
        if (!deviceResult.Saved)
        {
            return;
        }

        if (!viewModel.TryResolveBindingType(deviceResult.SelectedIds, out var isInput, out var error))
        {
            viewModel.BindingStatusText = error;
            return;
        }

        if (existingCard.IsInputBinding == isInput)
        {
            await viewModel.UpdateHotkeyCardAsync(existingCard, existingCard.Hotkey, deviceResult.SelectedIds);
            return;
        }

        await viewModel.DeleteHotkeyCardAsync(existingCard);
        await viewModel.AddHotkeyCardAsync(isInput, existingCard.Hotkey, deviceResult.SelectedIds);
    }

    public static async Task<(bool Saved, string Hotkey)> ShowHotkeyCaptureAsync(
        XamlRoot xamlRoot,
        ElementTheme theme,
        BluetoothPageViewModel viewModel,
        string initialHotkey)
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
        content.RequestedTheme = theme;
        content.Children.Add(new TextBlock
        {
            Text = "快捷键",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(hotkeyTextBox);
        content.Children.Add(tipText);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "设置快捷键",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = theme
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

            if (HotkeyTextHelper.IsModifierKey(key))
            {
                return;
            }

            var built = HotkeyTextHelper.TryBuildHotkeyText(key, modifiers);
            if (built is null)
            {
                return;
            }

            hotkey = built;
            hotkeyTextBox.Text = built;
        }

        KeyEventHandler keyDownHandler = (_, args) =>
        {
            var modifiers = HotkeyTextHelper.GetCurrentModifiers(args);
            CaptureByKey(args.Key, modifiers, handled => args.Handled = handled);
        };
        TypedEventHandler<UIElement, ProcessKeyboardAcceleratorEventArgs> acceleratorHandler = (_, args) =>
        {
            CaptureByKey(args.Key, args.Modifiers, handled => args.Handled = handled);
        };

        content.KeyDown += keyDownHandler;
        content.ProcessKeyboardAccelerators += acceleratorHandler;
        dialog.Opened += (_, _) => hotkeyTextBox.Focus(FocusState.Programmatic);

        viewModel.SetHotkeyCaptureSuspended(true);
        try
        {
            var result = await dialog.ShowAsync();
            return (result == ContentDialogResult.Primary, BluetoothPageViewModel.NormalizeHotkey(hotkey));
        }
        finally
        {
            content.KeyDown -= keyDownHandler;
            content.ProcessKeyboardAccelerators -= acceleratorHandler;
            viewModel.SetHotkeyCaptureSuspended(false);
        }
    }

    public static async Task<(bool Saved, List<string> SelectedIds)> ShowDeviceSelectionAsync(
        XamlRoot xamlRoot,
        ElementTheme theme,
        BluetoothPageViewModel viewModel,
        IReadOnlyList<string> preselectedIds)
    {
        // 设备清单以「打开这一刻」为准：启动后新连上的设备（如蓝牙耳机）要能立刻出现在这里。
        await viewModel.EnsureAllDevicesLoadedAsync(forceReload: true);
        var choices = viewModel.GetUnifiedDeviceChoices();

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
            Foreground = DialogVisualHelper.CreateCriticalBrush()
        };

        var content = new StackPanel { Spacing = 10 };
        content.RequestedTheme = theme;
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            Content = listPanel
        });
        content.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "选择设备",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = theme
        };

        dialog.PrimaryButtonClick += (dialogSender, args) =>
        {
            var selected = checkboxes
                .Where(box => box.IsChecked == true)
                .Select(box => box.Tag?.ToString() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!viewModel.TryResolveBindingType(selected, out _, out var error))
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
}
