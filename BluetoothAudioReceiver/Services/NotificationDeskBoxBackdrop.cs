using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;
using WinUIEx;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// DeskBox's direct window-material path adapted only for NotificationWindow.
/// The controllers are attached to the native window target instead of using
/// the simplified built-in backdrop objects.
/// </summary>
internal sealed class NotificationDeskBoxBackdrop : IDisposable
{
    private ICompositionSupportsSystemBackdrop? _backdropTarget;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private MicaController? _micaController;
    private DesktopAcrylicController? _acrylicController;
    private TransparentTintBackdrop? _solidColorBackdrop;
    private bool _micaControllerAttached;
    private bool _acrylicControllerAttached;
    private bool _disposed;

    internal void Apply(
        Window window,
        bool isDark,
        string materialType,
        Windows.UI.Color tintColor,
        double surfaceOpacity,
        double materialIntensity)
    {
        if (_disposed)
        {
            return;
        }

        IntPtr hwnd = window.GetWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NotificationWin32Helper.EnsureSystemDispatcherQueue();
        NotificationWin32Helper.SetWindowTheme(hwnd, isDark);
        NotificationWin32Helper.ApplyFullWindowFrame(hwnd);

        bool controllerApplied = false;
        if (materialType == NotificationMaterialSettings.MaterialTypeSolid)
        {
            controllerApplied = ApplySolidColorBackdrop(window, tintColor);
        }
        else
        {
            ClearSolidColorBackdrop(window);

            if (NotificationMaterialSettings.IsMicaMaterial(materialType))
            {
                controllerApplied = ApplyMicaController(
                    window,
                    isDark,
                    tintColor,
                    materialType == NotificationMaterialSettings.MaterialTypeMicaAlt,
                    materialIntensity);
            }

            if (!controllerApplied &&
                NotificationMaterialSettings.IsAcrylicMaterial(materialType))
            {
                controllerApplied = ApplyAcrylicController(
                    window,
                    isDark,
                    tintColor,
                    surfaceOpacity,
                    materialType == NotificationMaterialSettings.MaterialTypeAcrylicBase,
                    materialIntensity);
            }
        }

        if (controllerApplied)
        {
            int backdropType = NotificationWin32Helper.DwmSystemBackdropNone;
            NotificationWin32Helper.TrySetDwmWindowAttribute(
                hwnd,
                NotificationWin32Helper.DwmWindowAttributeSystemBackdropType,
                ref backdropType);
            NotificationWin32Helper.DisableAccentPolicy(hwnd);
            return;
        }

        int fallbackBackdropType = NotificationWin32Helper.DwmSystemBackdropTransientWindow;
        NotificationWin32Helper.TrySetDwmWindowAttribute(
            hwnd,
            NotificationWin32Helper.DwmWindowAttributeSystemBackdropType,
            ref fallbackBackdropType);
        DetachAcrylicControllerTarget();
        DetachMicaControllerTarget();

        double accentOpacity = NotificationMaterialSettings.IsAcrylicMaterial(materialType)
            ? NotificationMaterialVisualCalculator.CalculateLegacyAcrylicOpacity(
                materialType == NotificationMaterialSettings.MaterialTypeAcrylicBase,
                surfaceOpacity,
                materialIntensity)
            : Math.Min(surfaceOpacity, 0.52);

        window.SystemBackdrop = null;
        NotificationWin32Helper.ApplyAccentBlur(
            hwnd,
            tintColor,
            accentOpacity,
            enabled: true);
    }

    internal void UpdateActivationState(bool isInputActive)
    {
        if (_backdropConfiguration is null)
        {
            return;
        }

        _backdropConfiguration.IsInputActive = isInputActive;
        try
        {
            _micaController?.SetSystemBackdropConfiguration(_backdropConfiguration);
            if (_acrylicController is { IsClosed: false })
            {
                _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
            }
        }
        catch
        {
        }
    }

    internal void Clear(Window window)
    {
        IntPtr hwnd = window.GetWindowHandle();
        if (hwnd != IntPtr.Zero)
        {
            DetachAcrylicControllerTarget();
            DetachMicaControllerTarget();
            NotificationWin32Helper.DisableAccentPolicy(hwnd);
            int backdropType = NotificationWin32Helper.DwmSystemBackdropNone;
            NotificationWin32Helper.TrySetDwmWindowAttribute(
                hwnd,
                NotificationWin32Helper.DwmWindowAttributeSystemBackdropType,
                ref backdropType);
        }

        ClearSolidColorBackdrop(window);
        DisposeControllers();
        window.SystemBackdrop = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachAcrylicControllerTarget();
        DetachMicaControllerTarget();
        DisposeControllers();
        _solidColorBackdrop = null;
        _backdropTarget = null;
        _backdropConfiguration = null;
    }

