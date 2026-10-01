using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationRestoreStateActionProvider : IAutomationActionProvider
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationRestoreStateActionProvider));
    private readonly AutomationSessionStateStore _stateStore;
    private readonly IPowerProvider _powerProvider;
    private readonly ITouchpadProvider _touchpadProvider;
    private readonly IDisplayProvider _displayProvider;
    private readonly IAudioProvider _audioProvider;
    private readonly IThemeProvider _themeProvider;

    public AutomationRestoreStateActionProvider(
        AutomationSessionStateStore stateStore,
        IPowerProvider powerProvider,
        ITouchpadProvider touchpadProvider,
        IDisplayProvider displayProvider,
        IAudioProvider audioProvider,
        IThemeProvider themeProvider)
    {
        _stateStore = stateStore;
        _powerProvider = powerProvider;
        _touchpadProvider = touchpadProvider;
        _displayProvider = displayProvider;
        _audioProvider = audioProvider;
        _themeProvider = themeProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionRestoreState,
        "Restore State",
        AutomationActionProviderKind.State,
        AutomationActionResourceScope.SessionState | AutomationActionResourceScope.Power | AutomationActionResourceScope.Theme | AutomationActionResourceScope.Brightness | AutomationActionResourceScope.Audio | AutomationActionResourceScope.Input,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "restore-state";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var key = AutomationActionParameterHelper.ReadValue(action.ParametersJson, "default");
        _log.Information("Automation restore-state action started. Key={SnapshotKey}, TriggerType={TriggerType}", key, triggerEvent.TriggerType);
        if (!_stateStore.TryRelease(key, out var snapshot))
        {
            _log.Warning("Automation restore-state action skipped because snapshot lease was not found. Key={SnapshotKey}", key);
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ActivePowerScheme))
        {
            _log.Information("Automation restore-state applying power scheme. Key={SnapshotKey}, PowerScheme={PowerScheme}", key, snapshot.ActivePowerScheme);
            await _powerProvider.SetActiveSchemeAsync(snapshot.ActivePowerScheme, cancellationToken);
        }

        if (snapshot.IsTouchpadEnabled == true)
        {
            _log.Information("Automation restore-state enabling touchpad. Key={SnapshotKey}", key);
            await _touchpadProvider.EnableAsync(cancellationToken);
        }
        else if (snapshot.IsTouchpadEnabled == false)
        {
            _log.Information("Automation restore-state disabling touchpad. Key={SnapshotKey}", key);
            await _touchpadProvider.DisableAsync(cancellationToken);
        }

        if (snapshot.Brightness.HasValue)
        {
            _log.Information("Automation restore-state applying brightness. Key={SnapshotKey}, Brightness={Brightness}", key, snapshot.Brightness.Value);
            await _displayProvider.SetBrightnessAsync(snapshot.Brightness.Value, cancellationToken);
        }

        if (snapshot.InputVolumeIndex.HasValue)
        {
            _log.Information("Automation restore-state applying input volume. Key={SnapshotKey}, InputVolumeIndex={InputVolumeIndex}",
                key,
                snapshot.InputVolumeIndex.Value);
            await _audioProvider.SetInputVolumeAsync(snapshot.InputVolumeIndex.Value, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.OutputDeviceId))
        {
            _log.Information("Automation restore-state applying output device. Key={SnapshotKey}, OutputDeviceId={OutputDeviceId}",
                key,
                snapshot.OutputDeviceId);
            await _audioProvider.SetOutputDeviceAsync(snapshot.OutputDeviceId, cancellationToken);
        }

        if (snapshot.Theme.HasValue)
        {
            _log.Information("Automation restore-state applying theme. Key={SnapshotKey}, Theme={Theme}",
                key,
                snapshot.Theme.Value);
            await _themeProvider.SetThemeAsync(snapshot.Theme.Value, cancellationToken);
        }

        _log.Information("Automation restore-state action completed. Key={SnapshotKey}", key);
    }
}
