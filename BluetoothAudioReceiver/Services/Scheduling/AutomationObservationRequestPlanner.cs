using BluetoothAudioReceiver.Automation.Models;
using Windows.System.Power;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationObservationRequestPlanner
{
    private readonly ObservationDemandRegistry _demandRegistry;
    private readonly Func<string?> _getForegroundProcessName;
    private readonly Func<string?> _getForegroundWindowTitle;
    private readonly Func<bool> _isForegroundFullscreen;
    private readonly Func<string?, string?, bool> _isLikelyGame;

    public AutomationObservationRequestPlanner(
        ObservationDemandRegistry demandRegistry,
        Func<string?> getForegroundProcessName,
        Func<string?> getForegroundWindowTitle,
        Func<bool> isForegroundFullscreen,
        Func<string?, string?, bool> isLikelyGame)
    {
        _demandRegistry = demandRegistry;
        _getForegroundProcessName = getForegroundProcessName;
        _getForegroundWindowTitle = getForegroundWindowTitle;
        _isForegroundFullscreen = isForegroundFullscreen;
        _isLikelyGame = isLikelyGame;
    }

    public ObservationRequest Create(string reason, Guid? correlationId = null, bool isLocked = false)
    {
        var requestCorrelationId = correlationId.GetValueOrDefault();
        if (requestCorrelationId == Guid.Empty)
        {
            requestCorrelationId = Guid.NewGuid();
        }

        if (isLocked)
        {
            return new ObservationRequest(
                ObservationUsageScene.Locked,
                reason,
                Array.Empty<ObservationInfoKind>(),
                requestCorrelationId);
        }

        var scene = ResolveScene(reason);

        // 采集范围只由各模块登记的并集决定，不再按场景裁剪。
        // 场景仅作为日志和诊断标签保留。
        var items = _demandRegistry.GetUnion().ToHashSet();

        // 只采这次事件真正可能改变的部分；认不出来的原因一律全量，宁可多采不漏采。
        if (TryResolveDirtyKinds(reason) is { Count: > 0 } dirtyKinds)
        {
            items.IntersectWith(dirtyKinds);
        }

        return new ObservationRequest(scene, reason, items.ToArray(), requestCorrelationId);
    }

    /// <summary>
    /// 把触发这次刷新的原因翻译成"可能变了"的采集项。返回 null 表示按全量处理。
    /// 合并原因用 "+" 连接，任何一段认不出来就退回全量。
    /// </summary>
    private static HashSet<ObservationInfoKind>? TryResolveDirtyKinds(string reason)
    {
        var kinds = new HashSet<ObservationInfoKind>();

        foreach (var segment in reason.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryResolveSegment(segment, kinds))
            {
                return null;
            }
        }

        return kinds.Count == 0 ? null : kinds;
    }

    private static bool TryResolveSegment(string segment, HashSet<ObservationInfoKind> kinds)
    {
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
            case "audio-playback-connected":
            case "audio-playback-disconnected":
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

            // 启动、"sun-loop"（每分钟的定时兜底）、解锁、改时间、日出日落、代理通知之外的情况
            // 都按全量采，保证最迟一分钟内各项一定对齐。
            default:
                return false;
        }
    }

    private ObservationUsageScene ResolveScene(string reason)
    {
        var foregroundProcessName = _getForegroundProcessName();
        var foregroundWindowTitle = _getForegroundWindowTitle();
        var isFullscreen = _isForegroundFullscreen();

        if (string.Equals(reason, "startup", StringComparison.OrdinalIgnoreCase))
        {
            return ObservationUsageScene.Startup;
        }

        if (reason.Contains("session-", StringComparison.OrdinalIgnoreCase))
        {
            return ObservationUsageScene.Unlock;
        }

        if (PowerManager.RemainingChargePercent is int batteryPercent && batteryPercent <= 20)
        {
            return ObservationUsageScene.LowBattery;
        }

        if (isFullscreen && _isLikelyGame(foregroundProcessName, foregroundWindowTitle))
        {
            return ObservationUsageScene.Game;
        }

        if (isFullscreen)
        {
            return ObservationUsageScene.VideoPlayback;
        }

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
