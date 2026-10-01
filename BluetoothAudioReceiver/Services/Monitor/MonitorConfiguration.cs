using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BluetoothAudioReceiver.Services.Monitor
{
    internal static class MonitorConfiguration
    {
        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            out uint pdwNumberOfPhysicalMonitors);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            uint dwPhysicalMonitorArraySize,
            [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitors(
            uint dwPhysicalMonitorArraySize,
            [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitor(
            IntPtr hMonitor);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorCapabilities(
            SafePhysicalMonitorHandle hMonitor,
            out MC_CAPS pdwMonitorCapabilities,
            out MC_SUPPORTED_COLOR_TEMPERATURE pdwSupportedColorTemperatures);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorBrightness(
            SafePhysicalMonitorHandle hMonitor,
            out uint pdwMinimumBrightness,
            out uint pdwCurrentBrightness,
            out uint pdwMaximumBrightness);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetMonitorBrightness(
            SafePhysicalMonitorHandle hMonitor,
            uint dwNewBrightness);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCapabilitiesStringLength(
            SafePhysicalMonitorHandle hMonitor,
            out uint pdwCapabilitiesStringLengthInCharacters);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CapabilitiesRequestAndCapabilitiesReply(
            SafePhysicalMonitorHandle hMonitor,
            [MarshalAs(UnmanagedType.LPStr)]
            [Out] StringBuilder pszASCIICapabilitiesString,
            uint dwCapabilitiesStringLengthInCharacters);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVCPFeatureAndVCPFeatureReply(
            SafePhysicalMonitorHandle hMonitor,
            byte bVCPCode,
            out LPMC_VCP_CODE_TYPE pvct,
            out uint pdwCurrentValue,
            out uint pdwMaximumValue);

        [DllImport("Dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetVCPFeature(
            SafePhysicalMonitorHandle hMonitor,
            byte bVCPCode,
            uint dwNewValue);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        #region Private Enums

        [Flags]
        private enum MC_CAPS
        {
            MC_CAPS_NONE = 0x00000000,
            MC_CAPS_MONITOR_TECHNOLOGY_TYPE = 0x00000001,
            MC_CAPS_BRIGHTNESS = 0x00000002,
            MC_CAPS_CONTRAST = 0x00000004,
            MC_CAPS_COLOR_TEMPERATURE = 0x00000008,
            MC_CAPS_RED_GREEN_BLUE_GAIN = 0x00000010,
            MC_CAPS_RED_GREEN_BLUE_DRIVE = 0x00000020,
            MC_CAPS_DEGAUSS = 0x00000040,
            MC_CAPS_DISPLAY_AREA_POSITION = 0x00000080,
            MC_CAPS_DISPLAY_AREA_SIZE = 0x00000100,
            MC_CAPS_RESTORE_FACTORY_DEFAULTS = 0x00000400,
            MC_CAPS_RESTORE_FACTORY_COLOR_DEFAULTS = 0x00000800,
            MC_RESTORE_FACTORY_DEFAULTS_ENABLES_MONITOR_SETTINGS = 0x00001000
        }

        [Flags]
        private enum MC_SUPPORTED_COLOR_TEMPERATURE
        {
            MC_SUPPORTED_COLOR_TEMPERATURE_NONE = 0x00000000,
            MC_SUPPORTED_COLOR_TEMPERATURE_4000K = 0x00000001,
            MC_SUPPORTED_COLOR_TEMPERATURE_5000K = 0x00000002,
            MC_SUPPORTED_COLOR_TEMPERATURE_6500K = 0x00000004,
            MC_SUPPORTED_COLOR_TEMPERATURE_7500K = 0x00000008,
            MC_SUPPORTED_COLOR_TEMPERATURE_8200K = 0x00000010,
            MC_SUPPORTED_COLOR_TEMPERATURE_9300K = 0x00000020,
            MC_SUPPORTED_COLOR_TEMPERATURE_10000K = 0x00000040,
            MC_SUPPORTED_COLOR_TEMPERATURE_11500K = 0x00000080
        }

        private enum LPMC_VCP_CODE_TYPE
        {
            MC_MOMENTARY,
            MC_SET_PARAMETER
        }

        private enum VcpCode : byte
        {
            None = 0x0,
            Luminance = 0x10,
            Contrast = 0x12,
            Temperature = 0x14,
            InputSource = 0x60,
            SpeakerVolume = 0x62,
            PowerMode = 0xD6
        }

        #endregion

        #region Public Helpers

        public static void EnumerateDisplayMonitors(Action<IntPtr> onMonitor)
        {
            if (onMonitor == null)
            {
                return;
            }

            bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
            {
                onMonitor(hMonitor);
                return true;
            }

            _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        }

        public static MonitorCapability BuildCapability(SafePhysicalMonitorHandle physicalMonitorHandle)
        {
            var capability = new MonitorCapability();
            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
            {
                return capability;
            }

            if (GetMonitorCapabilities(
                physicalMonitorHandle,
                out var caps,
                out var supportedTemps))
            {
                capability.IsHighLevelBrightnessSupported = caps.HasFlag(MC_CAPS.MC_CAPS_BRIGHTNESS);
                capability.IsLowLevelBrightnessSupported = true;
                capability.IsContrastSupported = caps.HasFlag(MC_CAPS.MC_CAPS_CONTRAST);
                capability.IsColorTemperatureSupported = caps.HasFlag(MC_CAPS.MC_CAPS_COLOR_TEMPERATURE);
                capability.SupportedColorTemperaturesMask = (SupportedColorTemperatures)supportedTemps;
            }

            if (GetCapabilitiesString(physicalMonitorHandle, out var capabilitiesString))
            {
                capability.CapabilitiesString = capabilitiesString;
            }

            return capability;
        }

        public static (AccessResult result, uint minimum, uint current, uint maximum) GetBrightness(
            SafePhysicalMonitorHandle physicalMonitorHandle,
            bool isHighLevelBrightnessSupported = true)
        {
            if (!isHighLevelBrightnessSupported)
            {
                return GetVcpValue(physicalMonitorHandle, VcpCode.Luminance);
            }

            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
            {
                return (result: AccessResult.Failed, 0, 0, 0);
            }

            if (GetMonitorBrightness(
                physicalMonitorHandle,
                out uint minimumBrightness,
                out uint currentBrightness,
                out uint maximumBrightness))
            {
                return (result: AccessResult.Succeeded,
                    minimum: minimumBrightness,
                    current: currentBrightness,
                    maximum: maximumBrightness);
            }

            return (result: AccessResult.Failed, 0, 0, 0);
        }

        public static AccessResult SetBrightness(
            SafePhysicalMonitorHandle physicalMonitorHandle,
            uint brightness)
        {
            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
            {
                return AccessResult.Failed;
            }

            if (SetMonitorBrightness(physicalMonitorHandle, brightness))
            {
                return AccessResult.Succeeded;
            }

            return AccessResult.Failed;
        }

        public static (AccessResult result, uint current, uint maximum) GetColorTemperature(
            SafePhysicalMonitorHandle physicalMonitorHandle)
        {
            return GetVcpValueRaw(physicalMonitorHandle, VcpCode.Temperature);
        }

        public static AccessResult SetColorTemperature(
            SafePhysicalMonitorHandle physicalMonitorHandle,
            uint value)
        {
            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
            {
                return AccessResult.Failed;
            }

            return SetVCPFeature(physicalMonitorHandle, (byte)VcpCode.Temperature, value)
                ? AccessResult.Succeeded
                : AccessResult.Failed;
        }

        public static uint? ResolveColorTemperatureCode(int kelvin, SupportedColorTemperatures supportedMask)
        {
            var candidates = new List<(int Kelvin, uint Code, SupportedColorTemperatures Mask)>
            {
                (4000, 0x01, SupportedColorTemperatures.Temperature4000K),
                (5000, 0x02, SupportedColorTemperatures.Temperature5000K),
                (6500, 0x04, SupportedColorTemperatures.Temperature6500K),
                (7500, 0x08, SupportedColorTemperatures.Temperature7500K),
                (8200, 0x10, SupportedColorTemperatures.Temperature8200K),
                (9300, 0x20, SupportedColorTemperatures.Temperature9300K),
                (10000, 0x40, SupportedColorTemperatures.Temperature10000K),
                (11500, 0x80, SupportedColorTemperatures.Temperature11500K)
            };

            var usable = candidates
                .Where(c => supportedMask == SupportedColorTemperatures.None || supportedMask.HasFlag(c.Mask))
                .ToList();

            if (usable.Count == 0)
            {
                usable = candidates;
            }

            var nearest = usable.OrderBy(c => Math.Abs(c.Kelvin - kelvin)).First();
            return nearest.Code;
        }

        public static int DecodeColorTemperatureCode(uint code)
        {
            return code switch
            {
                0x01 => 4000,
                0x02 => 5000,
                0x04 => 6500,
                0x08 => 7500,
                0x10 => 8200,
                0x20 => 9300,
                0x40 => 10000,
                0x80 => 11500,
                _ => (int)code
            };
        }

        #endregion

        #region Internal Helpers

        private static (AccessResult result, uint minimum, uint current, uint maximum) GetVcpValue(
            SafePhysicalMonitorHandle physicalMonitorHandle,
            VcpCode vcpCode)
        {
            var (result, current, maximum) = GetVcpValueRaw(physicalMonitorHandle, vcpCode);
            return (result, 0, current, maximum);
        }

        private static (AccessResult result, uint current, uint maximum) GetVcpValueRaw(
            SafePhysicalMonitorHandle physicalMonitorHandle,
            VcpCode vcpCode)
        {
            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
            {
                return (result: AccessResult.Failed, 0, 0);
            }

            if (GetVCPFeatureAndVCPFeatureReply(
                physicalMonitorHandle,
                (byte)vcpCode,
                out LPMC_VCP_CODE_TYPE _,
                out uint currentValue,
                out uint maximumValue))
            {
                return (result: AccessResult.Succeeded,
                    current: currentValue,
                    maximum: maximumValue);
            }

            return (result: AccessResult.Failed, 0, 0);
        }

        private static bool GetCapabilitiesString(SafePhysicalMonitorHandle physicalMonitorHandle, out string capabilitiesString)
        {
            capabilitiesString = string.Empty;
            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
            {
                return false;
            }

            if (!GetCapabilitiesStringLength(physicalMonitorHandle, out var length) || length == 0)
            {
                return false;
            }

            var builder = new StringBuilder((int)length);
            if (!CapabilitiesRequestAndCapabilitiesReply(physicalMonitorHandle, builder, length))
            {
                return false;
            }

            capabilitiesString = builder.ToString();
            return !string.IsNullOrWhiteSpace(capabilitiesString);
        }

        private static bool EnsurePhysicalMonitorHandle(SafePhysicalMonitorHandle handle)
        {
            return handle != null && !handle.IsInvalid;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        #endregion
    }
}
