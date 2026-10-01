namespace BluetoothAudioReceiver.Automation;

internal static class AutomationTriggerCatalog
{
public const string AppForegroundChanged = "trigger.app.foreground";
public const string AppStopped = "trigger.app.stopped";
public const string PerformanceModeEntered = "trigger.performance-mode.entered";
public const string PerformanceModeExited = "trigger.performance-mode.exited";
public const string PowerChanged = "trigger.power.changed";
    public const string ThemeChanged = "trigger.theme.changed";
    public const string ClockTick = "trigger.clock.tick";
    public const string BluetoothConnected = "trigger.bluetooth.connected";
    public const string BluetoothDisconnected = "trigger.bluetooth.disconnected";
    public const string OutputDeviceChanged = "trigger.audio.output.changed";
    public const string SunTimesUpdated = "trigger.sun.times.updated";
    public const string SunriseReached = "trigger.sunrise.reached";
    public const string SunsetReached = "trigger.sunset.reached";

    private static readonly IReadOnlyDictionary<string, string> DisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AppForegroundChanged] = "程序启动",
            [AppStopped] = "程序退出",
            [PerformanceModeEntered] = "性能模式",
            [PerformanceModeExited] = "性能模式",
            [PowerChanged] = "电源状态",
            [ThemeChanged] = "主题变化",
            [ClockTick] = "时钟变化",
            [BluetoothConnected] = "蓝牙已连接",
            [BluetoothDisconnected] = "蓝牙已断开",
            [OutputDeviceChanged] = "输出设备变化",
            [SunTimesUpdated] = "日出日落已更新",
            [SunriseReached] = "到达日出",
            [SunsetReached] = "到达日落"
        };

    private static readonly IReadOnlyDictionary<string, string> LegacyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
["ForegroundAppChanged"] = AppForegroundChanged,
["ProcessStopped"] = AppStopped,
["PerformanceModeEntered"] = PerformanceModeEntered,
["PerformanceModeExited"] = PerformanceModeExited,
["PowerStateChanged"] = PowerChanged,
            ["SystemThemeChanged"] = ThemeChanged,
            ["ClockTickObserved"] = ClockTick,
            ["BluetoothDeviceConnected"] = BluetoothConnected,
            ["BluetoothDeviceDisconnected"] = BluetoothDisconnected,
            ["DefaultOutputDeviceChanged"] = OutputDeviceChanged,
            ["SunTimesUpdated"] = SunTimesUpdated,
            ["SunriseReached"] = SunriseReached,
            ["SunsetReached"] = SunsetReached
        };

    public static string Normalize(string? triggerId)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return string.Empty;
        }

        return LegacyAliases.TryGetValue(triggerId, out var normalized)
            ? normalized
            : triggerId.Trim();
    }

    public static string GetDisplayName(string? triggerId)
    {
        var normalized = Normalize(triggerId);
        return DisplayNames.TryGetValue(normalized, out var name)
            ? name
            : normalized;
    }
}
