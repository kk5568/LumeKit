using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.Networking.Connectivity;
using Windows.System.Power;

namespace BluetoothAudioReceiver.Services;

internal enum AutomationObservationGetterKind
{
    Official,
    Listener,
    Polling
}

internal interface IAutomationObservationGetter
{
    AutomationObservationGetterKind Kind { get; }

    bool CanGet(ObservationInfoKind infoKind);

    Task<AutomationDynamicContext> CaptureAsync(
        AutomationDynamicContext current,
        IReadOnlyCollection<ObservationInfoKind> items,
        CancellationToken token);
}

internal sealed class AutomationObservationGetterSelector
{
    private static readonly AutomationObservationGetterKind[] Priority =
    [
        AutomationObservationGetterKind.Official,
        AutomationObservationGetterKind.Listener,
        AutomationObservationGetterKind.Polling
    ];

    private readonly IReadOnlyList<IAutomationObservationGetter> _getters;

    public AutomationObservationGetterSelector(IEnumerable<IAutomationObservationGetter> getters)
    {
        _getters = getters.ToArray();
    }

    public IReadOnlyList<ObservationGetterPlan> BuildPlan(IReadOnlyList<ObservationInfoKind> requestedItems)
    {
        var groups = new Dictionary<IAutomationObservationGetter, HashSet<ObservationInfoKind>>();

        foreach (var item in requestedItems.Distinct())
        {
            var getter = Resolve(item);
            if (getter is null)
            {
                continue;
            }

            if (!groups.TryGetValue(getter, out var items))
            {
                items = [];
                groups[getter] = items;
            }

            items.Add(item);
        }

        return groups
            .OrderBy(group => Array.IndexOf(Priority, group.Key.Kind))
            .Select(group => new ObservationGetterPlan(group.Key, group.Value.ToArray()))
            .ToArray();
    }

    private IAutomationObservationGetter? Resolve(ObservationInfoKind item)
        => Priority
            .SelectMany(kind => _getters.Where(getter => getter.Kind == kind))
            .FirstOrDefault(getter => getter.CanGet(item));
}

internal sealed record ObservationGetterPlan(
    IAutomationObservationGetter Getter,
    IReadOnlyCollection<ObservationInfoKind> Items);

