using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutomationBuilderPage : Page
{
    public AutomationBuilderPageViewModel ViewModel { get; }

    public AutomationBuilderPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AutomationBuilderPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationEditorPage));
    }
}
