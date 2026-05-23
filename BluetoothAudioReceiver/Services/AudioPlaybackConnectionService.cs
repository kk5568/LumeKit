using System.Collections.Concurrent;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal class AudioPlaybackConnectionService : IAudioPlaybackConnectionService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AudioPlaybackConnectionService));

    private readonly ConcurrentDictionary<string, (DeviceInformation Device, AudioPlaybackConnection Connection)> _connections = [];

    public event EventHandler<DeviceInformation>? DeviceConnected;
    public event EventHandler<DeviceInformation>? DeviceDisconnected;

    public IReadOnlyList<DeviceInformation> ConnectedDevices =>
        _connections.Values.Select(v => v.Device).ToList().AsReadOnly();

    public string GetDeviceSelector()
    {
        return AudioPlaybackConnection.GetDeviceSelector();
    }

    public async Task<IEnumerable<DeviceInformation>> FindAllAudioDevicesAsync()
    {
        var selector = GetDeviceSelector();
        var devices = await DeviceInformation.FindAllAsync(selector);
        _log.Information("Found {Count} audio playback devices", devices.Count);
        return devices;
    }

    public async Task<bool> ConnectAsync(DeviceInformation device)
    {
        if (_connections.ContainsKey(device.Id))
        {
            _log.Warning("Device {Name} is already connected", device.Name);
            return true;
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

            await connection.StartAsync();
            var result = await connection.OpenAsync();

            switch (result.Status)
            {
                case AudioPlaybackConnectionOpenResultStatus.Success:
                    connection.StateChanged += OnConnectionStateChanged;
                    DeviceConnected?.Invoke(this, device);
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
            DeviceDisconnected?.Invoke(this, device);
            _log.Information("Disconnected from {Name}", device.Name);
        }

        return Task.CompletedTask;
    }

    public async Task ReconnectPreviousDevicesAsync(IEnumerable<string> deviceIds)
    {
        foreach (var deviceId in deviceIds)
        {
            try
            {
                var device = await DeviceInformation.CreateFromIdAsync(deviceId);
                if (device != null)
                {
                    await ConnectAsync(device);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to reconnect device {Id}", deviceId);
            }
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
    }
}
