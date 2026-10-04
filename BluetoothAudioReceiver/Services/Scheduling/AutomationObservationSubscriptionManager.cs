using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Media.Devices;
using Windows.Networking.Connectivity;
using Windows.System.Power;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationObservationSubscriptionManager
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationObservationSubscriptionManager));
    private readonly IAudioPlaybackConnectionService _audioPlaybackConnectionService;
    private readonly BluetoothRadioMonitorService _bluetoothRadioMonitorService;
    private readonly AutoDayNightService _autoDayNightService;
    private readonly EventHandler<DeviceInformation> _audioConnected;
    private readonly EventHandler<DeviceInformation> _audioDisconnected;
    private readonly Action<RadioState> _bluetoothStateChanged;
    private readonly EventHandler _sunStateChanged;
    private readonly EventHandler<object> _powerSnapshotChanged;
    private readonly TypedEventHandler<object, DefaultAudioRenderDeviceChangedEventArgs> _audioRenderDeviceChanged;
    private readonly NetworkStatusChangedEventHandler _networkStatusChanged;
    private readonly PowerModeChangedEventHandler _powerModeChanged;
    private readonly EventHandler _systemTimeChanged;
    private readonly UserPreferenceChangedEventHandler _userPreferenceChanged;
    private readonly EventHandler<IntPtr> _foregroundWindowChanged;
    private bool _subscribed;

    public AutomationObservationSubscriptionManager(
        IAudioPlaybackConnectionService audioPlaybackConnectionService,
        BluetoothRadioMonitorService bluetoothRadioMonitorService,
        AutoDayNightService autoDayNightService,
        EventHandler<DeviceInformation> audioConnected,
        EventHandler<DeviceInformation> audioDisconnected,
        Action<RadioState> bluetoothStateChanged,
        EventHandler sunStateChanged,
        EventHandler<object> powerSnapshotChanged,
        TypedEventHandler<object, DefaultAudioRenderDeviceChangedEventArgs> audioRenderDeviceChanged,
        NetworkStatusChangedEventHandler networkStatusChanged,
        PowerModeChangedEventHandler powerModeChanged,
        EventHandler systemTimeChanged,
        UserPreferenceChangedEventHandler userPreferenceChanged,
        EventHandler<IntPtr> foregroundWindowChanged)
    {
        _audioPlaybackConnectionService = audioPlaybackConnectionService;
        _bluetoothRadioMonitorService = bluetoothRadioMonitorService;
        _autoDayNightService = autoDayNightService;
        _audioConnected = audioConnected;
        _audioDisconnected = audioDisconnected;
        _bluetoothStateChanged = bluetoothStateChanged;
        _sunStateChanged = sunStateChanged;
        _powerSnapshotChanged = powerSnapshotChanged;
        _audioRenderDeviceChanged = audioRenderDeviceChanged;
        _networkStatusChanged = networkStatusChanged;
        _powerModeChanged = powerModeChanged;
        _systemTimeChanged = systemTimeChanged;
        _userPreferenceChanged = userPreferenceChanged;
        _foregroundWindowChanged = foregroundWindowChanged;
    }

    public void Subscribe()
    {
        if (_subscribed)
        {
            _log.Debug("Automation observation listeners already subscribed");
            return;
        }

        _log.Debug(
            "Starting automation observation listener registration. ProcessId={ProcessId}, ThreadId={ThreadId}",
            Environment.ProcessId,
            Environment.CurrentManagedThreadId);
        _subscribed = true;
        _audioPlaybackConnectionService.DeviceConnected += _audioConnected;
        _audioPlaybackConnectionService.DeviceDisconnected += _audioDisconnected;
        _bluetoothRadioMonitorService.StateChanged += _bluetoothStateChanged;
        _autoDayNightService.StateChanged += _sunStateChanged;
        PowerManager.PowerSupplyStatusChanged += _powerSnapshotChanged;
        PowerManager.RemainingChargePercentChanged += _powerSnapshotChanged;
        MediaDevice.DefaultAudioRenderDeviceChanged += _audioRenderDeviceChanged;
        NetworkInformation.NetworkStatusChanged += _networkStatusChanged;
        SystemEvents.PowerModeChanged += _powerModeChanged;
        SystemEvents.TimeChanged += _systemTimeChanged;
        SystemEvents.UserPreferenceChanged += _userPreferenceChanged;
        try
        {
            BackgroundEfficiencyWindowHook.SubscribeToWindowEvents();
            BackgroundEfficiencyWindowHook.SystemForegroundWindowChanged += _foregroundWindowChanged;
        }
        catch
        {
            // The remaining system listeners must continue if the foreground hook is unavailable.
        }

        _log.Information("Automation observation listeners registered");
    }

    public void Unsubscribe()
    {
        if (!_subscribed)
        {
            _log.Debug("Automation observation listeners already unsubscribed");
            return;
        }

        _log.Debug("Starting automation observation listener removal");
        _audioPlaybackConnectionService.DeviceConnected -= _audioConnected;
        _audioPlaybackConnectionService.DeviceDisconnected -= _audioDisconnected;
        _bluetoothRadioMonitorService.StateChanged -= _bluetoothStateChanged;
        _autoDayNightService.StateChanged -= _sunStateChanged;
        PowerManager.PowerSupplyStatusChanged -= _powerSnapshotChanged;
        PowerManager.RemainingChargePercentChanged -= _powerSnapshotChanged;
        MediaDevice.DefaultAudioRenderDeviceChanged -= _audioRenderDeviceChanged;
        NetworkInformation.NetworkStatusChanged -= _networkStatusChanged;
        SystemEvents.PowerModeChanged -= _powerModeChanged;
        SystemEvents.TimeChanged -= _systemTimeChanged;
        SystemEvents.UserPreferenceChanged -= _userPreferenceChanged;
        BackgroundEfficiencyWindowHook.SystemForegroundWindowChanged -= _foregroundWindowChanged;
        BackgroundEfficiencyWindowHook.UnsubscribeWindowEvents();
        _subscribed = false;
        _log.Information("Automation observation listeners removed");
    }
}
