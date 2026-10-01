using BluetoothAudioReceiver.DeskBoxFloating.Models;

namespace BluetoothAudioReceiver.DeskBoxFloating.Profiles;

/// <summary>
/// 新浮层的可变设计参数。新增场景优先增加配置档，不修改窗口代码。
/// </summary>
public static class DeskBoxFloatingProfiles
{
    public static DeskBoxFloatingLayoutProfile NotificationDefault { get; } = new(
        "Notification.Default", 600, 110, 320, 80, 900, 240,
        new(16, 0, 16, 0), 8, 30, DeskBoxFloatingContentLayout.SingleLine,
        DeskBoxFloatingPlacement.BottomCenter, true, 2500, false);

    public static DeskBoxFloatingLayoutProfile NotificationCompact { get; } = new(
        "Notification.Compact", 420, 110, 300, 80, 700, 180,
        new(16, 0, 16, 0), 8, 30, DeskBoxFloatingContentLayout.SingleLine,
        DeskBoxFloatingPlacement.BottomCenter, true, 2500, false);

    public static DeskBoxFloatingLayoutProfile NotificationAutomation { get; } = new(
        "Notification.Automation", 600, 130, 360, 90, 900, 300,
        new(16, 12, 16, 12), 8, 30, DeskBoxFloatingContentLayout.TitleAndDescription,
        DeskBoxFloatingPlacement.BottomCenter, true, 5000, false);

    public static DeskBoxFloatingLayoutProfile NotificationMerged { get; } = new(
        "Notification.Merged", 600, 180, 360, 120, 900, 360,
        new(16, 12, 16, 12), 8, 30, DeskBoxFloatingContentLayout.NotificationList,
        DeskBoxFloatingPlacement.BottomCenter, true, 5000, true);

    public static DeskBoxFloatingLayoutProfile NotificationHotkey { get; } = new(
        "Notification.Hotkey", 400, 110, 280, 80, 700, 180,
        new(16, 0, 16, 0), 8, 30, DeskBoxFloatingContentLayout.SingleLine,
        DeskBoxFloatingPlacement.BottomCenter, true, 2500, false);

    public static DeskBoxFloatingLayoutProfile TrayQuickPanel { get; } = new(
        "Tray.QuickPanel", 260, 400, 240, 260, 650, 720,
        new(0), 8, 30, DeskBoxFloatingContentLayout.DeviceList,
        DeskBoxFloatingPlacement.TrayIcon, false, 0, true);

    public static DeskBoxFloatingLayoutProfile TrayContextMenu { get; } = new(
        "Tray.ContextMenu", 192, 116, 176, 36, 420, 720,
        new(0, 4, 0, 4), 0, 18, DeskBoxFloatingContentLayout.CommandMenu,
        DeskBoxFloatingPlacement.TrayIcon, false, 0, false);

    public static IReadOnlyList<DeskBoxFloatingLayoutProfile> All { get; } =
    [
        NotificationDefault, NotificationCompact, NotificationAutomation,
        NotificationMerged, NotificationHotkey, TrayQuickPanel, TrayContextMenu
    ];

    public static DeskBoxFloatingLayoutProfile Resolve(
        string? profileId,
        DeskBoxFloatingKind kind)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            var match = All.FirstOrDefault(profile =>
                string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return kind switch
        {
            DeskBoxFloatingKind.Panel => TrayQuickPanel,
            DeskBoxFloatingKind.Menu => TrayContextMenu,
            _ => NotificationDefault
        };
    }
}
