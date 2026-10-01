using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using BluetoothAudioReceiver.Core.Contracts.Services;

namespace BluetoothAudioReceiver.Core.Helpers;

public static class DevicePriorityHelper
{
    public const string RecentDeviceIdsKey = "BluetoothRecentDeviceIdsV1";
    private const int MaxRecentDeviceIds = 12;
    private static readonly ConditionalWeakTable<ILocalSettingsService, RecentDeviceCacheState> RecentDeviceCache = new();

    public static IReadOnlyList<string> GetRecentDeviceIds(ILocalSettingsService localSettingsService)
    {
        var state = RecentDeviceCache.GetValue(localSettingsService, _ => new RecentDeviceCacheState());
        lock (state.Gate)
        {
            if (state.RecentDeviceIds is not null)
            {
                return state.RecentDeviceIds;
            }
        }

        var deviceIds = localSettingsService.ReadSetting(RecentDeviceIdsKey, new List<string>()) ?? [];
        var normalized = NormalizeDeviceIds(deviceIds);

        lock (state.Gate)
        {
            state.RecentDeviceIds = normalized;
        }

        return normalized;
    }

    public static async Task MarkDeviceAsUsedAsync(ILocalSettingsService localSettingsService, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        var recentIds = new List<string> { deviceId.Trim() };
        var existingIds = GetRecentDeviceIds(localSettingsService);

        foreach (var id in existingIds)
        {
            if (!string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                recentIds.Add(id);
            }

            if (recentIds.Count >= MaxRecentDeviceIds)
            {
                break;
            }
        }

        await localSettingsService.SaveSettingAsync(RecentDeviceIdsKey, recentIds);
        SetRecentDeviceIds(localSettingsService, recentIds);
    }

    public static IReadOnlyList<T> OrderDevices<T>(
        IEnumerable<T> devices,
        Func<T, string> idSelector,
        Func<T, string> nameSelector,
        IReadOnlyCollection<string>? recentDeviceIds = null,
        Func<T, bool>? isActivePredicate = null)
    {
        var recentIndexMap = BuildIndexMap(recentDeviceIds);

        return devices
            .Where(device => !string.IsNullOrWhiteSpace(idSelector(device)))
            .OrderByDescending(device => isActivePredicate?.Invoke(device) == true)
            .ThenBy(device => GetRecentIndex(device, idSelector, recentIndexMap))
            .ThenBy(device => string.IsNullOrWhiteSpace(nameSelector(device)) ? idSelector(device) : nameSelector(device), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, int> BuildIndexMap(IReadOnlyCollection<string>? recentDeviceIds)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (recentDeviceIds == null)
        {
            return map;
        }

        var index = 0;
        foreach (var id in recentDeviceIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var trimmed = id.Trim();
            if (map.ContainsKey(trimmed))
            {
                continue;
            }

            map[trimmed] = index++;
        }

        return map;
    }

    private static int GetRecentIndex<T>(T device, Func<T, string> idSelector, IReadOnlyDictionary<string, int> recentIndexMap)
    {
        var id = idSelector(device);
        return string.IsNullOrWhiteSpace(id) || !recentIndexMap.TryGetValue(id, out var index)
            ? int.MaxValue
            : index;
    }

    private static IReadOnlyList<string> NormalizeDeviceIds(IEnumerable<string> deviceIds)
    {
        var normalized = new List<string>(MaxRecentDeviceIds);
        foreach (var id in deviceIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var trimmed = id.Trim();
            if (normalized.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            normalized.Add(trimmed);
            if (normalized.Count >= MaxRecentDeviceIds)
            {
                break;
            }
        }

        return normalized;
    }

    private static void SetRecentDeviceIds(ILocalSettingsService localSettingsService, IReadOnlyList<string> recentIds)
    {
        var state = RecentDeviceCache.GetValue(localSettingsService, _ => new RecentDeviceCacheState());
        lock (state.Gate)
        {
            state.RecentDeviceIds = recentIds.ToList().AsReadOnly();
        }
    }

    private sealed class RecentDeviceCacheState
    {
        public object Gate { get; } = new();

        public IReadOnlyList<string>? RecentDeviceIds { get; set; }
    }
}
