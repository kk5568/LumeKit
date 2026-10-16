using System.Linq;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Devices.Enumeration;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.Views.Windows;
using DrawingPoint = System.Drawing.Point;

namespace BluetoothAudioReceiver.UserControls;

public sealed partial class TrayMenuControl : UserControl
{
    private const double TrayMenuItemWidth = 176;
    private const int TrayContextMenuEstimatedWidth = (int)TrayMenuItemWidth + 16;
    private const int TrayContextMenuFallbackOffsetPixels = 24;

    // 浮层已经被这次点击的“点外部关闭”关掉时，不要让同一次托盘左键又把它打开。
    private static readonly TimeSpan QuickPanelReopenGuard = TimeSpan.FromMilliseconds(400);

    public TrayMenuControlViewModel ViewModel { get; } = new();
    private readonly IAudioPlaybackConnectionService _connectionService;
    private Window? _trayWindow;
    private TaskbarIcon? _trayIcon;
    private MenuFlyout? _contextMenu;
    private MenuFlyoutItem? _trayShowWindowItem;
    private readonly IDeskBoxFloatingService _floatingService;
    private TrayAudioQuickPanelControl? _quickPanelContent;
    private bool _quickPanelVisible;
    private DateTimeOffset _quickPanelClosedAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public TrayMenuControl()
    {
        InitializeComponent();
        _contextMenu = CreateContextMenu();
        CreateTrayHostWindow();

        _floatingService = Ioc.Default.GetRequiredService<IDeskBoxFloatingService>();
        _floatingService.Dismissed += OnFloatingDismissed;

        _connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
        _connectionService.DeviceConnected += OnDeviceConnected;
        _connectionService.DeviceDisconnected += OnDeviceDisconnected;

        UpdateConnectionState(_connectionService);
    }

