namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 仓库端录入的供货说明。随下架请求一起提交，或对已下架商品单独修改。
/// </summary>
public sealed class WarehouseProductSupplyNoticeInputDto
{
    /// <summary>后续计划：WillRestock / Undecided / Seasonal / Discontinued。</summary>
    public string SupplyPlan { get; set; } = string.Empty;

    public DateOnly? ExpectedFrom { get; set; }

    public DateOnly? ExpectedTo { get; set; }

    /// <summary>时间精度：Unknown / Day / Range / Month。</summary>
    public string ExpectedPrecision { get; set; } = "Unknown";

    /// <summary>给分店看的说明。</summary>
    public string? StoreFacingNote { get; set; }

    /// <summary>内部备注，仅仓库端可见。</summary>
    public string? InternalNote { get; set; }
}

/// <summary>仓库端展示用的供货说明（含内部备注）。</summary>
public sealed class WarehouseProductSupplyNoticeDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string SupplyPlan { get; set; } = string.Empty;
    public DateOnly? ExpectedFrom { get; set; }
    public DateOnly? ExpectedTo { get; set; }
    public string ExpectedPrecision { get; set; } = "Unknown";

    /// <summary>预计时间段已过但商品仍未恢复订货。</summary>
    public bool IsOverdue { get; set; }
    public string? StoreFacingNote { get; set; }
    public string? InternalNote { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>正在关注该商品恢复订货的分店数，辅助安排补货。</summary>
    public int WatchingStoreCount { get; set; }
}

/// <summary>按商品编码批量查询当前有效的供货说明（仓库商品列表的“供货计划 / 预计恢复 / 更新时间”三列）。</summary>
public sealed class WarehouseProductSupplyNoticeQueryRequestDto
{
    public List<string> ProductCodes { get; set; } = new();
}

/// <summary>对已下架商品批量登记或修改供货说明。</summary>
public sealed class BatchUpsertWarehouseProductSupplyNoticeRequestDto
{
    public List<string> ProductCodes { get; set; } = new();
    public WarehouseProductSupplyNoticeInputDto Notice { get; set; } = new();
}

public sealed class BatchUpsertWarehouseProductSupplyNoticeResultDto
{
    public bool Success { get; set; }
    public int SuccessCount { get; set; }

    /// <summary>被跳过的商品：不存在、已删除，或当前在架（在架商品不需要供货说明）。</summary>
    public List<string> SkippedProductCodes { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 分店端看到的商品供货状态。刻意不含内部备注。
/// </summary>
public sealed class StoreProductSupplyStatusDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public string? Barcode { get; set; }
    public string? ProductName { get; set; }
    public string? ProductImage { get; set; }

    /// <summary>当前是否可订货。关注列表里为 true 表示“已恢复订货”。</summary>
    public bool IsOrderable { get; set; }

    /// <summary>后续计划；仓库没有登记说明时为 Undecided。</summary>
    public string SupplyPlan { get; set; } = "Undecided";

    /// <summary>仓库是否登记过说明；false 时 SupplyPlan 只是兜底值。</summary>
    public bool HasNotice { get; set; }
    public DateOnly? ExpectedFrom { get; set; }
    public DateOnly? ExpectedTo { get; set; }
    public string ExpectedPrecision { get; set; } = "Unknown";

    /// <summary>原预计时间已过、新时间待确认；为 true 时前端不再展示过期日期。</summary>
    public bool IsOverdue { get; set; }
    public string? StoreFacingNote { get; set; }
    public DateTime? NoticeUpdatedAtUtc { get; set; }

    /// <summary>当前分店是否已关注。</summary>
    public bool IsWatching { get; set; }
}

/// <summary>搜索或扫码零结果时，按条码 / 货号 / 商品编码精确查询暂停供货商品。</summary>
public sealed class StoreProductSupplyLookupRequestDto
{
    public string? StoreCode { get; set; }
    public string? Code { get; set; }
}

public sealed class StoreProductSupplyLookupResultDto
{
    public string Code { get; set; } = string.Empty;

    /// <summary>barcode / itemNumber / productCode；未命中为 null。</summary>
    public string? MatchType { get; set; }
    public List<StoreProductSupplyStatusDto> Items { get; set; } = new();
}

public sealed class StoreProductSupplyWatchRequestDto
{
    public string? StoreCode { get; set; }
    public string? ProductCode { get; set; }
}

/// <summary>确认“已恢复订货”提醒；ProductCodes 为空表示确认该分店全部已恢复的关注。</summary>
public sealed class StoreProductSupplyWatchAcknowledgeRequestDto
{
    public string? StoreCode { get; set; }
    public List<string>? ProductCodes { get; set; }
}

public sealed class StoreProductSupplyWatchSummaryDto
{
    /// <summary>仍在等待恢复的关注数。</summary>
    public int WatchingCount { get; set; }

    /// <summary>已恢复订货、待分店确认的关注数（用于提示条与角标）。</summary>
    public int RestockedCount { get; set; }
}
