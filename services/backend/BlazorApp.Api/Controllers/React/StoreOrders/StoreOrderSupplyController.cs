using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.SupplyNotices;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React.StoreOrders;

/// <summary>
/// 分店订货端的“暂停供货商品”状态查询与关注。
/// 权限沿用订货选品与购物车：能订货就能查看供货状态、关注恢复，不另设权限码。
/// </summary>
[ApiController]
// 用独立的路由前缀：订货基础路由上的控制器必须在旧兼容门面里逐一镜像（路由契约测试保护历史端点），
// 供货状态是新功能、没有任何调用方走旧门面，不应扩大那份兼容面。最终 URL 仍在 store-order 之下。
[Route(StoreOrderControllerBase.BaseRoute + "/supply")]
[Authorize]
public sealed class StoreOrderSupplyController(
    IStoreProductSupplyService supplyService,
    IStoreOrderAccessPolicy accessPolicy,
    ILogger<StoreOrderSupplyController> logger
) : StoreOrderControllerBase(accessPolicy)
{
    private const string SupplyCheckType = "supply-status";

    /// <summary>搜索或扫码零结果时调用：按条码 / 货号 / 商品编码精确查询暂停供货的商品。</summary>
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup([FromBody] StoreProductSupplyLookupRequestDto request)
    {
        try
        {
            var code = request?.Code?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                return BadRequest(new { success = false, message = "Code is required." });
            }

            var forbidden = ForbidIf(
                await AccessPolicy.RequireProductPickerReadAsync(
                    request!.StoreCode,
                    excludedOrderGuid: null,
                    SupplyCheckType
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var data = await supplyService.LookupAsync(request.StoreCode, code);
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Supply lookup failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    [HttpGet("watches/{storeCode}")]
    public async Task<IActionResult> GetWatches(string storeCode)
    {
        try
        {
            var forbidden = ForbidIf(await AccessPolicy.RequireCartReadAsync(storeCode, SupplyCheckType));
            if (forbidden != null)
            {
                return forbidden;
            }

            var data = await supplyService.GetWatchesAsync(storeCode.Trim());
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Get supply watches failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>提示条与入口角标用的汇总：仍在等待数、已恢复待确认数。</summary>
    [HttpGet("watches/{storeCode}/summary")]
    public async Task<IActionResult> GetWatchSummary(string storeCode)
    {
        try
        {
            var forbidden = ForbidIf(await AccessPolicy.RequireCartReadAsync(storeCode, SupplyCheckType));
            if (forbidden != null)
            {
                return forbidden;
            }

            var data = await supplyService.GetWatchSummaryAsync(storeCode.Trim());
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Get supply watch summary failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    [HttpPost("watches")]
    public Task<IActionResult> Watch([FromBody] StoreProductSupplyWatchRequestDto request) =>
        MutateWatchAsync(request, watch: true);

    [HttpPost("watches/remove")]
    public Task<IActionResult> Unwatch([FromBody] StoreProductSupplyWatchRequestDto request) =>
        MutateWatchAsync(request, watch: false);

    /// <summary>确认“已恢复订货”提醒。只会关闭当前确实可订的关注。</summary>
    [HttpPost("watches/acknowledge")]
    public async Task<IActionResult> Acknowledge(
        [FromBody] StoreProductSupplyWatchAcknowledgeRequestDto request
    )
    {
        try
        {
            var storeCode = request?.StoreCode?.Trim();
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return BadRequest(new { success = false, message = "StoreCode is required." });
            }

            var forbidden = ForbidIf(await AccessPolicy.RequireCartWriteAsync(storeCode, SupplyCheckType));
            if (forbidden != null)
            {
                return forbidden;
            }

            var closed = await supplyService.AcknowledgeRestockedAsync(
                storeCode,
                request!.ProductCodes,
                ResolveActorName()
            );
            return Ok(new { success = true, data = closed });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Acknowledge supply watches failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    private async Task<IActionResult> MutateWatchAsync(
        StoreProductSupplyWatchRequestDto? request,
        bool watch
    )
    {
        try
        {
            var storeCode = request?.StoreCode?.Trim();
            var productCode = request?.ProductCode?.Trim();
            if (string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(productCode))
            {
                return BadRequest(
                    new { success = false, message = "StoreCode and ProductCode are required." }
                );
            }

            // 关注属于本店订货数据的写入，沿用购物车写权限与分店范围校验。
            var forbidden = ForbidIf(await AccessPolicy.RequireCartWriteAsync(storeCode, SupplyCheckType));
            if (forbidden != null)
            {
                return forbidden;
            }

            var (success, message) = watch
                ? await supplyService.WatchAsync(storeCode, productCode, ResolveActorName())
                : await supplyService.UnwatchAsync(storeCode, productCode, ResolveActorName());
            return success
                ? Ok(new { success = true, message })
                : BadRequest(new { success = false, message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mutate supply watch failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    private string ResolveActorName() => User.Identity?.Name ?? "System";
}
