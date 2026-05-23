using Microsoft.Extensions.Configuration;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Com;

namespace BluetoothAudioReceiver;

#if DISABLE_XAML_GENERATED_MAIN

/// <summary>
/// Represents the base entry point of the app.
/// </summary>
/// <remarks>
/// Gets called at the first time when the app launched or activated.
/// </remarks>
public class Program
{
#if DEBUG
    private const string AppInstanceKey = $"{Constants.BluetoothAudioReceiver}-DEBUG";
#else
    private const string AppInstanceKey = $"{Constants.BluetoothAudioReceiver}-RELEASE";
#endif

    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(Program));
    private static readonly string StartupCrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAudioReceiver",
        "startup-crash.log");
    private static readonly string StartupTraceLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAudioReceiver",
        "startup-trace.log");

    /// <summary>
    /// Initializes the process; the entry point of the process.
    /// </summary>
    /// <remarks>
    /// <see cref="Main"/> cannot be declared to be async because this prevents Narrator from reading XAML elements in a WinUI app.
    /// </remarks>
    [STAThread]
    private static void Main(string[] args)
    {
        TryWriteStartupTrace("Main: enter");
        try
        {
            // Initialize core helpers
            TryWriteStartupTrace("Main: before LocalSettingsHelper.Initialize");
            LocalSettingsHelper.Initialize();
            TryWriteStartupTrace($"Main: LocalSettingsHelper.Initialize done, appData={LocalSettingsHelper.ApplicationDataPath}");

            // Set up Logging
            TryWriteStartupTrace("Main: before logger init");
            Environment.SetEnvironmentVariable("LOGGING_ROOT", Path.Combine(LocalSettingsHelper.LogDirectory, InfoHelper.GetVersion().ToString()));
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            TryWriteStartupTrace("Main: logger init done");

            _log.Information($"Program launched with args: {string.Join(' ', [.. args])}");

            TryWriteStartupTrace("Main: before InitializeComWrappers");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            TryWriteStartupTrace("Main: InitializeComWrappers done");

            AppInstance? instance = null;
            try
            {
                TryWriteStartupTrace($"Main: before FindOrRegisterForKey ({AppInstanceKey})");
                instance = AppInstance.FindOrRegisterForKey(AppInstanceKey);
                TryWriteStartupTrace($"Main: FindOrRegisterForKey done, isCurrent={instance.IsCurrent}");
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80040154)
            {
                // Unpackaged runtime fallback: AppLifecycle COM server may be unavailable.
                TryWriteStartupTrace("Main: AppInstance unavailable (class not registered), fallback to normal startup");
                _log.Warning(ex, "AppInstance is unavailable, skip single-instance features.");
            }

            if (instance?.IsCurrent == true)
            {
                instance.Activated += OnActivated;
                TryWriteStartupTrace("Main: attached AppInstance.Activated");
            }
#if SINGLE_INSTANCE
            else if (instance != null)
            {
                TryWriteStartupTrace("Main: redirect activation to existing instance");
                // Redirect activation to the existing instance
                RedirectActivationTo(instance, AppInstance.GetCurrent().GetActivatedEventArgs());

                // Kill the current process
                Environment.Exit(0);

                return;
            }
#endif

            try
            {
                TryWriteStartupTrace("Main: before Application.Start");
                Application.Start((p) =>
                {
                    TryWriteStartupTrace("Main: Application.Start callback enter");
                    try
                    {
                        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                        var context = new DispatcherQueueSynchronizationContext(dispatcherQueue);
                        SynchronizationContext.SetSynchronizationContext(context);
                        _ = new App();
                        TryWriteStartupTrace("Main: App constructor returned");
                    }
                    catch (Exception ex)
                    {
                        TryWriteStartupCrash("Application.Start callback", ex);
                        throw;
                    }
                });
                TryWriteStartupTrace("Main: Application.Start returned");
            }
            catch (Exception ex)
            {
                TryWriteStartupCrash("Application.Start", ex);
                throw;
            }

            _log.Information("Program terminated");
            Log.CloseAndFlush();
            TryWriteStartupTrace("Main: exit");
        }
        catch (Exception ex)
        {
            TryWriteStartupCrash("Main top-level", ex);
            throw;
        }
    }

    private static void TryWriteStartupCrash(string stage, Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(StartupCrashLogPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var content = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Stage: {stage}")
                .AppendLine(ex.ToString())
                .AppendLine(new string('-', 80))
                .ToString();
            File.AppendAllText(StartupCrashLogPath, content, Encoding.UTF8);
        }
        catch
        {
            // ignore
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

    /// <summary>
    /// Gets invoked when the application is activated.
    /// </summary>
    private static async void OnActivated(object? sender, AppActivationArguments args)
    {
        // WINUI3: Verify if needed or OnLaunched is called
        if (Application.Current is App currentApp)
        {
            await currentApp.OnActivatedAsync(args);
        }
    }

#if SINGLE_INSTANCE
    /// <summary>
    /// Redirects the activation to the main process.
    /// </summary>
    /// <remarks>
    /// Redirects on another thread and uses a non-blocking wait method to wait for the redirection to complete.
    /// </remarks>
    public static void RedirectActivationTo(AppInstance keyInstance, AppActivationArguments args)
    {
        // Create an event for activation synchronization
        var eventHandle = PInvoke.CreateEvent((SECURITY_ATTRIBUTES?)null, true, false, null!);

        // Redirect activation asynchronously
        Task.Run(() =>
        {
            keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
            PInvoke.SetEvent(eventHandle);
        });

        // Wait for the activation redirection to complete
        _ = PInvoke.CoWaitForMultipleObjects(
            (uint)CWMO_FLAGS.CWMO_DEFAULT,
            PInvoke.INFINITE,
            [new(eventHandle.DangerousGetHandle())],
            out var handleIndex);
    }
#endif

}

#endif
