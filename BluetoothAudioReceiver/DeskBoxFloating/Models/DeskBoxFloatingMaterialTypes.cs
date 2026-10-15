using BluetoothAudioReceiver.DeskBoxFloating.Services;

namespace BluetoothAudioReceiver.DeskBoxFloating.Models;

/// <summary>
/// 浮窗窗口材质类型。存储值沿用 DeskBox 的原串（SettingsService.WidgetMaterialType*），
/// 界面显示中文名（取自 DeskBox Strings\zh-CN.json 的 Settings.Material.*）。
/// 选项与顺序跟 DeskBox SettingsViewModel.FeatureOptions.cs:911-913 一致：
/// Win11 五个，Win10 去掉两个云母。
/// </summary>
public static class DeskBoxFloatingMaterialTypes
{
    public const string Acrylic = SettingsService.WidgetMaterialTypeAcrylic;
    public const string AcrylicBase = SettingsService.WidgetMaterialTypeAcrylicBase;
    public const string Mica = SettingsService.WidgetMaterialTypeMica;
    public const string MicaAlt = SettingsService.WidgetMaterialTypeMicaAlt;
    public const string Solid = SettingsService.WidgetMaterialTypeSolid;

    public const string AcrylicDisplay = "轻薄亚克力";
    public const string AcrylicBaseDisplay = "标准亚克力";
    public const string MicaDisplay = "云母";
    public const string MicaAltDisplay = "云母 Alt";
    public const string SolidDisplay = "纯色";

    public static IReadOnlyList<string> All { get; } = WindowsCompatibilityService.IsWindows11OrLater
        ? [AcrylicDisplay, AcrylicBaseDisplay, MicaDisplay, MicaAltDisplay, SolidDisplay]
        : [AcrylicDisplay, AcrylicBaseDisplay, SolidDisplay];

    public static string Normalize(string? materialType) =>
        materialType is Acrylic or AcrylicBase or Mica or MicaAlt or Solid
            ? materialType
            : Mica;

    public static string ToDisplay(string? materialType) => Normalize(materialType) switch
    {
        Acrylic => AcrylicDisplay,
        AcrylicBase => AcrylicBaseDisplay,
        MicaAlt => MicaAltDisplay,
        Solid => SolidDisplay,
        _ => MicaDisplay
    };

    public static string FromDisplay(string? displayName) => displayName switch
    {
        AcrylicDisplay => Acrylic,
        AcrylicBaseDisplay => AcrylicBase,
        MicaAltDisplay => MicaAlt,
        SolidDisplay => Solid,
        _ => Mica
    };
}
