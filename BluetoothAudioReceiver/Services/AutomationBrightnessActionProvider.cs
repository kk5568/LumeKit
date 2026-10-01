using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationBrightnessActionProvider : IAutomationActionProvider
{
    private readonly IDisplayProvider _displayProvider;

    public AutomationBrightnessActionProvider(IDisplayProvider displayProvider)
    {
        _displayProvider = displayProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSetBrightness,
        "Set Brightness",
        AutomationActionProviderKind.Device,
        AutomationActionResourceScope.Brightness,
        SupportsRestore: true,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "set-brightness";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var brightness = ResolveBrightness(action.ParametersJson);
        await _displayProvider.SetBrightnessAsync((byte)brightness, cancellationToken);
    }

    private static int ResolveBrightness(string parameters)
    {
        var value = AutomationActionParameterHelper.ReadBrightness(parameters, fallback: -1);
        if (value >= 0)
        {
            return value;
        }

        if (parameters.Contains("low", StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        if (parameters.Contains("high", StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        return 50;
    }
}
