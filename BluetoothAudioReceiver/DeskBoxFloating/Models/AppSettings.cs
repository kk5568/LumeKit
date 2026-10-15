// DeskBox Models\AppSettings.cs 里这四项转发到 WidgetShellSettingsSlice；
// LumeKit 浮窗没有设置切片，这里按 DeskBox 的常量给（Services\SettingsService.cs:92-119）。
// 默认动画与 DeskBox 出厂默认（Fade / Right）不同：浮窗贴在屏幕底部，
// 按用户要求进场“从下往上”，用 SlideDownFade + Down
// （DeskBox 算法里 Down 才是从工作区下沿往上滑，Up 是从上沿往下滑）。
// 时长与位移按“快 / 标准 / 慢”三档给，档位由设置页写入（DeskBoxFloatingAnimationTiers.cs）。
using BluetoothAudioReceiver.DeskBoxFloating.Services;

namespace BluetoothAudioReceiver.DeskBoxFloating.Models;

public sealed class AppSettings
{
    public static AppSettings Current { get; } = new();

    public string WidgetAnimationEffect { get; set; } = SettingsService.WidgetAnimationEffectSlideDownFade;

    public string WidgetAnimationSpeed { get; set; } = SettingsService.WidgetAnimationSpeedStandard;

    public string WidgetAnimationSlideDirection { get; set; } = SettingsService.WidgetAnimationSlideDirectionDown;

    /// <summary>动画档位：快 / 标准 / 慢（AppSettingsService 存档并同步到这里）。</summary>
    public string TrayAnimationTier { get; set; } = DeskBoxFloatingAnimationTiers.Standard;

    /// <summary>
    /// 统一布局开关：true = 新设计（标题和正文当一整块在窗口里垂直居中、无分隔线、高度贴合内容）；
    /// false = 旧设计（标题行 40 DIP + 分隔线、标题靠左、高度固定用档案值）。
    /// 旧设计那一套才是 DeskBox Controls\WidgetShell.xaml:36-39、184-190 的做法。
    /// </summary>
    public bool TrayUnifiedLayout { get; set; } = true;

    /// <summary>窗口材质类型（Mica / MicaAlt / Acrylic / AcrylicBase / Solid），由设置页写入。</summary>
    public string TrayMaterialType { get; set; } = SettingsService.DefaultWidgetMaterialType;

    /// <summary>进场起点：窗口正下方多少 px（DeskBox 的 down 只算到工作区下沿 + 16px，贴底浮窗看不清滑动）。</summary>
    public double? TraySlideOffsetOverrideY => DeskBoxFloatingAnimationTiers.Resolve(TrayAnimationTier).OffsetY;

    /// <summary>进场时长（DeskBox 进出场共用一个时长，这里拆成两档）。</summary>
    public int TrayShowDurationMs => DeskBoxFloatingAnimationTiers.Resolve(TrayAnimationTier).ShowDurationMs;

    /// <summary>退场时长。</summary>
    public int TrayHideDurationMs => DeskBoxFloatingAnimationTiers.Resolve(TrayAnimationTier).HideDurationMs;

    /// <summary>退场曲线：None = 线性，全程同速，没有慢起。</summary>
    public string WidgetAnimationHideEasingIntensity { get; set; } = SettingsService.WidgetAnimationEasingNone;

    public string WidgetAnimationEasingIntensity { get; set; } = SettingsService.WidgetAnimationEasingStandard;
}