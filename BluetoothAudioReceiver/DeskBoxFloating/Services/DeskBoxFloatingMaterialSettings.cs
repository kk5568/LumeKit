using Microsoft.UI.Composition.SystemBackdrops;

namespace BluetoothAudioReceiver.DeskBoxFloating.Services;

internal static class DeskBoxFloatingMaterialSettings
{
    public const double DefaultWidgetMaterialIntensity = 0.65;
    public const double MinWidgetMaterialIntensity = 0.0;
    public const double MaxWidgetMaterialIntensity = 1.0;
    public const string WidgetMaterialTypeMica = "Mica";
    public const string WidgetMaterialTypeMicaAlt = "MicaAlt";
    public const string WidgetMaterialTypeAcrylic = "Acrylic";
    public const string WidgetMaterialTypeAcrylicBase = "AcrylicBase";
    public const string WidgetMaterialTypeSolid = "Solid";

    public static bool IsMicaMaterial(string? materialType) =>
        materialType is WidgetMaterialTypeMica or WidgetMaterialTypeMicaAlt;

    public static bool IsAcrylicMaterial(string? materialType) =>
        materialType is WidgetMaterialTypeAcrylic or WidgetMaterialTypeAcrylicBase;

    public static string ResolveWidgetMaterialType(string? requestedMaterialType)
    {
        string materialType = requestedMaterialType is
            WidgetMaterialTypeMica or
            WidgetMaterialTypeMicaAlt or
            WidgetMaterialTypeAcrylic or
            WidgetMaterialTypeAcrylicBase or
            WidgetMaterialTypeSolid
                ? requestedMaterialType
                : WidgetMaterialTypeAcrylicBase;

        if (IsMicaMaterial(materialType) && !SupportsMica)
        {
            return SupportsDesktopAcrylic ? WidgetMaterialTypeAcrylicBase : WidgetMaterialTypeSolid;
        }

        if (IsAcrylicMaterial(materialType) && !SupportsDesktopAcrylic)
        {
            return SupportsMica ? WidgetMaterialTypeMica : WidgetMaterialTypeSolid;
        }

        return materialType;
    }

    public static bool SupportsMaterial(string materialType) =>
        IsMicaMaterial(materialType)
            ? SupportsMica
            : IsAcrylicMaterial(materialType) && SupportsDesktopAcrylic;

    public static bool SupportsMica =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
        IsSupported(() => MicaController.IsSupported());

    public static bool SupportsDesktopAcrylic =>
        IsSupported(() => DesktopAcrylicController.IsSupported());

    private static bool IsSupported(Func<bool> probe)
    {
        try
        {
            return probe();
        }
        catch
        {
            return false;
        }
    }
}
