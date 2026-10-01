using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class TouchpadProvider : ITouchpadProvider
{
    private readonly ITouchpadControlService _touchpadControlService;

    public TouchpadProvider(ITouchpadControlService touchpadControlService)
    {
        _touchpadControlService = touchpadControlService;
    }

    public Task<bool?> IsEnabledAsync(CancellationToken cancellationToken = default)
        => _touchpadControlService.IsEnabledAsync(cancellationToken);

    public Task<bool> EnableAsync(CancellationToken cancellationToken = default)
        => _touchpadControlService.EnableAsync(cancellationToken);

    public Task<bool> DisableAsync(CancellationToken cancellationToken = default)
        => _touchpadControlService.DisableAsync(cancellationToken);
}
