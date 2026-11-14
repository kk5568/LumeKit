using System.Diagnostics;
using System.Text.RegularExpressions;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Windows.System.Power;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 观察层：维护系统动态上下文、做进程事件去重，并按节拍采样前台性能事实。
/// 这里不读任何自动化规则，也不判断"算不算游戏"——那是决策层的事。
/// </summary>
internal sealed class AutomationObservationStateProcessor : IDisposable
{
    private static readonly TimeSpan ProcessDedupWindow = TimeSpan.FromSeconds(15);

    private readonly IAutomationSystemContextHub _systemContextHub;
    private readonly Func<string?> _getForegroundProcessName;
    private readonly Func<int?> _getForegroundProcessId;
    private readonly Func<bool> _isForegroundFullscreen;
    private readonly object _syncRoot = new();
    private readonly object _gpuSyncRoot = new();
    private readonly SystemPerformanceMetricsReader _systemPerformanceMetricsReader = new();
    private readonly Dictionary<string, DateTimeOffset> _recentProcessStarts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new(StringComparer.OrdinalIgnoreCase);

    public AutomationObservationStateProcessor(
        IAutomationSystemContextHub systemContextHub,
        Func<string?> getForegroundProcessName,
        Func<int?> getForegroundProcessId,
        Func<bool> isForegroundFullscreen)
    {
        _systemContextHub = systemContextHub;
        _getForegroundProcessName = getForegroundProcessName;
        _getForegroundProcessId = getForegroundProcessId;
        _isForegroundFullscreen = isForegroundFullscreen;
    }

    public Task ProcessAsync(AutomationDynamicContext current, CancellationToken cancellationToken)
        => _systemContextHub.UpdateDynamicAsync(current, cancellationToken);

    public bool ShouldSkipDuplicateProcessStart(string processName, DateTimeOffset occurredAt)
    {
        var key = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_syncRoot)
        {
            foreach (var staleKey in _recentProcessStarts
                         .Where(item => occurredAt - item.Value > ProcessDedupWindow)
                         .Select(item => item.Key)
                         .ToArray())
            {
                _recentProcessStarts.Remove(staleKey);
            }

            if (_recentProcessStarts.TryGetValue(key, out var previous) &&
                occurredAt - previous < ProcessDedupWindow)
            {
                return true;
            }

            _recentProcessStarts[key] = occurredAt;
            return false;
        }
    }

    /// <summary>
    /// 采样当前前台性能事实。调用方（决策层）决定是否需要、以及如何判断。
    /// </summary>
    public PerformanceSample CapturePerformanceSample()
    {
        var processName = NormalizeProcessName(_getForegroundProcessName());
        var processId = _getForegroundProcessId();
        var fullscreen = _isForegroundFullscreen();
        var gpu = processId is int pid ? GetGpuUsagePercent(pid) : null;
        var systemMetrics = _systemPerformanceMetricsReader.Capture();
        return new PerformanceSample(
            processName,
            processId,
            fullscreen,
            gpu,
            DateTimeOffset.Now,
            systemMetrics.CpuUsagePercent,
            systemMetrics.GpuUsagePercent,
            IsOnBattery());
    }

    private static bool? IsOnBattery()
    {
        try
        {
            return PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;
        }
        catch
        {
            return null;
        }
    }

    public static string? NormalizeProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(value.Trim());
        return string.IsNullOrWhiteSpace(name) ? value.Trim() : name;
    }

    private float? GetGpuUsagePercent(int processId)
    {
        try
        {
            lock (_gpuSyncRoot)
            {
                var category = new PerformanceCounterCategory("GPU Engine");
                var instances = category.GetInstanceNames()
                    .Where(item => item.Contains($"pid_{processId}_", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var stale in _gpuCounters.Keys.Except(instances, StringComparer.OrdinalIgnoreCase).ToArray())
                {
                    _gpuCounters[stale].Dispose();
                    _gpuCounters.Remove(stale);
                }

                var total = 0f;
                var sampled = false;
                foreach (var instance in instances)
                {
                    if (!_gpuCounters.TryGetValue(instance, out var counter))
                    {
                        counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                        _gpuCounters[instance] = counter;
                        counter.NextValue();
                        continue;
                    }

                    total += counter.NextValue();
                    sampled = true;
                }

                return sampled ? total : null;
            }
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _systemPerformanceMetricsReader.Dispose();
        lock (_gpuSyncRoot)
        {
            foreach (var counter in _gpuCounters.Values)
            {
                counter.Dispose();
            }

            _gpuCounters.Clear();
        }
    }
}

internal readonly record struct SystemPerformanceMetrics(float? CpuUsagePercent, float? GpuUsagePercent);

internal sealed class SystemPerformanceMetricsReader : IDisposable
{
    private static readonly Regex ThreeDimensionalEnginePattern = new(
        @"_luid_(?<luid>.+?)_phys_(?<physical>\d+)_eng_(?<engine>\d+)_engtype_3D$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new(StringComparer.OrdinalIgnoreCase);
    private PerformanceCounter? _cpuCounter;
    private bool _cpuPrimed;

    public SystemPerformanceMetrics Capture()
        => new(CaptureCpuUsagePercent(), CaptureGpuUsagePercent());

    private float? CaptureCpuUsagePercent()
    {
        try
        {
            if (_cpuCounter is null)
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                _cpuCounter.NextValue();
                _cpuPrimed = true;
                return null;
            }

            if (!_cpuPrimed)
            {
                _cpuCounter.NextValue();
                _cpuPrimed = true;
                return null;
            }

            return Math.Clamp(_cpuCounter.NextValue(), 0f, 100f);
        }
        catch
        {
            return null;
        }
    }

    private float? CaptureGpuUsagePercent()
    {
        lock (_syncRoot)
        {
            try
            {
                var instances = new PerformanceCounterCategory("GPU Engine")
                    .GetInstanceNames()
                    .Where(name => name.Contains("_engtype_3D", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var stale in _gpuCounters.Keys.Except(instances, StringComparer.OrdinalIgnoreCase).ToArray())
                {
                    _gpuCounters[stale].Dispose();
                    _gpuCounters.Remove(stale);
                }

                var addedCounter = false;
                var engineTotals = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                foreach (var instance in instances)
                {
                    if (!_gpuCounters.TryGetValue(instance, out var counter))
                    {
                        counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                        counter.NextValue();
                        _gpuCounters[instance] = counter;
                        addedCounter = true;
                        continue;
                    }

                    var match = ThreeDimensionalEnginePattern.Match(instance);
                    var engineKey = match.Success
                        ? $"{match.Groups["luid"].Value}:{match.Groups["physical"].Value}:{match.Groups["engine"].Value}"
                        : "3d-engine";
                    engineTotals[engineKey] = engineTotals.GetValueOrDefault(engineKey) + counter.NextValue();
                }

                if (addedCounter || engineTotals.Count == 0)
                {
                    return null;
                }

                return Math.Clamp(engineTotals.Values.Max(), 0f, 100f);
            }
            catch
            {
                return null;
            }
        }
    }

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _cpuCounter = null;

        lock (_syncRoot)
        {
            foreach (var counter in _gpuCounters.Values)
            {
                counter.Dispose();
            }

            _gpuCounters.Clear();
        }
    }
}
