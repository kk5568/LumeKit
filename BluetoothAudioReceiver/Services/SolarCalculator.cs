using Serilog;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BluetoothAudioReceiver.Services;

public sealed class SolarCalculator
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(SolarCalculator));
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static readonly string CacheFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAudioReceiver",
        "AutoDayNightCache.json");

    private const int HistoryRetentionDays = 7;
    private const int SevereDeviationThresholdMinutes = 20;

    public async Task<(double Latitude, double Longitude)?> ResolveSystemLocationAsync(CancellationToken token)
    {
        try
        {
            var locator = new Windows.Devices.Geolocation.Geolocator
            {
                DesiredAccuracyInMeters = 500
            };

            var position = await locator.GetGeopositionAsync().AsTask(token);
            var latitude = position.Coordinate.Point.Position.Latitude;
            var longitude = position.Coordinate.Point.Position.Longitude;
            return IsValidCoordinates(latitude, longitude) ? (latitude, longitude) : null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "ResolveSystemLocationAsync failed");
            return null;
        }
    }

    public async Task<ResolvedSunTimes?> ResolveBestSunTimesAsync((double Latitude, double Longitude)? location, CancellationToken token)
    {
        var cache = LoadCache();
        var today = DateOnly.FromDateTime(DateTime.Now.Date);

        if (location is { } currentLocation)
        {
            var online = await TryResolveOnlineAsync(currentLocation.Latitude, currentLocation.Longitude, today, cache, token);
            if (online is not null)
            {
                return online;
            }
        }

        var historyFallback = TryResolveFromRecentHistory(today, cache);
        if (historyFallback is not null)
        {
            return historyFallback;
        }

        var localFallback = TryResolveFromLocalProjection(today, location, cache);
        if (localFallback is not null)
        {
            SaveCache(localFallback.UpdatedCache);
            return localFallback.Result;
        }

        var latestCacheFallback = TryResolveFromLatestTrustedCache(cache);
        if (latestCacheFallback is not null)
        {
            return latestCacheFallback;
        }

        return null;
    }

    public static (TimeSpan Sunrise, TimeSpan Sunset) CalculateSunTimes(DateTime date, double latitude, double longitude)
    {
        const double zenith = 90.8333;

        static double DegToRad(double deg) => deg * Math.PI / 180.0;
        static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

        static double Normalize(double value, double max)
        {
            value %= max;
            if (value < 0)
            {
                value += max;
            }

            return value;
        }

        double Calc(bool sunrise)
        {
            var dayOfYear = date.DayOfYear;
            var lngHour = longitude / 15.0;
            var t = sunrise ? dayOfYear + ((6.0 - lngHour) / 24.0) : dayOfYear + ((18.0 - lngHour) / 24.0);
            var m = (0.9856 * t) - 3.289;
            var l = m + (1.916 * Math.Sin(DegToRad(m))) + (0.020 * Math.Sin(DegToRad(2 * m))) + 282.634;
            l = Normalize(l, 360.0);
            var ra = RadToDeg(Math.Atan(0.91764 * Math.Tan(DegToRad(l))));
            ra = Normalize(ra, 360.0);
            var lQuadrant = Math.Floor(l / 90.0) * 90.0;
            var raQuadrant = Math.Floor(ra / 90.0) * 90.0;
            ra = (ra + (lQuadrant - raQuadrant)) / 15.0;
            var sinDec = 0.39782 * Math.Sin(DegToRad(l));
            var cosDec = Math.Cos(Math.Asin(sinDec));
            var cosH = (Math.Cos(DegToRad(zenith)) - (sinDec * Math.Sin(DegToRad(latitude)))) /
                       (cosDec * Math.Cos(DegToRad(latitude)));

            if (cosH > 1 || cosH < -1)
            {
                return sunrise ? 6.0 : 18.0;
            }

            var h = sunrise ? 360.0 - RadToDeg(Math.Acos(cosH)) : RadToDeg(Math.Acos(cosH));
            h /= 15.0;
            var tLocal = h + ra - (0.06571 * t) - 6.622;
            var ut = tLocal - lngHour;
            return Normalize(ut, 24.0);
        }

        var sunriseUtc = Calc(true);
        var sunsetUtc = Calc(false);
        var offset = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
        return (
            TimeSpan.FromHours(Normalize(sunriseUtc + offset, 24.0)),
            TimeSpan.FromHours(Normalize(sunsetUtc + offset, 24.0)));
    }

    private async Task<ResolvedSunTimes?> TryResolveOnlineAsync(
        double latitude,
        double longitude,
        DateOnly today,
        AutoDayNightCache? cache,
        CancellationToken token)
    {
        var url = $"https://api.sunrise-sunset.org/json?lat={latitude}&lng={longitude}&formatted=0";

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.SunriseSunsetResponse, token);
            if (payload?.Results is null)
            {
                return null;
            }

            if (!TryConvertApiDateTimeToLocal(payload.Results.Sunrise, out var sunriseLocal) ||
                !TryConvertApiDateTimeToLocal(payload.Results.Sunset, out var sunsetLocal))
            {
                return null;
            }

            var sunrise = sunriseLocal.TimeOfDay;
            var sunset = sunsetLocal.TimeOfDay;
            if (sunrise >= sunset)
            {
                return null;
            }

            var onlineEntry = CreateHistoryEntry(
                today,
                latitude,
                longitude,
                sunrise,
                sunset,
                SunTimeSource.OnlineApi,
                isTrusted: true,
                note: string.Empty);

            if (HasSevereDeviation(onlineEntry, cache))
            {
                _log.Warning(
                    "Rejecting online sun times due to severe deviation. lat={Latitude}, lng={Longitude}, sunrise={Sunrise}, sunset={Sunset}",
                    latitude,
                    longitude,
                    sunrise,
                    sunset);
                return null;
            }

            var updatedCache = UpdateCache(cache, onlineEntry, [onlineEntry]);
            SaveCache(updatedCache);

            return new ResolvedSunTimes(
                sunrise,
                sunset,
                latitude,
                longitude,
                SunTimeSource.OnlineApi,
                IsFallback: false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TryResolveOnlineAsync failed");
            return null;
        }
    }

    private static ResolvedSunTimes? TryResolveFromRecentHistory(DateOnly today, AutoDayNightCache? cache)
    {
        var latestTrusted = GetTrustedHistory(cache)
            .OrderByDescending(entry => ParseDate(entry.Date))
            .ThenByDescending(entry => entry.SavedAtUtc)
            .FirstOrDefault();

        if (latestTrusted is null)
        {
            return null;
        }

        if (!TryParseCachedSunTimes(latestTrusted.Sunrise, latestTrusted.Sunset, out var sunrise, out var sunset))
        {
            return null;
        }

        return new ResolvedSunTimes(
            sunrise,
            sunset,
            latestTrusted.Latitude,
            latestTrusted.Longitude,
            SunTimeSource.RecentTrustedHistory,
            IsFallback: ParseDate(latestTrusted.Date) != today);
    }

    private static LocalProjectionFallback? TryResolveFromLocalProjection(
        DateOnly today,
        (double Latitude, double Longitude)? location,
        AutoDayNightCache? cache)
    {
        var fallbackCoordinates = location ?? GetFallbackCoordinates(cache);
        if (fallbackCoordinates is null)
        {
            return null;
        }

        var entries = BuildLocalProjectionEntries(today, fallbackCoordinates.Value.Latitude, fallbackCoordinates.Value.Longitude);
        if (entries.Count == 0)
        {
            return null;
        }

        var todayEntry = entries[0];
        var updatedCache = UpdateCache(cache, todayEntry, entries);

        return new LocalProjectionFallback(
            new ResolvedSunTimes(
                TimeSpan.Parse(todayEntry.Sunrise, CultureInfo.InvariantCulture),
                TimeSpan.Parse(todayEntry.Sunset, CultureInfo.InvariantCulture),
                todayEntry.Latitude,
                todayEntry.Longitude,
                SunTimeSource.LocalProjection,
                IsFallback: true),
            updatedCache);
    }

    private static ResolvedSunTimes? TryResolveFromLatestTrustedCache(AutoDayNightCache? cache)
    {
        var trusted = cache?.LastTrustedEntry;
        if (trusted is null)
        {
            return null;
        }

        return TryParseCachedSunTimes(trusted.Sunrise, trusted.Sunset, out var sunrise, out var sunset)
            ? new ResolvedSunTimes(sunrise, sunset, trusted.Latitude, trusted.Longitude, SunTimeSource.LastTrustedCache, IsFallback: true)
            : null;
    }

    private static List<SunTimeHistoryEntry> BuildLocalProjectionEntries(DateOnly startDate, double latitude, double longitude)
    {
        var entries = new List<SunTimeHistoryEntry>(HistoryRetentionDays);
        for (var i = 0; i < HistoryRetentionDays; i++)
        {
            var date = startDate.AddDays(i);
            var (sunrise, sunset) = CalculateSunTimes(date.ToDateTime(TimeOnly.MinValue), latitude, longitude);
            entries.Add(CreateHistoryEntry(
                date,
                latitude,
                longitude,
                sunrise,
                sunset,
                SunTimeSource.LocalProjection,
                isTrusted: false,
                note: "fallback"));
        }

        return entries;
    }

    private static SunTimeHistoryEntry CreateHistoryEntry(
        DateOnly date,
        double latitude,
        double longitude,
        TimeSpan sunrise,
        TimeSpan sunset,
        string source,
        bool isTrusted,
        string note)
    {
        return new SunTimeHistoryEntry
        {
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LocationKey = BuildLocationCacheKey(latitude, longitude),
            Latitude = latitude,
            Longitude = longitude,
            Sunrise = sunrise.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            Sunset = sunset.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            SavedAtUtc = DateTimeOffset.UtcNow,
            Source = source,
            IsTrusted = isTrusted,
            Note = note
        };
    }

    private static AutoDayNightCache UpdateCache(
        AutoDayNightCache? cache,
        SunTimeHistoryEntry currentEntry,
        IReadOnlyList<SunTimeHistoryEntry> projectedEntries)
    {
        var working = cache ?? new AutoDayNightCache();
        var history = working.History ?? [];

        history.RemoveAll(entry => string.Equals(entry.Date, currentEntry.Date, StringComparison.OrdinalIgnoreCase));
        history.Add(currentEntry);

        working.LocationKey = currentEntry.LocationKey;
        working.Latitude = currentEntry.Latitude;
        working.Longitude = currentEntry.Longitude;
        working.Sunrise = currentEntry.Sunrise;
        working.Sunset = currentEntry.Sunset;
        working.SavedAtUtc = DateTimeOffset.UtcNow;
        working.LastTrustedEntry = currentEntry.IsTrusted
            ? currentEntry
            : working.LastTrustedEntry;
        working.History = history
            .OrderByDescending(entry => ParseDate(entry.Date))
            .ThenByDescending(entry => entry.SavedAtUtc)
            .Take(HistoryRetentionDays)
            .ToList();
        working.ProjectedEntries = projectedEntries
            .OrderBy(entry => ParseDate(entry.Date))
            .ToList();

        return working;
    }

    private static AutoDayNightCache? LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(CacheFilePath), AppJsonContext.Default.AutoDayNightCache);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(AutoDayNightCache cache)
    {
        var dir = Path.GetDirectoryName(CacheFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(cache, AppJsonContext.Default.AutoDayNightCache));
    }

    private static bool HasSevereDeviation(SunTimeHistoryEntry onlineEntry, AutoDayNightCache? cache)
    {
        var trustedHistory = GetTrustedHistory(cache).ToArray();
        if (trustedHistory.Length == 0)
        {
            return false;
        }

        var sunriseAverageMinutes = trustedHistory
            .Select(entry => TimeSpan.Parse(entry.Sunrise, CultureInfo.InvariantCulture).TotalMinutes)
            .Average();
        var sunsetAverageMinutes = trustedHistory
            .Select(entry => TimeSpan.Parse(entry.Sunset, CultureInfo.InvariantCulture).TotalMinutes)
            .Average();
        var onlineSunriseMinutes = TimeSpan.Parse(onlineEntry.Sunrise, CultureInfo.InvariantCulture).TotalMinutes;
        var onlineSunsetMinutes = TimeSpan.Parse(onlineEntry.Sunset, CultureInfo.InvariantCulture).TotalMinutes;

        return Math.Abs(onlineSunriseMinutes - sunriseAverageMinutes) > SevereDeviationThresholdMinutes ||
               Math.Abs(onlineSunsetMinutes - sunsetAverageMinutes) > SevereDeviationThresholdMinutes;
    }

    private static IEnumerable<SunTimeHistoryEntry> GetTrustedHistory(AutoDayNightCache? cache)
    {
        if (cache?.History is null)
        {
            return [];
        }

        var cutoff = DateOnly.FromDateTime(DateTime.Now.Date.AddDays(-(HistoryRetentionDays - 1)));
        return cache.History.Where(entry =>
            entry.IsTrusted &&
            ParseDate(entry.Date) >= cutoff &&
            TryParseCachedSunTimes(entry.Sunrise, entry.Sunset, out _, out _));
    }

    private static (double Latitude, double Longitude)? GetFallbackCoordinates(AutoDayNightCache? cache)
    {
        var trusted = cache?.LastTrustedEntry;
        if (trusted is not null && IsValidCoordinates(trusted.Latitude, trusted.Longitude))
        {
            return (trusted.Latitude, trusted.Longitude);
        }

        if (cache is not null && IsValidCoordinates(cache.Latitude, cache.Longitude))
        {
            return (cache.Latitude, cache.Longitude);
        }

        return null;
    }

    private static bool IsValidCoordinates(double latitude, double longitude)
    {
        return latitude is >= -90 and <= 90 &&
               longitude is >= -180 and <= 180 &&
               (Math.Abs(latitude) > double.Epsilon || Math.Abs(longitude) > double.Epsilon);
    }

    private static string BuildLocationCacheKey(double latitude, double longitude)
    {
        return $"{Math.Round(latitude, 4):0.0000}_{Math.Round(longitude, 4):0.0000}";
    }

    private static bool TryConvertApiDateTimeToLocal(string value, out DateTime localTime)
    {
        localTime = default;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return false;
        }

        localTime = TimeZoneInfo.ConvertTime(dto, TimeZoneInfo.Local).DateTime;
        return true;
    }

    private static bool TryParseCachedSunTimes(string sunriseText, string sunsetText, out TimeSpan sunrise, out TimeSpan sunset)
    {
        sunrise = default;
        sunset = default;
        return TimeSpan.TryParse(sunriseText, CultureInfo.InvariantCulture, out sunrise) &&
               TimeSpan.TryParse(sunsetText, CultureInfo.InvariantCulture, out sunset) &&
               sunrise < sunset;
    }

    private static DateOnly ParseDate(string text)
    {
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateOnly.MinValue;
    }

    public sealed record ResolvedSunTimes(
        TimeSpan Sunrise,
        TimeSpan Sunset,
        double Latitude,
        double Longitude,
        string Source,
        bool IsFallback);

    private sealed record LocalProjectionFallback(ResolvedSunTimes Result, AutoDayNightCache UpdatedCache);

    internal static class SunTimeSource
    {
        public const string OnlineApi = "online-api";
        public const string RecentTrustedHistory = "recent-trusted-history";
        public const string LocalProjection = "local-projection";
        public const string LastTrustedCache = "last-trusted-cache";
    }

    internal sealed class SunriseSunsetResponse
    {
        [JsonPropertyName("results")]
        public SunriseSunsetResults? Results { get; set; }
    }

    internal sealed class SunriseSunsetResults
    {
        [JsonPropertyName("sunrise")]
        public string Sunrise { get; set; } = string.Empty;

        [JsonPropertyName("sunset")]
        public string Sunset { get; set; } = string.Empty;
    }

    internal sealed class AutoDayNightCache
    {
        public string LocationKey { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Sunrise { get; set; } = string.Empty;
        public string Sunset { get; set; } = string.Empty;
        public DateTimeOffset SavedAtUtc { get; set; }
        public List<SunTimeHistoryEntry> History { get; set; } = [];
        public List<SunTimeHistoryEntry> ProjectedEntries { get; set; } = [];
        public SunTimeHistoryEntry? LastTrustedEntry { get; set; }
    }

    internal sealed class SunTimeHistoryEntry
    {
        public string Date { get; set; } = string.Empty;
        public string LocationKey { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Sunrise { get; set; } = string.Empty;
        public string Sunset { get; set; } = string.Empty;
        public DateTimeOffset SavedAtUtc { get; set; }
        public string Source { get; set; } = string.Empty;
        public bool IsTrusted { get; set; }
        public string Note { get; set; } = string.Empty;
    }
}
