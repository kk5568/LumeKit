using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.DependencyInjection;
using BluetoothAudioReceiver.Views.Windows;

namespace BluetoothAudioReceiver.Services;

internal class NotificationService : INotificationService
{
    private const string HotkeyGlyph = "\uE765";

    private NotificationWindow? _window;
    private readonly object _lock = new();
    private static readonly Serilog.ILogger _log = Serilog.Log.ForContext("SourceContext", nameof(NotificationService));
    private readonly Queue<QueuedNotification> _queue = new();
    private bool _isShowing;
    private bool _disposed;

    public void Show(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ShowCore(
            request.IconGlyph ?? DeviceVisualHelper.SpeakerGlyph,
            request.Message,
            request.Status,
            request.CompactLayout,
            request.LayoutProfile,
            request.WindowWidthOverride,
            request.WindowHeightOverride,
            request.DetailLines,
            request.AutomationLayout,
            request.BypassSuppression,
            request.CorrelationId);
    }

    public void ShowPreview(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ShowCore(
            request.IconGlyph ?? DeviceVisualHelper.SpeakerGlyph,
            request.Message,
            request.Status,
            request.CompactLayout,
            request.LayoutProfile,
            request.WindowWidthOverride,
            request.WindowHeightOverride,
            request.DetailLines,
            request.AutomationLayout,
            bypassSuppression: true,
            correlationId: request.CorrelationId);
    }

    public void Show(string iconGlyph, string message, string status = "", bool compactLayout = false)
        => ShowCore(iconGlyph, message, status, compactLayout);

    public void ShowAutomation(string automationName, string statusText, IReadOnlyList<string> detailLines, Guid? correlationId = null)
        => ShowCore(
            "\uE9A4",
            NormalizeAutomationNotificationName(automationName),
            statusText,
            compactLayout: false,
            layoutProfile: NotificationLayoutProfiles.Default,
            windowWidthOverride: null,
            windowHeightOverride: null,
            detailLines: [],
            automationLayout: false,
            correlationId: correlationId);

    private static string NormalizeAutomationNotificationName(string automationName)
    {
        var name = automationName.EndsWith("-恢复", StringComparison.Ordinal)
            ? automationName[..^"-恢复".Length]
            : automationName;

        foreach (var actionText in new[] { " · 启动", " · 退出", "· 启动", "· 退出" })
        {
            if (name.EndsWith(actionText, StringComparison.Ordinal))
            {
                return name[..^actionText.Length].Trim();
            }
        }

        return name;
    }

