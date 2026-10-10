using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Serilog;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Windows.System;
using WinUIEx.Messaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using BluetoothAudioReceiver.Core.Helpers;
using BluetoothAudioReceiver.Models.Application;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.ViewModels.Pages;

namespace BluetoothAudioReceiver.Services;

internal sealed class AudioRouteHotkeyService : IAudioRouteHotkeyService, IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AudioRouteHotkeyService));
    private static readonly string IndependentSwitchLogPath = Path.Combine(
        Path.GetTempPath(),
        "LumeKit-audio-switch.log");

    private const int InputHotkeyId = 0x2001;
    private const int OutputHotkeyId = 0x2002;
    private const int AutoBrightnessHotkeyId = 0x2003;
    private const int UserHotkeyIdBase = 0x4000;
    private const int NotificationHotkeyIdBase = 0x3000;
    private const int WmHotkey = 0x0312;

    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly INotificationService _notificationService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly AutoBrightnessService _autoBrightnessService;
    private readonly AudioOutputDeviceWatcherService _audioOutputDeviceWatcherService;

    private readonly ConcurrentDictionary<int, (HOT_KEY_MODIFIERS Modifiers, uint Key)> _registeredHotkeys = [];
    private readonly ConcurrentDictionary<int, string> _registeredHotkeyTexts = [];
    private readonly ConcurrentDictionary<int, HotkeyActionKind> _registeredHotkeyKinds = [];
    private readonly ConcurrentDictionary<int, NotificationHotkeyConfig> _notificationHotkeyConfigs = [];
    private readonly ConcurrentDictionary<int, UserHotkeyBinding> _userHotkeyBindings = [];
    private readonly ConcurrentDictionary<int, (HOT_KEY_MODIFIERS Modifiers, uint Key)> _fallbackHotkeys = [];
    private readonly ConcurrentDictionary<int, byte> _fallbackActiveKeys = [];
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _hotkeyActionLocks = [];
    private readonly SemaphoreSlim _restoreOutputRouteLock = new(1, 1);
    private readonly SemaphoreSlim _userHotkeyRefreshLock = new(1, 1);
    private readonly object _outputDeviceListCacheGate = new();
    private IReadOnlyList<AudioOutputDeviceItem>? _cachedOutputDevices;
    private DateTimeOffset _cachedOutputDevicesAt = DateTimeOffset.MinValue;
    private string _notificationHotkeyRegistrationSignature = string.Empty;
    private volatile string[] _inputTargetCandidates = [];
    private volatile string[] _outputTargetCandidates = [];
    private volatile bool _inputTargetCandidatesFromManualSelection;
    private volatile bool _outputTargetCandidatesFromManualSelection;
    private volatile string? _inputCycleAnchorCandidateId;
    private volatile bool _hotkeysTemporarilyDisabledForCapture;
    private volatile bool _notificationHotkeysRegistered;
    private volatile bool _suspendHotkeyActions;
    private CancellationTokenSource? _resumeHotkeyCts;
    private CancellationTokenSource? _hotkeyCapturePollingCts;
    private WindowMessageMonitor? _messageMonitor;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private IntPtr _windowHandle;
    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;
    private LowLevelKeyboardProc? _keyboardHookProc;
    private LowLevelMouseProc? _mouseHookProc;
    private string? _lastCaptureSignature;
    private DateTime _lastUserActivityTriggerAt = DateTime.MinValue;

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const string InputHotkeyBindingsKey = "InputHotkeyBindingsV2";
    private const string OutputHotkeyBindingsKey = "OutputHotkeyBindingsV2";
    private const string NotificationHotkeyBindingsKey = "HotkeyNotification.CardsV1";
    private const string BluetoothConnectedDeviceIdsKey = "BluetoothConnectedDeviceIds";
    private static readonly TimeSpan OutputDeviceListCacheDuration = TimeSpan.FromSeconds(2);

    public event Action<string>? DebugMessage;
    public event Action<VirtualKey, VirtualKeyModifiers>? HotkeyCaptureKeyDown;

    public AudioRouteHotkeyService(
        IAudioPlaybackConnectionService connectionService,
        IAppSettingsService appSettingsService,
        INotificationService notificationService,
        ILocalSettingsService localSettingsService,
        AutoBrightnessService autoBrightnessService,
        AudioOutputDeviceWatcherService audioOutputDeviceWatcherService)
    {
        _connectionService = connectionService;
        _appSettingsService = appSettingsService;
        _notificationService = notificationService;
        _localSettingsService = localSettingsService;
        _autoBrightnessService = autoBrightnessService;
        _audioOutputDeviceWatcherService = audioOutputDeviceWatcherService;

        _connectionService.DeviceConnected += OnPlaybackDeviceConnected;
        _audioOutputDeviceWatcherService.DevicesChanged += OnAudioOutputDevicesChanged;
    }

    public void InitializeForWindow(MainWindow window)
    {
        if (_messageMonitor != null)
        {
            return;
        }

        _windowHandle = window.WindowHandle;
        _dispatcherQueue = window.DispatcherQueue;
        _messageMonitor = new WindowMessageMonitor(window);
        _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;
        EnsureKeyboardHook();
        RaiseDebug("热键服务已初始化");
        _ = RefreshUserHotkeysAsync();
        UpdateAutoBrightnessHotkey(_appSettingsService.AutoBrightnessHotkey);
        RefreshNotificationHotkeys();
    }

    public void UpdateHotkeys(string inputHotkey, string outputHotkey)
    {
        _ = RefreshUserHotkeysAsync();
    }

    public void UpdateInputHotkey(string inputHotkey)
    {
        _ = RefreshUserHotkeysAsync();
    }

    public void UpdateOutputHotkey(string outputHotkey)
    {
        _ = RefreshUserHotkeysAsync();
    }

    public void RefreshUserHotkeys()
    {
        _ = RefreshUserHotkeysAsync();
    }

    private async Task RefreshUserHotkeysAsync()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        await _userHotkeyRefreshLock.WaitAsync();
        try
        {
            foreach (var hotkeyId in _userHotkeyBindings.Keys.ToArray())
            {
                UnregisterHotkey(hotkeyId);
                _userHotkeyBindings.TryRemove(hotkeyId, out _);
            }

            var inputConfigs = await _localSettingsService.ReadSettingAsync(
                InputHotkeyBindingsKey,
                new List<HotkeyBindingConfig>()) ?? [];
            var outputConfigs = await _localSettingsService.ReadSettingAsync(
                OutputHotkeyBindingsKey,
                new List<HotkeyBindingConfig>()) ?? [];

            var nextId = UserHotkeyIdBase;
            foreach (var config in inputConfigs)
            {
                RegisterUserHotkey(config, isInput: true, ref nextId);
            }

            foreach (var config in outputConfigs)
            {
                RegisterUserHotkey(config, isInput: false, ref nextId);
            }

            RaiseDebug($"[用户快捷键] 刷新完成：输入配置={inputConfigs.Count}, 输出配置={outputConfigs.Count}, 已注册={_userHotkeyBindings.Count}");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Refresh user hotkeys failed");
            RaiseDebug($"[用户快捷键] 刷新异常：{ex.Message}");
        }
        finally
        {
            _userHotkeyRefreshLock.Release();
        }
    }

    private void RegisterUserHotkey(HotkeyBindingConfig config, bool isInput, ref int nextId)
    {
        if (!config.IsEnabled || string.IsNullOrWhiteSpace(config.Hotkey))
        {
            return;
        }

        var hotkeyId = nextId++;
        var binding = new UserHotkeyBinding
        {
            IsInput = isInput,
            Hotkey = config.Hotkey,
            DeviceIds = NormalizeDeviceIdList(config.DeviceIds)
        };
        RegisterOrUpdateHotkey(
            hotkeyId,
            binding.Hotkey,
            isInput ? HotkeyActionKind.InputRoute : HotkeyActionKind.OutputRoute);
        if (_registeredHotkeys.ContainsKey(hotkeyId) || _fallbackHotkeys.ContainsKey(hotkeyId))
        {
            _userHotkeyBindings[hotkeyId] = binding;
        }
        RaiseDebug($"[用户快捷键] 注册：id={hotkeyId}, type={(isInput ? "输入" : "输出")}, hotkey={binding.Hotkey}, deviceCount={binding.DeviceIds.Length}");
    }

    public void UpdateAutoBrightnessHotkey(string hotkey)
    {
        RegisterOrUpdateHotkey(AutoBrightnessHotkeyId, hotkey, HotkeyActionKind.AutoBrightness);
    }

    public void RefreshNotificationHotkeys()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var configs = LoadNotificationHotkeyConfigs();
        var signature = BuildNotificationHotkeySignature(configs);
        if (_notificationHotkeysRegistered &&
            string.Equals(signature, _notificationHotkeyRegistrationSignature, StringComparison.Ordinal))
        {
            return;
        }

        UnregisterNotificationHotkeys();

        for (var i = 0; i < configs.Count; i++)
        {
            var config = configs[i];
            var hotkeyId = NotificationHotkeyIdBase + i;
            RegisterOrUpdateHotkey(
                hotkeyId,
                config.Hotkey,
                HotkeyActionKind.Notification,
                config.StatusText);
            if (!string.IsNullOrWhiteSpace(config.Hotkey))
            {
                _notificationHotkeyConfigs[hotkeyId] = config;
            }
        }

        _notificationHotkeysRegistered = true;
        _notificationHotkeyRegistrationSignature = signature;
        RaiseDebug($"已刷新按键通知注册：{_notificationHotkeyConfigs.Count} 项");
    }

    public void SetHotkeyCaptureSuspended(bool suspended)
    {
        _suspendHotkeyActions = suspended;
        if (suspended)
        {
            TemporarilyDisableGlobalHotkeysForCapture();
            StartHotkeyCapturePolling();
            _resumeHotkeyCts?.Cancel();
            _resumeHotkeyCts = new CancellationTokenSource();
            var token = _resumeHotkeyCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), token);
                    if (!token.IsCancellationRequested)
                    {
                        StopHotkeyCapturePolling();
                        _suspendHotkeyActions = false;
                        RestoreGlobalHotkeysAfterCapture();
                        RaiseDebug("录制暂停超时，已自动恢复全局热键触发");
                    }
                }
                catch (TaskCanceledException)
                {
                    // ignore
                }
            }, token).Forget(nameof(AudioRouteHotkeyService));
        }
        else
        {
            _resumeHotkeyCts?.Cancel();
            StopHotkeyCapturePolling();
            RestoreGlobalHotkeysAfterCapture();
        }
        RaiseDebug(suspended ? "已暂停全局热键触发（录制模式）" : "已恢复全局热键触发");
    }

    private void StartHotkeyCapturePolling()
    {
        if (_hotkeyCapturePollingCts != null)
        {
            return;
        }

        _lastCaptureSignature = null;
        _hotkeyCapturePollingCts = new CancellationTokenSource();
        var token = _hotkeyCapturePollingCts.Token;
        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_suspendHotkeyActions && TryGetPressedCaptureHotkey(out var key, out var modifiers))
                    {
                        RaiseHotkeyCaptureKeyDown(key, modifiers);
                    }

                    await Task.Delay(25, token);
                }
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }, token).Forget(nameof(AudioRouteHotkeyService));
    }

    private void StopHotkeyCapturePolling()
    {
        _lastCaptureSignature = null;
        _hotkeyCapturePollingCts?.Cancel();
        _hotkeyCapturePollingCts?.Dispose();
        _hotkeyCapturePollingCts = null;
    }

    private void TemporarilyDisableGlobalHotkeysForCapture()
    {
        if (_windowHandle == IntPtr.Zero || _hotkeysTemporarilyDisabledForCapture)
        {
            return;
        }

        UnregisterAllRegisteredHotkeys();
        _registeredHotkeys.Clear();
        _registeredHotkeyTexts.Clear();
        _registeredHotkeyKinds.Clear();
        _fallbackHotkeys.Clear();
        _fallbackActiveKeys.Clear();
        _notificationHotkeyConfigs.Clear();
        _hotkeysTemporarilyDisabledForCapture = true;
        _notificationHotkeysRegistered = false;
        RaiseDebug("录制模式：已临时注销全局热键");
    }

    private void RestoreGlobalHotkeysAfterCapture()
    {
        if (!_hotkeysTemporarilyDisabledForCapture)
        {
            return;
        }

        _hotkeysTemporarilyDisabledForCapture = false;
        UpdateHotkeys(_appSettingsService.InputHotkey, _appSettingsService.OutputHotkey);
        UpdateAutoBrightnessHotkey(_appSettingsService.AutoBrightnessHotkey);
        RefreshNotificationHotkeys();
        RaiseDebug("录制模式：已恢复全局热键注册");
    }

    public async Task UpdateTargetsAsync(string? inputDeviceId, string? outputDeviceId)
    {
        await _appSettingsService.SetPreferredInputDeviceIdAsync(inputDeviceId ?? string.Empty);
        await _appSettingsService.SetPreferredOutputDeviceIdAsync(outputDeviceId ?? string.Empty);
    }

    public void UpdateTargetCandidates(IReadOnlyList<string>? inputDeviceIds, IReadOnlyList<string>? outputDeviceIds)
    {
        _inputTargetCandidates = NormalizeDeviceIdList(inputDeviceIds);
        _outputTargetCandidates = NormalizeDeviceIdList(outputDeviceIds);
        _inputTargetCandidatesFromManualSelection = inputDeviceIds is { Count: > 0 };
        _outputTargetCandidatesFromManualSelection = outputDeviceIds is { Count: > 0 };
    }

    public async Task<IReadOnlyList<AudioOutputDeviceItem>> GetOutputDevicesAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            lock (_outputDeviceListCacheGate)
            {
                if (_cachedOutputDevices is not null && now - _cachedOutputDevicesAt < OutputDeviceListCacheDuration)
                {
                    return _cachedOutputDevices;
                }
            }

            var list = EnumerateCoreAudioRenderEndpoints();
            if (list.Count == 0)
            {
                var selector = MediaDevice.GetAudioRenderSelector();
                var devices = await DeviceInformation.FindAllAsync(selector);
                foreach (var device in devices)
                {
                    if (string.IsNullOrWhiteSpace(device.Id))
                    {
                        continue;
                    }

                    list.Add(new AudioOutputDeviceItem
                    {
                        Id = device.Id,
                        EndpointId = DeviceIdentityHelper.NormalizeOutputEndpointId(device.Id),
                        PhysicalId = DeviceIdentityHelper.GetPhysicalDeviceId(device),
                        Name = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name
                    });
                }
            }

            var snapshot = list.AsReadOnly();
            lock (_outputDeviceListCacheGate)
            {
                _cachedOutputDevices = snapshot;
                _cachedOutputDevicesAt = now;
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to enumerate output audio devices");
            return [];
        }
    }

    public Task<string?> GetCurrentDefaultOutputEndpointIdAsync()
        => Task.FromResult(GetCurrentDefaultOutputEndpointId());

    public async Task<bool> SetOutputDeviceAsync(string deviceId, bool suppressNotification = false)
    {
        try
        {
            var targetId = NormalizeConfiguredOutputDeviceId(deviceId);
            if (string.IsNullOrWhiteSpace(targetId))
            {
                RaiseDebug("输出切换失败：未指定目标设备");
                return false;
            }

            InvalidateOutputDeviceCache();
            var outputDevices = await GetOutputDevicesAsync();
            var target = outputDevices.FirstOrDefault(d =>
                string.Equals(d.Id, targetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.EndpointId, targetId, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                RaiseDebug($"输出切换失败：未找到目标设备 {targetId}");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE946", "已选输出设备不可用");
                }

                return false;
            }

            var endpointId = string.IsNullOrWhiteSpace(target.EndpointId) ? target.Id : target.EndpointId;
            if (string.IsNullOrWhiteSpace(endpointId))
            {
                RaiseDebug($"输出切换失败：目标设备端点无效 {target.Name}");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE783", "输出切换失败");
                }

                return false;
            }

            var currentBefore = GetCurrentDefaultOutputEndpointId() ?? string.Empty;
            RaiseDebug($"输出切换准备：target={target.Name}, savedId={targetId}, endpointId={endpointId}, current={currentBefore}");
            var ok = SetDefaultAudioEndpoint(endpointId);
            if (!ok)
            {
                RaiseDebug($"输出切换失败：SetDefaultEndpoint 返回失败 {target.Name}");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE783", "输出切换失败");
                }

                return false;
            }

            var currentAfter = GetCurrentDefaultOutputEndpointId() ?? string.Empty;
            if (!string.Equals(currentAfter, endpointId, StringComparison.OrdinalIgnoreCase))
            {
                RaiseDebug($"输出切换失败：系统默认输出未切到目标。target={endpointId}, current={currentAfter}");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE783", "输出切换失败");
                }

                return false;
            }

            await _appSettingsService.SetPreferredOutputDeviceIdAsync(target.Id);
            await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, target.Id);

            var name = string.IsNullOrWhiteSpace(target.Name) ? "输出设备" : target.Name;
            RaiseDebug($"输出切换成功：{name}");
            if (ShouldShowAudioSwitchNotification(suppressNotification))
            {
                _notificationService.Show(DeviceVisualHelper.ResolveGlyph(name), name, "音频已连接");
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SetOutputDeviceAsync failed");
            RaiseDebug($"输出切换异常：{ex.Message}");
            if (ShouldShowAudioSwitchNotification(suppressNotification))
            {
                _notificationService.Show("\uE783", "输出切换失败");
            }

            return false;
        }
    }

    public async Task<bool> TriggerInputRouteAsync(bool suppressNotification = false)
    {
        try
        {
            await EnsureTargetCandidatesLoadedFromBindingsAsync(isInput: true, forceReload: !_inputTargetCandidatesFromManualSelection);

            var allInputCandidates = await LoadInputRouteCandidatesAsync();
            var candidateInputIds = _inputTargetCandidates;
            var inputCandidates = candidateInputIds.Length == 0
                ? allInputCandidates
                : FilterInputRouteCandidates(allInputCandidates, candidateInputIds);

            var availableInputCandidates = inputCandidates
                .Where(candidate => candidate.IsAvailable)
                .ToList();

            if (availableInputCandidates.Count == 0)
            {
                RaiseDebug(inputCandidates.Count == 0
                    ? "输入切换失败：未发现输入设备"
                    : "输入切换失败：已选输入设备不可用");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE946", inputCandidates.Count == 0 ? "未发现输入蓝牙设备" : "已选输入设备不可用");
                }
                return false;
            }

            var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
            var currentIndex = availableInputCandidates.FindIndex(candidate =>
                string.Equals(NormalizeInputDeviceId(candidate.Id), NormalizeInputDeviceId(preferredInputId), StringComparison.OrdinalIgnoreCase));

            if (currentIndex < 0)
            {
                var connectedId = _connectionService.ConnectedDevices
                    .Select(d => d.Id)
                    .FirstOrDefault(id => availableInputCandidates.Any(candidate =>
                        string.Equals(NormalizeInputDeviceId(candidate.Id), NormalizeInputDeviceId(id), StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrWhiteSpace(connectedId))
                {
                    currentIndex = availableInputCandidates.FindIndex(candidate =>
                        string.Equals(NormalizeInputDeviceId(candidate.Id), NormalizeInputDeviceId(connectedId), StringComparison.OrdinalIgnoreCase));
                }
            }

            var currentCandidate = currentIndex >= 0 ? availableInputCandidates[currentIndex] : null;
            if (currentCandidate is not null && string.IsNullOrWhiteSpace(_inputCycleAnchorCandidateId))
            {
                _inputCycleAnchorCandidateId = currentCandidate.Id;
            }

            var nextIndex = ResolveNextInputCandidateIndex(availableInputCandidates, currentIndex);
            var nextCandidate = availableInputCandidates[nextIndex];

            var nextDevice = nextCandidate.Device!;

            var alreadyConnected = _connectionService.ConnectedDevices.Any(d =>
                string.Equals(
                    DeviceIdentityHelper.GetSelectionId(d, isInput: true),
                    DeviceIdentityHelper.GetSelectionId(nextDevice, isInput: true),
                    StringComparison.OrdinalIgnoreCase));
            var ok = alreadyConnected || await _connectionService.ConnectAsync(nextDevice);
            if (!ok)
            {
                RaiseDebug("输入切换失败：目标设备连接失败");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE783", "输入切换失败");
                }

                return false;
            }

            await _appSettingsService.SetPreferredInputDeviceIdAsync(nextCandidate.Id);
            await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, GetSelectionPhysicalDeviceId(nextCandidate.Id));
            RaiseDebug($"输入切换成功：{nextCandidate.Name}");
            if (ShouldShowAudioSwitchNotification(suppressNotification))
            {
                _notificationService.Show(DeviceVisualHelper.ResolveGlyph(nextCandidate.Name), nextCandidate.Name, "音频已连接");
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TriggerInputRouteAsync failed");
            RaiseDebug($"输入切换异常：{ex.Message}");
            if (ShouldShowAudioSwitchNotification(suppressNotification))
            {
                _notificationService.Show("\uE783", "输入切换失败");
            }

            return false;
        }
        finally
        {
            _inputTargetCandidatesFromManualSelection = false;
        }
    }

    public async Task<bool> TriggerOutputRouteAsync(bool suppressNotification = false)
    {
        try
        {
            RaiseDebug($"[音频切换] 开始：手动候选={_outputTargetCandidatesFromManualSelection}, 候选ID数量={_outputTargetCandidates.Length}");
            await EnsureTargetCandidatesLoadedFromBindingsAsync(isInput: false, forceReload: !_outputTargetCandidatesFromManualSelection);
            RaiseDebug($"[音频切换] 候选绑定加载完成：候选ID数量={_outputTargetCandidates.Length}, 手动候选={_outputTargetCandidatesFromManualSelection}");

            var allOutputDevices = await GetOutputDevicesAsync();
            var candidateOutputIds = _outputTargetCandidates;
            RaiseDebug($"[音频切换] 枚举输出设备：总数={allOutputDevices.Count}, 筛选ID数量={candidateOutputIds.Length}");
            foreach (var device in allOutputDevices)
            {
                RaiseDebug($"[音频切换] 输出设备：name={device.Name}, id={device.Id}, endpoint={device.EndpointId}, physical={device.PhysicalId}");
            }

            var outputDevices = candidateOutputIds.Length == 0
                ? allOutputDevices
                : allOutputDevices
                    .Where(d => candidateOutputIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase) ||
                                candidateOutputIds.Contains(d.EndpointId, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            RaiseDebug($"[音频切换] 候选设备筛选完成：数量={outputDevices.Count}");

            if (outputDevices.Count == 0 && candidateOutputIds.Length > 0)
            {
                RaiseDebug("输出切换检测到候选设备可能已过期，正在重新枚举后重试");
                InvalidateOutputDeviceCache();
                allOutputDevices = await GetOutputDevicesAsync();
                outputDevices = allOutputDevices
                    .Where(d => candidateOutputIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase) ||
                                candidateOutputIds.Contains(d.EndpointId, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                RaiseDebug($"[音频切换] 重新枚举筛选完成：总数={allOutputDevices.Count}, 候选数量={outputDevices.Count}");
            }

            if (outputDevices.Count == 0)
            {
                RaiseDebug(candidateOutputIds.Length == 0
                    ? "输出切换失败：未发现输出设备"
                    : "输出切换失败：已选输出设备不可用");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE946", candidateOutputIds.Length == 0 ? "未发现输出音频设备" : "已选输出设备不可用");
                }
                return false;
            }

            var candidates = outputDevices
                .Select(d => new
                {
                    Device = d,
                    EndpointId = string.IsNullOrWhiteSpace(d.EndpointId) ? d.Id : d.EndpointId
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.EndpointId))
                .GroupBy(x => x.Device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (candidates.Count == 0)
            {
                RaiseDebug("输出切换失败：输出端点ID无效");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE783", "输出切换失败");
                }
                return false;
            }

            var currentDefaultId = GetCurrentDefaultOutputEndpointId() ?? string.Empty;
            var currentIndex = candidates.FindIndex(c =>
                string.Equals(c.EndpointId, currentDefaultId, StringComparison.OrdinalIgnoreCase));

            var nextIndex = currentIndex >= 0
                ? (currentIndex + 1) % candidates.Count
                : 0;

            var next = candidates[nextIndex];
            var targetEndpointId = next.EndpointId;
            RaiseDebug($"[音频切换] 轮换计算：可用候选={candidates.Count}, 当前索引={currentIndex}, 目标索引={nextIndex}");
            foreach (var candidate in candidates)
            {
                RaiseDebug($"[音频切换] 有效候选：name={candidate.Device.Name}, id={candidate.Device.Id}, endpoint={candidate.EndpointId}");
            }
            RaiseDebug($"[音频切换] 准备设置：target={next.Device.Name}, targetId={next.Device.Id}, endpointId={targetEndpointId}, current={currentDefaultId}");
            var ok = SetDefaultAudioEndpoint(targetEndpointId);
            var currentAfter = GetCurrentDefaultOutputEndpointId() ?? string.Empty;
            RaiseDebug($"[音频切换] 设置返回：setOk={ok}, currentAfter={currentAfter}, target={targetEndpointId}, match={string.Equals(currentAfter, targetEndpointId, StringComparison.OrdinalIgnoreCase)}");
            ok = ok && string.Equals(currentAfter, targetEndpointId, StringComparison.OrdinalIgnoreCase);

            if (ok)
            {
                await _appSettingsService.SetPreferredOutputDeviceIdAsync(next.Device.Id);
                await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, next.Device.Id);
                var name = string.IsNullOrWhiteSpace(next.Device.Name) ? "输出设备" : next.Device.Name;
                RaiseDebug($"[音频切换] 成功：{name}");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show(DeviceVisualHelper.ResolveGlyph(name), name, "音频已连接");
                }
            }
            else
            {
                RaiseDebug($"[音频切换] 失败：target={targetEndpointId}, current={currentAfter}, setOk={ok}");
                if (ShouldShowAudioSwitchNotification(suppressNotification))
                {
                    _notificationService.Show("\uE783", "输出切换失败");
                }
            }

            return ok;
        }
        finally
        {
            _outputTargetCandidatesFromManualSelection = false;
        }
    }

    public Task<bool> TriggerCustomHotkeyAsync(string hotkeyText)
    {
        if (!TryParseHotkey(hotkeyText, out var modifiers, out var virtualKey))
        {
            RaiseDebug($"自定义快捷键无效：{hotkeyText}");
            return Task.FromResult(false);
        }

        try
        {
            SendCustomHotkey(modifiers, virtualKey);
            RaiseDebug($"已执行自定义快捷键：{hotkeyText}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TriggerCustomHotkeyAsync failed. Hotkey={Hotkey}", hotkeyText);
            RaiseDebug($"自定义快捷键执行失败：{hotkeyText}");
            return Task.FromResult(false);
        }
    }

    private bool ShouldShowAudioSwitchNotification(bool suppressNotification)
    {
        if (suppressNotification)
        {
            return false;
        }

        if (_appSettingsService.AudioSwitchNotificationEnabled)
        {
            return true;
        }

        RaiseDebug("音频切换通知已关闭，跳过悬浮通知");
        return false;
    }

    private static string NormalizeConfiguredOutputDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        return deviceId.Trim();
    }

    private static string ResolveDeviceTypeGlyph(string deviceName)
    {
        var normalizedName = (deviceName ?? string.Empty).ToLowerInvariant();

        if (normalizedName.Contains("headphone")
            || normalizedName.Contains("headset")
            || normalizedName.Contains("earbud")
            || normalizedName.Contains("earbuds")
            || normalizedName.Contains("buds")
            || normalizedName.Contains("airpods")
            || normalizedName.Contains("耳机"))
        {
            return "\uE7F6"; // Headphone
        }

        if (normalizedName.Contains("microphone") || normalizedName.Contains("mic") || normalizedName.Contains("麦克风"))
        {
            return "\uE720"; // Microphone
        }

        if (normalizedName.Contains("speaker") || normalizedName.Contains("soundbar") || normalizedName.Contains("音箱") || normalizedName.Contains("扬声器"))
        {
            return "\uE7F5"; // Speaker
        }

        return "\uE7F5"; // Speaker (default fallback)
    }

    private async Task EnsureTargetCandidatesLoadedFromBindingsAsync(bool isInput, bool forceReload = false)
    {
        var currentCandidates = isInput ? _inputTargetCandidates : _outputTargetCandidates;
        if (currentCandidates.Length > 0 && !forceReload)
        {
            return;
        }

        try
        {
            var key = isInput ? InputHotkeyBindingsKey : OutputHotkeyBindingsKey;
            var configs = await _localSettingsService.ReadSettingAsync(key, new List<HotkeyBindingConfig>()) ?? [];
            if (configs.Count == 0)
            {
                return;
            }

            var expectedHotkey = NormalizeHotkey(isInput ? _appSettingsService.InputHotkey : _appSettingsService.OutputHotkey);
            var matched = configs.FirstOrDefault(c =>
                string.Equals(NormalizeHotkey(c.Hotkey), expectedHotkey, StringComparison.OrdinalIgnoreCase));

            var sourceIds = matched is { DeviceIds.Count: > 0 }
                ? matched.DeviceIds
                : configs.SelectMany(c => c.DeviceIds).ToList();

            var normalizedIds = NormalizeDeviceIdList(sourceIds);
            if (normalizedIds.Length == 0)
            {
                return;
            }

            if (isInput)
            {
                _inputTargetCandidates = normalizedIds;
            }
            else
            {
                _outputTargetCandidates = normalizedIds;
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to load target candidates from hotkey bindings");
        }
    }

    private static string NormalizeHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return string.Empty;
        }

        return hotkey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private List<AudioOutputDeviceItem> EnumerateCoreAudioRenderEndpoints()
    {
        var list = new List<AudioOutputDeviceItem>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out collection!);
            if (hr != 0 || collection == null)
            {
                RaiseDebug($"Core Audio 输出枚举失败：hr=0x{hr:X8}");
                return list;
            }

            hr = collection.GetCount(out var count);
            if (hr != 0)
            {
                RaiseDebug($"Core Audio 输出数量读取失败：hr=0x{hr:X8}");
                return list;
            }
            RaiseDebug($"[音频枚举] Core Audio 输出端点数量：{count}");

            for (var i = 0u; i < count; i++)
            {
                IMMDevice? endpoint = null;
                try
                {
                    hr = collection.Item(i, out endpoint!);
                    if (hr != 0 || endpoint == null)
                    {
                        continue;
                    }

                    hr = endpoint.GetId(out var endpointId);
                    if (hr != 0 || string.IsNullOrWhiteSpace(endpointId))
                    {
                        continue;
                    }

                    var name = ResolveOutputDeviceName(endpoint, endpointId);
                    RaiseDebug($"[音频枚举] 发现端点：name={name}, endpoint={endpointId}");
                    list.Add(new AudioOutputDeviceItem
                    {
                        Id = endpointId,
                        EndpointId = endpointId,
                        PhysicalId = endpointId,
                        Name = string.IsNullOrWhiteSpace(name) ? endpointId : name
                    });
                }
                finally
                {
                    if (endpoint != null)
                    {
                        Marshal.ReleaseComObject(endpoint);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to enumerate Core Audio render endpoints");
        }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }

        return list;
    }

    private string? GetCurrentDefaultOutputEndpointId()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out endpoint!);
            if (hr != 0 || endpoint == null)
            {
                return null;
            }

            hr = endpoint.GetId(out var id);
            return hr == 0 ? id : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (endpoint != null) Marshal.ReleaseComObject(endpoint);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if ((int)e.Message.MessageId != WmHotkey)
        {
            return;
        }

        var hotkeyId = (int)e.Message.WParam;
        RaiseDebug($"收到 WM_HOTKEY: id={hotkeyId}");

        if (_suspendHotkeyActions)
        {
            RaiseDebug($"忽略 WM_HOTKEY（录制暂停中）: id={hotkeyId}");
            return;
        }

        QueueHotkeyAction(hotkeyId, "WM_HOTKEY");
    }

    private void RegisterOrUpdateHotkey(int hotkeyId, string hotkeyText, HotkeyActionKind actionKind, string? statusText = null)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var hwnd = new HWND(_windowHandle);
        _ = PInvoke.UnregisterHotKey(hwnd, hotkeyId);
        _registeredHotkeys.TryRemove(hotkeyId, out _);
        _registeredHotkeyTexts.TryRemove(hotkeyId, out _);
        _registeredHotkeyKinds.TryRemove(hotkeyId, out _);
        UnregisterFallbackHotkey(hotkeyId);

        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            RaiseDebug($"已清空快捷键({hotkeyId})");
            return;
        }

        if (!TryParseHotkey(hotkeyText, out var modifiers, out var key))
        {
            _log.Warning("Skip registering invalid hotkey: {HotkeyText}", hotkeyText);
            RaiseDebug($"注册失败：无效快捷键 {hotkeyText}");
            _registeredHotkeyTexts.TryRemove(hotkeyId, out _);
            return;
        }

        if (actionKind == HotkeyActionKind.Notification)
        {
            RegisterFallbackHotkey(hotkeyId, modifiers, key, hotkeyText);
            _registeredHotkeyKinds[hotkeyId] = actionKind;
            _registeredHotkeyTexts[hotkeyId] = hotkeyText;
            _notificationHotkeyConfigs[hotkeyId] = new NotificationHotkeyConfig
            {
                Hotkey = hotkeyText,
                StatusText = statusText ?? string.Empty
            };
            RaiseDebug($"按键通知改用钩子监听，不占用系统热键: {hotkeyText}");
            return;
        }

        if (PInvoke.RegisterHotKey(hwnd, hotkeyId, modifiers, key))
        {
            _registeredHotkeys[hotkeyId] = (modifiers, key);
            _registeredHotkeyTexts[hotkeyId] = hotkeyText;
            _registeredHotkeyKinds[hotkeyId] = actionKind;
            if (actionKind == HotkeyActionKind.Notification)
            {
                _notificationHotkeyConfigs[hotkeyId] = new NotificationHotkeyConfig
                {
                    Hotkey = hotkeyText,
                    StatusText = statusText ?? string.Empty
                };
            }
            _log.Information("Registered hotkey {HotkeyId}: {HotkeyText}", hotkeyId, hotkeyText);
            RaiseDebug($"已注册快捷键({hotkeyId}): {hotkeyText}");
        }
        else
        {
            // Some environments do not support MOD_NOREPEAT for all combinations.
            var fallbackModifiers = modifiers & ~HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if (fallbackModifiers != modifiers && PInvoke.RegisterHotKey(hwnd, hotkeyId, fallbackModifiers, key))
            {
                _registeredHotkeys[hotkeyId] = (fallbackModifiers, key);
                _registeredHotkeyTexts[hotkeyId] = hotkeyText;
                _registeredHotkeyKinds[hotkeyId] = actionKind;
                if (actionKind == HotkeyActionKind.Notification)
                {
                    _notificationHotkeyConfigs[hotkeyId] = new NotificationHotkeyConfig
                    {
                        Hotkey = hotkeyText,
                        StatusText = statusText ?? string.Empty
                    };
                }
                _log.Information("Registered hotkey (fallback) {HotkeyId}: {HotkeyText}", hotkeyId, hotkeyText);
                RaiseDebug($"已注册快捷键(降级)(id={hotkeyId}): {hotkeyText}");
                return;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorHotkeyAlreadyRegistered)
            {
                RegisterFallbackHotkey(hotkeyId, fallbackModifiers, key, hotkeyText);
                _registeredHotkeyKinds[hotkeyId] = actionKind;
                if (actionKind == HotkeyActionKind.Notification)
                {
                    _notificationHotkeyConfigs[hotkeyId] = new NotificationHotkeyConfig
                    {
                        Hotkey = hotkeyText,
                        StatusText = statusText ?? string.Empty
                    };
                }
                RaiseDebug($"快捷键冲突，启用钩子监听: {hotkeyText}");
                return;
            }

            _log.Warning(
                "Failed to register hotkey {HotkeyId}: {HotkeyText}, key={VirtualKey}, modifiers={Modifiers}, win32={Error}",
                hotkeyId,
                hotkeyText,
                key,
                modifiers,
                error);
            RaiseDebug($"注册失败(id={hotkeyId}) win32={error}: {hotkeyText}");
        }

        EnsureKeyboardHook();
    }

    private void UnregisterHotkey(int hotkeyId)
    {
        if (_windowHandle != IntPtr.Zero)
        {
            _ = PInvoke.UnregisterHotKey(new HWND(_windowHandle), hotkeyId);
        }

        _registeredHotkeys.TryRemove(hotkeyId, out _);
        _registeredHotkeyTexts.TryRemove(hotkeyId, out _);
        _registeredHotkeyKinds.TryRemove(hotkeyId, out _);
        UnregisterFallbackHotkey(hotkeyId);
    }

    private List<NotificationHotkeyConfig> LoadNotificationHotkeyConfigs()
    {
        var configs = _localSettingsService.ReadSetting(NotificationHotkeyBindingsKey, new List<NotificationHotkeyConfig>()) ?? [];
        if (configs.Count > 0)
        {
            return configs
                .Select(config => new NotificationHotkeyConfig
                {
                    Id = string.IsNullOrWhiteSpace(config.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                    Hotkey = NormalizeOrDefault(config.Hotkey, HotkeyNotificationCard.DefaultHotkey),
                    StatusText = NormalizeOrDefault(config.StatusText, HotkeyNotificationCard.DefaultStatus)
                })
                .ToList();
        }

        return
        [
            new NotificationHotkeyConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Hotkey = NormalizeOrDefault(_localSettingsService.ReadSetting<string>("HotkeyNotification.Key"), HotkeyNotificationCard.DefaultHotkey),
                StatusText = NormalizeOrDefault(_localSettingsService.ReadSetting<string>("HotkeyNotification.Status"), HotkeyNotificationCard.DefaultStatus)
            }
        ];
    }

    private static bool TryParseHotkey(string hotkeyText, out HOT_KEY_MODIFIERS modifiers, out uint virtualKey)
    {
        modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            return false;
        }

        var normalized = NormalizeHotkeyText(hotkeyText);
        var parts = normalized
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (parts.Count == 0)
        {
            return false;
        }

        var keyToken = parts[^1];
        parts.RemoveAt(parts.Count - 1);

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= HOT_KEY_MODIFIERS.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= HOT_KEY_MODIFIERS.MOD_WIN;
                    break;
            }
        }

        if (keyToken.Length == 1)
        {
            var ch = keyToken[0];
            if (char.IsDigit(ch))
            {
                virtualKey = (uint)('0' + (ch - '0'));
                return true;
            }

            if (char.IsLetter(ch))
            {
                virtualKey = char.ToUpperInvariant(ch);
                return true;
            }
        }

        switch (keyToken.Trim().ToLowerInvariant())
        {
            case "esc":
            case "escape":
                virtualKey = (uint)VirtualKey.Escape;
                return true;
            case "back":
            case "backspace":
                virtualKey = (uint)VirtualKey.Back;
                return true;
            case "capslock":
                virtualKey = 0x14;
                return true;
            case "printscreen":
            case "snapshot":
                virtualKey = 0x2C;
                return true;
            case "pause":
                virtualKey = 0x13;
                return true;
            case "application":
            case "apps":
                virtualKey = 0x5D;
                return true;
        }

        if (Enum.TryParse<VirtualKey>(keyToken, true, out var key))
        {
            virtualKey = (uint)key;
            return true;
        }

        if (TryParseDigitAliases(keyToken, out virtualKey))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeHotkeyText(string hotkeyText)
    {
        return hotkeyText
            .Normalize(NormalizationForm.FormKC)
            .Replace("\\u002B", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("\\uFF0B", "+", StringComparison.OrdinalIgnoreCase)
            .Replace('\uFF0B', '+')
            .Trim();
    }

    private static void SendCustomHotkey(HOT_KEY_MODIFIERS modifiers, uint virtualKey)
    {
        var modifierKeys = new List<byte>();

        if (modifiers.HasFlag(HOT_KEY_MODIFIERS.MOD_CONTROL))
        {
            modifierKeys.Add((byte)VirtualKey.Control);
        }

        if (modifiers.HasFlag(HOT_KEY_MODIFIERS.MOD_SHIFT))
        {
            modifierKeys.Add((byte)VirtualKey.Shift);
        }

        if (modifiers.HasFlag(HOT_KEY_MODIFIERS.MOD_ALT))
        {
            modifierKeys.Add((byte)VirtualKey.Menu);
        }

        if (modifiers.HasFlag(HOT_KEY_MODIFIERS.MOD_WIN))
        {
            modifierKeys.Add((byte)VirtualKey.LeftWindows);
        }

        foreach (var modifierKey in modifierKeys)
        {
            NativeKeyboardMethods.keybd_event(modifierKey, 0, 0, 0);
        }

        NativeKeyboardMethods.keybd_event((byte)virtualKey, 0, 0, 0);
        NativeKeyboardMethods.keybd_event((byte)virtualKey, 0, 0x0002, 0);

        for (var index = modifierKeys.Count - 1; index >= 0; index--)
        {
            NativeKeyboardMethods.keybd_event(modifierKeys[index], 0, 0x0002, 0);
        }
    }

    private string GetHotkeyDisplayText(int hotkeyId)
    {
        if (_registeredHotkeyTexts.TryGetValue(hotkeyId, out var hotkeyText) &&
            !string.IsNullOrWhiteSpace(hotkeyText))
        {
            return hotkeyText;
        }

        if (_registeredHotkeyKinds.TryGetValue(hotkeyId, out var kind) &&
            kind == HotkeyActionKind.Notification)
        {
            return "按键通知";
        }

        if (hotkeyId == AutoBrightnessHotkeyId)
        {
            return "自动亮度快捷键";
        }

        return hotkeyId == InputHotkeyId ? "输入快捷键" : "输出快捷键";
    }

    private void UnregisterAllRegisteredHotkeys()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var hwnd = new HWND(_windowHandle);
        foreach (var hotkeyId in _registeredHotkeys.Keys.ToArray())
        {
            _ = PInvoke.UnregisterHotKey(hwnd, hotkeyId);
        }
    }

    private void UnregisterNotificationHotkeys()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var hwnd = new HWND(_windowHandle);
        foreach (var hotkeyId in _registeredHotkeyKinds
                     .Where(pair => pair.Value == HotkeyActionKind.Notification)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _ = PInvoke.UnregisterHotKey(hwnd, hotkeyId);
            _registeredHotkeys.TryRemove(hotkeyId, out _);
            _registeredHotkeyTexts.TryRemove(hotkeyId, out _);
            _registeredHotkeyKinds.TryRemove(hotkeyId, out _);
            _notificationHotkeyConfigs.TryRemove(hotkeyId, out _);
            _fallbackHotkeys.TryRemove(hotkeyId, out _);
            _fallbackActiveKeys.TryRemove(hotkeyId, out _);
        }

        _notificationHotkeysRegistered = false;
    }

    private static string BuildNotificationHotkeySignature(IReadOnlyList<NotificationHotkeyConfig> configs)
    {
        return string.Join('|', configs.Select(config =>
            $"{NormalizeOrDefault(config.Hotkey, string.Empty)}\u001F{NormalizeOrDefault(config.StatusText, string.Empty)}"));
    }

    private static string NormalizeOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim()
            .Replace("\\u002B", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("\\uFF0B", "+", StringComparison.OrdinalIgnoreCase);
    }

    private void InvalidateOutputDeviceCache()
    {
        lock (_outputDeviceListCacheGate)
        {
            _cachedOutputDevices = null;
            _cachedOutputDevicesAt = DateTimeOffset.MinValue;
        }
    }

    private static bool TryParseDigitAliases(string keyToken, out uint virtualKey)
    {
        virtualKey = 0;
        var token = keyToken.ToUpperInvariant();
        if (token.Length == 2 && token[0] == 'D' && char.IsDigit(token[1]))
        {
            virtualKey = (uint)token[1];
            return true;
        }

        if (token.StartsWith("NUMPAD", StringComparison.Ordinal) &&
            token.Length == "NUMPAD".Length + 1 &&
            char.IsDigit(token[^1]))
        {
            var num = token[^1] - '0';
            virtualKey = (uint)VirtualKey.NumberPad0 + (uint)num;
            return true;
        }

        return false;
    }

    private void RegisterFallbackHotkey(int hotkeyId, HOT_KEY_MODIFIERS modifiers, uint key, string hotkeyText)
    {
        _fallbackHotkeys[hotkeyId] = (modifiers & ~HOT_KEY_MODIFIERS.MOD_NOREPEAT, key);
        _registeredHotkeyTexts[hotkeyId] = hotkeyText;
        EnsureKeyboardHook();
        _log.Warning("Hotkey conflict detected, fallback hook enabled for {HotkeyId}: {HotkeyText}", hotkeyId, hotkeyText);
    }

    private void UnregisterFallbackHotkey(int hotkeyId)
    {
        _fallbackHotkeys.TryRemove(hotkeyId, out _);
        _fallbackActiveKeys.TryRemove(hotkeyId, out _);
    }

    private void EnsureKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            return;
        }

        _keyboardHookProc = KeyboardHookProc;
        _keyboardHookHandle = NativeKeyboardMethods.SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, IntPtr.Zero, 0);
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _log.Warning("Failed to enable keyboard hook fallback, win32={Error}", error);
        }
    }

    private void EnsureMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        _mouseHookProc = MouseHookProc;
        _mouseHookHandle = NativeKeyboardMethods.SetWindowsHookEx(WhMouseLl, _mouseHookProc, IntPtr.Zero, 0);
        if (_mouseHookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _log.Debug("Failed to enable mouse hook for user activity, win32={Error}", error);
        }
    }

    private void DisposeKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            _ = NativeKeyboardMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }
    }

    private void DisposeMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            _ = NativeKeyboardMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var isCapsLockVk = info.vkCode == 0x14;
            if (isCapsLockVk)
            {
                RaiseDebug($"CapsLock 钩子消息: msg=0x{message:X}, vk={info.vkCode}, keyState=0x{NativeKeyboardMethods.GetKeyState(0x14):X4}, asyncState=0x{NativeKeyboardMethods.GetAsyncKeyState(0x14):X4}");
            }

            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                NotifyUserActivityFromKeyboard(info.vkCode);
                if (_suspendHotkeyActions)
                {
                    RaiseHotkeyCaptureKeyDown((VirtualKey)info.vkCode, GetCurrentCaptureModifiers());
                }
                var handledByFallback = TryTriggerFallbackHotkeys(info.vkCode);
                if (isCapsLockVk)
                {
                    RaiseDebug($"CapsLock KeyDown fallback处理结果: handled={handledByFallback}");
                }

                if (handledByFallback)
                {
                    return (IntPtr)1;
                }
            }
            else if (message == WmKeyUp || message == WmSysKeyUp)
            {
                if (isCapsLockVk)
                {
                    RaiseDebug($"CapsLock KeyUp 跳过 fallback 释放，keyState=0x{NativeKeyboardMethods.GetKeyState(0x14):X4}, asyncState=0x{NativeKeyboardMethods.GetAsyncKeyState(0x14):X4}");
                }
                else if (ReleaseFallbackHotkeys(info.vkCode))
                {
                    return (IntPtr)1;
                }
            }
        }

        return NativeKeyboardMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private void NotifyUserActivityFromKeyboard(uint vkCode)
    {
        if (IsModifierKey(vkCode))
        {
            return;
        }

        if (ResolveActionHotkeyId(ToHotKeyModifiers(GetCurrentCaptureModifiers()), vkCode) != null)
        {
            return;
        }

        // Temporarily disabled: keyboard input should not trigger auto brightness.
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        return NativeKeyboardMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void NotifyUserAction(string source)
    {
        if (_suspendHotkeyActions)
        {
            return;
        }

        var now = DateTime.Now;
        if (now - _lastUserActivityTriggerAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastUserActivityTriggerAt = now;
        _autoBrightnessService.OnUserAction(source);
        RaiseDebug($"触发自动亮度更新：{source}");
    }

    private static bool IsModifierKey(uint vkCode)
    {
        return vkCode is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;
    }

    private void RaiseHotkeyCaptureKeyDown(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        try
        {
            var signature = $"{(int)key}:{(int)modifiers}";
            if (string.Equals(_lastCaptureSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            _lastCaptureSignature = signature;
            HotkeyCaptureKeyDown?.Invoke(key, modifiers);
        }
        catch
        {
            // ignore listener errors
        }
    }

    private static bool TryGetPressedCaptureHotkey(out VirtualKey key, out VirtualKeyModifiers modifiers)
    {
        modifiers = GetCurrentCaptureModifiers();
        for (var vk = 1; vk <= 255; vk++)
        {
            if (IsMouseButtonKey((uint)vk))
            {
                continue;
            }

            if (IsModifierKey((uint)vk))
            {
                continue;
            }

            if (IsVirtualKeyDown(vk))
            {
                key = (VirtualKey)vk;
                return true;
            }
        }

        key = default;
        return false;
    }

    private static bool IsMouseButtonKey(uint vkCode)
    {
        return vkCode is 0x01 or 0x02 or 0x04 or 0x05 or 0x06;
    }

    private static VirtualKeyModifiers GetCurrentCaptureModifiers()
    {
        var modifiers = VirtualKeyModifiers.None;
        if (IsVirtualKeyDown(0x11)) modifiers |= VirtualKeyModifiers.Control;
        if (IsVirtualKeyDown(0x12)) modifiers |= VirtualKeyModifiers.Menu;
        if (IsVirtualKeyDown(0x10)) modifiers |= VirtualKeyModifiers.Shift;
        if (IsVirtualKeyDown(0x5B) || IsVirtualKeyDown(0x5C)) modifiers |= VirtualKeyModifiers.Windows;
        return modifiers;
    }

    private static HOT_KEY_MODIFIERS ToHotKeyModifiers(VirtualKeyModifiers modifiers)
    {
        var hotkeyModifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (modifiers.HasFlag(VirtualKeyModifiers.Control)) hotkeyModifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        if (modifiers.HasFlag(VirtualKeyModifiers.Menu)) hotkeyModifiers |= HOT_KEY_MODIFIERS.MOD_ALT;
        if (modifiers.HasFlag(VirtualKeyModifiers.Shift)) hotkeyModifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        if (modifiers.HasFlag(VirtualKeyModifiers.Windows)) hotkeyModifiers |= HOT_KEY_MODIFIERS.MOD_WIN;
        return hotkeyModifiers;
    }

    private static bool IsVirtualKeyDown(int vKey)
    {
        return (NativeKeyboardMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    private bool TryTriggerFallbackHotkeys(uint pressedVk)
    {
        if (_suspendHotkeyActions)
        {
            return false;
        }

        HOT_KEY_MODIFIERS? matchedModifiers = null;
        foreach (var pair in _fallbackHotkeys)
        {
            var (modifiers, key) = pair.Value;

            if (pressedVk != key)
            {
                continue;
            }

            if (!AreModifiersPressed(modifiers))
            {
                continue;
            }

            matchedModifiers = modifiers;
            break;
        }

        if (matchedModifiers is null)
        {
            return false;
        }

        var resolvedHotkeyId = ResolveActionHotkeyId(matchedModifiers.Value, pressedVk);
        if (resolvedHotkeyId is null)
        {
            return false;
        }

        if (pressedVk == 0x14)
        {
            RaiseDebug($"CapsLock 命中 fallback 解析: hotkeyId={resolvedHotkeyId.Value}, modifiers={matchedModifiers.Value}");
        }

        // SoundSwitch-style behavior:
        // if a combo has a successfully registered WM_HOTKEY binding,
        // let WM_HOTKEY path own the execution to avoid double trigger.
        if (_registeredHotkeys.ContainsKey(resolvedHotkeyId.Value))
        {
            return false;
        }

        if (IsNotificationHotkey(resolvedHotkeyId.Value))
        {
            if (IsCapsLockNotificationHotkey(resolvedHotkeyId.Value))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(120);
                        QueueHotkeyAction(resolvedHotkeyId.Value, $"HOOK_DELAYED(vk={pressedVk})");
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "CapsLock delayed notification failed");
                    }
                }).Forget(nameof(AudioRouteHotkeyService));
            }
            else
            {
                QueueHotkeyAction(resolvedHotkeyId.Value, $"HOOK(vk={pressedVk})");
            }

            return false;
        }

        if (!_fallbackActiveKeys.TryAdd(resolvedHotkeyId.Value, 0))
        {
            return false;
        }

        QueueHotkeyAction(resolvedHotkeyId.Value, $"HOOK(vk={pressedVk})");
        return true;
    }

    private int? ResolveActionHotkeyId(HOT_KEY_MODIFIERS modifiers, uint key)
    {
        if (IsHotkeyMatchForId(InputHotkeyId, modifiers, key))
        {
            return InputHotkeyId;
        }

        if (IsHotkeyMatchForId(OutputHotkeyId, modifiers, key))
        {
            return OutputHotkeyId;
        }

        if (IsHotkeyMatchForId(AutoBrightnessHotkeyId, modifiers, key))
        {
            return AutoBrightnessHotkeyId;
        }

        foreach (var hotkeyId in _registeredHotkeys.Keys)
        {
            if (hotkeyId == InputHotkeyId || hotkeyId == OutputHotkeyId || hotkeyId == AutoBrightnessHotkeyId)
            {
                continue;
            }

            if (IsHotkeyMatchForId(hotkeyId, modifiers, key))
            {
                return hotkeyId;
            }
        }

        foreach (var hotkeyId in _fallbackHotkeys.Keys)
        {
            if (hotkeyId == InputHotkeyId || hotkeyId == OutputHotkeyId || hotkeyId == AutoBrightnessHotkeyId)
            {
                continue;
            }

            if (IsHotkeyMatchForId(hotkeyId, modifiers, key))
            {
                return hotkeyId;
            }
        }

        return null;
    }

    private bool IsHotkeyMatchForId(int hotkeyId, HOT_KEY_MODIFIERS modifiers, uint key)
    {
        if (_registeredHotkeys.TryGetValue(hotkeyId, out var registered)
            && NormalizeModifiers(registered.Modifiers) == NormalizeModifiers(modifiers)
            && registered.Key == key)
        {
            return true;
        }

        if (_fallbackHotkeys.TryGetValue(hotkeyId, out var fallback)
            && NormalizeModifiers(fallback.Modifiers) == NormalizeModifiers(modifiers)
            && fallback.Key == key)
        {
            return true;
        }

        return false;
    }

    private static HOT_KEY_MODIFIERS NormalizeModifiers(HOT_KEY_MODIFIERS modifiers)
    {
        return modifiers & ~HOT_KEY_MODIFIERS.MOD_NOREPEAT;
    }

    private void QueueHotkeyAction(int hotkeyId, string source)
    {
        RaiseDebug($"[快捷键] 请求排队：id={hotkeyId}, source={source}, registered={_registeredHotkeys.ContainsKey(hotkeyId)}, fallback={_fallbackHotkeys.ContainsKey(hotkeyId)}");
        if (!_registeredHotkeys.ContainsKey(hotkeyId) && !_fallbackHotkeys.ContainsKey(hotkeyId))
        {
            RaiseDebug($"忽略未知热键ID: {hotkeyId}, source={source}");
            return;
        }

        if (source.StartsWith("HOOK", StringComparison.OrdinalIgnoreCase) && _registeredHotkeys.ContainsKey(hotkeyId))
        {
            RaiseDebug($"忽略HOOK触发（已注册WM_HOTKEY）: id={hotkeyId}, source={source}");
            return;
        }

        if (_suspendHotkeyActions)
        {
            RaiseDebug($"忽略热键触发（录制暂停中）: id={hotkeyId}, source={source}");
            return;
        }

        var enqueued = _dispatcherQueue?.TryEnqueue(async () => await ExecuteQueuedHotkeyActionAsync(hotkeyId, source)) ?? false;
        RaiseDebug($"[快捷键] 排队结果：id={hotkeyId}, source={source}, dispatcherAvailable={_dispatcherQueue != null}, enqueued={enqueued}");
        if (!enqueued)
        {
            _ = ExecuteQueuedHotkeyActionAsync(hotkeyId, source);
        }
    }

    private async Task ExecuteQueuedHotkeyActionAsync(int hotkeyId, string source)
    {
        if (_suspendHotkeyActions)
        {
            RaiseDebug($"忽略已排队热键动作（录制暂停中）: id={hotkeyId}, source={source}");
            return;
        }

        var actionLock = _hotkeyActionLocks.GetOrAdd(hotkeyId, _ => new SemaphoreSlim(1, 1));
        await actionLock.WaitAsync();
        try
        {
            if (_suspendHotkeyActions)
            {
                RaiseDebug($"忽略执行中的热键动作（录制暂停中）: id={hotkeyId}, source={source}");
                return;
            }

            await ExecuteHotkeyActionAsync(hotkeyId, source);
        }
        finally
        {
            actionLock.Release();
        }
    }

    private async Task ExecuteHotkeyActionAsync(int hotkeyId, string source)
    {
        try
        {
            RaiseDebug($"[快捷键] 开始执行：id={hotkeyId}, source={source}, window=0x{_windowHandle.ToInt64():X}");
            if (_suspendHotkeyActions)
            {
                RaiseDebug($"忽略热键动作执行（录制暂停中）: id={hotkeyId}, source={source}");
                return;
            }

            var hotkeyText = GetHotkeyDisplayText(hotkeyId);
            if (!_registeredHotkeyKinds.TryGetValue(hotkeyId, out var actionKind))
            {
                RaiseDebug($"热键执行失败：未找到动作类型，id={hotkeyId}, source={source}");
                return;
            }
            RaiseDebug($"[快捷键] 动作解析：id={hotkeyId}, text={hotkeyText}, kind={actionKind}, outputId={OutputHotkeyId}");

            _inputTargetCandidatesFromManualSelection = false;
            _outputTargetCandidatesFromManualSelection = false;
            if (_userHotkeyBindings.TryGetValue(hotkeyId, out var userBinding))
            {
                if (userBinding.IsInput)
                {
                    _inputTargetCandidates = userBinding.DeviceIds;
                    _inputTargetCandidatesFromManualSelection = userBinding.DeviceIds.Length > 0;
                }
                else
                {
                    _outputTargetCandidates = userBinding.DeviceIds;
                    _outputTargetCandidatesFromManualSelection = userBinding.DeviceIds.Length > 0;
                }

                RaiseDebug($"[用户快捷键] 使用绑定：id={hotkeyId}, type={(userBinding.IsInput ? "输入" : "输出")}, deviceCount={userBinding.DeviceIds.Length}");
            }

            switch (actionKind)
            {
                case HotkeyActionKind.InputRoute:
                {
                    RaiseDebug($"开始执行输入热键，source={source}");
                    var triggerTask = TriggerInputRouteAsync();
                    var completed = await Task.WhenAny(triggerTask, Task.Delay(TimeSpan.FromSeconds(15)));
                    var ok = completed == triggerTask && await triggerTask;
                    if (completed != triggerTask)
                    {
                        RaiseDebug("输入热键执行超时，已释放触发锁");
                    }

                    RaiseDebug($"输入热键执行{(ok ? "成功" : "失败")}，source={source}");
                    break;
                }
                case HotkeyActionKind.OutputRoute:
                {
                    RaiseDebug($"[快捷键] 开始执行输出热键：source={source}");
                    var triggerTask = TriggerOutputRouteAsync();
                    var completed = await Task.WhenAny(triggerTask, Task.Delay(TimeSpan.FromSeconds(15)));
                    var ok = completed == triggerTask && await triggerTask;
                    if (completed != triggerTask)
                    {
                        RaiseDebug("输出热键执行超时，已释放触发锁");
                    }

                    RaiseDebug($"[快捷键] 输出热键执行{(ok ? "成功" : "失败")}：source={source}");
                    break;
                }
                case HotkeyActionKind.Notification:
                {
                    if (!_notificationHotkeyConfigs.TryGetValue(hotkeyId, out var config))
                    {
                        RaiseDebug($"按键通知执行失败：找不到配置，id={hotkeyId}, source={source}");
                        return;
                    }

                    var statusText = config.StatusText;
                    if (IsCapsLockHotkey(config.Hotkey))
                    {
                        statusText = GetCapsLockToggleStatusText();
                        RaiseDebug($"CapsLock 真实状态读取完成：{statusText}, source={source}");
                    }

                    RaiseDebug($"触发按键通知：{hotkeyText}, source={source}");
                    _notificationService.ShowHotkeyPreview(hotkeyText, statusText);
                    break;
                }
                case HotkeyActionKind.AutoBrightness:
                {
                    RaiseDebug($"触发自动亮度快捷键：{hotkeyText}, source={source}");
                    await _autoBrightnessService.TriggerFromHotkeyAsync();
                    break;
                }
                default:
                    RaiseDebug($"热键执行失败：未知动作类型 {actionKind}, id={hotkeyId}, source={source}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Hotkey action execution failed, id={HotkeyId}, source={Source}", hotkeyId, source);
            RaiseDebug($"热键执行异常: {ex.Message}, source={source}");
        }
        finally
        {
            _fallbackActiveKeys.TryRemove(hotkeyId, out _);
        }
    }

    private sealed class HotkeyBindingConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("hotkey")]
        public string Hotkey { get; set; } = string.Empty;

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("deviceIds")]
        public List<string> DeviceIds { get; set; } = [];
    }

    private sealed class UserHotkeyBinding
    {
        public bool IsInput { get; init; }

        public string Hotkey { get; init; } = string.Empty;

        public string[] DeviceIds { get; init; } = [];
    }

    private enum HotkeyActionKind
    {
        InputRoute,
        OutputRoute,
        Notification,
        AutoBrightness
    }

    private sealed class NotificationHotkeyConfig
    {
        public string Id { get; set; } = string.Empty;

        public string Hotkey { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;
    }

    private sealed class InputRouteCandidate
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public DeviceInformation? Device { get; init; }

        public bool IsAvailable { get; init; }
    }

    private static bool IsCapsLockHotkey(string? hotkey)
    {
        return string.Equals(BluetoothPageViewModel.NormalizeHotkey(hotkey), "CAPSLOCK", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCapsLockNotificationHotkey(int hotkeyId)
    {
        return _notificationHotkeyConfigs.TryGetValue(hotkeyId, out var config) && IsCapsLockHotkey(config.Hotkey);
    }

    private bool IsNotificationHotkey(int hotkeyId)
    {
        return _registeredHotkeyKinds.TryGetValue(hotkeyId, out var kind) && kind == HotkeyActionKind.Notification;
    }

    private static string GetCapsLockToggleStatusText()
    {
        var state = NativeKeyboardMethods.GetKeyState(0x14);
        var isOn = (state & 0x0001) != 0;
        RaiseDebugStatic($"CapsLock 状态读取: keyState=0x{state:X4}, result={(isOn ? "已开启" : "已关闭")}");
        return isOn ? "已开启" : "已关闭";
    }

    private static string[] NormalizeDeviceIdList(IReadOnlyList<string>? ids)
    {
        if (ids == null || ids.Count == 0)
        {
            return [];
        }

        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeSelectionId(string? selectionId, bool isInput = true)
    {
        if (string.IsNullOrWhiteSpace(selectionId))
        {
            return string.Empty;
        }

        if (DeviceIdentityHelper.TryParseSelectionId(selectionId, out _, out var physicalDeviceId))
        {
            return DeviceIdentityHelper.BuildSelectionId(physicalDeviceId, isInput);
        }

        return DeviceIdentityHelper.BuildSelectionId(selectionId, isInput);
    }

    private static string NormalizeInputDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        if (DeviceIdentityHelper.TryParseSelectionId(deviceId, out _, out var physicalDeviceId))
        {
            return physicalDeviceId;
        }

        return deviceId.Trim();
    }

    private static string GetSelectionPhysicalDeviceId(string? selectionId)
    {
        if (string.IsNullOrWhiteSpace(selectionId))
        {
            return string.Empty;
        }

        return DeviceIdentityHelper.TryParseSelectionId(selectionId, out _, out var physicalDeviceId)
            ? physicalDeviceId
            : selectionId.Trim();
    }

    private static string GetOutputSelectionId(AudioOutputDeviceItem device)
    {
        var physicalId = string.IsNullOrWhiteSpace(device.PhysicalId) ? device.Id : device.PhysicalId;
        return NormalizeSelectionId(physicalId, isInput: false);
    }

    private async Task<List<InputRouteCandidate>> LoadInputRouteCandidatesAsync()
    {
        var currentDevices = (await _connectionService.FindAllInputRouteDevicesAsync())
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => NormalizeInputDeviceId(d.Id), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var candidates = BuildInputRouteCandidates(currentDevices, isAvailable: true);
        var knownIds = new HashSet<string>(candidates.Select(candidate => candidate.Id), StringComparer.OrdinalIgnoreCase);

        var retainedIds = new List<string>();
        var inputConfigs = await _localSettingsService.ReadSettingAsync(InputHotkeyBindingsKey, new List<HotkeyBindingConfig>()) ?? [];
        retainedIds.AddRange(inputConfigs.SelectMany(config => config.DeviceIds ?? []));

        var savedDeviceIds = await _localSettingsService.ReadSettingAsync(BluetoothConnectedDeviceIdsKey, new List<string>()) ?? [];
        retainedIds.AddRange(savedDeviceIds);

        var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(preferredInputId))
        {
            retainedIds.Add(preferredInputId);
        }

        foreach (var retainedId in retainedIds
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(NormalizeInputDeviceId)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (knownIds.Contains(retainedId))
            {
                continue;
            }

            DeviceInformation? retainedDevice = null;
            try
            {
                retainedDevice = await DeviceInformation.CreateFromIdAsync(retainedId);
                if (retainedDevice == null || string.IsNullOrWhiteSpace(retainedDevice.Id))
                {
                    retainedDevice = null;
                }
            }
            catch
            {
                // ignore unresolved retained entries
            }

            candidates.Add(new InputRouteCandidate
            {
                Id = retainedId,
                Name = retainedDevice == null || string.IsNullOrWhiteSpace(retainedDevice.Name)
                    ? retainedId
                    : retainedDevice.Name,
                Device = retainedDevice,
                IsAvailable = false
            });
            knownIds.Add(retainedId);
        }

        return candidates;
    }

    private static List<InputRouteCandidate> BuildInputRouteCandidates(IReadOnlyList<DeviceInformation> devices, bool isAvailable)
    {
        return devices
            .Select(device => new InputRouteCandidate
            {
                Id = NormalizeInputDeviceId(device.Id),
                Name = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                Device = device,
                IsAvailable = isAvailable
            })
            .ToList();
    }

    private static List<InputRouteCandidate> FilterInputRouteCandidates(
        IReadOnlyList<InputRouteCandidate> allCandidates,
        IReadOnlyList<string> candidateIds)
    {
        var lookup = allCandidates.ToDictionary(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase);
        var filtered = new List<InputRouteCandidate>(candidateIds.Count);

        foreach (var candidateId in candidateIds
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(NormalizeInputDeviceId)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (lookup.TryGetValue(candidateId, out var candidate))
            {
                filtered.Add(candidate);
            }
        }

        return filtered;
    }

    private int ResolveNextInputCandidateIndex(IReadOnlyList<InputRouteCandidate> inputCandidates, int currentIndex)
    {
        if (inputCandidates.Count == 0)
        {
            return -1;
        }

        var anchorId = _inputCycleAnchorCandidateId ?? string.Empty;
        var realCandidates = inputCandidates
            .Select((candidate, index) => new { candidate, index })
            .ToList();
        var anchorRealIndex = realCandidates.FindIndex(item =>
            string.Equals(item.candidate.Id, anchorId, StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0)
        {
            return realCandidates.Count > 0 ? realCandidates[0].index : 0;
        }

        var currentRealIndex = realCandidates.FindIndex(item =>
            string.Equals(item.candidate.Id, inputCandidates[currentIndex].Id, StringComparison.OrdinalIgnoreCase));
        if (currentRealIndex < 0)
        {
            return realCandidates.Count > 0 ? realCandidates[0].index : currentIndex;
        }

        if (string.Equals(inputCandidates[currentIndex].Id, anchorId, StringComparison.OrdinalIgnoreCase))
        {
            if (anchorRealIndex >= 0 && anchorRealIndex < realCandidates.Count - 1)
            {
                return realCandidates[anchorRealIndex + 1].index;
            }

            return realCandidates.Count > 0 ? realCandidates[0].index : currentIndex;
        }

        if (currentRealIndex < realCandidates.Count - 1)
        {
            return realCandidates[currentRealIndex + 1].index;
        }

        return realCandidates[0].index;
    }

    private async Task DisconnectConnectedInputDevicesExceptAsync(string? keepDeviceId = null)
    {
        var connectedSnapshot = _connectionService.ConnectedDevices.ToList();
        var normalizedKeepId = NormalizeSelectionId(keepDeviceId, isInput: true);
        foreach (var connected in connectedSnapshot)
        {
            if (!string.IsNullOrWhiteSpace(keepDeviceId) &&
                string.Equals(DeviceIdentityHelper.GetSelectionId(connected, isInput: true), normalizedKeepId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await _connectionService.DisconnectAsync(connected);
        }
    }

    private bool ReleaseFallbackHotkeys(uint releasedVk)
    {
        var handled = false;
        foreach (var pair in _fallbackHotkeys)
        {
            if (IsNotificationHotkey(pair.Key))
            {
                continue;
            }

            if (pair.Value.Key == releasedVk)
            {
                _fallbackActiveKeys.TryRemove(pair.Key, out _);
                handled = true;
            }
        }

        return handled;
    }

    private static bool AreModifiersPressed(HOT_KEY_MODIFIERS expected)
    {
        static bool IsDown(int vKey) => (NativeKeyboardMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;

        var altRequired = expected.HasFlag(HOT_KEY_MODIFIERS.MOD_ALT);
        var ctrlRequired = expected.HasFlag(HOT_KEY_MODIFIERS.MOD_CONTROL);
        var shiftRequired = expected.HasFlag(HOT_KEY_MODIFIERS.MOD_SHIFT);
        var winRequired = expected.HasFlag(HOT_KEY_MODIFIERS.MOD_WIN);

        var altDown = IsDown(0x12);     // VK_MENU
        var ctrlDown = IsDown(0x11);    // VK_CONTROL
        var shiftDown = IsDown(0x10);   // VK_SHIFT
        var winDown = IsDown(0x5B) || IsDown(0x5C); // VK_LWIN / VK_RWIN

        if (altRequired && !altDown) return false;
        if (ctrlRequired && !ctrlDown) return false;
        if (shiftRequired && !shiftDown) return false;
        if (winRequired && !winDown) return false;
        return true;
    }

    private bool SetDefaultAudioEndpoint(string deviceId)
    {
        IPolicyConfig? config = null;
        try
        {
            RaiseDebug($"[系统切换] 开始设置默认输出：endpoint={deviceId}");
            config = (IPolicyConfig)new PolicyConfigClientComObject();

            var hr1 = config.SetDefaultEndpoint(deviceId, ERole.eConsole);
            var hr2 = config.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            var hr3 = config.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            RaiseDebug($"[系统切换] SetDefaultEndpoint 返回：console=0x{hr1:X8}, multimedia=0x{hr2:X8}, comm=0x{hr3:X8}, success={hr1 == 0 && hr2 == 0 && hr3 == 0}");

            return hr1 == 0 && hr2 == 0 && hr3 == 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SetDefaultAudioEndpoint failed. Endpoint={Endpoint}", deviceId);
            RaiseDebug($"[系统切换] SetDefaultEndpoint 异常：endpoint={deviceId}, type={ex.GetType().Name}, message={ex.Message}");
            return false;
        }
        finally
        {
            if (config != null) Marshal.ReleaseComObject(config);
        }
    }

    private void RaiseDebug(string message)
    {
        var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        _log.Information("{DebugMessage}", timestampedMessage);
        WriteIndependentSwitchLog(timestampedMessage);

        try
        {
            DebugMessage?.Invoke(message);
        }
        catch
        {
            // ignore listener errors
        }
    }

    private static void WriteIndependentSwitchLog(string message)
    {
        try
        {
            File.AppendAllText(
                IndependentSwitchLogPath,
                message + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
            // Independent diagnostics must never interrupt audio switching.
        }
    }

    private void OnPlaybackDeviceConnected(object? sender, DeviceInformation device)
    {
        _ = TryRestorePreferredOutputRouteAsync(device);
    }

    private void OnAudioOutputDevicesChanged(object? sender, EventArgs args)
    {
        InvalidateOutputDeviceCache();
    }

    private async Task TryRestorePreferredOutputRouteAsync(DeviceInformation connectedDevice)
    {
        var preferredOutputId = _appSettingsService.PreferredOutputDeviceId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preferredOutputId))
        {
            return;
        }

        await _restoreOutputRouteLock.WaitAsync();
        try
        {
            const int maxAttempts = 5;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                await Task.Delay(attempt == 1 ? 500 : 300);

                var outputDevices = await GetOutputDevicesAsync();
                if (outputDevices.Count == 0)
                {
                    continue;
                }

                var preferredDevice = outputDevices.FirstOrDefault(d =>
                    string.Equals(d.Id, preferredOutputId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.EndpointId, preferredOutputId, StringComparison.OrdinalIgnoreCase));

                if (preferredDevice == null && !string.IsNullOrWhiteSpace(connectedDevice.Name))
                {
                    var connectedName = NormalizeDeviceName(connectedDevice.Name);
                    var nameMatches = outputDevices.Where(d =>
                    {
                        var outputName = NormalizeDeviceName(d.Name);
                        return string.Equals(outputName, connectedName, StringComparison.OrdinalIgnoreCase)
                            || outputName.Contains(connectedName, StringComparison.OrdinalIgnoreCase)
                            || connectedName.Contains(outputName, StringComparison.OrdinalIgnoreCase);
                    }).ToList();

                    if (nameMatches.Count == 1 &&
                        IsConfiguredPreferredDevice(nameMatches[0], preferredOutputId))
                    {
                        preferredDevice = nameMatches[0];
                    }
                }

                if (preferredDevice == null)
                {
                    if (attempt == maxAttempts)
                    {
                        RaiseDebug($"未找到可恢复的输出设备：preferred={preferredOutputId}, connected={connectedDevice.Name}");
                    }

                    continue;
                }

                var targetEndpointId = string.IsNullOrWhiteSpace(preferredDevice.EndpointId) ? preferredDevice.Id : preferredDevice.EndpointId;
                var currentEndpointId = GetCurrentDefaultOutputEndpointId() ?? string.Empty;
                if (string.Equals(currentEndpointId, targetEndpointId, StringComparison.OrdinalIgnoreCase))
                {
                    RaiseDebug($"已是首选输出，无需恢复：{preferredDevice.Name}");
                    return;
                }

                var ok = SetDefaultAudioEndpoint(targetEndpointId);
                var currentAfter = GetCurrentDefaultOutputEndpointId() ?? string.Empty;
                if (ok && string.Equals(currentAfter, targetEndpointId, StringComparison.OrdinalIgnoreCase))
                {
                    await _appSettingsService.SetPreferredOutputDeviceIdAsync(preferredDevice.Id);
                    await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, preferredDevice.Id);
                    RaiseDebug($"已恢复默认输出：{preferredDevice.Name}");
                    return;
                }

                if (attempt == maxAttempts)
                {
                    RaiseDebug($"恢复默认输出失败：{preferredDevice.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to restore output route after device reconnect");
        }
        finally
        {
            _restoreOutputRouteLock.Release();
        }
    }

    private static bool IsConfiguredPreferredDevice(AudioOutputDeviceItem device, string preferredOutputId)
    {
        return string.Equals(device.Id, preferredOutputId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(device.EndpointId, preferredOutputId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDeviceName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : name.Trim().ToUpperInvariant();
    }

    private string ResolveOutputDeviceName(string deviceId, string fallbackName)
    {
        try
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? endpoint = null;
            IPropertyStore? properties = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                var hr = enumerator.GetDevice(deviceId, out endpoint!);
                if (hr != 0 || endpoint == null)
                {
                    return string.IsNullOrWhiteSpace(fallbackName) ? deviceId : fallbackName;
                }

                hr = endpoint.OpenPropertyStore((int)StgmAccess.Read, out properties!);
                if (hr != 0 || properties == null)
                {
                    return string.IsNullOrWhiteSpace(fallbackName) ? deviceId : fallbackName;
                }

                var friendlyNameKey = PropertyKeys.PKEY_Device_FriendlyName;
                hr = properties.GetValue(ref friendlyNameKey, out var value);
                if (hr == 0)
                {
                    try
                    {
                        var friendlyName = value.GetString();
                        if (!string.IsNullOrWhiteSpace(friendlyName))
                        {
                            return friendlyName.Trim();
                        }
                    }
                    finally
                    {
                        value.Clear();
                    }
                }
            }
            finally
            {
                if (properties != null) Marshal.ReleaseComObject(properties);
                if (endpoint != null) Marshal.ReleaseComObject(endpoint);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }
        catch
        {
            // ignore and use fallback
        }

        return string.IsNullOrWhiteSpace(fallbackName) ? deviceId : fallbackName;
    }

    private static string ResolveOutputDeviceName(IMMDevice endpoint, string fallbackName)
    {
        IPropertyStore? properties = null;
        try
        {
            var hr = endpoint.OpenPropertyStore((int)StgmAccess.Read, out properties!);
            if (hr != 0 || properties == null)
            {
                return fallbackName;
            }

            var friendlyNameKey = PropertyKeys.PKEY_Device_FriendlyName;
            hr = properties.GetValue(ref friendlyNameKey, out var value);
            if (hr != 0)
            {
                return fallbackName;
            }

            try
            {
                var friendlyName = value.GetString();
                return string.IsNullOrWhiteSpace(friendlyName) ? fallbackName : friendlyName.Trim();
            }
            finally
            {
                value.Clear();
            }
        }
        catch
        {
            return fallbackName;
        }
        finally
        {
            if (properties != null) Marshal.ReleaseComObject(properties);
        }
    }

    public void Dispose()
    {
        _connectionService.DeviceConnected -= OnPlaybackDeviceConnected;
        _audioOutputDeviceWatcherService.DevicesChanged -= OnAudioOutputDevicesChanged;

        _resumeHotkeyCts?.Cancel();
        _resumeHotkeyCts?.Dispose();
        StopHotkeyCapturePolling();

        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterAllRegisteredHotkeys();
        }

        UnregisterFallbackHotkey(InputHotkeyId);
        UnregisterFallbackHotkey(OutputHotkeyId);
        DisposeKeyboardHook();
        DisposeMouseHook();
        foreach (var actionLock in _hotkeyActionLocks.Values)
        {
            actionLock.Dispose();
        }
        _hotkeyActionLocks.Clear();
        _restoreOutputRouteLock.Dispose();
        _userHotkeyRefreshLock.Dispose();

        if (_messageMonitor != null)
        {
            _messageMonitor.WindowMessageReceived -= OnWindowMessageReceived;
            _messageMonitor.Dispose();
            _messageMonitor = null;
        }

        lock (_outputDeviceListCacheGate)
        {
            _cachedOutputDevices = null;
            _cachedOutputDevicesAt = DateTimeOffset.MinValue;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static class NativeKeyboardMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
    }

    private static void RaiseDebugStatic(string message)
    {
        _log.Information("{DebugMessage}", message);
    }

    [Flags]
    private enum DeviceState : uint
    {
        Active = 0x00000001
    }

    private enum EDataFlow
    {
        eRender = 0
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    private enum StgmAccess
    {
        Read = 0x00000000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)]
        private ushort vt;

        [FieldOffset(8)]
        private IntPtr pointerValue;

        public string? GetString()
        {
            const ushort VtLpwstr = 31;
            if (vt == VtLpwstr && pointerValue != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(pointerValue);
            }

            return null;
        }

        public void Clear()
        {
            _ = NativeMethods.PropVariantClear(ref this);
        }
    }

    private static class NativeMethods
    {
        [DllImport("ole32.dll")]
        internal static extern int PropVariantClear(ref PROPVARIANT pvar);
    }

    private static class PropertyKeys
    {
        public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
        {
            fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            pid = 14
        };
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClientComObject;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0A74A4F3E3B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);
        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig]
        int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);
        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig]
        int Commit();
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat();
        [PreserveSig]
        int GetDeviceFormat();
        [PreserveSig]
        int ResetDeviceFormat();
        [PreserveSig]
        int SetDeviceFormat();
        [PreserveSig]
        int GetProcessingPeriod();
        [PreserveSig]
        int SetProcessingPeriod();
        [PreserveSig]
        int GetShareMode();
        [PreserveSig]
        int SetShareMode();
        [PreserveSig]
        int GetPropertyValue();
        [PreserveSig]
        int SetPropertyValue();
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);
        [PreserveSig]
        int SetEndpointVisibility();
    }
}








