using System.Diagnostics;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;
using Windows.System.Power;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 决策层：根据用户配置的性能模式规则，判断前台程序是否进入/退出游戏（高性能）模式。
/// 观察层只负责采样（前台进程、是否全屏、GPU 占用），是否算游戏由这里决定。
/// </summary>
internal sealed class AutomationGameModeDecisionService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationGameModeDecisionService));
    private const int CandidateSamples = 20;
    private const int CandidateHits = 16;
    private static readonly TimeSpan ExitDelay = TimeSpan.FromSeconds(30);

    private readonly IAutomationTriggerBus _triggerBus;
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, PerformanceState> _performanceStates = new(StringComparer.OrdinalIgnoreCase);

    public AutomationGameModeDecisionService(
        IAutomationTriggerBus triggerBus,
        IAutomationPipelineRepository pipelineRepository)
    {
        _triggerBus = triggerBus;
        _pipelineRepository = pipelineRepository;
    }

    public void Evaluate(Func<PerformanceSample> sampleFactory)
    {
        ArgumentNullException.ThrowIfNull(sampleFactory);

        var configurations = GetPerformanceConfigurations();
        if (configurations.Count == 0)
        {
            lock (_syncRoot)
            {
                _performanceStates.Clear();
            }

            return;
        }

        var sample = sampleFactory();
        var now = DateTimeOffset.Now;

        lock (_syncRoot)
        {
            var keys = configurations.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleKey in _performanceStates.Keys.Except(keys, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                _performanceStates.Remove(staleKey);
            }

            foreach (var configuration in configurations)
            {
                if (!_performanceStates.TryGetValue(configuration.Key, out var state))
                {
                    state = new PerformanceState();
                    _performanceStates[configuration.Key] = state;
                }

                ProcessPerformanceState(
                    configuration,
                    state,
                    sample.ProcessName,
                    sample.ProcessId,
                    sample.IsFullscreen,
                    sample.GpuUsagePercent,
                    now);
            }
        }
    }

    /// <summary>
    /// 调度中心撮合一次采样：算一遍，并告诉它下一次该用快节拍还是慢节拍。
    /// </summary>
    public bool EvaluateAndReportActive(Func<PerformanceSample> sampleFactory)
    {
        Evaluate(sampleFactory);
        return IsAnyModeActive();
    }

    public bool IsAnyModeActive()
    {
        lock (_syncRoot)
        {
            return _performanceStates.Values.Any(state => !string.IsNullOrWhiteSpace(state.ActiveProcessName));
        }
    }

    public void HandleProcessStopped(string processName, DateTimeOffset occurredAt, string? processId)
    {
        lock (_syncRoot)
        {
            foreach (var item in _performanceStates)
            {
                if (!string.Equals(item.Value.ActiveProcessName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var configuration = PerformanceConfiguration.FromKey(item.Key);
                var activeId = item.Value.ActiveProcessId;
                item.Value.ClearActive();
                if (!configuration.IsDynamic)
                {
                    PublishPerformanceEvent(AutomationTriggerCatalog.PerformanceModeExited, processName, activeId, occurredAt, configuration);
                }
            }
        }
    }

    private void ProcessPerformanceState(
        PerformanceConfiguration configuration,
        PerformanceState state,
        string? processName,
        int? processId,
        bool fullscreen,
        float? gpu,
        DateTimeOffset now)
    {
        var meetsEnter = MatchesWindow(configuration.WindowMode, fullscreen) &&
                         processId.HasValue &&
                         !string.IsNullOrWhiteSpace(processName) &&
                         gpu is float usage &&
                         MatchesGpu(configuration.GpuComparison, usage, configuration.EnterThreshold);

        if (!string.IsNullOrWhiteSpace(state.ActiveProcessName))
        {
            var running = state.ActiveProcessId is int activePid
                ? IsProcessRunning(activePid)
                : IsProcessRunning(state.ActiveProcessName);
            if (!running)
            {
                var exitedName = state.ActiveProcessName;
                var exitedId = state.ActiveProcessId;
                state.ClearActive();
                PublishPerformanceEvent(AutomationTriggerCatalog.PerformanceModeExited, exitedName, exitedId, now, configuration);
                return;
            }

            var shouldExit = gpu.HasValue &&
                             ShouldExit(configuration, gpu.Value) &&
                             (state.ActiveProcessId != processId || !meetsEnter);
            if (!shouldExit)
            {
                if (gpu is float currentGpu)
                {
                    var stage = ResolveStage(currentGpu, IsOnBattery(), state.ActiveStage);
                    if (!string.Equals(stage, state.ActiveStage, StringComparison.OrdinalIgnoreCase))
                    {
                        state.ActiveStage = stage;
                        PublishPerformanceEvent(configuration.IsDynamic ? AutomationTriggerCatalog.DynamicPerformanceChanged : AutomationTriggerCatalog.PerformanceModeEntered, state.ActiveProcessName, state.ActiveProcessId, now, configuration, stage, IsOnBattery());
                    }
                }
                state.ExitSince = null;
                state.ClearCandidate();
                return;
            }

            state.ExitSince ??= now;
            if (now - state.ExitSince >= ExitDelay)
            {
                var exitedName = state.ActiveProcessName;
                var exitedId = state.ActiveProcessId;
                state.ClearActive();
                PublishPerformanceEvent(AutomationTriggerCatalog.PerformanceModeExited, exitedName, exitedId, now, configuration);
            }

            return;
        }

        if (!meetsEnter)
        {
            if (state.CandidateSince.HasValue &&
                string.Equals(state.CandidateProcessName, processName, StringComparison.OrdinalIgnoreCase) &&
                state.CandidateProcessId == processId)
            {
                state.CandidateSamples++;
                if (state.CandidateSamples >= CandidateSamples)
                {
                    state.ClearCandidate();
                }
            }

            return;
        }

        if (!string.Equals(state.CandidateProcessName, processName, StringComparison.OrdinalIgnoreCase) ||
            state.CandidateProcessId != processId)
        {
            state.CandidateProcessName = processName;
            state.CandidateProcessId = processId;
            state.CandidateSince = now;
            state.CandidateSamples = 1;
            state.CandidateHits = 1;
            return;
        }

        state.CandidateSamples++;
        state.CandidateHits++;
        if (state.CandidateSince.HasValue &&
            state.CandidateSamples >= CandidateSamples &&
            state.CandidateHits >= CandidateHits)
        {
            state.ActiveProcessName = processName;
            state.ActiveProcessId = processId;
            state.ActiveStage = ResolveStage(gpu ?? 0, IsOnBattery(), null);
            state.ClearCandidate();
            PublishPerformanceEvent(configuration.IsDynamic ? AutomationTriggerCatalog.DynamicPerformanceChanged : AutomationTriggerCatalog.PerformanceModeEntered, processName, processId, now, configuration, state.ActiveStage, IsOnBattery());
        }
    }

    private IReadOnlyList<PerformanceConfiguration> GetPerformanceConfigurations()
        => _pipelineRepository.Pipelines
            .Where(item => item.IsEnabled)
            .Select(item => AutomationTriggerCatalog.Normalize(item.Trigger.TriggerType) switch
            {
                AutomationTriggerCatalog.PerformanceModeEntered or AutomationTriggerCatalog.PerformanceModeExited
                    => PerformanceConfiguration.FromParameters(item.Trigger.Parameters, false),
                AutomationTriggerCatalog.DynamicPerformanceChanged
                    => PerformanceConfiguration.FromParameters(item.Trigger.Parameters, true),
                _ => null
            })
            .OfType<PerformanceConfiguration>()
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.First())
            .ToArray();

    private void PublishPerformanceEvent(
        string triggerType,
        string? processName,
        int? processId,
        DateTimeOffset occurredAt,
        PerformanceConfiguration configuration,
        string? stage = null,
        bool? onBattery = null)
    {
        var correlationId = Guid.NewGuid();
        _log.Information(
            "Automation performance-mode event published. TriggerType={TriggerType}, ProcessName={ProcessName}, ProcessId={ProcessId}, CorrelationId={CorrelationId}",
            triggerType,
            processName,
            processId,
            correlationId);
        _triggerBus.Publish(new TriggerEvent(
            triggerType,
            occurredAt,
            new Dictionary<string, string?>
            {
                ["state"] = triggerType == AutomationTriggerCatalog.PerformanceModeEntered ? "entered" : triggerType == AutomationTriggerCatalog.DynamicPerformanceChanged ? "changed" : "exited",
                ["processName"] = processName,
                ["processId"] = processId?.ToString(),
                ["windowMode"] = configuration.WindowMode,
                ["gpuComparison"] = configuration.GpuComparison,
                ["gpuThreshold"] = configuration.EnterThreshold.ToString("0")
                , ["stage"] = stage
                , ["powerSource"] = onBattery == true ? "battery" : "ac"
            },
            correlationId));
    }

    private static bool IsOnBattery()
        => PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;

    private static string ResolveStage(float gpu, bool onBattery, string? previousStage)
    {
        if (onBattery)
        {
            return previousStage switch
            {
                "high" when gpu >= 65 => "high",
                "balanced" when gpu >= 25 => gpu >= 80 ? "high" : "balanced",
                "quiet" when gpu < 40 => "quiet",
                _ => gpu >= 80 ? "high" : gpu >= 40 ? "balanced" : "quiet"
            };
        }

        return previousStage switch
        {
            "high" when gpu >= 55 => "high",
            "balanced" when gpu >= 35 => gpu >= 70 ? "high" : "balanced",
            "quiet" when gpu < 45 => "quiet",
            _ => gpu >= 70 ? "high" : gpu >= 45 ? "balanced" : "quiet"
        };
    }

    private static bool MatchesWindow(string mode, bool fullscreen)
        => string.Equals(mode, "windowed", StringComparison.OrdinalIgnoreCase) ? !fullscreen : fullscreen;

    private static bool MatchesGpu(string comparison, float usage, float threshold)
        => string.Equals(comparison, "less-than", StringComparison.OrdinalIgnoreCase) ? usage <= threshold : usage >= threshold;

    private static bool ShouldExit(PerformanceConfiguration configuration, float usage)
        => string.Equals(configuration.GpuComparison, "less-than", StringComparison.OrdinalIgnoreCase)
            ? usage >= configuration.EnterThreshold
            : usage <= configuration.ExitThreshold;

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(processName) && Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PerformanceState
    {
        public string? ActiveProcessName { get; set; }
        public int? ActiveProcessId { get; set; }
        public string? CandidateProcessName { get; set; }
        public int? CandidateProcessId { get; set; }
        public DateTimeOffset? CandidateSince { get; set; }
        public int CandidateSamples { get; set; }
        public int CandidateHits { get; set; }
        public DateTimeOffset? ExitSince { get; set; }
        public string? ActiveStage { get; set; }

        public void ClearCandidate()
        {
            CandidateProcessName = null;
            CandidateProcessId = null;
            CandidateSince = null;
            CandidateSamples = 0;
            CandidateHits = 0;
        }

        public void ClearActive()
        {
            ActiveProcessName = null;
            ActiveProcessId = null;
            ActiveStage = null;
            ExitSince = null;
            ClearCandidate();
        }
    }

    private sealed record PerformanceConfiguration(
        string Key,
        string WindowMode,
        string GpuComparison,
        float EnterThreshold,
        float ExitThreshold,
        bool IsDynamic)
    {
        public static PerformanceConfiguration FromParameters(IReadOnlyDictionary<string, string> parameters, bool isDynamic)
        {
            var windowMode = parameters.TryGetValue("windowMode", out var mode) &&
                             string.Equals(mode, "windowed", StringComparison.OrdinalIgnoreCase)
                ? "windowed"
                : "fullscreen";
            var comparison = parameters.TryGetValue("gpuComparison", out var value) &&
                             string.Equals(value, "less-than", StringComparison.OrdinalIgnoreCase)
                ? "less-than"
                : "greater-than";
            var threshold = ParseThreshold(parameters.TryGetValue("gpuThreshold", out var raw) ? raw : null, 60f);
            return new PerformanceConfiguration(
                $"{(isDynamic ? "dynamic" : "legacy")}:{windowMode}:{comparison}:{threshold:0}",
                windowMode,
                comparison,
                threshold,
                Math.Min(20f, threshold),
                isDynamic);
        }

        public static PerformanceConfiguration FromKey(string key)
        {
            var parts = key.Split(':', 4);
            var isDynamic = parts.Length > 0 && parts[0] == "dynamic";
            var offset = isDynamic || (parts.Length > 0 && parts[0] == "legacy") ? 1 : 0;
            var windowMode = parts.Length > offset && parts[offset] == "windowed" ? "windowed" : "fullscreen";
            var comparison = parts.Length > offset + 1 && parts[offset + 1] == "less-than" ? "less-than" : "greater-than";
            var threshold = parts.Length > offset + 2 && float.TryParse(parts[offset + 2], out var parsed) ? parsed : 60f;
            return new PerformanceConfiguration(key, windowMode, comparison, threshold, Math.Min(20f, threshold), isDynamic);
        }

        private static float ParseThreshold(string? value, float fallback)
        {
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            return float.TryParse(digits, out var parsed) ? parsed : fallback;
        }
    }
}
