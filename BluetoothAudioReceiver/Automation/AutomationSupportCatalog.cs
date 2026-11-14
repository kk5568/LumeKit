namespace BluetoothAudioReceiver.Automation;

internal static class AutomationSupportCatalog
{
public const string TriggerForegroundAppChanged = AutomationTriggerCatalog.AppForegroundChanged;
public const string TriggerProcessStopped = AutomationTriggerCatalog.AppStopped;
public const string TriggerPerformanceModeEntered = AutomationTriggerCatalog.PerformanceModeEntered;
public const string TriggerPerformanceModeExited = AutomationTriggerCatalog.PerformanceModeExited;
public const string TriggerDynamicPerformanceChanged = AutomationTriggerCatalog.DynamicPerformanceChanged;
public const string TriggerPowerStateChanged = AutomationTriggerCatalog.PowerChanged;

    public const string ConditionPowerState = "power-state";
    public const string ConditionForegroundProcess = "foreground-process";
    public const string ConditionWindowTitle = "window-title";
    public const string ConditionFullscreenApp = "fullscreen-app";
    public const string ConditionGameRunning = "game-running";
    public const string ConditionBatteryLevel = "battery-level";
    public const string ConditionChargingState = "charging-state";
    public const string ConditionNetworkName = "network-name";
    public const string ConditionThemeMode = "theme-mode";
    public const string TriggerSystemSignal = "trigger.system.signal";

    public const string ActionSendGHelperHotkey = "send-ghelper-hotkey";
    public const string ActionSendWindowsHotkey = "send-windows-hotkey";
    public const string ActionSendCustomHotkey = "send-custom-hotkey";
    public const string ActionSendHotkey = "send-hotkey";
    public const string ActionDisableTouchpad = "disable-touchpad";
    public const string ActionEnableTouchpad = "enable-touchpad";
    public const string ActionSetBrightness = "set-brightness";
    public const string ActionSetInputDevice = "set-input-device";
    public const string ActionSetPowerMode = "set-power-mode";
    public const string ActionSetTheme = "set-theme";
    public const string ActionShowNotification = "show-notification";
    public const string ActionSetOutputDevice = "set-output-device";
    public const string ActionProcessControl = "process-control";
    public const string ActionDelaySeconds = "delay-seconds";
    public const string ActionStopExecution = "stop-execution";

    public static IReadOnlySet<string> SupportedTriggerTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AutomationTriggerCatalog.AppForegroundChanged,
            AutomationTriggerCatalog.AppStopped,
            AutomationTriggerCatalog.PerformanceModeEntered,
            AutomationTriggerCatalog.PerformanceModeExited,
            AutomationTriggerCatalog.DynamicPerformanceChanged,
            AutomationTriggerCatalog.PowerChanged,
            AutomationTriggerCatalog.BluetoothConnected,
            AutomationTriggerCatalog.OutputDeviceChanged,
            AutomationTriggerCatalog.SunriseReached,
            AutomationTriggerCatalog.SunsetReached
        };

    public static IReadOnlySet<string> SupportedConditionTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ConditionPowerState,
            ConditionForegroundProcess,
            ConditionWindowTitle,
            ConditionFullscreenApp,
            ConditionGameRunning,
            ConditionBatteryLevel,
            ConditionChargingState,
            ConditionNetworkName,
            ConditionThemeMode
        };

    public static IReadOnlySet<string> SupportedActionTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ActionSendGHelperHotkey,
            ActionSendWindowsHotkey,
            ActionSendCustomHotkey,
            ActionSendHotkey,
            ActionDisableTouchpad,
            ActionEnableTouchpad,
            ActionSetBrightness,
            ActionSetInputDevice,
            ActionSetPowerMode,
            ActionSetTheme,
            ActionShowNotification,
            ActionSetOutputDevice,
            ActionProcessControl,
            ActionDelaySeconds,
            ActionStopExecution
        };

    public static bool IsSupportedTrigger(string? triggerType)
        => !string.IsNullOrWhiteSpace(triggerType) && SupportedTriggerTypes.Contains(triggerType);

    public static bool IsSupportedCondition(string? conditionType)
        => !string.IsNullOrWhiteSpace(conditionType) && SupportedConditionTypes.Contains(conditionType);

    public static bool IsSupportedAction(string? actionType)
        => !string.IsNullOrWhiteSpace(actionType) && SupportedActionTypes.Contains(actionType);
}
