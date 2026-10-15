using BluetoothAudioReceiver.Services;
using BluetoothAudioReceiver.Views.Dialogs;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class HomePage : Page
{
    private readonly IAudioRouteHotkeyService _hotkeyService;

    public HomePageViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = Ioc.Default.GetRequiredService<HomePageViewModel>();
        _hotkeyService = Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        await ViewModel.Bluetooth.OnNavigatedToAsync(null!);
        await ViewModel.HotkeyNotifications.LoadCardsAsync();
    }

    private async void OnAddHotkeyCardClick(object sender, RoutedEventArgs e)
    {
        await AudioHotkeyDialogs.AddOrEditAsync(XamlRoot, ActualTheme, ViewModel.Bluetooth, existingCard: null);
    }

    private async void OnAddHotkeyNotificationCardClick(object sender, RoutedEventArgs e)
    {
        var result = await HotkeyNotificationCardDialogs.ShowEditorAsync(
            XamlRoot,
            DispatcherQueue,
            ActualTheme,
            ViewModel.HotkeyNotifications,
            _hotkeyService,
            "添加按键卡片",
            HotkeyNotificationCard.DefaultHotkey,
            HotkeyNotificationCard.DefaultStatus);

        if (result is null)
        {
            return;
        }

        await ViewModel.HotkeyNotifications.AddCardAsync(result.Value.Hotkey, result.Value.Status);
    }

    private void OnAddAutomationCardClick(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationEditorPage));
    }

    private async void OnEditHotkeyCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await AudioHotkeyDialogs.AddOrEditAsync(XamlRoot, ActualTheme, ViewModel.Bluetooth, card);
    }

    private async void OnDeleteHotkeyCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await ViewModel.Bluetooth.DeleteHotkeyCardAsync(card);
    }

    private async void OnEditHotkeyNotificationCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyNotificationCard card)
        {
            return;
        }

        var result = await HotkeyNotificationCardDialogs.ShowEditorAsync(
            XamlRoot,
            DispatcherQueue,
            ActualTheme,
            ViewModel.HotkeyNotifications,
            _hotkeyService,
            "编辑按键卡片",
            card.Hotkey,
            card.StatusText,
            card.Id);

        if (result is null)
        {
            return;
        }

        await ViewModel.HotkeyNotifications.UpdateCardAsync(card, result.Value.Hotkey, result.Value.Status);
    }

    private async void OnDeleteHotkeyNotificationCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyNotificationCard card)
        {
            return;
        }

        if (await HotkeyNotificationCardDialogs.ConfirmDeleteAsync(XamlRoot, ActualTheme, card.HotkeyDisplay))
        {
            await ViewModel.HotkeyNotifications.DeleteCardAsync(card);
        }
    }

    private void OnEditAutomationCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AutomationListItem item)
        {
            Frame.Navigate(typeof(AutomationEditorPage), item.Id);
        }
    }

    private async void OnTestAutomationCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AutomationListItem item)
        {
            await ViewModel.Automations.TestAsync(item);
        }
    }

    private async void OnDeleteAutomationCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationListItem item)
        {
            return;
        }

        if (await AutomationCardDialogs.ConfirmDeleteAsync(XamlRoot, ActualTheme, item.Name))
        {
            await ViewModel.Automations.RemoveAsync(item);
        }
    }
}
