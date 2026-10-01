using System.Text.Json;

namespace BluetoothAudioReceiver.Automation;

public static class AutomationGameRecognitionCatalog
{
    private static readonly Lazy<GameRecognitionIndex> RecognitionIndex = new(LoadIndex);

    public static bool MatchesKnownGame(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = NormalizeSearchText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return ContainsAny(normalized, RecognitionIndex.Value.Platforms)
            || ContainsAny(normalized, RecognitionIndex.Value.Franchises)
            || ContainsAny(normalized, RecognitionIndex.Value.Titles);
    }

    private static GameRecognitionIndex LoadIndex()
    {
        var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var franchises = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var catalogPath = Path.Combine(AppContext.BaseDirectory, "Automation", "GameRecognitionSeedCatalog.json");
        if (!File.Exists(catalogPath))
        {
            return new GameRecognitionIndex(platforms, franchises, titles);
        }

        using var stream = File.OpenRead(catalogPath);
        using var document = JsonDocument.Parse(stream);

        if (document.RootElement.TryGetProperty("platforms", out var platformsElement))
        {
            AddStringArray(platformsElement, platforms);
        }

        if (document.RootElement.TryGetProperty("franchises", out var franchisesElement) &&
            franchisesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var category in franchisesElement.EnumerateObject())
            {
                AddStringArray(category.Value, franchises);
            }
        }

        if (document.RootElement.TryGetProperty("titles", out var titlesElement))
        {
            AddStringArray(titlesElement, titles);
        }

        return new GameRecognitionIndex(platforms, franchises, titles);
    }

    private static void AddStringArray(JsonElement element, HashSet<string> target)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var normalized = NormalizeSearchText(item.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                target.Add(normalized);
            }
        }
    }

    private static bool ContainsAny(string normalizedText, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (normalizedText.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            text.Trim()
                .ToLowerInvariant()
                .Split([
                    ' ', '-', '_', ':', '.', ',', '(', ')', '[', ']', '{', '}', '/', '\\', '+', '&', '\"', '\''
                ], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private sealed record GameRecognitionIndex(
        IReadOnlySet<string> Platforms,
        IReadOnlySet<string> Franchises,
        IReadOnlySet<string> Titles);
}
