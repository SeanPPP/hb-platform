using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Orders;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React.StoreOrders;

/// <summary>
/// 分店订货查询与基础资料维护接口。
/// </summary>
[ApiController]
[Route(StoreOrderControllerBase.BaseRoute)]
[Authorize]
public sealed class StoreOrderQueryController(
    IStoreOrderOrdersSlice ordersSlice,
    IStoreOrderAccessPolicy accessPolicy,
    ILogger<StoreOrderQueryController> logger
) : StoreOrderControllerBase(accessPolicy)
{
    /// <summary>
    /// 获取订单列表。
    /// </summary>
    [HttpPost("list")]
    public async Task<IActionResult> GetOrderList([FromBody] StoreOrderListFilterDto filter)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderListReadAsync(
                    filter.StoreCode,
                    filter.StoreCodes
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await ordersSlice.GetOrderListAsync(filter);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetOrderList failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取订单详情。
    /// </summary>
    [HttpGet("detail/{orderGuid}")]
    public async Task<IActionResult> GetOrderDetail(
        string orderGuid,
        [FromQuery] StoreOrderDetailQueryDto query
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderDetailReadAsync(orderGuid)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await ordersSlice.GetOrderDetailAsync(orderGuid, query);
            return Ok(
                new
                {
                    success = result.Success,
                    data = result.Data,
                    message = result.Message,
                }
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetOrderDetail failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取订单全量详情，供打印和发票页面使用。
    /// </summary>
    [HttpGet("detail/{orderGuid}/full")]
    public async Task<IActionResult> GetOrderDetailFull(string orderGuid)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderDetailReadAsync(orderGuid)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await ordersSlice.GetOrderDetailFullAsync(orderGuid);
            return Ok(
                new
                {
                    success = result.Success,
                    data = result.Data,
                    message = result.Message,
                }
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetOrderDetail failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 更新订单关联分店的联系信息。
    /// </summary>
    [HttpPost("store-contact/update")]
    public async Task<IActionResult> UpdateStoreContact(
        [FromBody] UpdateStoreOrderStoreContactDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderEditAsync(
                    request.OrderGUID,
                    request.StoreCode
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await ordersSlice.UpdateStoreContactAsync(request);
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
            logger.LogError(ex, "UpdateStoreContact failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取订单已包含商品编码，供分页明细页跨页去重使用。
    /// </summary>
    [HttpGet("detail/{orderGuid}/product-codes")]
    public async Task<IActionResult> GetOrderDetailProductCodes(string orderGuid)
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireOrderDetailProductCodesReadAsync(orderGuid)
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await ordersSlice.GetOrderDetailProductCodesAsync(orderGuid);
            return Ok(
                new
                {
                    success = result.Success,
                    data = result.Data,
                    message = result.Message,
                }
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetOrderDetailProductCodes failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取订单中使用过的分店信息。
    /// </summary>
    [HttpGet("used-branches")]
    public async Task<IActionResult> GetUsedBranches()
    {
        try
        {
            var forbidden = ForbidIf(await AccessPolicy.RequireOrderReadAsync());
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await ordersSlice.GetUsedBranchesAsync();
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetUsedBranches failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取订单中未能匹配本地分店的分店标识聚合。
    /// </summary>
    [HttpGet("unmatched-store-groups")]
    public async Task<IActionResult> GetUnmatchedStoreGroups()
    {
        try
        {
            var forbidden = ForbidIf(await AccessPolicy.RequireOrderReadAsync());
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await ordersSlice.GetUnmatchedStoreOrderGroupsAsync();
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetUnmatchedStoreGroups failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 批量将订单旧分店 GUID/标识修复为本地分店编码。
    /// </summary>
    [HttpPost("batch-map-store-code")]
    public async Task<IActionResult> BatchMapStoreCode(
        [FromBody] BatchMapStoreOrderStoreCodeDto request
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

            request ??= new BatchMapStoreOrderStoreCodeDto();
            var targetStoreCodes = request.Mappings
                .Select(item => item.TargetStoreCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var targetStoreCode in targetStoreCodes)
            {
                forbidden = ForbidIf(
                    await AccessPolicy.RequireStoreScopeAsync(targetStoreCode)
                );
                if (forbidden != null)
                {
                    return forbidden;
                }
            }

            var result = await ordersSlice.BatchMapStoreOrderStoreCodeAsync(request);
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
            logger.LogError(ex, "BatchMapStoreCode failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 获取当前用户可访问的分店代码列表。
    /// </summary>
    [HttpGet("accessible-branches")]
    public async Task<IActionResult> GetAccessibleBranches()
    {
        try
        {
            var forbidden = ForbidIf(await AccessPolicy.RequireOrderReadAsync());
            if (forbidden != null)
            {
                return forbidden;
            }

            var branchCodes = await AccessPolicy.GetAccessibleStoreCodesAsync();
            return Ok(new { success = true, data = branchCodes });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetAccessibleBranches failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }
}
