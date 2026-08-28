using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle;

public interface IStoreOrderLifecycleSlice
{
    Task<ApiResponse<bool>> CompleteOrderAsync(string orderGuid);

    Task<ApiResponse<bool>> StartPickingAsync(string orderGuid);

    Task<ApiResponse<bool>> UpdateOrderStatusAsync(
        string orderGuid,
        int newStatus,
        bool bypassPreorderGate = false
    );

    Task<ApiResponse<int>> BatchUpdateOrderStatusAsync(
        List<string> orderGuids,
        int newStatus,
        bool bypassPreorderGate = false
    );
}
