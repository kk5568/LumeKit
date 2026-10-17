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
    private readonly object _indexSync = new();
    private PipelineIndex? _index;

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
        _pipelineRepository.PipelinesChanged += OnPipelinesChanged;
    }

    private void OnPipelinesChanged(object? sender, EventArgs e)
    {
        lock (_indexSync)
        {
            _index = null;
        }
    }

    public async Task<string?> ExecutePipelineNowAsync(AutomationPipeline pipeline)
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
            return string.IsNullOrWhiteSpace(optionMessage) ? optionResult : optionMessage;
        }

        if (!_decisionService.ValidateActions(pipeline, out var validationMessage))
        {
            _log.Warning("Automation direct test rejected. {Message}", validationMessage);
            return validationMessage;
        }

        var context = CreateExecutionContext(pipeline, triggerEvent);
        _log.Information(
            "Automation direct test execution started. Pipeline={PipelineName}, StepCount={StepCount}, CorrelationId={CorrelationId}",
            pipeline.Name,
            pipeline.Steps.Count,
            triggerEvent.CorrelationId);
        _lastRunAt[pipeline.Id] = triggerEvent.OccurredAt;
        await _pipelineExecutor.ExecuteAsync(pipeline, triggerEvent, context);
        return null;
    }

    public async Task ExecuteStartupPipelinesAsync()
    {
        var triggerEvent = new TriggerEvent(
            "startup",
            DateTimeOffset.Now,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            Guid.NewGuid());
        var blockedScopes = AutomationActionResourceScope.None;

        foreach (var pipeline in GetIndex().Ordered.Where(item => item.IsEnabled && item.Options.RunOnStartupEnabled))
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

        // 只把这一类触发器下的规则拿出来比对，不再全表扫。
        foreach (var pipeline in GetIndex().GetByTrigger(normalizedEvent.TriggerType))
        {
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

    /// <summary>
    /// 排序结果和触发器索引都缓存，规则或场景顺序变了才重建。
    /// </summary>
    private PipelineIndex GetIndex()
    {
        var sceneList = _settingsService.AutomationSceneList ?? string.Empty;

        lock (_indexSync)
        {
            if (_index is not null && string.Equals(_index.SceneSignature, sceneList, StringComparison.Ordinal))
            {
                return _index;
            }
        }

        var sceneOrder = sceneList
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((name, index) => new { Name = name, Index = index })
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

        var ordered = _pipelineRepository.Pipelines
            .Select((pipeline, index) => new { Pipeline = pipeline, Index = index })
            .OrderByDescending(item => item.Pipeline.Options?.Priority ?? 0)
            .ThenBy(item => sceneOrder.TryGetValue(GetSceneName(item.Pipeline), out var sceneIndex) ? sceneIndex : int.MaxValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Pipeline)
            .ToArray();

        var byTrigger = new Dictionary<string, List<AutomationPipeline>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pipeline in ordered)
        {
            if (!pipeline.IsEnabled || pipeline.Trigger is null)
            {
                continue;
            }

            var triggerType = _triggerRegistry.Normalize(pipeline.Trigger.TriggerType);
            if (!_triggerRegistry.IsSupported(triggerType))
            {
                continue;
            }

            if (!byTrigger.TryGetValue(triggerType, out var bucket))
            {
                bucket = [];
                byTrigger[triggerType] = bucket;
            }

            bucket.Add(pipeline);
        }

        var index = new PipelineIndex(sceneList, ordered, byTrigger);
        lock (_indexSync)
        {
            _index = index;
        }

        return index;
    }

    private sealed record PipelineIndex(
        string SceneSignature,
        IReadOnlyList<AutomationPipeline> Ordered,
        IReadOnlyDictionary<string, List<AutomationPipeline>> ByTrigger)
    {
        public IEnumerable<AutomationPipeline> GetByTrigger(string triggerType)
            => ByTrigger.TryGetValue(triggerType, out var bucket) ? bucket : [];
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
