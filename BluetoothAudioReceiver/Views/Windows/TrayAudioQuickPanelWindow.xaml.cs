using System.Runtime.InteropServices;
using BluetoothAudioReceiver.DeskBoxFloating.Helpers;
using BluetoothAudioReceiver.DeskBoxFloating.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinUIEx;

namespace BluetoothAudioReceiver.Views.Windows;

/// <summary>
/// 托盘左键音频卡片浮层的承载窗口。
/// Flyout 从托盘隐藏宿主窗口弹不出来，所以改用独立小窗口承载
/// Views/Controls/TrayAudioQuickPanelControl；窗口外壳沿用 DeskBox 浮层那套
/// （无边框工具窗口 + 按工作区定位 + 点外部关闭），外观与卡片画法仍是一份代码。
/// </summary>
public sealed partial class TrayAudioQuickPanelWindow : WindowEx
{
    // 面板宽度（DIP）：卡片一行 3 张，宽度按这个坐标系算，窗口尺寸也用 DIP。
    private const double PanelWidth = 376;
    private const double MinPanelHeight = 200;
    private const double MaxPanelHeight = 620;
    private const int ScreenMargin = 10;
    private const int AnchorGap = 8;

    /// <summary>浮层是否正在显示（对应原来的 TrayQuickPanelWindow.IsPanelVisible）。</summary>
    public static bool IsPanelVisible { get; private set; }

    /// <summary>浮层被隐藏时触发：点外部、点 ×、Esc、或再点一次托盘。</summary>
    public event EventHandler? PanelClosed;

    private readonly DispatcherQueueTimer _outsideClickTimer;
    private bool _lastLeftDown;
    private bool _lastRightDown;
    private DateTimeOffset _ignoreOutsideClickUntil = DateTimeOffset.MinValue;
    private double _panelHeight = MaxPanelHeight;

    public TrayAudioQuickPanelWindow()
    {
        InitializeComponent();

        PanelContent.CloseRequested += (_, _) => HidePanel();
        PanelContent.KeyDown += OnPanelKeyDown;

        IsTitleBarVisible = false;
        IsMinimizable = false;
        IsMaximizable = false;
        IsResizable = false;
        IsShownInSwitchers = false;
        ExtendsContentIntoTitleBar = false;

        DeskBoxWindowShellState.TryApplyBorderlessOverlappedPresenter(AppWindow);
        DeskBoxFloatingWin32Helper.ConfigureWindow(this.GetWindowHandle());

        try { SystemBackdrop = new MicaBackdrop(); } catch { }

        _outsideClickTimer = DispatcherQueue.CreateTimer();
        _outsideClickTimer.Interval = TimeSpan.FromMilliseconds(30);
        _outsideClickTimer.IsRepeating = true;
        _outsideClickTimer.Tick += (_, _) => CheckOutsideClickToClose();
    }

    /// <summary>显示则收起、隐藏则弹出（左键再点一次托盘走这里）。</summary>
    public void TogglePanel(PointInt32 anchorPoint)
    {
        if (Visible)
        {
            HidePanel();
            return;
        }

        _ = ShowPanelAsync(anchorPoint);
    }

    public async Task ShowPanelAsync(PointInt32 anchorPoint)
    {
        PanelContent.FillWindowSurface();

        try
        {
            await PanelContent.RefreshAsync();
        }
        catch
        {
        }

        FitWindowToPanel();
        PositionNearAnchor(anchorPoint);

        if (!Visible) Activate(); else AppWindow.Show();

        DeskBoxFloatingWin32Helper.EnsureTopMostNoActivate(this.GetWindowHandle());

        IsPanelVisible = true;
        TrayAudioQuickPanelControl.IsOpen = true;

        _ignoreOutsideClickUntil = DateTimeOffset.Now.AddMilliseconds(300);
        _lastLeftDown = IsMouseButtonDown(0x01);
        _lastRightDown = IsMouseButtonDown(0x02);
        _outsideClickTimer.Start();

        PanelContent.Focus(FocusState.Programmatic);
    }

    public void HidePanel()
    {
        _outsideClickTimer.Stop();

        bool wasVisible = Visible;
        if (wasVisible)
        {
            WindowExtensions.Hide(this);
        }

        IsPanelVisible = false;
        TrayAudioQuickPanelControl.IsOpen = false;

        if (wasVisible)
        {
            PanelClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>按内容算窗口高度（DIP），范围夹在最小/最大之间，超出由浮层内部滚动。</summary>
    private void FitWindowToPanel()
    {
        PanelContent.Width = PanelWidth;
        PanelContent.Measure(new global::Windows.Foundation.Size(PanelWidth, double.PositiveInfinity));
        _panelHeight = Math.Clamp(
            Math.Ceiling(PanelContent.DesiredSize.Height),
            MinPanelHeight,
            MaxPanelHeight);

        Width = PanelWidth;
        Height = _panelHeight;
    }

    private double ResolveScale()
    {
        try
        {
            double scale = Win32Helper.GetDpiScaleForWindow(this.GetWindowHandle(), RootGrid.XamlRoot);
            if (scale > 0)
            {
                return scale;
            }
        }
        catch
        {
        }

        return 1;
    }

    /// <summary>贴着托盘图标附近的屏幕坐标放：默认在锚点上方，上方放不下就放下方，再按工作区夹住。</summary>
    private void PositionNearAnchor(PointInt32 anchorPoint)
    {
        double scale = ResolveScale();
        int width = (int)Math.Ceiling(PanelWidth * scale);
        int height = (int)Math.Ceiling(_panelHeight * scale);
        int gap = (int)Math.Ceiling(AnchorGap * scale);

        var displayArea = DisplayArea.GetFromPoint(anchorPoint, DisplayAreaFallback.Nearest);
        if (displayArea is null)
        {
            displayArea = DisplayArea.Primary;
        }

        if (displayArea is null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;

        int x = anchorPoint.X - (width / 2);
        int y = anchorPoint.Y - height - gap;
        if (y < workArea.Y + ScreenMargin)
        {
            y = anchorPoint.Y + gap;
        }

        int maxX = Math.Max(workArea.X + ScreenMargin, workArea.X + workArea.Width - width - ScreenMargin);
        int maxY = Math.Max(workArea.Y + ScreenMargin, workArea.Y + workArea.Height - height - ScreenMargin);
        x = Math.Clamp(x, workArea.X + ScreenMargin, maxX);
        y = Math.Clamp(y, workArea.Y + ScreenMargin, maxY);

        AppWindow.Move(new PointInt32(x, y));
    }

    private void OnPanelKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != global::Windows.System.VirtualKey.Escape)
        {
            return;
        }

        HidePanel();
        e.Handled = true;
    }

    private void CheckOutsideClickToClose()
    {
        if (!Visible)
        {
            _outsideClickTimer.Stop();
            return;
        }

        if (DateTimeOffset.Now < _ignoreOutsideClickUntil)
        {
            return;
        }

        var leftDown = IsMouseButtonDown(0x01);
        var rightDown = IsMouseButtonDown(0x02);
        var leftClicked = leftDown && !_lastLeftDown;
        var rightClicked = rightDown && !_lastRightDown;
        _lastLeftDown = leftDown;
        _lastRightDown = rightDown;

        if (!leftClicked && !rightClicked)
        {
            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var inside = cursor.X >= position.X && cursor.X <= position.X + size.Width &&
                     cursor.Y >= position.Y && cursor.Y <= position.Y + size.Height;

        if (!inside)
        {
            HidePanel();
        }
    }

    private static bool IsMouseButtonDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint lpPoint);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
