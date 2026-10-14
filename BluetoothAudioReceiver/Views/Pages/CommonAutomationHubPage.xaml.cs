using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class CommonAutomationHubPage : Page
{
    public CommonAutomationHubPageViewModel ViewModel { get; }

    public CommonAutomationHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<CommonAutomationHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void BluetoothEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(BluetoothPage));
    }

    private void HotkeyNotificationEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(HotkeyNotificationPage));
    }

}
