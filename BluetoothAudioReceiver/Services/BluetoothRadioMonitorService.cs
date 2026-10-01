using BluetoothAudioReceiver.Contracts.Services;
using Serilog;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace BluetoothAudioReceiver.Services;

internal sealed class BluetoothRadioMonitorService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(BluetoothRadioMonitorService));

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private Radio? _radio;
    private RadioState? _lastKnownState;
    private bool _isInitialized;

    public event Action<RadioState>? StateChanged;

    public RadioState? CurrentState => _lastKnownState;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null)
            {
                _log.Warning("Bluetooth adapter not found, radio monitor disabled");
                return;
            }

            _radio = await adapter.GetRadioAsync();
            if (_radio == null)
            {
                _log.Warning("Bluetooth radio not found, radio monitor disabled");
                return;
            }

            _lastKnownState = _radio.State;
            _radio.StateChanged += OnRadioStateChanged;
            _isInitialized = true;
            _log.Information("Bluetooth radio monitor initialized, state={State}", _lastKnownState);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to initialize Bluetooth radio monitor");
        }
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        _ = HandleRadioStateChangedAsync(sender.State);
    }

    private async Task HandleRadioStateChangedAsync(RadioState newState)
    {
        if (!_isInitialized)
        {
            return;
        }

        await _stateLock.WaitAsync();
        try
        {
            var previousState = _lastKnownState;
            _lastKnownState = newState;
            if (previousState != newState)
            {
                StateChanged?.Invoke(newState);
            }

            if (newState == RadioState.Off || (newState == RadioState.On && previousState == RadioState.Off))
            {
                _log.Information("Bluetooth radio state changed to {State}; automatic reconnect is disabled", newState);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error while handling Bluetooth radio state change");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void Dispose()
    {
        if (_radio != null)
        {
            _radio.StateChanged -= OnRadioStateChanged;
            _radio = null;
        }

        _stateLock.Dispose();
    }
}
