using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.Core.Helpers;
using BluetoothAudioReceiver.Models.Application;

namespace BluetoothAudioReceiver.Services;

internal sealed class AudioProvider : IAudioProvider
{
    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly ILocalSettingsService _localSettingsService;

    public AudioProvider(
        IAudioPlaybackConnectionService connectionService,
        IAppSettingsService appSettingsService,
        IAudioRouteHotkeyService audioRouteHotkeyService,
        ILocalSettingsService localSettingsService)
    {
        _connectionService = connectionService;
        _appSettingsService = appSettingsService;
        _audioRouteHotkeyService = audioRouteHotkeyService;
        _localSettingsService = localSettingsService;
    }

    public async Task SetInputDeviceAsync(
        string deviceId,
        bool disconnect = false,
        CancellationToken cancellationToken = default)
    {
        var device = (await _connectionService.FindAllAudioDevicesAsync())
            .FirstOrDefault(item => string.Equals(item.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            throw new InvalidOperationException($"找不到输入设备: {deviceId}");
        }

        if (disconnect)
        {
            var connectedDevice = _connectionService.ConnectedDevices
                .FirstOrDefault(item => string.Equals(item.Id, device.Id, StringComparison.OrdinalIgnoreCase));

            if (connectedDevice is not null)
            {
                await _connectionService.DisconnectAsync(connectedDevice);
            }

            if (string.Equals(_appSettingsService.PreferredInputDeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
            {
                await _appSettingsService.SetPreferredInputDeviceIdAsync(string.Empty);
            }

            return;
        }

        if (!await _connectionService.ConnectAsync(device))
        {
            throw new InvalidOperationException($"输入设备连接失败: {device.Name}");
        }

        await _appSettingsService.SetPreferredInputDeviceIdAsync(device.Id);
        await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, device.Id);
    }

    public Task<string?> GetCurrentOutputDeviceIdAsync(CancellationToken cancellationToken = default)
        => _audioRouteHotkeyService.GetCurrentDefaultOutputEndpointIdAsync();

    public async Task SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await _audioRouteHotkeyService.SetOutputDeviceAsync(deviceId, suppressNotification: true);
    }

    public Task<IReadOnlyList<AudioOutputDeviceItem>> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
        => _audioRouteHotkeyService.GetOutputDevicesAsync();
}
