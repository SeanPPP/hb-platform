using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Domain;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement.Commands.SubmitOrder;

internal sealed record SubmitOrderCommand(SubmitStoreOrderRequestDto? Request);

internal sealed class SubmitOrderValidator
{
    internal StoreOrderPlacementValidationFailure? Validate(SubmitOrderCommand command)
    {
        return command.Request == null || string.IsNullOrWhiteSpace(command.Request.StoreCode)
            ? new StoreOrderPlacementValidationFailure("StoreCode is required")
            : null;
    }
}

internal sealed class SubmitOrderHandler(
    SubmitOrderValidator validator,
    IStoreOrderCartOwnerScope ownerScope,
    IStoreOrderCartCommandCoordinator cartCoordinator,
    IStoreOrderCartPlacementPort cartPort,
    IStoreOrderPlacementGateCoordinator gateCoordinator,
    IStoreOrderPlacementExecutionContext executionContext,
    IOrderNumberGenerator orderNumberGenerator,
    ILogger<SubmitOrderHandler> logger
)
{
    internal async Task<ApiResponse<bool>> HandleAsync(SubmitOrderCommand command)
    {
        var validationFailure = validator.Validate(command);
        if (validationFailure != null)
        {
            return StoreOrderPlacementResponses.ValidationFailure<bool>(
                validationFailure.Value
            );
        }

        var request = command.Request!;
        var storeCode = request.StoreCode.Trim();
        var bypassPreorderGate = request.BypassPreorderGate
            || ownerScope.IsWarehouseStaffOnly
            || await executionContext.CanBypassPreorderCompletionAsync();
        try
        {
            return await gateCoordinator.ExecuteWithProcessGateAsync(
                storeCode,
                bypassPreorderGate,
                "React.SubmitOrder",
                async gateContext =>
                {
                    // 锁序固定为 StoreGate(process) -> Cart(process) -> Cart DB -> StoreGate DB。
                    var cartScope = ownerScope.Resolve(storeCode);
                    return await cartCoordinator.ExecuteAsync(cartScope, async () =>
                    {
                        var gateDecision = await gateCoordinator.IsBlockedInsideTransactionAsync(
                            gateContext,
                            storeCode,
                            "React.SubmitOrder"
                        );
                        if (gateDecision.IsBlocked)
                        {
                            return StoreOrderPlacementResponses.PreorderRequired<bool>(
                                "请先完成当前有效的 Preorder，再提交普通订货",
                                gateDecision.Details
                            );
                        }

                        // StoreGate 等待及数据库锁取得后才读取购物车和状态。
                        var cart = await cartPort.GetActiveForSubmissionAsync(cartScope);
                        if (cart == null)
                        {
                            return new ApiResponse<bool>
                            {
                                Success = false,
                                Message = "No active cart found",
                            };
                        }

                        if (await cartPort.CountActiveItemsAsync(cart.OrderGuid) == 0)
                        {
                            return new ApiResponse<bool>
                            {
                                Success = false,
                                Message = "Cart is empty",
                            };
                        }

                        var orderNo = await orderNumberGenerator.GetNextOrderNoAsync();
                        var affected = await cartPort.CompareExchangeSubmitAsync(
                            cart,
                            orderNo,
                            request.Remarks,
                            executionContext.LocalNow,
                            executionContext.ActorName
                        );
                        return affected == 1
                            ? new ApiResponse<bool> { Success = true, Data = true }
                            : StoreOrderPlacementResponses.OrderStatusConflict<bool>();
                    });
                }
            );
        }
        catch (PreorderBusinessException exception)
        {
            logger.LogWarning(exception, "SubmitOrder Preorder gate unavailable");
            return new ApiResponse<bool>
            {
                Success = false,
                ErrorCode = exception.ErrorCode,
                Message = exception.Message,
                Details = exception.Details,
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "SubmitOrderAsync failed");
            return new ApiResponse<bool>
            {
                Success = false,
                Message = "订单提交失败，请稍后重试",
            };
        }
    }
}
