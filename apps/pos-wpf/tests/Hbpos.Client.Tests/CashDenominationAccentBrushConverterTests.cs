using System.Globalization;
using System.Windows.Media;
using Hbpos.Client.Wpf.Converters;

namespace Hbpos.Client.Tests;

public sealed class CashDenominationAccentBrushConverterTests
{
    [Theory]
    [InlineData(100, "Background", "#FFDFF4E8")]
    [InlineData(50, "Background", "#FFFFF1C2")]
    [InlineData(20, "Background", "#FFFFE4D6")]
    [InlineData(10, "Background", "#FFDDEBFF")]
    [InlineData(5, "Background", "#FFF5E8FF")]
    [InlineData(2, "Background", "#FFFFE8A3")]
    [InlineData(1, "Background", "#FFFFF4C7")]
    [InlineData(0.50, "Background", "#FFE0F2FE")]
    [InlineData(0.20, "Background", "#FFDCFCE7")]
    [InlineData(0.10, "Background", "#FFE0E7FF")]
    [InlineData(0.05, "Background", "#FFF3E8FF")]
    public void Convert_returns_background_brush_for_each_cash_denomination(
        double value,
        string brushPart,
        string expectedColor)
    {
        var brush = Convert(value, brushPart);

        Assert.Equal(ParseColor(expectedColor), brush.Color);
    }

    [Theory]
    [InlineData(100, "Foreground", "#FF166534")]
    [InlineData(50, "Foreground", "#FF854D0E")]
    [InlineData(20, "Foreground", "#FF9A3412")]
    [InlineData(10, "Foreground", "#FF0040A1")]
    [InlineData(5, "Foreground", "#FF7E22CE")]
    [InlineData(2, "Foreground", "#FF854D0E")]
    [InlineData(1, "Foreground", "#FF854D0E")]
    [InlineData(0.50, "Foreground", "#FF075985")]
    [InlineData(0.20, "Foreground", "#FF166534")]
    [InlineData(0.10, "Foreground", "#FF3730A3")]
    [InlineData(0.05, "Foreground", "#FF6B21A8")]
    public void Convert_returns_foreground_brush_for_each_cash_denomination(
        double value,
        string brushPart,
        string expectedColor)
    {
        var brush = Convert(value, brushPart);

        Assert.Equal(ParseColor(expectedColor), brush.Color);
    }

    [Theory]
    [InlineData(100, "BorderBrush", "#FF86EFAC")]
    [InlineData(50, "BorderBrush", "#FFFCD34D")]
    [InlineData(20, "BorderBrush", "#FFFDBA74")]
    [InlineData(10, "BorderBrush", "#FF80A9E8")]
    [InlineData(5, "BorderBrush", "#FFD8B4FE")]
    [InlineData(2, "BorderBrush", "#FFEAB308")]
    [InlineData(1, "BorderBrush", "#FFFACC15")]
    [InlineData(0.50, "BorderBrush", "#FF7DD3FC")]
    [InlineData(0.20, "BorderBrush", "#FF86EFAC")]
    [InlineData(0.10, "BorderBrush", "#FFA5B4FC")]
    [InlineData(0.05, "BorderBrush", "#FFD8B4FE")]
    public void Convert_returns_border_brush_for_each_cash_denomination(
        double value,
        string brushPart,
        string expectedColor)
    {
        var brush = Convert(value, brushPart);

        Assert.Equal(ParseColor(expectedColor), brush.Color);
    }

    [Theory]
    [InlineData(999, "Background", "#FFEAF2FF")]
    [InlineData(999, "Foreground", "#FF0040A1")]
    [InlineData(999, "BorderBrush", "#FF80A9E8")]
    [InlineData(100, "Unknown", "#FFEAF2FF")]
    public void Convert_returns_fallback_brush_for_unknown_value_or_parameter(
        double value,
        string brushPart,
        string expectedColor)
    {
        var brush = Convert(value, brushPart);

        Assert.Equal(ParseColor(expectedColor), brush.Color);
    }

    private static SolidColorBrush Convert(double value, string brushPart)
    {
        var converter = new CashDenominationAccentBrushConverter();
        var result = converter.Convert(
            (decimal)value,
            typeof(Brush),
            brushPart,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.True(brush.IsFrozen);
        return brush;
    }

    private static Color ParseColor(string color)
    {
        return (Color)ColorConverter.ConvertFromString(color)!;
    }
}
