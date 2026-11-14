using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Automation;

internal static class AutomationPerformanceRuleCatalog
{
    public const string Ac = "dynamic-performance.ac";
    public const string Battery = "dynamic-performance.battery";

    public static IReadOnlyList<AutomationPipeline> CreateDefaults()
        => [
            Create(Ac, "动态性能 · 插电", "ac"),
            Create(Battery, "动态性能 · 电池", "battery")
        ];

    private static AutomationPipeline Create(string id, string name, string powerSource)
        => new()
        {
            SystemRuleId = id,
            Name = name,
            Category = "性能模式",
            Trigger = new AutomationTriggerDefinition
            {
                TriggerType = AutomationTriggerCatalog.DynamicPerformanceChanged,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["powerSource"] = powerSource
                }
            },
            Steps = [
                CreateStep("quiet", "安静模式 | Ctrl+Shift+Alt+F16"),
                CreateStep("balanced", "平衡模式 | Ctrl+Shift+Alt+F17"),
                CreateStep("high", "增强模式 | Ctrl+Shift+Alt+F18")]
        };

    private static AutomationStepDefinition CreateStep(string stage, string shortcut)
        => new()
        {
            ActionType = AutomationSupportCatalog.ActionSendGHelperHotkey,
            DynamicPerformanceStage = stage,
            ParametersJson = AutomationActionParameterHelper.BuildValue(shortcut)
        };
}
