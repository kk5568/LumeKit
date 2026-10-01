using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationEnableTouchpadActionProvider : IAutomationActionProvider
{
    private readonly ITouchpadProvider _touchpadProvider;

    public AutomationEnableTouchpadActionProvider(ITouchpadProvider touchpadProvider)
    {
        _touchpadProvider = touchpadProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionEnableTouchpad,
        "Enable Touchpad",
        AutomationActionProviderKind.Device,
        AutomationActionResourceScope.Input,
        SupportsRestore: true,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "enable-touchpad";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var success = await _touchpadProvider.EnableAsync(cancellationToken);

        if (!success)
        {
            throw new InvalidOperationException("开启触摸板失败");
        }
    }
}
