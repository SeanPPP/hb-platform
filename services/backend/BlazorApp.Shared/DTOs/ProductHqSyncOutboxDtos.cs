using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

public static class ProductHqSyncOutboxStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retrying = "retrying";
    public const string Succeeded = "succeeded";
    public const string Blocked = "blocked";
    public const string Superseded = "superseded";
}

/// <summary>
/// 队列负载中的精确删除/停用意图。执行器负责解释 ResourceKind 与 BusinessKey。
/// </summary>
public sealed record ProductHqSyncOutboxTombstoneDto(
    string ResourceKind,
    string? StoreCode,
    string BusinessKey
);

/// <summary>
/// 在商品本地事务内写入 outbox 的请求。PayloadJson 必须是对应 scope 的完整最新投影。
/// </summary>
public sealed record ProductHqSyncOutboxEnqueueRequest
{
    public string OperationKey { get; init; } = string.Empty;

    public string OperationKind { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public List<string>? TargetStoreCodes { get; init; }

    public List<string>? AuthorizedStoreCodes { get; init; }

    public List<string> FieldMask { get; init; } = new();

    public string PayloadJson { get; init; } = "{}";

    public List<ProductHqSyncOutboxTombstoneDto> Tombstones { get; init; } = new();

    public string Source { get; init; } = string.Empty;

    public string? RequestedByUserGuid { get; init; }

    public string? RequestedByDeviceId { get; init; }

    public DateTime OccurredAtUtc { get; init; }
}

/// <summary>
/// 对客户端稳定公开的 HQ 同步操作状态，不暴露 outbox 物理主键或内部合并信息。
/// </summary>
public sealed class ProductHqSyncOperationStatusDto
{
    public string OperationId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public string? StoreCode { get; init; }

    public int AttemptCount { get; init; }

    public DateTime? NextAttemptAt { get; init; }

    public bool Retryable { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}

/// <summary>
/// 服务内部入队结果。JsonIgnore 防止控制器误把物理 outbox 信息暴露为 API 契约。
/// </summary>
public sealed class ProductHqSyncOutboxEnqueueResultDto
{
    [JsonIgnore]
    public Guid OutboxId { get; init; }

    [JsonIgnore]
    public bool WasDuplicate { get; init; }

    [JsonIgnore]
    public IReadOnlyList<Guid> SupersededOutboxIds { get; init; } = Array.Empty<Guid>();

    public ProductHqSyncOperationStatusDto Operation { get; init; } = new();
}

/// <summary>
/// worker 交给具体 HQ 执行器的完整、不可变工作项。
/// </summary>
public sealed class ProductHqSyncOutboxWorkItemDto
{
    public Guid OutboxId { get; init; }

    public string OperationKey { get; init; } = string.Empty;

    public string OperationKind { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public string ScopeKey { get; init; } = string.Empty;

    public IReadOnlyList<string>? TargetStoreCodes { get; init; }

    public IReadOnlyList<string> FieldMask { get; init; } = Array.Empty<string>();

    public string PayloadJson { get; init; } = "{}";

    public IReadOnlyList<ProductHqSyncOutboxTombstoneDto> Tombstones { get; init; } =
        Array.Empty<ProductHqSyncOutboxTombstoneDto>();

    public string Source { get; init; } = string.Empty;

    public DateTime OccurredAtUtc { get; init; }

    public int AttemptCount { get; init; }

    [JsonIgnore]
    public string LeaseOwner { get; init; } = string.Empty;

    [JsonIgnore]
    public Guid LeaseToken { get; init; }
}

public enum ProductHqSyncOutboxExecutionDisposition
{
    Success,
    Retryable,
    Blocked,
}

/// <summary>
/// 执行器只返回稳定错误码与安全文案；原始异常由 worker 记录到服务端日志。
/// </summary>
public sealed record ProductHqSyncOutboxExecutionResult(
    ProductHqSyncOutboxExecutionDisposition Disposition,
    string? ErrorCode = null,
    string? Message = null
)
{
    public static ProductHqSyncOutboxExecutionResult Succeeded(string? message = null) =>
        new(ProductHqSyncOutboxExecutionDisposition.Success, null, message);

    public static ProductHqSyncOutboxExecutionResult Retryable(
        string errorCode,
        string? message = null
    ) => new(ProductHqSyncOutboxExecutionDisposition.Retryable, errorCode, message);

    public static ProductHqSyncOutboxExecutionResult Blocked(
        string errorCode,
        string? message = null
    ) => new(ProductHqSyncOutboxExecutionDisposition.Blocked, errorCode, message);
}
