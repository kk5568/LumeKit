using System.Diagnostics;
using CommunityToolkit.Mvvm.DependencyInjection;
#if !DISABLE_XAML_GENERATED_MAIN
using Microsoft.Extensions.Configuration;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using BluetoothAudioReceiver.Models;
using BluetoothAudioReceiver.Services;
using BluetoothAudioReceiver.Services.Monitor;
using System.Runtime.InteropServices;
using System.Text;

namespace BluetoothAudioReceiver;

public partial class App : Application
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(App));
    private static readonly string StartupTraceLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAudioReceiver",
        "startup-trace.log");

    #region Main Window

    public static MainWindow MainWindow { get; set; } = null!;

#if !DISABLE_XAML_GENERATED_MAIN && SINGLE_INSTANCE
    private static bool IsExistWindow { get; set; } = false;
#endif

#if TRAY_ICON
    public static bool CanCloseWindow { get; set; } = false;
#endif

    #endregion

    #region Tray Icon

#if TRAY_ICON
    public static TrayMenuControl TrayIcon { get; set; } = null!;
#endif

    #endregion

    #region Splash Screen

    public static TaskCompletionSource? SplashScreenLoadingTCS { get; private set; }

    #endregion

    #region Constructor

    public App()
    {
        TryWriteStartupTrace("App.ctor: enter");
#if !DISABLE_XAML_GENERATED_MAIN && SINGLE_INSTANCE
        // Check if app is already running
        if (SystemHelper.IsWindowExist(null, ConstantHelper.AppDisplayName, true))
        {
            IsExistWindow = true;
            Current.Exit();
            return;
        }
#endif

        // Initialize the component
        TryWriteStartupTrace("App.ctor: before InitializeComponent");
        InitializeComponent();
        TryWriteStartupTrace("App.ctor: InitializeComponent done");

#if !DISABLE_XAML_GENERATED_MAIN
        // Initialize core helpers
        LocalSettingsHelper.Initialize();

        // Set up Logging
        Environment.SetEnvironmentVariable("LOGGING_ROOT", Path.Combine(LocalSettingsHelper.LogDirectory, InfoHelper.GetVersion().ToString()));
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();
#endif

        // Build the host
        var host = Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging(builder => builder
                .AddSerilog(dispose: true))
            .UseDefaultServiceProvider((context, options) =>
            {
                options.ValidateOnBuild = true;
            })
            .ConfigureServices((context, services) =>
            {
                #region Core Service

                // Default Activation Handler
                services.AddSingleton<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                // Other Activation Handlers
                services.AddSingleton<IActivationHandler, AppNotificationActivationHandler>();

                // Windows Activation
                services.AddSingleton<IActivationService, ActivationService>();

                // Notifications
                services.AddSingleton<IAppNotificationService, AppNotificationService>();

                // File Storage
                services.AddSingleton<IFileService, FileService>();

                // Theme Management
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();

                // Backdrop Management
                services.AddSingleton<IBackdropSelectorService, BackdropSelectorService>();

                // Dialog Managment
                services.AddSingleton<IDialogService, DialogService>();

                // Bluetooth Audio Playback Connection
                services.AddSingleton<IAudioPlaybackConnectionService, AudioPlaybackConnectionService>();

                // Notification Flyout
                services.AddSingleton<INotificationService, NotificationService>();

                // Audio route and global hotkeys
                services.AddSingleton<IAudioRouteHotkeyService, AudioRouteHotkeyService>();

                // Main window: Allow access to the main window
                // from anywhere in the application.
                services.AddSingleton(_ => (Window)MainWindow);

                // DispatcherQueue: Allow access to the DispatcherQueue for
                // the main window for general purpose UI thread access.
                services.AddSingleton(_ => MainWindow.DispatcherQueue);

                #endregion

                #region Navigation Service

                // MainWindow Pages
                services.AddSingleton<IPageService, PageService>();

                // MainWindow Navigation View
                services.AddSingleton<INavigationViewService, NavigationViewService>();

                // MainWindow Navigation
                services.AddSingleton<INavigationService, NavigationService>();

                #endregion

                #region Settings Service

                // Local Stettings
                services.AddSingleton<LocalSettingsKeys>();

                // Local Storage
                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();

                // Settings Management
                services.AddSingleton<IAppSettingsService, AppSettingsService>();

                #endregion

                #region Auto Brightness Service

                // Brightness Settings
                services.AddSingleton<BrightnessSettings>();

                // Camera Reader Service
                services.AddSingleton<CameraReaderService>();

                // Monitor Manager
                services.AddSingleton<MonitorManager>();

                // Brightness Controller
                services.AddSingleton<BrightnessController>();

                // Auto Brightness Service
                services.AddSingleton<AutoBrightnessService>();

                #endregion

                #region Views & ViewModels

                // Main Window Pages
                services.AddTransient<NavShellPageViewModel>();
                services.AddTransient<NavShellPage>();
                services.AddTransient<SettingsPageViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<BluetoothPageViewModel>();
                services.AddTransient<BluetoothPage>();
                services.AddTransient<AutoBrightnessPageViewModel>();
                services.AddTransient<AutoBrightnessPage>();
                services.AddTransient<NotificationPageViewModel>();
                services.AddTransient<NotificationPage>();

                #endregion
            })
            .Build();
        TryWriteStartupTrace("App.ctor: host build done");
        TryWriteStartupTrace("App.ctor: before ConfigureServices");
        Ioc.Default.ConfigureServices(host.Services);
        TryWriteStartupTrace("App.ctor: ConfigureServices done");

        // Configure exception handlers
        TryWriteStartupTrace("App.ctor: before attach UnhandledException handlers");
        UnhandledException += (sender, e) => HandleAppUnhandledException(e.Exception, true);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleAppUnhandledException(e.ExceptionObject as Exception, false);
        TaskScheduler.UnobservedTaskException += (sender, e) => HandleAppUnhandledException(e.Exception, false);
        TryWriteStartupTrace("App.ctor: attach UnhandledException handlers done");

        // Initialize core services
        TryWriteStartupTrace("App.ctor: before AppSettingsService.Initialize");
        Ioc.Default.GetRequiredService<IAppSettingsService>().Initialize();
        TryWriteStartupTrace("App.ctor: AppSettingsService.Initialize done");
        TryWriteStartupTrace("App.ctor: before AppNotificationService.Initialize");
        Ioc.Default.GetRequiredService<IAppNotificationService>().Initialize();
        TryWriteStartupTrace("App.ctor: AppNotificationService.Initialize done");
        TryWriteStartupTrace("App.ctor: settings & notification initialized");

        // Initialize auto brightness services
        var cameraReader = Ioc.Default.GetRequiredService<CameraReaderService>();
        _ = cameraReader.InitializeAsync();
        TryWriteStartupTrace("App.ctor: camera init started");

        _log.Information($"App initialized. Language: {AppLanguageHelper.PreferredLanguage}.");
        TryWriteStartupTrace("App.ctor: exit");
    }

    #endregion

    #region App Lifecycle

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        TryWriteStartupTrace("App.OnLaunched: enter");
        base.OnLaunched(args);

