using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.Services;

internal static class BackgroundEfficiencyWindowHook
{
    private const int EventSystemForeground = 3;
    private const int WineventOutOfContext = 0;

    private static IntPtr _windowEventHook;
    private static readonly WinEventProc HookProcDelegate = WindowEventCallback;

    public static event EventHandler<IntPtr>? SystemForegroundWindowChanged;

    public static void SubscribeToWindowEvents()
    {
        if (_windowEventHook != IntPtr.Zero)
        {
            return;
        }

        _windowEventHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            HookProcDelegate,
            0,
            0,
            WineventOutOfContext);

        if (_windowEventHook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public static void UnsubscribeWindowEvents()
    {
        if (_windowEventHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWinEvent(_windowEventHook);
        _windowEventHook = IntPtr.Zero;
    }

    private static void WindowEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        SystemForegroundWindowChanged?.Invoke(null, hwnd);
    }

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(int eventMin, int eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, int idProcess, int idThread, int dwflags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int UnhookWinEvent(IntPtr hWinEventHook);
}
