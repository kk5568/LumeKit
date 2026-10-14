using Microsoft.UI.Xaml.Media;

namespace BluetoothAudioReceiver.Helpers;

internal static class DialogVisualHelper
{
    public static SolidColorBrush CreateCriticalBrush()
    {
        return new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 164, 38, 44));
    }
}
