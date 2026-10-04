using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationTriggerRegistry : IAutomationTriggerRegistry
{
    private static readonly AutomationTriggerDescriptor[] SupportedTriggers =
    [
        new(
            AutomationTriggerCatalog.AppForegroundChanged,
            "程序启动",
            AutomationTriggerProviderKind.Application,
            SupportsParameters: true,
            EmitsDynamicPayload: true,
            LegacyAliases: ["ForegroundAppChanged"]),
        new(
            AutomationTriggerCatalog.AppStopped,
            "程序退出",
            AutomationTriggerProviderKind.Application,
            SupportsParameters: true,
            EmitsDynamicPayload: true,
            LegacyAliases: ["ProcessStopped"]),
        new(
            AutomationTriggerCatalog.PerformanceModeEntered,
            "性能模式",
            AutomationTriggerProviderKind.Application,
            SupportsParameters: true,
            EmitsDynamicPayload: true,
            LegacyAliases: ["PerformanceModeEntered"]),
        new(
            AutomationTriggerCatalog.PerformanceModeExited,
            "性能模式",
            AutomationTriggerProviderKind.Application,
            SupportsParameters: true,
            EmitsDynamicPayload: true,
            LegacyAliases: ["PerformanceModeExited"]),
        new(
            AutomationTriggerCatalog.PowerChanged,
            "电源状态",
            AutomationTriggerProviderKind.System,
            SupportsParameters: true,
            EmitsDynamicPayload: true,
            LegacyAliases: ["PowerStateChanged"]),
        new(
            AutomationTriggerCatalog.BluetoothConnected,
            "蓝牙已连接",
            AutomationTriggerProviderKind.Device,
            SupportsParameters: true,
            EmitsDynamicPayload: true,
            LegacyAliases: ["BluetoothDeviceConnected"]),
        new(
            AutomationTriggerCatalog.OutputDeviceChanged,
            "输出设备变化",
            AutomationTriggerProviderKind.Device,
            SupportsParameters: true,
            EmitsDynamicPayload: true,
            LegacyAliases: ["DefaultOutputDeviceChanged"]),
        new(
            AutomationTriggerCatalog.SunriseReached,
            "到达日出",
            AutomationTriggerProviderKind.Schedule,
            SupportsParameters: false,
            EmitsDynamicPayload: false,
            LegacyAliases: ["SunriseReached"]),
        new(
            AutomationTriggerCatalog.SunsetReached,
            "到达日落",
            AutomationTriggerProviderKind.Schedule,
            SupportsParameters: false,
            EmitsDynamicPayload: false,
            LegacyAliases: ["SunsetReached"])
    ];

    private readonly IReadOnlyDictionary<string, AutomationTriggerDescriptor> _supportedById;
    private readonly IReadOnlyDictionary<string, string> _aliases;

    public AutomationTriggerRegistry()
    {
        _supportedById = SupportedTriggers.ToDictionary(item => item.TriggerId, StringComparer.OrdinalIgnoreCase);
        _aliases = SupportedTriggers
            .SelectMany(item => (item.LegacyAliases ?? []).Select(alias => new KeyValuePair<string, string>(alias, item.TriggerId)))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        Triggers = SupportedTriggers;
    }

    public IReadOnlyList<AutomationTriggerDescriptor> Triggers { get; }

    public AutomationTriggerDescriptor? GetDescriptor(string? triggerId)
    {
        var normalized = Normalize(triggerId);
        return _supportedById.TryGetValue(normalized, out var descriptor)
            ? descriptor
            : null;
    }

    public string Normalize(string? triggerId)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return string.Empty;
        }

        var trimmed = triggerId.Trim();
        return _aliases.TryGetValue(trimmed, out var normalized)
            ? normalized
            : trimmed;
    }

    public string GetDisplayName(string? triggerId)
        => GetDescriptor(triggerId)?.DisplayName ?? Normalize(triggerId);

    public bool IsSupported(string? triggerId)
        => GetDescriptor(triggerId) is not null;
}
