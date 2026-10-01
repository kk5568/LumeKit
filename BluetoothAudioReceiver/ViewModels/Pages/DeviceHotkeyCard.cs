using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class DeviceHotkeyCard : ObservableObject
{
    public required string DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public required bool IsInputDevice { get; init; }

    public string DeviceTypeText => IsInputDevice ? "输入设备" : "输出设备";

    public string PrimaryActionText => "连接";

    public string SecondaryActionText => "断开";

    [ObservableProperty]
    public partial string Hotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SavedHotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    public string ConnectionStateText => IsConnected ? "已连接" : "未连接";

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionStateText));
    }
}
