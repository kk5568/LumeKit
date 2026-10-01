using System.Text.Json;
using BluetoothAudioReceiver.Services;

namespace BluetoothAudioReceiver.Automation;

public sealed record AutomationProgramListEntry(string Name, string Executable);

public sealed record AutomationProgramListDefinition(string Id, string Name, IReadOnlyList<AutomationProgramListEntry> Entries);

public static class AutomationProgramListCatalog
{
    public const string DefaultProgramsListId = "default-program-list";
    public const string GameProgramsListId = "game-program-list";
    public const string DefaultProgramsListName = "默认程序清单";
    public const string GameProgramsListName = "游戏程序清单";

    public static IReadOnlyList<AutomationProgramListEntry> Parse(string? raw)
        => ParseLists(raw).SelectMany(list => list.Entries).ToArray();

    public static IReadOnlyList<AutomationProgramListDefinition> ParseLists(string? raw, string? fallbackDefaultPrograms = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CreateDefaultLists(fallbackDefaultPrograms, null);
        }

        var normalized = raw.Trim();
        if (normalized.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var container = JsonSerializer.Deserialize(
                    normalized,
                    AppJsonContext.Default.AutomationProgramListContainer);
                if (container?.Lists?.Count > 0)
                {
                    return NormalizeLists(container.Lists);
                }
            }
            catch
            {
                // Fall back to legacy parsing below.
            }
        }

        if (normalized.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var entries = JsonSerializer.Deserialize(
                    normalized,
                    AppJsonContext.Default.ListAutomationProgramListEntry) as List<AutomationProgramListEntry> ?? [];
                return CreateDefaultLists(fallbackDefaultPrograms, NormalizeEntries(entries));
            }
            catch
            {
                // Fall back to legacy line-based parsing.
            }
        }

        var legacyEntries = ParseExecutableLines(normalized);
        return CreateDefaultLists(fallbackDefaultPrograms, legacyEntries);
    }

    public static string Serialize(IEnumerable<AutomationProgramListEntry> entries)
        => SerializeLists(CreateDefaultLists(null, NormalizeEntries(entries)));

    public static string SerializeLists(IEnumerable<AutomationProgramListDefinition> lists)
    {
        var normalizedLists = NormalizeLists(lists);
        return JsonSerializer.Serialize(
            new AutomationProgramListContainer(normalizedLists.ToList()),
            AppJsonContext.Default.AutomationProgramListContainer);
    }

    public static IReadOnlyList<AutomationProgramListEntry> GetEntriesForList(
        string? raw,
        string? listId,
        string? fallbackDefaultPrograms = null)
    {
        var normalizedListId = NormalizeListId(listId);
        var entries = ParseLists(raw, fallbackDefaultPrograms)
            .FirstOrDefault(list => string.Equals(list.Id, normalizedListId, StringComparison.OrdinalIgnoreCase))
            ?.Entries
            ?? [];

        if (string.Equals(normalizedListId, GameProgramsListId, StringComparison.OrdinalIgnoreCase))
        {
            return entries
                .Where(entry => !AutomationInstalledProgramScanner.IsExcludedGameTool($"{entry.Name} {entry.Executable}"))
                .ToArray();
        }

        return entries;
    }

    public static string ResolveListId(string? displayName)
    {
        var normalized = displayName?.Trim() ?? string.Empty;
        return normalized switch
        {
            DefaultProgramsListName => DefaultProgramsListId,
            GameProgramsListName => GameProgramsListId,
            _ => normalized
        };
    }

    public static string ResolveListDisplayName(string? listId)
    {
        return NormalizeListId(listId) switch
        {
            DefaultProgramsListId => DefaultProgramsListName,
            GameProgramsListId => GameProgramsListName,
            var value => value
        };
    }

    public static AutomationProgramListEntry CreateEntryFromExecutable(string executable)
    {
        var trimmed = executable?.Trim() ?? string.Empty;
        var fileName = Path.GetFileName(trimmed);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return new AutomationProgramListEntry(
            string.IsNullOrWhiteSpace(stem) ? trimmed : stem,
            string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName);
    }

    public static string ToDisplayText(AutomationProgramListEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            return entry.Executable;
        }

        var executableStem = Path.GetFileNameWithoutExtension(entry.Executable ?? string.Empty);
        return string.Equals(entry.Name, executableStem, StringComparison.OrdinalIgnoreCase)
            ? entry.Executable ?? string.Empty
            : $"{entry.Name} | {entry.Executable}";
    }

    public static string ResolveExecutable(IEnumerable<AutomationProgramListEntry> entries, string? displayOrExecutable)
    {
        if (string.IsNullOrWhiteSpace(displayOrExecutable))
        {
            return string.Empty;
        }

        var normalized = displayOrExecutable.Trim();
        var match = entries.FirstOrDefault(entry =>
            string.Equals(ToDisplayText(entry), normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Executable, normalized, StringComparison.OrdinalIgnoreCase));

        return match?.Executable ?? normalized;
    }

    public static string ResolveDisplayText(IEnumerable<AutomationProgramListEntry> entries, string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return string.Empty;
        }

        var normalized = executable.Trim();
        var match = entries.FirstOrDefault(entry =>
            string.Equals(entry.Executable, normalized, StringComparison.OrdinalIgnoreCase));

        return match is null ? normalized : ToDisplayText(match);
    }

    public static string SerializeExecutableLines(IEnumerable<AutomationProgramListEntry> entries)
        => string.Join("\n", NormalizeEntries(entries).Select(entry => entry.Executable));

    public static IReadOnlyList<AutomationProgramListEntry> ParseExecutableLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal))
            .Select(CreateEntryFromExecutable)
            .ToArray();
    }

    private static IReadOnlyList<AutomationProgramListDefinition> CreateDefaultLists(
        string? fallbackDefaultPrograms,
        IReadOnlyList<AutomationProgramListEntry>? fallbackGamePrograms)
    {
        return
        [
            new AutomationProgramListDefinition(
                DefaultProgramsListId,
                DefaultProgramsListName,
                ParseExecutableLines(fallbackDefaultPrograms)),
            new AutomationProgramListDefinition(
                GameProgramsListId,
                GameProgramsListName,
                fallbackGamePrograms ?? [])
        ];
    }

    private static IReadOnlyList<AutomationProgramListDefinition> NormalizeLists(IEnumerable<AutomationProgramListDefinition> lists)
    {
        var normalized = lists
            .Select(list => new AutomationProgramListDefinition(
                NormalizeListId(list.Id),
                ResolveListDisplayName(list.Id),
                NormalizeEntries(list.Entries, NormalizeListId(list.Id))))
            .ToDictionary(list => list.Id, StringComparer.OrdinalIgnoreCase);

        if (!normalized.ContainsKey(DefaultProgramsListId))
        {
            normalized[DefaultProgramsListId] = new AutomationProgramListDefinition(
                DefaultProgramsListId,
                DefaultProgramsListName,
                []);
        }

        if (!normalized.ContainsKey(GameProgramsListId))
        {
            normalized[GameProgramsListId] = new AutomationProgramListDefinition(
                GameProgramsListId,
                GameProgramsListName,
                []);
        }

        return normalized.Values
            .OrderBy(list => list.Id == DefaultProgramsListId ? 0 : list.Id == GameProgramsListId ? 1 : 2)
            .ToArray();
    }

    private static IReadOnlyList<AutomationProgramListEntry> NormalizeEntries(IEnumerable<AutomationProgramListEntry> entries, string? listId = null)
    {
        var normalizedListId = NormalizeListId(listId);

        var normalizedEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Executable))
            .Select(entry => new AutomationProgramListEntry(
                string.IsNullOrWhiteSpace(entry.Name) ? Path.GetFileNameWithoutExtension(entry.Executable.Trim()) : entry.Name.Trim(),
                Path.GetFileName(entry.Executable.Trim())))
            .GroupBy(entry => entry.Executable, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (string.Equals(normalizedListId, GameProgramsListId, StringComparison.OrdinalIgnoreCase))
        {
            normalizedEntries = normalizedEntries
                .Where(entry => !AutomationInstalledProgramScanner.IsExcludedGameTool($"{entry.Name} {entry.Executable}"))
                .ToArray();
        }

        return normalizedEntries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeListId(string? listId)
    {
        var normalized = listId?.Trim() ?? string.Empty;
        return normalized switch
        {
            "" => DefaultProgramsListId,
            DefaultProgramsListName => DefaultProgramsListId,
            GameProgramsListName => GameProgramsListId,
            _ => normalized
        };
    }

    internal sealed record AutomationProgramListContainer(List<AutomationProgramListDefinition> Lists);
}
