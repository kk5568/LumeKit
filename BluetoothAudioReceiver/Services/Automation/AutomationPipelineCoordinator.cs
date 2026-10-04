using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationPipelineCoordinator
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationPipelineCoordinator));
    private readonly IAutomationTriggerRegistry _triggerRegistry;
    private readonly IAutomationSystemContextHub _systemContextHub;
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly IAppSettingsService _settingsService;
    private readonly AutomationTriggerMatcher _triggerMatcher;
    private readonly AutomationRuleDecisionService _decisionService;
    private readonly AutomationPipelineExecutor _pipelineExecutor;
    private readonly Dictionary<Guid, DateTimeOffset> _lastRunAt = [];
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    public AutomationPipelineCoordinator(
        IAutomationTriggerRegistry triggerRegistry,
        IAutomationSystemContextHub systemContextHub,
        IAutomationPipelineRepository pipelineRepository,
        IAppSettingsService settingsService,
        AutomationTriggerMatcher triggerMatcher,
        AutomationRuleDecisionService decisionService,
        AutomationPipelineExecutor pipelineExecutor)
    {
        _triggerRegistry = triggerRegistry;
        _systemContextHub = systemContextHub;
        _pipelineRepository = pipelineRepository;
        _settingsService = settingsService;
        _triggerMatcher = triggerMatcher;
        _decisionService = decisionService;
        _pipelineExecutor = pipelineExecutor;
    }

    public async Task ExecutePipelineNowAsync(AutomationPipeline pipeline)
    {
        var triggerType = _triggerRegistry.Normalize(pipeline.Trigger.TriggerType);
        var payload = pipeline.Trigger.Parameters.ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        if (string.Equals(triggerType, AutomationSupportCatalog.TriggerPowerStateChanged, StringComparison.OrdinalIgnoreCase) &&
            !payload.ContainsKey("isOnBattery"))
        {
            payload["isOnBattery"] = pipeline.Trigger.Parameters.TryGetValue("mode", out var mode) &&
                mode == "ac-disconnected" ? bool.TrueString : bool.FalseString;
        }

        var triggerEvent = new TriggerEvent(triggerType, DateTimeOffset.Now, payload, Guid.NewGuid());
        if (ShouldSkip(pipeline, triggerEvent, out var optionResult, out var optionMessage))
        {
            _log.Information("Automation direct test skipped. Result={Result}, Message={Message}", optionResult, optionMessage);
            return;
        }

        if (!_decisionService.ValidateActions(pipeline, out var validationMessage))
        {
            _log.Warning("Automation direct test rejected. {Message}", validationMessage);
            return;
        }

        var context = CreateExecutionContext(pipeline, triggerEvent);
        _log.Information(
            "Automation direct test execution started. Pipeline={PipelineName}, StepCount={StepCount}, CorrelationId={CorrelationId}",
            pipeline.Name,
            pipeline.Steps.Count,
            triggerEvent.CorrelationId);
        _lastRunAt[pipeline.Id] = triggerEvent.OccurredAt;
        await _pipelineExecutor.ExecuteAsync(pipeline, triggerEvent, context);
    }

    public async Task ExecuteStartupPipelinesAsync()
    {
        var triggerEvent = new TriggerEvent(
            "startup",
            DateTimeOffset.Now,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            Guid.NewGuid());
        var blockedScopes = AutomationActionResourceScope.None;

        foreach (var pipeline in GetPipelinesInSceneOrder().Where(item => item.IsEnabled && item.Options.RunOnStartupEnabled))
        {
            if (!_decisionService.EvaluateConditions(pipeline.Conditions, triggerEvent, _systemContextHub.Current))
            {
                _log.Information("Startup automation skipped because conditions were not met. Pipeline={PipelineName}", pipeline.Name);
                continue;
            }

            if (ShouldSkip(pipeline, triggerEvent, out var optionResult, out var optionMessage))
            {
                _log.Information("Startup automation skipped. Pipeline={PipelineName}, Result={Result}, Message={Message}", pipeline.Name, optionResult, optionMessage);
                continue;
            }

            if (!_decisionService.ValidateActions(pipeline, out var validationMessage))
            {
                _log.Warning("Startup automation rejected. Pipeline={PipelineName}, Message={Message}", pipeline.Name, validationMessage);
                continue;
            }

            var scope = _decisionService.GetResourceScope(pipeline);
            if (IsBlocked(scope, blockedScopes))
            {
                _log.Information("Startup automation blocked by priority. Pipeline={PipelineName}, BlockedScopes={BlockedScopes}", pipeline.Name, blockedScopes);
                continue;
            }

            var context = CreateExecutionContext(pipeline, triggerEvent);
            _lastRunAt[pipeline.Id] = triggerEvent.OccurredAt;
            var result = await _pipelineExecutor.ExecuteAsync(pipeline, triggerEvent, context);
            blockedScopes |= result.BlockedResourceScope;
        }
    }

    public Task HandleTriggerAsync(TriggerEvent triggerEvent)
        => HandleTriggerCoreAsync(triggerEvent);

    private async Task HandleTriggerCoreAsync(TriggerEvent triggerEvent)
    {
        var triggerType = _triggerRegistry.Normalize(triggerEvent.TriggerType);
        var normalizedEvent = triggerType == triggerEvent.TriggerType
            ? triggerEvent
            : triggerEvent with { TriggerType = triggerType };
        var blockedScopes = AutomationActionResourceScope.None;

        _log.Information(
            "Automation trigger received. TriggerType={TriggerType}, CorrelationId={CorrelationId}, PayloadKeys={PayloadKeys}",
            normalizedEvent.TriggerType,
            normalizedEvent.CorrelationId,
            string.Join(",", normalizedEvent.Payload.Keys));

        foreach (var pipeline in GetPipelinesInSceneOrder())
        {
            var pipelineTriggerType = _triggerRegistry.Normalize(pipeline.Trigger.TriggerType);
            if (!_triggerRegistry.IsSupported(pipelineTriggerType) || !pipeline.IsEnabled)
            {
                continue;
            }

            if (!_triggerMatcher.Matches(pipeline.Trigger, normalizedEvent))
            {
                continue;
            }

            if (!_decisionService.EvaluateConditions(pipeline.Conditions, normalizedEvent, _systemContextHub.Current))
            {
                _log.Information("Automation skipped because conditions were not met. Pipeline={PipelineName}", pipeline.Name);
                continue;
            }

            if (ShouldSkip(pipeline, normalizedEvent, out var optionResult, out var optionMessage))
            {
                _log.Information("Automation skipped. Pipeline={PipelineName}, Result={Result}, Message={Message}", pipeline.Name, optionResult, optionMessage);
                continue;
            }

            if (!_decisionService.ValidateActions(pipeline, out var validationMessage))
            {
                _log.Warning("Automation rejected. Pipeline={PipelineName}, Message={Message}", pipeline.Name, validationMessage);
                continue;
            }

            var scope = _decisionService.GetResourceScope(pipeline);
            if (IsBlocked(scope, blockedScopes))
            {
                _log.Information("Automation blocked by priority. Pipeline={PipelineName}, BlockedScopes={BlockedScopes}", pipeline.Name, blockedScopes);
                continue;
            }

            var context = CreateExecutionContext(pipeline, normalizedEvent);
            _lastRunAt[pipeline.Id] = normalizedEvent.OccurredAt;
            var result = await _pipelineExecutor.ExecuteAsync(pipeline, normalizedEvent, context);
            blockedScopes |= result.BlockedResourceScope;
        }
    }

    private bool ShouldSkip(
        AutomationPipeline pipeline,
        TriggerEvent triggerEvent,
        out string result,
        out string message)
        => _decisionService.ShouldSkip(pipeline, triggerEvent.OccurredAt, _startedAt, _lastRunAt, out result, out message);

    private IEnumerable<AutomationPipeline> GetPipelinesInSceneOrder()
    {
        var sceneOrder = (_settingsService.AutomationSceneList ?? string.Empty)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((name, index) => new { Name = name, Index = index })
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

        return _pipelineRepository.Pipelines
            .Select((pipeline, index) => new { Pipeline = pipeline, Index = index })
            .OrderBy(item => sceneOrder.TryGetValue(GetSceneName(item.Pipeline), out var index) ? index : int.MaxValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Pipeline);
    }

    private static AutomationExecutionContext CreateExecutionContext(
        AutomationPipeline pipeline,
        TriggerEvent triggerEvent)
        => new(
            Guid.NewGuid(),
            pipeline.Id,
            pipeline.Name,
            triggerEvent.TriggerType,
            triggerEvent.OccurredAt,
            triggerEvent.CorrelationId);

    private static bool IsBlocked(AutomationActionResourceScope scope, AutomationActionResourceScope blockedScopes)
        => scope != AutomationActionResourceScope.None &&
           (blockedScopes & scope) != AutomationActionResourceScope.None;

    private static string GetSceneName(AutomationPipeline pipeline)
        => string.IsNullOrWhiteSpace(pipeline.Category) ? "基础" : pipeline.Category.Trim();

}
