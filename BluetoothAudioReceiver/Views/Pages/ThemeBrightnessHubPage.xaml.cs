using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BluetoothAudioReceiver.ViewModels.Pages;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class ThemeBrightnessHubPage : Page
{
    public ThemeBrightnessHubPageViewModel ViewModel { get; }

    public ThemeBrightnessHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<ThemeBrightnessHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void ThemeManagementEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ThemeManagementPage));
    }

    private void AutoBrightnessEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutoBrightnessPage));
    }
}
