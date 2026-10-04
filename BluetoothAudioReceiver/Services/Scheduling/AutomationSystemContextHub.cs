using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationSystemContextHub : IAutomationSystemContextHub
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationSystemContextHub));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AutomationStaticContext _staticContext = new();
    private AutomationDynamicContext _dynamicContext = new();
    private SystemContext _current = SystemContext.From(new AutomationStaticContext(), new AutomationDynamicContext());

    public AutomationStaticContext StaticContext => _staticContext;

    public AutomationDynamicContext DynamicContext => _dynamicContext;

    public SystemContext Current => _current;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _staticContext = new AutomationStaticContext();
        _current = SystemContext.From(_staticContext, _dynamicContext);
        _log.Information("Automation system context hub initialized. Machine={MachineName}, OS={OperatingSystem}, CpuCount={ProcessorCount}",
            _staticContext.MachineName,
            _staticContext.OperatingSystem,
            _staticContext.ProcessorCount);
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(SystemContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _current = context;
            _dynamicContext = new AutomationDynamicContext
            {
                CapturedAt = context.CapturedAt,
                ForegroundProcessName = context.ForegroundProcessName,
                ForegroundWindowTitle = context.ForegroundWindowTitle,
                IsOnBattery = context.IsOnBattery,
                IsCharging = context.IsCharging,
                BatteryPercent = context.BatteryPercent,
                IsDarkMode = context.IsDarkMode,
                ScreenBrightnessPercent = context.ScreenBrightnessPercent,
                NetworkName = context.NetworkName,
                IsProxyEnabled = context.IsProxyEnabled,
                ProxyMode = context.ProxyMode,
                DefaultOutputAudioDeviceId = context.DefaultOutputAudioDeviceId,
                IsBluetoothRadioOn = context.IsBluetoothRadioOn,
                ConnectedBluetoothDevices = context.ConnectedBluetoothDevices,
                Sunrise = context.Sunrise,
                Sunset = context.Sunset,
                SunTimeSource = context.SunTimeSource,
                IsFullscreenAppActive = context.IsFullscreenAppActive,
                IsGameLikelyRunning = context.IsGameLikelyRunning,
                SuppressActionNotifications = context.SuppressActionNotifications
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateDynamicAsync(AutomationDynamicContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _dynamicContext = context;
            _current = SystemContext.From(_staticContext, _dynamicContext);
        }
        finally
        {
            _gate.Release();
        }
    }
}
