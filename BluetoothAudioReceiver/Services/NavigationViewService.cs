using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Services;

internal class NavigationViewService(IPageService pageService) : INavigationViewService
{
    private INavigationService _navigationService = null!;
    private readonly IPageService _pageService = pageService;

    private NavigationView? _navigationView;

    public IList<object> MenuItems => _navigationView?.MenuItems ?? [];

    public IList<object> FooterMenuItems => _navigationView?.FooterMenuItems ?? [];

    public object? SettingsItem => _navigationView?.SettingsItem;

    [MemberNotNull(nameof(_navigationView))]
    public void Initialize(NavigationView navigationView)
    {
        _navigationService = Ioc.Default.GetRequiredService<INavigationService>();
        _navigationView = navigationView;
        _navigationView.BackRequested += OnBackRequested;
        _navigationView.ItemInvoked += OnItemInvoked;
    }

    public void UnregisterEvents()
    {
        if (_navigationView != null)
        {
            _navigationView.BackRequested -= OnBackRequested;
            _navigationView.ItemInvoked -= OnItemInvoked;
        }
    }

    public NavigationViewItem? GetSelectedItem()
    {
        return _navigationView?.SelectedItem as NavigationViewItem;
    }

    public void SetNavigateTo(string navigateTo)
    {
        _navigationView!.ItemInvoked -= OnItemInvoked;
        if (navigateTo == _pageService.SettingPageKey)
        {
            _navigationView.SelectedItem = _navigationView.SettingsItem;
        }
        else
        {
            var item = FindMenuItem(
                item => NavigationHelper.GetNavigateTo(item) is string pageKey && pageKey == navigateTo);
            if (item != null)
            {
                _navigationView.SelectedItem = item;
            }
        }
        _navigationView.ItemInvoked += OnItemInvoked;
    }

    public NavigationViewItem? GetItem(Type pageType)
    {
        if (_navigationView != null)
        {
            if (pageType == _pageService.SettingPageType)
            {
                return _navigationView.SettingsItem as NavigationViewItem;
            }

            return FindMenuItem(
                item => NavigationHelper.GetNavigateTo(item) is string pageKey &&
                        _pageService.GetPageType(pageKey) == pageType);
        }

        return null;
    }

    private NavigationViewItem? FindMenuItem(Func<NavigationViewItem, bool> matches)
    {
        return FindMenuItem(MenuItems.Concat(FooterMenuItems), matches);
    }

    private static NavigationViewItem? FindMenuItem(IEnumerable<object> items, Func<NavigationViewItem, bool> matches)
    {
        foreach (var item in items)
        {
            if (item is not NavigationViewItem navigationViewItem)
            {
                continue;
            }

            if (matches(navigationViewItem))
            {
                return navigationViewItem;
            }

            var child = FindMenuItem(navigationViewItem.MenuItems, matches);
            if (child != null)
            {
                // 二级菜单默认收起，选中子项时把父级展开，保证高亮可见。
                navigationViewItem.IsExpanded = true;
                return child;
            }
        }

        return null;
    }

    private async void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        await _navigationService.GoBackAsync();
    }

    private async void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            await _navigationService.NavigateToAsync(typeof(SettingsPageViewModel).FullName!);
        }
        else
        {
            var selectedItem = args.InvokedItemContainer as NavigationViewItem;

            if (selectedItem?.GetValue(NavigationHelper.NavigateToProperty) is string pageKey)
            {
                await _navigationService.NavigateToAsync(pageKey);
            }
        }
    }
}
