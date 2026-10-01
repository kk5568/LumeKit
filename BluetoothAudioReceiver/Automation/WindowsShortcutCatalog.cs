namespace BluetoothAudioReceiver.Automation;

public sealed record WindowsShortcutOption(string DisplayName, string Hotkey)
{
    public string SelectionText => $"{DisplayName} | {Hotkey}";
}

public static class WindowsShortcutCatalog
{
    public static IReadOnlyList<WindowsShortcutOption> Options { get; } =
    [
        new("打开设置", "Win+I"),
        new("打开文件资源管理器", "Win+E"),
        new("显示桌面", "Win+D"),
        new("打开搜索", "Win+S"),
        new("打开快速设置", "Win+A"),
        new("打开通知中心", "Win+N"),
        new("打开剪贴板历史", "Win+V"),
        new("打开任务视图", "Win+Tab"),
        new("锁定电脑", "Win+L"),
        new("打开运行", "Win+R"),
        new("打开表情面板", "Win+."),
        new("打开小组件", "Win+W"),
        new("打开投屏", "Win+K"),
        new("打开截图工具", "Win+Shift+S"),
        new("窗口贴靠布局", "Win+Z"),
        new("最小化所有窗口", "Win+M")
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
            ? $"{displayName} | {hotkey}".Trim()
            : $"{normalizedDisplayName} | {hotkey}";
    }
}
