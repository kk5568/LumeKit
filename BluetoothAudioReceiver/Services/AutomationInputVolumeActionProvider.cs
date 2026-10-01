using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationInputVolumeActionProvider : IAutomationActionProvider
{
    private readonly IAudioProvider _audioProvider;

    public AutomationInputVolumeActionProvider(IAudioProvider audioProvider)
    {
        _audioProvider = audioProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSetInputVolume,
        "Set Input Volume",
        AutomationActionProviderKind.Device,
        AutomationActionResourceScope.Audio,
        SupportsRestore: true,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "set-input-volume";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var percentage = AutomationActionParameterHelper.ReadInputVolumePercentage(action.ParametersJson, 80);
        var index = percentage switch
        {
            <= 25 => 0,
            <= 50 => 1,
            <= 75 => 2,
            _ => 3
        };

        await _audioProvider.SetInputVolumeAsync(index, cancellationToken);
    }
}
