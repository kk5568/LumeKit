namespace BluetoothAudioReceiver.DeskBoxFloating.Models;

/// <summary>浮窗动画档位：快 / 标准 / 慢。每组数值直接喂给 DeskBox 的动画参数。</summary>
public static class DeskBoxFloatingAnimationTiers
{
    public const string Fast = "快";
    public const string Standard = "标准";
    public const string Slow = "慢";

    public static IReadOnlyList<string> All { get; } = [Fast, Standard, Slow];

    public static string Normalize(string? tier) => tier is Fast or Slow ? tier : Standard;

    /// <summary>OffsetY = 进场起点（窗口正下方多少 px），Show/HideDurationMs = 进场/退场时长。</summary>
    public static (double OffsetY, int ShowDurationMs, int HideDurationMs) Resolve(string? tier) =>
        Normalize(tier) switch
        {
            Fast => (110d, 220, 110),
            Slow => (180d, 460, 240),
            _ => (140d, 300, 140)
        };
}