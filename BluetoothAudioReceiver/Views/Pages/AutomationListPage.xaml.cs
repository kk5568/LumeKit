using BluetoothAudioReceiver.ViewModels.Pages;
using BluetoothAudioReceiver.Views.Dialogs;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutomationListPage : Page
{
    public AutomationListPageViewModel ViewModel { get; }

    public AutomationListPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AutomationListPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OnAddCardClick(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(AutomationEditorPage));
    }

    private void OnEditCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is AutomationListItem item)
        {
            Frame.Navigate(typeof(AutomationEditorPage), item.Id);
        }
    }

    private async void OnTestCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is AutomationListItem item)
        {
            await ViewModel.TestAsync(item);
        }
    }

    private async void OnDeleteCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationListItem item)
        {
            return;
        }

        if (await AutomationCardDialogs.ConfirmDeleteAsync(XamlRoot, ActualTheme, item.Name))
        {
            await ViewModel.RemoveAsync(item);
        }
    }
}
