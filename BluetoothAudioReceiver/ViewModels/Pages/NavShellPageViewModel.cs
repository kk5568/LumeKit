using CommunityToolkit.Mvvm.ComponentModel;
using BluetoothAudioReceiver.Helpers.Application;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class NavShellPageViewModel : ObservableRecipient
{
    [ObservableProperty]
    public partial bool IsBackEnabled { get; set; }

    [ObservableProperty]
    public partial object? Selected { get; set; }

    /// <summary>
    /// 应用默认标题。
    /// 当导航还没选中任何页面时，用这个值兜底。
    /// </summary>
    public string AppHeader => ConstantHelper.AppDisplayName;

    /// <summary>
    /// 当前导航页标题。
    /// 直接读取当前选中的 NavigationViewItem 文案，避免标题和页面不同步。
    /// </summary>
    public string SelectedHeader => Selected is NavigationViewItem selectedItem && selectedItem.Content is string text
        ? text
        : AppHeader;

    public INavigationService NavigationService { get; }

    public INavigationViewService NavigationViewService { get; }

    public NavShellPageViewModel(INavigationService navigationService, INavigationViewService shellService)
    {
        NavigationService = navigationService;
        NavigationViewService = shellService;
        NavigationService.Navigated += OnNavigated;
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        // Update the back button status
        IsBackEnabled = NavigationService.CanGoBack;

        // Update the selected NavigationViewItem based on the page type
        var selectedItem = NavigationViewService.GetItem(e.SourcePageType);
        if (selectedItem != null)
        {
            Selected = selectedItem;
            OnPropertyChanged(nameof(SelectedHeader));
        }
    }
}
