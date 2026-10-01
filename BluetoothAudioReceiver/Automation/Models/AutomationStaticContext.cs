using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver.Automation.Models;

public sealed record AutomationStaticContext
{
    public string MachineName { get; init; } = Environment.MachineName;
    public string OperatingSystem { get; init; } = RuntimeInformation.OSDescription;
    public string OperatingSystemArchitecture { get; init; } = RuntimeInformation.OSArchitecture.ToString();
    public string ProcessArchitecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();
    public int ProcessorCount { get; init; } = Environment.ProcessorCount;
    public bool Is64BitOperatingSystem { get; init; } = Environment.Is64BitOperatingSystem;
    public bool Is64BitProcess { get; init; } = Environment.Is64BitProcess;
}
