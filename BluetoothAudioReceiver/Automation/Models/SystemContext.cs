namespace BluetoothAudioReceiver.Automation.Models;

public sealed record SystemContext
{
    public string MachineName { get; init; } = Environment.MachineName;
    public string? OperatingSystem { get; init; }
    public string? OperatingSystemArchitecture { get; init; }
    public string? ProcessArchitecture { get; init; }
    public int? ProcessorCount { get; init; }
    public bool? Is64BitOperatingSystem { get; init; }
    public bool? Is64BitProcess { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string? ForegroundProcessName { get; init; }
    public string? ForegroundWindowTitle { get; init; }
    public bool? IsOnBattery { get; init; }
    public bool? IsCharging { get; init; }
    public int? BatteryPercent { get; init; }
    public bool? IsDarkMode { get; init; }
    public double? ScreenBrightnessPercent { get; init; }
    public string? NetworkName { get; init; }
    public bool? IsProxyEnabled { get; init; }
    public string? ProxyMode { get; init; }
    public string? DefaultOutputAudioDeviceId { get; init; }
    public bool? IsBluetoothRadioOn { get; init; }
    public IReadOnlyList<string> ConnectedBluetoothDevices { get; init; } = [];
    public TimeSpan? Sunrise { get; init; }
    public TimeSpan? Sunset { get; init; }
    public string? SunTimeSource { get; init; }
    public bool? IsFullscreenAppActive { get; init; }
    public bool? IsGameLikelyRunning { get; init; }
    public bool SuppressActionNotifications { get; init; }

    public static SystemContext From(AutomationStaticContext staticContext, AutomationDynamicContext dynamicContext)
    {
        ArgumentNullException.ThrowIfNull(staticContext);
        ArgumentNullException.ThrowIfNull(dynamicContext);

        return new SystemContext
        {
            MachineName = staticContext.MachineName,
            OperatingSystem = staticContext.OperatingSystem,
            OperatingSystemArchitecture = staticContext.OperatingSystemArchitecture,
            ProcessArchitecture = staticContext.ProcessArchitecture,
            ProcessorCount = staticContext.ProcessorCount,
            Is64BitOperatingSystem = staticContext.Is64BitOperatingSystem,
            Is64BitProcess = staticContext.Is64BitProcess,
            CapturedAt = dynamicContext.CapturedAt,
            ForegroundProcessName = dynamicContext.ForegroundProcessName,
            ForegroundWindowTitle = dynamicContext.ForegroundWindowTitle,
            IsOnBattery = dynamicContext.IsOnBattery,
            IsCharging = dynamicContext.IsCharging,
            BatteryPercent = dynamicContext.BatteryPercent,
            IsDarkMode = dynamicContext.IsDarkMode,
            ScreenBrightnessPercent = dynamicContext.ScreenBrightnessPercent,
            NetworkName = dynamicContext.NetworkName,
            IsProxyEnabled = dynamicContext.IsProxyEnabled,
            ProxyMode = dynamicContext.ProxyMode,
            DefaultOutputAudioDeviceId = dynamicContext.DefaultOutputAudioDeviceId,
            IsBluetoothRadioOn = dynamicContext.IsBluetoothRadioOn,
            ConnectedBluetoothDevices = dynamicContext.ConnectedBluetoothDevices,
            Sunrise = dynamicContext.Sunrise,
            Sunset = dynamicContext.Sunset,
            SunTimeSource = dynamicContext.SunTimeSource,
            IsFullscreenAppActive = dynamicContext.IsFullscreenAppActive,
            IsGameLikelyRunning = dynamicContext.IsGameLikelyRunning,
            SuppressActionNotifications = dynamicContext.SuppressActionNotifications
        };
    }
}
