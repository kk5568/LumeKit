using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.DeskBoxFloating.Profiles;
using BluetoothAudioReceiver.DeskBoxFloating.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace BluetoothAudioReceiver.DeskBoxFloating.Services;

public sealed class DeskBoxFloatingHost : IDeskBoxFloatingService
{
    private const double TrayMenuItemWidth = 176;
    private const double TrayMenuMaxItemWidth = 320;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherTimer _menuHideTimer = new();
    private DeskBoxFloatingWindow? _window;
    private MenuFlyout? _previewMenu;

    public event EventHandler? Dismissed;

    public DeskBoxFloatingHost(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        _menuHideTimer.Tick += OnMenuHideTimerTick;
    }

    public void Show(DeskBoxFloatingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = Normalize(request);
        _dispatcherQueue.TryEnqueue(() =>
        {
            // 通知类走独立浮窗。托盘面板和右键菜单暂时保留旧的 Flyout 实现，
            // 等各自独立窗口完成后，删除这条分支和 ShowDeskBoxFloatingMenu 整套方法。
            if (normalizedRequest.Kind == DeskBoxFloatingKind.Notification)
            {
                ShowDeskBoxFloatingWindow(normalizedRequest);
            }
            else
            {
                ShowDeskBoxFloatingMenu(normalizedRequest);
            }
        });
    }

    private void ShowDeskBoxFloatingWindow(DeskBoxFloatingRequest request)
    {
        _menuHideTimer.Stop();
        _previewMenu?.Hide();
        _window ??= CreateWindow();
        _window.Show(request);
    }

    public void Dismiss()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _previewMenu?.Hide();
            _window?.DismissNow();
        });
    }

    private DeskBoxFloatingWindow CreateWindow()
    {
        var window = new DeskBoxFloatingWindow();
        window.Closed += (_, _) => _window = null;
        window.Dismissed += (_, args) => Dismissed?.Invoke(this, args);
        return window;
    }

    private static DeskBoxFloatingRequest Normalize(DeskBoxFloatingRequest request)
    {
        var profile = DeskBoxFloatingProfiles.Resolve(request.ProfileId, request.Kind);
        return request with
        {
            ProfileId = profile.Id,
            DurationMilliseconds = request.DurationMilliseconds switch
            {
                5000 => 5000,
                10000 => 10000,
                _ => profile.DefaultDurationMilliseconds
            },
            WidthOverride = NormalizeDimension(request.WidthOverride),
            HeightOverride = NormalizeDimension(request.HeightOverride)
        };
    }

    private static int? NormalizeDimension(int? value)
        => value is > 0 and <= 1200 ? value : null;

    private void ShowDeskBoxFloatingMenu(DeskBoxFloatingRequest request)
    {
        _window?.DismissNow();
        _previewMenu?.Hide();
        _previewMenu = CreateDeskBoxFloatingMenu(request);

        if (App.MainWindow.Content is not FrameworkElement anchor)
        {
            return;
        }

        double itemWidth = ResolveMenuItemWidth(request);

        var options = new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            Position = new Point(
                Math.Max(0, anchor.ActualWidth - itemWidth - 32),
                96)
        };

        _previewMenu.ShowAt(anchor, options);

        _menuHideTimer.Stop();
        if (request.AutoDismissOverride ?? true)
        {
            _menuHideTimer.Interval = TimeSpan.FromMilliseconds(request.DurationMilliseconds);
            _menuHideTimer.Start();
        }
    }

    private static MenuFlyout CreateDeskBoxFloatingMenu(DeskBoxFloatingRequest request)
    {
        var menu = new MenuFlyout
        {
            ShouldConstrainToRootBounds = false,
            MenuFlyoutPresenterStyle = CreateTrayMenuPresenterStyle()
        };

        double itemWidth = ResolveMenuItemWidth(request);
        string title = string.IsNullOrWhiteSpace(request.Title)
            ? "通知"
            : request.Title;
        string status = string.IsNullOrWhiteSpace(request.Status)
            ? string.Empty
            : $"  ·  {request.Status}";

        menu.Items.Add(CreateMenuItem(
            title + status,
            string.IsNullOrWhiteSpace(request.IconGlyph) ? "\uE765" : request.IconGlyph,
            itemWidth,
            menu));

        if (!string.IsNullOrWhiteSpace(request.Description) || request.Items.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            menu.Items.Add(CreateMenuItem(
                request.Description,
                "\uE946",
                itemWidth,
                menu));
        }

        foreach (var item in request.Items)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                menu.Items.Add(CreateMenuItem(item, "\uE73E", itemWidth, menu));
            }
        }

        return menu;
    }

    private static MenuFlyoutItem CreateMenuItem(
        string text,
        string glyph,
        double width,
        MenuFlyout menu)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Width = width,
            Icon = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        };
        item.Click += (_, _) => menu.Hide();
        item.Loaded += OnMenuItemLoaded;
        return item;
    }

    private static double ResolveMenuItemWidth(DeskBoxFloatingRequest request)
    {
        var profile = DeskBoxFloatingProfiles.Resolve(request.ProfileId, request.Kind);
        int requestedWidth = request.WidthOverride ?? profile.Width;
        return Math.Clamp(requestedWidth - 16, TrayMenuItemWidth, TrayMenuMaxItemWidth);
    }

    private static Style CreateTrayMenuPresenterStyle()
    {
        var style = new Style(typeof(MenuFlyoutPresenter))
        {
            BasedOn = Application.Current.Resources[typeof(MenuFlyoutPresenter)] as Style
        };
        style.Setters.Add(new Setter(
            ScrollViewer.VerticalScrollModeProperty,
            ScrollMode.Disabled));
        style.Setters.Add(new Setter(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled));
        style.Setters.Add(new Setter(
            FrameworkElement.MaxHeightProperty,
            double.PositiveInfinity));
        return style;
    }

    private static void OnMenuItemLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuFlyoutItemBase item)
        {
            return;
        }

        ApplyMenuPresenterSettings(item);
    }

    private static void ApplyMenuPresenterSettings(MenuFlyoutItemBase item)
    {
        var presenter = FindVisualAncestor<MenuFlyoutPresenter>(item);
        if (presenter is null)
        {
            return;
        }

        presenter.SetValue(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Disabled);
        presenter.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        presenter.MaxHeight = double.PositiveInfinity;

        if (presenter.XamlRoot is null)
        {
            return;
        }

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(presenter.XamlRoot))
        {
            if (popup.Child is not null && ContainsVisual(popup.Child, presenter))
            {
                popup.ShouldConstrainToRootBounds = false;
                break;
            }
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        for (DependencyObject? current = child;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static bool ContainsVisual(DependencyObject root, DependencyObject target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            if (ContainsVisual(VisualTreeHelper.GetChild(root, index), target))
            {
                return true;
            }
        }

        return false;
    }

    private void OnMenuHideTimerTick(object? sender, object e)
    {
        _menuHideTimer.Stop();
        _previewMenu?.Hide();
    }
}
