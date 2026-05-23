using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class HomePageViewModel : ObservableRecipient
{
    [ObservableProperty]
    public partial string AppDisplayName { get; set; } = ConstantHelper.AppDisplayName;

    public HomePageViewModel()
    {

    }
}
