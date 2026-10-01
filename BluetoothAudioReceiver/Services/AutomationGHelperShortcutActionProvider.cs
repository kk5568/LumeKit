using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationGHelperShortcutActionProvider : IAutomationActionProvider
{
    private readonly IShortcutProvider _shortcutProvider;

    public AutomationGHelperShortcutActionProvider(IShortcutProvider shortcutProvider)
    {
        _shortcutProvider = shortcutProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSendGHelperHotkey,
        "GHelper Shortcut",
        AutomationActionProviderKind.Input,
        AutomationActionResourceScope.Input,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: true);

    public string ActionType => AutomationSupportCatalog.ActionSendGHelperHotkey;

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var selection = GHelperShortcutCatalog.NormalizeSelectionText(AutomationActionParameterHelper.ReadValue(action.ParametersJson));
        var hotkey = GHelperShortcutCatalog.ResolveHotkey(selection);
        var displayName = GHelperShortcutCatalog.ResolveDisplayName(selection);
        var success = await _shortcutProvider.TriggerAsync(hotkey, cancellationToken);

        if (!success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(hotkey) ? "快捷键未设置" : $"GHelper 快捷键执行失败: {displayName}");
        }
    }
}
