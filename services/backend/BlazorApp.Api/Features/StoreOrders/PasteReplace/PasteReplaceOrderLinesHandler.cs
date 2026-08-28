using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace;

internal sealed class PasteReplaceOrderLinesHandler(
    PasteReplaceOrderLinesValidator validator,
    PasteReplaceOrderLinesQuery query,
    PasteReplaceOrderLinesCommand command,
    ILogger<PasteReplaceOrderLinesHandler> logger
) : IStoreOrderPasteReplaceExecutor
{
    public async Task<ApiResponse<bool>> PasteReplaceOrderLinesAsync(
        PasteReplaceOrderLinesDto request
    )
    {
        try
        {
            // 保持旧入口的错误优先级：先判断订单，再验证目标字段和动作。
            var order = await query.GetEditableOrderAsync(request.OrderGUID);
            if (order == null)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = "Order not found or not editable",
                };
            }

            var validation = validator.Validate(request);
            if (!validation.IsValid)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = validation.ErrorMessage,
                };
            }

            var plan = await query.PrepareAsync(order, request.Items, request.TargetField);
            await command.ExecuteAsync(plan);
            return new ApiResponse<bool> { Success = true, Data = true };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PasteReplaceOrderLinesAsync failed");
            return new ApiResponse<bool> { Success = false, Message = ex.Message };
        }
    }
}
