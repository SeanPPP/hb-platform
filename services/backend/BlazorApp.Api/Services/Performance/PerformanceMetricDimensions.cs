using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.Performance;

internal sealed record NormalizedMetricDimensions(
    string Json,
    string Hash,
    string Selector
);

internal static class PerformanceMetricDimensions
{
    public static NormalizedMetricDimensions Normalize(
        IReadOnlyDictionary<string, string>? dimensions
    ) => Normalize(string.Empty, dimensions);

    public static NormalizedMetricDimensions Normalize(
        string metricName,
        IReadOnlyDictionary<string, string>? dimensions
    )
    {
        var normalized = (dimensions ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => NormalizeValue(pair.Key, pair.Value),
                StringComparer.Ordinal
            );
        var json = JsonSerializer.Serialize(normalized);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var selector = BuildSelector(metricName, normalized);
        return new NormalizedMetricDimensions(json, hash, selector);
    }

    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string NormalizeValue(string key, string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var maxLength = string.Equals(key, "sqlTemplate", StringComparison.Ordinal) ? 500 : 120;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string BuildSelector(
        string metricName,
        IReadOnlyDictionary<string, string> dimensions
    )
    {
        string Value(string key) => dimensions.GetValueOrDefault(key, string.Empty);

        var selector = metricName switch
        {
            PerformanceMetricNames.ApiRequestDuration => string.Join(
                ' ',
                new[] { Value("method"), Value("route"), Value("statusClass") }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
            ),
            PerformanceMetricNames.SqlCommandDuration => string.Join(
                ':',
                new[] { Value("databaseContext"), Value("sqlFingerprint") }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
            ),
            PerformanceMetricNames.HqSyncDuration
                or PerformanceMetricNames.HqSyncSuccessRate
                or PerformanceMetricNames.HqSyncFailureRate
                or PerformanceMetricNames.HqSyncBacklog
                or PerformanceMetricNames.BackgroundJobDuration
                or PerformanceMetricNames.BackgroundJobSuccessRate
                or PerformanceMetricNames.BackgroundJobFailureRate => Value("operation"),
            PerformanceMetricNames.WebTableReactCommit
                or PerformanceMetricNames.WebTableRenderToPaint => Value("metricId"),
            PerformanceMetricNames.WebFirstScreenBytes
                or PerformanceMetricNames.WebLargestInitialChunkBytes
                or PerformanceMetricNames.CiRunDuration => Value("lane"),
            PerformanceMetricNames.PosColdStart
                or PerformanceMetricNames.PosScanToCart
                or PerformanceMetricNames.PosPaymentResponse => Value("app"),
            PerformanceMetricNames.SentryCrashFreeSession => Value("project"),
            _ => Value("metricId"),
        };
        if (string.IsNullOrWhiteSpace(selector))
        {
            selector = Value("route");
        }
        if (string.IsNullOrWhiteSpace(selector))
        {
            selector = Value("component");
        }
        selector = string.IsNullOrWhiteSpace(selector) ? "all" : selector.Trim();
        return selector.Length <= 500 ? selector : selector[..500];
    }
}
