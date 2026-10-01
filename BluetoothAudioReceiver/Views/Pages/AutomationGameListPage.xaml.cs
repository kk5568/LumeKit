using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutomationGameListPage : Page
{
    public AutomationGameListPageViewModel ViewModel { get; }

    public AutomationGameListPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AutomationGameListPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveGameListCommand.Execute(null);
    }

    private void OnRestoreDefaultClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RestoreToDefaultGameListCommand.Execute(null);
    }

    private async void OnAddDefaultProgramClick(object sender, RoutedEventArgs e)
    {
        var executable = await ShowAddProgramDialogAsync("添加程序", "程序名", "例如：notepad.exe");
        if (!string.IsNullOrWhiteSpace(executable))
        {
            ViewModel.AddProgramToDefaultList(executable);
        }
    }

    private async void OnAddGameProgramClick(object sender, RoutedEventArgs e)
    {
        var executable = await ShowAddProgramDialogAsync("添加游戏程序", "程序名", "例如：cs2.exe");
        if (!string.IsNullOrWhiteSpace(executable))
        {
            ViewModel.AddProgramToGameList(executable);
        }
    }

    private void OnScanInstalledProgramsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ScanInstalledProgramsCommand.Execute(null);
    }

    private async Task<string?> ShowAddProgramDialogAsync(string title, string header, string placeholderText)
    {
        var executableBox = new TextBox
        {
            Header = header,
            PlaceholderText = placeholderText
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = executableBox,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(executableBox.Text))
        {
            return null;
        }

        return executableBox.Text.Trim();
    }
}