internal sealed class OfficialAutomationObservationGetter : IAutomationObservationGetter
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly IDisplayProvider _displayProvider;
    private readonly AutoDayNightService _autoDayNightService;

    public OfficialAutomationObservationGetter(
        IThemeSelectorService themeSelectorService,
        IAudioRouteHotkeyService audioRouteHotkeyService,
        IDisplayProvider displayProvider,
        AutoDayNightService autoDayNightService)
    {
        _themeSelectorService = themeSelectorService;
        _audioRouteHotkeyService = audioRouteHotkeyService;
        _displayProvider = displayProvider;
        _autoDayNightService = autoDayNightService;
    }

    public AutomationObservationGetterKind Kind => AutomationObservationGetterKind.Official;

    public bool CanGet(ObservationInfoKind infoKind)
        => infoKind is ObservationInfoKind.PowerState
            or ObservationInfoKind.ThemeMode
            or ObservationInfoKind.Brightness
            or ObservationInfoKind.NetworkState
            or ObservationInfoKind.ProxyState
            or ObservationInfoKind.AudioRoute;

    public async Task<AutomationDynamicContext> CaptureAsync(
        AutomationDynamicContext current,
        IReadOnlyCollection<ObservationInfoKind> items,
        CancellationToken token)
    {
        var result = current;

        if (items.Contains(ObservationInfoKind.PowerState))
        {
            var powerStatus = PowerManager.PowerSupplyStatus;
            var batteryPercent = PowerManager.RemainingChargePercent;
            result = result with
            {
                BatteryPercent = batteryPercent is >= 0 and <= 100 ? batteryPercent : null,
                IsOnBattery = powerStatus == PowerSupplyStatus.NotPresent,
                IsCharging = powerStatus == PowerSupplyStatus.Inadequate || powerStatus == PowerSupplyStatus.Adequate
            };
        }

        if (items.Contains(ObservationInfoKind.ThemeMode))
        {
            var actualTheme = _themeSelectorService.GetActualTheme();
            result = result with
            {
                IsDarkMode = actualTheme == ElementTheme.Dark,
                Sunrise = _autoDayNightService.CalculatedSunrise,
                Sunset = _autoDayNightService.CalculatedSunset,
                SunTimeSource = _autoDayNightService.CalculatedSunrise is not null && _autoDayNightService.CalculatedSunset is not null
                    ? "AutoDayNightService"
                    : null
            };
        }

        if (items.Contains(ObservationInfoKind.Brightness))
        {
            result = result with { ScreenBrightnessPercent = await TryGetScreenBrightnessAsync(token) };
        }

        if (items.Contains(ObservationInfoKind.NetworkState))
        {
            var networkName = GetCurrentNetworkName();
            result = result with { NetworkName = string.IsNullOrWhiteSpace(networkName) ? null : networkName };
        }

        if (items.Contains(ObservationInfoKind.ProxyState))
        {
            var proxyState = GetProxyState();
            result = result with
            {
                IsProxyEnabled = proxyState.Enabled,
                ProxyMode = proxyState.Mode
            };
        }

        if (items.Contains(ObservationInfoKind.AudioRoute))
        {
            var defaultOutputAudioDeviceId = await _audioRouteHotkeyService.GetCurrentDefaultOutputEndpointIdAsync();
            result = result with
            {
                DefaultOutputAudioDeviceId = string.IsNullOrWhiteSpace(defaultOutputAudioDeviceId) ? null : defaultOutputAudioDeviceId
            };
        }

        return result;
    }

    private async Task<double?> TryGetScreenBrightnessAsync(CancellationToken token)
    {
        try
        {
            return await _displayProvider.GetBrightnessAsync(token);
        }
        catch
        {
            return null;
        }
    }

    private static string GetCurrentNetworkName()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return profile?.ProfileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool GetSystemProxyEnabled()
    {
        try
        {
            if (WinHttpGetIEProxyConfigForCurrentUser(out var proxyConfig))
            {
                try
                {
                    var winHttpAutoConfigUrl = Marshal.PtrToStringUni(proxyConfig.lpszAutoConfigUrl);
                    var proxy = Marshal.PtrToStringUni(proxyConfig.lpszProxy);
                    return proxyConfig.fAutoDetect ||
                           !string.IsNullOrWhiteSpace(winHttpAutoConfigUrl) ||
                           !string.IsNullOrWhiteSpace(proxy);
                }
                finally
                {
                    FreeProxyConfig(proxyConfig);
                }
            }

            using var internetSettings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            var proxyEnable = internetSettings?.GetValue("ProxyEnable") is int enabled && enabled != 0;
            var proxyServer = internetSettings?.GetValue("ProxyServer")?.ToString();
            var autoConfigUrl = internetSettings?.GetValue("AutoConfigURL")?.ToString();
            if (internetSettings is not null)
            {
                return proxyEnable && !string.IsNullOrWhiteSpace(proxyServer) ||
                       !string.IsNullOrWhiteSpace(autoConfigUrl);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static ProxyState GetProxyState()
    {
        if (IsTunProxyEnabled())
        {
            return new ProxyState(true, "tun");
        }

        return GetSystemProxyEnabled()
            ? new ProxyState(true, "system")
            : new ProxyState(false, null);
    }

    private static bool IsTunProxyEnabled()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    IsTunAdapterName(adapter.Name, adapter.Description));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTunAdapterName(string? name, string? description)
    {
        var text = $"{name} {description}";
        return text.Contains("tun", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("wintun", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("clash", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("mihomo", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sing-box", StringComparison.OrdinalIgnoreCase);
    }

    private static void FreeProxyConfig(WINHTTP_CURRENT_USER_IE_PROXY_CONFIG proxyConfig)
    {
        if (proxyConfig.lpszAutoConfigUrl != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(proxyConfig.lpszAutoConfigUrl);
        }

        if (proxyConfig.lpszProxy != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(proxyConfig.lpszProxy);
        }

        if (proxyConfig.lpszProxyBypass != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(proxyConfig.lpszProxyBypass);
        }
    }

    [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpGetIEProxyConfigForCurrentUser(out WINHTTP_CURRENT_USER_IE_PROXY_CONFIG pProxyConfig);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINHTTP_CURRENT_USER_IE_PROXY_CONFIG
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAutoDetect;

        public IntPtr lpszAutoConfigUrl;
        public IntPtr lpszProxy;
        public IntPtr lpszProxyBypass;
    }

    private sealed record ProxyState(bool Enabled, string? Mode);
}

internal sealed class ListenerAutomationObservationGetter : IAutomationObservationGetter
{
    private readonly IAudioPlaybackConnectionService _audioPlaybackConnectionService;
    private readonly BluetoothRadioMonitorService _bluetoothRadioMonitorService;

    public ListenerAutomationObservationGetter(
        IAudioPlaybackConnectionService audioPlaybackConnectionService,
        BluetoothRadioMonitorService bluetoothRadioMonitorService)
    {
        _audioPlaybackConnectionService = audioPlaybackConnectionService;
        _bluetoothRadioMonitorService = bluetoothRadioMonitorService;
    }

    public AutomationObservationGetterKind Kind => AutomationObservationGetterKind.Listener;

    public bool CanGet(ObservationInfoKind infoKind)
        => infoKind is ObservationInfoKind.ForegroundWindow
            or ObservationInfoKind.BluetoothState
            or ObservationInfoKind.BluetoothRadioState;

    public Task<AutomationDynamicContext> CaptureAsync(
        AutomationDynamicContext current,
        IReadOnlyCollection<ObservationInfoKind> items,
        CancellationToken token)
    {
        var result = current;

        if (items.Contains(ObservationInfoKind.ForegroundWindow))
        {
            var processName = AutomationObservationWin32.GetForegroundProcessName();
            var windowTitle = AutomationObservationWin32.GetForegroundWindowTitle();
            result = result with
            {
                ForegroundProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName,
                ForegroundWindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle,
                IsFullscreenAppActive = AutomationObservationWin32.IsForegroundFullscreen(),
                IsGameLikelyRunning = AutomationGameRecognitionCatalog.MatchesKnownGame(processName) ||
                                      AutomationGameRecognitionCatalog.MatchesKnownGame(windowTitle)
            };
        }

        if (items.Contains(ObservationInfoKind.BluetoothState))
        {
            result = result with
            {
                ConnectedBluetoothDevices = _audioPlaybackConnectionService.ConnectedDevices
                    .Select(device => device.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        if (items.Contains(ObservationInfoKind.BluetoothRadioState))
        {
            result = result with
            {
                IsBluetoothRadioOn = _bluetoothRadioMonitorService.CurrentState switch
                {
                    Windows.Devices.Radios.RadioState.On => true,
                    Windows.Devices.Radios.RadioState.Off => false,
                    _ => null
                }
            };
        }

        return Task.FromResult(result);
    }
}

internal sealed class PollingAutomationObservationGetter : IAutomationObservationGetter
{
    public AutomationObservationGetterKind Kind => AutomationObservationGetterKind.Polling;

    public bool CanGet(ObservationInfoKind infoKind)
        => infoKind is ObservationInfoKind.ForegroundWindow;

    public Task<AutomationDynamicContext> CaptureAsync(
        AutomationDynamicContext current,
        IReadOnlyCollection<ObservationInfoKind> items,
        CancellationToken token)
    {
        if (!items.Contains(ObservationInfoKind.ForegroundWindow))
        {
            return Task.FromResult(current);
        }

        var processName = AutomationObservationWin32.GetForegroundProcessName();
        var windowTitle = AutomationObservationWin32.GetForegroundWindowTitle();
        return Task.FromResult(current with
        {
            ForegroundProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName,
            ForegroundWindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle,
            IsFullscreenAppActive = AutomationObservationWin32.IsForegroundFullscreen(),
            IsGameLikelyRunning = AutomationGameRecognitionCatalog.MatchesKnownGame(processName) ||
                                  AutomationGameRecognitionCatalog.MatchesKnownGame(windowTitle)
        });
    }
}

internal static class AutomationObservationWin32
{
    public static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            return Process.GetProcessById((int)pid).ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GetForegroundWindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new string('\0', length + 1);
            _ = GetWindowText(hwnd, buffer, buffer.Length);
            return buffer.TrimEnd('\0');
        }
        catch
        {
            return string.Empty;
        }
    }

    public static int? GetForegroundProcessId()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out var processId);
            return processId == 0 ? null : (int)processId;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsForegroundFullscreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return false;
            }

            var hMonitor = MonitorFromWindow(hwnd, 2u);
            if (hMonitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MONITORINFO
            {
                cbSize = Marshal.SizeOf<MONITORINFO>()
            };

            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                return false;
            }

            var windowWidth = rect.Right - rect.Left;
            var windowHeight = rect.Bottom - rect.Top;
            var monitorWidth = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left;
            var monitorHeight = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;

            return Math.Abs(windowWidth - monitorWidth) <= 2 &&
                   Math.Abs(windowHeight - monitorHeight) <= 2 &&
                   rect.Left == monitorInfo.rcMonitor.Left &&
                   rect.Top == monitorInfo.rcMonitor.Top;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, string lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
