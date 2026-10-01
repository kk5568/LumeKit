using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationTouchpadActionProvider : IAutomationActionProvider
{
    private readonly ITouchpadProvider _touchpadProvider;

    public AutomationTouchpadActionProvider(ITouchpadProvider touchpadProvider)
    {
        _touchpadProvider = touchpadProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionDisableTouchpad,
        "Disable Touchpad",
        AutomationActionProviderKind.Device,
        AutomationActionResourceScope.Input,
        SupportsRestore: true,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "disable-touchpad";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var enableTouchpad = string.Equals(action.ActionType, AutomationSupportCatalog.ActionEnableTouchpad, StringComparison.OrdinalIgnoreCase);
        var success = enableTouchpad
            ? await _touchpadProvider.EnableAsync(cancellationToken)
            : await _touchpadProvider.DisableAsync(cancellationToken);

        if (!success)
        {
            throw new InvalidOperationException(enableTouchpad ? "开启触摸板失败" : "关闭触摸板失败");
        }
    }
}
