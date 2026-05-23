using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class DeviceLineItem : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Glyph { get; set; } = "\uE7F5";
}

