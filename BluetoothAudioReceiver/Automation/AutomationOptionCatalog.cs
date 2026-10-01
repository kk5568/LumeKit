using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BluetoothAudioReceiver.Automation;

public static partial class AutomationOptionCatalog
{
    private static readonly Regex PowerPlanRegex = PowerPlanLineRegex();

    public static IReadOnlyList<string> PowerStateOptions { get; } = ["插电", "电池"];

    public static IReadOnlyList<string> ThemeOptions { get; } = ["浅色", "深色", "跟随系统"];

    public static IReadOnlyList<string> InputVolumeOptions { get; } = ["0", "1", "2", "3"];

    public static IReadOnlyList<string> WaitOptions { get; } = ["1 秒", "3 秒", "5 秒", "10 秒"];

    public static IReadOnlyList<string> BrightnessOptions { get; } =
        Enumerable.Range(1, 10)
            .Select(index => $"{index * 10}%")
            .ToArray();

    public static IReadOnlyList<string> GetPowerModeOptions()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/list",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var options = output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParsePowerModeOption)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return options.Length > 0 ? options : BuildDefaultPowerModeOptions();
        }
        catch
        {
            return BuildDefaultPowerModeOptions();
        }
    }

    public static string ExtractPowerModeGuid(string option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return string.Empty;
        }

        var separatorIndex = option.IndexOf('|');
        return separatorIndex >= 0 ? option[..separatorIndex].Trim() : string.Empty;
    }

    public static string ExtractPowerModeDisplayName(string option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return string.Empty;
        }

        var separatorIndex = option.IndexOf('|');
        return separatorIndex >= 0 ? option[(separatorIndex + 1)..].Trim() : option.Trim();
    }

    private static string? ParsePowerModeOption(string line)
    {
        var match = PowerPlanRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var guid = match.Groups["guid"].Value.Trim();
        var name = match.Groups["name"].Value.Trim();
        return string.IsNullOrWhiteSpace(guid) || string.IsNullOrWhiteSpace(name)
            ? null
            : $"{guid} | {name}";
    }

    private static IReadOnlyList<string> BuildDefaultPowerModeOptions()
    {
        return
        [
            "381b4222-f694-41f0-9685-ff5bb260df2e | 平衡",
            "a1841308-3541-4fab-bc81-f71556f20b4a | 节能",
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c | 高性能"
        ];
    }

    [GeneratedRegex(@"GUID:\s*(?<guid>[a-fA-F0-9\-]{36})\s*\((?<name>[^)]+)\)")]
    private static partial Regex PowerPlanLineRegex();
}
