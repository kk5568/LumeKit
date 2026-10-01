using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationCustomShortcutActionProvider : IAutomationActionProvider
{
    private readonly IShortcutProvider _shortcutProvider;

    public AutomationCustomShortcutActionProvider(IShortcutProvider shortcutProvider)
    {
        _shortcutProvider = shortcutProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSendCustomHotkey,
        "Custom Shortcut",
        AutomationActionProviderKind.Input,
        AutomationActionResourceScope.Input,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: true);

    public string ActionType => AutomationSupportCatalog.ActionSendCustomHotkey;

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var hotkey = AutomationActionParameterHelper.ReadValue(action.ParametersJson);
        var success = await _shortcutProvider.TriggerAsync(hotkey, cancellationToken);

        if (!success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(hotkey) ? "快捷键未设置" : $"自定义快捷键执行失败: {hotkey}");
        }
    }
}
