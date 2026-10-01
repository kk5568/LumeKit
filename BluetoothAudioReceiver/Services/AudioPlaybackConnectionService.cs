using System.Collections.Concurrent;
using BluetoothAudioReceiver.Core.Helpers;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal class AudioPlaybackConnectionService : IAudioPlaybackConnectionService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AudioPlaybackConnectionService));
    private static readonly TimeSpan DeviceListCacheDuration = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, (DeviceInformation Device, AudioPlaybackConnection Connection)> _connections = [];
    private readonly object _connectedDevicesSnapshotGate = new();
    private readonly object _deviceListCacheGate = new();
    private IReadOnlyList<DeviceInformation>? _cachedDevices;
    private DateTimeOffset _cachedDevicesAt = DateTimeOffset.MinValue;
    private IReadOnlyList<DeviceInformation> _connectedDevicesSnapshot = [];

    public event EventHandler<DeviceInformation>? DeviceConnected;
    public event EventHandler<DeviceInformation>? DeviceDisconnected;

    public IReadOnlyList<DeviceInformation> ConnectedDevices =>
        _connectedDevicesSnapshot;

    public string GetDeviceSelector()
    {
        return AudioPlaybackConnection.GetDeviceSelector();
    }

    public async Task<IEnumerable<DeviceInformation>> FindAllAudioDevicesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_deviceListCacheGate)
        {
            if (_cachedDevices is not null && now - _cachedDevicesAt < DeviceListCacheDuration)
            {
                return _cachedDevices;
            }
        }

        var selector = GetDeviceSelector();
        var devices = await DeviceInformation.FindAllAsync(selector, DeviceIdentityHelper.BuildAdditionalProperties());
        _log.Information("Found {Count} audio playback devices", devices.Count);
        var snapshot = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToList()
            .AsReadOnly();

        lock (_deviceListCacheGate)
        {
            _cachedDevices = snapshot;
            _cachedDevicesAt = now;
        }

        return snapshot;
    }

    public async Task<IEnumerable<DeviceInformation>> FindAllInputAudioDevicesAsync()
    {
        var selector = MediaDevice.GetAudioCaptureSelector();
        var devices = await DeviceInformation.FindAllAsync(
            selector,
            DeviceIdentityHelper.BuildAdditionalProperties());

        _log.Information("Found {Count} audio capture devices", devices.Count);
        return devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToList()
            .AsReadOnly();
    }

    public async Task<IEnumerable<DeviceInformation>> FindAllInputRouteDevicesAsync()
    {
        var playbackDevices = await FindAllAudioDevicesAsync();
        var captureDevices = await FindAllInputAudioDevicesAsync();

        var merged = playbackDevices
            .Concat(captureDevices)
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(
                device => DeviceIdentityHelper.GetPhysicalDeviceId(device),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList()
            .AsReadOnly();

        _log.Information(
            "Found {Count} input route devices from {PlaybackCount} playback and {CaptureCount} capture endpoints",
            merged.Count,
            playbackDevices.Count(),
            captureDevices.Count());

        return merged;
    }

    public void InvalidateDeviceListCache()
    {
        lock (_deviceListCacheGate)
        {
            _cachedDevices = null;
            _cachedDevicesAt = DateTimeOffset.MinValue;
        }
    }

    public async Task<bool> ConnectAsync(DeviceInformation device)
    {
        if (_connections.TryGetValue(device.Id, out var existing))
        {
            // 如果字典里还保留着旧连接，但底层状态已经关闭，先清掉再重建。
            if (existing.Connection.State != AudioPlaybackConnectionState.Closed)
            {
                _log.Information("Device {Name} is already tracked as connected", device.Name);
                return true;
            }

            _log.Warning("Removing stale connection entry for {Name}", device.Name);
            _connections.TryRemove(device.Id, out _);
            existing.Connection.StateChanged -= OnConnectionStateChanged;
            existing.Connection.Dispose();
        }

        try
        {
            var connection = AudioPlaybackConnection.TryCreateFromId(device.Id);
            if (connection == null)
            {
                _log.Error("Failed to create AudioPlaybackConnection for {Name}", device.Name);
                return false;
            }

            _connections[device.Id] = (device, connection);
            InvalidateDeviceListCache();
            RefreshConnectedDevicesSnapshot();

            await connection.StartAsync();
            var result = await connection.OpenAsync();

            switch (result.Status)
            {
                case AudioPlaybackConnectionOpenResultStatus.Success:
                    connection.StateChanged += OnConnectionStateChanged;
                    try
                    {
                        DeviceConnected?.Invoke(this, device);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Device connected notification handler failed for {Name}", device.Name);
                    }

                    _log.Information("Connected to {Name}", device.Name);
                    return true;

                case AudioPlaybackConnectionOpenResultStatus.RequestTimedOut:
                    _log.Warning("Connection to {Name} timed out", device.Name);
                    break;

                case AudioPlaybackConnectionOpenResultStatus.DeniedBySystem:
                    _log.Warning("Connection to {Name} denied by system", device.Name);
                    break;

                case AudioPlaybackConnectionOpenResultStatus.UnknownFailure:
                    _log.Error("Connection to {Name} failed with unknown error", device.Name);
                    break;
            }

            _connections.TryRemove(device.Id, out _);
            connection.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error connecting to {Name}", device.Name);
            _connections.TryRemove(device.Id, out _);
            return false;
        }
    }

    public Task DisconnectAsync(DeviceInformation device)
    {
        if (_connections.TryRemove(device.Id, out var entry))
        {
            entry.Connection.StateChanged -= OnConnectionStateChanged;
            entry.Connection.Dispose();
            InvalidateDeviceListCache();
            RefreshConnectedDevicesSnapshot();
            DeviceDisconnected?.Invoke(this, device);
            _log.Information("Disconnected from {Name}", device.Name);
        }

        return Task.CompletedTask;
    }

    public async Task ReconnectPreviousDevicesAsync(IEnumerable<string> deviceIds)
    {
        var ids = deviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            return;
        }

        const int maxAttempts = 5;

        foreach (var deviceId in ids)
        {
            var connected = false;
            for (var attempt = 1; attempt <= maxAttempts && !connected; attempt++)
            {
                try
                {
                    InvalidateDeviceListCache();
                    var deviceSnapshot = (await FindAllAudioDevicesAsync()).ToList();
                    var device = await TryResolveDeviceAsync(deviceId, deviceSnapshot);
                    if (device == null)
                    {
                        _log.Warning(
                            "Reconnect attempt {Attempt}/{MaxAttempts} skipped because device {Id} is not available yet",
                            attempt,
                            maxAttempts,
                            deviceId);

                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(500);
                        }

                        continue;
                    }

                    connected = await ConnectAsync(device);
                    if (!connected && attempt < maxAttempts)
                    {
                        await Task.Delay(500);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(
                        ex,
                        "Failed to reconnect device {Id} on attempt {Attempt}/{MaxAttempts}",
                        deviceId,
                        attempt,
                        maxAttempts);

                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(500);
                    }
                }
            }

            if (!connected)
            {
                _log.Warning("Reconnect failed for device {Id} after {MaxAttempts} attempts", deviceId, maxAttempts);
            }
        }
    }

    private async Task<DeviceInformation?> TryResolveDeviceAsync(string deviceId, IReadOnlyList<DeviceInformation> deviceSnapshot)
    {
        try
        {
            var matched = deviceSnapshot.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }

            if (DeviceIdentityHelper.TryParseSelectionId(deviceId, out _, out var physicalDeviceId) &&
                !string.IsNullOrWhiteSpace(physicalDeviceId))
            {
                matched = deviceSnapshot.FirstOrDefault(d =>
                    string.Equals(DeviceIdentityHelper.GetPhysicalDeviceId(d), physicalDeviceId, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    return matched;
                }
            }

            matched = deviceSnapshot.FirstOrDefault(d =>
                string.Equals(DeviceIdentityHelper.GetSelectionId(d, true), deviceId, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }

            var created = await DeviceInformation.CreateFromIdAsync(deviceId);
            return created;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to resolve device {Id}", deviceId);
            return null;
        }
    }

    private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
    {
        try
        {
            if (sender.State == AudioPlaybackConnectionState.Closed)
            {
                var deviceId = sender.DeviceId;
                if (_connections.TryRemove(deviceId, out var entry))
                {
                    sender.StateChanged -= OnConnectionStateChanged;
                    sender.Dispose();
                    InvalidateDeviceListCache();
                    RefreshConnectedDevicesSnapshot();
                    DeviceDisconnected?.Invoke(this, entry.Device);
                    _log.Information("Device {Name} disconnected", entry.Device.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in connection state changed handler");
        }
    }

    public void Dispose()
    {
        foreach (var entry in _connections.Values)
        {
            entry.Connection.StateChanged -= OnConnectionStateChanged;
            entry.Connection.Dispose();
        }

        _connections.Clear();
        RefreshConnectedDevicesSnapshot();
        InvalidateDeviceListCache();
    }

    private void RefreshConnectedDevicesSnapshot()
    {
        lock (_connectedDevicesSnapshotGate)
        {
            _connectedDevicesSnapshot = _connections.Values
                .Select(v => v.Device)
                .Where(device => !string.IsNullOrWhiteSpace(device.Id))
                .ToArray();
        }
    }
}
