using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BluetoothAudioReceiver.Services.Monitor
{
    public class MonitorManager
    {
        private readonly List<MonitorItem> _monitors = [];
        private bool _isInitialized;

        public IReadOnlyList<MonitorItem> Monitors => _monitors.AsReadOnly();

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

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

        private Task EnumerateMonitorsAsync()
        {
            _monitors.Clear();

            MonitorConfiguration.EnumerateDisplayMonitors(hMonitor =>
            {
                EnumeratePhysicalMonitorsAsync(hMonitor).GetAwaiter().GetResult();
            });

            return Task.CompletedTask;
        }

        private async Task EnumeratePhysicalMonitorsAsync(IntPtr monitorHandle)
        {
            if (monitorHandle == IntPtr.Zero)
            {
                return;
            }

            await Task.CompletedTask;

            if (!MonitorConfiguration.GetNumberOfPhysicalMonitorsFromHMONITOR(
                monitorHandle,
                out uint count))
            {
                return;
            }

            if (count == 0)
            {
                return;
            }

            var physicalMonitors = new MonitorConfiguration.PHYSICAL_MONITOR[count];
            if (!MonitorConfiguration.GetPhysicalMonitorsFromHMONITOR(
                monitorHandle,
                count,
                physicalMonitors))
            {
                return;
            }

            for (uint i = 0; i < count; i++)
            {
                var physicalMonitor = physicalMonitors[i];
                var handle = new SafePhysicalMonitorHandle(physicalMonitor.hPhysicalMonitor);
                var capability = MonitorConfiguration.BuildCapability(handle);

                if (capability.IsHighLevelBrightnessSupported ||
                    capability.IsLowLevelBrightnessSupported ||
                    capability.IsColorTemperatureSupported)
                {
                    var monitorItem = new DdcMonitorItem(
                        deviceInstanceId: GetDeviceInstanceId(physicalMonitor),
                        description: physicalMonitor.szPhysicalMonitorDescription,
                        displayIndex: 0,
                        monitorIndex: (byte)i,
                        handle: handle,
                        capability: capability);

                    _monitors.Add(monitorItem);
                }
                else
                {
                    handle.Dispose();
                }
            }
        }

        private static string GetDeviceInstanceId(MonitorConfiguration.PHYSICAL_MONITOR physicalMonitor)
        {
            return string.IsNullOrWhiteSpace(physicalMonitor.szPhysicalMonitorDescription)
                ? Guid.NewGuid().ToString()
                : physicalMonitor.szPhysicalMonitorDescription;
        }

        public async Task UpdateAllMonitorsAsync()
        {
            foreach (var monitor in _monitors)
            {
                await monitor.UpdateBrightnessAsync();
                await monitor.UpdateColorTemperatureAsync();
            }
        }

        public async Task SetAllMonitorsBrightnessAsync(int brightness)
        {
            foreach (var monitor in _monitors)
            {
                await monitor.SetBrightnessAsync(brightness);
            }
        }

        public async Task SetAllMonitorsColorTemperatureAsync(int colorTemperature)
        {
            foreach (var monitor in _monitors)
            {
                await monitor.SetColorTemperatureAsync(colorTemperature);
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
