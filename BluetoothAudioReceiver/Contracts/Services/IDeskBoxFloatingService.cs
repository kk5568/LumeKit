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
    /// 显示一个由调用方自绘内容的浮层（托盘左键面板这类）。
    /// 外形（材质、圆角、阴影、进出场动画）仍由浮窗统一提供，内容只负责画里面的东西。
    /// </summary>
    void ShowContent(DeskBoxFloatingRequest request, Microsoft.UI.Xaml.FrameworkElement content);

    /// <summary>
    /// 关闭当前显示的浮层。
    /// </summary>
    void Dismiss();

    /// <summary>
    /// 浮层退场动画结束、窗口真正隐藏后触发。调用方据此串行安排下一条浮层。
    /// </summary>
    event EventHandler? Dismissed;
}
