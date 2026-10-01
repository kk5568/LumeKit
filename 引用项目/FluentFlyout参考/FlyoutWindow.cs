// FlyoutWindow.cs
// 可复用浮窗基类：无边框、置顶、不抢焦点、滑入/淡出动画、超时自动隐藏、复用单窗口。
// 用法：继承本类，设计自己的 XAML 内容，然后调用 ShowFlyout() 即可。
// 说明：本模块改写自 FluentFlyout (GPL-3.0-or-later)，如直接复用请注意 GPL 协议要求。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static FlyoutModule.NativeMethods;

namespace FlyoutModule;

public class FlyoutWindow : Window
{
    // ===== 可调参数 =====
    /// <summary>浮窗停留时长，超时后自动隐藏。每次调用 ShowFlyout 都会重置。</summary>
    public TimeSpan DisplayDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>滑入/淡出动画时长（毫秒）。设为 0 表示不用动画。</summary>
    public int AnimationDurationMs { get; set; } = 300;

    /// <summary>距离屏幕底边的距离（逻辑单位）。</summary>
    public double BottomMargin { get; set; } = 16;

    /// <summary>目标显示器：null = 光标所在屏幕，否则用指定屏幕的工作区。</summary>
    public Func<RECT>? CustomWorkAreaProvider { get; set; }

    // ===== 内部状态 =====
    private CancellationTokenSource? _hideCts;
    private bool _isHiding = true;

    public FlyoutWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;          // 首次显示不抢焦点
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        AllowsTransparency = false;     // 若你的 XAML 用透明背景，可改为 true 且 WindowStyle 需为 None

        // 先藏到屏幕外，避免启动时闪现
        Left = -10000;
        Top = -10000;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // WS_EX_NOACTIVATE：永远不接受键盘焦点（打游戏时弹出不切走焦点）
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
            SetTopmostNative(hwnd);
        };
    }

    // 用 Win32 再设一次置顶，比纯 WPF 的 Topmost 更可靠
    private static void SetTopmostNative(IntPtr hwnd)
        => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// 显示浮窗（复用同一个窗口）。重复调用会重置隐藏倒计时。
    /// </summary>
    public void ShowFlyout()
    {
        if (_isHiding)
        {
            _isHiding = false;
            PlayOpenAnimation();
        }
        ResetHideTimer();
    }

    /// <summary>立即隐藏（带动画）。</summary>
    public void HideFlyout()
    {
        _hideCts?.Cancel();
        PlayCloseAnimation();
    }

    // ===== 动画 =====

    private (double left, double top) GetTargetPosition()
    {
        // 取目标屏幕工作区（设备像素），并换算成 WPF 逻辑单位
        var work = CustomWorkAreaProvider?.Invoke() ?? GetWorkAreaAtCursor();
        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX;
        double scaleY = dpi.DpiScaleY;

        double left = (work.Left + (work.Width - ActualWidth * scaleX) / 2) / scaleX;
        double top = (work.Bottom - ActualHeight * scaleY - BottomMargin * scaleY) / scaleY;
        return (left, top);
    }

    private static RECT GetWorkAreaAtCursor()
    {
        GetCursorPos(out var pt);
        IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (hMonitor != IntPtr.Zero && GetMonitorInfo(hMonitor, ref mi))
            return mi.rcWork;
        // 兜底：主屏工作区
        return new RECT { Left = 0, Top = 0, Right = (int)SystemParameters.PrimaryScreenWidth, Bottom = (int)SystemParameters.PrimaryScreenHeight };
    }

    private void PlayOpenAnimation()
    {
        UpdateLayout(); // 确保 ActualWidth/Height 已生效
        var (left, top) = GetTargetPosition();

        Left = left;
        double fromTop = AnimationDurationMs == 0 ? top : top + 20;
        Top = fromTop;
        Opacity = AnimationDurationMs == 0 ? 1 : 0;

        Visibility = Visibility.Visible;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) SetTopmostNative(hwnd);

        if (AnimationDurationMs == 0) return;

        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var move = new DoubleAnimation(top, duration) { EasingFunction = easing };
        BeginAnimation(TopProperty, move);

        var fade = new DoubleAnimation(1, duration) { EasingFunction = easing };
        BeginAnimation(OpacityProperty, fade);
    }

    private void PlayCloseAnimation()
    {
        if (_isHiding && Visibility != Visibility.Visible) return;

        // 动画结束后真正隐藏
        var hide = new Action(() =>
        {
            if (_isHiding) Visibility = Visibility.Hidden;
        });

        if (AnimationDurationMs == 0)
        {
            Opacity = 0;
            hide();
            return;
        }

        _isHiding = true;
        var duration = new Duration(TimeSpan.FromMilliseconds(AnimationDurationMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        var move = new DoubleAnimation(Top + 20, duration) { EasingFunction = easing };
        BeginAnimation(TopProperty, move);

        var fade = new DoubleAnimation(0, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => hide();
        BeginAnimation(OpacityProperty, fade);
    }

    // ===== 超时自动隐藏（CancellationTokenSource 重置式倒计时） =====

    private async void ResetHideTimer()
    {
        _hideCts?.Cancel();
        _hideCts = new CancellationTokenSource();
        var token = _hideCts.Token;

        try
        {
            await Task.Delay(DisplayDuration, token);
            PlayCloseAnimation();
            if (AnimationDurationMs > 0)
                await Task.Delay(AnimationDurationMs, token);
            if (Visibility == Visibility.Visible)
                Visibility = Visibility.Hidden;
        }
        catch (TaskCanceledException)
        {
            // 计时被新的 ShowFlyout 重置，无需处理
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideCts?.Cancel();
        _hideCts?.Dispose();
        base.OnClosed(e);
    }
}
