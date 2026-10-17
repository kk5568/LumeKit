namespace BluetoothAudioReceiver.Services;

/// <summary>
/// 调度中心能按需开关的事件源。每个源对应一组系统事件订阅或一个后台循环，
/// 没有任何模块登记它时，这个源完全不订阅、不运行。
/// </summary>
internal enum ObservationSourceKind
{
    /// <summary>音频播放连接的接入/断开事件。</summary>
    AudioPlaybackDevices,

    /// <summary>蓝牙开关状态变化事件。</summary>
    BluetoothRadioState,

    /// <summary>自动日夜服务发布的状态变化事件。</summary>
    SunScheduleState,

    /// <summary>电源/电量变化事件。</summary>
    PowerSupply,

    /// <summary>默认播放设备变化事件。</summary>
    AudioRenderDevice,

    /// <summary>网络状态变化事件。</summary>
    NetworkStatus,

    /// <summary>系统电源模式（含锁屏/解锁）事件。</summary>
    SessionPowerMode,

    /// <summary>系统时间变化事件。</summary>
    SystemTime,

    /// <summary>系统个性化设置（主题等）变化事件。</summary>
    UserPreference,

    /// <summary>前台窗口变化钩子。</summary>
    ForegroundWindow,

    /// <summary>WMI 进程启动/退出监听（较贵，只在有规则需要时开）。</summary>
    ProcessLifecycle,

    /// <summary>每分钟检查日出日落的循环（只在有规则需要时开）。</summary>
    SunScheduleLoop,

    /// <summary>注册表代理设置监听。</summary>
    ProxyRegistryWatch
}
