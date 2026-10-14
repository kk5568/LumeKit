// 移植自 DeskBox（引用项目\DeskBox-main\src\DeskBox）：
//   Views\WidgetWindowBase.Backdrop.cs         → ApplyBackdropPreference / 各材质控制器 / 边框
//   Views\WidgetWindowBase.Bounds.cs            → ApplyDwmBorderStyle
//   Views\QuickCaptureWidgetWindow.Appearance.cs → BuildNativeBackdropTintColor / BuildSolidColorBackdropTintColor
//                                                  / ApplySurfaceStyle / 颜色混合几个私有助手
// 仅把 DeskBox 的 Win32Helper / SettingsService.Settings / HWnd / RootElement 映射到 LumeKit 现有实现，
// 算法与调用顺序保持逐行一致；不再使用 popover 版 WidgetMaterialSystemBackdrop。

using BluetoothAudioReceiver.DeskBoxFloating.Helpers;
using BluetoothAudioReceiver.DeskBoxFloating.Services;
using BluetoothAudioReceiver.Helpers.Application;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using WinRT;
using WinRT.Interop;
using WinUIEx;

namespace BluetoothAudioReceiver.DeskBoxFloating.Views;

public sealed partial class DeskBoxFloatingWindow
{
    private readonly record struct BackdropSignature(
        bool IsDark,
        string MaterialType,
        double SurfaceOpacity,
        Windows.UI.Color TintColor,
        double MaterialIntensity);

    private static readonly ConditionalWeakTable<SolidColorBrush, object> MutableBrushes = new();
    private static readonly object MutableBrushMarker = new();

    private ICompositionSupportsSystemBackdrop? _backdropTarget;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private MicaController? _micaController;
    private bool _micaControllerAttached;
    private DesktopAcrylicController? _acrylicController;
    private bool _acrylicControllerAttached;
    private TransparentTintBackdrop? _solidColorBackdrop;
    private bool _legacyAccentBackdropActive;
    private BackdropSignature? _lastAppliedBackdropSignature;
    private Windows.UI.Color _lastLegacyAccentTintColor;
    private double _lastLegacyAccentOpacity;

    private bool IsSolidColorBackdropActive { get; set; }

    private void ApplyBackdropPreference()
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        bool isDark = RootContainer.ActualTheme == ElementTheme.Dark;
        double surfaceOpacity = Math.Clamp(SettingsService.DefaultWidgetOpacity, 0.0, 1.0);
        string requestedMaterialType = SettingsService.DefaultWidgetMaterialType;
        string materialType = WindowsCompatibilityService.ResolveWidgetMaterialType(
            requestedMaterialType);
        var tintColor = materialType == SettingsService.WidgetMaterialTypeSolid
            ? BuildSolidColorBackdropTintColor(isDark, surfaceOpacity)
            : BuildNativeBackdropTintColor(isDark);
        var signature = new BackdropSignature(
            isDark,
            materialType,
            Math.Round(surfaceOpacity, 3),
            tintColor,
            Math.Round(SettingsService.DefaultWidgetMaterialIntensity, 3));

        if (CanReuseAppliedBackdrop(signature))
        {
            ApplySurfaceStyle();
            return;
        }

