using BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Infrastructure;

internal sealed class ProductHistoryQueryStore(
    ProductsDynamicDataQueryStore dynamicDataQueryStore,
    ProductOrderHistoryQueryStore orderHistoryQueryStore,
    ProductSalesHistoryQueryStore salesHistoryQueryStore
) : IProductHistoryQueryStore
{
    public Task<ProductsDynamicDataReadResult> GetProductsDynamicDataAsync(
        ProductsDynamicDataQueryInput input
    )
    {
        return dynamicDataQueryStore.GetProductsDynamicDataAsync(input);
    }

    public Task<PagedListReactDto<StoreOrderProductOrderHistoryItemDto>> GetProductOrderHistoryAsync(
        ProductOrderHistoryQueryInput input
    )
    {
        return orderHistoryQueryStore.GetProductOrderHistoryAsync(input);
    }

    public Task<StoreOrderProductActivityHistoryResultDto> GetProductActivityHistoryAsync(
        ProductActivityHistoryQueryInput input
    )
    {
        return orderHistoryQueryStore.GetProductActivityHistoryAsync(input);
    }

    public Task<StoreOrderSalesSinceLastArrivalResultDto> GetSalesSinceLastArrivalAsync(
        SalesSinceLastArrivalQueryInput input
    )
    {
        return salesHistoryQueryStore.GetSalesSinceLastArrivalAsync(input);
    }

    public Task<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>> GetSalesSinceLastArrivalSummaryAsync(
        SalesSinceLastArrivalSummaryQueryInput input
    )
    {
        return salesHistoryQueryStore.GetSalesSinceLastArrivalSummaryAsync(input);
    }
}
