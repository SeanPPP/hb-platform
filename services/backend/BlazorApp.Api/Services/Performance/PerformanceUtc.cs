namespace BlazorApp.Api.Services.Performance;

internal static class PerformanceUtc
{
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        // SQL Server datetime/datetime2 读取后 Kind 为 Unspecified，但列内时钟值按约定就是 UTC。
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static DateTime? Normalize(DateTime? value) =>
        value.HasValue ? Normalize(value.Value) : null;
}
