using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace BluetoothAudioReceiver.DeskBoxFloating.Views;

public sealed partial class DeskBoxIslandPrototype : UserControl
{
    private bool _expansionTarget;

    public event EventHandler<bool>? ExpansionRequested;

    public bool IsExpanded => _expansionTarget;

    public DeskBoxIslandPrototype()
    {
        InitializeComponent();
        ApplyExpansionProgress(0);
    }

    public void SetExpansionTarget(bool expanded)
    {
        _expansionTarget = expanded;
    }

    /// <summary>
    /// Applies the same normalized progress used to resize the native island window.
    /// Shared elements stay alive; only their position and emphasis change.
    /// </summary>
    public void ApplyExpansionProgress(double progress)
    {
        double detailsProgress = SmoothStep(Math.Clamp(progress, 0, 1));

        StateTitleTranslate.Y = Lerp(0, -8, detailsProgress);
        StateSubtitle.Opacity = detailsProgress;
        StateSubtitleTranslate.Y = Lerp(8, 0, detailsProgress);
        SummaryMetrics.Opacity = 0.72 * (1 - detailsProgress);
        DetailsLayout.Opacity = detailsProgress;
        DetailsTranslate.Y = Lerp(10, 0, detailsProgress);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        bool nextTarget = !_expansionTarget;
        _expansionTarget = nextTarget;
        ExpansionRequested?.Invoke(this, nextTarget);
        e.Handled = true;
    }

    private static double SmoothStep(double value) => value * value * (3 - 2 * value);

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * progress);
}
