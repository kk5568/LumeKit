using BluetoothAudioReceiver.DeskBoxFloating.Models;

namespace BluetoothAudioReceiver.Contracts.Services;

/// <summary>
/// DeskBox 风格公共浮层入口。
/// </summary>
public interface IDeskBoxFloatingService
{
    /// <summary>
    /// 显示一个新的设计浮层。
    /// </summary>
    void Show(DeskBoxFloatingRequest request);

    /// <summary>
    /// 关闭当前显示的浮层。
    /// </summary>
    void Dismiss();
}
