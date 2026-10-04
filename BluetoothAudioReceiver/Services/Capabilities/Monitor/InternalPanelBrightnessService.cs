using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace BluetoothAudioReceiver.Services.Monitor
{
    public sealed class InternalPanelBrightnessService
    {
        private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(InternalPanelBrightnessService));

        private const string LcdDevicePath = @"\\.\LCD";
        private const uint FileAnyAccess = 0;
        private const uint FileDeviceVideo = 0x23;
        private const uint MethodBuffered = 0;
        private const uint IoctlVideoQuerySupportedBrightness = (FileDeviceVideo << 16) | (0x125u << 2) | (FileAnyAccess << 14) | MethodBuffered;
        private const uint IoctlVideoQueryDisplayBrightness = (FileDeviceVideo << 16) | (0x126u << 2) | (FileAnyAccess << 14) | MethodBuffered;
        private const uint IoctlVideoSetDisplayBrightness = (FileDeviceVideo << 16) | (0x127u << 2) | (FileAnyAccess << 14) | MethodBuffered;

        private readonly object _gate = new();
        private byte[]? _supportedBrightnessLevels;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct DisplayBrightness
        {
            public byte DisplayPolicy;
            public byte AcBrightness;
            public byte DcBrightness;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            [Out] byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref DisplayBrightness lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        public async Task<bool> TrySetBrightnessAsync(byte targetBrightness)
        {
            return await Task.Run<bool>(() =>
            {
                lock (_gate)
                {
                    try
                    {
                        using var device = OpenDevice();
                        if (device.IsInvalid)
                        {
                            return false;
                        }

                        var levels = GetSupportedLevels(device);
                        if (levels.Count == 0)
                        {
                            _log.Debug("LCD backlight levels not supported.");
                            return false;
                        }

                        var mapped = MapToSupportedLevel(levels, targetBrightness);
                        var request = new DisplayBrightness
                        {
                            DisplayPolicy = 0,
                            AcBrightness = mapped,
                            DcBrightness = mapped
                        };

                        var ok = DeviceIoControl(
                            device,
                            IoctlVideoSetDisplayBrightness,
                            ref request,
                            (uint)Marshal.SizeOf<DisplayBrightness>(),
                            IntPtr.Zero,
                            0,
                            out _,
                            IntPtr.Zero);

                        if (!ok)
                        {
                            _log.Debug("LCD brightness set failed, win32={Error}", Marshal.GetLastWin32Error());
                        }

                        return ok;
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Internal panel brightness fallback failed.");
                        return false;
                    }
                }
            });
        }

        public async Task<byte?> TryGetBrightnessAsync()
        {
            return await Task.Run<byte?>(() =>
            {
                lock (_gate)
                {
                    try
                    {
                        using var device = OpenDevice();
                        if (device.IsInvalid)
                        {
                            return (byte?)null;
                        }

                        var buffer = new byte[Marshal.SizeOf<DisplayBrightness>()];
                        var ok = DeviceIoControl(
                            device,
                            IoctlVideoQueryDisplayBrightness,
                            IntPtr.Zero,
                            0,
                            buffer,
                            (uint)buffer.Length,
                            out var bytesReturned,
                            IntPtr.Zero);

                        if (!ok || bytesReturned < buffer.Length)
                        {
                            return (byte?)null;
                        }

                        return buffer[2];
                    }
                    catch (Exception ex)
                    {
                        _log.Debug(ex, "Internal panel brightness query failed.");
                        return (byte?)null;
                    }
                }
            });
        }

        private static SafeFileHandle OpenDevice()
        {
            return CreateFile(
                LcdDevicePath,
                0xC0000000, // GENERIC_READ | GENERIC_WRITE
                0x00000003, // FILE_SHARE_READ | FILE_SHARE_WRITE
                IntPtr.Zero,
                3, // OPEN_EXISTING
                0,
                IntPtr.Zero);
        }

        private List<byte> GetSupportedLevels(SafeFileHandle device)
        {
            if (_supportedBrightnessLevels != null && _supportedBrightnessLevels.Length > 0)
            {
                return _supportedBrightnessLevels.ToList();
            }

            var buffer = new byte[256];
            var ok = DeviceIoControl(
                device,
                IoctlVideoQuerySupportedBrightness,
                IntPtr.Zero,
                0,
                buffer,
                (uint)buffer.Length,
                out var bytesReturned,
                IntPtr.Zero);

            if (!ok || bytesReturned == 0)
            {
                return [];
            }

            _supportedBrightnessLevels = buffer.Take((int)bytesReturned).ToArray();
            return _supportedBrightnessLevels.ToList();
        }

        private static byte MapToSupportedLevel(IReadOnlyList<byte> levels, byte targetBrightness)
        {
            return levels.OrderBy(level => Math.Abs(level - targetBrightness)).First();
        }
    }
}
