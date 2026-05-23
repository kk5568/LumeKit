using BluetoothAudioReceiver.Models.Application;
using BluetoothAudioReceiver.Views.Windows;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAudioRouteHotkeyService
{
    event Action<string>? DebugMessage;

    void InitializeForWindow(MainWindow window);

    void UpdateHotkeys(string inputHotkey, string outputHotkey);
    void UpdateInputHotkey(string inputHotkey);
    void UpdateOutputHotkey(string outputHotkey);
    void SetHotkeyCaptureSuspended(bool suspended);

    void UpdateTargets(string? inputDeviceId, string? outputDeviceId);
    void UpdateTargetCandidates(IReadOnlyList<string>? inputDeviceIds, IReadOnlyList<string>? outputDeviceIds);

    Task<IReadOnlyList<AudioOutputDeviceItem>> GetOutputDevicesAsync();

    Task<bool> TriggerInputRouteAsync();

    Task<bool> TriggerOutputRouteAsync();
}
