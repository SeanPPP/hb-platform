using BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement;

public interface IStoreOrderOrderManagementSlice
{
    Task<ApiResponse<bool>> AddOrderLineAsync(AddOrderLineDto request);

    Task<ApiResponse<bool>> BatchAddOrderLineAsync(BatchAddOrderLineDto request);

    Task<ApiResponse<bool>> UpdateOrderLineAsync(UpdateOrderLineDto request);

    Task<ApiResponse<bool>> RemoveOrderLineAsync(RemoveOrderLineDto request);

    Task<ApiResponse<bool>> BatchUpdateOrderLineAsync(BatchUpdateOrderLineDto request);

    Task<ApiResponse<RefreshStoreOrderImportPricesResultDto>> RefreshOrderLineImportPricesAsync(
        RefreshStoreOrderImportPricesDto request
    );

    Task<ApiResponse<bool>> UpdateOrderHeaderAsync(UpdateOrderHeaderDto request);

    Task<ApiResponse<bool>> UpdateOrderOutboundDateAsync(
        UpdateOrderOutboundDateDto request
    );

    Task<ApiResponse<bool>> DeleteOrderAsync(string orderGuid);

    Task<ApiResponse<bool>> UpdateProductStatusAsync(UpdateProductStatusDto request);

    Task<ApiResponse<bool>> BatchUpdateProductStatusAsync(
        BatchUpdateProductStatusDto request
    );
}

internal sealed class StoreOrderOrderManagementSlice(
    AddOrderLineHandler addOrderLineHandler,
    BatchAddOrderLineHandler batchAddOrderLineHandler,
    UpdateOrderLineHandler updateOrderLineHandler,
    RemoveOrderLineHandler removeOrderLineHandler,
    BatchUpdateOrderLineHandler batchUpdateOrderLineHandler,
    RefreshOrderLineImportPricesHandler refreshOrderLineImportPricesHandler,
    UpdateOrderHeaderHandler updateOrderHeaderHandler,
    UpdateOrderOutboundDateHandler updateOrderOutboundDateHandler,
    DeleteOrderHandler deleteOrderHandler,
    UpdateProductStatusHandler updateProductStatusHandler,
    BatchUpdateProductStatusHandler batchUpdateProductStatusHandler
) : IStoreOrderOrderManagementSlice
{
    public Task<ApiResponse<bool>> AddOrderLineAsync(AddOrderLineDto request)
    {
        return addOrderLineHandler.HandleAsync(new AddOrderLineCommand(request));
    }

    public Task<ApiResponse<bool>> BatchAddOrderLineAsync(BatchAddOrderLineDto request)
    {
        return batchAddOrderLineHandler.HandleAsync(new BatchAddOrderLineCommand(request));
    }

    public Task<ApiResponse<bool>> UpdateOrderLineAsync(UpdateOrderLineDto request)
    {
        return updateOrderLineHandler.HandleAsync(new UpdateOrderLineCommand(request));
    }

    public Task<ApiResponse<bool>> RemoveOrderLineAsync(RemoveOrderLineDto request)
    {
        return removeOrderLineHandler.HandleAsync(new RemoveOrderLineCommand(request));
    }

    public Task<ApiResponse<bool>> BatchUpdateOrderLineAsync(BatchUpdateOrderLineDto request)
    {
        return batchUpdateOrderLineHandler.HandleAsync(
            new BatchUpdateOrderLineCommand(request)
        );
    }

    public Task<ApiResponse<RefreshStoreOrderImportPricesResultDto>> RefreshOrderLineImportPricesAsync(
        RefreshStoreOrderImportPricesDto request
    )
    {
        return refreshOrderLineImportPricesHandler.HandleAsync(
            new RefreshOrderLineImportPricesCommand(request)
        );
    }

    public Task<ApiResponse<bool>> UpdateOrderHeaderAsync(UpdateOrderHeaderDto request)
    {
        return updateOrderHeaderHandler.HandleAsync(new UpdateOrderHeaderCommand(request));
    }

    public Task<ApiResponse<bool>> UpdateOrderOutboundDateAsync(
        UpdateOrderOutboundDateDto request
    )
    {
        return updateOrderOutboundDateHandler.HandleAsync(
            new UpdateOrderOutboundDateCommand(request)
        );
    }

    public Task<ApiResponse<bool>> DeleteOrderAsync(string orderGuid)
    {
        return deleteOrderHandler.HandleAsync(new DeleteOrderCommand(orderGuid));
    }

    public Task<ApiResponse<bool>> UpdateProductStatusAsync(UpdateProductStatusDto request)
    {
        return updateProductStatusHandler.HandleAsync(
            new UpdateProductStatusCommand(request)
        );
    }

    public Task<ApiResponse<bool>> BatchUpdateProductStatusAsync(
        BatchUpdateProductStatusDto request
    )
    {
        return batchUpdateProductStatusHandler.HandleAsync(
            new BatchUpdateProductStatusCommand(request)
        );
    }
}
