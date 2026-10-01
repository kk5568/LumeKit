using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutomationHubPage : Page
{
    public AutomationHubPageViewModel ViewModel { get; }

    public AutomationHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AutomationHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void AutomationSettingsEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationBuilderPage));
    }

    private void AutomationListEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationListPage));
    }

    private void AutomationGameListEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationGameListPage));
    }
}
