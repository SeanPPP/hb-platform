using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.BatchUpdateOrderStatus;

internal sealed class BatchUpdateOrderStatusValidator
{
    internal StoreOrderLifecycleFailure? ValidateRequest(BatchUpdateOrderStatusCommand command)
    {
        var targetFailure = StoreOrderLifecycleRules.ValidateStatusTarget(command.NewStatus);
        if (targetFailure != null)
        {
            return targetFailure;
        }

        return command.OrderGuids == null || command.OrderGuids.Count == 0
            ? StoreOrderLifecycleFailures.NoOrdersSpecified
            : null;
    }

    internal IReadOnlyList<string> NormalizeOrderGuids(IEnumerable<string> orderGuids)
    {
        return orderGuids
            .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
            .Select(orderGuid => orderGuid.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    internal StoreOrderLifecycleFailure? ValidateTransitions(
        IReadOnlyList<StoreOrderLifecycleSnapshot> orders,
        int targetStatus,
        bool canBypassPreorderGate
    )
    {
        foreach (var order in orders)
        {
            if (order.FlowStatus == targetStatus)
            {
                // 批量接口保留同目标状态的幂等兼容，后续 CAS 仍计入处理数量。
                continue;
            }

            var failure = StoreOrderLifecycleRules.ValidateStatusTransition(
                order.FlowStatus,
                targetStatus,
                canBypassPreorderGate
            );
            if (failure != null)
            {
                return failure;
            }
        }

        return null;
    }
}
