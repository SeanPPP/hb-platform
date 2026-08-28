using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement.Application.Ports;

internal interface IStoreOrderPlacementGateCoordinator
{
    Task<ApiResponse<T>> ExecuteWithProcessGateAsync<T>(
        string storeCode,
        bool bypassPreorderGate,
        string entryPoint,
        Func<StoreOrderPlacementGateContext, Task<ApiResponse<T>>> command
    );

    Task<StoreOrderPlacementGateDecision> IsBlockedInsideTransactionAsync(
        StoreOrderPlacementGateContext context,
        string storeCode,
        string entryPoint
    );
}

internal interface IStoreOrderPlacementOrderStore
{
    Task<ApiResponse<T>> ExecuteInTransactionAsync<T>(
        Func<Task<ApiResponse<T>>> command
    );

    Task<string> InsertCreatedOrderAsync(
        string storeCode,
        string? remarks,
        string orderNo,
        DateTime now,
        string actorName
    );

    Task<StoreOrderCopySource?> GetCopySourceAsync(string sourceOrderGuid);

    Task<CopyOrderResultDto> InsertCopiedOrderAsync(
        StoreOrderCopySource source,
        string targetStoreCode,
        bool copyOrderQuantity,
        bool copyAllocQuantity,
        string orderNo,
        DateTime now,
        string actorName
    );
}

internal interface IStoreOrderPlacementExecutionContext
{
    string ActorName { get; }

    DateTime LocalNow { get; }

    Task<bool> CanBypassPreorderCompletionAsync();
}