        try
        {
            // The immersive-dark-mode attribute, sheet-of-glass frame and DWM
            // border depend only on the theme, not the material. Re-issuing
            // them on every material change churns DWM state natively, so they
            // only run when the theme flipped or when no signature was applied
            // yet (first apply / post-dispose / after a failed apply).
            bool dwmThemeChanged = _lastAppliedBackdropSignature is not { } lastSignature ||
                lastSignature.IsDark != isDark;
            if (dwmThemeChanged)
            {
                NotificationWin32Helper.SetWindowTheme(hwnd, isDark);
                NotificationWin32Helper.ApplyFullWindowFrame(hwnd);
                ApplyDwmBorderStyle(hwnd);
            }

            int backdropType;
            bool controllerApplied = false;

            if (materialType == SettingsService.WidgetMaterialTypeSolid)
            {
                controllerApplied = ApplySolidColorBackdrop(tintColor);
            }
            else
            {
                ClearSolidColorBackdrop();

                if (SettingsService.IsMicaMaterial(materialType))
                {
                    controllerApplied = ApplyMicaController(
                        isDark,
                        tintColor,
                        materialType == SettingsService.WidgetMaterialTypeMicaAlt);
                }

                if (!controllerApplied && SettingsService.IsAcrylicMaterial(materialType))
                {
                    controllerApplied = ApplyAcrylicController(
                        isDark,
                        tintColor,
                        surfaceOpacity,
                        materialType == SettingsService.WidgetMaterialTypeAcrylicBase);
                }
            }

            if (controllerApplied)
            {
                _legacyAccentBackdropActive = false;
                backdropType = NotificationWin32Helper.DwmSystemBackdropNone;
                NotificationWin32Helper.TrySetDwmWindowAttribute(hwnd, NotificationWin32Helper.DwmWindowAttributeSystemBackdropType, ref backdropType);
                NotificationWin32Helper.DisableAccentPolicy(hwnd);
            }
            else
            {
                backdropType = NotificationWin32Helper.DwmSystemBackdropTransientWindow;
                NotificationWin32Helper.TrySetDwmWindowAttribute(hwnd, NotificationWin32Helper.DwmWindowAttributeSystemBackdropType, ref backdropType);
                DetachAcrylicControllerTarget();
                DetachMicaControllerTarget();
                double accentOpacity = WindowsCompatibilityService.UsesLegacyWindowAcrylic &&
                    SettingsService.IsAcrylicMaterial(materialType)
                        ? WidgetMaterialVisualCalculator.CalculateLegacyAcrylicOpacity(
                            materialType == SettingsService.WidgetMaterialTypeAcrylicBase,
                            surfaceOpacity,
                            SettingsService.DefaultWidgetMaterialIntensity)
                        : Math.Min(surfaceOpacity, 0.52);
                if (WindowsCompatibilityService.UsesLegacyWindowAcrylic)
                {
                    DisposeAcrylicController();
                    DisposeMicaController();
                    SystemBackdrop = null;
                }
                _legacyAccentBackdropActive = ApplyLegacyAccentBackdrop(
                    tintColor,
                    accentOpacity);
            }

            _lastAppliedBackdropSignature = signature;
        }
        catch
        {
            _lastAppliedBackdropSignature = null;
            ClearSolidColorBackdrop();
            DisposeAcrylicController();
            DisposeMicaController();
            if (materialType == SettingsService.WidgetMaterialTypeSolid)
            {
                int backdropType = NotificationWin32Helper.DwmSystemBackdropNone;
                NotificationWin32Helper.TrySetDwmWindowAttribute(hwnd, NotificationWin32Helper.DwmWindowAttributeSystemBackdropType, ref backdropType);
                NotificationWin32Helper.DisableAccentPolicy(hwnd);
                _legacyAccentBackdropActive = false;
            }
            else
            {
                double fallbackOpacity = WindowsCompatibilityService.UsesLegacyWindowAcrylic
                    ? WidgetMaterialVisualCalculator.CalculateLegacyAcrylicOpacity(
                        materialType == SettingsService.WidgetMaterialTypeAcrylicBase,
                        surfaceOpacity,
                        SettingsService.DefaultWidgetMaterialIntensity)
                    : Math.Min(surfaceOpacity, 0.52);
                _legacyAccentBackdropActive = ApplyLegacyAccentBackdrop(
                    tintColor,
                    fallbackOpacity);
            }
        }

