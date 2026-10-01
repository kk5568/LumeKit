using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Devices.Enumeration;

namespace BluetoothAudioReceiver.Core.Helpers;

public static class DeviceIdentityHelper
{
    public const string ContainerIdPropertyName = "System.Devices.ContainerId";
    private const string InputRolePrefix = "input";
    private const string OutputRolePrefix = "output";

    public static string GetPhysicalDeviceId(DeviceInformation? device)
    {
        if (device == null)
        {
            return string.Empty;
        }

        if (device.Properties != null &&
            device.Properties.TryGetValue(ContainerIdPropertyName, out var containerIdValue) &&
            containerIdValue != null)
        {
            var containerId = Convert.ToString(containerIdValue, CultureInfo.InvariantCulture)?.Trim();
            if (!string.IsNullOrWhiteSpace(containerId))
            {
                return containerId;
            }
        }

        return string.IsNullOrWhiteSpace(device.Id) ? string.Empty : device.Id.Trim();
    }

    public static string GetSelectionId(DeviceInformation? device, bool isInput)
    {
        return BuildSelectionId(GetPhysicalDeviceId(device), isInput);
    }

    public static string NormalizeOutputEndpointId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        const string endpointPrefix = "{0.0.0.00000000}.";
        var trimmed = deviceId.Trim();

        if (trimmed.StartsWith(endpointPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var startIndex = trimmed.IndexOf(endpointPrefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return trimmed;
        }

        var endIndex = trimmed.IndexOf('}', startIndex + endpointPrefix.Length);
        if (endIndex < 0)
        {
            return trimmed;
        }

        return trimmed.Substring(startIndex, endIndex - startIndex + 1);
    }

    public static string BuildSelectionId(string physicalDeviceId, bool isInput)
    {
        var trimmed = string.IsNullOrWhiteSpace(physicalDeviceId) ? string.Empty : physicalDeviceId.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return $"{(isInput ? InputRolePrefix : OutputRolePrefix)}::{trimmed}";
    }

    public static bool TryParseSelectionId(string? selectionId, out bool isInput, out string physicalDeviceId)
    {
        isInput = true;
        physicalDeviceId = string.Empty;

        if (string.IsNullOrWhiteSpace(selectionId))
        {
            return false;
        }

        var trimmed = selectionId.Trim();
        var separatorIndex = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 2)
        {
            physicalDeviceId = trimmed;
            return true;
        }

        var role = trimmed[..separatorIndex];
        physicalDeviceId = trimmed[(separatorIndex + 2)..].Trim();
        isInput = string.Equals(role, InputRolePrefix, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(physicalDeviceId);
    }

    public static IReadOnlyList<string> BuildAdditionalProperties()
    {
        return [ContainerIdPropertyName];
    }
}
