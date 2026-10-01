using Microsoft.UI.Xaml;
using BluetoothAudioReceiver.ViewModels.Pages;

namespace BluetoothAudioReceiver.Activation;

internal class DefaultActivationHandler(INavigationService navigationService) : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService = navigationService;

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame?.Content == null;
    }

    protected override async Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        await _navigationService.NavigateToAsync(typeof(BluetoothPageViewModel).FullName!, args.Arguments);
    }
}
