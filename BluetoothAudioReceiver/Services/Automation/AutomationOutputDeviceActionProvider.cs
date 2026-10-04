using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationOutputDeviceActionProvider : IAutomationActionProvider
{
    private readonly IAudioProvider _audioProvider;

    public AutomationOutputDeviceActionProvider(IAudioProvider audioProvider)
    {
        _audioProvider = audioProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSetOutputDevice,
        "Set Output Device",
        AutomationActionProviderKind.Device,
        AutomationActionResourceScope.Audio,
        SupportsRestore: true,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "set-output-device";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var deviceId = AutomationActionParameterHelper.ReadOutputDeviceId(action.ParametersJson);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        await _audioProvider.SetOutputDeviceAsync(deviceId, cancellationToken);
    }
}
