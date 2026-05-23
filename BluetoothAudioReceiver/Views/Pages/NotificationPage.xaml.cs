using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class NotificationPage : Page
{
    public NotificationPageViewModel ViewModel { get; }

    public NotificationPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<NotificationPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
