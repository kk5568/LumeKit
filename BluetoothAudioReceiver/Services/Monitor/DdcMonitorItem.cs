using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.Services.Monitor
{
    internal class DdcMonitorItem : MonitorItem
    {
        private readonly SafePhysicalMonitorHandle _handle;
        private readonly MonitorCapability _capability;
        private uint _minimumBrightness = 0;
        private uint _maximumBrightness = 100;

        public DdcMonitorItem(
            string deviceInstanceId,
            string description,
            byte displayIndex,
            byte monitorIndex,
            SafePhysicalMonitorHandle handle,
            MonitorCapability capability) : base(deviceInstanceId, description, displayIndex, monitorIndex, false, true)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
            _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        }

        public override AccessResult UpdateBrightness(int value = -1)
        {
            var (result, minimum, current, maximum) = MonitorConfiguration.GetBrightness(_handle, _capability.IsHighLevelBrightnessSupported);
            
            if (result.Status == AccessStatus.Succeeded && minimum < maximum && minimum <= current && current <= maximum)
            {
                Brightness = (int)Math.Round((double)(current - minimum) / (maximum - minimum) * 100.0);
                _minimumBrightness = minimum;
                _maximumBrightness = maximum;
                return AccessResult.Succeeded;
            }

            Brightness = -1;
            return result;
        }

        public override AccessResult SetBrightness(int brightness)
        {
            if (brightness < 0 || brightness > 100)
                throw new ArgumentOutOfRangeException(nameof(brightness), "亮度必须在 0-100 之间。");

            var buffer = (uint)Math.Round(brightness / 100.0 * (_maximumBrightness - _minimumBrightness) + _minimumBrightness);
            var result = MonitorConfiguration.SetBrightness(_handle, buffer);

            if (result.Status == AccessStatus.Succeeded)
            {
                Brightness = brightness;
            }

            return result;
        }

        public override AccessResult UpdateContrast()
        {
            Contrast = -1;
            return AccessResult.NotSupported;
        }

        public override AccessResult SetContrast(int contrast)
        {
            return AccessResult.NotSupported;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _handle.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