    private void ShowCore(
        string iconGlyph,
        string message,
        string status = "",
        bool compactLayout = false,
        NotificationLayoutProfile? layoutProfile = null,
        int? windowWidthOverride = null,
        int? windowHeightOverride = null,
        IReadOnlyList<string>? detailLines = null,
        bool automationLayout = false,
        bool bypassSuppression = false,
        Guid? correlationId = null,
        int? durationMilliseconds = null,
        bool deskBoxLayout = false)
    {
        if (_disposed)
        {
            return;
        }

        if (!bypassSuppression)
        {
            var appSettings = Ioc.Default.GetRequiredService<IAppSettingsService>();
            if (ShouldSuppressByScene(appSettings) ||
                (appSettings.HideNotificationWhenTrayPopupVisible && TrayQuickPanelWindow.IsPanelVisible))
            {
                return;
            }
        }

        var queued = new QueuedNotification(
            string.IsNullOrWhiteSpace(iconGlyph) ? DeviceVisualHelper.SpeakerGlyph : iconGlyph,
            message ?? string.Empty,
            status ?? string.Empty,
            compactLayout,
            windowWidthOverride ?? layoutProfile?.Width,
            windowHeightOverride ?? layoutProfile?.Height,
            detailLines?.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray() ?? [],
            automationLayout,
            DateTimeOffset.Now,
            correlationId,
            durationMilliseconds,
            deskBoxLayout);

        _log.Debug(
            "OSD notification queued. Message={Message}, CorrelationId={CorrelationId}",
            queued.Message,
            queued.CorrelationId);

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _queue.Enqueue(queued);
            if (!_isShowing)
            {
                _isShowing = true;
                _ = PumpQueueAsync();
            }
        }
    }

    private async Task PumpQueueAsync()
    {
        while (true)
        {
            QueuedNotification notification;
            lock (_lock)
            {
                if (_disposed)
                {
                    _isShowing = false;
                    return;
                }

                if (_queue.Count == 0)
                {
                    _isShowing = false;
                    return;
                }

                _isShowing = true;
                notification = _queue.Dequeue();
            }

            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (_disposed)
                {
                    shown.TrySetResult();
                    return;
                }

                try
                {
                    EnsureWindow();
                    if (_window == null)
                    {
                        shown.TrySetResult();
                        return;
                    }

                    void OnDismissed(object? sender, EventArgs args)
                    {
                        if (_window is not null)
                        {
                            _window.NotificationDismissed -= OnDismissed;
                        }

                        shown.TrySetResult();
                    }

                    _window.NotificationDismissed += OnDismissed;
                    var displayNotification = BuildDisplayNotification(notification);
                    _window.ShowNotification(
                        displayNotification.IconGlyph,
                        displayNotification.Message,
                        displayNotification.Status,
                        displayNotification.CompactLayout,
                        displayNotification.WindowWidthOverride,
                        displayNotification.WindowHeightOverride,
                        displayNotification.DetailLines,
                        displayNotification.AutomationLayout,
                        displayNotification.DurationMilliseconds,
                        displayNotification.DeskBoxLayout);
                }
                catch (Exception ex)
                {
                    _log.Warning(
                        ex,
                        "显示 OSD 通知失败。CorrelationId={CorrelationId}",
                        notification.CorrelationId);
                    shown.TrySetResult();
                }
            });

            await shown.Task;

        }
    }

    private static QueuedNotification BuildDisplayNotification(QueuedNotification notification)
    {
        if (notification.DetailLines.Count == 0)
        {
            return notification;
        }

        return notification with
        {
            WindowWidthOverride = Math.Max(
                notification.WindowWidthOverride ?? NotificationLayoutProfiles.Default.Width,
                NotificationLayoutProfiles.Merged.Width),
            WindowHeightOverride = GetMergedWindowHeight(notification.DetailLines.Count),
            CompactLayout = false,
            AutomationLayout = false
        };
    }

    private static int GetMergedWindowHeight(int detailLineCount)
    {
        return detailLineCount switch
        {
            <= 0 => NotificationLayoutProfiles.Default.Height,
            1 => 136,
            2 => 158,
            _ => NotificationLayoutProfiles.Merged.Height
        };
    }

    private void EnsureWindow()
    {
        if (_window != null)
        {
            return;
        }

        _window = new NotificationWindow();
        _window.Closed += (_, _) =>
        {
            lock (_lock)
            {
                _window = null;
            }
        };
    }

    public void ShowHotkey(string hotkeyText, string status)
    {
        ShowCore(HotkeyGlyph, hotkeyText, status, compactLayout: false, layoutProfile: NotificationLayoutProfiles.Hotkey);
    }

    public void ShowHotkeyPreview(string hotkeyText, string status)
    {
        ShowCore(
            HotkeyGlyph,
            hotkeyText,
            status,
            compactLayout: false,
            layoutProfile: NotificationLayoutProfiles.Hotkey,
            bypassSuppression: true);
    }

    public void ShowDeskBoxPreview()
    {
        ShowCore(
            HotkeyGlyph,
            "测试",
            "已触发",
            compactLayout: false,
            layoutProfile: NotificationLayoutProfiles.Hotkey,
            bypassSuppression: true,
            deskBoxLayout: true);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_lock)
        {
            _queue.Clear();
            _isShowing = false;
        }

        try
        {
            if (_window != null)
            {
                if (App.MainWindow?.DispatcherQueue != null)
                {
                    App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            _window?.Close();
                        }
                        catch
                        {
                            // ignore
                        }
                    });
                }
                else
                {
                    _window.Close();
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static bool ContainsAny(string source, params string[] keywords)
    {
        return keywords.Any(k => source.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record QueuedNotification(
        string IconGlyph,
        string Message,
        string Status,
        bool CompactLayout,
        int? WindowWidthOverride,
        int? WindowHeightOverride,
        IReadOnlyList<string> DetailLines,
        bool AutomationLayout,
        DateTimeOffset CreatedAt,
        Guid? CorrelationId,
        int? DurationMilliseconds,
        bool DeskBoxLayout);

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
