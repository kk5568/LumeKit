// AcrylicBlur.cs
// 可选：给浮窗加 Win10/11 亚克力（Acrylic）模糊背景。
// 需要窗口背景为半透明才看得见效果，例如 XAML 里 Window 背景设 #AA000000。
// 说明：本模块改写自 FluentFlyout (GPL-3.0-or-later)，如直接复用请注意 GPL 协议要求。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlyoutModule;

public static class AcrylicBlur
{
    private enum AccentState { ACCENT_DISABLED = 0, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4 }

    private enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor; // AABBGGRR：高 8 位透明度(0-255)，低 24 位 BGR 颜色
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// 开启亚克力模糊。
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="opacity">模糊层不透明度 0-255</param>
    /// <param name="color">底色（默认深灰），Light 主题建议换 Color.FromRgb(0xF3,0xF3,0xF3)</param>
    public static void Enable(Window window, byte opacity = 175, Color? color = null)
    {
        var c = color ?? Color.FromRgb(0x20, 0x20, 0x20);
        uint bgr = (uint)(c.R | (c.G << 8) | (c.B << 16));
        Apply(window, AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, ((uint)opacity << 24) | bgr);
    }

    public static void Disable(Window window) => Apply(window, AccentState.ACCENT_DISABLED, 0);

    private static void Apply(Window window, AccentState state, uint gradientColor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var accent = new AccentPolicy
        {
            AccentState = state,
            GradientColor = gradientColor
        };

        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
