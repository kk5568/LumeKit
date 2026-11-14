using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationConditionRegistry : IAutomationConditionRegistry
{
    private static readonly AutomationConditionDescriptor[] SupportedConditions =
    [
        new(
            AutomationSupportCatalog.ConditionPowerState,
            "电源状态",
            AutomationConditionProviderKind.System,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionForegroundProcess,
            "前台程序",
            AutomationConditionProviderKind.Application,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionWindowTitle,
            "窗口标题",
            AutomationConditionProviderKind.Application,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionFullscreenApp,
            "全屏应用",
            AutomationConditionProviderKind.Application,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionGameRunning,
            "游戏运行中",
            AutomationConditionProviderKind.Application,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionBatteryLevel,
            "电池电量",
            AutomationConditionProviderKind.System,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionChargingState,
            "充电中",
            AutomationConditionProviderKind.System,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionNetworkName,
            "网络名称",
            AutomationConditionProviderKind.System,
            SupportsParameters: true),
        new(
            AutomationSupportCatalog.ConditionThemeMode,
            "系统主题",
            AutomationConditionProviderKind.System,
            SupportsParameters: true)
    ];

    private readonly IReadOnlyDictionary<string, AutomationConditionDescriptor> _descriptors;

    public AutomationConditionRegistry()
    {
        _descriptors = SupportedConditions.ToDictionary(item => item.ConditionId, StringComparer.OrdinalIgnoreCase);
        Conditions = SupportedConditions;
    }

    public IReadOnlyList<AutomationConditionDescriptor> Conditions { get; }

    public AutomationConditionDescriptor? GetDescriptor(string? conditionType)
    {
        if (string.IsNullOrWhiteSpace(conditionType))
        {
            return null;
        }

        return _descriptors.TryGetValue(conditionType.Trim(), out var descriptor)
            ? descriptor
            : null;
    }

    public bool IsSupported(string? conditionType)
        => GetDescriptor(conditionType) is not null;

    public bool Evaluate(AutomationConditionDefinition condition, TriggerEvent triggerEvent, SystemContext context)
    {
        if (!IsSupported(condition.ConditionType))
        {
            return false;
        }

        return condition.ConditionType switch
        {
            AutomationSupportCatalog.ConditionPowerState => MatchPowerCondition(condition, triggerEvent, context),
            AutomationSupportCatalog.ConditionForegroundProcess => MatchForegroundProcess(condition, context),
            AutomationSupportCatalog.ConditionWindowTitle => MatchWindowTitle(condition, context),
            AutomationSupportCatalog.ConditionFullscreenApp => MatchFullscreenApp(condition, context),
            AutomationSupportCatalog.ConditionGameRunning => MatchGameRunning(condition, context),
            AutomationSupportCatalog.ConditionBatteryLevel => MatchBatteryLevel(condition, context),
            AutomationSupportCatalog.ConditionChargingState => MatchChargingState(condition, context),
            AutomationSupportCatalog.ConditionNetworkName => MatchNetworkName(condition, context),
            AutomationSupportCatalog.ConditionThemeMode => MatchThemeMode(condition, context),
            _ => false
        };
    }

    private static bool MatchPowerCondition(
        AutomationConditionDefinition condition,
        TriggerEvent triggerEvent,
        SystemContext context)
    {
        if (!condition.Parameters.TryGetValue("mode", out var mode) || string.IsNullOrWhiteSpace(mode))
        {
            return true;
        }

        var isOnBattery = context.IsOnBattery;
        if (triggerEvent.Payload.TryGetValue("isOnBattery", out var payloadValue) &&
            bool.TryParse(payloadValue, out var parsed))
        {
            isOnBattery = parsed;
        }

        return mode switch
        {
            "ac-connected" => isOnBattery == false,
            "ac-disconnected" => isOnBattery == true,
            _ => false
        };
    }

    /// <summary>前台程序：参数为空表示不设门槛。</summary>
    private static bool MatchForegroundProcess(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "process", out var expected))
        {
            return true;
        }

        var actualName = NormalizeProcessName(context.ForegroundProcessName);
        return actualName.Length > 0 &&
               string.Equals(actualName, NormalizeProcessName(expected), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>窗口标题：包含即命中，参数为空表示不设门槛。</summary>
    private static bool MatchWindowTitle(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "value", out var expected))
        {
            return true;
        }

        return context.ForegroundWindowTitle is { } title &&
               title.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchFullscreenApp(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "value", out var value))
        {
            return true;
        }

        var expectFullscreen = !string.Equals(value, "windowed", StringComparison.OrdinalIgnoreCase);
        return context.IsFullscreenAppActive is { } isFullscreen && isFullscreen == expectFullscreen;
    }

    private static bool MatchGameRunning(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "value", out var value))
        {
            return true;
        }

        var expectRunning = !string.Equals(value, "not-running", StringComparison.OrdinalIgnoreCase);
        return context.IsGameLikelyRunning is { } isRunning && isRunning == expectRunning;
    }

    /// <summary>电池电量：comparison=less-than 时取「小于等于」，否则「大于等于」。</summary>
    private static bool MatchBatteryLevel(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "percent", out var rawPercent) ||
            !TryParseDigits(rawPercent, out var percent))
        {
            return true;
        }

        if (context.BatteryPercent is not { } batteryPercent)
        {
            return false;
        }

        var lessThan = condition.Parameters.TryGetValue("comparison", out var comparison) &&
                       string.Equals(comparison, "less-than", StringComparison.OrdinalIgnoreCase);
        return lessThan ? batteryPercent <= percent : batteryPercent >= percent;
    }

    private static bool MatchChargingState(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "value", out var value))
        {
            return true;
        }

        var expectCharging = !string.Equals(value, "not-charging", StringComparison.OrdinalIgnoreCase);
        return context.IsCharging is { } isCharging && isCharging == expectCharging;
    }

    private static bool MatchNetworkName(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "value", out var expected))
        {
            return true;
        }

        return context.NetworkName is { } networkName &&
               (string.Equals(networkName, expected, StringComparison.OrdinalIgnoreCase) ||
                networkName.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchThemeMode(AutomationConditionDefinition condition, SystemContext context)
    {
        if (!TryGetParameter(condition, "value", out var value))
        {
            return true;
        }

        var expectDark = string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase);
        return context.IsDarkMode is { } isDarkMode && isDarkMode == expectDark;
    }

    /// <summary>参数缺失或为空时返回 false，调用方据此按「不设门槛」放行。</summary>
    private static bool TryGetParameter(AutomationConditionDefinition condition, string key, out string value)
    {
        if (condition.Parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryParseDigits(string text, out int value)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out value);
    }

    private static string NormalizeProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().Trim('"');
        var separatorIndex = trimmed.LastIndexOfAny(['\\', '/']);
        if (separatorIndex >= 0)
        {
            trimmed = trimmed[(separatorIndex + 1)..];
        }

        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }
}
