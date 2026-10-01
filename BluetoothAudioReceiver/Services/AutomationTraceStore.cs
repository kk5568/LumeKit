using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.Core.Helpers;
using BluetoothAudioReceiver.Infrastructure;
using BluetoothAudioReceiver.Infrastructure.Helpers;
using Serilog;
using System.Text;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationTraceStore : IAutomationTraceStore
{
    private const string TraceFileName = "automation-execution-traces.json";
    private const string TraceLogFileName = "automation-execution-traces.jsonl";
    private const int MaxTraceCount = 500;

    private readonly ILocalSettingsService _localSettingsService;
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly object _pendingPersistSync = new();
    private readonly string _traceLogPath;
    private readonly string _traceFilePath;
    private List<AutomationExecutionTrace>? _cache;
    private bool _cacheLoadFailed;
    private bool _cacheMissing;
    private bool _corruptFileBackedUp;
    private Task _lastPersistTask = Task.CompletedTask;
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationTraceStore));

    public AutomationTraceStore(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _traceLogPath = Path.Combine(LocalSettingsHelper.ApplicationDataPath, Constants.SettingsFolder, TraceLogFileName);
        _traceFilePath = Path.Combine(LocalSettingsHelper.ApplicationDataPath, Constants.SettingsFolder, TraceFileName);
    }

    public event EventHandler<AutomationExecutionTrace>? TraceAppended;

    public IReadOnlyList<AutomationExecutionTrace> GetRecent(int maxCount = 200)
    {
        lock (_syncRoot)
        {
            var traces = EnsureLoaded();
            return traces
                .OrderByDescending(item => item.Timestamp)
                .Take(Math.Max(0, maxCount))
                .ToArray();
        }
    }

    public IReadOnlyList<AutomationExecutionTrace> GetExecution(Guid executionId)
    {
        lock (_syncRoot)
        {
            return EnsureLoaded()
                .Where(item => item.ExecutionId == executionId)
                .OrderBy(item => item.Timestamp)
                .ThenBy(item => item.StepIndex ?? int.MaxValue)
                .ToArray();
        }
    }

    public string GetLogFilePath() => _traceLogPath;

    public void Append(AutomationExecutionTrace trace)
    {
        List<AutomationExecutionTrace> snapshot;
        lock (_syncRoot)
        {
            var traces = EnsureLoaded();
            traces.Add(trace);
            if (traces.Count > MaxTraceCount)
            {
                traces.RemoveRange(0, traces.Count - MaxTraceCount);
            }

            snapshot = traces.ToList();
        }

        lock (_pendingPersistSync)
        {
            _lastPersistTask = PersistAsync(snapshot, trace);
        }
        NotifyTraceAppended(trace);
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _cache = [];
        }

        lock (_pendingPersistSync)
        {
            _lastPersistTask = ClearPersistedAsync();
        }
    }

    public async Task FlushAsync()
    {
        Task pending;
        lock (_pendingPersistSync)
        {
            pending = _lastPersistTask;
        }

        await pending.ConfigureAwait(false);
    }

    private List<AutomationExecutionTrace> EnsureLoaded()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_traceFilePath))
        {
            _cache = [];
            _cacheLoadFailed = false;
            _cacheMissing = true;
            return _cache;
        }

        try
        {
            _cache = _localSettingsService.ReadJsonFileOrThrow<List<AutomationExecutionTrace>>(TraceFileName) ?? [];
            _cacheLoadFailed = false;
            _cacheMissing = false;
        }
        catch (Exception ex)
        {
            _cacheLoadFailed = true;
            _cacheMissing = false;
            _cache ??= [];
            _log.Warning(ex, "Failed to load automation execution traces from settings. Keeping in-memory cache only.");
        }

        return _cache;
    }

    private void NotifyTraceAppended(AutomationExecutionTrace trace)
    {
        var handlers = TraceAppended;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<AutomationExecutionTrace> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, trace);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Automation trace subscriber failed. PipelineId={PipelineId}", trace.PipelineId);
            }
        }
    }

    private void AppendJsonLine(AutomationExecutionTrace trace)
    {
        EnsureLogDirectory();
        File.AppendAllText(
            _traceLogPath,
            $"{JsonHelper.Stringify(trace)}{Environment.NewLine}",
            Encoding.UTF8);
    }

    private async Task PersistAsync(
        IReadOnlyList<AutomationExecutionTrace> snapshot,
        AutomationExecutionTrace trace)
    {
        await _persistGate.WaitAsync();
        try
        {
            if (_cacheLoadFailed && !_cacheMissing)
            {
                BackupCorruptTraceFile();
            }

            var saved = await _localSettingsService.SaveJsonFileAsync(TraceFileName, snapshot);
            if (!saved)
            {
                _log.Warning(
                    "Failed to persist automation execution trace cache. ExecutionId={ExecutionId}, CorrelationId={CorrelationId}",
                    trace.ExecutionId,
                    trace.CorrelationId);
            }
            else
            {
                _cacheLoadFailed = false;
                _cacheMissing = false;
            }

            AppendJsonLine(trace);
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to persist automation execution trace. ExecutionId={ExecutionId}, CorrelationId={CorrelationId}",
                trace.ExecutionId,
                trace.CorrelationId);
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private async Task ClearPersistedAsync()
    {
        await _persistGate.WaitAsync();
        try
        {
            if (_cacheLoadFailed && !_cacheMissing)
            {
                BackupCorruptTraceFile();
            }

            var saved = await _localSettingsService.SaveJsonFileAsync(TraceFileName, new List<AutomationExecutionTrace>());
            if (!saved)
            {
                _log.Warning("Failed to clear automation execution trace cache.");
            }
            else
            {
                _cacheLoadFailed = false;
                _cacheMissing = false;
            }

            EnsureLogDirectory();
            File.WriteAllText(_traceLogPath, string.Empty, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to clear automation execution traces.");
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private void EnsureLogDirectory()
    {
        var dir = Path.GetDirectoryName(_traceLogPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private void BackupCorruptTraceFile()
    {
        if (_corruptFileBackedUp || !File.Exists(_traceFilePath))
        {
            return;
        }

        try
        {
            var backupPath = $"{_traceFilePath}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmssfff}";
            File.Copy(_traceFilePath, backupPath, overwrite: false);
            _corruptFileBackedUp = true;
            _log.Warning(
                "Backed up corrupt automation execution trace file before recovery. BackupPath={BackupPath}",
                backupPath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to back up corrupt automation execution trace file. Path={TraceFilePath}", _traceFilePath);
        }
    }
}
