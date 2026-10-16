using System.Runtime.InteropServices;
using BluetoothAudioReceiver.DeskBoxFloating.Helpers;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.DeskBoxFloating.Platform;
using BluetoothAudioReceiver.DeskBoxFloating.Profiles;
using BluetoothAudioReceiver.DeskBoxFloating.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinUIEx;

namespace BluetoothAudioReceiver.DeskBoxFloating.Views;

public sealed partial class DeskBoxFloatingWindow : WindowEx
{
    // 定位时窗口底边离工作区下沿（任务栏顶）的距离。原来的 18 太窄，
    // DWM 的系统阴影会下探到任务栏上；留出 36 让阴影落在空档里。阴影本身没变。
    private const int WindowBottomMargin = 36;

    // 更靠屏幕边时留出的安全距离（左键快捷中心贴右边屏幕时用）。
    private const int ScreenEdgeMargin = 20;

    // 只用于算窗口高度：内容下方的余量。和定位无关，避免窗口跟着一起长高。
    private const int WindowContentBottomGutter = 18;
    private const int TaskbarClearancePadding = 12;
    private const int WindowContentSideGutter = 2;

    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherQueueTimer _outsideClickTimer;
    private bool _dismissOnOutsideClick;
    private bool _lastLeftDown;
    private bool _lastRightDown;
    private DateTimeOffset _ignoreOutsideClickUntil = DateTimeOffset.MinValue;
    private Thickness _defaultContentPadding;
    private int _windowWidth = 250;
    private int _windowHeight = 54;
    private int _hideRevision;
    private DeskBoxFloatingLayoutProfile _profile = DeskBoxFloatingProfiles.NotificationDefault;
    private readonly WidgetTrayAnimationController _trayAnimation;
    private bool _isHideAnimationRunning;

    // 动画每帧提交位置后会回调，用这三项判断「在任务栏区域里不置顶、越过任务栏后才置顶」。
    private int _taskbarTopY;
    private bool _isWindowTopMost;

    public event EventHandler? Dismissed;

    public DeskBoxFloatingWindow()
    {
        InitializeComponent();
        _trayAnimation = new WidgetTrayAnimationController(
            AppWindow,
            RootContainer,
            DispatcherQueue,
            this.GetWindowHandle(),
            GetCurrentAnimationBounds,
            LogTrayWindow);

        _trayAnimation.PositionCommitted += OnAnimationPositionCommitted;

        IsTitleBarVisible = false;
        IsMinimizable = false;
        IsMaximizable = false;
        IsResizable = false;
        IsShownInSwitchers = false;
        ExtendsContentIntoTitleBar = false;

        DeskBoxWindowShellState.TryHideFromSwitchers(AppWindow);
        DeskBoxWindowShellState.TryApplyBorderlessOverlappedPresenter(AppWindow);

        DeskBoxFloatingWin32Helper.ConfigureWindow(this.GetWindowHandle());
        ApplyMaterial();
        _defaultContentPadding = ContentTransitionViewport.Padding;
        _hideTimer.Tick += OnHideTimerTick;

        // 托盘左键面板这类常驻浮层要点外部关闭；通知有自己的自动关闭，不用这个。
        _outsideClickTimer = DispatcherQueue.CreateTimer();
        _outsideClickTimer.Interval = TimeSpan.FromMilliseconds(30);
        _outsideClickTimer.IsRepeating = true;
        _outsideClickTimer.Tick += OnOutsideClickTick;

        Closed += (_, _) =>
        {
            DisposeBackdropControllers();
            _trayAnimation.Dispose();
        };
    }

