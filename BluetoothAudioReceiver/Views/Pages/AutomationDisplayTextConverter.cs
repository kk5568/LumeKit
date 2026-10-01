using System;
using Microsoft.UI.Xaml.Data;
using BluetoothAudioReceiver.Automation;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutomationDisplayTextConverter : IValueConverter
{
    public static string ToDisplayText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (AutomationOptionCatalog.ExtractPowerModeGuid(text).Length > 0)
        {
            return AutomationOptionCatalog.ExtractPowerModeDisplayName(text);
        }

        return text;
    }

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string text ? ToDisplayText(text) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is string text ? text : string.Empty;
}
