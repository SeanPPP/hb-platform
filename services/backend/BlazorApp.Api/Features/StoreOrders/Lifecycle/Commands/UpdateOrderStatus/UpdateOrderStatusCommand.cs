namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.UpdateOrderStatus;

internal sealed record UpdateOrderStatusCommand(
    string OrderGuid,
    int NewStatus,
    bool BypassPreorderGate = false
);
