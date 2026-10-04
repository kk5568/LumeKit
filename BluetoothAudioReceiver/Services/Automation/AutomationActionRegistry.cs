using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationActionRegistry : IAutomationActionRegistry
{
    private static readonly AutomationActionCapability[] BuiltInCapabilities =
    [
        new(
            AutomationSupportCatalog.ActionDelaySeconds,
            "Delay",
            AutomationActionProviderKind.System,
            AutomationActionResourceScope.None,
            SupportsRestore: false,
            RequiresAdministrator: false,
            IsAsync: true,
            AllowsConcurrent: true),
        new(
            AutomationSupportCatalog.ActionStopExecution,
            "Stop Execution",
            AutomationActionProviderKind.System,
            AutomationActionResourceScope.None,
            SupportsRestore: false,
            RequiresAdministrator: false,
            IsAsync: false,
            AllowsConcurrent: true)
    ];

    private readonly IReadOnlyDictionary<string, AutomationActionCapability> _capabilities;
    private readonly IReadOnlyDictionary<string, IAutomationActionProvider> _providers;

    public AutomationActionRegistry(IEnumerable<IAutomationActionProvider> providers)
    {
        var providerList = providers.ToArray();

        foreach (var provider in providerList)
        {
            if (!string.Equals(provider.ActionType, provider.Capability.ActionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Automation action provider '{provider.GetType().Name}' has mismatched ActionType '{provider.ActionType}' and Capability.ActionId '{provider.Capability.ActionId}'.");
            }
        }

        _providers = providerList.ToDictionary(
            provider => provider.Capability.ActionId,
            StringComparer.OrdinalIgnoreCase);

        _capabilities = BuiltInCapabilities
            .Concat(providerList.Select(provider => provider.Capability))
            .ToDictionary(capability => capability.ActionId, StringComparer.OrdinalIgnoreCase);

        Capabilities = _capabilities.Values
            .OrderBy(capability => capability.ActionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<AutomationActionCapability> Capabilities { get; }

    public AutomationActionCapability? GetCapability(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return null;
        }

        return _capabilities.TryGetValue(actionType, out var capability)
            ? capability
            : null;
    }

    public IAutomationActionProvider? GetProvider(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return null;
        }

        return _providers.TryGetValue(actionType, out var provider)
            ? provider
            : null;
    }

    public bool IsSupported(string? actionType)
        => GetCapability(actionType) is not null;
}
