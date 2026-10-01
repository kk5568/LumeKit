using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class HotkeyNotificationCard : ObservableObject
{
    public const string DefaultHotkey = "Ctrl+Alt+K";
    public const string DefaultStatus = "已按下";

    public required string Id { get; init; }

    [ObservableProperty]
    public partial string Hotkey { get; set; } = DefaultHotkey;

    [ObservableProperty]
    public partial string StatusText { get; set; } = DefaultStatus;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    public string HotkeyDisplay => NormalizeOrDefault(Hotkey, "未设置");

    public string StatusDisplay => NormalizeOrDefault(StatusText, DefaultStatus);

    public string StatusLineText => $"状态：{StatusDisplay}";

    public string EnabledText => IsEnabled ? "已启用" : "已停用";

    private static string NormalizeOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    partial void OnHotkeyChanged(string value)
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusLineText));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnabledText));
    }
}
