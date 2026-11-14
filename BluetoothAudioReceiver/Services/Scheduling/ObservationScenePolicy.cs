using System.Runtime.InteropServices;
using Windows.System.Power;

namespace BluetoothAudioReceiver.Services;

/// <summary>场景只调整非必要采样，不改变已启用功能的需求。</summary>
internal sealed class ObservationScenePolicy
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SceneCheckInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReducedInterval = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private bool _locked;
    private bool _suspended;
    private IntPtr _mainWindow;
    private ObservationSceneSnapshot _current;

    public ObservationSceneSnapshot Current { get { lock (_sync) return _current; } }

    public void SetMainWindow(IntPtr handle) { lock (_sync) _mainWindow = handle; }
    public void SetLocked(bool value) { lock (_sync) _locked = value; }
    public void SetSuspended(bool value) { lock (_sync) _suspended = value; }

    public bool Refresh(bool refreshPower = false)
    {
        lock (_sync)
        {
            var blocked = _locked || _suspended;
            var foreground = blocked ? IntPtr.Zero : GetForegroundWindow();
            var fullscreen = !blocked && ForegroundWindowProbe.IsForegroundFullscreen();
            // 只有全屏分类需要读进程和标题，空闲检查不顺带查询全部前台信息。
            var game = fullscreen && ForegroundWindowProbe.IsLikelyGame(
                ForegroundWindowProbe.GetForegroundProcessName(), ForegroundWindowProbe.GetForegroundWindowTitle());
            var lastInput = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            var idle = !blocked && (GetLastInputInfo(ref lastInput)
                ? unchecked((uint)Environment.TickCount - lastInput.Time) >= IdleThreshold.TotalMilliseconds
                : _current.Idle);
            var battery = _current.OnBattery;
            var lowBattery = _current.LowBattery;
            if (!blocked && refreshPower)
            {
                try
                {
                    battery = PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;
                    lowBattery = battery && PowerManager.RemainingChargePercent is >= 0 and <= 20;
                }
                catch
                {
                    // 官方电源接口暂不可用时沿用上次状态，不影响锁屏/睡眠暂停。
                }
            }
            var next = new ObservationSceneSnapshot(_locked, _suspended, game, fullscreen && !game,
                battery, lowBattery, idle,
                !blocked && _mainWindow != IntPtr.Zero && foreground != _mainWindow);
            if (next == _current) return false;
            _current = next;
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

internal readonly record struct ObservationSceneSnapshot(
    bool Locked, bool Suspended, bool Game, bool Fullscreen,
    bool OnBattery, bool LowBattery, bool Idle, bool AppBackground)
{
    public bool Pause => Locked || Suspended;
    public bool Reduce => Game || Fullscreen || OnBattery || LowBattery || Idle || AppBackground;
}
