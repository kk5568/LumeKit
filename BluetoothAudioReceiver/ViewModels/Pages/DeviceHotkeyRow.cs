using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public sealed class DeviceHotkeyRow
{
    public required DeviceHotkeyCard LeftCard { get; init; }

    public DeviceHotkeyCard? MiddleCard { get; init; }

    public DeviceHotkeyCard? RightCard { get; init; }

    public Visibility MiddleCardVisibility => MiddleCard == null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility RightCardVisibility => RightCard == null ? Visibility.Collapsed : Visibility.Visible;
}
