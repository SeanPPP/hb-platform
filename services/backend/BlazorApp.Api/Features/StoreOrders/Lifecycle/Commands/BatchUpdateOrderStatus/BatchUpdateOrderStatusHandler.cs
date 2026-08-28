using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Queries;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.BatchUpdateOrderStatus;

internal sealed class BatchUpdateOrderStatusHandler(
    IStoreOrderLifecycleQueryHandler queries,
    IStoreOrderLifecycleCommandStore commands,
    IStoreOrderLifecycleExecutionContext executionContext,
    BatchUpdateOrderStatusValidator validator,
    ILogger<BatchUpdateOrderStatusHandler> logger
)
{
    internal async Task<ApiResponse<int>> HandleAsync(BatchUpdateOrderStatusCommand command)
    {
        try
        {
            var requestFailure = validator.ValidateRequest(command);
            if (requestFailure != null)
            {
                return StoreOrderLifecycleResponses.Failure<int>(requestFailure);
            }

            var canBypassPreorderGate =
                command.BypassPreorderGate
                || executionContext.IsWarehouseStaffOnly
                || await executionContext.CanBypassPreorderCompletionAsync();
            var distinctOrderGuids = validator.NormalizeOrderGuids(command.OrderGuids!);
            if (distinctOrderGuids.Count == 0)
            {
                return StoreOrderLifecycleResponses.Failure<int>(
                    StoreOrderLifecycleFailures.NoOrdersSpecified
                );
            }

            logger.LogInformation(
                "Batch updating {Count} orders to status {NewStatus}",
                distinctOrderGuids.Count,
                command.NewStatus
            );

            ApiResponse<int>? response = null;
            var transaction = await commands.ExecuteInTransactionAsync(async () =>
            {
                var orders = await queries.HandleAsync(
                    new GetStoreOrderLifecyclesQuery(distinctOrderGuids)
                );
                if (orders.Count != distinctOrderGuids.Count)
                {
                    response = StoreOrderLifecycleResponses.Failure<int>(
                        StoreOrderLifecycleFailures.BatchOrderNotFound
                    );
                    return;
                }

                var transitionFailure = validator.ValidateTransitions(
                    orders,
                    command.NewStatus,
                    canBypassPreorderGate
                );
                if (transitionFailure != null)
                {
                    response = StoreOrderLifecycleResponses.Failure<int>(transitionFailure);
                    return;
                }

                var updatedBy = executionContext.ActorName;
                var updatedAt = executionContext.LocalNow;
                var updatedCount = 0;
                // 按事务内读取到的源状态分组 CAS；任一组数量不符都会抛错并回滚已更新组。
                foreach (var group in orders.GroupBy(order => order.FlowStatus))
                {
                    var groupOrderGuids = group.Select(order => order.OrderGuid).ToList();
                    var affected = await commands.CompareExchangeStatusGroupAsync(
                        groupOrderGuids,
                        group.Key,
                        command.NewStatus,
                        updatedAt,
                        updatedBy
                    );
                    if (affected != groupOrderGuids.Count)
                    {
                        throw new StoreOrderStatusConcurrencyException();
                    }

                    updatedCount += affected;
                }

                response = StoreOrderLifecycleResponses.BatchSuccess(updatedCount);
            });

            if (!transaction.IsSuccess)
            {
                if (transaction.ErrorException is StoreOrderStatusConcurrencyException)
                {
                    return StoreOrderLifecycleResponses.Failure<int>(
                        StoreOrderLifecycleFailures.OrderStatusConflict
                    );
                }

                throw transaction.ErrorException
                    ?? new InvalidOperationException("批量更新订单状态事务失败");
            }

            return response
                ?? StoreOrderLifecycleResponses.Failure<int>(
                    StoreOrderLifecycleFailures.BatchUpdateFailed
                );
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "BatchUpdateOrderStatusAsync failed");
            return StoreOrderLifecycleResponses.Failure<int>(exception);
        }
    }
}
