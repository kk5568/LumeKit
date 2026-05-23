using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutoBrightnessPage : Page
{
    public AutoBrightnessPageViewModel ViewModel { get; }

    public AutoBrightnessPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AutoBrightnessPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
