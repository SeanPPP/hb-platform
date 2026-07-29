using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.Converters;

public sealed class ReceiptQrCodeImageSourceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string payload || string.IsNullOrWhiteSpace(payload))
        {
            return DependencyProperty.UnsetValue;
        }

        try
        {
            var rendered = AttendanceQrPngRenderer.Render(payload);
            using var stream = new MemoryStream(rendered.PngBytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
