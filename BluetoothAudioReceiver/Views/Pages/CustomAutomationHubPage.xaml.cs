using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class CustomAutomationHubPage : Page
{
    public CustomAutomationHubPageViewModel ViewModel { get; }

    public CustomAutomationHubPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<CustomAutomationHubPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void AutomationListEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationListPage));
    }

    private void AutomationSettingsEntry_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationBuilderPage));
    }

}
