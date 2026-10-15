namespace BluetoothAudioReceiver.DeskBoxFloating.Helpers;

using System.Runtime.InteropServices;
using Windows.Graphics;

internal static partial class DeskBoxFloatingWin32Helper
{
    internal static void ConfigureWindow(IntPtr hwnd)
    {
        NotificationWin32Helper.ConfigureDeskBoxWindow(hwnd);
    }

    /// <summary>抬升动画落定后再置顶：动画期间不置顶，浮窗才会从任务栏后面冒出来。</summary>
    internal static void EnsureTopMostNoActivate(IntPtr hwnd) =>
        NotificationWin32Helper.EnsureTopMostNoActivate(hwnd);

    /// <summary>摘掉置顶，让浮窗回到普通层，进出场动画才会被任务栏挡住。</summary>
    internal static void ClearTopMostNoActivate(IntPtr hwnd) =>
        NotificationWin32Helper.ClearTopMostNoActivate(hwnd);

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
