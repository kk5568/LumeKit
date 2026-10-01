using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace BluetoothAudioReceiver.Activation;

internal class AppNotificationActivationHandler(INavigationService navigationService, IAppNotificationService notificationService) : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService = navigationService;
    private readonly IAppNotificationService _notificationService = notificationService;

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        return AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind == ExtendedActivationKind.AppNotification;
    }

    protected override async Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        var activatedEventArgs = (AppNotificationActivatedEventArgs)AppInstance.GetCurrent().GetActivatedEventArgs().Data;
        var arguments = _notificationService.ParseArguments(activatedEventArgs.Argument);

        App.MainWindow.Show();
        App.MainWindow.BringToFront();

        if (string.Equals(arguments["action"], "ToastClick", StringComparison.OrdinalIgnoreCase))
        {
            await _navigationService.NavigateToAsync(typeof(BluetoothPageViewModel).FullName!);
        }
    }
}
