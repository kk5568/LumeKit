using System.Collections.Specialized;
using System.Web;

using Microsoft.Windows.AppNotifications;

namespace BluetoothAudioReceiver.Services;

internal class AppNotificationService(INavigationService navigationService) : IAppNotificationService
{
    private readonly INavigationService _navigationService = navigationService;

    ~AppNotificationService()
    {
        Unregister();
    }

    public void Initialize()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
        }
        catch (Exception ex)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            Serilog.Log.ForContext("SourceContext", nameof(AppNotificationService))
                .Warning(ex, "App notification registration failed");
        }
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
        AppNotificationManager.Default.Unregister();
    }
}
