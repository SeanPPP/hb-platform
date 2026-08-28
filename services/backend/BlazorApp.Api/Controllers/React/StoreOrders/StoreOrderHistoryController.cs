using System.Diagnostics;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductHistory;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React.StoreOrders;

[ApiController]
[Route(StoreOrderControllerBase.BaseRoute)]
[Authorize]
public sealed class StoreOrderHistoryController : StoreOrderControllerBase
{
    private const string ScanOrderFlowCheckType = "scan-order-flow";

    private readonly IStoreOrderProductHistorySlice _productHistorySlice;
    private readonly ILogger<StoreOrderHistoryController> _logger;

    public StoreOrderHistoryController(
        IStoreOrderProductHistorySlice productHistorySlice,
        IStoreOrderAccessPolicy accessPolicy,
        ILogger<StoreOrderHistoryController> logger
    ) : base(accessPolicy)
    {
        _productHistorySlice = productHistorySlice;
        _logger = logger;
    }

    /// <summary>
    /// 获取商品动态数据 (历史订单 + 购物车数量)
    /// </summary>
    [HttpPost("dynamic-data")]
    public async Task<IActionResult> GetDynamicData(
        [FromBody] StoreOrderDynamicDataRequestDto request
    )
    {
        var totalSw = Stopwatch.StartNew();
        try
        {
            var permissionSw = Stopwatch.StartNew();
            var forbidden = ForbidIf(
                await AccessPolicy.RequireCartReadAsync(
                    request.StoreCode,
                    ScanOrderFlowCheckType
                )
            );
            permissionSw.Stop();
            if (forbidden != null)
            {
                _logger.LogInformation(
                    "[shop-home-perf] stage=dynamic-data.controller.forbidden storeCode={StoreCode} requestCount={RequestCount} permissionMs={PermissionMs} totalMs={TotalMs}",
                    request.StoreCode,
                    request.ProductCodes?.Count ?? 0,
                    permissionSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );
                return forbidden;
            }

            var serviceSw = Stopwatch.StartNew();
            var result = await _productHistorySlice.GetProductsDynamicDataAsync(request);
            serviceSw.Stop();
            _logger.LogInformation(
                "[shop-home-perf] stage=dynamic-data.controller.done storeCode={StoreCode} requestCount={RequestCount} success={Success} resultCount={ResultCount} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                request.StoreCode,
                request.ProductCodes?.Count ?? 0,
                result.Success,
                result.Data?.Count ?? 0,
                permissionSw.ElapsedMilliseconds,
                serviceSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds
            );
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }
            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[shop-home-perf] stage=dynamic-data.controller.error message=GetDynamicData failed storeCode={StoreCode} requestCount={RequestCount} totalMs={TotalMs}",
                request.StoreCode,
                request.ProductCodes?.Count ?? 0,
                totalSw.ElapsedMilliseconds
            );
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 查询商品在指定分店的仓库订货发货记录（按订单聚合并分页）。
    /// </summary>
    [HttpPost("product-order-history")]
    public async Task<IActionResult> GetProductOrderHistory(
        [FromBody] StoreOrderProductOrderHistoryRequestDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireCartReadAsync(
                    request.StoreCode,
                    ScanOrderFlowCheckType
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await _productHistorySlice.GetProductOrderHistoryAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }
            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetProductOrderHistory failed storeCode={StoreCode} productCode={ProductCode}",
                request.StoreCode,
                request.ProductCode
            );
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 查询分店商品的统一订货/发货历史与销售明细合并时间轴。
    /// </summary>
    [HttpPost("product-activity-history")]
    public async Task<IActionResult> GetProductActivityHistory(
        [FromBody] StoreOrderProductActivityHistoryRequestDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireCartReadAsync(
                    request.StoreCode,
                    ScanOrderFlowCheckType
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await _productHistorySlice.GetProductActivityHistoryAsync(
                request
            );
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }
            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetProductActivityHistory failed storeCode={StoreCode} productCode={ProductCode} recordType={RecordType}",
                request.StoreCode,
                request.ProductCode,
                request.RecordType
            );
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 查询商品最近一次来货后的每日销售明细。
    /// </summary>
    [HttpPost("sales-since-last-arrival")]
    public async Task<IActionResult> GetSalesSinceLastArrival(
        [FromBody] StoreOrderSalesSinceLastArrivalRequestDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(
                await AccessPolicy.RequireCartReadAsync(
                    request.StoreCode,
                    ScanOrderFlowCheckType
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await _productHistorySlice.GetSalesSinceLastArrivalAsync(
                request
            );
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }
            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSalesSinceLastArrival failed storeCode={StoreCode} productCode={ProductCode}",
                request.StoreCode,
                request.ProductCode
            );
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 批量查询商品最近一次来货后的销售数量。
    /// </summary>
    [HttpPost("sales-since-last-arrival/summary")]
    public async Task<IActionResult> GetSalesSinceLastArrivalSummary(
        [FromBody] StoreOrderSalesSinceLastArrivalSummaryRequestDto request
    )
    {
        try
        {
            request.StoreCode = request.StoreCode?.Trim() ?? string.Empty;
            request.ProductCodes = (request.ProductCodes ?? new List<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var forbidden = ForbidIf(
                await AccessPolicy.RequireCartReadAsync(
                    request.StoreCode,
                    ScanOrderFlowCheckType
                )
            );
            if (forbidden != null)
            {
                return forbidden;
            }
            if (request.ProductCodes.Count > 500)
            {
                return BadRequest(new { success = false, message = "商品数量不能超过500" });
            }

            var result =
                await _productHistorySlice.GetSalesSinceLastArrivalSummaryAsync(
                    request
                );
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }
            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSalesSinceLastArrivalSummary failed storeCode={StoreCode} requestCount={RequestCount}",
                request.StoreCode,
                request.ProductCodes?.Count ?? 0
            );
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }
}
