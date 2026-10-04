using System.Runtime.InteropServices;
using Serilog;
using WinUIEx.Messaging;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationSessionObservationSource : IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(AutomationSessionObservationSource));

    private const int WmWtsSessionChange = 0x02B1;
    private const int WtsSessionLock = 0x0007;
    private const int WtsSessionUnlock = 0x0008;
    private const int NotifyForThisSession = 0;

    private readonly AutomationObservationPublisher _publisher;
    private readonly Func<bool> _isLocked;
    private readonly Action<bool> _setIsLocked;
    private readonly Func<string, Guid, Task> _requestObservationRefreshAsync;

    private WindowMessageMonitor? _sessionMessageMonitor;
    private IntPtr _sessionWindowHandle;
    private DateTimeOffset _lastSessionSignalAt = DateTimeOffset.MinValue;
    private string? _lastSessionSignalReason;

    public AutomationSessionObservationSource(
        AutomationObservationPublisher publisher,
        Func<bool> isLocked,
        Action<bool> setIsLocked,
        Func<string, Guid, Task> requestObservationRefreshAsync)
    {
        _publisher = publisher;
        _isLocked = isLocked;
        _setIsLocked = setIsLocked;
        _requestObservationRefreshAsync = requestObservationRefreshAsync;
    }

    public void InitializeForWindow(Views.Windows.MainWindow window)
    {
        if (_sessionMessageMonitor != null)
        {
            return;
        }

        _sessionWindowHandle = window.WindowHandle;
        _sessionMessageMonitor = new WindowMessageMonitor(window);
        _sessionMessageMonitor.WindowMessageReceived += OnSessionWindowMessageReceived;

        if (WTSRegisterSessionNotification(_sessionWindowHandle, NotifyForThisSession))
        {
            _log.Information(
                "Native session notification registered. WindowHandle={WindowHandle}",
                _sessionWindowHandle);
        }
        else
        {
            _log.Warning(
                "Native session notification registration failed. WindowHandle={WindowHandle}, Error={Error}",
                _sessionWindowHandle,
                Marshal.GetLastWin32Error());
        }
    }

    private void OnSessionWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if ((int)e.Message.MessageId != WmWtsSessionChange)
        {
            return;
        }

        var sessionEvent = (int)e.Message.WParam;
        var sessionId = (int)e.Message.LParam;
        _log.Information(
            "Received native session notification. Event={Event}, SessionId={SessionId}",
            sessionEvent,
            sessionId);

        if (sessionEvent == WtsSessionLock)
        {
            _setIsLocked(true);
            _log.Information("Processed native SessionLock notification. IsLockedAfter={IsLockedAfter}", _isLocked());
            return;
        }

        if (sessionEvent != WtsSessionUnlock)
        {
            return;
        }

        const string reason = "session-SessionUnlock";
        if (!TryAcceptSessionSignal(reason))
        {
            return;
        }

        _setIsLocked(false);
        var correlationId = Guid.NewGuid();
        _log.Information(
            "Processed native SessionUnlock notification. IsLockedAfter={IsLockedAfter}, CorrelationId={CorrelationId}",
            _isLocked(),
            correlationId);
        _publisher.PublishMainlineSystemSignal(reason, correlationId);
        _publisher.PublishBackgroundDispatchMessage(
            BackgroundDispatchMessageType.SystemSignal,
            reason,
            BackgroundDispatchScene.Unlock,
            new Dictionary<string, string?>
            {
                ["reason"] = reason
            },
            correlationId);
        _ = _requestObservationRefreshAsync(reason, correlationId);
    }

    private bool TryAcceptSessionSignal(string reason)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastSessionSignalReason == reason &&
            now - _lastSessionSignalAt < TimeSpan.FromSeconds(2))
        {
            _log.Debug(
                "Ignored duplicate session signal. Reason={Reason}, ElapsedMs={ElapsedMs}",
                reason,
                (now - _lastSessionSignalAt).TotalMilliseconds);
            return false;
        }

        _lastSessionSignalReason = reason;
        _lastSessionSignalAt = now;
        return true;
    }

    public void Dispose()
    {
        if (_sessionMessageMonitor != null)
        {
            _sessionMessageMonitor.WindowMessageReceived -= OnSessionWindowMessageReceived;
            _sessionMessageMonitor.Dispose();
            _sessionMessageMonitor = null;
        }

        if (_sessionWindowHandle != IntPtr.Zero)
        {
            if (!WTSUnRegisterSessionNotification(_sessionWindowHandle))
            {
                _log.Debug(
                    "Native session notification unregister failed. WindowHandle={WindowHandle}, Error={Error}",
                    _sessionWindowHandle,
                    Marshal.GetLastWin32Error());
            }

            _sessionWindowHandle = IntPtr.Zero;
        }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);
}
