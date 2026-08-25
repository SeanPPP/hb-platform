using System.Globalization;
using Hbpos.Client.Wpf.Converters;

namespace Hbpos.Client.Tests;

public sealed class AlternationIndexToRowNumberConverterTests
{
    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "2")]
    [InlineData(24, "25")]
    public void Convert_returns_one_based_row_number(int index, string expected)
    {
        var converter = new AlternationIndexToRowNumberConverter();

        var result = converter.Convert(
            index,
            typeof(string),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void Convert_returns_empty_string_for_invalid_input(object? value)
    {
        var converter = new AlternationIndexToRowNumberConverter();

        var result = converter.Convert(
            value!,
            typeof(string),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, result);
    }
}
