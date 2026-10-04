using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Models;
using Serilog;
using WinUIEx.Messaging;

namespace BluetoothAudioReceiver.Services
{
    public class AutoBrightnessService : IDisposable
    {
        private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutoBrightnessService));
        private const double NightLightMinColorTempKelvin = 4500;
        private const double NightLightMaxColorTempKelvin = 6500;
        private const double NeutralColorTempKelvin = 6500;
        private const int UnlockAutoBrightnessDelayMs = 500;
        private static readonly TimeSpan RequestLimitWindow = TimeSpan.FromSeconds(2);

        private readonly CameraReaderService _cameraReader;
        private readonly BrightnessController _controller;
        private readonly ScreenGammaColorTemperatureController _colorTemperatureController;
        private readonly NightLightStateService _nightLightStateService;
        private readonly BrightnessSettings _settings;
        private readonly INotificationService _notificationService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly IAutomationTriggerBus _triggerBus;
        private readonly object _requestGate = new();
        private IntPtr _windowHandle;
        private bool _disposed;
        private bool _requestPending;
        private bool _requestWorkerRunning;
        private DateTimeOffset _requestWindowStartedAt = DateTimeOffset.MinValue;
        private int _requestWindowAcceptedCount;
        private string _pendingSource = string.Empty;
        private DateTimeOffset _lastUnlockAt = DateTimeOffset.MinValue;
        private IntPtr _lastForegroundWindow;
        private DateTimeOffset _lastForegroundRequestAt = DateTimeOffset.MinValue;

        public AutoBrightnessService(
            CameraReaderService cameraReader,
            BrightnessController controller,
            ScreenGammaColorTemperatureController colorTemperatureController,
            NightLightStateService nightLightStateService,
            BrightnessSettings settings,
            INotificationService notificationService,
            IAppSettingsService appSettingsService,
            IAutomationTriggerBus triggerBus)
        {
            _cameraReader = cameraReader;
            _controller = controller;
            _colorTemperatureController = colorTemperatureController;
            _nightLightStateService = nightLightStateService;
            _settings = settings;
            _notificationService = notificationService;
            _appSettingsService = appSettingsService;
            _triggerBus = triggerBus;
            _triggerBus.TriggerPublished += OnTriggerPublished;
            _log.Information("Auto brightness subscribed to automation trigger bus");
        }

        public Task StartAsync()
        {
            _log.Information("Auto brightness service started in event-driven mode");
            return Task.CompletedTask;
        }

        public void InitializeForWindow(MainWindow window)
        {
            if (_windowHandle != IntPtr.Zero)
            {
                return;
            }

            _windowHandle = window.WindowHandle;
            _log.Information("Auto brightness window initialized; session unlock and foreground change come from the observation trigger bus");
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public void UpdateNightLightState()
        {
            try
            {
                var shouldDisable = _appSettingsService.DisableNightLightEnabled;

                // 如果启用了夜间模式关闭色温调节，检查系统夜灯是否激活
                if (shouldDisable && _appSettingsService.DisableNightLightAtNightEnabled)
                {
                    // 使用系统夜灯状态判断，而不是硬编码时间
                    if (!_nightLightStateService.IsNightLightActive())
                    {
                        shouldDisable = false;
                    }
                }

                if (shouldDisable)
                {
                    _nightLightStateService.DisableNightLight();
                    _log.Information("Night Light disabled by LumeKit settings");
                }
                else
                {
                    _nightLightStateService.RestoreNightLight();
                    _log.Information("Night Light restored by LumeKit settings");
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to update Night Light state");
            }
        }

        public void OnUserAction()
        {
            OnUserAction("user-action");
        }

        public void OnUserAction(string source)
        {
            _log.Debug("Auto brightness user activity trigger paused. Source={Source}", source);
        }

        public void OnScreenUnlocked()
        {
            _log.Information("Screen unlock received by auto brightness");
            _lastUnlockAt = DateTimeOffset.UtcNow;
            _ = Task.Run(TriggerScreenUnlockedAsync);
        }

        public void OnForegroundWindowChanged(IntPtr windowHandle)
        {
            if (!_appSettingsService.AutoBrightnessOnForegroundEnabled)
            {
                _log.Information("Foreground auto brightness skipped because scenario switch is disabled");
                return;
            }

            if (!_settings.AutoEnabled)
            {
                _log.Information("Foreground auto brightness skipped because master switch is disabled");
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (windowHandle == _lastForegroundWindow)
            {
                _log.Debug("Foreground auto brightness skipped because window did not change. WindowHandle={WindowHandle}", windowHandle);
                return;
            }

            _lastForegroundWindow = windowHandle;
            if (now - _lastUnlockAt < TimeSpan.FromSeconds(3))
            {
                _log.Information("Foreground auto brightness skipped because change followed screen unlock. ElapsedMs={ElapsedMs:F0}",
                    (now - _lastUnlockAt).TotalMilliseconds);
                return;
            }

            var cooldown = TimeSpan.FromMinutes(_appSettingsService.AutoBrightnessOnForegroundCooldownMinutes);
            if (cooldown > TimeSpan.Zero &&
                now - _lastForegroundRequestAt < cooldown)
            {
                _log.Information(
                    "Foreground auto brightness skipped by cooldown. CooldownMinutes={CooldownMinutes}, ElapsedMinutes={ElapsedMinutes:F1}",
                    _appSettingsService.AutoBrightnessOnForegroundCooldownMinutes,
                    (now - _lastForegroundRequestAt).TotalMinutes);
                return;
            }

            _lastForegroundRequestAt = now;
            _log.Information(
                "Foreground auto brightness accepted. WindowHandle={WindowHandle}, CooldownMinutes={CooldownMinutes}",
                windowHandle,
                _appSettingsService.AutoBrightnessOnForegroundCooldownMinutes);
            _ = ExecuteOneShotAsync("foreground-window");
        }

        public async Task UpdateBrightnessIfNeededAsync()
        {
            _log.Debug("Auto brightness manual refresh trigger paused");
            await Task.CompletedTask;
        }

        public Task ExecuteOneShotAsync(string source)
        {
            QueueOneShotRequest(source);
            return Task.CompletedTask;
        }

        private void QueueOneShotRequest(string source)
        {
            var shouldLogTiming = ShouldLogTiming(source);

            lock (_requestGate)
            {
                if (_disposed)
                {
                    if (shouldLogTiming)
                    {
                        _log.Information("Auto brightness timing: request ignored because service is disposed. Source={Source}", source);
                    }

                    return;
                }

                var now = DateTimeOffset.UtcNow;
                if (!_requestWorkerRunning || now - _requestWindowStartedAt >= RequestLimitWindow)
                {
                    _requestWindowStartedAt = now;
                    _requestWindowAcceptedCount = 0;
                }

                if (_requestWorkerRunning)
                {
                    if (_requestPending)
                    {
                        _pendingSource = source;
                        if (shouldLogTiming)
                        {
                            _log.Information(
                                "Auto brightness timing: pending request replaced. Source={Source}, AcceptedCount={AcceptedCount}",
                                source,
                                _requestWindowAcceptedCount);
                        }

                        _log.Debug(
                            "One-shot auto brightness pending request replaced. Source={Source}, AcceptedCount={AcceptedCount}",
                            source,
                            _requestWindowAcceptedCount);
                        return;
                    }

                    if (_requestWindowAcceptedCount >= 2)
                    {
                        if (shouldLogTiming)
                        {
                            _log.Information(
                                "Auto brightness timing: request ignored by 2-second limit. Source={Source}, AcceptedCount={AcceptedCount}",
                                source,
                                _requestWindowAcceptedCount);
                        }

                        _log.Debug(
                            "One-shot auto brightness request ignored by 2-second limit. Source={Source}, AcceptedCount={AcceptedCount}",
                            source,
                            _requestWindowAcceptedCount);
                        return;
                    }

                    _requestWindowAcceptedCount++;
                    _pendingSource = source;
                    _requestPending = true;
                    if (shouldLogTiming)
                    {
                        _log.Information(
                            "Auto brightness timing: request queued behind running work. Source={Source}, AcceptedCount={AcceptedCount}",
                            source,
                            _requestWindowAcceptedCount);
                    }

                    _log.Debug(
                        "One-shot auto brightness request queued. Source={Source}, AcceptedCount={AcceptedCount}",
                        source,
                        _requestWindowAcceptedCount);
                    return;
                }

                _requestWindowAcceptedCount = 1;
                _pendingSource = source;
                _requestPending = true;
                _requestWorkerRunning = true;
                if (shouldLogTiming)
                {
                    _log.Information("Auto brightness timing: request accepted and worker will start. Source={Source}", source);
                }
            }

            _ = ProcessQueuedRequestsAsync();
        }

        private async Task ProcessQueuedRequestsAsync()
        {
            while (true)
            {
                string source;
                lock (_requestGate)
                {
                    if (!_requestPending || _disposed)
                    {
                        _requestWorkerRunning = false;
                        return;
                    }

                    source = _pendingSource;
                    _pendingSource = string.Empty;
                    _requestPending = false;
                }

                if (ShouldLogTiming(source))
                {
                    _log.Information("Auto brightness timing: worker starts request. Source={Source}", source);
                }

                await ExecuteOneShotCoreAsync(source);
            }
        }

        private async Task ExecuteOneShotCoreAsync(string source)
        {
            var shouldLogTiming = ShouldLogTiming(source);
            var startedAt = Stopwatch.GetTimestamp();
            if (shouldLogTiming)
            {
                _log.Information("Auto brightness timing: execution entered. Source={Source}", source);
            }

            _log.Debug("One-shot auto brightness entered. Source={Source}", source);
            var previousAutoOpenAllowed = _cameraReader.AutoOpenAllowed;
            try
            {
                _cameraReader.SetAutoOpenAllowed(true, source);
                if (shouldLogTiming)
                {
                    _log.Information(
                        "Auto brightness timing: camera open allowed. Source={Source}, ElapsedMs={ElapsedMs:F0}",
                        source,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }

                _log.Debug("Camera sample started. Source={Source}, FrameCount={FrameCount}", source, 3);
                if (shouldLogTiming)
                {
                    _log.Information(
                        "Auto brightness timing: camera sample starts. Source={Source}, FrameCount={FrameCount}, ElapsedMs={ElapsedMs:F0}",
                        source,
                        3,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }

                var metrics = await _cameraReader.GetSampleMetricsAsync(frameCount: 3);
                if (shouldLogTiming)
                {
                    _log.Information(
                        "Auto brightness timing: camera sample completed. Source={Source}, Brightness={Brightness}, ElapsedMs={ElapsedMs:F0}",
                        source,
                        metrics.Brightness,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }

                _log.Debug(
                    "Camera sample completed. Source={Source}, Brightness={Brightness}",
                    source,
                    metrics.Brightness);
                var envBrightnessPercent = Math.Clamp(metrics.Brightness / 255.0 * 100.0, 0.0, 100.0);

                var minBrightness = _settings.MinBrightness;
                var maxBrightness = _settings.MaxBrightness;
                var (minColorTemp, maxColorTemp) = ResolveColorTemperatureRange();

                var targetBrightness = minBrightness + (envBrightnessPercent / 100.0) * (maxBrightness - minBrightness);
                targetBrightness = Math.Clamp(targetBrightness, 0, 100);

                var targetColorTemp = minColorTemp + (envBrightnessPercent / 100.0) * (maxColorTemp - minColorTemp);
                targetColorTemp = Math.Clamp(targetColorTemp, minColorTemp, maxColorTemp);

                _log.Debug(
                    "Brightness calculation completed. Source={Source}, TargetBrightness={TargetBrightness:F0}, TargetColorTemp={TargetColorTemp:F0}",
                    source,
                    targetBrightness,
                    targetColorTemp);
                _log.Debug("Brightness apply started. Source={Source}", source);
                await _controller.AdjustBrightnessAsync((byte)Math.Round(targetBrightness));
                await _colorTemperatureController.AdjustColorTemperatureAsync(targetColorTemp);
                if (shouldLogTiming)
                {
                    _log.Information(
                        "Auto brightness timing: brightness apply completed. Source={Source}, ElapsedMs={ElapsedMs:F0}",
                        source,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }

                _log.Debug("Brightness apply completed. Source={Source}", source);
                _log.Information(
                    "One-shot auto brightness executed from {Source}: env={EnvBrightnessPercent:F1}, targetBrightness={TargetBrightness:F0}, targetColorTemp={TargetColorTemp:F0}",
                    source,
                    envBrightnessPercent,
                    targetBrightness,
                    targetColorTemp);

                ShowSuccessNotification(source, targetBrightness, targetColorTemp);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "One-shot auto brightness failed from {Source}", source);
            }
            finally
            {
                try
                {
                    await _cameraReader.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to release camera after one-shot auto brightness from {Source}", source);
                }

                _cameraReader.SetAutoOpenAllowed(previousAutoOpenAllowed, $"{source}-restore");
                if (shouldLogTiming)
                {
                    _log.Information(
                        "Auto brightness timing: camera restored. Source={Source}, ElapsedMs={ElapsedMs:F0}",
                        source,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }
            }
        }

        public Task TriggerFromHotkeyAsync()
        {
            return ExecuteOneShotAsync("hotkey");
        }

        private async Task TriggerScreenUnlockedAsync()
        {
            if (!_appSettingsService.AutoBrightnessOnUnlockEnabled)
            {
                _log.Information("Screen unlock auto brightness skipped because unlock switch is disabled");
                return;
            }

            _log.Information("Screen unlock detected, scheduling one-shot auto brightness");

            try
            {
                // 解锁刚完成时，系统资源和摄像头管线可能还没完全稳定，稍等后再采样更稳。
                var startedAt = Stopwatch.GetTimestamp();
                _log.Information("Auto brightness timing: unlock delay starts. DelayMs={DelayMs}", UnlockAutoBrightnessDelayMs);
                _log.Debug("Screen unlock auto brightness scheduled. DelayMs={DelayMs}", UnlockAutoBrightnessDelayMs);
                await Task.Delay(UnlockAutoBrightnessDelayMs).ConfigureAwait(false);
                _log.Information(
                    "Auto brightness timing: unlock delay completed. ElapsedMs={ElapsedMs:F0}",
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                _log.Debug("Screen unlock auto brightness delay completed");
                await ExecuteOneShotAsync("screen-unlock").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Screen unlock auto brightness failed");
            }
        }

        public async Task ReleaseCameraAsync(string source)
        {
            try
            {
                await _cameraReader.ShutdownAsync();
                _log.Information("Camera released from {Source}", source);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to release camera from {Source}", source);
            }
        }

        public async Task SetEnabledAsync(bool enabled, string source)
        {
            await _settings.SetAutoEnabledAsync(enabled, source);
            _cameraReader.SetAutoOpenAllowed(enabled, source);
            _log.Information("Auto brightness enabled state changed from {Source}: {Enabled}", source, enabled);
        }

        private Task TriggerUpdateAsync(string source, bool forceUpdate)
        {
            if (!_settings.AutoEnabled)
            {
                _log.Debug("Auto brightness skipped from {Source}: disabled", source);
                return Task.CompletedTask;
            }

            QueueOneShotRequest(source);
            return Task.CompletedTask;
        }

        private void ShowSuccessNotification(string source, double targetBrightness, double targetColorTemp)
        {
            if (string.Equals(source, "hotkey", StringComparison.OrdinalIgnoreCase))
            {
                if (!_appSettingsService.AutoBrightnessNotificationEnabled)
                {
                    return;
                }
            }
            else if (string.Equals(source, "screen-unlock", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(source, "foreground-window", StringComparison.OrdinalIgnoreCase))
            {
                if (!_appSettingsService.AutoBrightnessNotificationEnabled ||
                    (string.Equals(source, "screen-unlock", StringComparison.OrdinalIgnoreCase) &&
                     !_appSettingsService.AutoBrightnessOnUnlockNotificationEnabled) ||
                    (string.Equals(source, "foreground-window", StringComparison.OrdinalIgnoreCase) &&
                     !_appSettingsService.AutoBrightnessOnForegroundNotificationEnabled))
                {
                    return;
                }
            }
            else
            {
                return;
            }

            // 主标题固定，状态栏显示实际亮度百分比和色温。
            var brightnessText = $"{Math.Clamp((int)Math.Round(targetBrightness), 0, 100)}% | {Math.Round(targetColorTemp):0}K";
            _notificationService.Show("\uE706", "屏幕亮度已调整", brightnessText);
        }

        private static bool ShouldLogTiming(string source)
        {
            return string.Equals(source, "screen-unlock", StringComparison.OrdinalIgnoreCase);
        }

        private (double MinColorTemp, double MaxColorTemp) ResolveColorTemperatureRange()
        {
            var minColorTemp = _settings.MinColorTemp;
            var maxColorTemp = _settings.MaxColorTemp;

            if (minColorTemp > maxColorTemp)
            {
                (minColorTemp, maxColorTemp) = (maxColorTemp, minColorTemp);
            }

            // 如果启用了“夜间模式关闭色温调节”且夜灯激活，返回中性色温，不调节。
            if (_appSettingsService.DisableNightLightAtNightEnabled && _nightLightStateService.IsNightLightActive())
            {
                return (NeutralColorTempKelvin, NeutralColorTempKelvin);
            }

            if (!_nightLightStateService.IsNightLightActive())
            {
                return (minColorTemp, maxColorTemp);
            }

            var narrowedMin = Math.Max(minColorTemp, NightLightMinColorTempKelvin);
            var narrowedMax = Math.Min(maxColorTemp, NightLightMaxColorTempKelvin);
            if (narrowedMin <= narrowedMax)
            {
                return (narrowedMin, narrowedMax);
            }

            var midpoint = Math.Clamp((minColorTemp + maxColorTemp) / 2.0, NightLightMinColorTempKelvin, NightLightMaxColorTempKelvin);
            return (midpoint, midpoint);
        }

        private void OnTriggerPublished(object? sender, TriggerEvent triggerEvent)
        {
            if (_disposed)
            {
                _log.Debug("Auto brightness ignored trigger because service is disposed");
                return;
            }

            if (!string.Equals(triggerEvent.TriggerType, AutomationSupportCatalog.TriggerSystemSignal, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!triggerEvent.Payload.TryGetValue("reason", out var reason) || string.IsNullOrWhiteSpace(reason))
            {
                _log.Debug("Auto brightness ignored system signal because reason is missing");
                return;
            }

            if (string.Equals(reason, "session-SessionUnlock", StringComparison.OrdinalIgnoreCase))
            {
                _log.Information(
                    "Auto brightness accepted session unlock. CorrelationId={CorrelationId}",
                    triggerEvent.CorrelationId);
                OnScreenUnlocked();
                return;
            }

            if (string.Equals(reason, "foreground-window-changed", StringComparison.OrdinalIgnoreCase))
            {
                if (!triggerEvent.Payload.TryGetValue("windowHandle", out var handleText) ||
                    !long.TryParse(handleText, out var handleValue))
                {
                    _log.Debug(
                        "Auto brightness ignored foreground change because window handle is invalid. WindowHandle={WindowHandle}",
                        handleText);
                    return;
                }

                _log.Information(
                    "Auto brightness accepted foreground window change from observation signal. WindowHandle={WindowHandle}, CorrelationId={CorrelationId}",
                    handleValue,
                    triggerEvent.CorrelationId);
                OnForegroundWindowChanged(new IntPtr(handleValue));
                return;
            }

            _log.Debug("Auto brightness ignored system signal because reason does not match unlock or foreground change. Reason={Reason}", reason);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _triggerBus.TriggerPublished -= OnTriggerPublished;
            _log.Information("Auto brightness unsubscribed from automation trigger bus");
        }

    }
}
