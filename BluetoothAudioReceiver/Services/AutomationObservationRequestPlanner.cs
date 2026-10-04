using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using Windows.System.Power;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationObservationRequestPlanner
{
    private readonly IAppSettingsService _appSettingsService;
    private readonly Func<string?> _getForegroundProcessName;
    private readonly Func<string?> _getForegroundWindowTitle;
    private readonly Func<bool> _isForegroundFullscreen;
    private readonly Func<string?, string?, bool> _isLikelyGame;

    public AutomationObservationRequestPlanner(
        IAppSettingsService appSettingsService,
        Func<string?> getForegroundProcessName,
        Func<string?> getForegroundWindowTitle,
        Func<bool> isForegroundFullscreen,
        Func<string?, string?, bool> isLikelyGame)
    {
        _appSettingsService = appSettingsService;
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
        var moduleItems = BuildModuleRequestedInfoKinds();
        ObservationInfoKind[] sceneItems = scene switch
        {
            ObservationUsageScene.Startup or ObservationUsageScene.Unlock =>
            [
                ObservationInfoKind.PowerState,
                ObservationInfoKind.ForegroundWindow,
                ObservationInfoKind.BluetoothState,
                ObservationInfoKind.BluetoothRadioState,
                ObservationInfoKind.AudioRoute,
                ObservationInfoKind.ThemeMode,
                ObservationInfoKind.Brightness,
                ObservationInfoKind.NetworkState,
                ObservationInfoKind.ProxyState
            ],
            ObservationUsageScene.HeavyWork =>
            [
                ObservationInfoKind.PowerState,
                ObservationInfoKind.ForegroundWindow,
                ObservationInfoKind.BluetoothState,
                ObservationInfoKind.BluetoothRadioState,
                ObservationInfoKind.AudioRoute,
                ObservationInfoKind.ThemeMode,
                ObservationInfoKind.NetworkState,
                ObservationInfoKind.ProxyState
            ],
            ObservationUsageScene.VideoPlayback or ObservationUsageScene.Game =>
            [
                ObservationInfoKind.PowerState,
                ObservationInfoKind.ForegroundWindow,
                ObservationInfoKind.BluetoothState,
                ObservationInfoKind.BluetoothRadioState,
                ObservationInfoKind.AudioRoute
            ],
            ObservationUsageScene.LowBattery =>
            [
                ObservationInfoKind.PowerState,
                ObservationInfoKind.BluetoothState,
                ObservationInfoKind.BluetoothRadioState,
                ObservationInfoKind.NetworkState,
                ObservationInfoKind.ProxyState
            ],
            _ =>
            [
                ObservationInfoKind.PowerState,
                ObservationInfoKind.ForegroundWindow,
                ObservationInfoKind.BluetoothState,
                ObservationInfoKind.BluetoothRadioState,
                ObservationInfoKind.AudioRoute,
                ObservationInfoKind.ThemeMode,
                ObservationInfoKind.NetworkState,
                ObservationInfoKind.ProxyState
            ]
        };

        var items = sceneItems.Where(moduleItems.Contains).ToHashSet();
        if (reason.Contains("proxy", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(ObservationInfoKind.NetworkState);
            items.Add(ObservationInfoKind.ProxyState);
        }

        return new ObservationRequest(scene, reason, items.ToArray(), requestCorrelationId);
    }

    private IReadOnlyList<ObservationInfoKind> BuildModuleRequestedInfoKinds()
    {
        var items = new HashSet<ObservationInfoKind>
        {
            ObservationInfoKind.AudioRoute,
            ObservationInfoKind.ForegroundWindow,
            ObservationInfoKind.BluetoothState,
            ObservationInfoKind.BluetoothRadioState,
            ObservationInfoKind.PowerState
        };

        if (_appSettingsService.AutoBrightnessNotificationEnabled ||
            _appSettingsService.AutoBrightnessOnUnlockEnabled ||
            _appSettingsService.AutoBrightnessOnUnlockNotificationEnabled ||
            _appSettingsService.AutoBrightnessOnForegroundEnabled)
        {
            items.Add(ObservationInfoKind.Brightness);
        }

        if (_appSettingsService.AutoDayNightEnabled ||
            _appSettingsService.AutoDayNightNotificationEnabled ||
            _appSettingsService.DisableNightLightEnabled ||
            _appSettingsService.DisableNightLightAtNightEnabled)
        {
            items.Add(ObservationInfoKind.ThemeMode);
            items.Add(ObservationInfoKind.PowerState);
        }

        if (_appSettingsService.ProxyStatusNotificationEnabled)
        {
            items.Add(ObservationInfoKind.NetworkState);
            items.Add(ObservationInfoKind.ProxyState);
        }

        return items.ToArray();
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
