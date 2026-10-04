using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace BluetoothAudioReceiver.Services.BackgroundEfficiency;

internal static class BackgroundEfficiencyWin32Api
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryFullProcessImageName([In] IntPtr hProcess, [In] int dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessInformation([In] IntPtr hProcess, [In] ProcessInformationClass processInformationClass, IntPtr processInformation, uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetPriorityClass(IntPtr handle, PriorityClass priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [SuppressUnmanagedCodeSecurity]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    public delegate bool WindowEnumProc(IntPtr hwnd, IntPtr lparam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hwnd, WindowEnumProc callback, IntPtr lParam);

    [Flags]
    public enum ProcessAccessFlags : uint
    {
        SetInformation = 0x00000200,
        QueryLimitedInformation = 0x00001000
    }

    public enum ProcessInformationClass
    {
        ProcessMemoryPriority,
        ProcessMemoryExhaustionInfo,
        ProcessAppMemoryInfo,
        ProcessInPrivateInfo,
        ProcessPowerThrottling,
        ProcessReservedValue1,
        ProcessTelemetryCoverageInfo,
        ProcessProtectionLevelInfo,
        ProcessLeapSecondInfo,
        ProcessInformationClassMax,
    }

    [Flags]
    public enum ProcessorPowerThrottlingFlags : uint
    {
        None = 0x0,
        ProcessPowerThrottlingExecutionSpeed = 0x1,
    }

    public enum PriorityClass : uint
    {
        IdlePriorityClass = 0x40,
        NormalPriorityClass = 0x20,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessPowerThrottlingState
    {
        public const uint CurrentVersion = 1;

        public uint Version;
        public ProcessorPowerThrottlingFlags ControlMask;
        public ProcessorPowerThrottlingFlags StateMask;
    }
}
