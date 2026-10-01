namespace BluetoothAudioReceiver.Models.Application;

public class AudioOutputDeviceItem
{
    public string Id { get; set; } = string.Empty;

    public string EndpointId { get; set; } = string.Empty;

    public string PhysicalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
