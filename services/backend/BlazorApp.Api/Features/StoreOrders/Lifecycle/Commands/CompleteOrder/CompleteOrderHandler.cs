using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Queries;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.CompleteOrder;

internal sealed class CompleteOrderHandler(
    IStoreOrderLifecycleQueryHandler queries,
    IStoreOrderLifecycleCommandStore commands,
    IStoreOrderLifecycleExecutionContext executionContext,
    CompleteOrderValidator validator,
    ILogger<CompleteOrderHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(CompleteOrderCommand command)
    {
        try
        {
            var order = await queries.HandleAsync(
                new GetStoreOrderLifecycleQuery(command.OrderGuid)
            );
            if (order == null)
            {
                return StoreOrderLifecycleResponses.Failure<bool>(
                    StoreOrderLifecycleFailures.OrderNotFound
                );
            }

            var failure = validator.Validate(order);
            if (failure != null)
            {
                return StoreOrderLifecycleResponses.Failure<bool>(failure);
            }

            var affected = await commands.CompareExchangeStatusAsync(
                command.OrderGuid,
                StoreOrderLifecycleRules.Submitted,
                StoreOrderLifecycleRules.Completed,
                executionContext.LocalNow,
                executionContext.ActorName
            );
            return affected == 1
                ? StoreOrderLifecycleResponses.BooleanSuccess()
                : StoreOrderLifecycleResponses.Failure<bool>(
                    StoreOrderLifecycleFailures.OrderStatusConflict
                );
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CompleteOrderAsync failed");
            return StoreOrderLifecycleResponses.Failure<bool>(exception);
        }
    }
}
