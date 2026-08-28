using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Features.ProductWarehouse;

/// <summary>
/// 表格查询 Handler：只负责把请求交给无事务 Query Store。
/// </summary>
internal sealed class ProductWarehouseTableSlice : IProductWarehouseTableSlice
{
    private readonly WarehouseProductTableQueryStore _queryStore;
    private readonly WarehouseProductTableDiagnostics _diagnostics;

    internal ProductWarehouseTableSlice(ProductWarehouseSliceContext context)
    {
        _queryStore = new WarehouseProductTableQueryStore(context);
        _diagnostics = new WarehouseProductTableDiagnostics(context);
    }

    public Task<ReactTableResponseDto<WarehouseProductReactListDto>> GetAntdTableDataAsync(
        ReactTableRequestDto request
    ) => ExecuteTableQueryAsync(request);

    public ISugarQueryable<ProductWarehouseTableCodeSearchCandidate> BuildWarehouseTextSearchCandidateQuery(
        string keyword
    ) => _queryStore.BuildWarehouseTextSearchCandidateQuery(keyword);

    public ISugarQueryable<ProductWarehouseTableCodeSearchCandidate> BuildWarehouseCodeSearchCandidateQuery(
        string keyword
    ) => _queryStore.BuildWarehouseCodeSearchCandidateQuery(keyword);

    private async Task<ReactTableResponseDto<WarehouseProductReactListDto>> ExecuteTableQueryAsync(
        ReactTableRequestDto request
    )
    {
        var outcome = await _queryStore.QueryAsync(request);
        _diagnostics.LogWarehouseProductTablePerformance(
            outcome.Request,
            outcome.Timings,
            outcome.Total,
            outcome.ItemCount
        );
        return outcome.Response;
    }
}
