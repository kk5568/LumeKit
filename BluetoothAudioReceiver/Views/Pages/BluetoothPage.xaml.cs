using BluetoothAudioReceiver.Views.Dialogs;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        await AudioHotkeyDialogs.AddOrEditAsync(XamlRoot, ActualTheme, ViewModel, existingCard: null);
    }

    private async void OnEditHotkeyCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await AudioHotkeyDialogs.AddOrEditAsync(XamlRoot, ActualTheme, ViewModel, card);
    }

    private async void OnDeleteHotkeyCardButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotkeyBindingCard card)
        {
            return;
        }

        await ViewModel.DeleteHotkeyCardAsync(card);
    }
}
