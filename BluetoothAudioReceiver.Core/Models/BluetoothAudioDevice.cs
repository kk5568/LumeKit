using System;
using Windows.Devices.Enumeration;

namespace BluetoothAudioReceiver.Core.Models;

public enum DeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}

public class BluetoothAudioDevice
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DeviceConnectionState ConnectionState { get; set; } = DeviceConnectionState.Disconnected;

    public string StatusText { get; set; } = string.Empty;

    public DeviceInformation? DeviceInfo { get; set; }

    public bool CanConnect => ConnectionState == DeviceConnectionState.Disconnected;

    public bool CanDisconnect => ConnectionState == DeviceConnectionState.Connected;

    public bool IsConnecting => ConnectionState == DeviceConnectionState.Connecting;
}
