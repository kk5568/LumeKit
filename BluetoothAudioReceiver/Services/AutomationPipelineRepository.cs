using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.Core.Helpers;
using BluetoothAudioReceiver.Views.Pages;
using Serilog;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationPipelineRepository : IAutomationPipelineRepository
{
    private const string PipelinesFileName = "automation-pipelines.json";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly List<AutomationPipeline> _pipelines;
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationPipelineRepository));

    public AutomationPipelineRepository(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _pipelines = LoadPipelines();
        InitializationTask = LastLoadStatus.NormalizedAndSaved
            ? PersistLoadedPipelinesAsync()
            : Task.CompletedTask;
    }

    public IReadOnlyList<AutomationPipeline> Pipelines => _pipelines;
    public AutomationPipelineRepositoryLoadStatus LastLoadStatus { get; private set; } = AutomationPipelineRepositoryLoadStatus.Empty;
    public Task InitializationTask { get; }

    public event EventHandler? PipelinesChanged;

    public async Task ReplaceAllAsync(IEnumerable<AutomationPipeline> pipelines)
    {
        var next = NormalizePipelines(pipelines).ToList();

        await _saveGate.WaitAsync();
        try
        {
            var saved = await _localSettingsService.SaveJsonFileAsync(PipelinesFileName, next);
            if (!saved)
            {
                throw new IOException("保存自动化规则失败，请检查磁盘权限或存储空间。");
            }

            _pipelines.Clear();
            _pipelines.AddRange(next);
            LastLoadStatus = new AutomationPipelineRepositoryLoadStatus(
                Success: true,
                StoredCount: next.Count,
                LoadedCount: next.Count,
                NormalizedAndSaved: false,
                ErrorMessage: null);
        }
        finally
        {
            _saveGate.Release();
        }

        var handlers = PipelinesChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Automation pipeline change subscriber failed.");
            }
        }
    }

    private List<AutomationPipeline> LoadPipelines()
    {
        try
        {
            var stored = _localSettingsService.ReadJsonFileOrThrow<List<AutomationPipeline>>(PipelinesFileName);
            var storedCount = stored?.Count ?? 0;
            var pipelines = NormalizePipelines(stored ?? []).ToList();
            var removedCompanionCount = storedCount - pipelines.Count;

            LastLoadStatus = new AutomationPipelineRepositoryLoadStatus(
                Success: true,
                StoredCount: storedCount,
                LoadedCount: pipelines.Count,
                NormalizedAndSaved: removedCompanionCount > 0,
                ErrorMessage: null);

            _log.Information(
                "Automation pipelines restored. StoredCount={StoredCount}, LoadedCount={LoadedCount}, RemovedCompanionCount={RemovedCompanionCount}",
                storedCount,
                pipelines.Count,
                removedCompanionCount);

            return pipelines;
        }
        catch (Exception ex)
        {
            BackupCorruptPipelineFile();
            LastLoadStatus = new AutomationPipelineRepositoryLoadStatus(
                Success: false,
                StoredCount: 0,
                LoadedCount: 0,
                NormalizedAndSaved: false,
                ErrorMessage: ex.Message);
            _log.Error(ex, "Automation pipelines restore failed");
            return [];
        }
    }

    private async Task PersistLoadedPipelinesAsync()
    {
        try
        {
            var saved = await _localSettingsService.SaveJsonFileAsync(PipelinesFileName, _pipelines);
            if (!saved)
            {
                throw new IOException("保存自动化规则失败，请检查磁盘权限或存储空间。");
            }

            LastLoadStatus = LastLoadStatus with { NormalizedAndSaved = true };
        }
        catch (Exception ex)
        {
            LastLoadStatus = LastLoadStatus with { ErrorMessage = ex.Message };
            _log.Warning(ex, "Failed to persist normalized automation pipelines.");
        }
    }

    private void BackupCorruptPipelineFile()
    {
        var path = Path.Combine(
            LocalSettingsHelper.ApplicationDataPath,
            Constants.SettingsFolder,
            PipelinesFileName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var backupPath = $"{path}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmssfff}";
            File.Copy(path, backupPath, overwrite: false);
            _log.Warning(
                "Backed up corrupt automation pipeline file. BackupPath={BackupPath}",
                backupPath);
        }
        catch (Exception backupException)
        {
            _log.Warning(
                backupException,
                "Failed to back up corrupt automation pipeline file. Path={Path}",
                path);
        }
    }

    private static bool ArePipelinesEquivalent(IReadOnlyList<AutomationPipeline>? left, IReadOnlyList<AutomationPipeline> right)
    {
        if (left is null)
        {
            return right.Count == 0;
        }

        return JsonHelper.Stringify(left) == JsonHelper.Stringify(right);
    }

    private static IEnumerable<AutomationPipeline> NormalizePipelines(IEnumerable<AutomationPipeline> pipelines)
    {
        var normalized = pipelines
            .Where(pipeline => !IsLegacyCompanionRestorePipeline(pipeline))
            .ToList();
        foreach (var pipeline in normalized)
        {
            pipeline.Name = AutomationDisplayTextConverter.ToDisplayText(pipeline.Name);
            pipeline.Category = AutomationDisplayTextConverter.ToDisplayText(pipeline.Category);
            pipeline.Options ??= new AutomationPipelineOptions();

            foreach (var step in pipeline.Steps)
            {
                step.ActionType = NormalizeStepActionType(step.ActionType);
                step.ParametersJson = NormalizeStepParameters(step.ActionType, step.ParametersJson);
            }

        }

        return normalized;
    }

    private static bool IsLegacyCompanionRestorePipeline(AutomationPipeline pipeline)
        => pipeline.Trigger.Parameters.ContainsKey("companionFor");

    private static string NormalizeStepParameters(string actionType, string parametersJson)
    {
        return actionType switch
        {
            AutomationSupportCatalog.ActionSetBrightness => AutomationActionParameterHelper.BuildBrightness(AutomationActionParameterHelper.ReadBrightness(parametersJson, 80)),
            AutomationSupportCatalog.ActionSetInputVolume => AutomationActionParameterHelper.BuildInputVolume(AutomationActionParameterHelper.ReadInputVolumePercentage(parametersJson, 50)),
            AutomationSupportCatalog.ActionSetTheme => AutomationActionParameterHelper.BuildValue(AutomationActionParameterHelper.ReadValue(parametersJson, "dark")),
            AutomationSupportCatalog.ActionSendGHelperHotkey => AutomationActionParameterHelper.BuildValue(GHelperShortcutCatalog.NormalizeSelectionText(AutomationActionParameterHelper.ReadValue(parametersJson))),
            AutomationSupportCatalog.ActionSendWindowsHotkey => AutomationActionParameterHelper.BuildValue(WindowsShortcutCatalog.NormalizeSelectionText(AutomationActionParameterHelper.ReadValue(parametersJson))),
            AutomationSupportCatalog.ActionSendCustomHotkey => AutomationActionParameterHelper.BuildValue(AutomationActionParameterHelper.ReadValue(parametersJson)),
            AutomationSupportCatalog.ActionShowNotification => AutomationActionParameterHelper.BuildValue(AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadValue(parametersJson))),
            AutomationSupportCatalog.ActionSetInputDevice => AutomationActionParameterHelper.BuildInputDevice(
                AutomationActionParameterHelper.ReadInputDeviceId(parametersJson),
                AutomationActionParameterHelper.ReadInputDeviceOperation(parametersJson)),
            AutomationSupportCatalog.ActionSetOutputDevice => AutomationActionParameterHelper.BuildOutputDevice(AutomationActionParameterHelper.ReadOutputDeviceId(parametersJson)),
            AutomationSupportCatalog.ActionSetPowerMode => NormalizePowerMode(parametersJson),
            AutomationSupportCatalog.ActionProcessControl => NormalizeProcessControl(parametersJson),
            AutomationSupportCatalog.ActionDisableTouchpad => AutomationActionParameterHelper.BuildValue("disable"),
            AutomationSupportCatalog.ActionEnableTouchpad => AutomationActionParameterHelper.BuildValue("enable"),
            AutomationSupportCatalog.ActionDelaySeconds => AutomationActionParameterHelper.BuildDelaySeconds(AutomationActionParameterHelper.ReadDelaySeconds(parametersJson, 0)),
            AutomationSupportCatalog.ActionCaptureState => AutomationActionParameterHelper.BuildSnapshotKey(AutomationActionParameterHelper.ReadValue(parametersJson, "default")),
            AutomationSupportCatalog.ActionRestoreState => AutomationActionParameterHelper.BuildSnapshotKey(AutomationActionParameterHelper.ReadValue(parametersJson, "default")),
            AutomationSupportCatalog.ActionStopExecution => AutomationActionParameterHelper.BuildStopExecution(),
            _ => parametersJson
        };
    }

    private static string NormalizeStepActionType(string actionType)
    {
        return string.Equals(actionType, AutomationSupportCatalog.ActionSendHotkey, StringComparison.OrdinalIgnoreCase)
            ? AutomationSupportCatalog.ActionSendGHelperHotkey
            : actionType;
    }

    private static string NormalizePowerMode(string parametersJson)
    {
        var guid = AutomationActionParameterHelper.ReadPowerModeGuid(parametersJson);
        var displayName = AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadPowerModeDisplayName(parametersJson));
        if (!string.IsNullOrWhiteSpace(guid) || !string.IsNullOrWhiteSpace(displayName))
        {
            return AutomationActionParameterHelper.BuildPowerMode($"{guid} | {displayName}".Trim());
        }

        return AutomationActionParameterHelper.BuildValue(AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadValue(parametersJson)));
    }

    private static string NormalizeProcessControl(string parametersJson)
    {
        var (operation, target) = AutomationActionParameterHelper.ReadProcessOperation(parametersJson);
        if (string.Equals(operation, "close", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationActionParameterHelper.BuildProcessClose(target);
        }

        if (string.Equals(operation, "launch", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationActionParameterHelper.BuildProcessLaunch(target);
        }

        return parametersJson;
    }
}
