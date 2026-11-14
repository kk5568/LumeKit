using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class DynamicPerformanceService : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(DynamicPerformanceService));
    private static readonly TimeSpan UpShiftConfirmation = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DownShiftConfirmation = TimeSpan.FromSeconds(15);

    private readonly PerformanceObservationSampler _sampler;
    private readonly IGHelperPerformanceModeController _modeController;
    private IDisposable? _subscription;
    private PerformanceStage? _currentStage;
    private PerformanceStage? _pendingStage;
    private DateTimeOffset? _pendingSince;
    private bool? _lastIsOnBattery;
    private bool _disposed;

    public DynamicPerformanceService(
        PerformanceObservationSampler sampler,
        IGHelperPerformanceModeController modeController)
    {
        _sampler = sampler;
        _modeController = modeController;
    }

    public event EventHandler<DynamicPerformanceChangedEventArgs>? StageChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _subscription ??= _sampler.Subscribe("dynamic-performance", OnTickAsync);
    }

    private async Task<bool> OnTickAsync(Func<PerformanceSample> capture, CancellationToken cancellationToken)
    {
        var sample = capture();
        if (sample.SystemCpuUsagePercent is not float cpuUsage ||
            sample.SystemGpuUsagePercent is not float gpuUsage ||
            sample.IsOnBattery is not bool isOnBattery)
        {
            _log.Debug(
                "Dynamic performance waiting for complete system metrics. Cpu={CpuUsage}, Gpu={GpuUsage}, IsOnBattery={IsOnBattery}",
                sample.SystemCpuUsagePercent,
                sample.SystemGpuUsagePercent,
                sample.IsOnBattery);
            return _currentStage.HasValue;
        }

        var load = Math.Max(cpuUsage, gpuUsage);
        var now = sample.CapturedAt;
        if (!_currentStage.HasValue || _lastIsOnBattery != isOnBattery)
        {
            var initialStage = ResolveInitialStage(isOnBattery, load);
            await ApplyStageAsync(initialStage, isOnBattery, cpuUsage, gpuUsage, now, cancellationToken);
            return _currentStage.HasValue;
        }

        var targetStage = ResolveNextStage(_currentStage.Value, load);
        if (!targetStage.HasValue)
        {
            _pendingStage = null;
            _pendingSince = null;
            return true;
        }

        if (_pendingStage != targetStage)
        {
            _pendingStage = targetStage;
            _pendingSince = now;
            return true;
        }

        var confirmation = IsUpShift(_currentStage.Value, targetStage.Value)
            ? UpShiftConfirmation
            : DownShiftConfirmation;
        if (_pendingSince is not DateTimeOffset pendingSince || now - pendingSince < confirmation)
        {
            return true;
        }

        await ApplyStageAsync(targetStage.Value, isOnBattery, cpuUsage, gpuUsage, now, cancellationToken);
        return true;
    }

    private async Task ApplyStageAsync(
        PerformanceStage targetStage,
        bool isOnBattery,
        float cpuUsage,
        float gpuUsage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var oldStage = _currentStage;
        var targetMode = GetMode(targetStage);
        if (!oldStage.HasValue || GetMode(oldStage.Value) != targetMode)
        {
            bool success;
            try
            {
                success = await _modeController.SetModeAsync(targetMode, cancellationToken);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Dynamic performance mode switch failed. Stage={Stage}, Mode={Mode}", targetStage, targetMode);
                return;
            }

            if (!success)
            {
                _log.Warning("Dynamic performance mode switch was rejected. Stage={Stage}, Mode={Mode}", targetStage, targetMode);
                return;
            }
        }

        _currentStage = targetStage;
        _lastIsOnBattery = isOnBattery;
        _pendingStage = null;
        _pendingSince = null;

        if (oldStage != targetStage)
        {
            _log.Information(
                "Dynamic performance stage changed. PreviousStage={PreviousStage}, Stage={Stage}, Mode={Mode}, PowerSource={PowerSource}, CpuUsage={CpuUsage}, GpuUsage={GpuUsage}",
                oldStage?.ToString(),
                targetStage,
                targetMode,
                isOnBattery ? "battery" : "ac",
                cpuUsage,
                gpuUsage);
            try
            {
                StageChanged?.Invoke(
                    this,
                    new DynamicPerformanceChangedEventArgs(
                        targetStage.ToString(),
                        targetMode,
                        isOnBattery,
                        cpuUsage,
                        gpuUsage,
                        occurredAt));
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Dynamic performance stage-change subscriber failed.");
            }
        }
    }

    private static PerformanceStage ResolveInitialStage(bool isOnBattery, float load)
    {
        if (isOnBattery)
        {
            return load >= 45f ? PerformanceStage.BatteryBalanced : PerformanceStage.BatteryQuiet;
        }

        return load >= 75f
            ? PerformanceStage.AcEnhanced
            : load >= 35f
                ? PerformanceStage.AcBalanced
                : PerformanceStage.AcQuiet;
    }

    private static PerformanceStage? ResolveNextStage(PerformanceStage current, float load)
        => current switch
        {
            PerformanceStage.AcQuiet when load >= 35f => PerformanceStage.AcBalanced,
            PerformanceStage.AcBalanced when load >= 75f => PerformanceStage.AcEnhanced,
            PerformanceStage.AcBalanced when load <= 25f => PerformanceStage.AcQuiet,
            PerformanceStage.AcEnhanced when load <= 60f => PerformanceStage.AcBalanced,
            PerformanceStage.BatteryQuiet when load >= 45f => PerformanceStage.BatteryBalanced,
            PerformanceStage.BatteryBalanced when load <= 30f => PerformanceStage.BatteryQuiet,
            _ => null
        };

    private static bool IsUpShift(PerformanceStage current, PerformanceStage target)
        => (int)GetMode(target) > (int)GetMode(current);

    private static GHelperPerformanceMode GetMode(PerformanceStage stage)
        => stage switch
        {
            PerformanceStage.AcQuiet or PerformanceStage.BatteryQuiet => GHelperPerformanceMode.Quiet,
            PerformanceStage.AcBalanced or PerformanceStage.BatteryBalanced => GHelperPerformanceMode.Balanced,
            PerformanceStage.AcEnhanced => GHelperPerformanceMode.Enhanced,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported dynamic performance stage.")
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
    }

    private enum PerformanceStage
    {
        AcQuiet,
        AcBalanced,
        AcEnhanced,
        BatteryQuiet,
        BatteryBalanced
    }
}

internal sealed class DynamicPerformanceChangedEventArgs : EventArgs
{
    public DynamicPerformanceChangedEventArgs(
        string stage,
        GHelperPerformanceMode mode,
        bool isOnBattery,
        float cpuUsagePercent,
        float gpuUsagePercent,
        DateTimeOffset occurredAt)
    {
        Stage = stage;
        Mode = mode;
        IsOnBattery = isOnBattery;
        CpuUsagePercent = cpuUsagePercent;
        GpuUsagePercent = gpuUsagePercent;
        OccurredAt = occurredAt;
    }

    public string Stage { get; }
    public GHelperPerformanceMode Mode { get; }
    public bool IsOnBattery { get; }
    public float CpuUsagePercent { get; }
    public float GpuUsagePercent { get; }
    public DateTimeOffset OccurredAt { get; }
}
