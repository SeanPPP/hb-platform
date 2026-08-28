namespace BlazorApp.Api.Features.DataSync.Full.Products;

/// <summary>商品同步单批执行结果，仅在 Product 全量切片内部流转。</summary>
internal sealed class DataSyncProductBatchResult
{
    internal bool IsSuccess { get; init; }
    internal int ProcessedCount { get; init; }
    internal int PageNumber { get; init; }
}
