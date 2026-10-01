using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.DependencyInjection;
using BluetoothAudioReceiver.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinUIEx;
using Serilog;

using HWND = global::Windows.Win32.Foundation.HWND;
using PInvoke = global::Windows.Win32.PInvoke;
using SET_WINDOW_POS_FLAGS = global::Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS;
using WINDOW_LONG_PTR_INDEX = global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX;
using WinUIColor = global::Windows.UI.Color;

namespace BluetoothAudioReceiver.Views.Windows;

public sealed partial class NotificationWindow : WindowEx
{
    private static readonly Serilog.ILogger _log =
        Log.ForContext("SourceContext", nameof(NotificationWindow));

    public event EventHandler? NotificationDismissed;

    // 悬浮通知通用参数统一入口，后续只需要改这里即可调整动画节奏和显示手感。
    private static class NotificationAnimationSettings
    {
        public const int ShowDurationMs = 300;
        public const int HideDurationMs = 200;
        public const int ResizeDurationMs = 160;
        public const int AutoHideDelayMs = 2500;
        public const double StartOffsetY = 30;
        public const int AnimationFrameMs = 16;
        public const int WindowOpacityMin = 0;
        public const int WindowOpacityMax = 255;
        public const double CubicEasePower = 3;
    }

    // 悬浮通知图标位移统一入口，只调整图标自身，不影响其它元素。
    private static class NotificationIconLayoutSettings
    {
        public const double IconScaleMultiplier = 1.3;
        public static readonly Thickness DefaultIconMargin = new(-8, 0, 0, 0);
        public static readonly Thickness CompactIconMargin = new(0);
    }

    private const int DefaultWindowWidth = 600;
    private const int DefaultWindowHeight = 110;
    private const int CompactWindowWidth = 420;
    private const int CompactWindowHeight = 84;
    private const int WindowBottomMargin = 18;
    private const double BaseWindowHeight = DefaultWindowHeight;

    public double LayoutScale => _pendingAutomationLayout ? 1 : _windowHeight / BaseWindowHeight;
    public int NotificationCardHeight => ScaleByWindow(74);
    public int IconBoxSize => ScaleByWindow(30, NotificationIconLayoutSettings.IconScaleMultiplier);
    public int ContentBoxHeight => ScaleByWindow(40);
    public int IconFontSize => ScaleByWindow(14, NotificationIconLayoutSettings.IconScaleMultiplier);
    public int MergedIconBoxSize => 34;
    public int MergedIconFontSize => 20;
    public FontFamily IconFontFamily => new("Segoe Fluent Icons");
    public int TextFontSize => ScaleByWindow(13);
    public int AutomationDetailTextFontSize => 13;
    public int AutomationIconFontSize => 18;
    public int CenterRowOffsetY => -Math.Max(1, ScaleByWindow(1));
    public Thickness DefaultIconMargin => NotificationIconLayoutSettings.DefaultIconMargin;
    public Thickness CompactIconMargin => NotificationIconLayoutSettings.CompactIconMargin;

    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _animationTimer = new();
    private readonly DispatcherTimer _resizeTimer = new();
    private bool _isHiding;
    private bool _isAnimating;
    private bool _isResizing;
    private int _animationRevision;
    private Action? _hideCompletedAction;
    private DateTimeOffset _animationStartTime;
    private PointInt32 _animationStartPosition;
    private PointInt32 _animationEndPosition;
    private DateTimeOffset _resizeStartTime;
    private int _resizeStartWidth;
    private int _resizeStartHeight;
    private int _resizeEndWidth;
    private int _resizeEndHeight;
    private int _windowWidth = DefaultWindowWidth;
    private int _windowHeight = DefaultWindowHeight;
    private string _pendingGlyph = DeviceVisualHelper.SpeakerGlyph;
    private string _pendingMessage = string.Empty;
    private string _pendingStatus = string.Empty;
    private bool _pendingCompactLayout;
    private string[] _pendingDetailLines = [];
    private bool _pendingAutomationLayout;
    private bool _pendingDeskBoxLayout;
    private bool _hasShown;
    private readonly NotificationDeskBoxBackdrop _deskBoxBackdrop = new();