    public void Show(DeskBoxFloatingRequest request, FrameworkElement? content = null)
    {
        IconGlyph.Text = string.IsNullOrWhiteSpace(request.IconGlyph) ? "\uE765" : request.IconGlyph;
        TitleText.Text = request.Title ?? string.Empty;
        StatusText.Text = request.Status ?? string.Empty;
        StatusText.Visibility = string.IsNullOrWhiteSpace(request.Status)
            ? Visibility.Collapsed
            : Visibility.Visible;
        DescriptionText.Text = request.Description ?? string.Empty;
        DescriptionText.Visibility = string.IsNullOrWhiteSpace(request.Description)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _profile = DeskBoxFloatingProfiles.Resolve(request.ProfileId, request.Kind);
        TitleBarGrid.Padding = _profile.Padding;
        TitleBarGrid.ColumnSpacing = _profile.ContentSpacing;
        ShellContentPresenter.RowSpacing = _profile.ContentSpacing / 2;
        IconGlyph.FontSize = _profile.IconSize / 2;
        UpdateItems(request, _profile);

        // 自绘内容：整块塞进内容宿主，窗口自己的标题行收掉（内容自己带标题）。
        // 材质、圆角、阴影、进出场动画还是窗口这一套，内容只画里面。
        bool hasCustomContent = content is not null;
        CustomContentHost.Content = content;
        CustomContentHost.Visibility = hasCustomContent ? Visibility.Visible : Visibility.Collapsed;
        TitleBarGrid.Visibility = hasCustomContent ? Visibility.Collapsed : Visibility.Visible;
        ContentTransitionViewport.Padding = hasCustomContent
            ? new Thickness(0)
            : _defaultContentPadding;

        // 统一布局：标题和正文当成一整块在窗口里居中；没有正文就把正文块收掉，高度才收得干净。
        bool hasBodyContent = hasCustomContent
            || DescriptionText.Visibility == Visibility.Visible
            || ItemsScrollViewer.Visibility == Visibility.Visible;
        ContentTransitionViewport.Visibility = hasBodyContent ? Visibility.Visible : Visibility.Collapsed;

        // 新旧设计开关（设置页「统一布局（新设计）」）：每次 Show 都读一次，改完下一次弹出即生效。
        bool unifiedLayout = AppSettings.Current.TrayUnifiedLayout;
        ApplyLayoutMode(unifiedLayout);

        if (hasCustomContent)
        {
            // 自绘内容铺满整窗，不受标题行那套对齐方式影响。
            ContentBlock.VerticalAlignment = VerticalAlignment.Stretch;
            HeaderDivider.Visibility = Visibility.Collapsed;
        }

        (_windowWidth, _windowHeight) = ResolveWindowSize(request, _profile);
        if (content is not null)
        {
            // 窗口宽度跟着内容自己的 DIP 宽度走，再按档位夹住。
            _windowWidth = Math.Clamp(
                MeasureContentWidth(content),
                _profile.MinWidth,
                _profile.MaxWidth);
        }

        if (unifiedLayout || hasCustomContent)
        {
            _windowHeight = MeasureFittedHeight();
        }
        AppWindow.Resize(new SizeInt32(_windowWidth, _windowHeight));

        _hideRevision++;
        _isHideAnimationRunning = false;

        // DeskBox QuickCaptureWidgetWindow.RevealFromTray 的顺序：
        // 先 cloak + 摆到动画起点，再显示窗口，避免首帧闪一下。定位也在这一步里做，
        // 保证定位是最后一次挪窗口（抬升动画的落点是 PrepareVisualState 当场读的窗口矩形）。
        PrepareTrayShowAnimation(request.PlacementOverride ?? _profile.Placement, request.AnchorPoint);

        if (!Visible)
        {
            Activate();
        }

        if (hasCustomContent && content is not null)
        {
            // 让 Esc 能用：焦点落进自绘内容，窗口级键盘快捷键才会命中。
            try { content.Focus(FocusState.Programmatic); } catch { }
        }

        DeskBoxFloatingWin32Helper.ConfigureWindow(this.GetWindowHandle());
        ApplyMaterial();

        // 显形先不置顶：窗口起点落在任务栏区域里，让任务栏挡住它，看上去是从任务栏后面冒出来。
        // 越过任务栏顶后由 OnAnimationPositionCommitted 置顶，落定还有一次兜底置顶。
        // 必须排在 ConfigureWindow 之后（它内部的 SetWindowPos 会重置 z-order）。
        DeskBoxFloatingWin32Helper.ClearTopMostNoActivate(this.GetWindowHandle());
        _isWindowTopMost = false;

        _trayAnimation.PrepareHiddenState();
        _trayAnimation.RevealWindowForTrayShow();

        _hideTimer.Stop();
        bool autoDismiss = request.AutoDismissOverride ?? _profile.AutoDismiss;
        if (autoDismiss)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(request.DurationMilliseconds);
            _hideTimer.Start();
        }

