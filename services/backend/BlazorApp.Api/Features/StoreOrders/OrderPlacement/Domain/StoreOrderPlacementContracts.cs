using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement.Domain;

internal static class StoreOrderPlacementErrorCodes
{
    internal const string PreorderRequired = "PREORDER_REQUIRED";
    internal const string PreorderGateUnavailable = "PREORDER_GATE_UNAVAILABLE";
    internal const string PreorderSubmitEndpointRequired =
        "PREORDER_SUBMIT_ENDPOINT_REQUIRED";
    internal const string OrderStatusConflict = "ORDER_STATUS_CONFLICT";
}

internal readonly record struct StoreOrderPlacementValidationFailure(string Message);

internal sealed record StoreOrderPlacementGateContext(string? StoreLockResource)
{
    internal bool RequiresEvaluation => !string.IsNullOrWhiteSpace(StoreLockResource);
}

internal sealed record StoreOrderPlacementGateDecision(
    bool IsBlocked,
    PreorderGateResult? Details = null
);

internal sealed record StoreOrderCopySource(
    string OrderGuid,
    string? OrderNo,
    int? FlowStatus,
    IReadOnlyList<WareHouseOrderDetails> Details
);

internal static class StoreOrderPlacementResponses
{
    internal static ApiResponse<T> ValidationFailure<T>(
        StoreOrderPlacementValidationFailure failure
    ) => new()
    {
        Success = false,
        Message = failure.Message,
    };

    internal static ApiResponse<T> PreorderRequired<T>(
        string message,
        PreorderGateResult? details
    ) => new()
    {
        Success = false,
        ErrorCode = StoreOrderPlacementErrorCodes.PreorderRequired,
        Message = message,
        Details = details,
    };

    internal static ApiResponse<T> OrderStatusConflict<T>() => new()
    {
        Success = false,
        ErrorCode = StoreOrderPlacementErrorCodes.OrderStatusConflict,
        Message = "订单状态已被其他操作更新，请刷新后重试",
    };
}
