using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ImageMango.UI;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is null (placeholder text).</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public static readonly NullToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapsed when the value is null.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class FitToBrushConverter : IValueConverter
{
    private static readonly Brush Good = Frozen(0x2E, 0xA0, 0x43);
    private static readonly Brush Offload = Frozen(0xD0, 0x8A, 0x00);
    private static readonly Brush TooLarge = Frozen(0xD1, 0x34, 0x38);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        FitLevel.Good => Good,
        FitLevel.Offload => Offload,
        _ => TooLarge,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly Brush Filtered = FitToBrushConverter.Frozen(0x2B, 0x7C, 0xD3);
    private static readonly Brush Unfiltered = FitToBrushConverter.Frozen(0x8A, 0x5C, 0xD6);
    private static readonly Brush Nfaa = FitToBrushConverter.Frozen(0xC2, 0x3B, 0x6E);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "filtered" => Filtered,
        "nfaa" => Nfaa,
        _ => Unfiltered,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
