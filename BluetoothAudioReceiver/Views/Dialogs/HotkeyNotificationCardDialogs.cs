using BluetoothAudioReceiver.Helpers;
using BluetoothAudioReceiver.Services;
using BluetoothAudioReceiver.ViewModels.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace BluetoothAudioReceiver.Views.Dialogs;

internal static class HotkeyNotificationCardDialogs
{
    public static async Task<bool> ConfirmDeleteAsync(XamlRoot xamlRoot, ElementTheme theme, string hotkeyDisplay)
    {
        var confirmDialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "删除按键卡片",
            Content = $"确认删除 {hotkeyDisplay} 这张卡片吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = theme
        };

        return await confirmDialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<(string Hotkey, string Status)?> ShowEditorAsync(
        XamlRoot xamlRoot,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue,
        ElementTheme theme,
        HotkeyNotificationPageViewModel viewModel,
        IAudioRouteHotkeyService hotkeyService,
        string title,
        string initialHotkey,
        string initialStatus,
        string? ignoreCardId = null)
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
            Foreground = DialogVisualHelper.CreateCriticalBrush(),
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
        content.RequestedTheme = theme;

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
            XamlRoot = xamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = theme
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
            var validation = viewModel.ValidateHotkeyCandidate(hotkey, ignoreCardId);
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

            if (HotkeyTextHelper.IsModifierKey(key))
            {
                return;
            }

            var built = HotkeyTextHelper.TryBuildHotkeyText(key, modifiers);
            if (built is null)
            {
                return;
            }

            _ = dispatcherQueue.TryEnqueue(() =>
            {
                hotkey = built;
                hotkeyTextBox.Text = built;

                var validation = viewModel.ValidateHotkeyCandidate(built, ignoreCardId);
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

        hotkeyService.HotkeyCaptureKeyDown += OnHotkeyCaptureKeyDown;
        viewModel.SetHotkeyCaptureSuspended(true);
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
            hotkeyService.HotkeyCaptureKeyDown -= OnHotkeyCaptureKeyDown;
            viewModel.SetHotkeyCaptureSuspended(false);
        }
    }

    private static string NormalizeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
