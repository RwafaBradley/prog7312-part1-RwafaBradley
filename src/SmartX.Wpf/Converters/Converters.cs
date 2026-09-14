using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SmartX.Core.Domain;
using SmartX.Core.Engagement;
using SmartX.Core.Topology;

namespace SmartX.Wpf.Converters;

internal static class Palette
{
    public static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    // brushes made once and frozen, these are asked for by every tile on every frame
    public static readonly SolidColorBrush Teal = Frozen("#4FD6C4");
    public static readonly SolidColorBrush Amber = Frozen("#F2B33D");
    public static readonly SolidColorBrush Rose = Frozen("#F06A9B");
    public static readonly SolidColorBrush Violet = Frozen("#9C8CF5");
    public static readonly SolidColorBrush Slate = Frozen("#6B7484");
    public static readonly SolidColorBrush Faint = Frozen("#59616F");
}

public sealed class RhythmStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RhythmState state
            ? state switch
            {
                RhythmState.Healthy => Palette.Teal,
                RhythmState.Drifting => Palette.Amber,
                RhythmState.Flaring => Palette.Rose,
                RhythmState.Stalled => Palette.Violet,
                RhythmState.Flatlined => Palette.Slate,
                _ => Palette.Faint
            }
            : Palette.Faint;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class CategoryToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SensorCategory category
            ? category switch
            {
                SensorCategory.PowerConsumption => Palette.Amber,
                SensorCategory.Actuator => Palette.Violet,
                _ => Palette.Teal
            }
            : Palette.Teal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FindingSeverity severity
            ? severity switch
            {
                FindingSeverity.Error => Palette.Rose,
                FindingSeverity.Warning => Palette.Amber,
                _ => Palette.Slate
            }
            : Palette.Slate;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int n ? n : 0;
        var visible = count > 0;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isError = value is true;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) isError = !isError;
        return isError ? Palette.Rose : Palette.Teal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && value.Equals(parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null ? parameter : Binding.DoNothing;
}

public sealed class IndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var indent = value is double d ? d : 0d;
        return new Thickness(indent, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
