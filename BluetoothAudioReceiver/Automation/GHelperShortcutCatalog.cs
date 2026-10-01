namespace BluetoothAudioReceiver.Automation;

public sealed record GHelperShortcutOption(string DisplayName, string Hotkey)
{
    public string SelectionText => $"{DisplayName} | {Hotkey}";
}

public static class GHelperShortcutCatalog
{
    public static IReadOnlyList<GHelperShortcutOption> Options { get; } =
    [
        new("性能模式循环", "Ctrl+Shift+F5"),
        new("性能模式循环（反向）", "Ctrl+Shift+Alt+F5"),
        new("安静模式", "Ctrl+Shift+Alt+F16"),
        new("平衡模式", "Ctrl+Shift+Alt+F17"),
        new("增强模式", "Ctrl+Shift+Alt+F18"),
        new("自定义模式 1", "Ctrl+Shift+Alt+F19"),
        new("自定义模式 2", "Ctrl+Shift+Alt+F20"),
        new("打开 G-Helper", "Ctrl+Shift+F12"),
        new("打开 Overlay", "Ctrl+Shift+Alt+O"),
        new("XGM 切换", "Ctrl+Shift+Alt+F21"),
        new("亮度降低", "Ctrl+Volumedown"),
        new("亮度提高", "Ctrl+Volumeup"),
        new("键盘背光降低", "Shift+Volumedown"),
        new("键盘背光提高", "Shift+Volumeup"),
        new("亮度细调降低", "Ctrl+Shift+Alt+F7"),
        new("亮度细调提高", "Ctrl+Shift+Alt+F8"),
        new("触摸屏开关", "Ctrl+Shift+Alt+F6"),
        new("FPS 限制开关", "Ctrl+Shift+Alt+F9"),
        new("刷新率切换", "Ctrl+Shift+Alt+F13"),
        new("GPU Eco 模式", "Ctrl+Shift+Alt+F14"),
        new("GPU Standard 模式", "Ctrl+Shift+Alt+F15"),
        new("麦克风静音", "Ctrl+Shift+F20"),
        new("截图", "F6"),
        new("音量静音", "F1"),
        new("睡眠", "F11")
    ];

    public static IReadOnlyList<string> SelectionTexts { get; } =
        Options.Select(option => option.SelectionText).ToArray();

    public static string ResolveHotkey(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
        {
            return string.Empty;
        }

        var separatorIndex = selection.LastIndexOf('|');
        return separatorIndex >= 0
            ? selection[(separatorIndex + 1)..].Trim()
            : selection.Trim();
    }

    public static string ResolveDisplayName(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
        {
            return string.Empty;
        }

        var normalized = NormalizeSelectionText(selection);
        var separatorIndex = normalized.LastIndexOf('|');
        return separatorIndex >= 0
            ? normalized[..separatorIndex].Trim()
            : normalized.Trim();
    }

    public static string NormalizeSelectionText(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
        {
            return string.Empty;
        }

        var separatorIndex = selection.LastIndexOf('|');
        if (separatorIndex < 0)
        {
            return selection.Trim();
        }

        var displayName = selection[..separatorIndex].Trim();
        var hotkey = selection[(separatorIndex + 1)..].Trim();
        var normalizedDisplayName = Options
            .FirstOrDefault(option => string.Equals(option.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName;

        return string.IsNullOrWhiteSpace(normalizedDisplayName)
            ? selection.Trim()
            : $"{normalizedDisplayName} | {hotkey}";
    }
}
