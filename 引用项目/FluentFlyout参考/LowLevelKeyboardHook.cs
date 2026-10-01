// LowLevelKeyboardHook.cs
// 全局低级键盘钩子（WH_KEYBOARD_LL）封装，独立于浮窗，可单独使用。
// 说明：本模块改写自 FluentFlyout (GPL-3.0-or-later)，如直接复用请注意 GPL 协议要求。
using System.Diagnostics;
using System.Runtime.InteropServices;
using static FlyoutModule.NativeMethods;

namespace FlyoutModule;

public class LowLevelKeyboardHook : IDisposable
{
    /// <summary>参数：虚拟键码 vkCode、是否松开（true=KeyUp）。</summary>
    public event Action<int, bool>? KeyAction;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc; // 必须持引用，否则被 GC 回收导致崩溃

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
    }

    public bool Start()
    {
        if (_hookId != IntPtr.Zero) return true;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule == null) return false;

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        return _hookId != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            bool isKeyUp = wParam == WM_KEYUP;
            if (isKeyUp || wParam == WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                KeyAction?.Invoke(vkCode, isKeyUp);
            }
        }
        // 始终放行按键，不影响系统和其他程序
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}

// 常用虚拟键码
public static class Vk
{
    public const int CapsLock = 0x14;
    public const int NumLock = 0x90;
    public const int ScrollLock = 0x91;
    public const int Insert = 0x2D;
    public const int VolumeMute = 0xAD;
    public const int VolumeDown = 0xAE;
    public const int VolumeUp = 0xAF;
    public const int MediaNext = 0xB0;
    public const int MediaPrev = 0xB1;
    public const int MediaStop = 0xB2;
    public const int MediaPlayPause = 0xB3;
}