    public NotificationWindow()
    {
        InitializeComponent();

        IsTitleBarVisible = false;
        IsMinimizable = false;
        IsMaximizable = false;
        IsResizable = false;
        IsShownInSwitchers = false;
        ExtendsContentIntoTitleBar = false;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        NotificationWin32Helper.ConfigureDeskBoxWindow(this.GetWindowHandle());
        RootContainer.Loaded += OnRootContainerLoaded;
        RootContainer.ActualThemeChanged += OnRootContainerActualThemeChanged;
        Activated += OnNotificationWindowActivated;

        _hideTimer.Tick += OnHideTimerTick;
        _animationTimer.Tick += OnAnimationTimerTick;
        _resizeTimer.Tick += OnResizeTimerTick;
    }

    public void ShowNotification(
        string iconGlyph,
        string message,
        string status,
        bool compactLayout = false,
        int? windowWidthOverride = null,
        int? windowHeightOverride = null,
        IReadOnlyList<string>? detailLines = null,
        bool automationLayout = false,
        int? durationMilliseconds = null,
        bool deskBoxLayout = false)
    {
        SetPendingContent(
            iconGlyph,
            message,
            status,
            compactLayout,
            windowWidthOverride,
            windowHeightOverride,
            detailLines,
            automationLayout,
            deskBoxLayout);

        try
        {
            ApplyThemeFromSettings();
            ApplyDeskBoxBackdrop();
            ResizeAndPosition(_windowWidth, _windowHeight);
            ApplyPendingContent();
            EnsureTopMostNoActivate();

            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds is 5000 or 10000
                ? durationMilliseconds.Value
                : 2500);
            _hideTimer.Start();

            _hasShown = true;
            StartShowAnimation();
        }
        catch
        {
            try { WindowExtensions.Hide(this); } catch { }
        }
    }

    public void UpdateNotification(
        string iconGlyph,
        string message,
        string status,
        bool compactLayout = false,
        int? windowWidthOverride = null,
        int? windowHeightOverride = null,
        IReadOnlyList<string>? detailLines = null,
        bool automationLayout = false)
    {
        var previousSize = AppWindow.Size;
        var previousWidth = previousSize.Width > 0 ? previousSize.Width : _windowWidth;
        var previousHeight = previousSize.Height > 0 ? previousSize.Height : _windowHeight;

        SetPendingContent(
            iconGlyph,
            message,
            status,
            compactLayout,
            windowWidthOverride,
            windowHeightOverride,
            detailLines,
            automationLayout);

        try
        {
            ApplyThemeFromSettings();
            ApplyDeskBoxBackdrop();
            var shouldAnimateResize = _hasShown &&
                Visible &&
                !_isHiding &&
                (previousWidth != _windowWidth || previousHeight != _windowHeight);

            if (shouldAnimateResize)
            {
                StartResizeAnimation(previousWidth, previousHeight, _windowWidth, _windowHeight);
            }
            else
            {
                ResizeAndPosition(_windowWidth, _windowHeight);
            }

            ApplyPendingContent();
            EnsureTopMostNoActivate();

            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(NotificationAnimationSettings.AutoHideDelayMs);
            _hideTimer.Start();

            _animationTimer.Stop();
            _isAnimating = false;
            _hideCompletedAction = null;

            if (!_hasShown || !Visible || _isHiding)
            {
                _hasShown = true;
                StartShowAnimation();
            }
            else
            {
                _isHiding = false;
                _animationRevision++;
                SetWindowOpacity(NotificationAnimationSettings.WindowOpacityMax);
            }
        }
        catch
        {
            try { WindowExtensions.Hide(this); } catch { }
        }
    }

    public void DismissNow()
    {
        _hideTimer.Stop();
        StartHideAnimation();
    }

    private void SetPendingContent(
        string iconGlyph,
        string message,
        string status,
        bool compactLayout,
        int? windowWidthOverride,
        int? windowHeightOverride,
        IReadOnlyList<string>? detailLines,
        bool automationLayout,
        bool deskBoxLayout = false)
    {
        _pendingGlyph = string.IsNullOrWhiteSpace(iconGlyph) ? DeviceVisualHelper.SpeakerGlyph : iconGlyph;
        _pendingMessage = message ?? string.Empty;
        _pendingStatus = status ?? string.Empty;
        _pendingCompactLayout = compactLayout;
        _pendingDetailLines = detailLines?.Where(line => !string.IsNullOrWhiteSpace(line)).Take(4).ToArray() ?? [];
        _pendingAutomationLayout = automationLayout;
        _pendingDeskBoxLayout = deskBoxLayout;
        _windowWidth = windowWidthOverride ??
            (deskBoxLayout
                ? GetDeskBoxResourceInt("DeskBoxTestWidth", 250)
                : (compactLayout ? CompactWindowWidth : DefaultWindowWidth));
        _windowHeight = windowHeightOverride ??
            (deskBoxLayout
                ? GetDeskBoxResourceInt("DeskBoxTestHeight", 54)
                : GetDefaultWindowHeight(compactLayout, _pendingDetailLines.Length));
    }

    private void ApplyThemeFromSettings()
    {
        try
        {
            var appSettings = Ioc.Default.GetRequiredService<IAppSettingsService>();
            RootContainer.RequestedTheme = appSettings.Theme == ElementTheme.Default
                ? (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
                : appSettings.Theme;
        }
        catch
        {
            RootContainer.RequestedTheme = ElementTheme.Default;
        }
    }

    private void ApplyDeskBoxBackdrop()
    {
        try
        {
            NotificationWin32Helper.ConfigureDeskBoxWindow(this.GetWindowHandle());
            var appSettings = Ioc.Default.GetRequiredService<IAppSettingsService>();
            var isDark = RootContainer.ActualTheme == ElementTheme.Dark;
            var materialType = NotificationMaterialSettings.ResolveNotificationMaterialType(
                appSettings.BackdropType);
            var accentColor = GetAccentColor();
            var tintColor = NotificationMaterialSettings.IsMicaMaterial(materialType) ||
                NotificationMaterialSettings.IsAcrylicMaterial(materialType)
                ? NotificationMaterialVisualCalculator.BuildContentTintColor(isDark, accentColor)
                : NotificationMaterialVisualCalculator.BuildContentSolidSurfaceColor(
                    isDark,
                    accentColor,
                    NotificationMaterialSettings.DefaultWidgetOpacity);

            RootContainer.Background = new SolidColorBrush(
                WinUIColor.FromArgb(0x01, 0x00, 0x00, 0x00));
            NotificationSurface.Background = new SolidColorBrush(
                WinUIColor.FromArgb(0x01, 0x00, 0x00, 0x00));
            NotificationCard.Background = new SolidColorBrush(
                WinUIColor.FromArgb(0x00, 0x00, 0x00, 0x00));
            ApplyDeskBoxBorder(isDark);

            _deskBoxBackdrop.Apply(
                this,
                isDark,
                materialType,
                tintColor,
                NotificationMaterialSettings.DefaultWidgetOpacity,
                NotificationMaterialSettings.DefaultWidgetMaterialIntensity);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to apply DeskBox notification backdrop; using legacy accent fallback.");
            ClearDeskBoxBackdrop();
            ApplyLegacyDeskBoxFallback();
        }
    }

    private void ApplyDeskBoxBorder(bool isDark)
    {
        byte channel = isDark ? (byte)0xFF : (byte)0x00;
        byte opacity = (byte)Math.Clamp(
            GetDeskBoxResourceInt("DeskBoxFloatingBorderOpacity", 48),
            0,
            255);
        NotificationSurface.BorderThickness = new Thickness(1);
        NotificationSurface.BorderBrush = new SolidColorBrush(
            WinUIColor.FromArgb(opacity, channel, channel, channel));
        NotificationCard.BorderThickness = new Thickness(0);
        NotificationCard.BorderBrush = new SolidColorBrush(
            WinUIColor.FromArgb(0x00, channel, channel, channel));
    }

    private void OnRootContainerLoaded(object sender, RoutedEventArgs args)
    {
        ApplyThemeFromSettings();
        ApplyDeskBoxBackdrop();
    }

    private void OnRootContainerActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyDeskBoxBackdrop();
    }

    private void OnNotificationWindowActivated(
        object sender,
        WindowActivatedEventArgs args)
    {
        _deskBoxBackdrop.UpdateActivationState(
            args.WindowActivationState != WindowActivationState.Deactivated);
    }

    private void ClearDeskBoxBackdrop()
    {
        _deskBoxBackdrop.Clear(this);
    }

    private static WinUIColor GetAccentColor()
    {
        try
        {
            return new UISettings().GetColorValue(UIColorType.Accent);
        }
        catch
        {
            return WinUIColor.FromArgb(0xFF, 0x00, 0x78, 0xD4);
        }
    }

    private void ApplySolidDeskBoxFallback()
    {
        try
        {
            NotificationSurface.Background = new SolidColorBrush(
                NotificationMaterialVisualCalculator.BuildContentSolidSurfaceColor(
                    RootContainer.ActualTheme == ElementTheme.Dark,
                    GetAccentColor(),
                    NotificationMaterialSettings.DefaultWidgetOpacity));
            NotificationCard.Background = new SolidColorBrush(
                WinUIColor.FromArgb(0x00, 0x00, 0x00, 0x00));
        }
        catch
        {
            NotificationSurface.Background = new SolidColorBrush(
                WinUIColor.FromArgb(0xCC, 0x2A, 0x30, 0x38));
            NotificationCard.Background = new SolidColorBrush(
                WinUIColor.FromArgb(0x00, 0x00, 0x00, 0x00));
        }
    }

    private void ApplyLegacyDeskBoxFallback()
    {
        try
        {
            var hwnd = this.GetWindowHandle();
            if (hwnd == IntPtr.Zero)
            {
                ApplySolidDeskBoxFallback();
                return;
            }

            bool isDark = RootContainer.ActualTheme == ElementTheme.Dark;
            var accentColor = GetAccentColor();
            var tintColor = NotificationMaterialVisualCalculator.BuildContentTintColor(
                isDark,
                accentColor);
            NotificationWin32Helper.ApplyAccentBlur(
                hwnd,
                tintColor,
                NotificationMaterialSettings.DefaultWidgetOpacity,
                enabled: true);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Legacy notification backdrop fallback failed.");
            ApplySolidDeskBoxFallback();
        }
    }

    private void ApplyPendingContent()
    {
        NotificationCard.Padding = _pendingDeskBoxLayout
            ? GetDeskBoxResourceThickness("DeskBoxFloatingPadding", new Thickness(14, 0, 14, 0))
            : new Thickness(16, 0, 16, 0);

        var cornerRadius = _pendingDeskBoxLayout
            ? GetDeskBoxResourceDouble("DeskBoxFloatingCornerRadius", 8)
            : 8;
        NotificationSurface.CornerRadius = new CornerRadius(cornerRadius);
        NotificationCard.CornerRadius = new CornerRadius(cornerRadius);

        if (_pendingAutomationLayout)
        {
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            MergedContentGrid.Visibility = Visibility.Collapsed;
            CompactContentPanel.Visibility = Visibility.Collapsed;
            AutomationContentGrid.Visibility = Visibility.Visible;

            AutomationIconGlyph.Text = _pendingGlyph;
            AutomationTitleText.Text = _pendingMessage;
            AutomationActionsPanel.Children.Clear();

            foreach (var detailLine in _pendingDetailLines)
            {
                AutomationActionsPanel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = detailLine,
                    FontFamily = new FontFamily("Segoe UI Variable Text"),
                    FontSize = AutomationDetailTextFontSize,
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                    LineHeight = AutomationDetailTextFontSize,
                    Opacity = 0.68,
                    TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxLines = 1
                });
            }

            return;
        }

        if (_pendingCompactLayout)
        {
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            MergedContentGrid.Visibility = Visibility.Collapsed;
            AutomationContentGrid.Visibility = Visibility.Collapsed;
            CompactContentPanel.Visibility = Visibility.Visible;

            CompactIconGlyph.Text = _pendingGlyph;
            CompactMessageText.Text = _pendingMessage;
            CompactStatusText.Text = _pendingStatus;
            CompactStatusText.Visibility = string.IsNullOrWhiteSpace(_pendingStatus) ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        if (_pendingDeskBoxLayout)
        {
            DefaultContentGrid.Visibility = Visibility.Visible;
            MergedContentGrid.Visibility = Visibility.Collapsed;
            AutomationContentGrid.Visibility = Visibility.Collapsed;
            CompactContentPanel.Visibility = Visibility.Collapsed;
        }

        if (_pendingDetailLines.Length > 0)
        {
            DefaultContentGrid.Visibility = Visibility.Collapsed;
            CompactContentPanel.Visibility = Visibility.Collapsed;
            AutomationContentGrid.Visibility = Visibility.Collapsed;
            MergedContentGrid.Visibility = Visibility.Visible;

            MergedIconGlyph.Text = _pendingGlyph;
            MergedMessageText.Text = _pendingMessage;
            MergedStatusText.Text = _pendingStatus;
            MergedStatusText.Visibility = string.IsNullOrWhiteSpace(_pendingStatus) ? Visibility.Collapsed : Visibility.Visible;
            MergedDetailsPanel.Children.Clear();

            foreach (var detailLine in _pendingDetailLines)
            {
                MergedDetailsPanel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = detailLine,
                    FontFamily = new FontFamily("Segoe UI Variable Text"),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                    LineHeight = 16,
                    Opacity = 0.62,
                    TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxLines = 1
                });
            }

            return;
        }

        DefaultContentGrid.Visibility = Visibility.Visible;
        CompactContentPanel.Visibility = Visibility.Collapsed;
        AutomationContentGrid.Visibility = Visibility.Collapsed;
        MergedContentGrid.Visibility = Visibility.Collapsed;

        IconGlyph.Text = _pendingGlyph;
        MessageText.Text = _pendingMessage;
        StatusText.Text = _pendingStatus;
        StatusText.Visibility = string.IsNullOrWhiteSpace(_pendingStatus) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PositionToBottomCenter()
        => PositionToBottomCenter(_windowWidth, _windowHeight);

    private void PositionToBottomCenter(int windowWidth, int windowHeight)
    {
        var displayArea = DisplayArea.Primary;
        if (displayArea == null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        AppWindow.Move(new PointInt32(
            workArea.X + (workArea.Width - windowWidth) / 2,
            workArea.Y + workArea.Height - windowHeight - WindowBottomMargin));
    }

    private void ResizeAndPosition(int windowWidth, int windowHeight)
    {
        AppWindow.Resize(new SizeInt32(windowWidth, windowHeight));
        PositionToBottomCenter(windowWidth, windowHeight);
    }

    private void EnsureTopMostNoActivate()
    {
        try
        {
            if (!Visible)
            {
                Activate();
            }

            var hwnd = new HWND(this.GetWindowHandle());
            var exStyle = (int)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | 0x08000000);
            PInvoke.SetWindowPos(
                hwnd,
                new HWND(-1),
                0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
                SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        }
        catch
        {
            Activate();
        }
    }

    private void StartShowAnimation()
    {
        if (_pendingDeskBoxLayout)
        {
            _isHiding = false;
            _isAnimating = false;
            _animationTimer.Stop();
            _animationRevision++;
            DeskBoxFloatingTransitionHelper.PlayEnter(RootContainer);
            return;
        }

        _isHiding = false;
        _isResizing = false;
        _resizeTimer.Stop();
        _animationRevision++;

        SetWindowOpacity(NotificationAnimationSettings.WindowOpacityMin);
        BeginWindowAnimation(entering: true);
    }

    private void StartHideAnimation()
    {
        if (_isHiding)
        {
            return;
        }

        if (_pendingDeskBoxLayout)
        {
            _isHiding = true;
            _isAnimating = false;
            _animationTimer.Stop();
            var deskBoxRevision = ++_animationRevision;
            _hideCompletedAction = () =>
            {
                if (deskBoxRevision != _animationRevision)
                {
                    _isHiding = false;
                    return;
                }

                try { WindowExtensions.Hide(this); } catch { }
                ClearDeskBoxBackdrop();
                DeskBoxFloatingTransitionHelper.Reset(RootContainer);
                _isHiding = false;
                _hasShown = false;
                NotificationDismissed?.Invoke(this, EventArgs.Empty);
            };
            _ = PlayDeskBoxHideAsync(deskBoxRevision);
            return;
        }

        _isHiding = true;
        _isResizing = false;
        _resizeTimer.Stop();
        var revision = ++_animationRevision;

        BeginWindowAnimation(entering: false);

        _hideCompletedAction = () =>
        {
            if (revision != _animationRevision)
            {
                _isHiding = false;
                return;
            }

            try { WindowExtensions.Hide(this); } catch { }
            ClearDeskBoxBackdrop();
            SetWindowOpacity(NotificationAnimationSettings.WindowOpacityMin);
            _isHiding = false;
            _hasShown = false;
            NotificationDismissed?.Invoke(this, EventArgs.Empty);
        };
    }

    private async Task PlayDeskBoxHideAsync(int revision)
    {
        try
        {
            await DeskBoxFloatingTransitionHelper.PlayExitAsync(RootContainer);
        }
        catch
        {
        }

        if (revision == _animationRevision && _hideCompletedAction is Action finished)
        {
            _hideCompletedAction = null;
            finished();
        }
    }

    private void OnHideTimerTick(object? sender, object e)
    {
        _hideTimer.Stop();
        StartHideAnimation();
    }

    private void OnAnimationTimerTick(object? sender, object e)
    {
        if (!_isAnimating)
        {
            _animationTimer.Stop();
            return;
        }

        var elapsed = (DateTimeOffset.Now - _animationStartTime).TotalMilliseconds;
        var duration = _isHiding ? NotificationAnimationSettings.HideDurationMs : NotificationAnimationSettings.ShowDurationMs;
        var progress = Math.Clamp(elapsed / duration, 0, 1);
        var easedProgress = GetEasedProgress(progress, entering: !_isHiding);
        var x = (int)Math.Round(_animationStartPosition.X + (_animationEndPosition.X - _animationStartPosition.X) * easedProgress);
        var y = (int)Math.Round(_animationStartPosition.Y + (_animationEndPosition.Y - _animationStartPosition.Y) * easedProgress);
        AppWindow.Move(new PointInt32(x, y));
        var opacity = _isHiding ? 1 - easedProgress : easedProgress;
        SetWindowOpacity((int)Math.Round(NotificationAnimationSettings.WindowOpacityMax * opacity));

        if (progress >= 1)
        {
            _isAnimating = false;
            _animationTimer.Stop();

            if (_isHiding && _hideCompletedAction is Action finished)
            {
                _hideCompletedAction = null;
                finished();
            }
        }
    }

    private void OnResizeTimerTick(object? sender, object e)
    {
        if (!_isResizing)
        {
            _resizeTimer.Stop();
            return;
        }

        var elapsed = (DateTimeOffset.Now - _resizeStartTime).TotalMilliseconds;
        var progress = Math.Clamp(elapsed / NotificationAnimationSettings.ResizeDurationMs, 0, 1);
        var easedProgress = 1 - Math.Pow(1 - progress, NotificationAnimationSettings.CubicEasePower);
        var width = (int)Math.Round(_resizeStartWidth + (_resizeEndWidth - _resizeStartWidth) * easedProgress);
        var height = (int)Math.Round(_resizeStartHeight + (_resizeEndHeight - _resizeStartHeight) * easedProgress);
        ResizeAndPosition(width, height);

        if (progress >= 1)
        {
            _isResizing = false;
            _resizeTimer.Stop();
            ResizeAndPosition(_resizeEndWidth, _resizeEndHeight);
        }
    }

    private void StartResizeAnimation(int startWidth, int startHeight, int endWidth, int endHeight)
    {
        _resizeStartWidth = startWidth;
        _resizeStartHeight = startHeight;
        _resizeEndWidth = endWidth;
        _resizeEndHeight = endHeight;
        _resizeStartTime = DateTimeOffset.Now;
        _isResizing = true;
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(NotificationAnimationSettings.AnimationFrameMs);
        _resizeTimer.Start();
    }

    private void BeginWindowAnimation(bool entering)
    {
        var displayArea = DisplayArea.Primary;
        if (displayArea == null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        var targetX = workArea.X + (workArea.Width - _windowWidth) / 2;
        var targetY = workArea.Y + workArea.Height - _windowHeight - WindowBottomMargin;
        var offsetY = _pendingDeskBoxLayout
            ? GetDeskBoxResourceDouble("DeskBoxFloatingShowOffsetY", NotificationAnimationSettings.StartOffsetY)
            : NotificationAnimationSettings.StartOffsetY;
        var startY = entering ? targetY + (int)offsetY : targetY;
        var endY = entering ? targetY : targetY + (int)offsetY;

        _animationStartPosition = new PointInt32(targetX, startY);
        _animationEndPosition = new PointInt32(targetX, endY);
        _animationStartTime = DateTimeOffset.Now;
        _isAnimating = true;
        _isHiding = !entering;

        AppWindow.Move(_animationStartPosition);
        SetWindowOpacity(entering ? NotificationAnimationSettings.WindowOpacityMin : NotificationAnimationSettings.WindowOpacityMax);
        _animationTimer.Interval = TimeSpan.FromMilliseconds(NotificationAnimationSettings.AnimationFrameMs);
        _animationTimer.Start();
    }

    private static double GetEasedProgress(double progress, bool entering)
    {
        progress = Math.Clamp(progress, 0, 1);
        return entering
            ? 1 - Math.Pow(1 - progress, NotificationAnimationSettings.CubicEasePower)
            : Math.Pow(progress, NotificationAnimationSettings.CubicEasePower);
    }

    private void SetWindowOpacity(int alpha)
    {
        alpha = Math.Clamp(alpha, NotificationAnimationSettings.WindowOpacityMin, NotificationAnimationSettings.WindowOpacityMax);
        RootContainer.Opacity = alpha / 255.0;
    }

    private int ScaleByWindow(int baseValue, double multiplier = 1.0)
    {
        return Math.Max(1, (int)Math.Round(baseValue * LayoutScale * multiplier));
    }

    private static int GetDeskBoxResourceInt(string key, int fallback)
        => (int)Math.Round(GetDeskBoxResourceDouble(key, fallback));

    private static double GetDeskBoxResourceDouble(string key, double fallback)
    {
        try
        {
            return Application.Current.Resources.TryGetValue(key, out var value) && value is double number
                ? number
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static Thickness GetDeskBoxResourceThickness(string key, Thickness fallback)
    {
        try
        {
            return Application.Current.Resources.TryGetValue(key, out var value) && value is Thickness thickness
                ? thickness
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static int GetDefaultWindowHeight(bool compactLayout, int detailLineCount)
    {
        if (detailLineCount <= 0)
        {
            return compactLayout ? CompactWindowHeight : DefaultWindowHeight;
        }

        return detailLineCount switch
        {
            1 => 136,
            2 => 158,
            _ => 180
        };
    }
}
