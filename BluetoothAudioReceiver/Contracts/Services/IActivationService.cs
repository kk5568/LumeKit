using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IActivationService
{
    Task ActivateMainWindowAsync(object activationArgs, bool showWindow = true);

    Task ActivateWindowAsync(Window window);
}
