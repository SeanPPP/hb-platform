using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;

/// <summary>
/// ProductHistory 的只读依赖端口；实现不得开启事务。
/// </summary>
internal interface IProductHistoryQueryStore
{
    Task<ProductsDynamicDataReadResult> GetProductsDynamicDataAsync(
        ProductsDynamicDataQueryInput input
    );

    Task<PagedListReactDto<StoreOrderProductOrderHistoryItemDto>> GetProductOrderHistoryAsync(
        ProductOrderHistoryQueryInput input
    );

    Task<StoreOrderProductActivityHistoryResultDto> GetProductActivityHistoryAsync(
        ProductActivityHistoryQueryInput input
    );

    Task<StoreOrderSalesSinceLastArrivalResultDto> GetSalesSinceLastArrivalAsync(
        SalesSinceLastArrivalQueryInput input
    );

    Task<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>> GetSalesSinceLastArrivalSummaryAsync(
        SalesSinceLastArrivalSummaryQueryInput input
    );
}
