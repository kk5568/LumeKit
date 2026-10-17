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

/// <summary>
/// 按需求并集订阅系统事件。每个源单独挂、单独摘，重复调用同一种需求不会有副作用，
/// 所以上层可以随时把最新的并集丢进来。
/// </summary>
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
    private readonly HashSet<ObservationSourceKind> _attached = [];
    private readonly object _syncRoot = new();

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

    /// <summary>
    /// 把订阅调整到「正好等于这份需求」。已经挂上的不会重复挂，不再需要的会摘掉。
    /// </summary>
    public void Apply(IReadOnlyCollection<ObservationSourceKind> needed)
    {
        ArgumentNullException.ThrowIfNull(needed);
        var wanted = needed as ISet<ObservationSourceKind> ?? new HashSet<ObservationSourceKind>(needed);

        lock (_syncRoot)
        {
            foreach (var source in Enum.GetValues<ObservationSourceKind>())
            {
                var want = wanted.Contains(source);
                var have = _attached.Contains(source);
                if (want == have)
                {
                    continue;
                }

                if (want)
                {
                    if (TryAttach(source))
                    {
                        _attached.Add(source);
                        _log.Information("Observation source attached. Source={Source}", source);
                    }
                }
                else
                {
                    TryDetach(source);
                    _attached.Remove(source);
                    _log.Information("Observation source detached. Source={Source}", source);
                }
            }
        }
    }

    /// <summary>摘掉全部订阅（退出时用）。</summary>
    public void UnsubscribeAll()
    {
        lock (_syncRoot)
        {
            foreach (var source in _attached.ToArray())
            {
                TryDetach(source);
            }

            _attached.Clear();
        }

        _log.Information("Automation observation listeners removed");
    }

    private bool TryAttach(ObservationSourceKind source)
    {
        try
        {
            switch (source)
            {
                case ObservationSourceKind.AudioPlaybackDevices:
                    _audioPlaybackConnectionService.DeviceConnected += _audioConnected;
                    _audioPlaybackConnectionService.DeviceDisconnected += _audioDisconnected;
                    return true;
                case ObservationSourceKind.BluetoothRadioState:
                    _bluetoothRadioMonitorService.StateChanged += _bluetoothStateChanged;
                    return true;
                case ObservationSourceKind.SunScheduleState:
                    _autoDayNightService.StateChanged += _sunStateChanged;
                    return true;
                case ObservationSourceKind.PowerSupply:
                    PowerManager.PowerSupplyStatusChanged += _powerSnapshotChanged;
                    PowerManager.RemainingChargePercentChanged += _powerSnapshotChanged;
                    return true;
                case ObservationSourceKind.AudioRenderDevice:
                    MediaDevice.DefaultAudioRenderDeviceChanged += _audioRenderDeviceChanged;
                    return true;
                case ObservationSourceKind.NetworkStatus:
                    NetworkInformation.NetworkStatusChanged += _networkStatusChanged;
                    return true;
                case ObservationSourceKind.SessionPowerMode:
                    SystemEvents.PowerModeChanged += _powerModeChanged;
                    return true;
                case ObservationSourceKind.SystemTime:
                    SystemEvents.TimeChanged += _systemTimeChanged;
                    return true;
                case ObservationSourceKind.UserPreference:
                    SystemEvents.UserPreferenceChanged += _userPreferenceChanged;
                    return true;
                case ObservationSourceKind.ForegroundWindow:
                    BackgroundEfficiencyWindowHook.SubscribeToWindowEvents();
                    BackgroundEfficiencyWindowHook.SystemForegroundWindowChanged += _foregroundWindowChanged;
                    return true;
                default:
                    // 进程监听、日出循环、注册表代理监听由服务自己按需启动，不走事件订阅。
                    return false;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to attach observation source. Source={Source}", source);
            return false;
        }
    }

    private void TryDetach(ObservationSourceKind source)
    {
        try
        {
            switch (source)
            {
                case ObservationSourceKind.AudioPlaybackDevices:
                    _audioPlaybackConnectionService.DeviceConnected -= _audioConnected;
                    _audioPlaybackConnectionService.DeviceDisconnected -= _audioDisconnected;
                    break;
                case ObservationSourceKind.BluetoothRadioState:
                    _bluetoothRadioMonitorService.StateChanged -= _bluetoothStateChanged;
                    break;
                case ObservationSourceKind.SunScheduleState:
                    _autoDayNightService.StateChanged -= _sunStateChanged;
                    break;
                case ObservationSourceKind.PowerSupply:
                    PowerManager.PowerSupplyStatusChanged -= _powerSnapshotChanged;
                    PowerManager.RemainingChargePercentChanged -= _powerSnapshotChanged;
                    break;
                case ObservationSourceKind.AudioRenderDevice:
                    MediaDevice.DefaultAudioRenderDeviceChanged -= _audioRenderDeviceChanged;
                    break;
                case ObservationSourceKind.NetworkStatus:
                    NetworkInformation.NetworkStatusChanged -= _networkStatusChanged;
                    break;
                case ObservationSourceKind.SessionPowerMode:
                    SystemEvents.PowerModeChanged -= _powerModeChanged;
                    break;
                case ObservationSourceKind.SystemTime:
                    SystemEvents.TimeChanged -= _systemTimeChanged;
                    break;
                case ObservationSourceKind.UserPreference:
                    SystemEvents.UserPreferenceChanged -= _userPreferenceChanged;
                    break;
                case ObservationSourceKind.ForegroundWindow:
                    BackgroundEfficiencyWindowHook.SystemForegroundWindowChanged -= _foregroundWindowChanged;
                    BackgroundEfficiencyWindowHook.UnsubscribeWindowEvents();
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to detach observation source. Source={Source}", source);
        }
    }
}
