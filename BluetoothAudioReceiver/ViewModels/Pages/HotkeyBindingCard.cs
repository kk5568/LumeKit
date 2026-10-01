using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class HotkeyBindingCard : ObservableObject
{
    public required string Id { get; init; }

    public required bool IsInputBinding { get; init; }

    [ObservableProperty]
    public partial string Hotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeviceNamesText { get; set; } = "未选择设备";

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    public ObservableCollection<string> DeviceNameLines { get; } = [];
    public ObservableCollection<DeviceLineItem> DeviceLines { get; } = [];

    public List<string> SelectedDeviceIds { get; } = [];
    public List<string> SelectedDeviceDisplayNames { get; } = [];

    public string HotkeyDisplay => string.IsNullOrWhiteSpace(Hotkey) ? "未设置" : Hotkey;

    public string EnabledText => IsEnabled ? "已启用" : "已停用";

    public void SetSelectedDeviceIds(IEnumerable<string> ids)
    {
        SelectedDeviceIds.Clear();
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SelectedDeviceIds.Add(id);
        }
    }

    public void SetSelectedDeviceDisplayNames(IEnumerable<string> names)
    {
        SelectedDeviceDisplayNames.Clear();
        foreach (var name in names.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            SelectedDeviceDisplayNames.Add(name);
        }
    }

    public void SetDeviceNameLines(IEnumerable<string> names)
    {
        DeviceNameLines.Clear();
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            DeviceNameLines.Add(name);
        }
    }

    public void SetDeviceLines(IEnumerable<DeviceLineItem> lines)
    {
        DeviceLines.Clear();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Name)))
        {
            DeviceLines.Add(line);
        }
    }

    partial void OnHotkeyChanged(string value)
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnabledText));
    }
}
