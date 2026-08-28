using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React.StoreOrders;

/// <summary>
/// 分店订货发票邮件接口。
/// </summary>
[ApiController]
[Route(StoreOrderControllerBase.BaseRoute)]
[Authorize]
public sealed class StoreOrderInvoiceController(
    IStoreOrderInvoiceEmailJobService invoiceEmailJobService,
    IStoreOrderInvoiceEmailTextTranslationService invoiceEmailTextTranslationService,
    IStoreOrderAccessPolicy accessPolicy,
    ILogger<StoreOrderInvoiceController> logger
) : StoreOrderControllerBase(accessPolicy)
{
    /// <summary>
    /// 发送订单发票邮件。
    /// </summary>
    [HttpPost("invoice/email")]
    public async Task<IActionResult> SendInvoiceEmail(
        [FromBody] SendStoreOrderInvoiceEmailDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderEditAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var job = await invoiceEmailJobService.StartJobAsync(
                request,
                HttpContext.RequestAborted
            );
            return Ok(new { success = true, message = job.Message, data = job });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendInvoiceEmail failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 翻译订单发票邮件弹窗中的主题和正文。
    /// </summary>
    [HttpPost("invoice/email/translate-text")]
    public async Task<IActionResult> TranslateInvoiceEmailText(
        [FromBody] StoreOrderInvoiceEmailTextTranslationRequestDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderEditAsync(request.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await invoiceEmailTextTranslationService.TranslateAsync(
                request,
                HttpContext.RequestAborted
            );
            if (result.Success)
            {
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TranslateInvoiceEmailText failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取订单发票邮件发送 job 状态。
    /// </summary>
    [HttpGet("invoice/email/jobs/{jobId}")]
    public async Task<IActionResult> GetInvoiceEmailJob(string jobId)
    {
        try
        {
            // 保持旧协议顺序：先解析 job 和 404，再按 job 内订单做编辑权限与范围检查。
            var job = await invoiceEmailJobService.GetJobAsync(
                jobId,
                HttpContext.RequestAborted
            );
            if (job == null)
            {
                return NotFound(new { success = false, message = "任务不存在" });
            }

            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderEditAsync(job.OrderGUID)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            return Ok(new { success = true, data = job });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetInvoiceEmailJob failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }
}
