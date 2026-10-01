using BluetoothAudioReceiver.Contracts.Services;
using Microsoft.UI.Xaml;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationSessionStateStore
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationSessionStateStore));

    private readonly IPowerProvider _powerProvider;
    private readonly ITouchpadProvider _touchpadProvider;
    private readonly IDisplayProvider _displayProvider;
    private readonly IAudioProvider _audioProvider;
    private readonly IThemeProvider _themeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<string, AutomationSessionLease> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AutomationResourceKind, int> _resourceReferenceCounts = new();

    public AutomationSessionStateStore(
        IPowerProvider powerProvider,
        ITouchpadProvider touchpadProvider,
        IDisplayProvider displayProvider,
        IAudioProvider audioProvider,
        IThemeProvider themeProvider)
    {
        _powerProvider = powerProvider;
        _touchpadProvider = touchpadProvider;
        _displayProvider = displayProvider;
        _audioProvider = audioProvider;
        _themeProvider = themeProvider;
    }

    public async Task CaptureAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_leases.ContainsKey(key))
            {
                _log.Information("Automation snapshot capture skipped because lease already exists. Key={SnapshotKey}", key);
                return;
            }
        }

        var liveSnapshot = new AutomationSessionSnapshot
        {
            ActivePowerScheme = await _powerProvider.GetActiveSchemeAsync(cancellationToken),
            IsTouchpadEnabled = await _touchpadProvider.IsEnabledAsync(cancellationToken),
            Brightness = await _displayProvider.GetBrightnessAsync(cancellationToken),
            InputVolumeIndex = _audioProvider.CurrentInputVolumeIndex,
            OutputDeviceId = await _audioProvider.GetCurrentOutputDeviceIdAsync(cancellationToken),
            Theme = _themeProvider.CurrentTheme
        };

        lock (_gate)
        {
            if (_leases.ContainsKey(key))
            {
                _log.Information("Automation snapshot capture skipped after state fetch because lease already exists. Key={SnapshotKey}", key);
                return;
            }

            var capturedSnapshot = new AutomationSessionSnapshot();

            CaptureResourceIfAvailable(AutomationResourceKind.PowerScheme, liveSnapshot.ActivePowerScheme, value => capturedSnapshot.ActivePowerScheme = value);
            CaptureResourceIfAvailable(AutomationResourceKind.Touchpad, liveSnapshot.IsTouchpadEnabled, value => capturedSnapshot.IsTouchpadEnabled = value);
            CaptureResourceIfAvailable(AutomationResourceKind.Brightness, liveSnapshot.Brightness, value => capturedSnapshot.Brightness = value);
            CaptureResourceIfAvailable(AutomationResourceKind.InputVolume, liveSnapshot.InputVolumeIndex, value => capturedSnapshot.InputVolumeIndex = value);
            CaptureResourceIfAvailable(AutomationResourceKind.OutputDevice, liveSnapshot.OutputDeviceId, value => capturedSnapshot.OutputDeviceId = value);
            CaptureResourceIfAvailable(AutomationResourceKind.Theme, liveSnapshot.Theme, value => capturedSnapshot.Theme = value);

            _leases[key] = new AutomationSessionLease(capturedSnapshot);
            IncrementReferenceCount(AutomationResourceKind.PowerScheme);
            IncrementReferenceCount(AutomationResourceKind.Touchpad);
            IncrementReferenceCount(AutomationResourceKind.Brightness);
            IncrementReferenceCount(AutomationResourceKind.InputVolume);
            IncrementReferenceCount(AutomationResourceKind.OutputDevice);
            IncrementReferenceCount(AutomationResourceKind.Theme);

            _log.Information(
                "Automation snapshot captured with lease. Key={SnapshotKey}, RefCounts={RefCounts}, PowerScheme={PowerScheme}, TouchpadEnabled={TouchpadEnabled}, Brightness={Brightness}, InputVolumeIndex={InputVolumeIndex}, OutputDeviceId={OutputDeviceId}, Theme={Theme}",
                key,
                FormatReferenceCounts(),
                capturedSnapshot.ActivePowerScheme,
                capturedSnapshot.IsTouchpadEnabled,
                capturedSnapshot.Brightness,
                capturedSnapshot.InputVolumeIndex,
                capturedSnapshot.OutputDeviceId,
                capturedSnapshot.Theme);
        }
    }

    public bool TryRelease(string key, out AutomationSessionSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_leases.Remove(key, out var lease))
            {
                snapshot = new AutomationSessionSnapshot();
                _log.Warning("Automation snapshot lease missing on release. Key={SnapshotKey}", key);
                return false;
            }

            var restoreSnapshot = new AutomationSessionSnapshot();
            ReleaseResource(AutomationResourceKind.PowerScheme, lease.Snapshot.ActivePowerScheme, value => restoreSnapshot.ActivePowerScheme = value);
            ReleaseResource(AutomationResourceKind.Touchpad, lease.Snapshot.IsTouchpadEnabled, value => restoreSnapshot.IsTouchpadEnabled = value);
            ReleaseResource(AutomationResourceKind.Brightness, lease.Snapshot.Brightness, value => restoreSnapshot.Brightness = value);
            ReleaseResource(AutomationResourceKind.InputVolume, lease.Snapshot.InputVolumeIndex, value => restoreSnapshot.InputVolumeIndex = value);
            ReleaseResource(AutomationResourceKind.OutputDevice, lease.Snapshot.OutputDeviceId, value => restoreSnapshot.OutputDeviceId = value);
            ReleaseResource(AutomationResourceKind.Theme, lease.Snapshot.Theme, value => restoreSnapshot.Theme = value);

            snapshot = restoreSnapshot;
            _log.Information(
                "Automation snapshot lease released. Key={SnapshotKey}, RefCounts={RefCounts}, RestorePowerScheme={PowerScheme}, RestoreTouchpad={TouchpadEnabled}, RestoreBrightness={Brightness}, RestoreInputVolumeIndex={InputVolumeIndex}, RestoreOutputDeviceId={OutputDeviceId}, RestoreTheme={Theme}",
                key,
                FormatReferenceCounts(),
                snapshot.ActivePowerScheme,
                snapshot.IsTouchpadEnabled,
                snapshot.Brightness,
                snapshot.InputVolumeIndex,
                snapshot.OutputDeviceId,
                snapshot.Theme);
            return true;
        }
    }

    private void CaptureResourceIfAvailable<T>(AutomationResourceKind resourceKind, T value, Action<T> assignCapturedValue)
    {
        if (GetReferenceCount(resourceKind) == 0)
        {
            assignCapturedValue(value);
        }
    }

    private void ReleaseResource<T>(AutomationResourceKind resourceKind, T value, Action<T> assignRestoreValue)
    {
        var nextCount = DecrementReferenceCount(resourceKind);
        if (nextCount == 0)
        {
            assignRestoreValue(value);
        }
    }

    private void IncrementReferenceCount(AutomationResourceKind resourceKind)
    {
        _resourceReferenceCounts[resourceKind] = GetReferenceCount(resourceKind) + 1;
    }

    private int DecrementReferenceCount(AutomationResourceKind resourceKind)
    {
        var current = GetReferenceCount(resourceKind);
        if (current <= 1)
        {
            _resourceReferenceCounts.Remove(resourceKind);
            return 0;
        }

        var next = current - 1;
        _resourceReferenceCounts[resourceKind] = next;
        return next;
    }

    private int GetReferenceCount(AutomationResourceKind resourceKind)
        => _resourceReferenceCounts.TryGetValue(resourceKind, out var count) ? count : 0;

    private string FormatReferenceCounts()
        => string.Join(", ", Enum.GetValues<AutomationResourceKind>().Select(kind => $"{kind}:{GetReferenceCount(kind)}"));
}

internal sealed class AutomationSessionSnapshot
{
    public string? ActivePowerScheme { get; set; }
    public bool? IsTouchpadEnabled { get; set; }
    public byte? Brightness { get; set; }
    public int? InputVolumeIndex { get; set; }
    public string? OutputDeviceId { get; set; }
    public ElementTheme? Theme { get; set; }
}

internal sealed record AutomationSessionLease(AutomationSessionSnapshot Snapshot);

internal enum AutomationResourceKind
{
    PowerScheme,
    Touchpad,
    Brightness,
    InputVolume,
    OutputDevice,
    Theme
}
