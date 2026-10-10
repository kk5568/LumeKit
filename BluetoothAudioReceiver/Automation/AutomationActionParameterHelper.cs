using System.Text;
using System.Text.Json;

namespace BluetoothAudioReceiver.Automation;

internal static class AutomationActionParameterHelper
{
    public static string BuildValue(string value)
        => $$"""{"value":"{{EscapeJsonString(value)}}"}""";

    public static string BuildSnapshotKey(string key)
        => BuildValue(key);

    public static string BuildDelaySeconds(int seconds)
        => $$"""{"seconds":{{Math.Clamp(seconds, 0, 600)}}}""";

    public static string BuildStopExecution()
        => BuildValue("stop");

    public static string BuildBrightness(int brightness)
        => $$"""{"brightness":{{Math.Clamp(brightness, 0, 100)}}}""";

    public static string BuildPowerMode(string option)
        => $$"""{"schemeGuid":"{{EscapeJsonString(AutomationOptionCatalog.ExtractPowerModeGuid(option))}}","displayName":"{{EscapeJsonString(AutomationOptionCatalog.ExtractPowerModeDisplayName(option))}}"}""";

    public static string BuildOutputDevice(string deviceId)
        => $$"""{"deviceId":"{{EscapeJsonString(deviceId)}}"}""";

    public static string BuildInputDevice(string deviceId, string operation)
        => $$"""{"deviceId":"{{EscapeJsonString(deviceId)}}","operation":"{{EscapeJsonString(operation)}}"}""";

    public static string BuildProcessLaunch(string target)
        => $$"""{"operation":"launch","target":"{{EscapeJsonString(target)}}"}""";

    public static string BuildProcessClose(string processName)
        => $$"""{"operation":"close","target":"{{EscapeJsonString(processName)}}"}""";

    public static string ReadValue(string? parameters, string fallback = "")
    {
        if (TryReadString(parameters, "value", out var value))
        {
            return value;
        }

        return string.IsNullOrWhiteSpace(parameters) || parameters == "{}"
            ? fallback
            : parameters.Trim();
    }

    public static int ReadBrightness(string? parameters, int fallback = 50)
    {
        if (TryReadInt(parameters, "brightness", out var brightness))
        {
            return Math.Clamp(brightness, 0, 100);
        }

        return ReadDigits(parameters, fallback);
    }

    public static string ReadPowerModeGuid(string? parameters)
    {
        if (TryReadString(parameters, "schemeGuid", out var schemeGuid))
        {
            return schemeGuid;
        }

        return AutomationOptionCatalog.ExtractPowerModeGuid(parameters ?? string.Empty);
    }

    public static string ReadPowerModeDisplayName(string? parameters)
    {
        if (TryReadString(parameters, "displayName", out var displayName))
        {
            return displayName;
        }

        return AutomationOptionCatalog.ExtractPowerModeDisplayName(parameters ?? string.Empty);
    }

    public static string ReadOutputDeviceId(string? parameters)
    {
        if (TryReadString(parameters, "deviceId", out var deviceId))
        {
            return deviceId;
        }

        return ReadValue(parameters);
    }

    public static string ReadInputDeviceOperation(string? parameters, string fallback = "connect")
    {
        if (TryReadString(parameters, "operation", out var operation))
        {
            return string.IsNullOrWhiteSpace(operation) ? fallback : operation;
        }

        return fallback;
    }

    public static string ReadInputDeviceId(string? parameters)
    {
        if (TryReadString(parameters, "deviceId", out var deviceId))
        {
            return deviceId;
        }

        return ReadValue(parameters);
    }

    public static (string Operation, string Target) ReadProcessOperation(string? parameters)
    {
        if (TryReadString(parameters, "operation", out var operation) &&
            TryReadString(parameters, "target", out var target))
        {
            return (operation, target);
        }

        var raw = parameters?.Trim() ?? string.Empty;
        if (raw.StartsWith("close:", StringComparison.OrdinalIgnoreCase))
        {
            return ("close", raw["close:".Length..].Trim().Trim('"'));
        }

        if (raw.StartsWith("launch:", StringComparison.OrdinalIgnoreCase))
        {
            return ("launch", raw["launch:".Length..].Trim().Trim('"'));
        }

        return (string.Empty, string.Empty);
    }

    public static int ReadDelaySeconds(string? parameters, int fallback = 0)
    {
        if (TryReadInt(parameters, "seconds", out var seconds))
        {
            return Math.Clamp(seconds, 0, 600);
        }

        return Math.Clamp(ReadDigits(parameters, fallback), 0, 600);
    }

    private static int ReadDigits(string? parameters, int fallback)
    {
        var digits = new string((parameters ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : fallback;
    }

    private static bool TryReadString(string? parameters, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryReadJson(parameters, out var root))
        {
            return false;
        }

        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadInt(string? parameters, string propertyName, out int value)
    {
        value = default;
        if (!TryReadJson(parameters, out var root))
        {
            return false;
        }

        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return property.TryGetInt32(out value);
    }

    private static bool TryReadJson(string? parameters, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EscapeJsonString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        return builder.ToString();
    }
}
