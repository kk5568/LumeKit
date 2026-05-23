namespace BluetoothAudioReceiver.Activation;

internal interface IActivationHandler
{
    bool CanHandle(object args);

    Task HandleAsync(object args);
}
