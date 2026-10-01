using Serilog;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace BluetoothAudioReceiver.Services;

internal sealed class AudioOutputDeviceWatcherService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AudioOutputDeviceWatcherService));
    private readonly object _gate = new();
    private DeviceWatcher? _watcher;
    private bool _started;

    public event EventHandler? DevicesChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _watcher = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
            _watcher.Added += OnAdded;
            _watcher.Updated += OnUpdated;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += OnEnumerationCompleted;
            _watcher.Stopped += OnStopped;
            _watcher.Start();
            _started = true;
            _log.Information("Audio output device watcher started");
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation args)
    {
        _log.Information("Audio output device added: {Name}, Id={Id}", args.Name, args.Id);
        RaiseDevicesChanged();
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        _log.Debug("Audio output device updated: {Id}", args.Id);
        RaiseDevicesChanged();
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        _log.Information("Audio output device removed: {Id}", args.Id);
        RaiseDevicesChanged();
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        RaiseDevicesChanged();
    }

    private void OnStopped(DeviceWatcher sender, object args)
    {
        _log.Debug("Audio output device watcher stopped");
    }

    private void RaiseDevicesChanged()
    {
        try
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Audio output device watcher handler failed");
        }
    }

    public void Dispose()
    {
        DeviceWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
            _started = false;
        }

        if (watcher == null)
        {
            return;
        }

        watcher.Added -= OnAdded;
        watcher.Updated -= OnUpdated;
        watcher.Removed -= OnRemoved;
        watcher.EnumerationCompleted -= OnEnumerationCompleted;
        watcher.Stopped -= OnStopped;

        if (watcher.Status is not DeviceWatcherStatus.Stopped and not DeviceWatcherStatus.Aborted)
        {
            watcher.Stop();
        }

        _log.Information("Audio output device watcher disposed");
    }
}
