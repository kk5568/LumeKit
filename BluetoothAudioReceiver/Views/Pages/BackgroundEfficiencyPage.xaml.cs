using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class BackgroundEfficiencyPage : Page
{
    public BackgroundEfficiencyPageViewModel ViewModel { get; }

    public BackgroundEfficiencyPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<BackgroundEfficiencyPageViewModel>();
        ViewModel.Initialize();
        InitializeComponent();
    }

    private void OnSaveWhitelistClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveProcessWhitelistCommand.Execute(null);
    }

    private void OnRestoreWhitelistDefaultClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RestoreToDefaultProcessWhitelistCommand.Execute(null);
    }

    private void OnSaveBlacklistClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveProcessBlacklistCommand.Execute(null);
    }

    private void OnRestoreBlacklistDefaultClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RestoreToDefaultProcessBlacklistCommand.Execute(null);
    }
}
