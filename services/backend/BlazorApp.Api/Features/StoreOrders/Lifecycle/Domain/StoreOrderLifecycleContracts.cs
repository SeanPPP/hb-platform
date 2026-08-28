namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;

internal sealed record StoreOrderLifecycleSnapshot(string OrderGuid, int? FlowStatus);

internal sealed record StoreOrderLifecycleFailure(string? ErrorCode, string Message);

internal static class StoreOrderLifecycleFailures
{
    internal static readonly StoreOrderLifecycleFailure OrderNotFound =
        new(null, "订单不存在");

    internal static readonly StoreOrderLifecycleFailure OrderNotFoundEnglish =
        new(null, "Order not found");

    internal static readonly StoreOrderLifecycleFailure BatchOrderNotFound =
        new("ORDER_NOT_FOUND", "部分订单不存在或已删除");

    internal static readonly StoreOrderLifecycleFailure NoOrdersSpecified =
        new(null, "No orders specified");

    internal static readonly StoreOrderLifecycleFailure OrderStatusConflict =
        new("ORDER_STATUS_CONFLICT", "订单状态已被其他操作更新，请刷新后重试");

    internal static readonly StoreOrderLifecycleFailure BatchUpdateFailed =
        new(null, "批量更新订单状态失败");
}

internal sealed class StoreOrderStatusConcurrencyException : Exception
{
    internal StoreOrderStatusConcurrencyException()
        : base("订单状态已被其他操作更新") { }
}
