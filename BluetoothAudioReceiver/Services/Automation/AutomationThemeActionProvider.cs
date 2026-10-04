using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationThemeActionProvider : IAutomationActionProvider
{
    private readonly IThemeProvider _themeProvider;

    public AutomationThemeActionProvider(IThemeProvider themeProvider)
    {
        _themeProvider = themeProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSetTheme,
        "Set Theme",
        AutomationActionProviderKind.System,
        AutomationActionResourceScope.Theme,
        SupportsRestore: true,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => "set-theme";

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var parameter = AutomationActionParameterHelper.ReadValue(action.ParametersJson);
        var target = parameter.Contains("light", StringComparison.OrdinalIgnoreCase)
            ? ElementTheme.Light
            : parameter.Contains("system", StringComparison.OrdinalIgnoreCase)
                ? ElementTheme.Default
                : ElementTheme.Dark;

        await _themeProvider.SetThemeAsync(target, cancellationToken);
    }
}
