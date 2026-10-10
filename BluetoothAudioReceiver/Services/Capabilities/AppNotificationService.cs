using System.Collections.Specialized;
using System.Web;

using Microsoft.Windows.AppNotifications;

namespace BluetoothAudioReceiver.Services;

internal class AppNotificationService(INavigationService navigationService) : IAppNotificationService
{
    private readonly INavigationService _navigationService = navigationService;
    private AppNotificationManager? _manager;
    private bool _handlerAttached;
    private bool _registered;

    ~AppNotificationService()
    {
        Unregister();
    }

    public void Initialize()
    {
        AppNotificationManager? manager = null;
        try
        {
            manager = AppNotificationManager.Default;
            _manager = manager;
            manager.NotificationInvoked += OnNotificationInvoked;
            _handlerAttached = true;
            manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            // Never touch AppNotificationManager.Default in this catch: accessing that property is
            // exactly what failed (for example 0x80070005 when the notification platform denies
            // access), and doing it again here would replace the original error with a new one that
            // escapes Initialize() and takes the whole app down.
            DetachHandler(manager);
            _manager = null;
            _registered = false;
            Serilog.Log.ForContext("SourceContext", nameof(AppNotificationService))
                .Warning(ex, "App notification registration failed; the app continues without notifications");
        }
    }

    private void DetachHandler(AppNotificationManager? manager)
    {
        if (!_handlerAttached || manager is null)
        {
            return;
        }

        try
        {
            manager.NotificationInvoked -= OnNotificationInvoked;
        }
        catch
        {
            // The notification platform may already be unavailable; nothing left to do here.
        }

        _handlerAttached = false;
    }

    // Handle notification invocations when your app is already running based on the notification arguments.
    public async void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            var arguments = ParseArguments(args.Argument);
            if (!string.Equals(arguments["action"], "ToastClick", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await App.MainWindow.EnqueueOrInvokeAsync(async window =>
            {
                window.Show();
                window.BringToFront();
                await _navigationService.NavigateToAsync(typeof(BluetoothPageViewModel).FullName!);
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.ForContext("SourceContext", nameof(AppNotificationService))
                .Warning(ex, "Notification activation handling failed");
        }
    }

    public bool Show(string payload)
    {
        var appNotification = new AppNotification(payload);

        AppNotificationManager.Default.Show(appNotification);

        return appNotification.Id != 0;
    }

    public void TryShow(string payload)
    {
        try
        {
            Show(payload);
        }
        catch (Exception ex)
        {
            Serilog.Log.ForContext("SourceContext", nameof(AppNotificationService))
                .Warning(ex, "Failed to show app notification");
        }
    }

    public void RunShow(string payload)
    {
        Task.Run(() => TryShow(payload)).Forget(nameof(AppNotificationService));
    }

    public void ShowNotification(string title, string body)
    {
        var payload = $@"<toast>
  <visual>
    <binding template=""ToastGeneric"">
      <text>{title}</text>
      <text>{body}</text>
    </binding>
  </visual>
</toast>";
        RunShow(payload);
    }

    public NameValueCollection ParseArguments(string arguments)
    {
        return HttpUtility.ParseQueryString(arguments);
    }

    public void Unregister()
    {
        // Also invoked from the finalizer, so it must never throw.
        var manager = _manager;
        _manager = null;
        if (manager is null)
        {
            return;
        }

        try
        {
            DetachHandler(manager);
            if (_registered)
            {
                manager.Unregister();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.ForContext("SourceContext", nameof(AppNotificationService))
                .Warning(ex, "App notification unregister failed");
        }
        finally
        {
            _registered = false;
        }
    }
}
