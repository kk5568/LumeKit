using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class OtherSettingsHubPage : Page
{
    public OtherSettingsHubPageViewModel ViewModel { get; }

    public OtherSettingsHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<OtherSettingsHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void NotificationEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(NotificationPage));
    }

}
