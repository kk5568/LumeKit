using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationSunObservationSource : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationSunObservationSource));

    private readonly IAutomationTriggerBus _triggerBus;
    private readonly Func<bool> _isLocked;
    private readonly Func<CancellationToken, Task> _publishCurrentStateAsync;

    private readonly SemaphoreSlim _sunLoopGate = new(1, 1);
    private DateOnly? _lastPublishedSunriseDate;
    private DateOnly? _lastPublishedSunsetDate;

    public AutomationSunObservationSource(
        IAutomationTriggerBus triggerBus,
        Func<bool> isLocked,
        Func<CancellationToken, Task> publishCurrentStateAsync)
    {
        _triggerBus = triggerBus;
        _isLocked = isLocked;
        _publishCurrentStateAsync = publishCurrentStateAsync;
    }

    public async Task RunSunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_isLocked())
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                    continue;
                }

                var now = DateTimeOffset.Now;
                var next = new DateTimeOffset(
                    now.Year,
                    now.Month,
                    now.Day,
                    now.Hour,
                    now.Minute,
                    0,
                    now.Offset).AddMinutes(1);

                var delay = next - now;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token);
                }

                await RefreshSunScheduleAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Automation sun schedule loop failed");
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
    }

    private async Task RefreshSunScheduleAsync(CancellationToken token)
    {
        await _sunLoopGate.WaitAsync(token);
        try
        {
            await _publishCurrentStateAsync(token);
        }
        finally
        {
            _sunLoopGate.Release();
        }
    }

    public void PublishSunEvents(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        Guid correlationId)
    {
        var localDate = DateOnly.FromDateTime(current.CapturedAt.LocalDateTime.Date);
        var currentTime = current.CapturedAt.LocalDateTime.TimeOfDay;

        if (current.Sunrise is TimeSpan sunrise &&
            currentTime >= sunrise &&
            currentTime < sunrise.Add(TimeSpan.FromMinutes(1)) &&
            _lastPublishedSunriseDate != localDate)
        {
            _lastPublishedSunriseDate = localDate;
            _triggerBus.Publish(new TriggerEvent(
                AutomationTriggerCatalog.SunriseReached,
                current.CapturedAt,
                new Dictionary<string, string?>
                {
                    ["sunrise"] = sunrise.ToString(@"hh\:mm"),
                    ["source"] = current.SunTimeSource
                },
                correlationId));
        }

        if (current.Sunset is TimeSpan sunset &&
            currentTime >= sunset &&
            currentTime < sunset.Add(TimeSpan.FromMinutes(1)) &&
            _lastPublishedSunsetDate != localDate)
        {
            _lastPublishedSunsetDate = localDate;
            _triggerBus.Publish(new TriggerEvent(
                AutomationTriggerCatalog.SunsetReached,
                current.CapturedAt,
                new Dictionary<string, string?>
                {
                    ["sunset"] = sunset.ToString(@"hh\:mm"),
                    ["source"] = current.SunTimeSource
                },
                correlationId));
        }

        if (previous.Sunrise == current.Sunrise && previous.Sunset == current.Sunset)
        {
            return;
        }

        _log.Information(
            "Automation sun schedule refreshed. Sunrise={Sunrise}, Sunset={Sunset}, Source={Source}",
            current.Sunrise,
            current.Sunset,
            current.SunTimeSource);
    }

    public void Dispose()
    {
        _sunLoopGate.Dispose();
    }
}
