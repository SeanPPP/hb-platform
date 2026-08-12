using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

/// <summary>
/// 仓库商品批量修改后台任务服务。
/// </summary>
public interface IWarehouseProductBatchUpdateJobService
{
    Task<WarehouseProductBatchUpdateJobDto> StartJobAsync(
        WarehouseProductBatchUpdateJobRequestDto request,
        string? updatedBy,
        CancellationToken cancellationToken = default
    );

    Task<WarehouseProductBatchUpdateJobDto?> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 后台批量修改队列已达到容量上限。
/// </summary>
public sealed class WarehouseProductBatchUpdateQueueFullException : Exception
{
    public WarehouseProductBatchUpdateQueueFullException()
        : base("仓库商品批量修改后台任务队列已满，请稍后重试") { }
}
