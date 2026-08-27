using System.Diagnostics.Metrics;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Performance;

public sealed class AspNetCoreRequestMetricListener : IHostedService, IDisposable
{
    private const string AspNetCoreMeterName = "Microsoft.AspNetCore.Hosting";
    private const string RequestDurationInstrumentName = "http.server.request.duration";

    private readonly IPerformanceMetricRecorder _recorder;
    private readonly PerformanceMetricsOptions _options;
    private readonly MeterListener _listener = new();

    public AspNetCoreRequestMetricListener(
        IPerformanceMetricRecorder recorder,
        IOptions<PerformanceMetricsOptions> options
    )
    {
        _recorder = recorder;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _listener.InstrumentPublished = static (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == AspNetCoreMeterName
                && instrument.Name == RequestDurationInstrumentName
            )
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose() => _listener.Dispose();

    private void OnMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state
    )
    {
        var route = GetTag(tags, "http.route");
        var method = GetTag(tags, "http.request.method");
        if (!ShouldRecord(route, method) || !double.IsFinite(measurement) || measurement < 0)
        {
            return;
        }

        var statusCode = GetTag(tags, "http.response.status_code");
        _recorder.Record(
            new PerformanceMetricRecord(
                PerformanceMetricNames.ApiRequestDuration,
                _options.BackendProjectCode,
                _options.DefaultEnvironment,
                "api",
                measurement * 1000,
                DateTime.UtcNow,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["route"] = route!,
                    ["method"] = method!,
                    ["statusClass"] = ToStatusClass(statusCode),
                }
            )
        );
    }

    internal static bool ShouldRecord(string? route, string? method)
    {
        if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(route) || string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        return !ShouldDisablePath(route);
    }

    internal static bool ShouldDisablePath(string? path)
    {
        var normalized = path?.Trim().TrimStart('/') ?? string.Empty;
        return normalized.StartsWith("health", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("swagger", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("api/system/logs", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("api/system/performance", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ToStatusClass(string? statusCode)
    {
        if (int.TryParse(statusCode, out var value) && value is >= 100 and <= 599)
        {
            return $"{value / 100}xx";
        }
        return "unknown";
    }

    private static string? GetTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string key
    )
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        return null;
    }
}
