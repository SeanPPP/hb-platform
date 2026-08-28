using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.StartPicking;

internal sealed class StartPickingValidator
{
    internal bool IsAlreadySatisfied(StoreOrderLifecycleSnapshot order)
    {
        return StoreOrderLifecycleRules.IsStartPickingAlreadySatisfied(order.FlowStatus);
    }

    internal StoreOrderLifecycleFailure? Validate(StoreOrderLifecycleSnapshot order)
    {
        return StoreOrderLifecycleRules.ValidateStartPicking(order.FlowStatus);
    }
}
