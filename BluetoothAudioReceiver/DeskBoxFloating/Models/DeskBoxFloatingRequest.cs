namespace BluetoothAudioReceiver.DeskBoxFloating.Models;

using Windows.Graphics;

public sealed record DeskBoxFloatingRequest
{
    public string ProfileId { get; init; } = string.Empty;

    public DeskBoxFloatingKind Kind { get; init; } = DeskBoxFloatingKind.Notification;

    public string IconGlyph { get; init; } = "\uE765";

    public string Title { get; init; } = "测试";

    public string Status { get; init; } = "已触发";

    public string Description { get; init; } = string.Empty;

    public int DurationMilliseconds { get; init; } = 2500;

    public int? WidthOverride { get; init; }

    public int? HeightOverride { get; init; }

    public DeskBoxFloatingPlacement? PlacementOverride { get; init; }

    public DeskBoxFloatingContentLayout? ContentLayoutOverride { get; init; }

    public IReadOnlyList<string> Items { get; init; } = [];

    public PointInt32? AnchorPoint { get; init; }

    public bool? AutoDismissOverride { get; init; }
}
