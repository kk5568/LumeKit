using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class IslandPage : Page
{
    public IslandPageViewModel ViewModel { get; }

    public IslandPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<IslandPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
