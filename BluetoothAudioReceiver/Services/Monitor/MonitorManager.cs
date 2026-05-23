using System;
using System.Collections.Generic;

namespace BluetoothAudioReceiver.Services.Monitor
{
    public class MonitorManager
    {
        private readonly List<MonitorItem> _monitors = new List<MonitorItem>();
        private bool _isInitialized = false;

        public IReadOnlyList<MonitorItem> Monitors => _monitors.AsReadOnly();

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                await EnumerateMonitorsAsync();
                _isInitialized = true;
            }
            catch
            {
                _isInitialized = false;
            }
        }

        private async Task EnumerateMonitorsAsync()
        {
            _monitors.Clear();
            await Task.CompletedTask;
        }

        private async Task EnumeratePhysicalMonitorsAsync(IntPtr monitorHandle)
        {
            if (monitorHandle == IntPtr.Zero) return;

            await Task.CompletedTask;

            if (!MonitorConfiguration.GetNumberOfPhysicalMonitorsFromHMONITOR(
                monitorHandle,
                out uint count))
            {
                return;
            }

            if (count == 0) return;

            var physicalMonitors = new MonitorConfiguration.PHYSICAL_MONITOR[count];

            if (!MonitorConfiguration.GetPhysicalMonitorsFromHMONITOR(
                monitorHandle,
                count,
                physicalMonitors))
            {
                return;
            }

            try
            {
                for (uint i = 0; i < count; i++)
                {
                    var physicalMonitor = physicalMonitors[i];
                    var handle = new SafePhysicalMonitorHandle(physicalMonitor.hPhysicalMonitor);

                    var capability = GetMonitorCapability(handle);

                    if (capability.IsHighLevelBrightnessSupported || capability.IsLowLevelBrightnessSupported)
                    {
                        var monitorItem = new DdcMonitorItem(
                            deviceInstanceId: GetDeviceInstanceId(physicalMonitor),
                            description: physicalMonitor.szPhysicalMonitorDescription,
                            displayIndex: (byte)0,
                            monitorIndex: (byte)i,
                            handle: handle,
                            capability: capability);

                        _monitors.Add(monitorItem);
                    }
                }
            }
            finally
            {
                MonitorConfiguration.DestroyPhysicalMonitors(count, physicalMonitors);
            }
        }

        private string GetDeviceInstanceId(MonitorConfiguration.PHYSICAL_MONITOR physicalMonitor)
        {
            try
            {
                return physicalMonitor.szPhysicalMonitorDescription;
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        private MonitorCapability GetMonitorCapability(SafePhysicalMonitorHandle handle)
        {
            var capability = new MonitorCapability();
            capability.IsHighLevelBrightnessSupported = true;
            capability.IsLowLevelBrightnessSupported = true;
            return capability;
        }

        public async Task UpdateAllMonitorsAsync()
        {
            foreach (var monitor in _monitors)
            {
                await monitor.UpdateBrightnessAsync();
            }
        }

        public async Task SetAllMonitorsBrightnessAsync(int brightness)
        {
            foreach (var monitor in _monitors)
            {
                await monitor.SetBrightnessAsync(brightness);
            }
        }

        public void Dispose()
        {
            foreach (var monitor in _monitors)
            {
                monitor.Dispose();
            }
            _monitors.Clear();
        }
    }
}
