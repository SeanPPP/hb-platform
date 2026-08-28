namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.BatchUpdateOrderStatus;

internal sealed record BatchUpdateOrderStatusCommand(
    List<string>? OrderGuids,
    int NewStatus,
    bool BypassPreorderGate = false
);
