using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace CustomRP.Modern.Converters;

/// <summary>
/// True when the bound string is a http(s) URL. Used by the preview to
/// decide whether to render an image control or a placeholder.
/// </summary>
public sealed class StringIsUrlConverter : IValueConverter
{
    public static readonly StringIsUrlConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return false;
        var trimmed = s.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringIsNotUrlConverter : IValueConverter
{
    public static readonly StringIsNotUrlConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !(bool)(StringIsUrlConverter.Instance.Convert(value, targetType, parameter, culture) ?? false);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
