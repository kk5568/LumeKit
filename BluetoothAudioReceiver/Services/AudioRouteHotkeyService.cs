using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Diagnostics;
using Serilog;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Windows.System;
using WinUIEx.Messaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using BluetoothAudioReceiver.Models.Application;

namespace BluetoothAudioReceiver.Services;

internal sealed class AudioRouteHotkeyService : IAudioRouteHotkeyService, IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AudioRouteHotkeyService));

    private const int InputHotkeyId = 0x2001;
    private const int OutputHotkeyId = 0x2002;
    private const int WmHotkey = 0x0312;

    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly INotificationService _notificationService;

    private readonly ConcurrentDictionary<int, (HOT_KEY_MODIFIERS Modifiers, uint Key)> _registeredHotkeys = [];
    private readonly ConcurrentDictionary<int, (HOT_KEY_MODIFIERS Modifiers, uint Key)> _fallbackHotkeys = [];
    private readonly ConcurrentDictionary<int, byte> _fallbackActiveKeys = [];
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _hotkeyActionLocks = [];
    private volatile string[] _inputTargetCandidates = [];
    private volatile string[] _outputTargetCandidates = [];
    private volatile bool _hotkeysTemporarilyDisabledForCapture;
    private volatile bool _suspendHotkeyActions;
    private CancellationTokenSource? _resumeHotkeyCts;
    private WindowMessageMonitor? _messageMonitor;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private IntPtr _windowHandle;
    private IntPtr _keyboardHookHandle;
    private LowLevelKeyboardProc? _keyboardHookProc;

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    public event Action<string>? DebugMessage;

    public AudioRouteHotkeyService(
        IAudioPlaybackConnectionService connectionService,
        IAppSettingsService appSettingsService,
        INotificationService notificationService)
    {
        _connectionService = connectionService;
        _appSettingsService = appSettingsService;
        _notificationService = notificationService;
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

        UpdateHotkeys(_appSettingsService.InputHotkey, _appSettingsService.OutputHotkey);
    }

    public void UpdateHotkeys(string inputHotkey, string outputHotkey)
    {
        UpdateInputHotkey(inputHotkey);
        UpdateOutputHotkey(outputHotkey);
    }

    public void UpdateInputHotkey(string inputHotkey)
    {
        RegisterOrUpdateHotkey(InputHotkeyId, inputHotkey);
    }

    public void UpdateOutputHotkey(string outputHotkey)
    {
        RegisterOrUpdateHotkey(OutputHotkeyId, outputHotkey);
    }

    public void SetHotkeyCaptureSuspended(bool suspended)
    {
        _suspendHotkeyActions = suspended;
        if (suspended)
        {
            TemporarilyDisableGlobalHotkeysForCapture();
            _resumeHotkeyCts?.Cancel();
            _resumeHotkeyCts = new CancellationTokenSource();
            var token = _resumeHotkeyCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), token);
                    if (!token.IsCancellationRequested)
                    {
                        _suspendHotkeyActions = false;
                        RestoreGlobalHotkeysAfterCapture();
                        RaiseDebug("录制暂停超时，已自动恢复全局热键触发");
                    }
                }
                catch (TaskCanceledException)
                {
                    // ignore
                }
            }, token);
        }
        else
        {
            _resumeHotkeyCts?.Cancel();
            RestoreGlobalHotkeysAfterCapture();
        }
        RaiseDebug(suspended ? "已暂停全局热键触发（录制模式）" : "已恢复全局热键触发");
    }

    private void TemporarilyDisableGlobalHotkeysForCapture()
    {
        if (_windowHandle == IntPtr.Zero || _hotkeysTemporarilyDisabledForCapture)
        {
            return;
        }

        var hwnd = new HWND(_windowHandle);
        _ = PInvoke.UnregisterHotKey(hwnd, InputHotkeyId);
        _ = PInvoke.UnregisterHotKey(hwnd, OutputHotkeyId);
        _registeredHotkeys.Clear();
        _fallbackHotkeys.Clear();
        _fallbackActiveKeys.Clear();
        _hotkeysTemporarilyDisabledForCapture = true;
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
        RaiseDebug("录制模式：已恢复全局热键注册");
    }

    public void UpdateTargets(string? inputDeviceId, string? outputDeviceId)
    {
        _ = _appSettingsService.SetPreferredInputDeviceIdAsync(inputDeviceId ?? string.Empty);
        _ = _appSettingsService.SetPreferredOutputDeviceIdAsync(outputDeviceId ?? string.Empty);
    }

    public void UpdateTargetCandidates(IReadOnlyList<string>? inputDeviceIds, IReadOnlyList<string>? outputDeviceIds)
    {
        _inputTargetCandidates = NormalizeDeviceIdList(inputDeviceIds);
        _outputTargetCandidates = NormalizeDeviceIdList(outputDeviceIds);
    }

    public async Task<IReadOnlyList<AudioOutputDeviceItem>> GetOutputDevicesAsync()
    {
        try
        {
            var list = new List<AudioOutputDeviceItem>();
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
                    Name = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name
                });
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to enumerate output audio devices");
            return [];
        }
    }

    public async Task<bool> TriggerInputRouteAsync()
    {
        try
        {
            var allInputDevices = (await _connectionService.FindAllAudioDevicesAsync())
                .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(d => string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var candidateInputIds = _inputTargetCandidates;
            var inputDevices = candidateInputIds.Length == 0
                ? allInputDevices
                : allInputDevices
                    .Where(d => candidateInputIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
                    .ToList();

            if (inputDevices.Count == 0)
            {
                RaiseDebug(candidateInputIds.Length == 0
                    ? "输入切换失败：未发现输入设备"
                    : "输入切换失败：已选输入设备不可用");
                _notificationService.Show("\uE946", candidateInputIds.Length == 0 ? "未发现输入蓝牙设备" : "已选输入设备不可用");
                return false;
            }

            var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
            var currentIndex = inputDevices.FindIndex(d =>
                string.Equals(d.Id, preferredInputId, StringComparison.OrdinalIgnoreCase));

            if (currentIndex < 0)
            {
                var connectedId = _connectionService.ConnectedDevices
                    .Select(d => d.Id)
                    .FirstOrDefault(id => inputDevices.Any(dev => string.Equals(dev.Id, id, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrWhiteSpace(connectedId))
                {
                    currentIndex = inputDevices.FindIndex(d => string.Equals(d.Id, connectedId, StringComparison.OrdinalIgnoreCase));
                }
            }

            var nextIndex = currentIndex >= 0
                ? (currentIndex + 1) % inputDevices.Count
                : 0;
            var nextDevice = inputDevices[nextIndex];

            var connectedSnapshot = _connectionService.ConnectedDevices.ToList();
            foreach (var connected in connectedSnapshot.Where(c => !string.Equals(c.Id, nextDevice.Id, StringComparison.OrdinalIgnoreCase)))
            {
                await _connectionService.DisconnectAsync(connected);
            }

            var alreadyConnected = _connectionService.ConnectedDevices.Any(d =>
                string.Equals(d.Id, nextDevice.Id, StringComparison.OrdinalIgnoreCase));
            var ok = alreadyConnected || await _connectionService.ConnectAsync(nextDevice);
            if (!ok)
            {
                RaiseDebug("输入切换失败：目标设备连接失败");
                _notificationService.Show("\uE783", "输入切换失败");
                return false;
            }

            await _appSettingsService.SetPreferredInputDeviceIdAsync(nextDevice.Id);
            var name = string.IsNullOrWhiteSpace(nextDevice.Name) ? "输入设备" : nextDevice.Name;
            RaiseDebug($"输入切换成功：{name}");
            _notificationService.Show(ResolveDeviceTypeGlyph(name), name, "音频已连接");
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TriggerInputRouteAsync failed");
            RaiseDebug($"输入切换异常：{ex.Message}");
            _notificationService.Show("\uE783", "输入切换失败");
            return false;
        }
    }

    public async Task<bool> TriggerOutputRouteAsync()
    {
        var allOutputDevices = await GetOutputDevicesAsync();
        var candidateOutputIds = _outputTargetCandidates;
        var outputDevices = candidateOutputIds.Length == 0
            ? allOutputDevices
            : allOutputDevices
                .Where(d => candidateOutputIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();

        if (outputDevices.Count == 0)
        {
            RaiseDebug(candidateOutputIds.Length == 0
                ? "输出切换失败：未发现输出设备"
                : "输出切换失败：已选输出设备不可用");
            _notificationService.Show("\uE946", candidateOutputIds.Length == 0 ? "未发现输出音频设备" : "已选输出设备不可用");
            return false;
        }

        var candidates = outputDevices
            .Select(d => new
            {
                Device = d,
                EndpointId = NormalizeOutputEndpointId(d.Id)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.EndpointId))
            .GroupBy(x => x.EndpointId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (candidates.Count == 0)
        {
            RaiseDebug("输出切换失败：输出端点ID无效");
            _notificationService.Show("\uE783", "输出切换失败");
            return false;
        }

        var currentDefaultId = NormalizeOutputEndpointId(GetCurrentDefaultOutputEndpointId() ?? string.Empty);
        var currentIndex = candidates.FindIndex(c =>
            string.Equals(c.EndpointId, currentDefaultId, StringComparison.OrdinalIgnoreCase));

        var nextIndex = currentIndex >= 0
            ? (currentIndex + 1) % candidates.Count
            : 0;

        var next = candidates[nextIndex];
        var ok = SetDefaultAudioEndpoint(next.EndpointId);
        if (!ok && !string.Equals(next.EndpointId, next.Device.Id, StringComparison.Ordinal))
        {
            ok = SetDefaultAudioEndpoint(next.Device.Id);
        }

        if (ok)
        {
            await _appSettingsService.SetPreferredOutputDeviceIdAsync(next.Device.Id);
            var name = string.IsNullOrWhiteSpace(next.Device.Name) ? "输出设备" : next.Device.Name;
            RaiseDebug($"输出切换成功：{name}");
            _notificationService.Show(ResolveDeviceTypeGlyph(name), name, "音频已连接");
        }
        else
        {
            RaiseDebug("输出切换失败：SetDefaultEndpoint 返回失败");
            _notificationService.Show("\uE783", "输出切换失败");
        }

        return ok;
    }

    private static string NormalizeOutputEndpointId(string configuredId)
    {
        if (string.IsNullOrWhiteSpace(configuredId))
        {
            return configuredId;
        }

        const string endpointPrefix = "{0.0.0.00000000}.";
        var trimmed = configuredId.Trim();

        if (trimmed.StartsWith(endpointPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var startIndex = trimmed.IndexOf(endpointPrefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return trimmed;
        }

        var endIndex = trimmed.IndexOf('}', startIndex + endpointPrefix.Length);
        if (endIndex < 0)
        {
            return trimmed;
        }

        return trimmed.Substring(startIndex, endIndex - startIndex + 1);
    }

    private static string ResolveDeviceTypeGlyph(string deviceName)
    {
        var normalizedName = (deviceName ?? string.Empty).ToLowerInvariant();

        if (normalizedName.Contains("headphone") || normalizedName.Contains("headset") || normalizedName.Contains("耳机"))
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

    private void RegisterOrUpdateHotkey(int hotkeyId, string hotkeyText)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var hwnd = new HWND(_windowHandle);
        _ = PInvoke.UnregisterHotKey(hwnd, hotkeyId);
        _registeredHotkeys.TryRemove(hotkeyId, out _);
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
            return;
        }

        if (PInvoke.RegisterHotKey(hwnd, hotkeyId, modifiers, key))
        {
            _registeredHotkeys[hotkeyId] = (modifiers, key);
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
                _log.Information("Registered hotkey (fallback) {HotkeyId}: {HotkeyText}", hotkeyId, hotkeyText);
                RaiseDebug($"已注册快捷键(降级)(id={hotkeyId}): {hotkeyText}");
                return;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorHotkeyAlreadyRegistered)
            {
                RegisterFallbackHotkey(hotkeyId, fallbackModifiers, key, hotkeyText);
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
            .Replace('\uFF0B', '+')
            .Trim();
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

    private void DisposeKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            _ = NativeKeyboardMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                TryTriggerFallbackHotkeys(info.vkCode);
            }
            else if (message == WmKeyUp || message == WmSysKeyUp)
            {
                ReleaseFallbackHotkeys(info.vkCode);
            }
        }

        return NativeKeyboardMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private void TryTriggerFallbackHotkeys(uint pressedVk)
    {
        if (_suspendHotkeyActions)
        {
            return;
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
            return;
        }

        var resolvedHotkeyId = ResolveActionHotkeyId(matchedModifiers.Value, pressedVk);
        if (resolvedHotkeyId is null)
        {
            return;
        }

        // SoundSwitch-style behavior:
        // if a combo has a successfully registered WM_HOTKEY binding,
        // let WM_HOTKEY path own the execution to avoid double trigger.
        if (_registeredHotkeys.ContainsKey(resolvedHotkeyId.Value))
        {
            return;
        }

        if (!_fallbackActiveKeys.TryAdd(resolvedHotkeyId.Value, 0))
        {
            return;
        }

        QueueHotkeyAction(resolvedHotkeyId.Value, $"HOOK(vk={pressedVk})");
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
        if (hotkeyId != InputHotkeyId && hotkeyId != OutputHotkeyId)
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
        if (!enqueued)
        {
            _ = ExecuteQueuedHotkeyActionAsync(hotkeyId, source);
        }
    }

    private async Task ExecuteQueuedHotkeyActionAsync(int hotkeyId, string source)
    {
        var actionLock = _hotkeyActionLocks.GetOrAdd(hotkeyId, _ => new SemaphoreSlim(1, 1));
        await actionLock.WaitAsync();
        try
        {
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
            var action = hotkeyId == InputHotkeyId ? "输入" : "输出";
            RaiseDebug($"开始执行{action}热键，source={source}");

            var triggerTask = hotkeyId == InputHotkeyId
                ? TriggerInputRouteAsync()
                : TriggerOutputRouteAsync();
            var completed = await Task.WhenAny(triggerTask, Task.Delay(TimeSpan.FromSeconds(15)));
            var ok = completed == triggerTask && await triggerTask;
            if (completed != triggerTask)
            {
                RaiseDebug($"{action}热键执行超时，已释放触发锁");
            }

            RaiseDebug($"{action}热键执行{(ok ? "成功" : "失败")}，source={source}");
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

    private void ReleaseFallbackHotkeys(uint releasedVk)
    {
        foreach (var pair in _fallbackHotkeys)
        {
            if (pair.Value.Key == releasedVk)
            {
                _fallbackActiveKeys.TryRemove(pair.Key, out _);
            }
        }
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
            config = (IPolicyConfig)new PolicyConfigClientComObject();

            var hr1 = config.SetDefaultEndpoint(deviceId, ERole.eConsole);
            var hr2 = config.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            var hr3 = config.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            RaiseDebug($"SetDefaultEndpoint: console=0x{hr1:X8}, multimedia=0x{hr2:X8}, comm=0x{hr3:X8}");

            return hr1 == 0 && hr2 == 0 && hr3 == 0;
        }
        catch (Exception ex)
        {
            RaiseDebug($"SetDefaultEndpoint 异常：{ex.Message}");
            return false;
        }
        finally
        {
            if (config != null) Marshal.ReleaseComObject(config);
        }
    }

    private void RaiseDebug(string message)
    {
        _log.Information("{DebugMessage}", message);

        try
        {
            DebugMessage?.Invoke(message);
        }
        catch
        {
            // ignore listener errors
        }
    }

    public void Dispose()
    {
        _resumeHotkeyCts?.Cancel();
        _resumeHotkeyCts?.Dispose();

        if (_windowHandle != IntPtr.Zero)
        {
            var hwnd = new HWND(_windowHandle);
            _ = PInvoke.UnregisterHotKey(hwnd, InputHotkeyId);
            _ = PInvoke.UnregisterHotKey(hwnd, OutputHotkeyId);
        }

        UnregisterFallbackHotkey(InputHotkeyId);
        UnregisterFallbackHotkey(OutputHotkeyId);
        DisposeKeyboardHook();
        foreach (var actionLock in _hotkeyActionLocks.Values)
        {
            actionLock.Dispose();
        }
        _hotkeyActionLocks.Clear();

        if (_messageMonitor != null)
        {
            _messageMonitor.WindowMessageReceived -= OnWindowMessageReceived;
            _messageMonitor.Dispose();
            _messageMonitor = null;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static class NativeKeyboardMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);
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
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState dwStateMask, out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0A74A4F3E3B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        int Commit();
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat();
        int GetDeviceFormat();
        int ResetDeviceFormat();
        int SetDeviceFormat();
        int GetProcessingPeriod();
        int SetProcessingPeriod();
        int GetShareMode();
        int SetShareMode();
        int GetPropertyValue();
        int SetPropertyValue();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);
        int SetEndpointVisibility();
    }
}




