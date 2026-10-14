using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class BackgroundTasksHubPage : Page
{
    public BackgroundTasksHubPageViewModel ViewModel { get; }

    public BackgroundTasksHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<BackgroundTasksHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void BackgroundEfficiencyEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(BackgroundEfficiencyPage));
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
