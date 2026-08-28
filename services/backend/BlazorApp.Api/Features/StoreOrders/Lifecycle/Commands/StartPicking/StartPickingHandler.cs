using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Queries;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.StartPicking;

internal sealed class StartPickingHandler(
    IStoreOrderLifecycleQueryHandler queries,
    IStoreOrderLifecycleCommandStore commands,
    IStoreOrderLifecycleExecutionContext executionContext,
    StartPickingValidator validator,
    ILogger<StartPickingHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(StartPickingCommand command)
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

            if (validator.IsAlreadySatisfied(order))
            {
                return StoreOrderLifecycleResponses.BooleanSuccess();
            }

            var failure = validator.Validate(order);
            if (failure != null)
            {
                return StoreOrderLifecycleResponses.Failure<bool>(failure);
            }

            var affected = await commands.CompareExchangeStatusAsync(
                command.OrderGuid,
                StoreOrderLifecycleRules.Submitted,
                StoreOrderLifecycleRules.Picking,
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
            logger.LogError(exception, "StartPickingAsync failed");
            return StoreOrderLifecycleResponses.Failure<bool>(exception);
        }
    }
}
