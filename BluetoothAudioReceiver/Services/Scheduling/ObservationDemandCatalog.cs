using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 各模块提交给调度中心的采集申请。每一块代表一个模块自己的需求，
/// 以前这里是靠读这几个模块的开关去猜，现在由模块侧明确写出要什么。
/// 将来模块改造时，把对应的一块搬进模块自身即可。
/// </summary>
internal sealed class ObservationDemandCatalog : IDisposable
{
    private static readonly IReadOnlyCollection<ObservationInfoKind> CoreInfoKinds =
    [
        ObservationInfoKind.AudioRoute,
        ObservationInfoKind.ForegroundWindow,
        ObservationInfoKind.BluetoothState,
        ObservationInfoKind.BluetoothRadioState,
        ObservationInfoKind.PowerState
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
    private readonly IAutomationPipelineRepository _pipelineRepository;
    private readonly List<IDisposable> _registrations = [];

    public ObservationDemandCatalog(
        ObservationDemandRegistry registry,
        IAppSettingsService appSettingsService,
        IAutomationPipelineRepository pipelineRepository)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(appSettingsService);
        ArgumentNullException.ThrowIfNull(pipelineRepository);

        _appSettingsService = appSettingsService;
        _pipelineRepository = pipelineRepository;

        _registrations.Add(registry.Register("core", () => CoreInfoKinds));
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

            var triggerType = pipeline.Trigger?.TriggerType;
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
        return settings.AutoBrightnessNotificationEnabled ||
               settings.AutoBrightnessOnUnlockEnabled ||
               settings.AutoBrightnessOnUnlockNotificationEnabled ||
               settings.AutoBrightnessOnForegroundEnabled
            ? [ObservationInfoKind.Brightness, ObservationInfoKind.ForegroundWindow]
            : Empty;
    }

    /// <summary>自动日夜：需要主题和电源状态。</summary>
    private IReadOnlyCollection<ObservationInfoKind> GetAutoDayNightDemand()
    {
        var settings = _appSettingsService;
        return settings.AutoDayNightEnabled ||
               settings.AutoDayNightNotificationEnabled ||
               settings.DisableNightLightEnabled ||
               settings.DisableNightLightAtNightEnabled
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
                if (string.Equals(condition.ConditionType, AutomationSupportCatalog.ConditionPowerState, StringComparison.OrdinalIgnoreCase))
                {
                    kinds.Add(ObservationInfoKind.PowerState);
                }
            }
        }

        return kinds;
    }

    private static void AddTriggerDemand(HashSet<ObservationInfoKind> kinds, string? triggerType)
    {
        if (string.IsNullOrWhiteSpace(triggerType))
        {
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
