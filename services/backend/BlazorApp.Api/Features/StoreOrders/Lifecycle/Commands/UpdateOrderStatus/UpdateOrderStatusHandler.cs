using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Queries;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.UpdateOrderStatus;

internal sealed class UpdateOrderStatusHandler(
    IStoreOrderLifecycleQueryHandler queries,
    IStoreOrderLifecycleCommandStore commands,
    IStoreOrderLifecycleExecutionContext executionContext,
    UpdateOrderStatusValidator validator,
    ILogger<UpdateOrderStatusHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(UpdateOrderStatusCommand command)
    {
        try
        {
            var requestFailure = validator.ValidateRequest(command);
            if (requestFailure != null)
            {
                return StoreOrderLifecycleResponses.Failure<bool>(requestFailure);
            }

            var order = await queries.HandleAsync(
                new GetStoreOrderLifecycleQuery(command.OrderGuid)
            );
            if (order == null)
            {
                return StoreOrderLifecycleResponses.Failure<bool>(
                    StoreOrderLifecycleFailures.OrderNotFoundEnglish
                );
            }

            var canBypassPreorderGate =
                command.BypassPreorderGate
                || executionContext.IsWarehouseStaffOnly
                || await executionContext.CanBypassPreorderCompletionAsync();
            var transitionFailure = validator.ValidateTransition(
                command,
                order,
                canBypassPreorderGate
            );
            if (transitionFailure != null)
            {
                return StoreOrderLifecycleResponses.Failure<bool>(transitionFailure);
            }

            var updatedBy = executionContext.ActorName;
            logger.LogInformation(
                "Updating order {OrderGUID} status from {OldStatus} to {NewStatus}",
                command.OrderGuid,
                order.FlowStatus,
                command.NewStatus
            );

            var updatedAt = executionContext.LocalNow;
            var affected = await commands.CompareExchangeStatusAsync(
                command.OrderGuid,
                order.FlowStatus,
                command.NewStatus,
                updatedAt,
                updatedBy
            );
            return affected == 1
                ? StoreOrderLifecycleResponses.StatusChangeSuccess(command.NewStatus)
                : StoreOrderLifecycleResponses.Failure<bool>(
                    StoreOrderLifecycleFailures.OrderStatusConflict
                );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "UpdateOrderStatusAsync failed for order {OrderGUID}",
                command.OrderGuid
            );
            return StoreOrderLifecycleResponses.Failure<bool>(exception);
        }
    }
}
