using BluetoothAudioReceiver.Models.Application;
using BluetoothAudioReceiver.Views.Windows;
using Windows.System;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAudioRouteHotkeyService
{
    event Action<string>? DebugMessage;
    event Action<VirtualKey, VirtualKeyModifiers>? HotkeyCaptureKeyDown;

    void InitializeForWindow(MainWindow window);

    void UpdateHotkeys(string inputHotkey, string outputHotkey);
    void UpdateInputHotkey(string inputHotkey);
    void UpdateOutputHotkey(string outputHotkey);
    void RefreshUserHotkeys();
    void UpdateAutoBrightnessHotkey(string hotkey);
    void RefreshNotificationHotkeys();
    void SetHotkeyCaptureSuspended(bool suspended);

    Task UpdateTargetsAsync(string? inputDeviceId, string? outputDeviceId);
    void UpdateTargetCandidates(IReadOnlyList<string>? inputDeviceIds, IReadOnlyList<string>? outputDeviceIds);

    Task<IReadOnlyList<AudioOutputDeviceItem>> GetOutputDevicesAsync();
    Task<string?> GetCurrentDefaultOutputEndpointIdAsync();

    Task<bool> SetOutputDeviceAsync(string deviceId, bool suppressNotification = false);

    Task<bool> TriggerInputRouteAsync(bool suppressNotification = false);

    Task<bool> TriggerOutputRouteAsync(bool suppressNotification = false);

    Task<bool> TriggerCustomHotkeyAsync(string hotkeyText);
}
