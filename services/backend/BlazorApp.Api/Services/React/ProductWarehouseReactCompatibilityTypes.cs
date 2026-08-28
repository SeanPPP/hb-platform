using System;

namespace BlazorApp.Api.Services.React;

// 保留既有反射与私有查询入口依赖的 React 类型快照；Feature 内部不再引用这些兼容类型。
internal sealed record WarehouseProductTableTimingSnapshot(
    long CandidateMs,
    long CountMs,
    long PageMs,
    long LocationMs,
    long RowsMs,
    long MapMs,
    long TotalMs
);

internal sealed record WarehouseProductTableRequestSnapshot(
    int PageNumber,
    int PageSize,
    int CategoryCount,
    int FilterCount,
    string KeywordType,
    int KeywordLength,
    string SortBy,
    string SortOrder
);

internal sealed class WarehouseProductTableQueryException : Exception
{
    public WarehouseProductTableQueryException(
        string failedStage,
        WarehouseProductTableTimingSnapshot timings,
        Exception innerException,
        WarehouseProductTableRequestSnapshot? request = null
    )
        : base($"仓库商品表格查询在 {failedStage} 阶段失败。", innerException)
    {
        FailedStage = failedStage;
        Timings = timings;
        Request = request;
    }

    public string FailedStage { get; }

    public WarehouseProductTableTimingSnapshot Timings { get; }

    public WarehouseProductTableRequestSnapshot? Request { get; }
}

internal sealed class WarehouseProductTableTimings
{
    public long CandidateMs { get; set; }
    public long CountMs { get; set; }
    public long PageMs { get; set; }
    public long LocationMs { get; set; }
    public long RowsMs { get; set; }
    public long MapMs { get; set; }

    public WarehouseProductTableTimingSnapshot Snapshot(long totalMs) =>
        new(CandidateMs, CountMs, PageMs, LocationMs, RowsMs, MapMs, totalMs);
}

internal sealed class WarehouseProductCodeSearchCandidate
{
    public string ProductCode { get; set; } = string.Empty;
}
