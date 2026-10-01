using Microsoft.Win32;

namespace BluetoothAudioReceiver.Automation;

public static class AutomationInstalledProgramScanner
{
    private static readonly string[] SystemProgramKeywords =
    [
        "applicationframehost.exe",
        "backgroundtaskhost.exe",
        "cmd.exe",
        "conhost.exe",
        "csrss.exe",
        "ctfmon.exe",
        "dllhost.exe",
        "dwm.exe",
        "explorer.exe",
        "fontdrvhost.exe",
        "lockapp.exe",
        "searchhost.exe",
        "searchindexer.exe",
        "shellexperiencehost.exe",
        "sihost.exe",
        "smss.exe",
        "startmenuexperiencehost.exe",
        "svchost.exe",
        "tabtip.exe",
        "taskhostw.exe",
        "textinputhost.exe",
        "useroobebroker.exe",
        "widgets.exe",
        "wudfrd.exe"
    ];

    private static readonly string[] GamePathKeywords =
    [
        "steamapps",
        "riot games",
        "epic games",
        "battle.net",
        "blizzard",
        "ubisoft",
        "ea games",
        "xboxgames",
        "game pass",
        "hoyoverse",
        "kuro game",
        "wuthering waves",
        "genshin impact",
        "star rail"
    ];

    private static readonly string[] HiddenEntryReleaseTypes =
    [
        "hotfix",
        "security update",
        "update rollup",
        "servicepack"
    ];

    private static readonly string[] NonUserProgramKeywords =
    [
        "driver",
        "runtime",
        "redistributable",
        "redist",
        "sdk",
        "framework",
        "webview",
        "edgewebview",
        "microsoft visual c++",
        ".net",
        "vc_redist",
        "prereq",
        "prerequisite",
        "installer",
        "setup",
        "update",
        "service",
        "component",
        "extension",
        "plugin",
        "helper",
        "crash handler",
        "uninstall",
        "repair",
        "telemetry"
    ];

    private static readonly string[] GameKeywords =
    [
        "steam",
        "epic",
        "gog",
        "uplay",
        "ubisoft",
        "riot",
        "battle.net",
        "battlenet",
        "game",
        "games",
        "launcher",
        "valorant",
        "league",
        "genshin",
        "star rail",
        "honkai",
        "overwatch",
        "cs2",
        "dota",
        "apex",
        "pubg",
        "elden ring",
        "black myth",
        "wuthering"
    ];

    internal static readonly string[] GameToolKeywords =
    [
        "steam.exe",
        "steamwebhelper.exe",
        "steamservice.exe",
        "epicgameslauncher.exe",
        "epicwebhelper.exe",
        "epiconlineservices",
        "riotclientservices.exe",
        "riotclientux.exe",
        "riotclientcrashhandler.exe",
        "battle.net.exe",
        "battle.net helper.exe",
        "agent.exe",
        "upc.exe",
        "ubisoftconnect.exe",
        "eadesktop.exe",
        "ea app",
        "origin.exe",
        "gog galaxy",
        "galaxyclient.exe",
        "wegame.exe",
        "razer cortex",
        "xunyou",
        "迅游",
        "leigod",
        "雷神加速器",
        "uu",
        "网易uu",
        "uu加速器",
        "biubiu",
        "海豚加速器"
    ];

    public static IReadOnlyList<AutomationProgramListDefinition> BuildInstalledProgramLists(string? fallbackDefaultPrograms = null)
    {
        var scannedEntries = EnumerateInstalledPrograms();
        var defaultEntries = scannedEntries
            .Where(entry => !IsGameCandidate(entry))
            .Select(ToProgramListEntry)
            .ToArray();
        var gameEntries = scannedEntries
            .Where(IsGameCandidate)
            .Select(ToProgramListEntry)
            .ToArray();

        return
        [
            new AutomationProgramListDefinition(
                AutomationProgramListCatalog.DefaultProgramsListId,
                AutomationProgramListCatalog.DefaultProgramsListName,
                defaultEntries.Length == 0
                    ? AutomationProgramListCatalog.ParseExecutableLines(fallbackDefaultPrograms)
                    : defaultEntries),
            new AutomationProgramListDefinition(
                AutomationProgramListCatalog.GameProgramsListId,
                AutomationProgramListCatalog.GameProgramsListName,
                gameEntries)
        ];
    }

    public static string BuildSerializedInstalledProgramLists(string? fallbackDefaultPrograms = null)
        => AutomationProgramListCatalog.SerializeLists(BuildInstalledProgramLists(fallbackDefaultPrograms));

