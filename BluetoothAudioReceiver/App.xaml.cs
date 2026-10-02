using System.Diagnostics;
using CommunityToolkit.Mvvm.DependencyInjection;
#if !DISABLE_XAML_GENERATED_MAIN
using Microsoft.Extensions.Configuration;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Helpers.Application;
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
    public static DispatcherQueue? AppDispatcherQueue { get; set; }

#if !DISABLE_XAML_GENERATED_MAIN && SINGLE_INSTANCE
    private static bool IsExistWindow { get; set; } = false;
#endif

#if TRAY_ICON
    public static bool CanCloseWindow { get; set; } = false;
    public static bool IsExiting { get; set; } = false;
#endif

    #endregion

    #region Tray Icon

#if TRAY_ICON
    public static TrayMenuControl TrayIcon { get; set; } = null!;
#endif

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
                services.AddSingleton<ThemeManager>();
                services.AddSingleton<WallpaperDimService>();
                services.AddSingleton<SolarCalculator>();
                services.AddSingleton<TaskSchedulerManager>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddSingleton<IThemeProvider, ThemeProvider>();
                services.AddSingleton<IPowerProvider, PowerProvider>();

                // Backdrop Management
                services.AddSingleton<IBackdropSelectorService, BackdropSelectorService>();

                // Background efficiency mode
                services.AddSingleton<BackgroundEfficiencyService>();
                services.AddSingleton<IBackgroundEfficiencyService>(sp => sp.GetRequiredService<BackgroundEfficiencyService>());
                services.AddSingleton<IBackgroundDispatchMessageHandler>(sp => sp.GetRequiredService<BackgroundEfficiencyService>());
                services.AddSingleton<IBackgroundDispatchBus, BackgroundDispatchBus>();

                // Dialog Managment
                services.AddSingleton<IDialogService, DialogService>();

                // Bluetooth Audio Playback Connection
                services.AddSingleton<IAudioPlaybackConnectionService, AudioPlaybackConnectionService>();

                // Bluetooth radio state monitor
                services.AddSingleton<BluetoothRadioMonitorService>();
                services.AddSingleton<AudioOutputDeviceWatcherService>();

                // Notification Flyout
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<DeskBoxFloating.Services.DeskBoxFloatingHost>();
                services.AddSingleton<Contracts.Services.IDeskBoxFloatingService>(
                    sp => sp.GetRequiredService<DeskBoxFloating.Services.DeskBoxFloatingHost>());
                services.AddSingleton<ITouchpadControlService, TouchpadControlService>();
                services.AddSingleton<ITouchpadProvider, TouchpadProvider>();

                // Input audio volume control
                services.AddSingleton<InputAudioVolumeService>();
                services.AddSingleton<IInputAudioVolumeService>(sp => sp.GetRequiredService<InputAudioVolumeService>());
                services.AddSingleton<IAudioProvider, AudioProvider>();

                // Audio route and global hotkeys
                services.AddSingleton<IAudioRouteHotkeyService, AudioRouteHotkeyService>();
                services.AddSingleton<IShortcutProvider, ShortcutProvider>();

                // Automation observe-only foundation
                services.AddSingleton<IAutomationSystemContextHub, AutomationSystemContextHub>();
                services.AddSingleton<OfficialAutomationObservationGetter>();
                services.AddSingleton<ListenerAutomationObservationGetter>();
                services.AddSingleton<PollingAutomationObservationGetter>();
                services.AddSingleton<IAutomationObservationGetter>(sp => sp.GetRequiredService<OfficialAutomationObservationGetter>());
                services.AddSingleton<IAutomationObservationGetter>(sp => sp.GetRequiredService<ListenerAutomationObservationGetter>());
                services.AddSingleton<IAutomationObservationGetter>(sp => sp.GetRequiredService<PollingAutomationObservationGetter>());
                services.AddSingleton<AutomationObservationGetterSelector>();
                services.AddSingleton<IAutomationTriggerBus, AutomationTriggerBus>();
                services.AddSingleton<IAutomationTriggerRegistry, AutomationTriggerRegistry>();
                services.AddSingleton<IAutomationConditionRegistry, AutomationConditionRegistry>();
                services.AddSingleton<IAutomationPipelineRepository, AutomationPipelineRepository>();
                services.AddSingleton<IAutomationActionProvider, AutomationNotificationActionProvider>();
                services.AddSingleton<AutomationTouchpadActionProvider>();
                services.AddSingleton<IAutomationActionProvider>(sp => sp.GetRequiredService<AutomationTouchpadActionProvider>());
                services.AddSingleton<IAutomationActionProvider, AutomationEnableTouchpadActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationPowerModeActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationBrightnessActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationInputVolumeActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationThemeActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationProcessActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationGHelperShortcutActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationWindowsShortcutActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationCustomShortcutActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationInputDeviceActionProvider>();
                services.AddSingleton<IAutomationActionProvider, AutomationOutputDeviceActionProvider>();
                services.AddSingleton<IAutomationActionRegistry, AutomationActionRegistry>();
                services.AddSingleton<AutomationTriggerMatcher>();
                services.AddSingleton<AutomationRuleDecisionService>();
                services.AddSingleton<AutomationPipelineExecutor>();
                services.AddSingleton<AutomationPipelineCoordinator>();
                services.AddSingleton<AutomationRuleEngine>(sp => new AutomationRuleEngine(
                    sp.GetRequiredService<IAutomationTriggerBus>(),
                    sp.GetRequiredService<IAutomationPipelineRepository>(),
                    sp.GetRequiredService<AutomationPipelineCoordinator>()));
                services.AddSingleton<AutomationObservationService>();

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

                // WMI brightness fallback for internal laptop panels
                services.AddSingleton<WmiBrightnessService>();

                // LCD backlight fallback for stubborn internal panels
                services.AddSingleton<InternalPanelBrightnessService>();

                // Brightness Controller
                services.AddSingleton<BrightnessController>();
                services.AddSingleton<IBrightnessService>(sp => sp.GetRequiredService<BrightnessController>());
                services.AddSingleton<IDisplayProvider, DisplayProvider>();

                // Software color temperature controller
                services.AddSingleton<ScreenGammaColorTemperatureController>();

                // Windows Night Light state detection
                services.AddSingleton<NightLightStateService>();

                // Auto Brightness Service
                services.AddSingleton<AutoBrightnessService>();

                // Auto Day/Night Service
                services.AddSingleton<AutoDayNightService>();

                // Theme test timer service
                services.AddSingleton<ThemeTestTimerService>();
                services.AddSingleton<AutomationProgramListRefreshService>();

                #endregion

                #region Views & ViewModels

                // Main Window Pages
                services.AddTransient<NavShellPageViewModel>();
                services.AddTransient<NavShellPage>();
                services.AddTransient<AudioNotificationHubPageViewModel>();
                services.AddTransient<AudioNotificationHubPage>();
                services.AddTransient<ThemeBrightnessHubPageViewModel>();
                services.AddTransient<ThemeBrightnessHubPage>();
                services.AddTransient<AutomationHubPageViewModel>();
                services.AddTransient<AutomationHubPage>();
                services.AddTransient<AutomationListPageViewModel>();
                services.AddTransient<AutomationListPage>();
                services.AddTransient<AutomationGameListPageViewModel>();
                services.AddTransient<AutomationGameListPage>();
                services.AddTransient<AutomationEditorPage>();
                services.AddTransient<AutomationBuilderPageViewModel>();
                services.AddTransient<AutomationBuilderPage>();
                services.AddTransient<EfficiencySettingsHubPageViewModel>();
                services.AddTransient<EfficiencySettingsHubPage>();
                services.AddTransient<SettingsPageViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddSingleton<ThemeManagementPageViewModel>();
                services.AddTransient<ThemeManagementPage>();
                services.AddTransient<BluetoothPageViewModel>();
                services.AddTransient<BluetoothPage>();
                services.AddTransient<AutoBrightnessPageViewModel>();
                services.AddTransient<AutoBrightnessPage>();
                services.AddTransient<BackgroundEfficiencyPageViewModel>();
                services.AddTransient<BackgroundEfficiencyPage>();
                services.AddTransient<NotificationPageViewModel>();
                services.AddTransient<NotificationPage>();
                services.AddTransient<HotkeyNotificationPageViewModel>();
                services.AddTransient<HotkeyNotificationPage>();

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
        var brightnessSettings = Ioc.Default.GetRequiredService<BrightnessSettings>();
        _log.Information(
            "Auto brightness startup state. SettingsPath={SettingsPath}, MasterEnabled={MasterEnabled}, ForegroundEnabled={ForegroundEnabled}",
            LocalSettingsHelper.ApplicationDataPath,
            brightnessSettings.AutoEnabled,
            Ioc.Default.GetRequiredService<IAppSettingsService>().AutoBrightnessOnForegroundEnabled);
        TryWriteStartupTrace("App.ctor: before InputAudioVolumeService.ApplyCurrentVolumeAsync");
        _ = Ioc.Default.GetRequiredService<InputAudioVolumeService>().ApplyCurrentVolumeAsync();
        TryWriteStartupTrace("App.ctor: InputAudioVolumeService.ApplyCurrentVolumeAsync queued");
        TryWriteStartupTrace("App.ctor: before AppLanguageHelper.Initialize");
        AppLanguageHelper.Initialize();
        TryWriteStartupTrace("App.ctor: AppLanguageHelper.Initialize done");
        TryWriteStartupTrace("App.ctor: before AppNotificationService.Initialize");
        Ioc.Default.GetRequiredService<IAppNotificationService>().Initialize();
        TryWriteStartupTrace("App.ctor: AppNotificationService.Initialize done");
        TryWriteStartupTrace("App.ctor: settings & notification initialized");

        // Camera should not auto-start on app launch.
        // It will be initialized lazily only when user starts detection or triggers refresh.
        TryWriteStartupTrace("App.ctor: camera init skipped (lazy)");

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
            MainWindow.Show();
            return;
        }

        _ = ActivateAsync();

        async Task ActivateAsync()
        {
            TryWriteStartupTrace("App.OnLaunched: ActivateAsync enter");
            var launchArgs = Environment.GetCommandLineArgs();
            var startupLaunch = launchArgs.Contains(StartupHelper.NonMsixStartupTag, StringComparer.OrdinalIgnoreCase);

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
            InitializeSystemThemeMonitoring();
            TryWriteStartupTrace("App.OnLaunched: system theme monitoring initialized");

            _log.Information($"App launched. Launch args type: {args.GetType().Name}.");

            // Check startup
            _ = StartupHelper.CheckStartup();

            // TODO: Initialize others things

            await Ioc.Default.GetRequiredService<IActivationService>().ActivateMainWindowAsync(args, showWindow: !startupLaunch);
            TryWriteStartupTrace("App.OnLaunched: ActivateMainWindowAsync done");

            TryWriteStartupTrace("App.OnLaunched: before BluetoothRadioMonitorService.InitializeAsync");
            await Ioc.Default.GetRequiredService<BluetoothRadioMonitorService>().InitializeAsync();
            TryWriteStartupTrace("App.OnLaunched: BluetoothRadioMonitorService.InitializeAsync done");
            Ioc.Default.GetRequiredService<AudioOutputDeviceWatcherService>().Start();

            TryWriteStartupTrace("App.OnLaunched: before AutoBrightnessService.InitializeForWindow");
            Ioc.Default.GetRequiredService<AutoBrightnessService>().InitializeForWindow(MainWindow);
            TryWriteStartupTrace("App.OnLaunched: AutoBrightnessService.InitializeForWindow done");

            Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>().InitializeForWindow(MainWindow);
            TryWriteStartupTrace("App.OnLaunched: hotkey service initialized on UI thread");

            Ioc.Default.GetRequiredService<AutomationObservationService>().InitializeForWindow(MainWindow);
            TryWriteStartupTrace("App.OnLaunched: automation observation window initialized on UI thread");

            _ = Task.Run(async () => await RunDeferredStartupWorkAsync());

            if (startupLaunch)
            {
                TryWriteStartupTrace("App.OnLaunched: startup launch kept window hidden");
            }
        }
    }

    private static async Task RunDeferredStartupWorkAsync()
    {
        try
        {
            await Task.Delay(250);

            await ExecuteStartupStepAsync(
                "Deferred startup: BackgroundEfficiencyService.Initialize",
                () =>
                {
                    Ioc.Default.GetRequiredService<IBackgroundEfficiencyService>().Initialize();
                    return Task.CompletedTask;
                });

            await ExecuteStartupStepAsync(
                "Deferred startup: AutoBrightnessService.StartAsync",
                () => Ioc.Default.GetRequiredService<AutoBrightnessService>().StartAsync());

            await ExecuteStartupStepAsync(
                "Deferred startup: automation restore and initialization",
                async () =>
                {
                    var pipelineRepository = Ioc.Default.GetRequiredService<IAutomationPipelineRepository>();
                    await pipelineRepository.InitializationTask;
                    var loadStatus = pipelineRepository.LastLoadStatus;
                    if (!loadStatus.Success)
                    {
                        TryWriteStartupTrace(
                            $"Deferred startup: automation pipelines unavailable, starting with an empty user list. error={loadStatus.ErrorMessage}");
                    }
                    else
                    {
                        TryWriteStartupTrace(
                            $"Deferred startup: automation pipelines restored. stored={loadStatus.StoredCount}, loaded={loadStatus.LoadedCount}, normalized={loadStatus.NormalizedAndSaved}");
                    }

                    await Ioc.Default.GetRequiredService<IAutomationSystemContextHub>().InitializeAsync();
                    var automationRuleEngine = Ioc.Default.GetRequiredService<AutomationRuleEngine>();
                    automationRuleEngine.Initialize();
                    await automationRuleEngine.ExecuteStartupPipelinesAsync();
                    await MainWindow.EnqueueOrInvokeAsync(async (_) =>
                        await Ioc.Default.GetRequiredService<AutomationObservationService>().StartAsync());
                    Ioc.Default.GetRequiredService<AutomationProgramListRefreshService>().Start();
                });

            await ExecuteStartupStepAsync(
                "Deferred startup: AutoDayNightService.StartAsync",
                () => Ioc.Default.GetRequiredService<AutoDayNightService>().StartAsync());

            try
            {
                var wallpaperVm = Ioc.Default.GetRequiredService<ThemeManagementPageViewModel>();
                await wallpaperVm.SyncWallpaperProcessingToThemeAsync(Ioc.Default.GetRequiredService<IThemeSelectorService>().GetActualTheme());
                TryWriteStartupTrace("Deferred startup: wallpaper sync after theme check done");
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to synchronize wallpaper dim state on startup");
            }

            try
            {
                Ioc.Default.GetRequiredService<AutoBrightnessService>().UpdateNightLightState();
                TryWriteStartupTrace("Deferred startup: Night Light state initialized");
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to initialize Night Light state");
            }

        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Deferred startup work failed");
        }
    }

    private static async Task ExecuteStartupStepAsync(string stepName, Func<Task> action)
    {
        TryWriteStartupTrace($"{stepName} - start");
        try
        {
            await action();
            TryWriteStartupTrace($"{stepName} - done");
        }
        catch (Exception ex)
        {
            TryWriteStartupTrace($"{stepName} - failed: {ex.GetType().Name}: {ex.Message}");
            throw;
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

        if (MainWindow is null)
        {
            if (AppDispatcherQueue is null)
            {
                _log.Warning("AppDispatcherQueue is unavailable during activation; creating MainWindow on the current thread.");
                MainWindow = new MainWindow();
                InitializeSystemThemeMonitoring();
                await Ioc.Default.GetRequiredService<IActivationService>().ActivateMainWindowAsync(activatedEventArgs);
                return;
            }

            var activationCompleted = new TaskCompletionSource();
            AppDispatcherQueue.TryEnqueue(() =>
            {
                _ = ActivateMainWindowAsyncOnUIThread(activatedEventArgs, activationCompleted);
            });

            await activationCompleted.Task;
            return;
        }

        await MainWindow.EnqueueOrInvokeAsync(async (_) => await Ioc.Default.GetRequiredService<IActivationService>().ActivateMainWindowAsync(activatedEventArgs));
    }
#endif

    private static async Task ActivateMainWindowAsyncOnUIThread(AppActivationArguments activatedEventArgs, TaskCompletionSource activationCompleted)
    {
        try
        {
            MainWindow ??= new MainWindow();
            InitializeSystemThemeMonitoring();
            await Ioc.Default.GetRequiredService<IActivationService>().ActivateMainWindowAsync(activatedEventArgs);
            activationCompleted.TrySetResult();
        }
        catch (Exception ex)
        {
            activationCompleted.TrySetException(ex);
        }
    }

    private static void InitializeSystemThemeMonitoring()
    {
        if (MainWindow is null)
        {
            return;
        }

        Ioc.Default.GetRequiredService<IThemeSelectorService>()
            .InitializeSystemThemeMonitoring(MainWindow.DispatcherQueue);
    }

    public static async Task ExitAsync()
    {
        if (IsExiting)
        {
            return;
        }

        IsExiting = true;
        var wallpaperRestoreOnExit = Ioc.Default.GetRequiredService<IAppSettingsService>().WallpaperDimRestoreOnExit;
        _log.Information("ExitAsync started. WallpaperDimRestoreOnExit={WallpaperRestoreOnExit}", wallpaperRestoreOnExit);

        // Unregister app notification service
        Ioc.Default.GetRequiredService<IAppNotificationService>().Unregister();

        // Restore gamma ramp before disposing
        try
        {
            Ioc.Default.GetRequiredService<ScreenGammaColorTemperatureController>().Restore();
        }
        catch
        {
            // ignore
        }

        // Restore Night Light before exiting
        try
        {
            Ioc.Default.GetRequiredService<NightLightStateService>().RestoreNightLight();
        }
        catch
        {
            // ignore
        }

        // Stop auto brightness service
        try
        {
            await Ioc.Default.GetRequiredService<AutoBrightnessService>().StopAsync();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (wallpaperRestoreOnExit)
            {
                _log.Information("ExitAsync restoring wallpaper before shutdown.");
                await Ioc.Default.GetRequiredService<WallpaperDimService>().RestoreAsync();
                _log.Information("ExitAsync wallpaper restore completed.");
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            await Ioc.Default.GetRequiredService<AutoDayNightService>().StopAsync();
        }
        catch
        {
            // ignore
        }

        // Dispose all IDisposable services
        DisposeService<IAudioRouteHotkeyService>();
        DisposeService<AutomationObservationService>();
        DisposeService<AutomationRuleEngine>();
        DisposeService<AutoBrightnessService>();
        DisposeService<AutoDayNightService>();
        DisposeService<IThemeSelectorService>();
        DisposeService<ScreenGammaColorTemperatureController>();
        DisposeService<InputAudioVolumeService>();
        DisposeService<BluetoothRadioMonitorService>();
        DisposeService<AudioOutputDeviceWatcherService>();
        DisposeService<MonitorManager>();
        DisposeService<IBackgroundEfficiencyService>();

        // Close all windows
        _log.Information("ExitAsync closing all windows.");
        await WindowsExtensions.CloseAllWindowsAsync();

        try
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to flush automation execution traces before shutdown.");
        }

        // Flush and close log
        Log.CloseAndFlush();

        Current.Exit();
    }

    private static void DisposeService<T>() where T : class
    {
        try
        {
            var service = Ioc.Default.GetService<T>();
            if (service is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // ignore
        }
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

