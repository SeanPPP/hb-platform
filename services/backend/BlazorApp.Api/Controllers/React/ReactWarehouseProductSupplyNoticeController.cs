using BlazorApp.Api.Features.SupplyNotices;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

/// <summary>
/// 仓库端的商品供货说明：对已下架商品登记或修改“后续计划 / 预计恢复订货时间”。
/// 独立成控制器，避免给仓库商品主控制器的长构造函数再加依赖。
/// </summary>
[ApiController]
[Route("api/react/v1/product-warehouse/supply-notices")]
[Authorize]
public sealed class ReactWarehouseProductSupplyNoticeController(
    IWarehouseProductSupplyNoticeService service,
    ILogger<ReactWarehouseProductSupplyNoticeController> logger
) : ControllerBase
{
    /// <summary>按商品编码批量查询当前有效的供货说明（仓库商品列表的三列）。</summary>
    [HttpPost("query")]
    [Authorize(Roles = "Admin,WarehouseManager,WarehouseStaff")]
    public async Task<IActionResult> Query([FromBody] WarehouseProductSupplyNoticeQueryRequestDto request)
    {
        try
        {
            var data = await service.GetOpenNoticesAsync(request?.ProductCodes ?? new List<string>());
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "查询供货说明失败");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>对已下架商品批量登记或修改供货说明；与批量上下架同一权限。</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,WarehouseManager")]
    public async Task<IActionResult> Upsert(
        [FromBody] BatchUpsertWarehouseProductSupplyNoticeRequestDto request
    )
    {
        try
        {
            var result = await service.UpsertForPausedProductsAsync(
                request,
                User.Identity?.Name ?? "System"
            );
            return result.Success
                ? Ok(new { success = true, data = result, message = result.Message })
                : BadRequest(new { success = false, data = result, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "保存供货说明失败");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }
}
