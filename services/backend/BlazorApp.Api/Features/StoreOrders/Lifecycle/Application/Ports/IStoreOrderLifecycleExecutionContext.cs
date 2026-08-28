namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;

internal interface IStoreOrderLifecycleExecutionContext
{
    string ActorName { get; }

    bool IsWarehouseStaffOnly { get; }

    DateTime LocalNow { get; }

    Task<bool> CanBypassPreorderCompletionAsync();
}