        ApplySurfaceStyle();
    }

    private void DisposeBackdropControllers()
    {
        ClearSolidColorBackdrop();
        DisposeAcrylicController();
        DisposeMicaController();
        _backdropConfiguration = null;
        _backdropTarget = null;
        _lastAppliedBackdropSignature = null;
    }

    private bool ApplyLegacyAccentBackdrop(Windows.UI.Color tintColor, double opacity)
    {
        _lastLegacyAccentTintColor = tintColor;
        _lastLegacyAccentOpacity = opacity;
        return NotificationWin32Helper.ApplyAccentBlur(
            WindowNative.GetWindowHandle(this),
            tintColor,
            opacity,
            enabled: true);
    }

    private bool CanReuseAppliedBackdrop(BackdropSignature signature)
    {
        if (_lastAppliedBackdropSignature != signature)
        {
            return false;
        }

        return SettingsService.IsMicaMaterial(signature.MaterialType)
            ? _micaController is not null && _micaControllerAttached
            : SettingsService.IsAcrylicMaterial(signature.MaterialType)
                ? (_acrylicController is { IsClosed: false } && _acrylicControllerAttached) ||
                  _legacyAccentBackdropActive
                : signature.MaterialType == SettingsService.WidgetMaterialTypeSolid &&
                  IsSolidColorBackdropActive &&
                  _solidColorBackdrop is not null &&
                  ReferenceEquals(SystemBackdrop, _solidColorBackdrop);
    }

    private Windows.UI.Color BuildSolidColorBackdropTintColor(
        bool isDark,
        double surfaceOpacity)
    {
        Windows.UI.Color tintColor = BuildNativeBackdropTintColor(isDark);
        return Windows.UI.Color.FromArgb(
            (byte)Math.Clamp(Math.Round(surfaceOpacity * 255), 0, 255),
            tintColor.R,
            tintColor.G,
            tintColor.B);
    }

    private bool ApplySolidColorBackdrop(Windows.UI.Color tintColor)
    {
        DetachAcrylicControllerTarget();
        DetachMicaControllerTarget();

        if (_solidColorBackdrop is null)
        {
            _solidColorBackdrop = new TransparentTintBackdrop(tintColor);
            SystemBackdrop = _solidColorBackdrop;
        }
        else
        {
            _solidColorBackdrop.TintColor = tintColor;
            if (!ReferenceEquals(SystemBackdrop, _solidColorBackdrop))
            {
                SystemBackdrop = _solidColorBackdrop;
            }
        }

        IsSolidColorBackdropActive = true;
        return true;
    }

    private void ClearSolidColorBackdrop()
    {
        IsSolidColorBackdropActive = false;
        if (_solidColorBackdrop is null)
        {
            return;
        }

        if (ReferenceEquals(SystemBackdrop, _solidColorBackdrop))
        {
            SystemBackdrop = null;
        }

        _solidColorBackdrop = null;
    }

    private static SolidColorBrush GetOrUpdateSolidColorBrush(Brush? current, Windows.UI.Color color)
    {
        if (current is SolidColorBrush brush && MutableBrushes.TryGetValue(brush, out _))
        {
            try
            {
                if (!brush.Color.Equals(color))
                {
                    brush.Color = color;
                }

                return brush;
            }
            catch (Exception)
            {
                MutableBrushes.Remove(brush);
            }
        }

        var replacement = new SolidColorBrush(color);
        MutableBrushes.Add(replacement, MutableBrushMarker);
        return replacement;
    }

    private (double Thickness, Windows.UI.Color BorderColor, Windows.UI.Color DividerColor)
        GetWidgetBorderVisuals(bool isDark, Windows.UI.Color accentColor)
    {
        WidgetBorderVisuals visuals = WidgetBorderVisualCalculator.Resolve(
            SettingsService.DefaultWidgetBorderStyle,
            SettingsService.DefaultWidgetBorderColorMode,
            isDark,
            accentColor);
        return (visuals.Thickness, visuals.BorderColor, visuals.DividerColor);
    }

    private bool ApplyMicaController(
        bool isDark,
        Windows.UI.Color tintColor,
        bool useAlt)
    {
        if (!MicaController.IsSupported())
        {
            DisposeMicaController();
            return false;
        }

        _backdropTarget ??= this.As<ICompositionSupportsSystemBackdrop>();
        _backdropConfiguration ??= new SystemBackdropConfiguration();
        _backdropConfiguration.IsInputActive = true;
        _backdropConfiguration.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

        // Kind is a mutable property: switching Mica/BaseAlt must reuse the
        // controller. Recreating system backdrop controllers on every material
        // change leaks native composition memory and DWM handles that no GC or
        // working-set trim reclaims; detached controllers stay idle instead.
        if (_micaController is null)
        {
            DetachAcrylicControllerTarget();
            _micaController = new MicaController
            {
                Kind = useAlt ? MicaKind.BaseAlt : MicaKind.Base
            };
        }

        DetachAcrylicControllerTarget();
        if (!_micaControllerAttached)
        {
            if (!_micaController.AddSystemBackdropTarget(_backdropTarget!))
            {
                DisposeMicaController();
                return false;
            }

            _micaControllerAttached = true;
            _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);
        }

        _micaController.Kind = useAlt ? MicaKind.BaseAlt : MicaKind.Base;
        _micaController.TintColor = tintColor;
        _micaController.FallbackColor = WidgetMaterialVisualCalculator.BuildMicaFallbackColor(
            isDark,
            useAlt);

        WidgetMaterialOpacityProfile profile = WidgetMaterialVisualCalculator.CalculateMica(
            isDark,
            useAlt,
            SettingsService.DefaultWidgetMaterialIntensity);
        _micaController.TintOpacity = (float)profile.TintOpacity;
        _micaController.LuminosityOpacity = (float)profile.LuminosityOpacity;
        return true;
    }

    private void DisposeMicaController()
    {
        _lastAppliedBackdropSignature = null;
        if (_micaController is null)
        {
            return;
        }

        try
        {
            _micaController.RemoveAllSystemBackdropTargets();
            _micaController.Dispose();
        }
        catch
        {
        }
        finally
        {
            _micaController = null;
            _micaControllerAttached = false;
        }
    }

    private void DetachMicaControllerTarget()
    {
        if (_micaController is null || !_micaControllerAttached)
        {
            return;
        }

        try
        {
            _micaController.RemoveAllSystemBackdropTargets();
        }
        catch
        {
        }
        finally
        {
            _micaControllerAttached = false;
        }
    }

    private bool ApplyAcrylicController(
        bool isDark,
        Windows.UI.Color tintColor,
        double surfaceOpacity,
        bool useBase)
    {
        if (!WindowsCompatibilityService.SupportsDesktopAcrylic)
        {
            DisposeAcrylicController();
            return false;
        }

        _backdropTarget ??= this.As<ICompositionSupportsSystemBackdrop>();
        _backdropConfiguration ??= new SystemBackdropConfiguration();
        _backdropConfiguration.IsInputActive = true;
        _backdropConfiguration.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        _backdropConfiguration.HighContrastBackgroundColor = isDark
            ? ColorHelper.FromArgb(0xFF, 0x20, 0x20, 0x20)
            : ColorHelper.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);

        if (_acrylicController is null || _acrylicController.IsClosed)
        {
            DetachMicaControllerTarget();
            _acrylicController = new DesktopAcrylicController
            {
                Kind = useBase ? DesktopAcrylicKind.Base : DesktopAcrylicKind.Thin
            };
        }

        DetachMicaControllerTarget();
        if (!_acrylicControllerAttached)
        {
            if (!_acrylicController.AddSystemBackdropTarget(_backdropTarget!))
            {
                DisposeAcrylicController();
                return false;
            }

            _acrylicControllerAttached = true;
            _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
        }

        _acrylicController.Kind = useBase ? DesktopAcrylicKind.Base : DesktopAcrylicKind.Thin;
        _acrylicController.TintColor = tintColor;
        _acrylicController.FallbackColor = tintColor;

        WidgetMaterialOpacityProfile profile = WidgetMaterialVisualCalculator.CalculateAcrylic(
            isDark,
            useBase,
            surfaceOpacity,
            SettingsService.DefaultWidgetMaterialIntensity);
        _acrylicController.TintOpacity = (float)profile.TintOpacity;
        _acrylicController.LuminosityOpacity = (float)profile.LuminosityOpacity;
        return true;
    }

    private void DisposeAcrylicController()
    {
        _lastAppliedBackdropSignature = null;
        if (_acrylicController is null)
        {
            return;
        }

        try
        {
            _acrylicController.RemoveAllSystemBackdropTargets();
            _acrylicController.Dispose();
        }
        catch
        {
        }
        finally
        {
            _acrylicController = null;
            _acrylicControllerAttached = false;
        }
    }

    private void DetachAcrylicControllerTarget()
    {
        if (_acrylicController is null || !_acrylicControllerAttached)
        {
            return;
        }

        try
        {
            _acrylicController.RemoveAllSystemBackdropTargets();
        }
        catch
        {
        }
        finally
        {
            _acrylicControllerAttached = false;
        }
    }

    // ── Window-specific appearance（DeskBox QuickCaptureWidgetWindow.Appearance） ──

    private Windows.UI.Color BuildNativeBackdropTintColor(bool isDark)
    {
        var accentColor = ResolveAccentColor();
        var baseColor = isDark
            ? ColorHelper.FromArgb(0xFF, 0x20, 0x22, 0x26)
            : ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        return BuildAccentSurfaceColor(
            isDark,
            accentColor,
            baseColor,
            accentMix: isDark ? 0.08 : 0.16,
            overlayMix: isDark ? 0.04 : 0.08);
    }

    private void ApplySurfaceStyle()
    {
        bool isDark = RootContainer.ActualTheme == ElementTheme.Dark;
        double surfaceOpacity = Math.Clamp(SettingsService.DefaultWidgetOpacity, 0.0, 1.0);
        var accentColor = ResolveAccentColor();
        string materialType = SettingsService.DefaultWidgetMaterialType;

        // Simplified layering: only apply surface color overlay for Solid mode.
        if (materialType is SettingsService.WidgetMaterialTypeSolid && !IsSolidColorBackdropActive)
        {
            var surfaceColor = BuildFrostedSurfaceColor(isDark, accentColor, surfaceOpacity);
            BackgroundPlate.Background = GetOrUpdateSolidColorBrush(BackgroundPlate.Background, surfaceColor);
        }
        else
        {
            BackgroundPlate.Background = GetOrUpdateSolidColorBrush(BackgroundPlate.Background, Colors.Transparent);
        }

        var (borderThickness, borderColor, dividerColor) = GetWidgetBorderVisuals(isDark, accentColor);

        BackgroundPlate.BorderThickness = new Thickness(borderThickness);
        BackgroundPlate.BorderBrush = GetOrUpdateSolidColorBrush(BackgroundPlate.BorderBrush, borderColor);
        BackgroundPlate.CornerRadius = new CornerRadius(GetCurrentSurfaceCornerRadius());
        HeaderDivider.Background = GetOrUpdateSolidColorBrush(HeaderDivider.Background, dividerColor);
    }

    private static Windows.UI.Color BuildFrostedSurfaceColor(
        bool isDark,
        Windows.UI.Color accentColor,
        double surfaceOpacity)
    {
        return ApplySurfaceOpacity(
            BuildAccentSurfaceColor(
                isDark,
                accentColor,
                isDark
                    ? ColorHelper.FromArgb(0xFF, 0x21, 0x24, 0x2A)
                    : ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                accentMix: isDark ? 0.18 : 0.18,
                overlayMix: isDark ? 0.15 : 0.04),
            Math.Clamp(surfaceOpacity, 0.0, 1.0));
    }

    private static Windows.UI.Color ApplySurfaceOpacity(Windows.UI.Color color, double opacity)
    {
        byte alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Windows.UI.Color BuildAccentSurfaceColor(
        bool isDark,
        Windows.UI.Color accentColor,
        Windows.UI.Color baseColor,
        double accentMix,
        double overlayMix)
    {
        var tintedColor = BlendColors(baseColor, accentColor, accentMix);
        var overlayColor = isDark
            ? ColorHelper.FromArgb(0xFF, 0x12, 0x14, 0x18)
            : ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        return BlendColors(tintedColor, overlayColor, overlayMix);
    }

    private static Windows.UI.Color BlendColors(Windows.UI.Color fromColor, Windows.UI.Color toColor, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);

        static byte BlendChannel(byte from, byte to, double mix) =>
            (byte)Math.Clamp(Math.Round(from + ((to - from) * mix)), 0, 255);

        return ColorHelper.FromArgb(
            BlendChannel(fromColor.A, toColor.A, amount),
            BlendChannel(fromColor.R, toColor.R, amount),
            BlendChannel(fromColor.G, toColor.G, amount),
            BlendChannel(fromColor.B, toColor.B, amount));
    }

    private static double GetCurrentSurfaceCornerRadius() =>
        GetResourceDouble("DeskBoxFloatingCornerRadius", 8);

    private static void ApplyDwmBorderStyle(IntPtr hwnd)
    {
        // Always set DWM border to transparent — the visual border is drawn by XAML
        // BackgroundPlate.BorderBrush which correctly follows the XAML CornerRadius.
        int borderNone = unchecked((int)0xFFFFFFFE);
        NotificationWin32Helper.TrySetDwmWindowAttribute(
            hwnd,
            NotificationWin32Helper.DwmWindowAttributeBorderColor,
            ref borderNone);
    }
}
