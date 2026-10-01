using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationCaptureStateActionProvider : IAutomationActionProvider
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationCaptureStateActionProvider));
    private readonly AutomationSessionStateStore _stateStore;

    public AutomationCaptureStateActionProvider(AutomationSessionStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionCaptureState,
        "Capture State",
        AutomationActionProviderKind.State,
        AutomationActionResourceScope.SessionState | AutomationActionResourceScope.Power | AutomationActionResourceScope.Theme | AutomationActionResourceScope.Brightness | AutomationActionResourceScope.Audio | AutomationActionResourceScope.Input,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "capture-state";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var key = AutomationActionParameterHelper.ReadValue(action.ParametersJson, "default");
        _log.Information("Automation capture-state action started. Key={SnapshotKey}, TriggerType={TriggerType}", key, triggerEvent.TriggerType);
        await _stateStore.CaptureAsync(key, cancellationToken);
    }
}
