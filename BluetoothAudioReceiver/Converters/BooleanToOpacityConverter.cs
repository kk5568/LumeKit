using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BluetoothAudioReceiver.Converters;

public sealed partial class BooleanToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;

    public double FalseOpacity { get; set; } = 0.45;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool isTrue && isTrue ? TrueOpacity : FalseOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is double opacity && opacity >= (TrueOpacity + FalseOpacity) / 2.0;
    }
}
