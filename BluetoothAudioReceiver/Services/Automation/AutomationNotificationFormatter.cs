using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Services;

internal static class AutomationNotificationFormatter
{
    public static string ResolveAutomationStatusText(string finalResult)
        => string.Equals(finalResult, "completed", StringComparison.OrdinalIgnoreCase) ? "启动" : "退出";

    public static IReadOnlyList<string> BuildAutomationDetailLines(AutomationPipeline pipeline, string finalResult)
    {
        var primary = pipeline.Steps.Select(ResolveAutomationStepText).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        return [string.IsNullOrWhiteSpace(primary)
            ? (string.Equals(finalResult, "completed", StringComparison.OrdinalIgnoreCase) ? "已执行" : "执行结束")
            : primary!];
    }

    private static string ResolveAutomationStepText(AutomationStepDefinition step)
    {
        return step.ActionType switch
        {
            AutomationSupportCatalog.ActionSendGHelperHotkey => $"G-Helper 热键 · {AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadValue(step.ParametersJson))}",
            AutomationSupportCatalog.ActionSendWindowsHotkey => $"Windows 热键 · {AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadValue(step.ParametersJson))}",
            AutomationSupportCatalog.ActionSendCustomHotkey => $"自定义热键 · {AutomationActionParameterHelper.ReadValue(step.ParametersJson)}",
            AutomationSupportCatalog.ActionSendHotkey => $"热键 · {AutomationActionParameterHelper.ReadValue(step.ParametersJson)}",
            AutomationSupportCatalog.ActionDisableTouchpad => "触摸板 · 关闭",
            AutomationSupportCatalog.ActionEnableTouchpad => "触摸板 · 开启",
            AutomationSupportCatalog.ActionSetBrightness => $"亮度 · {AutomationActionParameterHelper.ReadBrightness(step.ParametersJson, 80)}%",
            AutomationSupportCatalog.ActionSetInputVolume => $"音量 · {AutomationActionParameterHelper.ReadInputVolumePercentage(step.ParametersJson, 50)}%",
            AutomationSupportCatalog.ActionSetInputDevice => $"输入音频 · {AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadInputDeviceId(step.ParametersJson))}",
            AutomationSupportCatalog.ActionSetOutputDevice => $"输出设备 · {AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadOutputDeviceId(step.ParametersJson))}",
            AutomationSupportCatalog.ActionSetPowerMode => $"电源模式 · {AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadPowerModeDisplayName(step.ParametersJson))}",
            AutomationSupportCatalog.ActionSetTheme => $"深色模式 · {ResolveThemeLabel(step.ParametersJson)}",
            AutomationSupportCatalog.ActionShowNotification => $"通知 · {AutomationDisplayTextConverter.ToDisplayText(AutomationActionParameterHelper.ReadValue(step.ParametersJson))}",
            AutomationSupportCatalog.ActionProcessControl => ResolveProcessControlText(step.ParametersJson),
            AutomationSupportCatalog.ActionDelaySeconds => $"等待 · {AutomationActionParameterHelper.ReadDelaySeconds(step.ParametersJson, 0)} 秒",
            AutomationSupportCatalog.ActionStopExecution => "停止执行",
            _ => string.Empty
        };
    }

    private static string ResolveProcessControlText(string parameters)
    {
        var (operation, target) = AutomationActionParameterHelper.ReadProcessOperation(parameters);
        if (string.Equals(operation, "close", StringComparison.OrdinalIgnoreCase))
        {
            return $"关闭程序 · {target}";
        }

        if (string.Equals(operation, "launch", StringComparison.OrdinalIgnoreCase))
        {
            return target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? $"打开网页 · {target}"
                : $"启动程序 · {Path.GetFileName(target)}";
        }

        return string.Empty;
    }

    private static string ResolveThemeLabel(string parameters)
    {
        var value = AutomationActionParameterHelper.ReadValue(parameters);
        if (value.Contains("light", StringComparison.OrdinalIgnoreCase))
        {
            return "浅色";
        }

        if (value.Contains("system", StringComparison.OrdinalIgnoreCase))
        {
            return "跟随系统";
        }

        return "深色";
    }
}