#if !DISABLE_XAML_GENERATED_MAIN && SINGLE_INSTANCE
        if (IsExistWindow)
        {
            return;
        }
#endif

        // Ensure the current window is active
        if (MainWindow != null)
        {
            return;
        }

        _ = ActivateAsync();

        async Task ActivateAsync()
        {
            TryWriteStartupTrace("App.OnLaunched: ActivateAsync enter");
            // AppLifecycle may be unavailable in some unpackaged startup paths.
            object appActivationArguments = args;
            try
            {
                appActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80040154)
            {
                TryWriteStartupTrace("App.OnLaunched: AppInstance unavailable, use LaunchActivatedEventArgs");
                _log.Warning(ex, "AppInstance unavailable in OnLaunched, fallback to launch args.");
            }

            // Initialize the window
            MainWindow = new MainWindow();
            TryWriteStartupTrace("App.OnLaunched: MainWindow created");

#if SPLASH_SCREEN
            // Show the splash screen
            SplashScreenLoadingTCS = new TaskCompletionSource();
            await Ioc.Default.GetRequiredService<IActivationService>().LaunchMainWindowAsync(appActivationArguments);

            // Activate the window
            MainWindow.Activate();
#endif

            _log.Information($"App launched. Launch args type: {args.GetType().Name}.");

#if SPLASH_SCREEN
            static async Task WithTimeoutAsync(Task task, TimeSpan timeout)
            {
                if (task == await Task.WhenAny(task, Task.Delay(timeout)))
                {
                    await task;
                }
            }

            // Wait for the UI to update
            await WithTimeoutAsync(SplashScreenLoadingTCS!.Task, TimeSpan.FromMilliseconds(500));
            SplashScreenLoadingTCS = null;
#endif

            // Check startup
            _ = StartupHelper.CheckStartup();

            // TODO: Initialize others things

            await Ioc.Default.GetRequiredService<IActivationService>().ActivateMainWindowAsync(args);
            TryWriteStartupTrace("App.OnLaunched: ActivateMainWindowAsync done");

            // Register global hotkeys after the window is fully initialized.
            Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>().InitializeForWindow(MainWindow);
            TryWriteStartupTrace("App.OnLaunched: hotkey service initialized");

            // 如果通过开机自启参数(/startup)启动，则隐藏主窗口
            // 应用仍在后台运行（系统托盘图标可用）
            if (Environment.GetCommandLineArgs().Contains(StartupHelper.NonMsixStartupTag, StringComparer.OrdinalIgnoreCase))
            {
                MainWindow.Hide();
                TryWriteStartupTrace("App.OnLaunched: startup tag hide window");
            }
        }
    }

    private static void TryWriteStartupTrace(string stage)
    {
        try
        {
            var dir = Path.GetDirectoryName(StartupTraceLogPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {stage}{Environment.NewLine}";
            File.AppendAllText(StartupTraceLogPath, line, Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }

    private static void HandleAppUnhandledException(Exception? ex, bool showToastNotification)
    {
        var exceptionString = ExceptionFormatter.FormatExcpetion(ex);

        Debugger.Break();

        // Log the error
        _log.Fatal(ex, $"An unhandled error occurred : {exceptionString}");

        // Close the log
        Log.CloseAndFlush();

        // Try to show a notification
        if (showToastNotification)
        {
            Ioc.Default.GetRequiredService<IAppNotificationService>().TryShow(
                string.Format("AppNotificationUnhandledExceptionPayload".GetLocalizedString(),
                $"{ex?.ToString()}{Environment.NewLine}"));
        }

        // We are very likely in a bad and unrecoverable state, so ensure Dev Home crashes w/ the exception info.
        Environment.FailFast(exceptionString, ex);
    }

#if DISABLE_XAML_GENERATED_MAIN
    public async Task OnActivatedAsync(AppActivationArguments activatedEventArgs)
    {
        _log.Information($"App is activated. Activation type: {activatedEventArgs.Data.GetType().Name}");

        await MainWindow.EnqueueOrInvokeAsync(async (_) => await Ioc.Default.GetRequiredService<IActivationService>().ActivateMainWindowAsync(activatedEventArgs));
    }
#endif

    public static async new void Exit()
    {
        _log.Information("Exiting current application");

        // Unregister app notification service
        Ioc.Default.GetRequiredService<IAppNotificationService>().Unregister();

        // Close all windows
        await WindowsExtensions.CloseAllWindowsAsync();

        Current.Exit();
    }

    public static void RestartApplication(string? param = null, bool admin = false)
    {
        _log.Information("Restarting current application with args: {param}, admin: {admin}", param, admin);

        // Get the path to the executable
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;

        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            // Start a new instance of the application
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    Arguments = param,
                    Verb = admin ? "runas" : string.Empty
                });
            }
            catch (Exception)
            {
                // Ignore any exceptions that occur while starting the new process
            }

            // Close the log
            Log.CloseAndFlush();

            // Kill the current process
            Process.GetCurrentProcess().Kill();
        }
    }

    #endregion
}
