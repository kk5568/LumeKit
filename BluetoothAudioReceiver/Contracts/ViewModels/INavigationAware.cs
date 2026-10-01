namespace BluetoothAudioReceiver.Contracts.ViewModels;

internal interface INavigationAware
{
    Task OnNavigatedToAsync(object parameter);

    Task OnNavigatedFromAsync();
}
