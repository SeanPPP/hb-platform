using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderManagement;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement;
using BlazorApp.Api.Features.StoreOrders.PasteReplace;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React.StoreOrders;

[ApiController]
[Route(StoreOrderControllerBase.BaseRoute)]
[Authorize]
public sealed class StoreOrderManagementController(
    IStoreOrderOrderManagementSlice orderManagement,
    IStoreOrderPlacementSlice orderPlacement,
    IStoreOrderPasteReplaceExecutor pasteReplace,
    IStoreOrderPasteReplaceJobService pasteReplaceJobService,
    IStoreOrderAccessPolicy accessPolicy,
    ILogger<StoreOrderManagementController> logger
) : StoreOrderControllerBase(accessPolicy)
{
    /// <summary>
    /// 创建新订单 (FlowStatus=1)
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateStoreOrderDto request)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireCreateOrderAsync(request.StoreCode)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderPlacement.CreateOrderAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            var atomicGateError = MapPreorderGateServiceError(
                result.ErrorCode,
                result.Message,
                result.Details
            );
            if (atomicGateError != null)
            {
                return atomicGateError;
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateOrder failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 添加商品到指定订单
    /// </summary>
    [HttpPost("line/add")]
    public async Task<IActionResult> AddOrderLine([FromBody] AddOrderLineDto request)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.AddOrderLineAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AddOrderLine failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 批量添加商品到指定订单
    /// </summary>
    [HttpPost("line/batch-add")]
    public async Task<IActionResult> BatchAddOrderLine(
        [FromBody] BatchAddOrderLineDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.BatchAddOrderLineAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BatchAddOrderLine failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// Excel 粘贴覆盖订单行
    /// </summary>
    [HttpPost("line/paste-replace")]
    public async Task<IActionResult> PasteReplaceOrderLines(
        [FromBody] PasteReplaceOrderLinesDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await pasteReplace.PasteReplaceOrderLinesAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PasteReplaceOrderLines failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 创建 Excel 粘贴覆盖订单行后台 job
    /// </summary>
    [HttpPost("line/paste-replace/jobs")]
    public async Task<IActionResult> CreatePasteReplaceOrderLinesJob(
        [FromBody] PasteReplaceOrderLinesDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var job = await pasteReplaceJobService.StartJobAsync(
                request,
                HttpContext.RequestAborted
            );
            return Ok(new { success = true, data = job });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreatePasteReplaceOrderLinesJob failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取 Excel 粘贴覆盖订单行后台 job 状态
    /// </summary>
    [HttpGet("line/paste-replace/jobs/{jobId}")]
    public async Task<IActionResult> GetPasteReplaceOrderLinesJob(string jobId)
    {
        try
        {
            var job = await pasteReplaceJobService.GetJobAsync(
                jobId,
                HttpContext.RequestAborted
            );
            if (job == null)
            {
                return NotFound(new { success = false, message = "任务不存在" });
            }

            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(job.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            return Ok(new { success = true, data = job });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetPasteReplaceOrderLinesJob failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 更新指定订单行数量
    /// </summary>
    [HttpPost("line/update")]
    public async Task<IActionResult> UpdateOrderLine([FromBody] UpdateOrderLineDto request)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.UpdateOrderLineAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateOrderLine failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 软删除指定订单行
    /// </summary>
    [HttpPost("line/remove")]
    public async Task<IActionResult> RemoveOrderLine([FromBody] RemoveOrderLineDto request)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.RemoveOrderLineAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RemoveOrderLine failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 批量更新订单行数量或价格
    /// </summary>
    [HttpPost("line/batch-update")]
    public async Task<IActionResult> BatchUpdateOrderLine(
        [FromBody] BatchUpdateOrderLineDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderLineMutationAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.BatchUpdateOrderLineAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BatchUpdateOrderLine failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 从仓库商品表刷新订单明细进口价，允许管理员/仓库管理员修正已完成订单成本。
    /// </summary>
    [HttpPost("line/refresh-import-prices")]
    public async Task<IActionResult> RefreshOrderLineImportPrices(
        [FromBody] RefreshStoreOrderImportPricesDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireImportPriceRefreshAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.RefreshOrderLineImportPricesAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RefreshOrderLineImportPrices failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 更新商品状态 (单个)
    /// </summary>
    [HttpPost("product/status")]
    public async Task<IActionResult> UpdateProductStatus(
        [FromBody] UpdateProductStatusDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderManagementEditAsync()
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.UpdateProductStatusAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateProductStatus failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 批量更新商品状态
    /// </summary>
    [HttpPost("product/batch-status")]
    public async Task<IActionResult> BatchUpdateProductStatus(
        [FromBody] BatchUpdateProductStatusDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderManagementEditAsync()
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.BatchUpdateProductStatusAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BatchUpdateProductStatus failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 更新订单头信息
    /// </summary>
    [HttpPost("header/update")]
    public async Task<IActionResult> UpdateOrderHeader(
        [FromBody] UpdateOrderHeaderDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderEditAsync(
                    request.OrderGuid,
                    request.StoreCode
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.UpdateOrderHeaderAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateOrderHeader failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 更新订单出库日期，可选同步完成订单。
    /// </summary>
    [HttpPost("outbound-date")]
    public async Task<IActionResult> UpdateOrderOutboundDate(
        [FromBody] UpdateOrderOutboundDateDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderEditAsync(request.OrderGuid)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.UpdateOrderOutboundDateAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateOrderOutboundDate failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 删除订单 (软删除)
    /// </summary>
    [HttpDelete("{orderGuid}")]
    public async Task<IActionResult> DeleteOrder(string orderGuid)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderDeleteAsync(orderGuid)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderManagement.DeleteOrderAsync(orderGuid);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DeleteOrder failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 复制订单到另一个分店
    /// </summary>
    [HttpPost("copy")]
    public async Task<IActionResult> CopyOrder([FromBody] CopyOrderDto request)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireCopyOrderAsync(
                    request.SourceOrderGUID,
                    request.TargetStoreCode
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await orderPlacement.CopyOrderAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            var atomicGateError = MapPreorderGateServiceError(
                result.ErrorCode,
                result.Message,
                result.Details
            );
            if (atomicGateError != null)
            {
                return atomicGateError;
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CopyOrder failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }
}
