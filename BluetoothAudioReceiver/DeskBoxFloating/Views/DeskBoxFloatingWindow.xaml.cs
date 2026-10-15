using BluetoothAudioReceiver.DeskBoxFloating.Helpers;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.DeskBoxFloating.Platform;
using BluetoothAudioReceiver.DeskBoxFloating.Profiles;
using BluetoothAudioReceiver.DeskBoxFloating.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinUIEx;

namespace BluetoothAudioReceiver.DeskBoxFloating.Views;

public sealed partial class DeskBoxFloatingWindow : WindowEx
{
    // 定位时窗口底边离工作区下沿（任务栏顶）的距离。原来的 18 太窄，
    // DWM 的系统阴影会下探到任务栏上；留出 36 让阴影落在空档里。阴影本身没变。
    private const int WindowBottomMargin = 36;

    // 只用于算窗口高度：内容下方的余量。和定位无关，避免窗口跟着一起长高。
    private const int WindowContentBottomGutter = 18;
    private const int TaskbarClearancePadding = 12;

    private readonly DispatcherTimer _hideTimer = new();
    private int _windowWidth = 250;
    private int _windowHeight = 54;
    private int _hideRevision;
    private DeskBoxFloatingLayoutProfile _profile = DeskBoxFloatingProfiles.NotificationDefault;
    private readonly WidgetTrayAnimationController _trayAnimation;
    private bool _isHideAnimationRunning;

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
        _hideTimer.Tick += OnHideTimerTick;
        Closed += (_, _) =>
        {
            DisposeBackdropControllers();
            _trayAnimation.Dispose();
        };
    }

    public void Show(DeskBoxFloatingRequest request)
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

        // 统一布局：标题和正文当成一整块在窗口里居中；没有正文就把正文块收掉，高度才收得干净。
        bool hasBodyContent = DescriptionText.Visibility == Visibility.Visible
            || ItemsScrollViewer.Visibility == Visibility.Visible;
        ContentTransitionViewport.Visibility = hasBodyContent ? Visibility.Visible : Visibility.Collapsed;

        // 新旧设计开关（设置页「统一布局（新设计）」）：每次 Show 都读一次，改完下一次弹出即生效。
        bool unifiedLayout = AppSettings.Current.TrayUnifiedLayout;
        ApplyLayoutMode(unifiedLayout);

        (_windowWidth, _windowHeight) = ResolveWindowSize(request, _profile);
        if (unifiedLayout)
        {
            _windowHeight = MeasureFittedHeight();
        }
        AppWindow.Resize(new SizeInt32(_windowWidth, _windowHeight));
        Position(request.PlacementOverride ?? _profile.Placement, request.AnchorPoint);

        _hideRevision++;
        _isHideAnimationRunning = false;

        // 置顶只在抬升动画结束后才加（见 PlayTrayRaiseAnimation / CompleteTrayShowWithoutAnimation），
        // 动画期间保持普通层，浮窗才会从任务栏后面冒出来。
        DeskBoxFloatingWin32Helper.ClearTopMostNoActivate(this.GetWindowHandle());

        // DeskBox QuickCaptureWidgetWindow.RevealFromTray 的顺序：
        // 先 cloak + 摆到动画起点，再显示窗口，避免首帧闪一下。
        PrepareTrayShowAnimation();

        if (!Visible)
        {
            Activate();
        }

        DeskBoxFloatingWin32Helper.ConfigureWindow(this.GetWindowHandle());
        ApplyMaterial();

        _trayAnimation.PrepareHiddenState();
        _trayAnimation.RevealWindowForTrayShow();

        _hideTimer.Stop();
        if (request.AutoDismissOverride ?? _profile.AutoDismiss)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(request.DurationMilliseconds);
            _hideTimer.Start();
        }

        PlayTrayRaiseAnimationAfterFirstFrame();
    }

    public void DismissNow()
    {
        _hideTimer.Stop();
        var revision = ++_hideRevision;

        if (!Visible)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_isHideAnimationRunning)
        {
            return;
        }

        _isHideAnimationRunning = true;
        // 退场也要落回任务栏后面：先摘掉置顶，再开始下滑动画。
        DeskBoxFloatingWin32Helper.ClearTopMostNoActivate(this.GetWindowHandle());
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

    private void PrepareTrayShowAnimation()
    {
        WidgetTrayAnimationPreparation.TryPrepare(
            () =>
            {
                _trayAnimation.NextGeneration();
                _trayAnimation.StopAndRestoreWindowPosition();
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
                // 升到位置之后再置顶：动画从任务栏后面出来，落定后压住其它窗口。
                DeskBoxFloatingWin32Helper.EnsureTopMostNoActivate(this.GetWindowHandle());
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

    private void Position(DeskBoxFloatingPlacement placement, PointInt32? anchorPoint)
    {
        var displayArea = DisplayArea.Primary;
        PointInt32? anchor = anchorPoint;
        if (placement is DeskBoxFloatingPlacement.TrayIcon or DeskBoxFloatingPlacement.Cursor)
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
        var point = placement switch
        {
            DeskBoxFloatingPlacement.TrayIcon or DeskBoxFloatingPlacement.Cursor when anchor is { } p =>
                new PointInt32(p.X - _windowWidth / 2, p.Y - _windowHeight - WindowBottomMargin),
            DeskBoxFloatingPlacement.Custom when anchor is { } p =>
                new PointInt32(p.X, p.Y),
            DeskBoxFloatingPlacement.ScreenCenter => new PointInt32(
                workArea.X + (workArea.Width - _windowWidth) / 2,
                workArea.Y + (workArea.Height - _windowHeight) / 2),
            _ => new PointInt32(
                workArea.X + (workArea.Width - _windowWidth) / 2,
                workArea.Y + workArea.Height - _windowHeight - WindowBottomMargin)
        };
        AppWindow.Move(ClampToWorkArea(point, workArea, _windowWidth, _windowHeight));
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
