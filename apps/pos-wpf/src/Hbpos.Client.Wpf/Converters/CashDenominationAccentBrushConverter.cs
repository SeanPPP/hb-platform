using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Hbpos.Client.Wpf.Converters;

public sealed class CashDenominationAccentBrushConverter : IValueConverter
{
    private static readonly Brush FallbackBackground = CreateBrush("#FFEAF2FF");
    private static readonly Brush FallbackForeground = CreateBrush("#FF0040A1");
    private static readonly Brush FallbackBorder = CreateBrush("#FF80A9E8");

    private static readonly IReadOnlyDictionary<decimal, DenominationPalette> Palettes =
        new Dictionary<decimal, DenominationPalette>
        {
            [100m] = new("#FFDFF4E8", "#FF166534", "#FF86EFAC"),
            [50m] = new("#FFFFF1C2", "#FF854D0E", "#FFFCD34D"),
            [20m] = new("#FFFFE4D6", "#FF9A3412", "#FFFDBA74"),
            [10m] = new("#FFDDEBFF", "#FF0040A1", "#FF80A9E8"),
            [5m] = new("#FFF5E8FF", "#FF7E22CE", "#FFD8B4FE"),
            [2m] = new("#FFFFE8A3", "#FF854D0E", "#FFEAB308"),
            [1m] = new("#FFFFF4C7", "#FF854D0E", "#FFFACC15"),
            [0.50m] = new("#FFE0F2FE", "#FF075985", "#FF7DD3FC"),
            [0.20m] = new("#FFDCFCE7", "#FF166534", "#FF86EFAC"),
            [0.10m] = new("#FFE0E7FF", "#FF3730A3", "#FFA5B4FC"),
            [0.05m] = new("#FFF3E8FF", "#FF6B21A8", "#FFD8B4FE")
        };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var palette = TryGetPalette(value);
        var brushPart = parameter as string;

        return brushPart switch
        {
            "Background" => palette?.Background ?? FallbackBackground,
            "Foreground" => palette?.Foreground ?? FallbackForeground,
            "BorderBrush" => palette?.Border ?? FallbackBorder,
            _ => FallbackBackground
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static DenominationPalette? TryGetPalette(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            var denominationValue = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return Palettes.TryGetValue(denominationValue, out var palette)
                ? palette
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        brush.Freeze();
        return brush;
    }

    private sealed record DenominationPalette(string BackgroundColor, string ForegroundColor, string BorderColor)
    {
        public Brush Background { get; } = CreateBrush(BackgroundColor);

        public Brush Foreground { get; } = CreateBrush(ForegroundColor);

        public Brush Border { get; } = CreateBrush(BorderColor);
    }
}
