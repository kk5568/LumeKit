using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace BluetoothAudioReceiver.Core.Contracts.Services;

public interface IAudioPlaybackConnectionService
{
    event EventHandler<DeviceInformation>? DeviceConnected;

    event EventHandler<DeviceInformation>? DeviceDisconnected;

    IReadOnlyList<DeviceInformation> ConnectedDevices { get; }

    string GetDeviceSelector();

    Task<IEnumerable<DeviceInformation>> FindAllAudioDevicesAsync();

    Task<bool> ConnectAsync(DeviceInformation device);

    Task DisconnectAsync(DeviceInformation device);

    Task ReconnectPreviousDevicesAsync(IEnumerable<string> deviceIds);
}
