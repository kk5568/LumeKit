using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Controls;

public sealed partial class AutomationCardControl : UserControl
{
    public event RoutedEventHandler? EditClicked;
    public event RoutedEventHandler? TestClicked;
    public event RoutedEventHandler? DeleteClicked;

    public AutomationCardControl()
    {
        InitializeComponent();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        EditClicked?.Invoke(this, e);
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        TestClicked?.Invoke(this, e);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        DeleteClicked?.Invoke(this, e);
    }
}
