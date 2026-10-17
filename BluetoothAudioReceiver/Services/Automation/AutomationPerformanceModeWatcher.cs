using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 自动化侧的性能模式入口：向调度中心订阅采样节拍，再交给判定服务决定算不算游戏。
/// 只有在存在启用中的性能模式规则时才订阅，没有规则就不占用采样节拍。
/// </summary>
internal sealed class AutomationPerformanceModeWatcher : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationPerformanceModeWatcher));

    private readonly AutomationGameModeDecisionService _decisionService;
    private readonly PerformanceObservationSampler _sampler;
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly object _syncRoot = new();
    private IDisposable? _subscription;
    private bool _disposed;

    public AutomationPerformanceModeWatcher(
        AutomationGameModeDecisionService decisionService,
        PerformanceObservationSampler sampler,
        IAutomationPipelineRepository pipelineRepository)
    {
        _decisionService = decisionService;
        _sampler = sampler;
        _pipelineRepository = pipelineRepository;
        _pipelineRepository.PipelinesChanged += OnPipelinesChanged;
        UpdateSubscription();
    }

    private void OnPipelinesChanged(object? sender, EventArgs e) => UpdateSubscription();

    private void UpdateSubscription()
    {
        var needed = HasPerformanceModeRule();

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            if (needed && _subscription is null)
            {
                _subscription = _sampler.Subscribe("automation.performance-mode", OnTick);
                _log.Information("Performance mode subscription activated because a performance rule exists");
            }
            else if (!needed && _subscription is not null)
            {
                _subscription.Dispose();
                _subscription = null;
                _log.Information("Performance mode subscription released because no performance rule remains");
            }
        }
    }

    private bool HasPerformanceModeRule()
    {
        foreach (var pipeline in _pipelineRepository.Pipelines)
        {
            if (!pipeline.IsEnabled || pipeline.Trigger is null)
            {
                continue;
            }

            var triggerType = pipeline.Trigger.TriggerType;
            if (string.Equals(triggerType, AutomationTriggerCatalog.PerformanceModeEntered, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(triggerType, AutomationTriggerCatalog.PerformanceModeExited, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool OnTick(Func<PerformanceSample> capture) => _decisionService.EvaluateAndReportActive(capture);

    public void Dispose()
    {
        IDisposable? subscription;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subscription = _subscription;
            _subscription = null;
        }

        _pipelineRepository.PipelinesChanged -= OnPipelinesChanged;
        subscription?.Dispose();
    }
}
