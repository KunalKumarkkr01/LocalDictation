using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalDictation.Desktop.Converters;

/// <summary>
/// Inverts a boolean. Used to drive <c>IsEnabled</c> from a "busy"-style flag (enabled when not busy).
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>
/// Maps <c>false → Visible</c>, <c>true → Collapsed</c>. Used to show an element only while a flag is
/// false (e.g. the download button before the download completes).
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
