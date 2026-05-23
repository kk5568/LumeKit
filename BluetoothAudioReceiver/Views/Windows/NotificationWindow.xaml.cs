using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinUIEx;

using HWND = global::Windows.Win32.Foundation.HWND;
using PInvoke = global::Windows.Win32.PInvoke;
using SET_WINDOW_POS_FLAGS = global::Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS;
using WINDOW_LONG_PTR_INDEX = global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX;

namespace BluetoothAudioReceiver.Views.Windows;

public sealed partial class NotificationWindow : WindowEx
{
    private const int WindowWidth = 600;
    private const int WindowHeight = 110;
    private const int ShowDurationMs = 240;
    private const int HideDurationMs = 240;
    private const int AutoHideDelayMs = 2500;
    private const double StartOffsetY = 20;
    private const int AnimationFrameMs = 16;
    private const int WindowOpacityMin = 0;
    private const int WindowOpacityMax = 255;
    private const double BaseWindowHeight = 110.0;

    // Proportional layout values driven by the current window height.
    public double LayoutScale => WindowHeight / BaseWindowHeight;
    public int NotificationCardHeight => ScaleByWindow(74);
    public int IconBoxSize => ScaleByWindow(30);
    public int ContentBoxHeight => ScaleByWindow(40);
    public int IconFontSize => ScaleByWindow(14);
    public int TextFontSize => ScaleByWindow(13);
    public int CenterRowOffsetY => -Math.Max(1, ScaleByWindow(1));
    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _animationTimer = new();
    private bool _isHiding;
    private bool _isAnimating;
    private int _animationRevision;
    private Action? _hideCompletedAction;
    private DateTimeOffset _animationStartTime;
    private PointInt32 _animationStartPosition;
    private PointInt32 _animationEndPosition;
    private bool _layeredStyleApplied;
    private string _pendingGlyph = "\uE7C9";
    private string _pendingMessage = string.Empty;
    private string _pendingStatus = string.Empty;

    public NotificationWindow()
    {
        InitializeComponent();

        IsTitleBarVisible = false;
        IsMinimizable = false;
        IsMaximizable = false;
        IsResizable = false;
        IsShownInSwitchers = false;

        _hideTimer.Tick += OnHideTimerTick;
        _animationTimer.Tick += OnAnimationTimerTick;
    }

    public void ShowNotification(string iconGlyph, string message, string status)
    {
        _pendingGlyph = string.IsNullOrWhiteSpace(iconGlyph) ? "\uE7C9" : iconGlyph;
        _pendingMessage = message ?? string.Empty;
        _pendingStatus = status ?? string.Empty;

        try
        {
            ApplyThemeFromSettings();
            SystemBackdrop = new MicaBackdrop();
            AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
            PositionToBottomCenter();
            EnsureLayeredOpacitySupport();
            ApplyPendingContent();
            EnsureTopMostNoActivate();

            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(AutoHideDelayMs);
            _hideTimer.Start();

            StartShowAnimation();
        }
        catch
        {
            try { WindowExtensions.Hide(this); } catch { }
        }
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

    private void ApplyPendingContent()
    {
        IconGlyph.Glyph = _pendingGlyph;
        MessageText.Text = _pendingMessage;
        StatusText.Text = _pendingStatus;
        StatusText.Visibility = string.IsNullOrWhiteSpace(_pendingStatus) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PositionToBottomCenter()
    {
        var displayArea = DisplayArea.Primary;
        if (displayArea == null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        AppWindow.Move(new PointInt32(
            workArea.X + (workArea.Width - WindowWidth) / 2,
            workArea.Y + workArea.Height - WindowHeight - 12));
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
        _isHiding = false;
        _animationRevision++;

        SetWindowOpacity(WindowOpacityMin);
        BeginWindowAnimation(entering: true);
    }

    private void StartHideAnimation()
    {
        if (_isHiding)
        {
            return;
        }

        _isHiding = true;
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
            SystemBackdrop = null;
            SetWindowOpacity(WindowOpacityMin);
            _isHiding = false;
        };
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
        var duration = _isHiding ? HideDurationMs : ShowDurationMs;
        var progress = Math.Clamp(elapsed / duration, 0, 1);
        var x = (int)Math.Round(_animationStartPosition.X + (_animationEndPosition.X - _animationStartPosition.X) * progress);
        var y = (int)Math.Round(_animationStartPosition.Y + (_animationEndPosition.Y - _animationStartPosition.Y) * progress);
        AppWindow.Move(new PointInt32(x, y));
        var opacity = _isHiding ? 1 - progress : progress;
        SetWindowOpacity((int)Math.Round(WindowOpacityMax * opacity));

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

    private void BeginWindowAnimation(bool entering)
    {
        var displayArea = DisplayArea.Primary;
        if (displayArea == null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        var targetX = workArea.X + (workArea.Width - WindowWidth) / 2;
        var targetY = workArea.Y + workArea.Height - WindowHeight - 12;
        var startY = entering ? targetY + (int)StartOffsetY : targetY;
        var endY = entering ? targetY : targetY + (int)StartOffsetY;

        _animationStartPosition = new PointInt32(targetX, startY);
        _animationEndPosition = new PointInt32(targetX, endY);
        _animationStartTime = DateTimeOffset.Now;
        _isAnimating = true;
        _isHiding = !entering;

        AppWindow.Move(_animationStartPosition);
        SetWindowOpacity(entering ? WindowOpacityMin : WindowOpacityMax);
        _animationTimer.Interval = TimeSpan.FromMilliseconds(AnimationFrameMs);
        _animationTimer.Start();
    }

    private void EnsureLayeredOpacitySupport()
    {
        if (_layeredStyleApplied)
        {
            return;
        }

        var hwnd = new HWND(this.GetWindowHandle());
        var exStyle = (int)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if ((exStyle & 0x00080000) == 0)
        {
            PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | 0x00080000);
        }

        _layeredStyleApplied = true;
    }

    private void SetWindowOpacity(int alpha)
    {
        alpha = Math.Clamp(alpha, WindowOpacityMin, WindowOpacityMax);

        if (!_layeredStyleApplied)
        {
            return;
        }

        var hwnd = new HWND(this.GetWindowHandle());
        SetLayeredWindowAttributes(hwnd, 0, (byte)alpha, 0x00000002);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    private int ScaleByWindow(int baseValue)
    {
        return Math.Max(1, (int)Math.Round(baseValue * LayoutScale));
    }
}