    private static IReadOnlyList<InstalledProgramCandidate> EnumerateInstalledPrograms()
    {
        var results = new Dictionary<string, InstalledProgramCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            ReadUninstallKey(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
            ReadUninstallKey(root, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results);
        }

        return results.Values
            .Where(entry => !IsSystemProgram(entry.Executable))
            .Where(IsUserFacingProgram)
            .OrderBy(entry => entry.Executable, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ReadUninstallKey(RegistryKey root, string path, Dictionary<string, InstalledProgramCandidate> results)
    {
        using var uninstallKey = root.OpenSubKey(path);
        if (uninstallKey is null)
        {
            return;
        }

        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            using var subKey = uninstallKey.OpenSubKey(subKeyName);
            if (subKey is null)
            {
                continue;
            }

            var candidate = CreateInstalledProgramCandidate(subKey);
            if (candidate is null)
            {
                continue;
            }

            results[candidate.Executable] = candidate;
        }
    }

    private static InstalledProgramCandidate? CreateInstalledProgramCandidate(RegistryKey subKey)
    {
        var displayIcon = subKey.GetValue("DisplayIcon") as string;
        var installLocation = subKey.GetValue("InstallLocation") as string;
        var displayName = subKey.GetValue("DisplayName") as string;
        var publisher = subKey.GetValue("Publisher") as string;
        var releaseType = subKey.GetValue("ReleaseType") as string;
        var parentKeyName = subKey.GetValue("ParentKeyName") as string;
        var systemComponent = ReadDwordValue(subKey, "SystemComponent");

        if (systemComponent == 1 ||
            !string.IsNullOrWhiteSpace(parentKeyName) ||
            HiddenEntryReleaseTypes.Any(value => string.Equals(value, releaseType, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var fromIcon = NormalizeExecutableCandidate(displayIcon);
        if (!string.IsNullOrWhiteSpace(fromIcon))
        {
            return new InstalledProgramCandidate(
                displayName ?? string.Empty,
                fromIcon,
                installLocation ?? string.Empty,
                publisher ?? string.Empty,
                releaseType ?? string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            var candidates = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .OrderBy(fileName => ScoreExecutable(fileName!, displayName))
                .ToArray();

            if (candidates.Length > 0)
            {
                return new InstalledProgramCandidate(
                    displayName ?? string.Empty,
                    candidates[0]!,
                    installLocation,
                    publisher ?? string.Empty,
                    releaseType ?? string.Empty);
            }
        }

        return null;
    }

    private static string NormalizeExecutableCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var normalized = candidate.Trim().Trim('"');
        var commaIndex = normalized.IndexOf(',');
        if (commaIndex >= 0)
        {
            normalized = normalized[..commaIndex];
        }

        if (!normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return Path.GetFileName(normalized);
    }

    private static int ScoreExecutable(string fileName, string? displayName)
    {
        var normalizedFileName = fileName.ToLowerInvariant();
        var normalizedDisplayName = (displayName ?? string.Empty).ToLowerInvariant();

        if (normalizedFileName == normalizedDisplayName || normalizedFileName == $"{normalizedDisplayName}.exe")
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(normalizedDisplayName) &&
            normalizedFileName.Contains(normalizedDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static bool IsGameCandidate(InstalledProgramCandidate candidate)
    {
        var identityText = $"{candidate.DisplayName} {candidate.Executable} {candidate.Publisher}";
        if (IsExcludedGameTool(identityText))
        {
            return false;
        }

        if (AutomationGameRecognitionCatalog.MatchesKnownGame(identityText))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(candidate.InstallLocation) &&
            GamePathKeywords.Any(keyword => candidate.InstallLocation.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return GameKeywords.Any(keyword => identityText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUserFacingProgram(InstalledProgramCandidate candidate)
    {
        var identityText = $"{candidate.DisplayName} {candidate.Executable} {candidate.Publisher} {candidate.ReleaseType}";
        return !NonUserProgramKeywords.Any(keyword => identityText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static AutomationProgramListEntry ToProgramListEntry(InstalledProgramCandidate candidate)
    {
        var displayName = string.IsNullOrWhiteSpace(candidate.DisplayName)
            ? Path.GetFileNameWithoutExtension(candidate.Executable)
            : candidate.DisplayName.Trim();

        return new AutomationProgramListEntry(displayName, candidate.Executable);
    }

    private static bool IsSystemProgram(string executable)
    {
        var normalized = executable.Trim().ToLowerInvariant();
        return SystemProgramKeywords.Contains(normalized);
    }

    internal static bool IsExcludedGameTool(string? identityText)
    {
        if (string.IsNullOrWhiteSpace(identityText))
        {
            return false;
        }

        return GameToolKeywords.Any(keyword => identityText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static int ReadDwordValue(RegistryKey subKey, string valueName)
    {
        return subKey.GetValue(valueName) switch
        {
            int intValue => intValue,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => 0
        };
    }

    private sealed record InstalledProgramCandidate(
        string DisplayName,
        string Executable,
        string InstallLocation,
        string Publisher,
        string ReleaseType);
}
