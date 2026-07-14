using BlazorApp.Shared.DTOs;

namespace Hbpos.Api.Logging;

// 上传模型采用显式白名单，防止共享 DTO 后续新增敏感字段时被自动带出进程。
internal sealed class CentralLogIngestWireRequest
{
    public List<CentralLogIngestWireItem> Logs { get; set; } = [];
}

internal sealed class CentralLogIngestWireItem
{
    public Guid? ClientEventId { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public string ProjectCode { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string? ServiceName { get; set; }

    public string? Category { get; set; }

    public string? EventId { get; set; }

    public string? TraceId { get; set; }

    public string? RequestPath { get; set; }

    public string? RequestMethod { get; set; }

    public int? StatusCode { get; set; }

    public string? ExceptionType { get; set; }

    public string? StackTrace { get; set; }

    public static CentralLogIngestWireItem From(ApplicationLogIngestItemDto item)
    {
        return new CentralLogIngestWireItem
        {
            ClientEventId = item.ClientEventId,
            Level = item.Level,
            Message = item.Message,
            TimestampUtc = item.TimestampUtc,
            ProjectCode = item.ProjectCode,
            Environment = item.Environment,
            SourceType = item.SourceType,
            ServiceName = item.ServiceName,
            Category = item.Category,
            EventId = item.EventId,
            TraceId = item.TraceId,
            RequestPath = item.RequestPath,
            RequestMethod = item.RequestMethod,
            StatusCode = item.StatusCode,
            ExceptionType = item.ExceptionType,
            StackTrace = item.StackTrace
        };
    }
}
