namespace BlazorApp.Shared.DTOs
{
    public class ApplicationLogIngestRequestDto
    {
        public List<ApplicationLogIngestItemDto> Logs { get; set; } = new();
    }

    public class ApplicationLogIngestItemDto
    {
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string ProjectCode { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string? ServiceName { get; set; }
        public string? InstanceId { get; set; }
        public string? Category { get; set; }
        public string? EventId { get; set; }
        public string? TraceId { get; set; }
        public string? RequestPath { get; set; }
        public string? RequestMethod { get; set; }
        public int? StatusCode { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? ClientIp { get; set; }
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
        public string? StackTrace { get; set; }
        public Dictionary<string, object?>? Properties { get; set; }
    }

    public class ApplicationLogIngestResultDto
    {
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }
    }

    public class ApplicationLogQueryDto
    {
        public string? ProjectCode { get; set; }
        public List<string>? ProjectCodes { get; set; }
        public string? Environment { get; set; }
        public string? SourceType { get; set; }
        public string? Level { get; set; }
        public string? Category { get; set; }
        public string? RequestPath { get; set; }
        public string? TraceId { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Keyword { get; set; }
        public DateTime? StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string? SortBy { get; set; }
        public string? SortDirection { get; set; }
    }

    public class ApplicationLogDto
    {
        public Guid Id { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string ProjectCode { get; set; } = string.Empty;
        public string? ProjectName { get; set; }
        public string Environment { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string? ServiceName { get; set; }
        public string Level { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? RequestPath { get; set; }
        public string? RequestMethod { get; set; }
        public int? StatusCode { get; set; }
        public string? TraceId { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? ClientIp { get; set; }
        public string? PropertiesJson { get; set; }
    }

    public class ApplicationLogSummaryDto
    {
        public int Total { get; set; }
        public List<ApplicationLogGroupCountDto> ByProject { get; set; } = new();
        public List<ApplicationLogGroupCountDto> ByLevel { get; set; } = new();
        public List<ApplicationLogGroupCountDto> ByExceptionType { get; set; } = new();
        public List<ApplicationLogGroupCountDto> ByRequestPath { get; set; } = new();
        public ApplicationLogPipelineRuntimeDto Pipeline { get; set; } = new();
    }

    public class ApplicationLogPipelineRuntimeDto
    {
        public int DroppedOldestCount { get; set; }
        public int EnqueueFailureCount { get; set; }
        public int FailedFlushBatchCount { get; set; }
        public int FailedFlushLogCount { get; set; }
        public int LastFailedFlushBatchSize { get; set; }
        public string? LastFailedFlushReason { get; set; }
    }

    public class ApplicationLogGroupCountDto
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
