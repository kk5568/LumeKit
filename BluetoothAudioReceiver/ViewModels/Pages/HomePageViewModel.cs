using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class HomePageViewModel : ObservableRecipient
{
    [ObservableProperty]
    public partial string AppDisplayName { get; set; } = ConstantHelper.AppDisplayName;

    public BluetoothPageViewModel Bluetooth { get; }

    public HotkeyNotificationPageViewModel HotkeyNotifications { get; }

    public AutomationListPageViewModel Automations { get; }

    public HomePageViewModel(
        BluetoothPageViewModel bluetooth,
        HotkeyNotificationPageViewModel hotkeyNotifications,
        AutomationListPageViewModel automations)
    {
        Bluetooth = bluetooth;
        HotkeyNotifications = hotkeyNotifications;
        Automations = automations;
    }
}
