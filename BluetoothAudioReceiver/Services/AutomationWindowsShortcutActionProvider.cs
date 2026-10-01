using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationWindowsShortcutActionProvider : IAutomationActionProvider
{
    private readonly IShortcutProvider _shortcutProvider;

    public AutomationWindowsShortcutActionProvider(IShortcutProvider shortcutProvider)
    {
        _shortcutProvider = shortcutProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSendWindowsHotkey,
        "Windows Shortcut",
        AutomationActionProviderKind.Input,
        AutomationActionResourceScope.Input,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: true);

    public string ActionType => AutomationSupportCatalog.ActionSendWindowsHotkey;

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var selection = WindowsShortcutCatalog.NormalizeSelectionText(AutomationActionParameterHelper.ReadValue(action.ParametersJson));
        var hotkey = WindowsShortcutCatalog.ResolveHotkey(selection);
        var displayName = WindowsShortcutCatalog.ResolveDisplayName(selection);
        var success = await _shortcutProvider.TriggerAsync(hotkey, cancellationToken);

        if (!success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(hotkey) ? "快捷键未设置" : $"Win11 快捷键执行失败: {displayName}");
        }
    }
}
