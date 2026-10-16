using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.Helpers.Application;

/// <summary>
/// DeskBox's window-level backdrop interop copied for the notification window.
/// Keep this helper limited to the APIs used by the DeskBox material path.
/// </summary>
internal static partial class NotificationWin32Helper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        internal int Size;
        internal int ThreadType;
        internal int ApartmentType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        internal int AccentState;
        internal int AccentFlags;
        internal uint GradientColor;
        internal int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        internal int Attribute;
        internal IntPtr Data;
        internal int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins
    {
        internal int Left;
        internal int Right;
        internal int Top;
        internal int Bottom;
    }

    internal const int DwmWindowAttributeImmersiveDarkMode = 20;
    internal const int DwmWindowAttributeBorderColor = 34;
    internal const int DwmWindowAttributeSystemBackdropType = 38;
    internal const int DwmSystemBackdropNone = 1;
    internal const int DwmSystemBackdropTransientWindow = 3;
    // 关掉浮窗外圈的系统阴影用：DWM 把窗口框（含窗口阴影）画在非客户区，停掉非客户区渲染就不会有阴影。
    internal const int DwmWindowAttributeNcRenderingPolicy = 2;
    internal const int DwmNcRenderingDisabled = 1;
    // 停掉非客户区后圆角可能一起没了，显式再指定一次圆角。
    internal const int DwmWindowAttributeWindowCornerPreference = 33;
    internal const int DwmWindowCornerRound = 2;
    internal const int WindowCompositionAttributeAccentPolicy = 19;

    internal const int GwlExStyle = -20;
    internal const int GwlStyle = -16;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExTopMost = 0x00000008;
    internal const int WsCaption = 0x00C00000;
    internal const int WsBorder = 0x00800000;
    internal const int WsDlgFrame = 0x00400000;
    internal const int WsThickFrame = 0x00040000;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;

    internal const int AccentDisabled = 0;
    internal const int AccentEnableGradient = 1;
    internal const int AccentEnableTransparentGradient = 2;
    internal const int AccentEnableBlurBehind = 3;
    internal const int AccentEnableAcrylicBlurBehind = 4;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttributeData data);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref Margins margins);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        ref IntPtr dispatcherQueueController);

    private static IntPtr _dispatcherQueueController;

    internal static void SetWindowTheme(IntPtr hwnd, bool isDark)
    {
        int darkMode = isDark ? 1 : 0;
        TrySetDwmWindowAttribute(hwnd, DwmWindowAttributeImmersiveDarkMode, ref darkMode);
    }

    internal static void ConfigureDeskBoxWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExToolWindow);

        int style = GetWindowLong(hwnd, GwlStyle);
        style &= ~(WsCaption | WsBorder | WsDlgFrame | WsThickFrame);
        SetWindowLong(hwnd, GwlStyle, style);

        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);

        int borderNone = unchecked((int)0xFFFFFFFE);
        TrySetDwmWindowAttribute(hwnd, DwmWindowAttributeBorderColor, ref borderNone);

        // 浮窗外圈那圈系统阴影来自 DWM 的窗口框渲染（非客户区），这里整块停掉；所有用这套
        // 窗口外壳的浮窗（通知、托盘快捷中心）都会跟着没有阴影。
        int ncRenderingDisabled = DwmNcRenderingDisabled;
        TrySetDwmWindowAttribute(hwnd, DwmWindowAttributeNcRenderingPolicy, ref ncRenderingDisabled);

        // 停掉非客户区后 Win11 的圆角可能一并消失，这里显式再锁一次圆角。
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int roundCorner = DwmWindowCornerRound;
            TrySetDwmWindowAttribute(hwnd, DwmWindowAttributeWindowCornerPreference, ref roundCorner);
        }
    }

    /// <summary>
    /// 通知浮窗保持置顶（不抢焦点、不改尺寸/位置）。ConfigureDeskBoxWindow 里的
    /// SetWindowPos 用的是 HWND_TOP，会把 z-order 重置，所以置顶必须在它之后调用。
    /// 做法对齐 Views\Windows\NotificationWindow.xaml.cs:574 的 EnsureTopMostNoActivate。
    /// </summary>
    internal static void EnsureTopMostNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int exStyle = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, exStyle | WsExTopMost | WsExToolWindow);
            SetWindowPos(
                hwnd,
                new IntPtr(-1),
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 摘掉置顶（WS_EX_TOPMOST + HWND_NOTOPMOST）。SetWindowPos 用 HWND_TOP 并不会
    /// 把窗口移出置顶层，必须显式 HWND_NOTOPMOST。
    /// </summary>
    internal static void ClearTopMostNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int exStyle = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, exStyle & ~WsExTopMost);
            SetWindowPos(
                hwnd,
                new IntPtr(-2),
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        catch
        {
        }
    }

    internal static void EnsureSystemDispatcherQueue()
    {
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is not null ||
            _dispatcherQueueController != IntPtr.Zero)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = 2,
            ApartmentType = 2
        };

        _ = CreateDispatcherQueueController(options, ref _dispatcherQueueController);
    }

    internal static void ApplyFullWindowFrame(IntPtr hwnd)
    {
        var margins = new Margins
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };

        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    internal static int TrySetDwmWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value)
    {
        if (attribute == DwmWindowAttributeSystemBackdropType &&
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return 0;
        }

        try
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
        }
        catch
        {
            return -1;
        }
    }

    internal static bool ApplyAccentBlur(
        IntPtr hwnd,
        global::Windows.UI.Color tintColor,
        double opacity,
        bool enabled,
        bool blurEnabled = true)
    {
        opacity = Math.Clamp(opacity, 0.0, 1.0);
        int accentState = ResolveAccentState(enabled, blurEnabled, opacity);
        int accentFlags = 2;
        uint gradientColor = ToAbgr(ApplyAlpha(tintColor, opacity));

        if (accentState == AccentDisabled)
        {
            accentFlags = 0;
            gradientColor = 0;
        }

        var accent = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = accentFlags,
            GradientColor = gradientColor,
            AnimationId = 0
        };

        int accentSize = Marshal.SizeOf<AccentPolicy>();
        IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = accentPtr,
                SizeOfData = accentSize
            };

            return SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    internal static void DisableAccentPolicy(IntPtr hwnd)
    {
        var accent = new AccentPolicy
        {
            AccentState = AccentDisabled,
            AccentFlags = 0,
            GradientColor = 0,
            AnimationId = 0
        };

        int accentSize = Marshal.SizeOf<AccentPolicy>();
        IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = accentPtr,
                SizeOfData = accentSize
            };

            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    private static int ResolveAccentState(
        bool enabled,
        bool blurEnabled,
        double opacity)
    {
        if (enabled)
        {
            if (!blurEnabled)
            {
                return AccentEnableTransparentGradient;
            }

            return opacity <= 0.01
                ? AccentEnableBlurBehind
                : AccentEnableAcrylicBlurBehind;
        }

        return opacity <= 0.001
            ? AccentDisabled
            : AccentEnableGradient;
    }

    private static global::Windows.UI.Color ApplyAlpha(
        global::Windows.UI.Color color,
        double opacity)
    {
        byte alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return global::Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static uint ToAbgr(global::Windows.UI.Color color) =>
        ((uint)color.A << 24) |
        ((uint)color.B << 16) |
        ((uint)color.G << 8) |
        color.R;
}
