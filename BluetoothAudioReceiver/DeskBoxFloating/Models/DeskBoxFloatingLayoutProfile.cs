using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.DeskBoxFloating.Models;

public sealed record DeskBoxFloatingLayoutProfile(
    string Id,
    int Width,
    int Height,
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    Thickness Padding,
    double ContentSpacing,
    double IconSize,
    DeskBoxFloatingContentLayout ContentLayout,
    DeskBoxFloatingPlacement Placement,
    bool AutoDismiss,
    int DefaultDurationMilliseconds,
    bool AllowScroll);
