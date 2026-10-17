using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.DependencyInjection;
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using BluetoothAudioReceiver.DeskBoxFloating.Profiles;
using BluetoothAudioReceiver.Views.Windows;

namespace BluetoothAudioReceiver.Services;

internal class NotificationService : INotificationService
{
    private const string HotkeyGlyph = "\uE765";

    private IDeskBoxFloatingService? _floating;
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
            // 托盘左键浮层（快捷中心）开着时不弹通知：通知和它共用同一个浮窗，
            // 弹出来会把面板顶掉，看着像"点一下切换浮窗就关了"。
            if (ShouldSuppressByScene(appSettings) ||
                TrayAudioQuickPanelControl.IsOpen ||
                (appSettings.HideNotificationWhenTrayPopupVisible &&
                 TrayAudioQuickPanelWindow.IsPanelVisible))
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

                IDeskBoxFloatingService? floating = null;

                void OnDismissed(object? sender, EventArgs args)
                {
                    if (floating is not null)
                    {
                        floating.Dismissed -= OnDismissed;
                    }

                    shown.TrySetResult();
                }

                try
                {
                    floating = EnsureFloatingService();
                    if (floating == null)
                    {
                        shown.TrySetResult();
                        return;
                    }

                    // DeskBox 浮窗的 Dismissed 在退场动画结束、窗口真正隐藏之后才触发，
                    // 用它代替旧窗口的 NotificationDismissed 给队列串行化：这一条放完再放下一条。
                    floating.Dismissed += OnDismissed;
                    floating.Show(BuildFloatingRequest(notification));
                }
                catch (Exception ex)
                {
                    if (floating is not null)
                    {
                        floating.Dismissed -= OnDismissed;
                    }

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

    private IDeskBoxFloatingService? EnsureFloatingService()
    {
        if (_floating != null)
        {
            return _floating;
        }

        try
        {
            _floating = Ioc.Default.GetRequiredService<IDeskBoxFloatingService>();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "获取 DeskBox 浮层服务失败。");
            return null;
        }

        return _floating;
    }

    /// <summary>
    /// 把队列里的通知翻译成 DeskBox 浮窗请求。多行明细/合并暂不接入（用户决定），
    /// 只保留单行：图标 + 消息 + 状态；窄档（快捷键/紧凑，宽 400/420）走快捷键档案，其余走标准档案。
    /// </summary>
    private static DeskBoxFloatingRequest BuildFloatingRequest(QueuedNotification notification)
    {
        bool narrowProfile = notification.CompactLayout
            || notification.WindowWidthOverride is > 0 and <= 420;

        return new DeskBoxFloatingRequest
        {
            ProfileId = narrowProfile
                ? DeskBoxFloatingProfiles.NotificationHotkey.Id
                : DeskBoxFloatingProfiles.NotificationDefault.Id,
            IconGlyph = notification.IconGlyph,
            Title = notification.Message,
            Status = notification.Status,
            DurationMilliseconds = notification.DurationMilliseconds ?? 2500,
            WidthOverride = notification.WindowWidthOverride,
            HeightOverride = notification.WindowHeightOverride
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
            var floating = _floating;
            if (floating != null)
            {
                if (App.MainWindow?.DispatcherQueue != null)
                {
                    App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            floating.Dismiss();
                        }
                        catch
                        {
                            // ignore
                        }
                    });
                }
                else
                {
                    floating.Dismiss();
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
