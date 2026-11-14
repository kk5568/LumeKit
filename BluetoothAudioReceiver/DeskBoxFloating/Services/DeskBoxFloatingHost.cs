using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.DeskBoxFloating.Profiles;
using BluetoothAudioReceiver.DeskBoxFloating.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Foundation;

namespace BluetoothAudioReceiver.DeskBoxFloating.Services;

public sealed class DeskBoxFloatingHost : IDeskBoxFloatingService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(DeskBoxFloatingHost));

    private const int IslandExpandedWidth = 600;
    private const int IslandExpandedHeight = 260;
    private const double TrayMenuItemWidth = 176;
    private const double TrayMenuMaxItemWidth = 320;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherTimer _menuHideTimer = new();
    private DeskBoxFloatingWindow? _window;
    private DeskBoxFloatingWindow? _islandWindow;
    private DeskBoxIslandPrototype? _islandPrototype;
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

    public void ShowContent(DeskBoxFloatingRequest request, FrameworkElement content)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);

        var normalizedRequest = Normalize(request);
        _dispatcherQueue.TryEnqueue(() => ShowDeskBoxFloatingWindow(normalizedRequest, content));
    }

    public void ShowIslandPreview(FrameworkElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        bool enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _log.Information("Showing island preview.");
                _islandWindow ??= CreateWindow();
                _islandWindow.Show(new DeskBoxFloatingRequest
                {
                    ProfileId = DeskBoxFloatingProfiles.NotificationDefault.Id,
                    PlacementOverride = DeskBoxFloatingPlacement.TopCenter,
                    WidthOverride = AppSettings.Current.IslandWidth,
                    HeightOverride = AppSettings.Current.IslandHeight,
                    AutoDismissOverride = false,
                    ActivateOnShowOverride = false,
                    MaterialTypeOverride = AppSettings.Current.IslandMaterialType,
                    IsIslandPreview = true
                }, content);
                _log.Information("Island preview shown.");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to show island preview.");
            }
        });

        if (!enqueued)
        {
            _log.Warning("Could not queue island preview because the UI queue is unavailable.");
        }
    }

    public void ShowIslandStatus(FrameworkElement content, int durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(content);
        int duration = Math.Clamp(durationMilliseconds, 500, 10_000);
        bool enqueued = _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _islandWindow ??= CreateWindow();
                _islandWindow.Show(new DeskBoxFloatingRequest
                {
                    ProfileId = DeskBoxFloatingProfiles.NotificationDefault.Id,
                    PlacementOverride = DeskBoxFloatingPlacement.TopCenter,
                    WidthOverride = AppSettings.Current.IslandWidth,
                    HeightOverride = AppSettings.Current.IslandHeight,
                    DurationMilliseconds = duration,
                    AutoDismissOverride = true,
                    ActivateOnShowOverride = false,
                    MaterialTypeOverride = AppSettings.Current.IslandMaterialType,
                    IsIslandPreview = true
                }, content);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to show island status.");
            }
        });

        if (!enqueued)
        {
            _log.Warning("Could not queue island status because the UI queue is unavailable.");
        }
    }

    public void DismissIslandPreview()
    {
        _dispatcherQueue.TryEnqueue(() => _islandWindow?.DismissNow());
    }

    public void RefreshIslandAppearance()
    {
        _dispatcherQueue.TryEnqueue(() => _islandWindow?.RefreshIslandAppearance());
    }

    public void CloseForExit()
    {
        _menuHideTimer.Stop();
        _previewMenu?.Hide();
        _islandWindow?.Close();
        _window?.Close();
        _islandWindow = null;
        _window = null;
    }

    public void ToggleIslandPreview(FrameworkElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_islandWindow?.Visible == true)
            {
                _islandWindow.DismissNow();
            }
            else
            {
                ShowIslandPreviewOnUiThread(content);
            }
        });
    }

    public void ToggleIslandPrototype()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_islandWindow?.Visible == true)
            {
                _islandWindow.DismissNow();
                _islandPrototype = null;
                return;
            }

            _islandPrototype = new DeskBoxIslandPrototype();
            _islandPrototype.ExpansionRequested += OnIslandPrototypeExpansionRequested;
            ShowIslandPreviewOnUiThread(_islandPrototype);
        });
    }

    private void OnIslandPrototypeExpansionRequested(object? sender, bool expanded)
    {
        if (_islandPrototype is null || _islandWindow is null)
        {
            return;
        }

        _islandPrototype.SetExpansionTarget(expanded);
        int width = expanded ? IslandExpandedWidth : AppSettings.Current.IslandWidth;
        int height = expanded ? IslandExpandedHeight : AppSettings.Current.IslandHeight;
        _islandWindow.AnimateIslandSize(
            width,
            height,
            expanded ? 1 : 0,
            (progress, _) => _islandPrototype?.ApplyExpansionProgress(progress));
    }

    private void ShowIslandPreviewOnUiThread(FrameworkElement content)
    {
        try
        {
            _log.Information("Showing island preview.");
            _islandWindow ??= CreateWindow();
            _islandWindow.Show(new DeskBoxFloatingRequest
            {
                ProfileId = DeskBoxFloatingProfiles.NotificationDefault.Id,
                PlacementOverride = DeskBoxFloatingPlacement.TopCenter,
                WidthOverride = AppSettings.Current.IslandWidth,
                HeightOverride = AppSettings.Current.IslandHeight,
                AutoDismissOverride = false,
                ActivateOnShowOverride = false,
                MaterialTypeOverride = AppSettings.Current.IslandMaterialType,
                IsIslandPreview = true
            }, content);
            _log.Information("Island preview shown.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to show island preview.");
        }
    }

    private void ShowDeskBoxFloatingWindow(DeskBoxFloatingRequest request, FrameworkElement? content = null)
    {
        _menuHideTimer.Stop();
        _previewMenu?.Hide();
        _window ??= CreateWindow();
        _window.Show(request, content);
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
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_islandWindow, window))
            {
                _islandWindow = null;
            }
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
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
