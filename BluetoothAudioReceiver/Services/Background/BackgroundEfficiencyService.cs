using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Services.BackgroundEfficiency;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class BackgroundEfficiencyService : IBackgroundEfficiencyService, IBackgroundDispatchMessageHandler, IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(BackgroundEfficiencyService));
    private readonly object _lockObject = new();
    private CancellationTokenSource _houseKeepingCancellationTokenSource = new();
    private CancellationTokenSource? _foregroundDebounceCancellationTokenSource;
    private CancellationTokenSource? _catchUpCancellationTokenSource;

    private readonly IAppSettingsService _settingsService;
    private const string UwpFrameHostApp = "ApplicationFrameHost.exe";

    private readonly IntPtr _pThrottleOn;
    private readonly IntPtr _pThrottleOff;
    private readonly int _szControlBlock;

    private uint _pendingProcPid;
    private string _pendingProcName = string.Empty;
    private bool _isInitialized;
    private bool _pauseThrottling;
    private IntPtr _lastQueuedForegroundWindow;

    private static readonly TimeSpan ForegroundDebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CatchUpDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HouseKeepingInterval = TimeSpan.FromMinutes(15);

    public BackgroundEfficiencyService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        _szControlBlock = Marshal.SizeOf<BackgroundEfficiencyWin32Api.ProcessPowerThrottlingState>();
        _pThrottleOn = Marshal.AllocHGlobal(_szControlBlock);
        _pThrottleOff = Marshal.AllocHGlobal(_szControlBlock);

        BackgroundEfficiencyWin32Api.ProcessPowerThrottlingState throttleState = new()
        {
            Version = BackgroundEfficiencyWin32Api.ProcessPowerThrottlingState.CurrentVersion,
            ControlMask = BackgroundEfficiencyWin32Api.ProcessorPowerThrottlingFlags.ProcessPowerThrottlingExecutionSpeed,
            StateMask = BackgroundEfficiencyWin32Api.ProcessorPowerThrottlingFlags.ProcessPowerThrottlingExecutionSpeed,
        };

        BackgroundEfficiencyWin32Api.ProcessPowerThrottlingState unthrottleState = new()
        {
            Version = BackgroundEfficiencyWin32Api.ProcessPowerThrottlingState.CurrentVersion,
            ControlMask = BackgroundEfficiencyWin32Api.ProcessorPowerThrottlingFlags.ProcessPowerThrottlingExecutionSpeed,
            StateMask = BackgroundEfficiencyWin32Api.ProcessorPowerThrottlingFlags.None,
        };

        Marshal.StructureToPtr(throttleState, _pThrottleOn, false);
        Marshal.StructureToPtr(unthrottleState, _pThrottleOff, false);
    }

    public ThrottleStatus ThrottleStatus { get; private set; } = ThrottleStatus.Stopped;

    public bool PauseThrottling
    {
        get => _pauseThrottling;
        set
        {
            lock (_lockObject)
            {
                if (_pauseThrottling != value)
                {
                    _pauseThrottling = value;
                    UpdateThrottleStatusAndNotify();
                }
            }
        }
    }

    public IReadOnlySet<string> ProcessWhitelist { get; private set; } = new HashSet<string>();
    public IReadOnlySet<string> ProcessBlacklist { get; private set; } = new HashSet<string>();

    private IReadOnlySet<string> WildcardProcessWhitelist { get; set; } = new HashSet<string>();
    private IReadOnlySet<string> WildcardProcessBlacklist { get; set; } = new HashSet<string>();

    public event EventHandler<ThrottleStatus>? ThrottleStatusChanged;

    public void Initialize()
    {
        lock (_lockObject)
        {
            if (_isInitialized)
            {
                return;
            }

            ApplyProcessWhitelist(_settingsService.BackgroundEfficiencyProcessWhitelist);
            ApplyProcessBlacklist(_settingsService.BackgroundEfficiencyProcessBlacklist);
            UpdateThrottleStatusAndNotify();
            _isInitialized = true;
        }
    }

    public void ApplyAndSaveProcessWhitelist(string processWhitelistString)
    {
        lock (_lockObject)
        {
            ApplyProcessWhitelist(processWhitelistString);
            _settingsService.SetBackgroundEfficiencyProcessWhitelistAsync(processWhitelistString).GetAwaiter().GetResult();
            _log.Information("ProcessWhitelist saved");
        }
    }

    public void ApplyAndSaveProcessBlacklist(string processBlacklistString)
    {
        lock (_lockObject)
        {
            ApplyProcessBlacklist(processBlacklistString);
            _settingsService.SetBackgroundEfficiencyProcessBlacklistAsync(processBlacklistString).GetAwaiter().GetResult();
            _log.Information("ProcessBlacklist saved");
        }
    }

    public void ApplyProcessWhitelist(string processWhitelistString)
    {
        lock (_lockObject)
        {
            var previousThrottleStatus = ThrottleStatus;

            if (previousThrottleStatus != ThrottleStatus.Stopped)
            {
                StopThrottling(previousThrottleStatus);
            }

            (HashSet<string> processWhitelist, HashSet<string> wildcardProcessWhitelist) = ParseProcessList(processWhitelistString);
#if DEBUG
            processWhitelist.Add("devenv.exe");
#endif
            ProcessWhitelist = processWhitelist;
            WildcardProcessWhitelist = wildcardProcessWhitelist;

            _log.Information("Apply ProcessWhitelist: {ProcessWhitelist}", string.Join(",", processWhitelist));

            if (previousThrottleStatus != ThrottleStatus.Stopped)
            {
                StartThrottling(previousThrottleStatus);
            }
        }
    }

    public void ApplyProcessBlacklist(string processBlacklistString)
    {
        lock (_lockObject)
        {
            var previousThrottleStatus = ThrottleStatus;

            if (previousThrottleStatus != ThrottleStatus.Stopped)
            {
                StopThrottling(previousThrottleStatus);
            }

            (HashSet<string> processBlacklist, HashSet<string> wildcardProcessBlacklist) = ParseProcessList(processBlacklistString);
            ProcessBlacklist = processBlacklist;
            WildcardProcessBlacklist = wildcardProcessBlacklist;

            _log.Information("Apply ProcessBlacklist: {ProcessBlacklist}", string.Join(",", processBlacklist));

            if (previousThrottleStatus != ThrottleStatus.Stopped)
            {
                StartThrottling(previousThrottleStatus);
            }
        }
    }

    private static (HashSet<string> fullProcessList, HashSet<string> wildcardProcessList) ParseProcessList(string processListString)
    {
        HashSet<string> fullProcessList = new();
        HashSet<string> wildcardProcessList = new();

        Regex doubleSlashRegex = new("//");

        using StringReader stringReader = new(processListString ?? string.Empty);
        while (stringReader.ReadLine() is string line)
        {
            Match doubleSlashMatch = doubleSlashRegex.Match(line);
            string processName = (doubleSlashMatch.Success ? line[..doubleSlashMatch.Index] : line).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(processName))
            {
                fullProcessList.Add(processName);

                if (processName.Contains('?') || processName.Contains('*'))
                {
                    wildcardProcessList.Add(processName);
                }
            }
        }

        return (fullProcessList, wildcardProcessList);
    }

    private bool UpdateThrottleStatusAndNotify()
    {
        lock (_lockObject)
        {
            var fromThrottleStatus = ThrottleStatus;
            var toThrottleStatus = PauseThrottling ? ThrottleStatus.Stopped : ThrottleStatus.BlacklistAndAllButWhitelist;

            bool throttleStatusChanged = (fromThrottleStatus, toThrottleStatus) switch
            {
                (ThrottleStatus.Stopped, ThrottleStatus.BlacklistAndAllButWhitelist) => StartThrottling(toThrottleStatus),
                (ThrottleStatus.BlacklistAndAllButWhitelist, ThrottleStatus.Stopped) => StopThrottling(fromThrottleStatus),
                _ when fromThrottleStatus == toThrottleStatus => false,
                _ => throw new ArgumentException($"Unknown ThrottleStatus transition: {fromThrottleStatus} -> {toThrottleStatus}")
            };

            if (throttleStatusChanged)
            {
                ThrottleStatus = toThrottleStatus;
                ThrottleStatusChanged?.Invoke(this, ThrottleStatus);
                _log.Information("ThrottleStatus changed to: {ThrottleStatus}", toThrottleStatus);
            }

            return throttleStatusChanged;
        }
    }

    private bool StartThrottling(ThrottleStatus toThrottleStatus)
    {
        lock (_lockObject)
        {
            if (toThrottleStatus == ThrottleStatus.Stopped)
            {
                return false;
            }

            _log.Information("Start throttling");
            ThrottleUserBackgroundProcesses(toThrottleStatus);
            _houseKeepingCancellationTokenSource = new CancellationTokenSource();
            _ = HouseKeeping(_houseKeepingCancellationTokenSource.Token);

            return true;
        }
    }

    private bool StopThrottling(ThrottleStatus fromThrottleStatus)
    {
        lock (_lockObject)
        {
            if (fromThrottleStatus == ThrottleStatus.Stopped)
            {
                return false;
            }

            _log.Information("Stop throttling");
            _houseKeepingCancellationTokenSource.Cancel();
            _foregroundDebounceCancellationTokenSource?.Cancel();
            _foregroundDebounceCancellationTokenSource = null;
            _catchUpCancellationTokenSource?.Cancel();
            _catchUpCancellationTokenSource = null;
            RecoverUserProcesses(fromThrottleStatus);

            return true;
        }
    }

    private bool ThrottleUserBackgroundProcesses(ThrottleStatus toThrottleStatus)
    {
        lock (_lockObject)
        {
            if (toThrottleStatus == ThrottleStatus.Stopped)
            {
                return false;
            }

            Process[] runningProcesses = Process.GetProcesses();
            int currentSessionId = Process.GetCurrentProcess().SessionId;

            IEnumerable<Process> sameAsThisSession = runningProcesses.Where(p => p.SessionId == currentSessionId);
            foreach (Process proc in sameAsThisSession)
            {
                using (proc)
                {
                    if (proc.Id == _pendingProcPid)
                    {
                        continue;
                    }

                    if (ShouldBypassProcess($"{proc.ProcessName}.exe".ToLowerInvariant(), toThrottleStatus))
                    {
                        continue;
                    }

                    IntPtr hProcess = BackgroundEfficiencyWin32Api.OpenProcess((uint)BackgroundEfficiencyWin32Api.ProcessAccessFlags.SetInformation, false, (uint)proc.Id);
                    if (hProcess == IntPtr.Zero)
                    {
                        continue;
                    }

                    ToggleEfficiencyMode(hProcess, true);
                    BackgroundEfficiencyWin32Api.CloseHandle(hProcess);
                }
            }

            return true;
        }
    }

    private bool RecoverUserProcesses(ThrottleStatus fromThrottleStatus)
    {
        lock (_lockObject)
        {
            if (fromThrottleStatus == ThrottleStatus.Stopped)
            {
                return false;
            }

            Process[] runningProcesses = Process.GetProcesses();
            int currentSessionId = Process.GetCurrentProcess().SessionId;

            IEnumerable<Process> sameAsThisSession = runningProcesses.Where(p => p.SessionId == currentSessionId);
            foreach (Process proc in sameAsThisSession)
            {
                using (proc)
                {
                    if (ShouldBypassProcess($"{proc.ProcessName}.exe".ToLowerInvariant(), fromThrottleStatus))
                    {
                        continue;
                    }

                    IntPtr hProcess = BackgroundEfficiencyWin32Api.OpenProcess((uint)BackgroundEfficiencyWin32Api.ProcessAccessFlags.SetInformation, false, (uint)proc.Id);
                    if (hProcess == IntPtr.Zero)
                    {
                        continue;
                    }

                    ToggleEfficiencyMode(hProcess, false);
                    BackgroundEfficiencyWin32Api.CloseHandle(hProcess);
                }
            }

            return true;
        }
    }

    private async Task HouseKeeping(CancellationToken cancellationToken)
    {
        _log.Information("House keeping task started");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HouseKeepingInterval, cancellationToken);
                if (ThrottleStatus == ThrottleStatus.Stopped)
                {
                    continue;
                }

                ThrottleUserBackgroundProcesses(ThrottleStatus);
                _log.Information("House keeping task throttling background processes");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "House keeping task error");
            }
        }

        _log.Information("House keeping task stopped.");
    }

    private void HandleForegroundWindowChanged(IntPtr hwnd)
    {
        lock (_lockObject)
        {
            if (ThrottleStatus == ThrottleStatus.Stopped)
            {
                return;
            }

            uint windowThreadId = BackgroundEfficiencyWin32Api.GetWindowThreadProcessId(hwnd, out uint procId);
            if (windowThreadId == 0 || procId == 0)
            {
                return;
            }

            IntPtr procHandle = BackgroundEfficiencyWin32Api.OpenProcess(
                (uint)(BackgroundEfficiencyWin32Api.ProcessAccessFlags.QueryLimitedInformation | BackgroundEfficiencyWin32Api.ProcessAccessFlags.SetInformation), false, procId);
            if (procHandle == IntPtr.Zero)
            {
                return;
            }

            string appName = GetProcessNameFromHandle(procHandle);

            if (appName == UwpFrameHostApp)
            {
                bool found = false;
                BackgroundEfficiencyWin32Api.EnumChildWindows(hwnd, (innerHwnd, lparam) =>
                {
                    if (found)
                    {
                        return true;
                    }

                    if (BackgroundEfficiencyWin32Api.GetWindowThreadProcessId(innerHwnd, out uint innerProcId) > 0)
                    {
                        if (procId == innerProcId)
                        {
                            return true;
                        }

                        IntPtr innerProcHandle = BackgroundEfficiencyWin32Api.OpenProcess(
                            (uint)(BackgroundEfficiencyWin32Api.ProcessAccessFlags.QueryLimitedInformation | BackgroundEfficiencyWin32Api.ProcessAccessFlags.SetInformation), false, innerProcId);
                        if (innerProcHandle == IntPtr.Zero)
                        {
                            return true;
                        }

                        found = true;
                        BackgroundEfficiencyWin32Api.CloseHandle(procHandle);
                        procHandle = innerProcHandle;
                        procId = innerProcId;
                        appName = GetProcessNameFromHandle(procHandle);
                    }

                    return true;
                }, IntPtr.Zero);
            }

            bool bypass = ShouldBypassProcess(appName, ThrottleStatus);
            if (!bypass)
            {
                _log.Information("Boosting {ProcessName}", appName);
                ToggleEfficiencyMode(procHandle, false);
            }

            if (_pendingProcPid != 0)
            {
                _log.Information("Throttle {ProcessName}", _pendingProcName);

                IntPtr prevProcHandle = BackgroundEfficiencyWin32Api.OpenProcess((uint)BackgroundEfficiencyWin32Api.ProcessAccessFlags.SetInformation, false, _pendingProcPid);
                if (prevProcHandle != IntPtr.Zero)
                {
                    ToggleEfficiencyMode(prevProcHandle, true);
                    BackgroundEfficiencyWin32Api.CloseHandle(prevProcHandle);
                    _pendingProcPid = 0;
                    _pendingProcName = string.Empty;
                }
            }

            if (!bypass)
            {
                _pendingProcPid = procId;
                _pendingProcName = appName;
            }

            BackgroundEfficiencyWin32Api.CloseHandle(procHandle);
        }
    }

    private bool ShouldBypassProcess(string processName, ThrottleStatus throttleStatus) => !ShouldThrottleProcess(processName, throttleStatus);

    private void ScheduleForegroundWindowChanged(IntPtr hwnd)
    {
        CancellationTokenSource debounceCancellationTokenSource;
        lock (_lockObject)
        {
            if (ThrottleStatus == ThrottleStatus.Stopped || hwnd == IntPtr.Zero)
            {
                return;
            }

            _lastQueuedForegroundWindow = hwnd;
            _foregroundDebounceCancellationTokenSource?.Cancel();
            _foregroundDebounceCancellationTokenSource = new CancellationTokenSource();
            debounceCancellationTokenSource = _foregroundDebounceCancellationTokenSource;
        }

        _ = RunForegroundDebounceAsync(debounceCancellationTokenSource);
    }

    private async Task RunForegroundDebounceAsync(CancellationTokenSource debounceCancellationTokenSource)
    {
        try
        {
            await Task.Delay(ForegroundDebounceDelay, debounceCancellationTokenSource.Token);

            IntPtr hwnd;
            lock (_lockObject)
            {
                if (_foregroundDebounceCancellationTokenSource != debounceCancellationTokenSource ||
                    ThrottleStatus == ThrottleStatus.Stopped)
                {
                    return;
                }

                hwnd = _lastQueuedForegroundWindow;
                _foregroundDebounceCancellationTokenSource = null;
            }

            HandleForegroundWindowChanged(hwnd);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Background efficiency foreground debounce failed");
        }
        finally
        {
            debounceCancellationTokenSource.Dispose();
        }
    }

    private void ScheduleCatchUpThrottling()
    {
        CancellationTokenSource catchUpCancellationTokenSource;
        lock (_lockObject)
        {
            if (ThrottleStatus == ThrottleStatus.Stopped)
            {
                return;
            }

            _catchUpCancellationTokenSource?.Cancel();
            _catchUpCancellationTokenSource = new CancellationTokenSource();
            catchUpCancellationTokenSource = _catchUpCancellationTokenSource;
        }

        _ = RunCatchUpThrottlingAsync(catchUpCancellationTokenSource);
    }

    private async Task RunCatchUpThrottlingAsync(CancellationTokenSource catchUpCancellationTokenSource)
    {
        try
        {
            await Task.Delay(CatchUpDelay, catchUpCancellationTokenSource.Token);

            lock (_lockObject)
            {
                if (_catchUpCancellationTokenSource != catchUpCancellationTokenSource ||
                    ThrottleStatus == ThrottleStatus.Stopped)
                {
                    return;
                }

                _catchUpCancellationTokenSource = null;
            }

            ThrottleUserBackgroundProcesses(ThrottleStatus);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Background efficiency catch-up throttling failed");
        }
        finally
        {
            catchUpCancellationTokenSource.Dispose();
        }
    }

    public Task HandleDispatchMessageAsync(BackgroundDispatchMessage message, CancellationToken cancellationToken = default)
    {
        if (message.MessageType == BackgroundDispatchMessageType.SystemSignal &&
            message.Reason is not null &&
            (message.Reason.Contains("Resume", StringComparison.OrdinalIgnoreCase) ||
             message.Reason.Contains("Unlock", StringComparison.OrdinalIgnoreCase) ||
             message.Reason.Contains("Logon", StringComparison.OrdinalIgnoreCase)))
        {
            ScheduleCatchUpThrottling();
        }

        if (message.MessageType != BackgroundDispatchMessageType.ForegroundWindowChanged)
        {
            return Task.CompletedTask;
        }

        if (message.Context.ForegroundWindowTitle is null && message.Context.ForegroundProcessName is null)
        {
            return Task.CompletedTask;
        }

        _log.Debug(
            "Background efficiency accepted dispatch foreground change. ProcessName={ProcessName}, WindowTitle={WindowTitle}, ThrottleStatus={ThrottleStatus}",
            message.Context.ForegroundProcessName,
            message.Context.ForegroundWindowTitle,
            ThrottleStatus);
        if (message.Payload.TryGetValue("windowHandle", out var hwndValue) &&
            long.TryParse(hwndValue, out var hwndLong) &&
            hwndLong != 0)
        {
            ScheduleForegroundWindowChanged(new IntPtr(hwndLong));
        }

        return Task.CompletedTask;
    }

    private bool ShouldThrottleProcess(string processName, ThrottleStatus throttleStatus)
    {
        if (IsProcessInBlacklist(processName))
        {
            return throttleStatus != ThrottleStatus.Stopped;
        }

        return throttleStatus switch
        {
            ThrottleStatus.Stopped => false,
            ThrottleStatus.BlacklistAndAllButWhitelist => !IsProcessInWhitelist(processName),
            _ => throw new ArgumentException("Unknown ThrottleStatus")
        };
    }

    private bool IsProcessInWhitelist(string processName)
    {
        return IsProcessInList(processName, ProcessWhitelist, WildcardProcessWhitelist);
    }

    private bool IsProcessInBlacklist(string processName)
    {
        return IsProcessInList(processName, ProcessBlacklist, WildcardProcessBlacklist);
    }

    private static bool IsProcessInList(string processName, IReadOnlySet<string> fullProcessList, IReadOnlySet<string> wildcardProcessList)
    {
        if (fullProcessList.Contains(processName.ToLowerInvariant()))
        {
            return true;
        }

        if (wildcardProcessList.Any(wildcardExpression => FileSystemName.MatchesSimpleExpression(wildcardExpression, processName, true)))
        {
            return true;
        }

        return false;
    }

    private void ToggleEfficiencyMode(IntPtr hProcess, bool enable)
    {
        BackgroundEfficiencyWin32Api.SetProcessInformation(
            hProcess,
            BackgroundEfficiencyWin32Api.ProcessInformationClass.ProcessPowerThrottling,
            enable ? _pThrottleOn : _pThrottleOff,
            (uint)_szControlBlock);
        BackgroundEfficiencyWin32Api.SetPriorityClass(
            hProcess,
            enable ? BackgroundEfficiencyWin32Api.PriorityClass.IdlePriorityClass : BackgroundEfficiencyWin32Api.PriorityClass.NormalPriorityClass);
    }

    private static string GetProcessNameFromHandle(IntPtr hProcess)
    {
        int capacity = 1024;
        StringBuilder sb = new(capacity);

        if (BackgroundEfficiencyWin32Api.QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
        {
            return Path.GetFileName(sb.ToString());
        }

        return string.Empty;
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            StopThrottling(ThrottleStatus);
            _houseKeepingCancellationTokenSource.Cancel();
            _foregroundDebounceCancellationTokenSource?.Cancel();
            _foregroundDebounceCancellationTokenSource = null;
            _catchUpCancellationTokenSource?.Cancel();
            _catchUpCancellationTokenSource = null;
            _isInitialized = false;
        }

        Marshal.FreeHGlobal(_pThrottleOn);
        Marshal.FreeHGlobal(_pThrottleOff);
    }
}