    private void CreateTrayHostWindow()
    {
        _trayWindow = new Window();
        _trayWindow.AppWindow.IsShownInSwitchers = false;
        _trayWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1, 1));

        _trayIcon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "speakericon3_32x32.ico")),
            ToolTipText = ViewModel.TrayIconToolTip,
            ContextMenuMode = ContextMenuMode.SecondWindow,
            MenuActivation = PopupActivationMode.None,
            NoLeftClickDelay = true,
            RightClickCommand = new RelayCommand(ShowContextMenuFromTray),
            LeftClickCommand = new RelayCommand(ShowQuickPanel),
            ContextFlyout = _contextMenu
        };
        _trayIcon.SecondWindowContextMenuOpened += OnSecondWindowContextMenuOpened;

        if (_trayWindow.Content is null)
        {
            _trayWindow.Content = new Grid
            {
                Width = 1,
                Height = 1,
                MinWidth = 1,
                MinHeight = 1,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };
        }

        if (_trayWindow.Content is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            panel.Width = 1;
            panel.Height = 1;
            panel.MinWidth = 1;
            panel.MinHeight = 1;
            panel.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            panel.Children.Clear();
            panel.Children.Add(_trayIcon);
        }

        _trayWindow.Activate();

        if (!_trayIcon.IsCreated)
        {
            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        }

        _trayWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (_trayWindow is not null)
            {
                WindowExtensions.Hide(_trayWindow, enableEfficiencyMode: false);
            }
        });
    }

    private void UpdateConnectionState(IAudioPlaybackConnectionService connectionService)
    {
        var connectedDevices = connectionService.ConnectedDevices;
        if (connectedDevices.Count > 0)
        {
            var deviceNames = string.Join(", ", connectedDevices.Select(d => d.Name));
            ViewModel.IsConnected = true;
            ViewModel.ConnectedDeviceName = deviceNames;
            ViewModel.TrayIconToolTip = $"蓝牙音频接收器 - 已连接: {deviceNames}";
            if (_trayIcon is not null)
            {
                _trayIcon.ToolTipText = ViewModel.TrayIconToolTip;
            }
        }
        else
        {
            ViewModel.IsConnected = false;
            ViewModel.ConnectedDeviceName = null;
            ViewModel.TrayIconToolTip = "蓝牙音频接收器 - 未连接";
            if (_trayIcon is not null)
            {
                _trayIcon.ToolTipText = ViewModel.TrayIconToolTip;
            }
        }
    }

    private void OnDeviceConnected(object? sender, DeviceInformation device)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
            UpdateConnectionState(connectionService);
        });
    }

    private void OnDeviceDisconnected(object? sender, DeviceInformation device)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
            UpdateConnectionState(connectionService);
        });
    }

    [RelayCommand]
    private void ShowWindow()
    {
        App.MainWindow.Show();
        App.MainWindow.BringToFront();
    }

    [RelayCommand]
    private void RestartApp()
    {
        DisposeTrayIconControl();
        App.RestartApplication();
    }

    [RelayCommand]
    private void ShowQuickPanel()
    {
        if (_disposed)
        {
            return;
        }

        // 这一下点击已经先把浮层“点外部关闭”了，就不要再打开。
        if (DateTimeOffset.Now - _quickPanelClosedAt < QuickPanelReopenGuard)
        {
            return;
        }

        // 再点一次托盘先关掉；这也是「点浮层外面」「按 Esc」之外的最后一条关闭路径。
        if (_quickPanelVisible)
        {
            _floatingService.Dismiss();
            return;
        }

        var content = _quickPanelContent ??= CreateQuickPanelContent();
        // 新一次打开：卡片顺序按最新优先级重排，这次打开期间保持不变。
        content.ResetOrderSnapshot();
        _quickPanelVisible = true;
        TrayAudioQuickPanelControl.IsOpen = true;
        _ = ShowQuickPanelAsync(content);
    }

    // 托盘左键面板改用公共浮窗（DeskBoxFloatingWindow）承载：材质、圆角、阴影、从托盘升起的动画
    // 都用通知那套现成的；面板只画里面的卡片，外壳和定位不再自己写。
    private TrayAudioQuickPanelControl CreateQuickPanelContent()
    {
        var content = new TrayAudioQuickPanelControl();
        content.CloseRequested += (_, _) => _floatingService.Dismiss();
        content.PrepareForHost();
        return content;
    }

    private async Task ShowQuickPanelAsync(TrayAudioQuickPanelControl content)
    {
        try
        {
            await content.RefreshAsync();
        }
        catch
        {
            // 设备读不出来也让浮层弹出来，里面会显示「暂无设备」。
        }

        if (_disposed || !_quickPanelVisible)
        {
            return;
        }

        _floatingService.ShowContent(
            new DeskBoxFloatingRequest
            {
                Kind = DeskBoxFloatingKind.Panel,
                ProfileId = "Tray.QuickPanel",
                AnchorPoint = ResolveQuickPanelAnchorPoint(),
                AutoDismissOverride = false
            },
            content);
    }

    private void OnFloatingDismissed(object? sender, EventArgs e)
    {
        if (!_quickPanelVisible)
        {
            // 通知等别的浮层退场，不管。
            return;
        }

        _quickPanelVisible = false;
        TrayAudioQuickPanelControl.IsOpen = false;

        // 浮层可能是被这次左键的「点外部关闭」关掉的，记时间防止同一次点击又打开。
        _quickPanelClosedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// 托盘左键快捷中心的锚点：底部任务栏时直接用托盘图标自己的左边缘/上边缘（面板由浮窗做
    /// 「左对齐 + 右边贴屏」）；取不到图标或任务栏不在底部时，退回右键那套锚点（贴边不越界）。
    /// </summary>
    private global::Windows.Graphics.PointInt32 ResolveQuickPanelAnchorPoint()
    {
        if (!GetCursorPos(out var cursor))
        {
            return default;
        }

        try
        {
            if (TryGetTrayIconIdentity(out var windowHandle, out var trayIconId) &&
                TryGetNotifyIconRect(windowHandle, trayIconId, out var iconRect) &&
                IsUsableTrayIconRect(iconRect) &&
                TryGetMonitorWorkArea(
                    iconRect.Left + ((iconRect.Right - iconRect.Left) / 2),
                    iconRect.Top + ((iconRect.Bottom - iconRect.Top) / 2),
                    out var monitor,
                    out var workArea) &&
                GetNearestTaskbarEdge(iconRect, monitor, workArea) == TaskbarEdge.Bottom)
            {
                return new global::Windows.Graphics.PointInt32(iconRect.Left, iconRect.Top);
            }

            var point = GetTrayContextMenuAnchorPoint(new DrawingPoint(cursor.X, cursor.Y));
            return new global::Windows.Graphics.PointInt32(point.X, point.Y);
        }
        catch
        {
            return new global::Windows.Graphics.PointInt32(cursor.X, cursor.Y);
        }
    }

    [RelayCommand]
    private void ExitApp()
    {
        App.MainWindow.Hide();
        DisposeTrayIconControl();
#if TRAY_ICON
        App.CanCloseWindow = true;
#endif
        App.MainWindow.Close();
    }

    private MenuFlyout CreateContextMenu()
    {
        var menu = new MenuFlyout
        {
            ShouldConstrainToRootBounds = false,
            MenuFlyoutPresenterStyle = CreateTrayMenuPresenterStyle()
        };

        var showWindowItem = CreateMenuItem(
            "主界面",
            "\uE80F",
            () => RunTrayMenuAction(menu, ShowWindow));
        showWindowItem.Loaded += OnMenuItemLoaded;
        _trayShowWindowItem = showWindowItem;

        var restartItem = CreateMenuItem(
            "重启",
            "\uE777",
            () => RunTrayMenuAction(menu, RestartApp));

        var exitItem = CreateMenuItem(
            "退出",
            "\uE711",
            () => RunTrayMenuAction(menu, ExitApp));

        menu.Items.Add(showWindowItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(restartItem);
        menu.Items.Add(exitItem);

        return menu;
    }

    private static MenuFlyoutItem CreateMenuItem(
        string text,
        string glyph,
        Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Width = TrayMenuItemWidth,
            Icon = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static void RunTrayMenuAction(MenuFlyout menu, Action action)
    {
        menu.Hide();
        action();
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

        ApplySecondWindowMenuPresenterSettings(item);
    }

    private void OnSecondWindowContextMenuOpened(object? sender, EventArgs args)
    {
        if (_trayShowWindowItem is not null)
        {
            ApplySecondWindowMenuPresenterSettings(_trayShowWindowItem);
        }
    }

    private static void ApplySecondWindowMenuPresenterSettings(MenuFlyoutItemBase item)
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

    private void ShowContextMenuFromTray()
    {
        if (_disposed ||
            _contextMenu is null ||
            _trayIcon is null ||
            !_trayIcon.IsCreated ||
            !GetCursorPos(out var cursor))
        {
            return;
        }

        var fallbackPoint = new DrawingPoint(cursor.X, cursor.Y);
        DrawingPoint point;
        try
        {
            point = GetTrayContextMenuAnchorPoint(fallbackPoint);
        }
        catch
        {
            point = fallbackPoint;
        }

        try
        {
            _trayIcon.ShowContextMenu(point);
        }
        catch
        {
            // The tray icon can disappear while Explorer refreshes the
            // notification area. Ignore that click instead of terminating.
        }
    }

    private DrawingPoint GetTrayContextMenuAnchorPoint(DrawingPoint fallbackPoint)
    {
        if (TryGetTrayIconIdentity(out var windowHandle, out var trayIconId) &&
            TryGetNotifyIconRect(windowHandle, trayIconId, out var iconRect) &&
            IsUsableTrayIconRect(iconRect))
        {
            return GetTrayContextMenuAnchorPointFromIconRect(iconRect, fallbackPoint);
        }

        return GetFallbackTrayContextMenuAnchorPoint(fallbackPoint);
    }

    private static DrawingPoint GetTrayContextMenuAnchorPointFromIconRect(
        NativeRect iconRect,
        DrawingPoint fallbackPoint)
    {
        int centerX = iconRect.Left + ((iconRect.Right - iconRect.Left) / 2);
        int centerY = iconRect.Top + ((iconRect.Bottom - iconRect.Top) / 2);
        var anchor = new DrawingPoint(
            centerX - (TrayContextMenuEstimatedWidth / 2),
            centerY);

        if (!TryGetMonitorWorkArea(centerX, centerY, out var monitor, out var workArea))
        {
            return anchor;
        }

        anchor = GetNearestTaskbarEdge(iconRect, monitor, workArea) switch
        {
            TaskbarEdge.Bottom => new DrawingPoint(anchor.X, workArea.Bottom - 1),
            TaskbarEdge.Top => new DrawingPoint(anchor.X, workArea.Top),
            TaskbarEdge.Right => new DrawingPoint(workArea.Right - 1, centerY),
            TaskbarEdge.Left => new DrawingPoint(workArea.Left, centerY),
            _ => GetFallbackTrayContextMenuAnchorPoint(fallbackPoint)
        };

        return ClampPointToRect(anchor, monitor);
    }

    private static DrawingPoint GetFallbackTrayContextMenuAnchorPoint(DrawingPoint point)
    {
        if (!TryGetMonitorWorkArea(point.X, point.Y, out var monitor, out var workArea))
        {
            return new DrawingPoint(
                point.X - (TrayContextMenuEstimatedWidth / 2),
                point.Y - TrayContextMenuFallbackOffsetPixels);
        }

        int x = point.X - (TrayContextMenuEstimatedWidth / 2);
        int y = point.Y;
        bool moved = false;

        if (workArea.Bottom < monitor.Bottom && y >= workArea.Bottom)
        {
            y = workArea.Bottom - 1;
            moved = true;
        }
        else if (workArea.Top > monitor.Top && y <= workArea.Top)
        {
            y = workArea.Top;
            moved = true;
        }

        if (workArea.Right < monitor.Right && x >= workArea.Right)
        {
            x = workArea.Right - 1;
            moved = true;
        }
        else if (workArea.Left > monitor.Left && x <= workArea.Left)
        {
            x = workArea.Left;
            moved = true;
        }

        if (!moved)
        {
            int distanceToBottom = Math.Abs(monitor.Bottom - y);
            int distanceToTop = Math.Abs(y - monitor.Top);
            int distanceToRight = Math.Abs(monitor.Right - x);
            int distanceToLeft = Math.Abs(x - monitor.Left);
            int nearestDistance = Math.Min(
                Math.Min(distanceToBottom, distanceToTop),
                Math.Min(distanceToRight, distanceToLeft));

            if (nearestDistance == distanceToBottom)
            {
                y -= TrayContextMenuFallbackOffsetPixels;
            }
            else if (nearestDistance == distanceToTop)
            {
                y += TrayContextMenuFallbackOffsetPixels;
            }
            else if (nearestDistance == distanceToRight)
            {
                x -= TrayContextMenuFallbackOffsetPixels;
            }
            else
            {
                x += TrayContextMenuFallbackOffsetPixels;
            }
        }

        return ClampPointToRect(new DrawingPoint(x, y), monitor);
    }

    private static bool IsUsableTrayIconRect(NativeRect rect)
    {
        return rect.Right > rect.Left && rect.Bottom > rect.Top;
    }

    private static TaskbarEdge GetNearestTaskbarEdge(
        NativeRect iconRect,
        NativeRect monitor,
        NativeRect workArea)
    {
        if (workArea.Bottom < monitor.Bottom &&
            iconRect.Top >= workArea.Bottom)
        {
            return TaskbarEdge.Bottom;
        }

        if (workArea.Top > monitor.Top &&
            iconRect.Bottom <= workArea.Top)
        {
            return TaskbarEdge.Top;
        }

        if (workArea.Right < monitor.Right &&
            iconRect.Left >= workArea.Right)
        {
            return TaskbarEdge.Right;
        }

        if (workArea.Left > monitor.Left &&
            iconRect.Right <= workArea.Left)
        {
            return TaskbarEdge.Left;
        }

        int distanceToBottom = Math.Abs(monitor.Bottom - iconRect.Bottom);
        int distanceToTop = Math.Abs(iconRect.Top - monitor.Top);
        int distanceToRight = Math.Abs(monitor.Right - iconRect.Right);
        int distanceToLeft = Math.Abs(iconRect.Left - monitor.Left);
        int nearestDistance = Math.Min(
            Math.Min(distanceToBottom, distanceToTop),
            Math.Min(distanceToRight, distanceToLeft));

        if (nearestDistance == distanceToBottom)
        {
            return TaskbarEdge.Bottom;
        }

        if (nearestDistance == distanceToTop)
        {
            return TaskbarEdge.Top;
        }

        return nearestDistance == distanceToRight
            ? TaskbarEdge.Right
            : TaskbarEdge.Left;
    }

    private static DrawingPoint ClampPointToRect(
        DrawingPoint point,
        NativeRect rect)
    {
        return new DrawingPoint(
            Math.Clamp(point.X, rect.Left, rect.Right - 1),
            Math.Clamp(point.Y, rect.Top, rect.Bottom - 1));
    }

    private static bool TryGetMonitorWorkArea(
        int x,
        int y,
        out NativeRect monitor,
        out NativeRect workArea)
    {
        monitor = default;
        workArea = default;

        IntPtr monitorHandle = MonitorFromPoint(
            new NativePoint { X = x, Y = y },
            MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>()
        };
        if (!GetMonitorInfoW(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        monitor = monitorInfo.Monitor;
        workArea = monitorInfo.WorkArea;
        return true;
    }

    private enum TaskbarEdge
    {
        Bottom,
        Top,
        Right,
        Left
    }

    private bool TryGetTrayIconIdentity(out IntPtr windowHandle, out Guid id)
    {
        if (_trayIcon is null)
        {
            windowHandle = IntPtr.Zero;
            id = Guid.Empty;
            return false;
        }

        var trayIcon = _trayIcon.TrayIcon;
        windowHandle = trayIcon.WindowHandle;
        id = trayIcon.Id;
        return windowHandle != IntPtr.Zero && id != Guid.Empty;
    }

    private static bool TryGetNotifyIconRect(
        IntPtr windowHandle,
        Guid id,
        out NativeRect iconLocation)
    {
        var identifier = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            WindowHandle = windowHandle,
            GuidItem = id
        };

        return Shell_NotifyIconGetRect(ref identifier, out iconLocation) == 0 &&
               iconLocation.Right > iconLocation.Left &&
               iconLocation.Bottom > iconLocation.Top;
    }

    private void DisposeTrayIconControl()
        => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _connectionService.DeviceConnected -= OnDeviceConnected;
            _connectionService.DeviceDisconnected -= OnDeviceDisconnected;
        }
        catch
        {
            // ignore
        }

        try
        {
            _floatingService.Dismissed -= OnFloatingDismissed;
            _quickPanelVisible = false;
            TrayAudioQuickPanelControl.IsOpen = false;
            _quickPanelContent = null;
            _floatingService.Dismiss();
        }
        catch
        {
            // ignore
        }

        _contextMenu?.Hide();
        _contextMenu = null;

        try
        {
            if (_trayIcon is not null)
            {
                _trayIcon.SecondWindowContextMenuOpened -= OnSecondWindowContextMenuOpened;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            if (_trayWindow is not null)
            {
                _trayWindow.Content = null;
                _trayWindow.Close();
                _trayWindow = null;
            }
        }
        catch
        {
            // ignore
        }
    }

    [LibraryImport("shell32.dll", SetLastError = true)]
    private static partial int Shell_NotifyIconGetRect(
        ref NotifyIconIdentifier identifier,
        out NativeRect iconLocation);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(
        IntPtr monitor,
        ref NativeMonitorInfo monitorInfo);

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint IconId;
        public Guid GuidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}

public partial class TrayMenuControlViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string TrayIconToolTip { get; set; } = "蓝牙音频接收器 - 未连接";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string? ConnectedDeviceName { get; set; }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        var connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();

        if (IsConnected)
        {
            var devices = connectionService.ConnectedDevices.ToList();
            foreach (var device in devices)
            {
                await connectionService.DisconnectAsync(device);
            }
        }
        else
        {
            var localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();
            var localSettingsKeys = Ioc.Default.GetRequiredService<LocalSettingsKeys>();
            var savedIds = await localSettingsService.ReadSettingAsync<List<string>>(
                localSettingsKeys.BluetoothConnectedDeviceIdsKey);
            if (savedIds?.Count > 0)
            {
                await connectionService.ReconnectPreviousDevicesAsync(savedIds);
            }
        }
    }
}
