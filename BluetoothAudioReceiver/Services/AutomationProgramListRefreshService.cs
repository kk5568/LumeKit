using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

public sealed class AutomationProgramListRefreshService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationProgramListRefreshService));

    private readonly IAppSettingsService _settingsService;
    private readonly object _syncRoot = new();
    private Timer? _dueTimer;
    private Timer? _dailyTimer;
    private bool _started;

    public AutomationProgramListRefreshService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Start()
    {
        lock (_syncRoot)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _dueTimer = new Timer(OnDueTimerTick, null, GetDelayUntilNextRun(), Timeout.InfiniteTimeSpan);
            _dailyTimer = new Timer(OnDailyTimerTick, null, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        }

        _ = EnsureTodayScannedAsync();
    }

    private void OnDueTimerTick(object? state)
    {
        _ = EnsureTodayScannedAsync();
    }

    private void OnDailyTimerTick(object? state)
    {
        _ = EnsureTodayScannedAsync();
    }

    private async Task EnsureTodayScannedAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (DateOnly.TryParse(_settingsService.AutomationProgramListLastScanDate, out var lastScannedDate) &&
                lastScannedDate >= today)
            {
                return;
            }

            var serializedLists = AutomationInstalledProgramScanner.BuildSerializedInstalledProgramLists(
                _settingsService.BackgroundEfficiencyProcessBlacklist);
            await _settingsService.SetAutomationGameListAsync(serializedLists);
            await _settingsService.SetAutomationProgramListLastScanDateAsync(today.ToString("yyyy-MM-dd"));
            _log.Information("Automation program lists refreshed on {ScanDate}", today);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to refresh automation program lists");
        }
    }

    private static TimeSpan GetDelayUntilNextRun()
    {
        var now = DateTime.Now;
        var tomorrow = now.Date.AddDays(1);
        return tomorrow - now;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _dueTimer?.Dispose();
            _dailyTimer?.Dispose();
            _dueTimer = null;
            _dailyTimer = null;
            _started = false;
        }
    }
}
