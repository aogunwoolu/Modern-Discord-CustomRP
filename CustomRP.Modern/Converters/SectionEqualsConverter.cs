using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace CustomRP.Modern.Converters;

/// <summary>
/// Returns true when the bound value (case-insensitively) equals the
/// ConverterParameter. Used for sidebar tab selection / page visibility.
/// </summary>
public sealed class SectionEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter?.ToString() : Avalonia.Data.BindingOperations.DoNothing;
}
