using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 商品报告商品明细的排序字段，查询参数取值沿用 compact-sales-board 的 sortField 约定。
/// </summary>
internal enum ProductReportSortField
{
    Amount = 0,
    Quantity = 1,
    UnitPrice = 2,
}

/// <summary>
/// 商品明细排序条件。default 必须等于历史顺序（金额降序），
/// 因此方向用 Ascending 表达，避免未显式传参的调用被当成升序。
/// </summary>
internal readonly record struct ProductReportSort(ProductReportSortField Field, bool Ascending)
{
    public bool IsDefault => Field == ProductReportSortField.Amount && !Ascending;

    /// <summary>按数量或均价排序时，排名阶段才需要额外汇总数量。</summary>
    public bool RequiresQuantity => Field != ProductReportSortField.Amount;

    public string FieldName => Field switch
    {
        ProductReportSortField.Quantity => CompactSalesBoardQuery.SortByQuantity,
        ProductReportSortField.UnitPrice => CompactSalesBoardQuery.SortByUnitPrice,
        _ => CompactSalesBoardQuery.SortByAmount,
    };

    /// <summary>归一化后的排序标记，只可能是 6 个常量之一，可以安全写入日志。</summary>
    public string Token => $"{FieldName}:{(Ascending ? "asc" : "desc")}";

    /// <summary>默认排序不参与缓存键，保证历史缓存键与 single-flight 去重键逐字节不变。</summary>
    public string? CacheToken => IsDefault ? null : Token;

    /// <summary>
    /// 解析查询参数：字段只认白名单，未知值（含 itemNumber）回退为金额；
    /// 方向只有 asc/ascend 表示升序，其余一律降序。
    /// </summary>
    public static ProductReportSort Parse(string? sortField, string? sortOrder)
    {
        var field = sortField?.Trim();
        var parsedField = string.Equals(field, CompactSalesBoardQuery.SortByQuantity, StringComparison.OrdinalIgnoreCase)
            ? ProductReportSortField.Quantity
            : string.Equals(field, CompactSalesBoardQuery.SortByUnitPrice, StringComparison.OrdinalIgnoreCase)
                ? ProductReportSortField.UnitPrice
                : ProductReportSortField.Amount;
        var order = sortOrder?.Trim();
        var ascending = string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(order, "ascend", StringComparison.OrdinalIgnoreCase);
        return new ProductReportSort(parsedField, ascending);
    }

    /// <summary>
    /// 内存路径的排序值。均价口径与 AverageUnitPrice 一致：数量不大于 0 时按 0。
    /// </summary>
    public decimal ValueOf(decimal amount, int quantity) => Field switch
    {
        ProductReportSortField.Quantity => quantity,
        ProductReportSortField.UnitPrice => quantity > 0 ? amount / quantity : 0m,
        _ => amount,
    };
}
