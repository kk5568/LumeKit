using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationObservationRequestPlanner
{
    private readonly ObservationDemandRegistry _demandRegistry;
    private readonly ObservationScenePolicy _scenePolicy;
    private readonly Func<IReadOnlyCollection<ObservationInfoKind>> _getSunDemand;

    public AutomationObservationRequestPlanner(
        ObservationDemandRegistry demandRegistry,
        ObservationScenePolicy scenePolicy,
        Func<IReadOnlyCollection<ObservationInfoKind>> getSunDemand)
    {
        _demandRegistry = demandRegistry;
        _scenePolicy = scenePolicy;
        _getSunDemand = getSunDemand;
    }

    public ObservationRequest Create(string reason, Guid? correlationId = null, bool isLocked = false)
    {
        var requestCorrelationId = correlationId.GetValueOrDefault();
        if (requestCorrelationId == Guid.Empty)
        {
            requestCorrelationId = Guid.NewGuid();
        }

        if (isLocked || _scenePolicy.Current.Pause)
        {
            return new ObservationRequest(
                _scenePolicy.Current.Suspended ? ObservationUsageScene.Suspended : ObservationUsageScene.Locked,
                reason,
                Array.Empty<ObservationInfoKind>(),
                requestCorrelationId);
        }

        var scene = ResolveScene(reason);

        var periodic = reason.Split('+').All(segment => segment is "sun-loop" or "scene-check" or "cache-refresh");
        var items = _demandRegistry.GetAllowedUnion(_scenePolicy.Current,
            includeDeferred: !periodic || reason.Contains("cache-refresh", StringComparison.OrdinalIgnoreCase)).ToHashSet();

        // 只采这次事件真正可能改变的部分；认不出来的原因一律全量，宁可多采不漏采。
        if (TryResolveDirtyKinds(reason) is { } dirtyKinds)
        {
            items.IntersectWith(dirtyKinds);
        }
        if (items.Contains(ObservationInfoKind.ThemeMode)) items.UnionWith(_getSunDemand());

        return new ObservationRequest(scene, reason, items.ToArray(), requestCorrelationId);
    }

    /// <summary>
    /// 把触发这次刷新的原因翻译成"可能变了"的采集项。返回 null 表示按全量处理。
    /// 合并原因用 "+" 连接，任何一段认不出来就退回全量。
    /// </summary>
    private HashSet<ObservationInfoKind>? TryResolveDirtyKinds(string reason)
    {
        var kinds = new HashSet<ObservationInfoKind>();

        foreach (var segment in reason.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryResolveSegment(segment, kinds))
            {
                return null;
            }
        }

        return kinds;
    }

    private bool TryResolveSegment(string segment, HashSet<ObservationInfoKind> kinds)
    {
        if (segment.StartsWith("session-", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "system-power-resume", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "scene-changed", StringComparison.OrdinalIgnoreCase))
        {
            kinds.UnionWith(_demandRegistry.GetAllowedUnion(_scenePolicy.Current, includeDeferred: false));
            return true;
        }
        if (segment.StartsWith("system-power-", StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.PowerState);
            return true;
        }

        if (segment.Contains("bluetooth", StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(ObservationInfoKind.BluetoothState);
            kinds.Add(ObservationInfoKind.BluetoothRadioState);
            return true;
        }

        switch (segment.ToLowerInvariant())
        {
            case "sun-loop":
            case "sun-time-updated":
                kinds.UnionWith(_getSunDemand());
                kinds.Add(ObservationInfoKind.ThemeMode);
                return true;

            case "scene-check":
                kinds.Add(ObservationInfoKind.ForegroundWindow);
                return true;

            case "cache-refresh":
                kinds.UnionWith(_demandRegistry.GetUnion(ObservationDemandPurpose.BackgroundCache));
                return true;

            case "audio-playback-connected":
            case "audio-playback-disconnected":
                kinds.Add(ObservationInfoKind.BluetoothState);
                kinds.Add(ObservationInfoKind.AudioRoute);
                return true;

            case "default-output-changed":
                kinds.Add(ObservationInfoKind.AudioRoute);
                return true;

            case "power-changed":
                kinds.Add(ObservationInfoKind.PowerState);
                return true;

            case "network-state-changed":
                kinds.Add(ObservationInfoKind.NetworkState);
                return true;

            case "proxy-registry-changed":
            case "network-proxy-changed":
                kinds.Add(ObservationInfoKind.NetworkState);
                kinds.Add(ObservationInfoKind.ProxyState);
                return true;

            case "foreground-window-changed":
            case "process-started":
            case "process-stopped":
                kinds.Add(ObservationInfoKind.ForegroundWindow);
                return true;

            // 恢复及需求变化仍受当前场景和用途约束，不用周期刷新代替系统事件。
            default:
                return false;
        }
    }

    private ObservationUsageScene ResolveScene(string reason)
    {
        var current = _scenePolicy.Current;
        if (current.Suspended) return ObservationUsageScene.Suspended;
        if (current.Locked) return ObservationUsageScene.Locked;

        if (reason.Split('+').Contains("startup", StringComparer.OrdinalIgnoreCase))
        {
            return ObservationUsageScene.Startup;
        }

        if (reason.Contains("session-", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("system-power-resume", StringComparison.OrdinalIgnoreCase))
        {
            return ObservationUsageScene.Unlock;
        }

        if (current.LowBattery)
        {
            return ObservationUsageScene.LowBattery;
        }

        if (current.Game)
        {
            return ObservationUsageScene.Game;
        }

        if (current.Fullscreen)
        {
            return ObservationUsageScene.VideoPlayback;
        }
        if (current.Idle) return ObservationUsageScene.Idle;
        if (current.OnBattery) return ObservationUsageScene.OnBattery;
        if (current.AppBackground) return ObservationUsageScene.AppBackground;

        if (reason.Contains("foreground", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("proxy", StringComparison.OrdinalIgnoreCase))
        {
            return ObservationUsageScene.HeavyWork;
        }

        return ObservationUsageScene.LightWork;
    }
}
