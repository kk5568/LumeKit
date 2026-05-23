using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BluetoothAudioReceiver.Services.Monitor
{
    internal static class MonitorConfiguration
    {
        #region Win32 API

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

        #endregion

        #region VCP Codes

        private enum VcpCode : byte
        {
            None = 0x0,
            Luminance = 0x10,
            Contrast = 0x12,
            Temperature = 0x14,
            InputSource = 0x60,
            SpeakerVolume = 0x62,
            PowerMode = 0xD6,
        }

        #endregion

        #region Public Methods

        public static (AccessResult result, uint minimum, uint current, uint maximum) GetBrightness(
            SafePhysicalMonitorHandle physicalMonitorHandle,
            bool isHighLevelBrightnessSupported = true)
        {
            if (!isHighLevelBrightnessSupported)
                return GetVcpValue(physicalMonitorHandle, VcpCode.Luminance);

            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
                return (result: AccessResult.Failed, 0, 0, 0);

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
                return AccessResult.Failed;

            if (SetMonitorBrightness(physicalMonitorHandle, brightness))
            {
                return AccessResult.Succeeded;
            }

            return AccessResult.Failed;
        }

        private static (AccessResult result, uint minimum, uint current, uint maximum) GetVcpValue(
            SafePhysicalMonitorHandle physicalMonitorHandle,
            VcpCode vcpCode)
        {
            if (!EnsurePhysicalMonitorHandle(physicalMonitorHandle))
                return (result: AccessResult.Failed, 0, 0, 0);

            if (GetVCPFeatureAndVCPFeatureReply(
                physicalMonitorHandle,
                (byte)vcpCode,
                out LPMC_VCP_CODE_TYPE pvct,
                out uint currentValue,
                out uint maximumValue))
            {
                return (result: AccessResult.Succeeded,
                    minimum: 0,
                    current: currentValue,
                    maximum: maximumValue);
            }

            return (result: AccessResult.Failed, 0, 0, 0);
        }

        private static bool EnsurePhysicalMonitorHandle(SafePhysicalMonitorHandle handle)
        {
            return handle != null && !handle.IsInvalid;
        }

        #endregion
    }
}
