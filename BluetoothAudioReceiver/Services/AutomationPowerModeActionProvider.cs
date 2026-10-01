using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationPowerModeActionProvider : IAutomationActionProvider
{
    private const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string PowerSaverScheme = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private readonly IPowerProvider _powerProvider;

    public AutomationPowerModeActionProvider(IPowerProvider powerProvider)
    {
        _powerProvider = powerProvider;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionSetPowerMode,
        "Set Power Mode",
        AutomationActionProviderKind.System,
        AutomationActionResourceScope.Power,
        SupportsRestore: true,
        RequiresAdministrator: false,
        IsAsync: true,
        AllowsConcurrent: false);

    public string ActionType => AutomationSupportCatalog.ActionSetPowerMode;

    public async Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var mode = ResolveMode(action.ParametersJson);
        var success = await _powerProvider.SetActiveSchemeAsync(mode.SchemeGuid, cancellationToken);

        if (!success)
        {
            throw new InvalidOperationException($"电源模式切换失败: {mode.DisplayName}");
        }
    }

    private static (string SchemeGuid, string DisplayName) ResolveMode(string parameters)
    {
        var explicitGuid = AutomationActionParameterHelper.ReadPowerModeGuid(parameters);
        var explicitDisplayName = AutomationActionParameterHelper.ReadPowerModeDisplayName(parameters);
        if (!string.IsNullOrWhiteSpace(explicitGuid))
        {
            var displayName = string.IsNullOrWhiteSpace(explicitDisplayName)
                ? AutomationOptionCatalog.ExtractPowerModeDisplayName(parameters)
                : explicitDisplayName;
            return (explicitGuid, displayName);
        }

        if (parameters.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            parameters.Contains("silent", StringComparison.OrdinalIgnoreCase) ||
            parameters.Contains("quiet", StringComparison.OrdinalIgnoreCase) ||
            parameters.Contains("节能", StringComparison.OrdinalIgnoreCase))
        {
            return (PowerSaverScheme, "节能");
        }

        if (parameters.Contains("high", StringComparison.OrdinalIgnoreCase) ||
            parameters.Contains("performance", StringComparison.OrdinalIgnoreCase) ||
            parameters.Contains("高性能", StringComparison.OrdinalIgnoreCase) ||
            parameters.Contains("性能", StringComparison.OrdinalIgnoreCase))
        {
            return (HighPerformanceScheme, "高性能");
        }

        return (BalancedScheme, "平衡");
    }
}
