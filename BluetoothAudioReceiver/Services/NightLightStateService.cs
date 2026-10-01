using Microsoft.Win32;
using Serilog;

namespace BluetoothAudioReceiver.Services;

public sealed class NightLightStateService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(NightLightStateService));

    private const string StateRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    private const string SettingsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings";

    private byte[]? _savedStateData;
    private bool _isDisabledByUs;

    public bool IsNightLightActive()
    {
        try
        {
            if (TryGetStateBlob(out var stateBlob) && TryExtractInnerPayload(stateBlob, out var statePayload))
            {
                if (HasForceEnabledFlag(statePayload))
                {
                    return true;
                }
            }

            if (TryGetSettingsBlob(out var settingsBlob) && TryExtractInnerPayload(settingsBlob, out var settingsPayload))
            {
                return IsScheduledNightLightActive(settingsPayload, DateTime.Now.TimeOfDay);
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to detect Night Light state");
        }

        return false;
    }

    public bool IsDisabledByUs => _isDisabledByUs;

    public void DisableNightLight()
    {
        try
        {
            if (_isDisabledByUs)
            {
                return;
            }

            // 保存当前状态
            if (TryGetStateBlob(out var stateBlob))
            {
                _savedStateData = stateBlob;
            }

            // 禁用夜灯：写入一个最小化的状态 blob，表示夜灯关闭
            var disableData = new byte[] { 0x43, 0x42, 0x01, 0x00, 0x00 };
            using var key = Registry.CurrentUser.OpenSubKey(StateRegistryPath, true);
            if (key is not null)
            {
                key.SetValue("Data", disableData, RegistryValueKind.Binary);
                _isDisabledByUs = true;
                _log.Information("Night Light disabled by LumeKit");
            }
            else
            {
                _log.Warning("Failed to open Night Light registry key for writing");
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to disable Night Light");
        }
    }

    public void RestoreNightLight()
    {
        try
        {
            if (!_isDisabledByUs)
            {
                return;
            }

            if (_savedStateData is not null)
            {
                using var key = Registry.CurrentUser.OpenSubKey(StateRegistryPath, true);
                if (key is not null)
                {
                    key.SetValue("Data", _savedStateData, RegistryValueKind.Binary);
                    _log.Information("Night Light restored by LumeKit");
                }
            }

            _savedStateData = null;
            _isDisabledByUs = false;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to restore Night Light");
        }
    }

    private static bool TryGetStateBlob(out byte[] blob)
    {
        blob = [];
        using var key = Registry.CurrentUser.OpenSubKey(StateRegistryPath);
        var data = key?.GetValue("Data") as byte[];
        if (data is { Length: > 0 })
        {
            blob = data;
            return true;
        }

        return false;
    }

    private static bool TryGetSettingsBlob(out byte[] blob)
    {
        blob = [];
        using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
        var data = key?.GetValue("Data") as byte[];
        if (data is { Length: > 0 })
        {
            blob = data;
            return true;
        }

        return false;
    }

    private static bool TryExtractInnerPayload(byte[] cloudStoreBlob, out byte[] payload)
    {
        payload = [];

        var reader = new BinaryReader(cloudStoreBlob);
        if (!reader.TryReadBytes(4, out var header) || header is not [0x43, 0x42, 0x01, 0x00])
        {
            return false;
        }

        if (!reader.TryReadByte(out var tag) || tag != 0x0A)
        {
            return false;
        }

        if (!reader.TryReadByte(out var metadataType) || metadataType != 0x02)
        {
            return false;
        }

        if (!reader.TryReadByte(out var metadataValue) || metadataValue != 0x01)
        {
            return false;
        }

        if (!reader.TryReadByte(out var metadataStop) || metadataStop != 0x00)
        {
            return false;
        }

        if (!reader.TryReadByte(out var payloadContainerTag) || payloadContainerTag != 0x2A)
        {
            return false;
        }

        if (!reader.TryReadByte(out var payloadTimestampType) || payloadTimestampType != 0x06)
        {
            return false;
        }

        if (!reader.TrySkipVarUInt())
        {
            return false;
        }

        if (!reader.TryReadByte(out var dataWrapperTag) || dataWrapperTag != 0x2A)
        {
            return false;
        }

        if (!reader.TryReadByte(out var listTag) || listTag != 0x2B)
        {
            return false;
        }

        if (!reader.TryReadByte(out var elementType) || elementType != 0x0E)
        {
            return false;
        }

        if (!reader.TryReadVarUInt(out var count) || count > (ulong)reader.Remaining)
        {
            return false;
        }

        payload = reader.ReadBytes((int)count);
        return payload.Length > 0;
    }

    private static bool HasForceEnabledFlag(byte[] statePayload)
    {
        if (statePayload.Length < 5)
        {
            return false;
        }

        if (statePayload[0] != 0x43 || statePayload[1] != 0x42 || statePayload[2] != 0x01 || statePayload[3] != 0x00)
        {
            return false;
        }

        // In the Night Light state blob, field 0 presence means Night Light is actively forced on.
        return statePayload[4] == 0x03;
    }

    private static bool IsScheduledNightLightActive(byte[] settingsPayload, TimeSpan now)
    {
        if (settingsPayload.Length < 5)
        {
            return false;
        }

        if (settingsPayload[0] != 0x43 || settingsPayload[1] != 0x42 || settingsPayload[2] != 0x01 || settingsPayload[3] != 0x00)
        {
            return false;
        }

        var reader = new BinaryReader(settingsPayload.AsSpan(4).ToArray());
        if (!reader.TryReadByte(out var firstFieldTag) || firstFieldTag != 0x02)
        {
            return false;
        }

        if (!reader.TryReadByte(out var scheduleEnabledValue) || scheduleEnabledValue != 0x01)
        {
            return false;
        }

        if (reader.TryPeekByte(out var nextTag) && nextTag == 0xC2)
        {
            // Set hours mode.
            if (!reader.TryReadByte(out _)
                || !reader.TryReadByte(out var setHoursFieldId)
                || setHoursFieldId != 0x0A
                || !reader.TryReadByte(out var setHoursValue))
            {
                return false;
            }

            _ = setHoursValue; // Presence only.

            if (!TryReadTimeBlock(reader, 0xCA, 0x14, out var startTime))
            {
                return false;
            }

            if (!TryReadTimeBlock(reader, 0xCA, 0x1E, out var endTime))
            {
                return false;
            }

            return IsWithinTimeRange(now, startTime, endTime);
        }

        // Sunset to sunrise mode. Prefer the explicit sunrise/sunset blocks if present.
        if (TryReadTimeBlock(reader, 0xCA, 0x32, out var sunsetTime) &&
            TryReadTimeBlock(reader, 0xCA, 0x3C, out var sunriseTime))
        {
            return IsWithinOvernightRange(now, sunsetTime, sunriseTime);
        }

        return false;
    }

    private static bool TryReadTimeBlock(BinaryReader reader, byte expectedStructTag, byte expectedFieldId, out TimeSpan time)
    {
        time = default;

        if (!reader.TryReadByte(out var structTag) || structTag != expectedStructTag)
        {
            return false;
        }

        if (!reader.TryReadByte(out var fieldId) || fieldId != expectedFieldId)
        {
            return false;
        }

        if (!reader.TryReadByte(out var maybeStop))
        {
            return false;
        }

        if (maybeStop == 0x00)
        {
            time = TimeSpan.Zero;
            return true;
        }

        if (maybeStop != 0x0E)
        {
            return false;
        }

        if (!reader.TryReadByte(out var hour))
        {
            return false;
        }

        var minute = 0;
        if (reader.TryPeekByte(out var next) && next == 0x2E)
        {
            if (!reader.TryReadByte(out _)
                || !reader.TryReadByte(out var minuteFieldId)
                || minuteFieldId != 0x01
                || !reader.TryReadByte(out var minuteValue))
            {
                return false;
            }

            minute = minuteValue;
        }

        if (!reader.TryReadByte(out var endStop) || endStop != 0x00)
        {
            return false;
        }

        time = new TimeSpan(hour, minute, 0);
        return true;
    }

    private static bool IsWithinTimeRange(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        if (start == end)
        {
            return false;
        }

        if (start < end)
        {
            return now >= start && now < end;
        }

        return now >= start || now < end;
    }

    private static bool IsWithinOvernightRange(TimeSpan now, TimeSpan sunset, TimeSpan sunrise)
    {
        if (sunset == sunrise)
        {
            return false;
        }

        if (sunset < sunrise)
        {
            return now >= sunset && now < sunrise;
        }

        return now >= sunset || now < sunrise;
    }

    private sealed class BinaryReader(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _offset;

        public int Remaining => Math.Max(0, _data.Length - _offset);

        public bool TryReadByte(out byte value)
        {
            if (_offset >= _data.Length)
            {
                value = default;
                return false;
            }

            value = _data[_offset++];
            return true;
        }

        public bool TryPeekByte(out byte value)
        {
            if (_offset >= _data.Length)
            {
                value = default;
                return false;
            }

            value = _data[_offset];
            return true;
        }

        public bool TryReadBytes(int count, out byte[] value)
        {
            if (count < 0 || _offset + count > _data.Length)
            {
                value = [];
                return false;
            }

            value = _data.AsSpan(_offset, count).ToArray();
            _offset += count;
            return true;
        }

        public byte[] ReadBytes(int count)
        {
            if (!TryReadBytes(count, out var value))
            {
                return [];
            }

            return value;
        }

        public bool TrySkipVarUInt()
        {
            return TryReadVarUInt(out _);
        }

        public bool TryReadVarUInt(out ulong value)
        {
            value = 0;
            var shift = 0;

            while (_offset < _data.Length)
            {
                var current = _data[_offset++];
                value |= (ulong)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return true;
                }

                shift += 7;
                if (shift > 63)
                {
                    break;
                }
            }

            value = 0;
            return false;
        }
    }
}
