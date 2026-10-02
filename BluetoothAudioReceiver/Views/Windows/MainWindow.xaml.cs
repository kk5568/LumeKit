using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BluetoothAudioReceiver.Core.Contracts.Services;
using Serilog;
using Windows.Graphics;
using Windows.Win32;
using WinUIEx;

namespace BluetoothAudioReceiver.Views.Windows;

public sealed partial class MainWindow : WindowEx
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(MainWindow));

    #region Position & Size

    public PointInt32 Position
    {
        get => AppWindow.Position;
        set => this.Move(value.X, value.Y);
    }

    public SizeInt32 Size
    {
        get => new((int)(AppWindow.Size.Width * 96f / this.GetDpiForWindow()), (int)(AppWindow.Size.Height * 96f / this.GetDpiForWindow()));
        set => this.SetWindowSize(value.Width, value.Height);
    }

    #endregion

    #region UI Elements

    public UIElement? TitleBar { get; set; }

    public UIElement? TitleBarText { get; set; }

    public UIElement? Shell { get; set; }

    #endregion

    #region Manager & Handle

    public WindowManager WindowManager => _manager;
    public IntPtr WindowHandle => _handle;

    private readonly WindowManager _manager;
    private readonly IntPtr _handle;

    #endregion

    public MainWindow()
    {
        InitializeComponent();

        _manager = WindowManager.Get(this);
        _handle = this.GetWindowHandle();

        AppWindow.SetIcon(Constants.AppIconPath);
        Title = ConstantHelper.AppDisplayName;
        Content = null;

    }

    #region Hide & Show & Activate

    private bool activated = false;

    public void Hide()
    {
        WindowExtensions.Hide(this);
    }

    public void Show()
    {
        if (!activated)
        {
            Activate();
        }
        else
        {
            AppWindow.Show();
        }

        if (PInvoke.IsIconic(new(WindowHandle)))
        {
            this.Restore();
        }

        // This handles updating the caption button colors correctly when windows system theme is changed while the app is shown
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, TitleBarText);
    }

    public new void Activate()
    {
        base.Activate();
        activated = true;
    }

    #endregion

    #region Events

    // This handles updating the caption button colors correctly when windows system theme is changed while the visibility of the app is changed
    private void MainWindow_VisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        if (args.Visible)
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons(this, TitleBarText);
        }
    }

    // this enables the app to continue running in background after clicking close button
    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
#if TRAY_ICON
        _log.Information("MainWindow closed. CanCloseWindow={CanCloseWindow}, IsExiting={IsExiting}", App.CanCloseWindow, App.IsExiting);
#else
        _log.Information("MainWindow closed. IsExiting={IsExiting}", App.IsExiting);
#endif
#if TRAY_ICON
        if (!App.CanCloseWindow)
        {
            _log.Information("MainWindow close was intercepted and hidden to keep the app running in background.");
            Hide();
            args.Handled = true;
            return;
        }
#endif
        VisibilityChanged -= MainWindow_VisibilityChanged;
        Closed -= MainWindow_Closed;

        if (App.IsExiting)
        {
            _log.Information("MainWindow closed during app exit.");
            return;
        }

        _log.Information("MainWindow closed, calling App.ExitAsync.");
        await App.ExitAsync();
    }

    public async Task InitializeApplicationAsync(object activatedEventArgs, bool showWindow = true)
    {
        var rootFrame = EnsureWindowIsInitialized(false);

        if (rootFrame is null)
        {
            return;
        }

        if (showWindow)
        {
            // Show window for normal launch/restore paths.
            AppWindow.Show();
            Activate();

            // Bring to front
            BringToFront();

            // Restore window if minimized
            if (PInvoke.IsIconic(new(WindowHandle)))
            {
                this.Restore();
            }
        }

        await Task.CompletedTask;
    }

    private Frame? EnsureWindowIsInitialized(bool splash)
    {
        try
        {
            if (splash)
            {
                if (Content is not Frame splashFrame)
                {
                    // Create a Frame to act as the navigation context and navigate to the first page
                    splashFrame = new() { CacheSize = 1 };
                    splashFrame.NavigationFailed += (s, e) =>
                    {
                        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
                    };

                    // Place the frame in the current Window
                    Content = splashFrame;
                }

                return splashFrame;
            }
            else
            {
                if (Content is not NavShellPage shell)
                {
                    shell = Ioc.Default.GetRequiredService<NavShellPage>();
                    if (shell == null)
                    {
                        var frame = new Frame();
                        Content = frame;
                        return frame;
                    }
                    else
                    {
                        Shell = shell;
                        Content = shell;
                        return shell.ShellFrame;
                    }
                }
                else
                {
                    return shell.ShellFrame;
                }
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    #endregion
}
