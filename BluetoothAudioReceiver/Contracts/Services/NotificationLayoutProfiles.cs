namespace BluetoothAudioReceiver.Contracts.Services;

/// <summary>
/// 悬浮通知布局统一入口。
/// 这里按模块分组，后续修改宽度/高度优先改这里，不要分散到各个页面或服务里。
/// </summary>
public static class NotificationLayoutProfiles
{
    /// <summary>标准通知：音频管理等常规提示使用。</summary>
    public static NotificationLayoutProfile Default { get; } = new(
        Width: 600,
        Height: 110);

    /// <summary>紧凑通知：单行、短内容提示使用。</summary>
    public static NotificationLayoutProfile Compact { get; } = new(
        Width: 420,
        Height: 110);

    /// <summary>自动化通知：标题加多行动作明细。</summary>
    public static NotificationLayoutProfile Automation { get; } = new(
        Width: 600,
        Height: 130);

    /// <summary>合并通知：第一行主通知，下面显示短时间内追加的通知。</summary>
    public static NotificationLayoutProfile Merged { get; } = new(
        Width: 600,
        Height: 180);

    /// <summary>按键通知：独立于音频管理通知的单独模块尺寸。</summary>
    public static NotificationLayoutProfile Hotkey { get; } = new(
        Width: 400,
        Height: 110);
}

/// <summary>
/// 单个通知布局配置。
/// </summary>
public sealed record NotificationLayoutProfile(int Width, int Height);
