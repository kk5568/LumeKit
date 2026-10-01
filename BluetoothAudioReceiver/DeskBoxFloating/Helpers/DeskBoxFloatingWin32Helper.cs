namespace BluetoothAudioReceiver.DeskBoxFloating.Helpers;

using System.Runtime.InteropServices;
using Windows.Graphics;

internal static partial class DeskBoxFloatingWin32Helper
{
    internal static void ConfigureWindow(IntPtr hwnd)
    {
        NotificationWin32Helper.ConfigureDeskBoxWindow(hwnd);
    }

    internal static bool TryGetCursorPosition(out PointInt32 point)
    {
        if (GetCursorPos(out var nativePoint))
        {
            point = new PointInt32(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
