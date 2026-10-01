using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BluetoothAudioReceiver.ViewModels.Pages;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AudioNotificationHubPage : Page
{
    public AudioNotificationHubPageViewModel ViewModel { get; }

    public AudioNotificationHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AudioNotificationHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void BluetoothEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(BluetoothPage));
    }

    private void NotificationEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(NotificationPage));
    }

    private void HotkeyNotificationEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(HotkeyNotificationPage));
    }
}
