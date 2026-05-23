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

    public ObservableCollection<string> DeviceNameLines { get; } = [];
    public ObservableCollection<DeviceLineItem> DeviceLines { get; } = [];

    public List<string> SelectedDeviceIds { get; } = [];

    public string HotkeyDisplay => string.IsNullOrWhiteSpace(Hotkey) ? "未设置" : Hotkey;

    public void SetSelectedDeviceIds(IEnumerable<string> ids)
    {
        SelectedDeviceIds.Clear();
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SelectedDeviceIds.Add(id);
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
}
