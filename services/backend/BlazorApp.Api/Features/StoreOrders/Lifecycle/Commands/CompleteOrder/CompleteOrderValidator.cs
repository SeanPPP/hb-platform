using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.CompleteOrder;

internal sealed class CompleteOrderValidator
{
    internal StoreOrderLifecycleFailure? Validate(StoreOrderLifecycleSnapshot order)
    {
        return StoreOrderLifecycleRules.ValidateComplete(order.FlowStatus);
    }
}
