using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class ThemeManagementPage : Page
{
    public ThemeManagementPageViewModel ViewModel { get; }

    public ThemeManagementPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<ThemeManagementPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
