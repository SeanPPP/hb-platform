using BlazorApp.Api.Features.StoreOrders.OrderManagement.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderManagement.Application;

internal static class StoreOrderManagementResponseMapper
{
    internal static ApiResponse<T> Map<T>(StoreOrderManagementResult<T> result)
    {
        return result.Success
            ? new ApiResponse<T> { Success = true, Data = result.Data! }
            : new ApiResponse<T>
            {
                Success = false,
                Message = result.ErrorMessage ?? string.Empty,
            };
    }

    internal static ApiResponse<T> ValidationError<T>(string message)
    {
        return new ApiResponse<T> { Success = false, Message = message };
    }
}
