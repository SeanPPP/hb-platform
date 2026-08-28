using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.UpdateOrderStatus;

internal sealed class UpdateOrderStatusValidator
{
    internal StoreOrderLifecycleFailure? ValidateRequest(UpdateOrderStatusCommand command)
    {
        return StoreOrderLifecycleRules.ValidateStatusTarget(command.NewStatus);
    }

    internal StoreOrderLifecycleFailure? ValidateTransition(
        UpdateOrderStatusCommand command,
        StoreOrderLifecycleSnapshot order,
        bool canBypassPreorderGate
    )
    {
        if (order.FlowStatus == command.NewStatus)
        {
            return new StoreOrderLifecycleFailure(null, "Status is already the target status");
        }

        return StoreOrderLifecycleRules.ValidateStatusTransition(
            order.FlowStatus,
            command.NewStatus,
            canBypassPreorderGate
        );
    }
}
