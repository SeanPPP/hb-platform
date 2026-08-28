namespace BlazorApp.Api.Services.Performance;

public sealed record PerformanceMetricRecord(
    string MetricName,
    string ProjectCode,
    string Environment,
    string SourceType,
    double Value,
    DateTime ObservedAtUtc,
    IReadOnlyDictionary<string, string>? Dimensions = null,
    long Weight = 1
);

public interface IPerformanceMetricRecorder
{
    void Record(PerformanceMetricRecord metric);
}
