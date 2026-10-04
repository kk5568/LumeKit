using System.Diagnostics;
using System.Runtime.InteropServices;
using BluetoothAudioReceiver.Automation;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal static class ForegroundWindowProbe
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", "ForegroundWindowProbe");

    public static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            return Process.GetProcessById((int)pid).ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GetForegroundWindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new string('\0', length + 1);
            _ = GetWindowText(hwnd, buffer, buffer.Length);
            return buffer.TrimEnd('\0');
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool IsForegroundFullscreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return false;
            }

            var hMonitor = MonitorFromWindow(hwnd, 2u);
            if (hMonitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MONITORINFO
            {
                cbSize = Marshal.SizeOf<MONITORINFO>()
            };

            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                return false;
            }

            var windowWidth = rect.Right - rect.Left;
            var windowHeight = rect.Bottom - rect.Top;
            var monitorWidth = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left;
            var monitorHeight = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;

            return Math.Abs(windowWidth - monitorWidth) <= 2 &&
                   Math.Abs(windowHeight - monitorHeight) <= 2 &&
                   rect.Left == monitorInfo.rcMonitor.Left &&
                   rect.Top == monitorInfo.rcMonitor.Top;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLikelyGame(string? foregroundProcessName, string? foregroundWindowTitle)
    {
        return AutomationGameRecognitionCatalog.MatchesKnownGame(foregroundProcessName) ||
               AutomationGameRecognitionCatalog.MatchesKnownGame(foregroundWindowTitle);
    }

    public static int? GetForegroundProcessId()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out var processId);
            return processId == 0 ? null : (int)processId;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, string lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

}
