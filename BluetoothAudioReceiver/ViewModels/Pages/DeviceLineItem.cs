using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public sealed class DeviceLineItem : ObservableObject
{
    private string _name = string.Empty;
    private string _glyph = "\uE7F5";
    private bool _isAvailable = true;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Glyph
    {
        get => _glyph;
        set => SetProperty(ref _glyph, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }
}
