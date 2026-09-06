using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Interfaces.React;

public interface IProductHqSyncOutboxQueue
{
    Task<ProductHqSyncOutboxEnqueueResultDto> EnqueueAsync(
        ISqlSugarClient db,
        ProductHqSyncOutboxEnqueueRequest request,
        CancellationToken cancellationToken = default
    );

    Task<ProductHqSyncOperationStatusDto?> GetStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default
    );

    Task<ProductHqSyncOutboxAccessDescriptor?> GetAccessDescriptorAsync(
        string operationId,
        CancellationToken cancellationToken = default
    );

    Task<ProductHqSyncOperationStatusDto?> RetryAsync(
        string operationId,
        string requestedBy,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 状态端点内部使用的授权投影，不直接序列化给客户端。
/// </summary>
public sealed class ProductHqSyncOutboxAccessDescriptor
{
    public ProductHqSyncOperationStatusDto Operation { get; init; } = new();

    public string OperationKind { get; init; } = string.Empty;

    public IReadOnlyList<string>? TargetStoreCodes { get; init; }

    public IReadOnlyList<string>? AuthorizedStoreCodes { get; init; }

    public string? RequestedByUserGuid { get; init; }

    public string? RequestedByDeviceId { get; init; }
}
