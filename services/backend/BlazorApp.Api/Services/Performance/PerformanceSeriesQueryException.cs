namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceSeriesQueryException : Exception
{
    public PerformanceSeriesQueryException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
