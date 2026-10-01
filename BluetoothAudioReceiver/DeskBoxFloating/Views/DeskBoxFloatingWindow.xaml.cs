using BluetoothAudioReceiver.DeskBoxFloating.Helpers;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.DeskBoxFloating.Profiles;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinUIEx;

namespace BluetoothAudioReceiver.DeskBoxFloating.Views;

public sealed partial class DeskBoxFloatingWindow : WindowEx
{
    private const int WindowBottomMargin = 18;

    private readonly DispatcherTimer _hideTimer = new();
    private int _windowWidth = 250;
    private int _windowHeight = 54;
    private int _hideRevision;
    private DeskBoxFloatingLayoutProfile _profile = DeskBoxFloatingProfiles.NotificationDefault;
    private readonly DeskBoxFloatingMaterialController _materialController = new();
    private readonly DeskBoxFloatingTransitionHelper _transitionHelper;

    public event EventHandler? Dismissed;

    public DeskBoxFloatingWindow()
    {
        InitializeComponent();
        _transitionHelper = new DeskBoxFloatingTransitionHelper(RootContainer);

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
        Closed += (_, _) => _materialController.Dispose();
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

        (_windowWidth, _windowHeight) = ResolveWindowSize(request, _profile);
        AppWindow.Resize(new SizeInt32(_windowWidth, _windowHeight));
        Position(request.PlacementOverride ?? _profile.Placement, request.AnchorPoint);

        if (!Visible)
        {
            Activate();
        }

        DeskBoxFloatingWin32Helper.ConfigureWindow(this.GetWindowHandle());
        ApplyMaterial();

        _hideTimer.Stop();
        if (request.AutoDismissOverride ?? _profile.AutoDismiss)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(request.DurationMilliseconds);
            _hideTimer.Start();
        }

        _hideRevision++;
        _transitionHelper.PlayEnter();
    }

    public async void DismissNow()
    {
        _hideTimer.Stop();
        var revision = ++_hideRevision;
        try
        {
            await _transitionHelper.PlayExitAsync();
        }
        catch
        {
        }

        if (revision != _hideRevision)
        {
            return;
        }

        try { WindowExtensions.Hide(this); } catch { }
        _transitionHelper.Reset();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

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

    private void ApplyMaterial()
    {
        try
        {
            var theme = App.MainWindow.Content is FrameworkElement element
                ? element.ActualTheme
                : ElementTheme.Default;
            _materialController.Apply(
                this,
                RootContainer,
                theme == ElementTheme.Dark);
        }
        catch
        {
            SystemBackdrop ??= new MicaBackdrop();
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
