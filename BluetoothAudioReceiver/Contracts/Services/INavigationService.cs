using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface INavigationService
{
    event NavigatedEventHandler Navigated;

    bool CanGoBack { get; }

    Frame? Frame { get; set; }

    Task<bool> NavigateToAsync(string pageKey, object? parameter = null, bool clearNavigation = false);

    Task<bool> GoBackAsync();

    string? GetCurrentPageKey();
}