    private bool ApplySolidColorBackdrop(
        Window window,
        Windows.UI.Color tintColor)
    {
        DetachAcrylicControllerTarget();
        DetachMicaControllerTarget();

        if (_solidColorBackdrop is null)
        {
            _solidColorBackdrop = new TransparentTintBackdrop(tintColor);
        }
        else
        {
            _solidColorBackdrop.TintColor = tintColor;
        }

        if (!ReferenceEquals(window.SystemBackdrop, _solidColorBackdrop))
        {
            window.SystemBackdrop = _solidColorBackdrop;
        }

        return true;
    }

    private void ClearSolidColorBackdrop(Window window)
    {
        if (_solidColorBackdrop is null)
        {
            return;
        }

        if (ReferenceEquals(window.SystemBackdrop, _solidColorBackdrop))
        {
            window.SystemBackdrop = null;
        }

        _solidColorBackdrop = null;
    }

    private bool ApplyMicaController(
        Window window,
        bool isDark,
        Windows.UI.Color tintColor,
        bool useAlt,
        double materialIntensity)
    {
        if (!NotificationMaterialSettings.SupportsMaterial(
                useAlt
                    ? NotificationMaterialSettings.MaterialTypeMicaAlt
                    : NotificationMaterialSettings.MaterialTypeMica))
        {
            DisposeMicaController();
            return false;
        }

        _backdropTarget ??= window.As<ICompositionSupportsSystemBackdrop>();
        _backdropConfiguration ??= new SystemBackdropConfiguration();
        _backdropConfiguration.IsInputActive = true;
        _backdropConfiguration.Theme = isDark
            ? SystemBackdropTheme.Dark
            : SystemBackdropTheme.Light;

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
            if (!_micaController.AddSystemBackdropTarget(_backdropTarget))
            {
                DisposeMicaController();
                return false;
            }

            _micaControllerAttached = true;
            _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);
        }

        _micaController.Kind = useAlt ? MicaKind.BaseAlt : MicaKind.Base;
        _micaController.TintColor = tintColor;
        _micaController.FallbackColor =
            NotificationMaterialVisualCalculator.BuildMicaFallbackColor(isDark, useAlt);

        NotificationMaterialOpacityProfile profile =
            NotificationMaterialVisualCalculator.CalculateMica(
                isDark,
                useAlt,
                materialIntensity);
        _micaController.TintOpacity = (float)profile.TintOpacity;
        _micaController.LuminosityOpacity = (float)profile.LuminosityOpacity;
        return true;
    }

    private bool ApplyAcrylicController(
        Window window,
        bool isDark,
        Windows.UI.Color tintColor,
        double surfaceOpacity,
        bool useBase,
        double materialIntensity)
    {
        string materialType = useBase
            ? NotificationMaterialSettings.MaterialTypeAcrylicBase
            : NotificationMaterialSettings.MaterialTypeAcrylic;
        if (!NotificationMaterialSettings.SupportsMaterial(materialType))
        {
            DisposeAcrylicController();
            return false;
        }

        _backdropTarget ??= window.As<ICompositionSupportsSystemBackdrop>();
        _backdropConfiguration ??= new SystemBackdropConfiguration();
        _backdropConfiguration.IsInputActive = true;
        _backdropConfiguration.Theme = isDark
            ? SystemBackdropTheme.Dark
            : SystemBackdropTheme.Light;
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
            if (!_acrylicController.AddSystemBackdropTarget(_backdropTarget))
            {
                DisposeAcrylicController();
                return false;
            }

            _acrylicControllerAttached = true;
            _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
        }

        _acrylicController.Kind = useBase
            ? DesktopAcrylicKind.Base
            : DesktopAcrylicKind.Thin;
        _acrylicController.TintColor = tintColor;
        _acrylicController.FallbackColor = tintColor;

        NotificationMaterialOpacityProfile profile =
            NotificationMaterialVisualCalculator.CalculateAcrylic(
                isDark,
                useBase,
                surfaceOpacity,
                materialIntensity);
        _acrylicController.TintOpacity = (float)profile.TintOpacity;
        _acrylicController.LuminosityOpacity = (float)profile.LuminosityOpacity;
        return true;
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

    private void DisposeControllers()
    {
        DisposeMicaController();
        DisposeAcrylicController();
    }

    private void DisposeMicaController()
    {
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

    private void DisposeAcrylicController()
    {
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
}
