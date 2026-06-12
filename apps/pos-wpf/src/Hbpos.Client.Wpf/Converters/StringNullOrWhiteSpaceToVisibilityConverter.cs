using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Hbpos.Client.Wpf.Converters;

public sealed class StringNullOrWhiteSpaceToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is string text ? !string.IsNullOrWhiteSpace(text) : value is not null;
        var isVisible = Invert ? !hasValue : hasValue;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
