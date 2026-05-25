using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hbpos.Client.Wpf.Converters;

public sealed class ProductThumbnailImageSourceConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public int DecodePixelWidth { get; set; } = 72;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string uriText || string.IsNullOrWhiteSpace(uriText))
        {
            return null;
        }

        var cacheKey = uriText.Trim();
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var imageSource = CreateImageSource(cacheKey);
        if (imageSource is not null)
        {
            Cache.TryAdd(cacheKey, imageSource);
        }

        return imageSource;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private ImageSource? CreateImageSource(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.RelativeOrAbsolute, out var uri))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = Math.Max(1, DecodePixelWidth);
            image.CreateOptions = BitmapCreateOptions.DelayCreation;
            image.CacheOption = BitmapCacheOption.Default;
            image.EndInit();
            if (image.CanFreeze)
            {
                image.Freeze();
            }

            return image;
        }
        catch
        {
            return null;
        }
    }
}