        // 不自动关的常驻浮层（托盘左键面板）点外部关闭；Esc 由 XAML 的快捷键处理。
        _dismissOnOutsideClick = hasCustomContent && !autoDismiss;
        if (_dismissOnOutsideClick)
        {
            _ignoreOutsideClickUntil = DateTimeOffset.Now.AddMilliseconds(300);
            _lastLeftDown = IsMouseButtonDown(0x01);
            _lastRightDown = IsMouseButtonDown(0x02);
            _outsideClickTimer.Start();
        }
        else
        {
            _outsideClickTimer.Stop();
        }

        PlayTrayRaiseAnimationAfterFirstFrame();
    }

    public void DismissNow()
    {
        _hideTimer.Stop();
        _outsideClickTimer.Stop();
        _dismissOnOutsideClick = false;

        if (!Visible)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        // 已经在关就直接放行。不能先加代次：加了会让正在跑的这次退场动画作废，
        // 窗口既不隐藏也不发 Dismissed，托盘左键浮层会永远弹不出来。
        if (_isHideAnimationRunning)
        {
            return;
        }

        var revision = ++_hideRevision;
        _isHideAnimationRunning = true;
        // 退场全程保持置顶，下滑时不会再掉到其它窗口后面（也不会闪一下）。
        _trayAnimation.NextGeneration();
        _trayAnimation.RevealWindowForTrayShow();
        if (_trayAnimation.IsPositionTransitionActive)
        {
            _trayAnimation.StopAndRestoreWindowPosition();
        }
        else
        {
            _trayAnimation.Stop();
        }

        if (!GetTrayAnimationProfile().IsEnabled)
        {
            CompleteTrayHideAnimation(revision);
            return;
        }

        if (!WidgetTrayAnimationPreparation.TryPrepare(
                () => _trayAnimation.PrepareVisualState(
                    0,
                    0,
                    WidgetTrayAnimationController.RestingOpacity,
                    WidgetTrayAnimationController.RestingScale),
                () => CompleteTrayHideAnimation(revision),
                ex => LogTrayWindow($"PrepareHide failed: {ex.Message}")))
        {
            return;
        }

        PlayTrayHideAnimation(revision);
    }

    private void PlayTrayHideAnimation(int revision)
    {
        if (!_isHideAnimationRunning)
        {
            return;
        }

        var profile = GetTrayAnimationProfile();
        _trayAnimation.Animate(
            0,
            0,
            profile.HideOffsetX,
            profile.HideOffsetY,
            WidgetTrayAnimationController.RestingOpacity,
            profile.HideEndOpacity,
            WidgetTrayAnimationController.RestingScale,
            profile.HideEndScale,
            AppSettings.Current.TrayHideDurationMs,
            false,
            _trayAnimation.Generation,
            WidgetAnimationSettings.NormalizeEasingIntensity(AppSettings.Current.WidgetAnimationHideEasingIntensity),
            () => CompleteTrayHideAnimation(revision),
            () => CompleteTrayHideAnimation(revision));
    }

    private void CompleteTrayHideAnimation(int revision)
    {
        if (revision != _hideRevision)
        {
            // 作废的这次也要把标志落回来，否则后面每次关闭都会被上面挡掉。
            _isHideAnimationRunning = false;
            return;
        }

        _isHideAnimationRunning = false;
        try { _trayAnimation.Stop(); } catch (Exception ex) { LogTrayWindow($"Stop failed: {ex.Message}"); }
        try { WindowExtensions.Hide(this); } catch (Exception ex) { LogTrayWindow($"Hide failed: {ex.Message}"); }
        _trayAnimation.RevealWindowForTrayShow();
        _trayAnimation.RestoreVisualState();
        try { _trayAnimation.RestoreWindowPosition(); } catch (Exception ex) { LogTrayWindow($"Restore position failed: {ex.Message}"); }
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareTrayShowAnimation(DeskBoxFloatingPlacement placement, PointInt32? anchorPoint)
    {
        // 先清掉上一次动画留下的复位点，再定位。抬升动画的落点是 PrepareVisualState 当场
        // 读到的窗口矩形，所以定位必须是最后一次挪窗口；否则新窗口首秀时读到的是窗口默认
        // 位置（屏幕中间），整段动画就按错位置跑，第一次左键的面板会落在屏幕中下。
        try
        {
            _trayAnimation.NextGeneration();
            _trayAnimation.StopAndRestoreWindowPosition();
        }
        catch (Exception ex)
        {
            LogTrayWindow($"Reset show state failed: {ex.Message}");
        }

        Position(placement, anchorPoint);

        WidgetTrayAnimationPreparation.TryPrepare(
            () =>
            {
                _trayAnimation.CloakWindowForTrayShow();
                _trayAnimation.SetOffsetOverride(null, ResolveTraySlideOffsetY());
                var profile = GetTrayAnimationProfile();
                _trayAnimation.PrepareVisualState(
                    profile.ShowOffsetX,
                    profile.ShowOffsetY,
                    profile.ShowStartOpacity,
                    profile.ShowStartScale);
            },
            CompleteTrayShowWithoutAnimation,
            ex => LogTrayWindow($"PrepareShow failed: {ex.Message}"));
    }

    private void PlayTrayRaiseAnimationAfterFirstFrame()
    {
        if (Visible)
        {
            _trayAnimation.PlayAfterContentReady(PlayTrayRaiseAnimation);
        }
    }

    private void PlayTrayRaiseAnimation()
    {
        if (!Visible)
        {
            return;
        }

        var profile = GetTrayAnimationProfile();
        if (!profile.IsEnabled)
        {
            CompleteTrayShowWithoutAnimation();
            return;
        }

        _trayAnimation.Animate(
            profile.ShowOffsetX,
            profile.ShowOffsetY,
            0,
            0,
            profile.ShowStartOpacity,
            WidgetTrayAnimationController.RestingOpacity,
            profile.ShowStartScale,
            WidgetTrayAnimationController.RestingScale,
            AppSettings.Current.TrayShowDurationMs,
            true,
            _trayAnimation.Generation,
            WidgetAnimationSettings.NormalizeEasingIntensity(AppSettings.Current.WidgetAnimationEasingIntensity),
            () =>
            {
                // 落定兜底置顶：正常路径上动画越过任务栏时就已经置顶了。
                DeskBoxFloatingWin32Helper.EnsureTopMostNoActivate(this.GetWindowHandle());
                _isWindowTopMost = true;
                WidgetTrayAnimationPreparation.CompleteShow(
                    _trayAnimation.RestoreWindowPosition,
                    _trayAnimation.RevealWindowForTrayShow,
                    () => { },
                    ex => LogTrayWindow($"CompleteShow cleanup failed: {ex.Message}"));
            },
            null);
    }

    private void CompleteTrayShowWithoutAnimation()
    {
        DeskBoxFloatingWin32Helper.EnsureTopMostNoActivate(this.GetWindowHandle());
        _isWindowTopMost = true;
        _isHideAnimationRunning = false;
        try { _trayAnimation.Stop(); } catch (Exception ex) { LogTrayWindow($"Stop failed: {ex.Message}"); }
        WidgetTrayAnimationPreparation.CompleteShow(
            _trayAnimation.RestoreWindowPosition,
            _trayAnimation.RevealWindowForTrayShow,
            _trayAnimation.RestoreVisualState,
            ex => LogTrayWindow($"CompleteShowWithoutAnimation cleanup failed: {ex.Message}"));
    }

    private WidgetTrayAnimationProfile GetTrayAnimationProfile() =>
        _trayAnimation.CreateProfile(WidgetAnimationSettings.From(AppSettings.Current));

    private Windows.Foundation.Rect GetCurrentAnimationBounds()
    {
        IntPtr handle = this.GetWindowHandle();
        if (handle != IntPtr.Zero && Win32Helper.GetWindowRect(handle, out var rect))
        {
            return new Windows.Foundation.Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        var position = AppWindow.Position;
        var size = AppWindow.Size;
        return new Windows.Foundation.Rect(position.X, position.Y, size.Width, size.Height);
    }

    /// <summary>
    /// 布局方案 2：档案高度打底，内容装不下（或 DPI 换算后更高）才往上长到档案 MaxHeight。
    /// XAML 尺寸是 DIP，AppWindow.Resize 用物理像素；浮窗首帧隐藏时 XamlRoot 可能拿不到，
    /// 所以缩放走 Win32Helper.GetDpiScaleForWindow（XamlRoot 优先，回退 GetDpiForWindow）。
    /// </summary>
    /// <summary>
    /// 新旧设计切换。旧设计照着 DeskBox Controls\WidgetShell.xaml:36-39（标题行 MinHeight 40）、
    /// :184-190（分隔线 Height 1 / Margin 12,0,12,4 / Opacity 0.62）和标题靠左来的。
    /// </summary>
    private void ApplyLayoutMode(bool unifiedLayout)
    {
        ContentBlock.VerticalAlignment = unifiedLayout
            ? VerticalAlignment.Center
            : VerticalAlignment.Top;
        TitleBarGrid.MinHeight = unifiedLayout ? 0 : 40;
        TitleText.HorizontalAlignment = unifiedLayout
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;
        HeaderDivider.Visibility = unifiedLayout ? Visibility.Collapsed : Visibility.Visible;
    }

    private int MeasureFittedHeight()
    {
        IntPtr handle = this.GetWindowHandle();
        double scale = Win32Helper.GetDpiScaleForWindow(handle, RootContainer.XamlRoot);
        ContentBlock.Measure(new Windows.Foundation.Size(_windowWidth / scale, double.PositiveInfinity));
        int fitted = (int)Math.Ceiling(ContentBlock.DesiredSize.Height * scale) + WindowContentBottomGutter;

        int floor = _profile.Height;
        int ceiling = Math.Max(floor, _profile.MaxHeight);
        return Math.Clamp(fitted, floor, ceiling);
    }

    /// <summary>
    /// 自绘内容的宽度（物理像素）：按内容自己的 DIP 宽度乘缩放，再加一点侧边余量，
    /// 免得窗口边框压到卡片边上。内容没给宽度时退回档位宽度。
    /// </summary>
    private int MeasureContentWidth(FrameworkElement content)
    {
        double scale = Win32Helper.GetDpiScaleForWindow(this.GetWindowHandle(), RootContainer.XamlRoot);
        if (!(scale > 0))
        {
            scale = 1;
        }

        content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double widthDip = content.DesiredSize.Width;
        if (!(widthDip > 0) || double.IsInfinity(widthDip))
        {
            return _profile.Width;
        }

        return (int)Math.Ceiling(widthDip * scale) + WindowContentSideGutter;
    }

    /// <summary>
    /// 动画起点必须整块落在工作区下沿以下，否则高窗口会先露在任务栏上面，看着像从前面冒出来。
    /// 挡位值只管速度曲线，这里取「挡位值」和「窗口高 + 底距 + 余量」里更大的那个。
    /// </summary>
    private double ResolveTraySlideOffsetY() =>
        Math.Max(
            AppSettings.Current.TraySlideOffsetOverrideY ?? 0d,
            _windowHeight + WindowBottomMargin + TaskbarClearancePadding);

    private void LogTrayWindow(string message) => App.LogVerbose($"[DeskBoxFloating] {message}");

    private void OnHideTimerTick(object? sender, object e)
    {
        _hideTimer.Stop();
        DismissNow();
    }

    // 不用 KeyboardAccelerator：它会跟着鼠标悬浮出「Esc」提示，落在设备卡片上很难看。
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != global::Windows.System.VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        DismissNow();
    }

    /// <summary>点浮层外面就关：只在“不自动关”的自绘内容浮层上启用。</summary>
    private void OnOutsideClickTick(object? sender, object e)
    {
        if (!_dismissOnOutsideClick || !Visible)
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
        var clicked = (leftDown && !_lastLeftDown) || (rightDown && !_lastRightDown);
        _lastLeftDown = leftDown;
        _lastRightDown = rightDown;

        if (!clicked || !GetCursorPos(out var cursor))
        {
            return;
        }

        if (!Win32Helper.GetWindowRect(this.GetWindowHandle(), out var rect))
        {
            return;
        }

        bool inside = cursor.X >= rect.Left && cursor.X <= rect.Right &&
                      cursor.Y >= rect.Top && cursor.Y <= rect.Bottom;
        if (!inside)
        {
            DismissNow();
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

    private void Position(DeskBoxFloatingPlacement placement, PointInt32? anchorPoint)
    {
        var displayArea = DisplayArea.Primary;
        PointInt32? anchor = anchorPoint;
        if (placement is DeskBoxFloatingPlacement.TrayIcon
            or DeskBoxFloatingPlacement.TrayIconLeftAligned
            or DeskBoxFloatingPlacement.Cursor)
        {
            if (anchor is null)
            {
                DeskBoxFloatingWin32Helper.TryGetCursorPosition(out var cursor);
                anchor = cursor;
            }

            displayArea = DisplayArea.GetFromPoint(anchor.Value, DisplayAreaFallback.Nearest);
        }

        if (displayArea is null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;

        // 记下任务栏顶（工作区下沿），动画每帧据此判断窗口有没有越过任务栏。
        _taskbarTopY = workArea.Y + workArea.Height;

        var point = placement switch
        {
            DeskBoxFloatingPlacement.TrayIcon or DeskBoxFloatingPlacement.Cursor when anchor is { } p =>
                new PointInt32(p.X - _windowWidth / 2, p.Y - _windowHeight - WindowBottomMargin),
            DeskBoxFloatingPlacement.TrayIconLeftAligned when anchor is { } p =>
                ResolveTrayLeftAlignedPoint(p, workArea),
            DeskBoxFloatingPlacement.Custom when anchor is { } p =>
                new PointInt32(p.X, p.Y),
            DeskBoxFloatingPlacement.ScreenCenter => new PointInt32(
                workArea.X + (workArea.Width - _windowWidth) / 2,
                workArea.Y + (workArea.Height - _windowHeight) / 2),
            _ => new PointInt32(
                workArea.X + (workArea.Width - _windowWidth) / 2,
                workArea.Y + workArea.Height - _windowHeight - WindowBottomMargin)
        };
        var target = ClampToWorkArea(point, workArea, _windowWidth, _windowHeight);
        AppWindow.Move(target);

        // AppWindow.Move 对刚创建、还没落定的窗口会有延迟；这里用动画提交时的同一套 flags
        // 直接再落一次 HWND 矩形，保证随后的 PrepareVisualState 读到正确位置。
        CommitWindowPosition(target);
    }

    /// <summary>
    /// 动画每帧提交位置后回调：窗口底边还在任务栏区域里就取消置顶，让任务栏挡住这一截
    /// （看上去从任务栏后面冒出来）；整块越过任务栏顶后才置顶，之后不会被别的窗口压住。
    /// 只在跨越的那一帧切换一次，来回抖不了。
    /// </summary>
    private void OnAnimationPositionCommitted(PointInt32 position)
    {
        bool aboveTaskbar = position.Y + _windowHeight <= _taskbarTopY;
        if (aboveTaskbar == _isWindowTopMost)
        {
            return;
        }

        var handle = this.GetWindowHandle();
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _isWindowTopMost = aboveTaskbar;
        if (aboveTaskbar)
        {
            DeskBoxFloatingWin32Helper.EnsureTopMostNoActivate(handle);
        }
        else
        {
            DeskBoxFloatingWin32Helper.ClearTopMostNoActivate(handle);
        }
    }

    private void CommitWindowPosition(PointInt32 position)
    {
        var handle = this.GetWindowHandle();
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Win32Helper.SetWindowPos(
                handle,
                IntPtr.Zero,
                position.X,
                position.Y,
                0,
                0,
                Win32Helper.SWP_NOSIZE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            LogTrayWindow($"Commit position failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 左键快捷中心的摆法：面板左边缘和托盘图标左边缘对齐；右边放不下就贴屏幕右边，留
    /// ScreenEdgeMargin；左边同样保证留出边距。上下仍摆在图标上方，最后再统一兜一次不超出工作区。
    /// </summary>
    private PointInt32 ResolveTrayLeftAlignedPoint(PointInt32 anchor, RectInt32 workArea)
    {
        int left = anchor.X;
        int rightLimit = workArea.X + workArea.Width - ScreenEdgeMargin;
        if (left + _windowWidth > rightLimit)
        {
            left = rightLimit - _windowWidth;
        }

        left = Math.Max(left, workArea.X + ScreenEdgeMargin);
        return new PointInt32(left, anchor.Y - _windowHeight - WindowBottomMargin);
    }

    /// <summary>
    /// DeskBox WidgetWindowBase.ApplyBackdropPreference: 窗口级材质路径，
    /// 直接挂 Mica/Acrylic 控制器（不用 popover 版 WidgetMaterialSystemBackdrop），
    /// 配置里 IsInputActive 恒为 true，所以失焦也保持同一种材质。
    /// </summary>
    private void ApplyMaterial()
    {
        try
        {
            var appTheme = App.MainWindow.Content is FrameworkElement element
                ? element.ActualTheme
                : ElementTheme.Default;
            RootContainer.RequestedTheme = appTheme;
            ApplyBackdropPreference();
        }
        catch
        {
            SystemBackdrop ??= new MicaBackdrop();
        }
    }

    private static Windows.UI.Color ResolveAccentColor()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings()
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        }
        catch
        {
            return AccentColorHelper.DefaultAccentColor;
        }
    }

    private static double GetResourceDouble(string key, double fallback)
    {
        try
        {
            return Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var value) &&
                value is double number
                    ? number
                    : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private void UpdateItems(
        DeskBoxFloatingRequest request,
        DeskBoxFloatingLayoutProfile profile)
    {
        PreviewItemsPanel.Children.Clear();
        var items = request.Items;
        ItemsScrollViewer.Visibility = items.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        ItemsScrollViewer.VerticalScrollBarVisibility = profile.AllowScroll
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;

        foreach (var item in items)
        {
            PreviewItemsPanel.Children.Add(new TextBlock
            {
                Text = item,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI Variable, Segoe UI"),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["DeskBoxFloatingSecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
    }

    private static PointInt32 ClampToWorkArea(
        PointInt32 point,
        RectInt32 workArea,
        int windowWidth,
        int windowHeight)
        => new(
            Math.Clamp(
                point.X,
                workArea.X,
                workArea.X + Math.Max(0, workArea.Width - windowWidth)),
            Math.Clamp(
                point.Y,
                workArea.Y,
                workArea.Y + Math.Max(0, workArea.Height - windowHeight)));

    private static int GetResourceInt(string key, int fallback)
    {
        try
        {
            return Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var value) &&
                value is double number
                    ? (int)Math.Round(number)
                    : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static Thickness GetResourceThickness(string key, Thickness fallback)
    {
        try
        {
            return Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var value) &&
                value is Thickness thickness
                    ? thickness
                    : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static (int Width, int Height) ResolveWindowSize(
        DeskBoxFloatingRequest request,
        DeskBoxFloatingLayoutProfile profile)
    {
        int width = request.WidthOverride ?? profile.Width;
        int height = request.HeightOverride ?? profile.Height;
        return (
            Math.Clamp(width, profile.MinWidth, profile.MaxWidth),
            Math.Clamp(height, profile.MinHeight, profile.MaxHeight));
    }
}
