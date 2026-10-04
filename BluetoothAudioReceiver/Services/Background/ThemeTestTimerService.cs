using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Serilog;
using System.Runtime.InteropServices;
using System.Threading;

namespace BluetoothAudioReceiver.Services;

public sealed class ThemeTestTimerService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(ThemeTestTimerService));
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";
    private const string SystemUsesLightThemeValueName = "SystemUsesLightTheme";

    private readonly AutoDayNightService _autoDayNightService;
    private readonly object _syncRoot = new();
    private Timer? _dueTimer;
    private Timer? _dailyTimer;
    private TimeSpan? _scheduledTime;
    private ElementTheme _scheduledTheme = ElementTheme.Dark;

    public ThemeTestTimerService(AutoDayNightService autoDayNightService)
    {
        _autoDayNightService = autoDayNightService;
        StatusText = "未设置测试定时器";
    }

    public event EventHandler? StateChanged;

    public bool Enabled { get; private set; }

    public TimeSpan? ScheduledTime => _scheduledTime;

    public bool UseDarkMode => _scheduledTheme == ElementTheme.Dark;

    public string StatusText { get; private set; }

    public void Schedule(TimeSpan targetTime, bool useDarkMode)
    {
        var dueTime = GetDelayUntilNextRun(targetTime);

        _ = _autoDayNightService.StopAsync();

        lock (_syncRoot)
        {
            CancelTimersNoLock();
            _scheduledTime = targetTime;
            _scheduledTheme = useDarkMode ? ElementTheme.Dark : ElementTheme.Light;
            _dueTimer = new Timer(OnDueTimerTick, null, dueTime, Timeout.InfiniteTimeSpan);
            Enabled = true;
            StatusText = $"测试已设置：{targetTime:hh\\:mm} 切到{(useDarkMode ? "深色" : "浅色")}，剩余 {FormatDuration(dueTime)}";
        }

        RaiseStateChanged();
    }

    public void ScheduleOppositeThemeAfterThirtySeconds()
    {
        var currentTheme = GetCurrentTheme();
        var targetTheme = currentTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;

        lock (_syncRoot)
        {
            CancelTimersNoLock();
            _scheduledTime = DateTime.Now.AddSeconds(30).TimeOfDay;
            _scheduledTheme = targetTheme;
            _dueTimer = new Timer(OnOneShotDueTimerTick, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
            Enabled = true;
            StatusText = $"30秒测试已启动：{DateTime.Now.AddSeconds(30):HH:mm:ss} 切到{(targetTheme == ElementTheme.Dark ? "深色" : "浅色")}";
        }

        RaiseStateChanged();
    }

    public void Cancel()
    {
        lock (_syncRoot)
        {
            CancelTimersNoLock();
            _scheduledTime = null;
            Enabled = false;
            StatusText = "测试定时器已取消";
        }
        RaiseStateChanged();
    }

    private void OnDueTimerTick(object? state)
    {
        ElementTheme theme;
        TimeSpan? scheduledTime;

        lock (_syncRoot)
        {
            theme = _scheduledTheme;
            scheduledTime = _scheduledTime;
            _dailyTimer?.Dispose();
            _dailyTimer = new Timer(OnDailyTimerTick, null, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        }

        _ = ExecuteThemeSwitchAsync(theme, scheduledTime);
    }

    private void OnOneShotDueTimerTick(object? state)
    {
        ElementTheme theme;

        lock (_syncRoot)
        {
            theme = _scheduledTheme;
            CancelTimersNoLock();
            _scheduledTime = null;
        }

        _ = ExecuteOneShotThemeSwitchAsync(theme);
    }

    private void OnDailyTimerTick(object? state)
    {
        ElementTheme theme;
        TimeSpan? scheduledTime;

        lock (_syncRoot)
        {
            theme = _scheduledTheme;
            scheduledTime = _scheduledTime;
        }

        _ = ExecuteThemeSwitchAsync(theme, scheduledTime);
    }

    private async Task ExecuteThemeSwitchAsync(ElementTheme theme, TimeSpan? scheduledTime)
    {
        try
        {
            ApplySystemTheme(theme);
            await BroadcastThemeChangeAsync();

            lock (_syncRoot)
            {
                Enabled = true;
                StatusText = scheduledTime is not null
                    ? $"测试已触发：{DateTime.Now:HH:mm:ss} 切到{(theme == ElementTheme.Dark ? "深色" : "浅色")}，明天 {scheduledTime.Value:hh\\:mm} 再次执行"
                    : $"测试已触发：{DateTime.Now:HH:mm:ss} 切到{(theme == ElementTheme.Dark ? "深色" : "浅色")}";
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Theme test timer failed");

            lock (_syncRoot)
            {
                StatusText = $"测试失败：{ex.Message}";
            }
        }

        _ = _autoDayNightService.StartAsync();
        RaiseStateChanged();
    }

    private async Task ExecuteOneShotThemeSwitchAsync(ElementTheme theme)
    {
        try
        {
            ApplySystemTheme(theme);
            await BroadcastThemeChangeAsync();

            lock (_syncRoot)
            {
                Enabled = false;
                StatusText = $"30秒测试已执行：{DateTime.Now:HH:mm:ss} 切到{(theme == ElementTheme.Dark ? "深色" : "浅色")}，测试定时器已自动删除";
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "One-shot theme test timer failed");

            lock (_syncRoot)
            {
                Enabled = false;
                StatusText = $"30秒测试失败：{ex.Message}";
            }
        }

        _ = _autoDayNightService.StartAsync();
        RaiseStateChanged();
    }

    private static void ApplySystemTheme(ElementTheme theme)
    {
        var value = theme == ElementTheme.Dark ? 0 : 1;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath, writable: true);
            if (key is null)
            {
                return;
            }

            key.SetValue(AppsUseLightThemeValueName, value, RegistryValueKind.DWord);
            key.SetValue(SystemUsesLightThemeValueName, value, RegistryValueKind.DWord);
        }
        catch
        {
        }
    }

    private static ElementTheme GetCurrentTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath, writable: false);
            var appValue = key?.GetValue(AppsUseLightThemeValueName);
            var systemValue = key?.GetValue(SystemUsesLightThemeValueName);
            var rawValue = appValue ?? systemValue;

            return Convert.ToInt32(rawValue) == 0 ? ElementTheme.Dark : ElementTheme.Light;
        }
        catch
        {
            return ElementTheme.Light;
        }
    }

    private static async Task BroadcastThemeChangeAsync()
    {
        BroadcastThemeChangeOnce();
        await Task.Delay(155);
        BroadcastThemeChangeOnce();
    }

    private static void BroadcastThemeChangeOnce()
    {
        const int hwndBroadcast = -1;
        const uint wmSettingChange = 0x001A;
        const uint smtoAbortIfHung = 0x0002;
        SendMessageTimeout(new IntPtr(hwndBroadcast), wmSettingChange, IntPtr.Zero, "ImmersiveColorSet", smtoAbortIfHung, 1000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    private void CancelTimersNoLock()
    {
        _dueTimer?.Dispose();
        _dailyTimer?.Dispose();
        _dueTimer = null;
        _dailyTimer = null;
    }

    private static TimeSpan GetDelayUntilNextRun(TimeSpan targetTime)
    {
        var now = DateTime.Now;
        var target = now.Date.Add(targetTime);
        if (target <= now)
        {
            target = target.AddDays(1);
        }

        return target - now;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}小时{duration.Minutes}分钟";
        }

        return $"{Math.Max(0, duration.Minutes)}分钟{Math.Max(0, duration.Seconds)}秒";
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            CancelTimersNoLock();
        }
    }
}
