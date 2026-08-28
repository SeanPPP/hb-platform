using System;

namespace BlazorApp.Api.Features.ProductWarehouse;

// 表格切片的规范类型归属 Feature，避免基础查询与 React 门面形成反向依赖。
internal sealed record ProductWarehouseTableTimingSnapshot(
    long CandidateMs,
    long CountMs,
    long PageMs,
    long LocationMs,
    long RowsMs,
    long MapMs,
    long TotalMs
);

internal sealed record ProductWarehouseTableRequestSnapshot(
    int PageNumber,
    int PageSize,
    int CategoryCount,
    int FilterCount,
    string KeywordType,
    int KeywordLength,
    string SortBy,
    string SortOrder
);

internal sealed class ProductWarehouseTableQueryException : Exception
{
    internal ProductWarehouseTableQueryException(
        string failedStage,
        ProductWarehouseTableTimingSnapshot timings,
        Exception innerException,
        ProductWarehouseTableRequestSnapshot? request = null
    )
        : base($"仓库商品表格查询在 {failedStage} 阶段失败。", innerException)
    {
        FailedStage = failedStage;
        Timings = timings;
        Request = request;
    }

    internal string FailedStage { get; }

    internal ProductWarehouseTableTimingSnapshot Timings { get; }

    internal ProductWarehouseTableRequestSnapshot? Request { get; }
}

internal sealed class ProductWarehouseTableTimings
{
    public long CandidateMs { get; set; }
    public long CountMs { get; set; }
    public long PageMs { get; set; }
    public long LocationMs { get; set; }
    public long RowsMs { get; set; }
    public long MapMs { get; set; }

    public ProductWarehouseTableTimingSnapshot Snapshot(long totalMs) =>
        new(CandidateMs, CountMs, PageMs, LocationMs, RowsMs, MapMs, totalMs);
}

internal sealed class ProductWarehouseTableCodeSearchCandidate
{
    public string ProductCode { get; set; } = string.Empty;
}
