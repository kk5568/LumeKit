using BluetoothAudioReceiver.DeskBoxFloating.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BluetoothAudioReceiver.DeskBoxFloating.Helpers;

internal sealed class DeskBoxFloatingMaterialController : IDisposable
{
    private DeskBoxFloatingMaterialSystemBackdrop? _backdrop;

    internal void Apply(Window window, FrameworkElement root, bool isDark)
    {
        try
        {
            string material = DeskBoxFloatingMaterialSettings.ResolveWidgetMaterialType(
                DeskBoxFloatingMaterialSettings.WidgetMaterialTypeAcrylicBase);
            var appearance = new DeskBoxFloatingMaterialBackdropAppearance(
                material,
                isDark,
                isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x30, 0x38)
                    : Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3),
                0.72,
                DeskBoxFloatingMaterialSettings.DefaultWidgetMaterialIntensity);

            if (material == DeskBoxFloatingMaterialSettings.WidgetMaterialTypeSolid ||
                !DeskBoxFloatingMaterialSystemBackdrop.IsSupported(material))
            {
                window.SystemBackdrop ??= new MicaBackdrop();
            }
            else if (_backdrop is null)
            {
                _backdrop = new DeskBoxFloatingMaterialSystemBackdrop(appearance);
                window.SystemBackdrop = _backdrop;
            }
            else
            {
                _backdrop.UpdateAppearance(appearance);
                if (!ReferenceEquals(window.SystemBackdrop, _backdrop))
                {
                    window.SystemBackdrop = _backdrop;
                }
            }

            root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        }
        catch
        {
            window.SystemBackdrop ??= new MicaBackdrop();
        }
    }

    public void Dispose()
    {
        _backdrop?.Dispose();
        _backdrop = null;
    }
}
