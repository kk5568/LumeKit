using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.DependencyInjection;
using BluetoothAudioReceiver.Views.Windows;

namespace BluetoothAudioReceiver.Services;

internal class NotificationService : INotificationService
{
    private NotificationWindow? _window;
    private readonly object _lock = new();
    private static readonly Serilog.ILogger _log = Serilog.Log.ForContext("SourceContext", nameof(NotificationService));

    public void Show(string iconGlyph, string message, string status = "")
    {
        var appSettings = Ioc.Default.GetRequiredService<IAppSettingsService>();
        if (!appSettings.NotificationEnabled ||
            ShouldSuppressByScene(appSettings) ||
            (appSettings.HideNotificationWhenTrayPopupVisible && TrayQuickPanelWindow.IsPanelVisible))
        {
            return;
        }

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                lock (_lock)
                {
                    if (_window == null)
                    {
                        _window = new NotificationWindow();
                        _window.Closed += (_, _) =>
                        {
                            lock (_lock) { _window = null; }
                        };
                    }
                }

                _window?.ShowNotification(iconGlyph, message, status);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "显示 OSD 通知失败");
            }
        });
    }

    private static bool ShouldSuppressByScene(IAppSettingsService settings)
    {
        var processName = GetForegroundProcessName().ToLowerInvariant();
        var isFullscreen = IsForegroundFullscreen();

        if (settings.DisableNotificationInFullscreen && isFullscreen)
        {
            return true;
        }

        if (settings.DisableNotificationInGame && ContainsAny(processName, "steam", "epicgameslauncher", "riotclient", "valorant", "cs2", "dota2", "league", "genshin", "starrail", "overwatch", "pubg", "apex"))
        {
            return true;
        }

        if (settings.DisableNotificationInVideo && ContainsAny(processName, "vlc", "potplayer", "mpv", "wmplayer", "movies", "tv", "bilibili", "youku", "iqiyi", "qqlive", "腾讯视频"))
        {
            return true;
        }

        if (settings.DisableNotificationInRecordingOrLive && ContainsAny(processName, "obs64", "obs", "streamlabs", "xsplit", "douyin", "huya", "douyu", "bilibili"))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string source, params string[] keywords)
    {
        return keywords.Any(k => source.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            var process = Process.GetProcessById((int)pid);
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsForegroundFullscreen()
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

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
