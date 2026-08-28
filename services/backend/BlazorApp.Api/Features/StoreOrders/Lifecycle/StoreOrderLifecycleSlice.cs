using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.BatchUpdateOrderStatus;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.CompleteOrder;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.StartPicking;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.UpdateOrderStatus;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle;

internal sealed class StoreOrderLifecycleSlice(
    CompleteOrderHandler completeOrder,
    StartPickingHandler startPicking,
    UpdateOrderStatusHandler updateOrderStatus,
    BatchUpdateOrderStatusHandler batchUpdateOrderStatus
) : IStoreOrderLifecycleSlice
{
    public Task<ApiResponse<bool>> CompleteOrderAsync(string orderGuid)
    {
        return completeOrder.HandleAsync(new CompleteOrderCommand(orderGuid));
    }

    public Task<ApiResponse<bool>> StartPickingAsync(string orderGuid)
    {
        return startPicking.HandleAsync(new StartPickingCommand(orderGuid));
    }

    public Task<ApiResponse<bool>> UpdateOrderStatusAsync(
        string orderGuid,
        int newStatus,
        bool bypassPreorderGate = false
    )
    {
        return updateOrderStatus.HandleAsync(
            new UpdateOrderStatusCommand(orderGuid, newStatus, bypassPreorderGate)
        );
    }

    public Task<ApiResponse<int>> BatchUpdateOrderStatusAsync(
        List<string> orderGuids,
        int newStatus,
        bool bypassPreorderGate = false
    )
    {
        return batchUpdateOrderStatus.HandleAsync(
            new BatchUpdateOrderStatusCommand(orderGuids, newStatus, bypassPreorderGate)
        );
    }
}
