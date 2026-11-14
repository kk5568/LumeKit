using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.DeskBoxFloating.Views;

public sealed partial class DeskBoxIslandStatusContent : UserControl
{
    public DeskBoxIslandStatus Status { get; }

    public DeskBoxIslandStatusContent(DeskBoxIslandStatus status)
    {
        Status = status;
        InitializeComponent();
    }
}
