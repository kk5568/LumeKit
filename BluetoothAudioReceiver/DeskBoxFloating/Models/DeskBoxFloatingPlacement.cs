namespace BluetoothAudioReceiver.DeskBoxFloating.Models;

public enum DeskBoxFloatingPlacement
{
    BottomCenter,
    TrayIcon,
    /// <summary>托盘图标左对齐：左边缘和图标左边缘齐平，右边放不下就贴屏幕右边留安全边距。</summary>
    TrayIconLeftAligned,
    Cursor,
    ScreenCenter,
    Custom
}
