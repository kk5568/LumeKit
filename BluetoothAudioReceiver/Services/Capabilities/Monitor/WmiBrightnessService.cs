using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Serilog;

namespace BluetoothAudioReceiver.Services.Monitor
{
    public sealed class WmiBrightnessService
    {
        private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(WmiBrightnessService));
        private const string WmiScope = @"root\wmi";
        private const string BrightnessMethodsQuery = "SELECT * FROM WmiMonitorBrightnessMethods";
        private const string BrightnessQuery = "SELECT * FROM WmiMonitorBrightness";

        public async Task<bool> SetBrightnessAsync(byte brightness)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(WmiScope, BrightnessMethodsQuery);
                    var methods = searcher.Get().Cast<ManagementObject>().ToList();
                    if (methods.Count == 0)
                    {
                        _log.Debug("WMI brightness methods not found.");
                        return false;
                    }

                    var ok = false;
                    foreach (var method in methods)
                    {
                        try
                        {
                            method.InvokeMethod("WmiSetBrightness", new object[] { 1u, brightness });
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            _log.Warning(ex, "Failed to set WMI brightness to {Brightness}", brightness);
                        }
                    }

                    return ok;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "SetBrightnessAsync via WMI failed.");
                    return false;
                }
            });
        }

        public async Task<byte?> GetCurrentBrightnessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(WmiScope, BrightnessQuery);
                    var monitors = searcher.Get().Cast<ManagementObject>().ToList();
                    var first = monitors.FirstOrDefault();
                    if (first == null)
                    {
                        return null;
                    }

                    var brightness = first["CurrentBrightness"];
                    return brightness == null ? null : (byte?)Convert.ToByte(brightness);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "GetCurrentBrightnessAsync via WMI failed.");
                    return null;
                }
            });
        }
    }
}
