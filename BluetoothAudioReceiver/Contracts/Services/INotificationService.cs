using System.Collections.Generic;

namespace BluetoothAudioReceiver.Contracts.Services;

/// <summary>
/// OSD notification service.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// 通过统一请求模型显示通知，便于其他页面复用同一个悬浮通知模块。
    /// </summary>
    void Show(NotificationRequest request);

    /// <summary>
    /// 通过统一请求模型显示预览通知，不受抑制条件影响。
    /// </summary>
    void ShowPreview(NotificationRequest request);

    /// <summary>
    /// Show a notification at the bottom of the screen.
    /// </summary>
    /// <param name="iconGlyph">Icon glyph, connected="\uE8B6" disconnected="\uE7C9".</param>
    /// <param name="message">Main message text.</param>
    /// <param name="status">Status text rendered in gray.</param>
    /// <param name="compactLayout">Use a single-line compact layout.</param>
    void Show(string iconGlyph, string message, string status = "", bool compactLayout = false);

    /// <summary>
    /// Show an automation notification with a title and detail lines.
    /// </summary>
    /// <param name="automationName">Automation name shown in the first line.</param>
    /// <param name="statusText">Short status suffix such as 启动 or 退出.</param>
    /// <param name="detailLines">Action lines shown underneath.</param>
    void ShowAutomation(string automationName, string statusText, IReadOnlyList<string> detailLines, Guid? correlationId = null);

    /// <summary>
    /// Show a single-line hotkey notification.
    /// </summary>
    /// <param name="hotkeyText">Hotkey label, for example Ctrl+Alt+K.</param>
    /// <param name="status">Status text, for example Executed or Failed.</param>
    void ShowHotkey(string hotkeyText, string status);

    /// <summary>
    /// Show a hotkey notification preview even if notification suppression is enabled.
    /// </summary>
    /// <param name="hotkeyText">Hotkey label, for example Ctrl+Alt+K.</param>
    /// <param name="status">Status text, for example Executed or Failed.</param>
    void ShowHotkeyPreview(string hotkeyText, string status);

    /// <summary>
    /// 显示通知设置页的 DeskBox 浮层测试通知。
    /// </summary>
    void ShowDeskBoxPreview();
}
