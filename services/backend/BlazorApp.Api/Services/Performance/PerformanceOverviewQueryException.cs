namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceOverviewQueryException : Exception
{
    public PerformanceOverviewQueryException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
