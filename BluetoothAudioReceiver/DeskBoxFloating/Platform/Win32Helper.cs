// Extracted verbatim from 引用项目\DeskBox-main\src\DeskBox\Platform\Win32Helper.cs
// Only the native declarations the ported animation stack needs (B 收窄版).
using BluetoothAudioReceiver.DeskBoxFloating.Models;
using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.DeskBoxFloating.Platform;

public static partial class Win32Helper
{
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOREDRAW = 0x0008;
        public const uint SWP_NOACTIVATE = 0x0010;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWNOACTIVATE = 4;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [LibraryImport("dwmapi.dll")]
        public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pvAttribute, int cbAttribute);

        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        public const int DWMWA_CLOAK = 13;



        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
        }

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const uint MONITOR_DEFAULTTOPRIMARY = 1;
        private const int EnumCurrentSettings = -1;

        [LibraryImport("user32.dll")]
        public static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [LibraryImport("user32.dll")]
        public static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(
            string lpszDeviceName,
            int iModeNum,
            ref DEVMODE lpDevMode);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfoEx(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        /// <summary>
        /// Returns the refresh rate of the monitor currently containing the window.
        /// Invalid driver values safely normalize to 60 Hz.
        /// </summary>
        public static int GetDisplayRefreshRateForWindow(IntPtr hWnd)
        {
            try
            {
                IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero)
                {
                    return WidgetDisplayRefreshRatePolicy.DefaultRefreshRateHz;
                }

                var monitorInfo = new MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<MONITORINFOEX>(),
                    szDevice = string.Empty
                };
                if (!GetMonitorInfoEx(monitor, ref monitorInfo) ||
                    string.IsNullOrWhiteSpace(monitorInfo.szDevice))
                {
                    return WidgetDisplayRefreshRatePolicy.DefaultRefreshRateHz;
                }

                var mode = new DEVMODE
                {
                    dmDeviceName = string.Empty,
                    dmFormName = string.Empty,
                    dmSize = (short)Marshal.SizeOf<DEVMODE>()
                };
                return EnumDisplaySettings(monitorInfo.szDevice, EnumCurrentSettings, ref mode)
                    ? WidgetDisplayRefreshRatePolicy.Normalize((uint)Math.Max(0, mode.dmDisplayFrequency))
                    : WidgetDisplayRefreshRatePolicy.DefaultRefreshRateHz;
            }
            catch (Exception ex) when (
                ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return WidgetDisplayRefreshRatePolicy.DefaultRefreshRateHz;
            }
        }

        [LibraryImport("user32.dll")]
        private static partial uint GetDpiForWindow(IntPtr hWnd);

        /// <summary>
        /// 取窗口 DPI 缩放：XamlRoot 能拿到就优先用，浮窗首帧隐藏时拿不到，回退 GetDpiForWindow。
        /// 摘自 引用项目\DeskBox-main\src\DeskBox\Platform\Win32Helper.cs:2511（GetDpiScaleForWindow）。
        /// </summary>
        public static double GetDpiScaleForWindow(IntPtr hWnd, Microsoft.UI.Xaml.XamlRoot? xamlRoot)
        {
            double xamlScale = xamlRoot?.RasterizationScale ?? 0;
            if (xamlScale > 0)
            {
                return xamlScale;
            }

            try
            {
                uint dpi = GetDpiForWindow(hWnd);
                return dpi > 0 ? dpi / 96.0 : 1.0;
            }
            catch (EntryPointNotFoundException)
            {
                return 1.0;
            }
        }
}
