namespace BluetoothAudioReceiver.Automation.Models;

public sealed record AutomationDynamicContext
{
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
}
