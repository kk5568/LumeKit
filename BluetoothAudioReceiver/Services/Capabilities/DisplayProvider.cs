using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class DisplayProvider : IDisplayProvider
{
    private readonly IBrightnessService _brightnessService;

    public DisplayProvider(IBrightnessService brightnessService)
    {
        _brightnessService = brightnessService;
    }

    public async Task<byte?> GetBrightnessAsync(CancellationToken cancellationToken = default)
        => await _brightnessService.GetCurrentBrightnessAsync();

    public Task SetBrightnessAsync(byte brightness, CancellationToken cancellationToken = default)
        => _brightnessService.AdjustBrightnessAsync(brightness);
}
