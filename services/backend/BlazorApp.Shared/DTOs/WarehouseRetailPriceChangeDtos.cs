namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 仓库商品零售价月度变化查询条件。
/// </summary>
public sealed record WarehouseRetailPriceChangeQuery
{
    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public bool OnlyWithLocation { get; init; } = true;

    public string? Keyword { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

/// <summary>
/// 零售价变化页的单个商品。货位只用于筛选，避免暴露不属于本页的明细。
/// </summary>
public sealed class WarehouseRetailPriceChangeItem
{
    public string ProductCode { get; init; } = string.Empty;

    public string? ProductImage { get; init; }

    public string? ItemNumber { get; init; }

    public string? Barcode { get; init; }

    public decimal? LatestRetailPrice { get; init; }

    public DateTime LastPriceChangedAtUtc { get; init; }
}

public sealed class WarehouseRetailPriceChangePage
{
    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    public bool OnlyWithLocation { get; init; }

    public List<WarehouseRetailPriceChangeItem> Items { get; init; } = [];

    public int Total { get; init; }

    public int PageNumber { get; init; }

    public int PageSize { get; init; }
}
