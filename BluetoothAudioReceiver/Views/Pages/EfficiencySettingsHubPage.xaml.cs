using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BluetoothAudioReceiver.ViewModels.Pages;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class EfficiencySettingsHubPage : Page
{
    public EfficiencySettingsHubPageViewModel ViewModel { get; }

    public EfficiencySettingsHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<EfficiencySettingsHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void BackgroundEfficiencyEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(BackgroundEfficiencyPage));
    }
}
