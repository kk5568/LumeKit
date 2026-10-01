using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationInputDeviceActionProvider : IAutomationActionProvider
{
    private readonly IAudioProvider _audioProvider;

    public AutomationInputDeviceActionProvider(IAudioProvider audioProvider)
    {
        _audioProvider = audioProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSetInputDevice,
        "Set Input Device",
        AutomationActionProviderKind.Device,
        AutomationActionResourceScope.Audio,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "set-input-device";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var deviceId = AutomationActionParameterHelper.ReadInputDeviceId(action.ParametersJson);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new InvalidOperationException("未指定输入设备");
        }

        var operation = AutomationActionParameterHelper.ReadInputDeviceOperation(action.ParametersJson, "connect");
        await _audioProvider.SetInputDeviceAsync(
            deviceId,
            disconnect: string.Equals(operation, "disconnect", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
    }
}
