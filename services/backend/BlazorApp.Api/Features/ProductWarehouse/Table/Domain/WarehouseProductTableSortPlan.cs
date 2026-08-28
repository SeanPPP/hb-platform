using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 排序计划只解释请求，不接触 SqlSugar；具体列表达式仍由 Query Store 按旧 SQL 生成。
/// </summary>
internal sealed record WarehouseProductTableSortPlan(
    bool HasRequestedSort,
    string Sort,
    bool Descending
)
{
    internal static WarehouseProductTableSortPlan Create(ReactTableRequestDto request) =>
        new(
            !string.IsNullOrWhiteSpace(request.SortBy),
            request.SortBy?.ToLower() ?? string.Empty,
            string.Equals(request.SortOrder, "descend", StringComparison.OrdinalIgnoreCase)
        );
}
