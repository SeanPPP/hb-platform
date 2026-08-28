using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React.StoreOrders;

public abstract class StoreOrderControllerBase(
    IStoreOrderAccessPolicy accessPolicy
) : ControllerBase
{
    internal const string BaseRoute = "api/react/v1/store-order";

    protected IStoreOrderAccessPolicy AccessPolicy { get; } = accessPolicy;

    protected IActionResult? ForbidIf(StoreOrderAccessDecision decision)
    {
        return decision.IsForbidden ? Forbid() : null;
    }

    protected bool IsScanCartMutationRoute()
    {
        return Request.Path.Value?.Contains(
            "/cart/scan-",
            StringComparison.OrdinalIgnoreCase
        ) == true;
    }

    protected IActionResult? MapPreorderGateServiceError(
        string? errorCode,
        string? message,
        object? details
    )
    {
        return errorCode switch
        {
            "PREORDER_REQUIRED" when details is PreorderGateResult gate => Conflict(
                new ApiResponse<PreorderGateResult>
                {
                    Success = false,
                    Message = message ?? "请先完成当前有效的 Preorder，再提交普通订货",
                    ErrorCode = errorCode,
                    Data = gate,
                    Details = gate,
                }
            ),
            "PREORDER_REQUIRED" => Conflict(
                ApiResponse<object>.Error(
                    message ?? "请先完成当前有效的 Preorder，再提交普通订货",
                    errorCode,
                    details
                )
            ),
            "PREORDER_GATE_UNAVAILABLE" => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Error(
                    message ?? "Preorder 状态暂时无法确认，请稍后重试",
                    errorCode,
                    details
                )
            ),
            "PREORDER_STORE_IDENTITY_CHANGED" => Conflict(
                ApiResponse<object>.Error(
                    message ?? "分店标识已变化，请刷新后重试",
                    errorCode,
                    details
                )
            ),
            _ => null,
        };
    }
}
