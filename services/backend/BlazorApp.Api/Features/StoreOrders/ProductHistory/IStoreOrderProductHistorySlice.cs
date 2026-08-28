using BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory;

/// <summary>
/// Controller/旧服务后续接线时使用的 ProductHistory 入站端口。
/// </summary>
public interface IStoreOrderProductHistorySlice
{
    Task<ApiResponse<List<StoreOrderDynamicDataDto>>> GetProductsDynamicDataAsync(
        StoreOrderDynamicDataRequestDto request
    );

    Task<ApiResponse<PagedListReactDto<StoreOrderProductOrderHistoryItemDto>>> GetProductOrderHistoryAsync(
        StoreOrderProductOrderHistoryRequestDto request
    );

    Task<ApiResponse<StoreOrderProductActivityHistoryResultDto>> GetProductActivityHistoryAsync(
        StoreOrderProductActivityHistoryRequestDto request
    );

    Task<ApiResponse<StoreOrderSalesSinceLastArrivalResultDto>> GetSalesSinceLastArrivalAsync(
        StoreOrderSalesSinceLastArrivalRequestDto request
    );

    Task<ApiResponse<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>>> GetSalesSinceLastArrivalSummaryAsync(
        StoreOrderSalesSinceLastArrivalSummaryRequestDto request
    );
}

internal sealed class StoreOrderProductHistorySlice(
    GetProductsDynamicDataHandler getProductsDynamicDataHandler,
    GetProductOrderHistoryHandler getProductOrderHistoryHandler,
    GetProductActivityHistoryHandler getProductActivityHistoryHandler,
    GetSalesSinceLastArrivalHandler getSalesSinceLastArrivalHandler,
    GetSalesSinceLastArrivalSummaryHandler getSalesSinceLastArrivalSummaryHandler
) : IStoreOrderProductHistorySlice
{
    public Task<ApiResponse<List<StoreOrderDynamicDataDto>>> GetProductsDynamicDataAsync(
        StoreOrderDynamicDataRequestDto request
    )
    {
        return getProductsDynamicDataHandler.HandleAsync(
            new GetProductsDynamicDataQuery(request)
        );
    }

    public Task<ApiResponse<PagedListReactDto<StoreOrderProductOrderHistoryItemDto>>> GetProductOrderHistoryAsync(
        StoreOrderProductOrderHistoryRequestDto request
    )
    {
        return getProductOrderHistoryHandler.HandleAsync(
            new GetProductOrderHistoryQuery(request)
        );
    }

    public Task<ApiResponse<StoreOrderProductActivityHistoryResultDto>> GetProductActivityHistoryAsync(
        StoreOrderProductActivityHistoryRequestDto request
    )
    {
        return getProductActivityHistoryHandler.HandleAsync(
            new GetProductActivityHistoryQuery(request)
        );
    }

    public Task<ApiResponse<StoreOrderSalesSinceLastArrivalResultDto>> GetSalesSinceLastArrivalAsync(
        StoreOrderSalesSinceLastArrivalRequestDto request
    )
    {
        return getSalesSinceLastArrivalHandler.HandleAsync(
            new GetSalesSinceLastArrivalQuery(request)
        );
    }

    public Task<ApiResponse<List<StoreOrderSalesSinceLastArrivalSummaryItemDto>>> GetSalesSinceLastArrivalSummaryAsync(
        StoreOrderSalesSinceLastArrivalSummaryRequestDto request
    )
    {
        return getSalesSinceLastArrivalSummaryHandler.HandleAsync(
            new GetSalesSinceLastArrivalSummaryQuery(request)
        );
    }
}
