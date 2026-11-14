using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Models;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 各模块提交给调度中心的采集申请。每一块代表一个模块自己的需求，
/// 以前这里是靠读这几个模块的开关去猜，现在由模块侧明确写出要什么。
/// 将来模块改造时，把对应的一块搬进模块自身即可。
/// </summary>
internal sealed class ObservationDemandCatalog : IDisposable
{
    private static readonly AutomationTriggerRegistry TriggerRegistry = new();
    private static readonly IReadOnlyCollection<ObservationInfoKind> CoreInfoKinds =
    [
        ObservationInfoKind.ForegroundWindow,
        ObservationInfoKind.PowerState
    ];

    // 设备展示快照不是后台效率/亮度的输入；启用的设备规则会另外登记 Required 需求。
    private static readonly IReadOnlyCollection<ObservationInfoKind> DevicePresentationKinds =
    [
        ObservationInfoKind.AudioRoute,
        ObservationInfoKind.BluetoothState,
        ObservationInfoKind.BluetoothRadioState
    ];

    /// <summary>
    /// 这些事件源在界面上有展示（前台程序、蓝牙、音频设备、电量、网络、主题、锁屏状态），
    /// 或者被后台效率、自动亮度这些常驻模块消费，所以任何时候都保持订阅。
    /// 它们都是被动的系统事件，不订阅就只是不更新，订阅着也不占 CPU。
    /// </summary>
    private static readonly IReadOnlyCollection<ObservationSourceKind> CoreSourceKinds =
    [
        ObservationSourceKind.AudioPlaybackDevices,
        ObservationSourceKind.BluetoothRadioState,
        ObservationSourceKind.SunScheduleState,
        ObservationSourceKind.PowerSupply,
        ObservationSourceKind.AudioRenderDevice,
        ObservationSourceKind.NetworkStatus,
        ObservationSourceKind.SessionPowerMode,
        ObservationSourceKind.SystemTime,
        ObservationSourceKind.UserPreference,
        ObservationSourceKind.ForegroundWindow
    ];

    private static readonly IReadOnlyCollection<ObservationInfoKind> Empty = [];
    private static readonly IReadOnlyCollection<ObservationSourceKind> EmptySources = [];

    private readonly IAppSettingsService _appSettingsService;
    private readonly BrightnessSettings _brightnessSettings;
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly List<IDisposable> _registrations = [];

    public ObservationDemandCatalog(
        ObservationDemandRegistry registry,
        IAppSettingsService appSettingsService,
        BrightnessSettings brightnessSettings,
        IAutomationPipelineRepository pipelineRepository)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(appSettingsService);
        ArgumentNullException.ThrowIfNull(brightnessSettings);
        ArgumentNullException.ThrowIfNull(pipelineRepository);

        _appSettingsService = appSettingsService;
        _brightnessSettings = brightnessSettings;
        _pipelineRepository = pipelineRepository;

        _registrations.Add(registry.Register("core", () => CoreInfoKinds));
        _registrations.Add(registry.Register("device-presentation", () => DevicePresentationKinds,
            ObservationDemandPurpose.WindowPresentation));
        _registrations.Add(registry.Register("auto-brightness", GetAutoBrightnessDemand));
        _registrations.Add(registry.Register("auto-day-night", GetAutoDayNightDemand));
        _registrations.Add(registry.Register("proxy-notification", GetProxyNotificationDemand));
        _registrations.Add(registry.Register("automation", GetAutomationDemand));

