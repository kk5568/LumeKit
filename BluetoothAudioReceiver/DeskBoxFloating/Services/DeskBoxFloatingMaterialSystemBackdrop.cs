using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace BluetoothAudioReceiver.DeskBoxFloating.Services;

internal readonly record struct DeskBoxFloatingMaterialBackdropAppearance(
    string MaterialType,
    bool IsDark,
    Windows.UI.Color AccentColor,
    double SurfaceOpacity,
    double MaterialIntensity);

internal sealed partial class DeskBoxFloatingMaterialSystemBackdrop : SystemBackdrop, IDisposable
{
    private DeskBoxFloatingMaterialBackdropAppearance _appearance;
    private ICompositionSupportsSystemBackdrop? _target;
    private SystemBackdropConfiguration? _configuration;
    private MicaController? _micaController;
    private DesktopAcrylicController? _acrylicController;
    private bool _isSuspended;
    private bool _isDisposed;

    internal DeskBoxFloatingMaterialSystemBackdrop(
        DeskBoxFloatingMaterialBackdropAppearance appearance)
    {
        _appearance = appearance;
    }

    internal static bool IsSupported(string materialType) =>
        DeskBoxFloatingMaterialSettings.IsMicaMaterial(materialType)
            ? DeskBoxFloatingMaterialSettings.SupportsMica
            : DeskBoxFloatingMaterialSettings.IsAcrylicMaterial(materialType) &&
              DeskBoxFloatingMaterialSettings.SupportsDesktopAcrylic;

    internal void UpdateAppearance(
        DeskBoxFloatingMaterialBackdropAppearance appearance)
    {
        if (_isDisposed || (_appearance == appearance && !_isSuspended))
        {
            return;
        }

        _appearance = appearance;
        _isSuspended = false;
        if (_target is not null && _configuration is not null)
        {
            ApplyController();
        }
    }

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        DisposeControllers();
        _target = connectedTarget;
        _configuration = GetDefaultSystemBackdropConfiguration(
            connectedTarget,
            xamlRoot);
        ApplyController();
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        _configuration = GetDefaultSystemBackdropConfiguration(
            target,
            xamlRoot);
        ApplyConfiguration();
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        try
        {
            base.OnTargetDisconnected(disconnectedTarget);
        }
        finally
        {
            DisposeControllers();
            _target = null;
            _configuration = null;
        }
    }

    internal void Suspend()
    {
        _isSuspended = true;
        DisposeControllers();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Suspend();
    }

    private void ApplyController()
    {
        if (_isDisposed || _isSuspended) return;
        try
        {
            if (DeskBoxFloatingMaterialSettings.IsMicaMaterial(_appearance.MaterialType))
            {
                ApplyMicaController();
                return;
            }

            if (DeskBoxFloatingMaterialSettings.IsAcrylicMaterial(_appearance.MaterialType))
            {
                ApplyAcrylicController();
                return;
            }

            DisposeControllers();
        }
        catch
        {
            DisposeControllers();
        }
    }

    private void ApplyMicaController()
    {
        if (_target is null ||
            _configuration is null ||
            !DeskBoxFloatingMaterialSettings.SupportsMica)
        {
            DisposeControllers();
            return;
        }

        DisposeAcrylicController();
        bool useAlt = _appearance.MaterialType ==
            DeskBoxFloatingMaterialSettings.WidgetMaterialTypeMicaAlt;
        if (_micaController is null)
        {
            _micaController = new MicaController();
            _micaController.SetSystemBackdropConfiguration(_configuration);
            if (!_micaController.AddSystemBackdropTarget(_target))
            {
                throw new InvalidOperationException(
                    "Mica backdrop target registration failed.");
            }
        }

        _micaController.Kind = useAlt ? MicaKind.BaseAlt : MicaKind.Base;
        _micaController.TintColor =
            DeskBoxFloatingMaterialVisualCalculator.BuildContentTintColor(
                _appearance.IsDark,
                _appearance.AccentColor);
        _micaController.FallbackColor =
            DeskBoxFloatingMaterialVisualCalculator.BuildMicaFallbackColor(
                _appearance.IsDark,
                useAlt);
        DeskBoxFloatingMaterialOpacityProfile profile =
            DeskBoxFloatingMaterialVisualCalculator.CalculateMica(
                _appearance.IsDark,
                useAlt,
                _appearance.MaterialIntensity);
        _micaController.TintOpacity = (float)profile.TintOpacity;
        _micaController.LuminosityOpacity =
            (float)profile.LuminosityOpacity;
    }

    private void ApplyAcrylicController()
    {
        if (_target is null ||
            _configuration is null ||
            !DeskBoxFloatingMaterialSettings.SupportsDesktopAcrylic)
        {
            DisposeControllers();
            return;
        }

        DisposeMicaController();
        bool useBase = _appearance.MaterialType ==
            DeskBoxFloatingMaterialSettings.WidgetMaterialTypeAcrylicBase;
        if (_acrylicController is null || _acrylicController.IsClosed)
        {
            _acrylicController = new DesktopAcrylicController();
            _acrylicController.SetSystemBackdropConfiguration(_configuration);
            if (!_acrylicController.AddSystemBackdropTarget(_target))
            {
                throw new InvalidOperationException(
                    "Acrylic backdrop target registration failed.");
            }
        }

        _acrylicController.Kind = useBase
            ? DesktopAcrylicKind.Base
            : DesktopAcrylicKind.Thin;
        Windows.UI.Color tintColor =
            DeskBoxFloatingMaterialVisualCalculator.BuildContentTintColor(
                _appearance.IsDark,
                _appearance.AccentColor);
        _acrylicController.TintColor = tintColor;
        _acrylicController.FallbackColor = tintColor;
        DeskBoxFloatingMaterialOpacityProfile profile =
            DeskBoxFloatingMaterialVisualCalculator.CalculateAcrylic(
                _appearance.IsDark,
                useBase,
                _appearance.SurfaceOpacity,
                _appearance.MaterialIntensity);
        _acrylicController.TintOpacity = (float)profile.TintOpacity;
        _acrylicController.LuminosityOpacity =
            (float)profile.LuminosityOpacity;
    }

    private void ApplyConfiguration()
    {
        if (_configuration is null || _isDisposed || _isSuspended)
        {
            return;
        }

        _micaController?.SetSystemBackdropConfiguration(_configuration);
        if (_acrylicController is { IsClosed: false })
        {
            _acrylicController.SetSystemBackdropConfiguration(_configuration);
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
        }
    }
}
