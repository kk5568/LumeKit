namespace BluetoothAudioReceiver.DeskBoxFloating.Models;

/// <summary>
/// 现有场景的兼容分类。新增设计优先使用 ProfileId，不再扩展这个枚举。
/// </summary>
public enum DeskBoxFloatingKind
{
    Notification,
    Panel,
    Menu
}
