using System;
using System.Collections.Generic;

namespace BluetoothAudioReceiver.Services.Monitor
{
    [Flags]
    public enum MC_CAPS
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
    public enum MC_SUPPORTED_COLOR_TEMPERATURE
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

    public class MonitorCapability
    {
        public bool IsHighLevelBrightnessSupported { get; set; }
        public bool IsLowLevelBrightnessSupported { get; set; }
        public bool IsContrastSupported { get; set; }
        public bool IsColorTemperatureSupported { get; set; }
        public Dictionary<byte, byte[]> CapabilitiesCodes { get; set; }
        public string CapabilitiesString { get; set; } = string.Empty;

        public MonitorCapability()
        {
            CapabilitiesCodes = new Dictionary<byte, byte[]>();
        }
    }
}
