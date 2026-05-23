using System;
using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.Services.Monitor
{
    internal class SafePhysicalMonitorHandle : SafeHandle
    {
        public SafePhysicalMonitorHandle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => false;

        protected override bool ReleaseHandle()
        {
            return MonitorConfiguration.DestroyPhysicalMonitor(handle);
        }
    }
}
