using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Application;

internal static class StoreOrderLifecycleResponses
{
    internal static ApiResponse<T> Failure<T>(StoreOrderLifecycleFailure failure) => new()
    {
        Success = false,
        ErrorCode = failure.ErrorCode,
        Message = failure.Message,
    };

    internal static ApiResponse<T> Failure<T>(Exception exception) => new()
    {
        Success = false,
        Message = exception.Message,
    };

    internal static ApiResponse<bool> BooleanSuccess() => new()
    {
        Success = true,
        Data = true,
    };

    internal static ApiResponse<bool> StatusChangeSuccess(int targetStatus) => new()
    {
        Success = true,
        Data = true,
        Message = $"Status changed to {(targetStatus == StoreOrderLifecycleRules.Submitted ? "Submitted" : "Completed")}",
    };

    internal static ApiResponse<int> BatchSuccess(int updatedCount) => new()
    {
        Success = true,
        Data = updatedCount,
        Message = $"Updated {updatedCount} orders",
    };
}