        _registrations.Add(registry.RegisterSources("core", () => CoreSourceKinds));
        _registrations.Add(registry.RegisterSources("proxy-notification", GetProxyNotificationSourceDemand));
        _registrations.Add(registry.RegisterSources("automation", GetAutomationSourceDemand));
    }

    /// <summary>代理通知：只有它在意代理变化，关掉这项功能就没人需要注册表监听。</summary>
    private IReadOnlyCollection<ObservationSourceKind> GetProxyNotificationSourceDemand()
        => _appSettingsService.ProxyStatusNotificationEnabled
            ? [ObservationSourceKind.ProxyRegistryWatch]
            : EmptySources;

    /// <summary>自动化：只有进程监听和日出循环是「有活干才跑」，其余源由 core 常驻。</summary>
    private IReadOnlyCollection<ObservationSourceKind> GetAutomationSourceDemand()
    {
        var sources = new HashSet<ObservationSourceKind>();
        foreach (var pipeline in _pipelineRepository.Pipelines)
        {
            if (!pipeline.IsEnabled)
            {
                continue;
            }

            var triggerType = TriggerRegistry.Normalize(pipeline.Trigger?.TriggerType);
            if (string.IsNullOrWhiteSpace(triggerType))
            {
                continue;
            }

            if (string.Equals(triggerType, AutomationTriggerCatalog.AppForegroundChanged, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(triggerType, AutomationTriggerCatalog.AppStopped, StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(ObservationSourceKind.ProcessLifecycle);
            }
            else if (string.Equals(triggerType, AutomationTriggerCatalog.SunriseReached, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(triggerType, AutomationTriggerCatalog.SunsetReached, StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(ObservationSourceKind.SunScheduleLoop);
            }
        }

        return sources;
    }

    /// <summary>自动亮度：需要亮度和前台窗口。</summary>
    private IReadOnlyCollection<ObservationInfoKind> GetAutoBrightnessDemand()
    {
        var settings = _appSettingsService;
        return _brightnessSettings.AutoEnabled &&
               (settings.AutoBrightnessOnUnlockEnabled || settings.AutoBrightnessOnForegroundEnabled)
            ? [ObservationInfoKind.Brightness, ObservationInfoKind.ForegroundWindow]
            : Empty;
    }

    /// <summary>自动日夜：需要主题和电源状态。</summary>
    private IReadOnlyCollection<ObservationInfoKind> GetAutoDayNightDemand()
    {
        var settings = _appSettingsService;
        return settings.AutoDayNightEnabled
            ? [ObservationInfoKind.ThemeMode, ObservationInfoKind.PowerState]
            : Empty;
    }

    /// <summary>代理通知：需要网络和代理状态。</summary>
    private IReadOnlyCollection<ObservationInfoKind> GetProxyNotificationDemand()
        => _appSettingsService.ProxyStatusNotificationEnabled
            ? [ObservationInfoKind.NetworkState, ObservationInfoKind.ProxyState]
            : Empty;

    /// <summary>自动化：按已启用的规则实际用到的触发器和条件反推。</summary>
    private IReadOnlyCollection<ObservationInfoKind> GetAutomationDemand()
    {
        var kinds = new HashSet<ObservationInfoKind>();
        foreach (var pipeline in _pipelineRepository.Pipelines)
        {
            if (!pipeline.IsEnabled)
            {
                continue;
            }

            AddTriggerDemand(kinds, pipeline.Trigger?.TriggerType);

            if (pipeline.Conditions is null)
            {
                continue;
            }

            foreach (var condition in pipeline.Conditions)
            {
                AddConditionDemand(kinds, condition.ConditionType);
            }
        }

        return kinds;
    }

    // 太阳定时刷新只服务太阳触发器及其条件，不再刷新所有模块的并集。
    internal static IReadOnlyCollection<ObservationInfoKind> GetSunScheduleDemand(IAutomationPipelineRepository repository)
    {
        var kinds = new HashSet<ObservationInfoKind>();
        foreach (var pipeline in repository.Pipelines.Where(p => p.IsEnabled))
        {
            var trigger = TriggerRegistry.Normalize(pipeline.Trigger?.TriggerType);
            if (!string.Equals(trigger, AutomationTriggerCatalog.SunriseReached, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trigger, AutomationTriggerCatalog.SunsetReached, StringComparison.OrdinalIgnoreCase)) continue;
            kinds.Add(ObservationInfoKind.ThemeMode);
            if (pipeline.Conditions is null) continue;
            foreach (var condition in pipeline.Conditions) AddConditionDemand(kinds, condition.ConditionType);
        }
        return kinds;
    }

    private static void AddConditionDemand(HashSet<ObservationInfoKind> kinds, string? conditionType)
    {
        if (string.IsNullOrWhiteSpace(conditionType))
        {
            return;
        }

        if (string.Equals(conditionType, AutomationSupportCatalog.ConditionForegroundProcess, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionType, AutomationSupportCatalog.ConditionWindowTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionType, AutomationSupportCatalog.ConditionFullscreenApp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionType, AutomationSupportCatalog.ConditionGameRunning, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.ForegroundWindow);
            return;
        }

        if (string.Equals(conditionType, AutomationSupportCatalog.ConditionPowerState, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionType, AutomationSupportCatalog.ConditionBatteryLevel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionType, AutomationSupportCatalog.ConditionChargingState, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.PowerState);
            return;
        }

        if (string.Equals(conditionType, AutomationSupportCatalog.ConditionNetworkName, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.NetworkState);
            return;
        }

        if (string.Equals(conditionType, AutomationSupportCatalog.ConditionThemeMode, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.ThemeMode);
        }
    }

    private static void AddTriggerDemand(HashSet<ObservationInfoKind> kinds, string? triggerType)
    {
        triggerType = TriggerRegistry.Normalize(triggerType);
        if (string.IsNullOrWhiteSpace(triggerType))
        {
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.SunriseReached, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(triggerType, AutomationTriggerCatalog.SunsetReached, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.ThemeMode);
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.AppForegroundChanged, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(triggerType, AutomationTriggerCatalog.AppStopped, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(triggerType, AutomationTriggerCatalog.PerformanceModeEntered, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(triggerType, AutomationTriggerCatalog.PerformanceModeExited, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.ForegroundWindow);
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.PowerChanged, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.PowerState);
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.BluetoothConnected, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.BluetoothState);
            return;
        }

        if (string.Equals(triggerType, AutomationTriggerCatalog.OutputDeviceChanged, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.AudioRoute);
        }
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }
}
