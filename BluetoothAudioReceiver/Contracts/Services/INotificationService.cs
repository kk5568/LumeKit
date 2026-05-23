namespace BluetoothAudioReceiver.Contracts.Services;

/// <summary>
/// OSD 通知服务接口
/// 调用方：TrayMenuControl.OnDeviceConnected / OnDeviceDisconnected
/// 实现：NotificationService
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// 显示屏幕底部 OSD 通知
    /// </summary>
    /// <param name="iconGlyph">图标字符，连接="\uE8B6" 断开="\uE7C9"</param>
    /// <param name="message">通知文字，如 "设备名 蓝牙音频已连接"</param>
    void Show(string iconGlyph, string message, string status = "");
}
