// Material subset of DeskBox src/DeskBox/Services/SettingsService.cs
// (DeskBox 原名保留，成员逐行照抄；LumeKit 的整体设置来源是 AppSettingsService，
//  这里只承载 DeskBox 的材质/边框常量与判定。)
namespace BluetoothAudioReceiver.DeskBoxFloating.Services;

internal static class SettingsService
{
    public const double DefaultWidgetOpacity = 0.80;
    public const double MinWidgetOpacity = 0.0;
    public const double MaxWidgetOpacity = 1.0;
    public const double DefaultWidgetMaterialIntensity = 0.65;
    public const double MinWidgetMaterialIntensity = 0.0;
    public const double MaxWidgetMaterialIntensity = 1.0;
    public const string WidgetMaterialTypeMica = "Mica";
    public const string WidgetMaterialTypeMicaAlt = "MicaAlt";
    public const string WidgetMaterialTypeAcrylic = "Acrylic";
    public const string WidgetMaterialTypeAcrylicBase = "AcrylicBase";
    public const string WidgetMaterialTypeSolid = "Solid";
    public const string WidgetBorderColorModeNeutral = "Neutral";
    public const string WidgetBorderColorModeAccent = "Accent";
    public const string WidgetBorderColorModeNone = "None";
    public const string WidgetBorderStyleNone = "None";
    public const string WidgetBorderStyleThin = "Thin";
    public const string WidgetBorderStyleMedium = "Medium";
    public const string WidgetBorderStyleThick = "Thick";
    public const string WidgetCornerPreferenceSquare = "Square";
    public const string WidgetCornerPreferenceSmall = "Small";
    public const string WidgetCornerPreferenceRound = "Round";

    /// <summary>DeskBox SettingsService.CreateDefaultSettings: 材质默认 Mica、边框 Thin、颜色模式 Neutral。</summary>
    public const string DefaultWidgetMaterialType = WidgetMaterialTypeMica;
    public const string DefaultWidgetBorderStyle = WidgetBorderStyleThin;
    public const string DefaultWidgetBorderColorMode = WidgetBorderColorModeNeutral;

    public static bool IsMicaMaterial(string? materialType) =>
        materialType is WidgetMaterialTypeMica or WidgetMaterialTypeMicaAlt;

    public static bool IsAcrylicMaterial(string? materialType) =>
        materialType is WidgetMaterialTypeAcrylic or WidgetMaterialTypeAcrylicBase;

    public static bool SupportsWidgetOpacity(string? materialType) =>
        IsAcrylicMaterial(materialType) || materialType == WidgetMaterialTypeSolid;

    public static bool SupportsMaterialIntensity(string? materialType) =>
        IsMicaMaterial(materialType) || IsAcrylicMaterial(materialType);

    public const string WidgetCompactMediaCornerFollowWidget = "FollowWidget";
    public const string WidgetCompactMediaCornerSquare = "Square";
    public const string WidgetCompactMediaCornerSmall = "Small";
    public const string WidgetCompactMediaCornerRound = "Round";

    public static string NormalizeWidgetCompactMediaCornerMode(string? value)
    {
        if (string.Equals(value, WidgetCompactMediaCornerSquare, StringComparison.OrdinalIgnoreCase))
        {
            return WidgetCompactMediaCornerSquare;
        }

        if (string.Equals(value, WidgetCompactMediaCornerSmall, StringComparison.OrdinalIgnoreCase))
        {
            return WidgetCompactMediaCornerSmall;
        }

        return string.Equals(value, WidgetCompactMediaCornerRound, StringComparison.OrdinalIgnoreCase)
            ? WidgetCompactMediaCornerRound
            : WidgetCompactMediaCornerFollowWidget;
    }
}
