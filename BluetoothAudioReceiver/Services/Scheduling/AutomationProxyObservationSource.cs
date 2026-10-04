using System.Runtime.InteropServices;
using Serilog;
using Windows.Networking.Connectivity;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationProxyObservationSource
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationProxyObservationSource));

    private const string InternetSettingsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string InternetSettingsConnectionsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";

    private readonly Func<bool> _isLocked;
    private readonly Func<string, Guid, Task> _requestObservationRefreshAsync;

    public AutomationProxyObservationSource(
        Func<bool> isLocked,
        Func<string, Guid, Task> requestObservationRefreshAsync)
    {
        _isLocked = isLocked;
        _requestObservationRefreshAsync = requestObservationRefreshAsync;
    }

    public async Task RunProxyRegistryWatchAsync(CancellationToken token)
    {
        await Task.WhenAll(
            RunProxyRegistryWatchPathAsync(InternetSettingsRegistryPath, token),
            RunProxyRegistryWatchPathAsync(InternetSettingsConnectionsRegistryPath, token));
    }

    private async Task RunProxyRegistryWatchPathAsync(string registryPath, CancellationToken token)
    {
        IntPtr keyHandle = IntPtr.Zero;
        IntPtr eventHandle = IntPtr.Zero;

        try
        {
            if (!TryOpenRegistryKey(registryPath, out keyHandle))
            {
                _log.Warning("Automation proxy registry watch failed to open key. RegistryPath={RegistryPath}", registryPath);
                return;
            }

            eventHandle = CreateEvent(IntPtr.Zero, false, false, null);
            if (eventHandle == IntPtr.Zero)
            {
                _log.Warning("Automation proxy registry watch failed to create event handle");
                return;
            }

            while (!token.IsCancellationRequested)
            {
                var notifyResult = RegNotifyChangeKeyValue(
                        keyHandle,
                        true,
                        RegChangeNotifyFilter.Key | RegChangeNotifyFilter.Attribute | RegChangeNotifyFilter.Value,
                        eventHandle,
                        true);
                if (notifyResult != 0)
                {
                    _log.Warning("Automation proxy registry watch failed to arm notification. RegistryPath={RegistryPath}, ErrorCode={ErrorCode}", registryPath, notifyResult);
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    continue;
                }

                var waitTask = Task.Run(() => WaitForSingleObject(eventHandle, Timeout.Infinite), token);
                var completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout.InfiniteTimeSpan, token));
                if (completedTask != waitTask)
                {
                    break;
                }

                var waitResult = await waitTask;
                if (waitResult == WaitObjectResult.WaitFailed)
                {
                    _log.Warning("Automation proxy registry watch wait failed. RegistryPath={RegistryPath}, ErrorCode={ErrorCode}", registryPath, Marshal.GetLastWin32Error());
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    continue;
                }

                if (_isLocked())
                {
                    continue;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), token);
                await _requestObservationRefreshAsync("proxy-registry-changed", Guid.NewGuid());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Automation proxy registry watch failed. RegistryPath={RegistryPath}", registryPath);
        }
        finally
        {
            if (eventHandle != IntPtr.Zero)
            {
                CloseHandle(eventHandle);
            }

            if (keyHandle != IntPtr.Zero)
            {
                RegCloseKey(keyHandle);
            }
        }
    }

    private static string GetCurrentNetworkName()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                return string.Empty;
            }

            return profile.ProfileName ?? string.Empty;
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
            if (!WinHttpGetIEProxyConfigForCurrentUser(out var proxyConfig))
            {
                return false;
            }

            try
            {
                var autoConfigUrl = Marshal.PtrToStringUni(proxyConfig.lpszAutoConfigUrl);
                var proxy = Marshal.PtrToStringUni(proxyConfig.lpszProxy);
                return proxyConfig.fAutoDetect ||
                       !string.IsNullOrWhiteSpace(autoConfigUrl) ||
                       !string.IsNullOrWhiteSpace(proxy);
            }
            finally
            {
                FreeProxyConfig(proxyConfig);
            }
        }
        catch
        {
            return false;
        }
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

    private static bool TryOpenRegistryKey(string registryPath, out IntPtr keyHandle)
    {
        const int KEY_NOTIFY = 0x0010;
        var result = RegOpenKeyEx(HKEY_CURRENT_USER, registryPath, 0, KEY_NOTIFY, out keyHandle);
        return result == 0 && keyHandle != IntPtr.Zero;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyEx(UIntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        IntPtr hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        RegChangeNotifyFilter dwNotifyFilter,
        IntPtr hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern WaitObjectResult WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly UIntPtr HKEY_CURRENT_USER = (UIntPtr)0x80000001u;

    [Flags]
    private enum RegChangeNotifyFilter : uint
    {
        Name = 0x00000001,
        Attribute = 0x00000002,
        Value = 0x00000004,
        Security = 0x00000008,
        Key = Name
    }

    private enum WaitObjectResult : uint
    {
        WaitObject0 = 0x00000000,
        WaitTimeout = 0x00000102,
        WaitFailed = 0xFFFFFFFF
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINHTTP_CURRENT_USER_IE_PROXY_CONFIG
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAutoDetect;

        public IntPtr lpszAutoConfigUrl;
        public IntPtr lpszProxy;
        public IntPtr lpszProxyBypass;
    }
}
