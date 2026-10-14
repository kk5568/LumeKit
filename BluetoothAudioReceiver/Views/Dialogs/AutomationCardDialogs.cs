using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Dialogs;

internal static class AutomationCardDialogs
{
    public static async Task<bool> ConfirmDeleteAsync(XamlRoot xamlRoot, ElementTheme theme, string name)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "删除自动化",
            Content = $"确认删除“{name}”？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = theme
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
