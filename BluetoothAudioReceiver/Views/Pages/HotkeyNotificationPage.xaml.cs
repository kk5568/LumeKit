using BluetoothAudioReceiver.Services;
using BluetoothAudioReceiver.Views.Dialogs;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        var result = await HotkeyNotificationCardDialogs.ShowEditorAsync(
            XamlRoot,
            DispatcherQueue,
            ActualTheme,
            ViewModel,
            _hotkeyService,
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

        var result = await HotkeyNotificationCardDialogs.ShowEditorAsync(
            XamlRoot,
            DispatcherQueue,
            ActualTheme,
            ViewModel,
            _hotkeyService,
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

        if (await HotkeyNotificationCardDialogs.ConfirmDeleteAsync(XamlRoot, ActualTheme, card.HotkeyDisplay))
        {
            await ViewModel.DeleteCardAsync(card);
        }
    }
}
