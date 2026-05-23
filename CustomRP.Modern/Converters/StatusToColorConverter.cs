using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace CustomRP.Modern.Converters;

public sealed class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString() switch
        {
            "Connected" => "AppSuccessBrush",
            "Connecting" => "AppWarningBrush",
            "Error" => "AppDangerBrush",
            _ => "AppTextMutedBrush",
        };
        if (Application.Current is not null &&
            Application.Current.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var brush))
            return brush;
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
