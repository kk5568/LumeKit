using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

public sealed class AutomationRuleEngine : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationRuleEngine));
    private readonly IAutomationTriggerBus _triggerBus;
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly AutomationPipelineCoordinator _coordinator;
    private bool _initialized;

    internal AutomationRuleEngine(
        IAutomationTriggerBus triggerBus,
        IAutomationPipelineRepository pipelineRepository,
        AutomationPipelineCoordinator coordinator)
    {
        _triggerBus = triggerBus;
        _pipelineRepository = pipelineRepository;
        _coordinator = coordinator;
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _triggerBus.TriggerPublished += OnTriggerPublished;
        _pipelineRepository.PipelinesChanged += OnPipelinesChanged;
        _log.Information(
            "Automation rule engine initialized with {PipelineCount} pipelines",
            _pipelineRepository.Pipelines.Count);
    }

    public Task ExecutePipelineNowAsync(AutomationPipeline pipeline)
        => _coordinator.ExecutePipelineNowAsync(pipeline);

    public Task ExecuteStartupPipelinesAsync()
        => _coordinator.ExecuteStartupPipelinesAsync();

    private void OnTriggerPublished(object? sender, TriggerEvent triggerEvent)
    {
        _ = HandleTriggerSafelyAsync(triggerEvent);
    }

    private async Task HandleTriggerSafelyAsync(TriggerEvent triggerEvent)
    {
        try
        {
            await _coordinator.HandleTriggerAsync(triggerEvent);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Automation trigger processing failed. TriggerType={TriggerType}, CorrelationId={CorrelationId}",
                triggerEvent.TriggerType,
                triggerEvent.CorrelationId);
        }
    }

    private void OnPipelinesChanged(object? sender, EventArgs e)
    {
        _log.Information(
            "Automation pipelines reloaded. Count={PipelineCount}",
            _pipelineRepository.Pipelines.Count);
    }

    public void Dispose()
    {
        if (!_initialized)
        {
            return;
        }

        _triggerBus.TriggerPublished -= OnTriggerPublished;
        _pipelineRepository.PipelinesChanged -= OnPipelinesChanged;
        _initialized = false;
    }
}
