using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class CardTerminalSettingsTests
{
    [Fact]
    public void FromEnvironment_reads_linkly_configuration()
    {
        using var variables = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HBPOS_CARD_PROCESSOR"] = "linkly",
            ["HBPOS_LINKLY_HOST"] = "192.168.1.50",
            ["HBPOS_LINKLY_PORT"] = "5444",
            ["HBPOS_SQUARE_ACCESS_TOKEN"] = null,
            ["HBPOS_SQUARE_TOKEN"] = null,
            ["SQUARE_TOKEN"] = null,
            ["HBPOS_SQUARE_LOCATION_ID"] = null,
            ["HBPOS_SQUARE_DEVICE_ID"] = null
        });

        var settings = CardTerminalSettings.FromEnvironment();

        Assert.Equal(CardProcessorKind.Linkly, settings.Processor);
        Assert.Equal("192.168.1.50", settings.LinklyHost);
        Assert.Equal(5444, settings.LinklyPort);
    }

    [Fact]
    public void FromEnvironment_reads_square_configuration_with_hbpos_token_fallback()
    {
        using var variables = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HBPOS_CARD_PROCESSOR"] = "square",
            ["HBPOS_SQUARE_ACCESS_TOKEN"] = null,
            ["HBPOS_SQUARE_TOKEN"] = "square-token",
            ["SQUARE_TOKEN"] = null,
            ["HBPOS_SQUARE_LOCATION_ID"] = "LOC-01",
            ["HBPOS_SQUARE_DEVICE_ID"] = "DEV-01"
        });

        var settings = CardTerminalSettings.FromEnvironment();

        Assert.Equal(CardProcessorKind.Square, settings.Processor);
        Assert.Equal("square-token", settings.SquareAccessToken);
        Assert.Equal("LOC-01", settings.SquareLocationId);
        Assert.Equal("DEV-01", settings.SquareDeviceId);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var entry in values)
            {
                _originalValues[entry.Key] = Environment.GetEnvironmentVariable(entry.Key);
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
