using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Infrastructure;

internal interface IStoreOrderLineCommandStore
{
    Task<StoreOrderManagementResult<bool>> AddOrderLineAsync(AddOrderLineInput input);

    Task<StoreOrderManagementResult<bool>> BatchAddOrderLineAsync(
        BatchAddOrderLineInput input
    );

    Task<StoreOrderManagementResult<bool>> UpdateOrderLineAsync(
        UpdateOrderLineInput input
    );

    Task<StoreOrderManagementResult<bool>> RemoveOrderLineAsync(
        RemoveOrderLineInput input
    );

    Task<StoreOrderManagementResult<bool>> BatchUpdateOrderLineAsync(
        BatchUpdateOrderLineInput input
    );

    Task<StoreOrderManagementResult<RefreshOrderLineImportPricesResult>> RefreshOrderLineImportPricesAsync(
        RefreshOrderLineImportPricesInput input
    );
}

internal interface IStoreOrderHeaderCommandStore
{
    Task<StoreOrderManagementResult<bool>> UpdateOrderHeaderAsync(
        UpdateOrderHeaderInput input
    );

    Task<StoreOrderManagementResult<bool>> UpdateOrderOutboundDateAsync(
        UpdateOrderOutboundDateInput input
    );

    Task<StoreOrderManagementResult<bool>> DeleteOrderAsync(DeleteOrderInput input);
}

internal interface IStoreOrderProductStatusCommandStore
{
    Task<StoreOrderManagementResult<bool>> UpdateProductStatusAsync(
        UpdateProductStatusInput input
    );

    Task<StoreOrderManagementResult<bool>> BatchUpdateProductStatusAsync(
        BatchUpdateProductStatusInput input
    );
}
