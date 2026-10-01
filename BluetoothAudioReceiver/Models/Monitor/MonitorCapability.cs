using System;
using System.Collections.Generic;

namespace BluetoothAudioReceiver.Models.Monitor
{
    [Flags]
    public enum MonitorCapabilities
    {
        None = 0x00000000,
        MonitorTechnologyType = 0x00000001,
        Brightness = 0x00000002,
        Contrast = 0x00000004,
        ColorTemperature = 0x00000008,
        RedGreenBlueGain = 0x00000010,
        RedGreenBlueDrive = 0x00000020,
        Degauss = 0x00000040,
        DisplayAreaPosition = 0x00000080,
        DisplayAreaSize = 0x00000100,
        RestoreFactoryDefaults = 0x00000400,
        RestoreFactoryColorDefaults = 0x00000800,
        RestoreFactoryDefaultsEnablesMonitorSettings = 0x00001000
    }

    [Flags]
    public enum SupportedColorTemperatures
    {
        None = 0x00000000,
        Temperature4000K = 0x00000001,
        Temperature5000K = 0x00000002,
        Temperature6500K = 0x00000004,
        Temperature7500K = 0x00000008,
        Temperature8200K = 0x00000010,
        Temperature9300K = 0x00000020,
        Temperature10000K = 0x00000040,
        Temperature11500K = 0x00000080
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
