using Microsoft.UI.Composition.SystemBackdrops;

namespace BluetoothAudioReceiver.Services;

internal static class NotificationMaterialSettings
{
    public const double DefaultWidgetOpacity = 0.80;
    public const double MinWidgetOpacity = 0.0;
    public const double MaxWidgetOpacity = 1.0;
    public const double DefaultWidgetMaterialIntensity = 0.65;
    public const double MinWidgetMaterialIntensity = 0.0;
    public const double MaxWidgetMaterialIntensity = 1.0;
    public const string MaterialTypeMica = "Mica";
    public const string MaterialTypeMicaAlt = "MicaAlt";
    public const string MaterialTypeAcrylic = "Acrylic";
    public const string MaterialTypeAcrylicBase = "AcrylicBase";
    public const string MaterialTypeSolid = "Solid";

    public static bool IsMicaMaterial(string? materialType) =>
        materialType is MaterialTypeMica or MaterialTypeMicaAlt;

    public static bool IsAcrylicMaterial(string? materialType) =>
        materialType is MaterialTypeAcrylic or MaterialTypeAcrylicBase;

    public static string ResolveMaterialType(BackdropType requestedBackdrop)
    {
        var requestedMaterialType = requestedBackdrop switch
        {
            BackdropType.Mica => MaterialTypeMica,
            BackdropType.Acrylic => MaterialTypeAcrylic,
            BackdropType.None => MaterialTypeSolid,
            BackdropType.Transparent => MaterialTypeSolid,
            _ => MaterialTypeMica
        };

        if (IsMicaMaterial(requestedMaterialType) && !SupportsMica)
        {
            return SupportsDesktopAcrylic ? MaterialTypeAcrylic : MaterialTypeSolid;
        }

        if (IsAcrylicMaterial(requestedMaterialType) && !SupportsDesktopAcrylic)
        {
            return MaterialTypeSolid;
        }

        return requestedMaterialType;
    }

    public static string ResolveNotificationMaterialType(BackdropType requestedBackdrop)
    {
        var requestedMaterialType = requestedBackdrop switch
        {
            BackdropType.Mica => MaterialTypeMica,
            BackdropType.Acrylic => MaterialTypeAcrylic,
            BackdropType.None => MaterialTypeSolid,
            BackdropType.Blur => MaterialTypeSolid,
            BackdropType.Transparent => MaterialTypeSolid,
            _ => MaterialTypeSolid
        };

        if (requestedMaterialType == MaterialTypeMica && !SupportsMica)
        {
            return SupportsDesktopAcrylic ? MaterialTypeAcrylic : MaterialTypeSolid;
        }

        if (requestedMaterialType == MaterialTypeAcrylic && !SupportsDesktopAcrylic)
        {
            return SupportsMica ? MaterialTypeMica : MaterialTypeSolid;
        }

        return requestedMaterialType;
    }

    public static bool SupportsMaterial(string materialType) =>
        IsMicaMaterial(materialType)
            ? SupportsMica
            : IsAcrylicMaterial(materialType) && SupportsDesktopAcrylic;

    private static bool SupportsMica =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
        IsSupported(() => MicaController.IsSupported());

    private static bool SupportsDesktopAcrylic =>
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
