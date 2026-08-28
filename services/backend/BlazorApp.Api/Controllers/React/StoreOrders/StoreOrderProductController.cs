using System.Diagnostics;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductPicker;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React.StoreOrders;

[ApiController]
[Route(StoreOrderControllerBase.BaseRoute)]
[Authorize]
public sealed class StoreOrderProductController : StoreOrderControllerBase
{
    private const string ScanTraceHeaderName = "X-Scan-Trace-Id";
    private const string ScanOrderFlowCheckType = "scan-order-flow";
    private readonly IStoreOrderProductPickerSlice _productPickerSlice;
    private readonly ILogger<StoreOrderProductController> _logger;

    public StoreOrderProductController(
        IStoreOrderProductPickerSlice productPickerSlice,
        IStoreOrderAccessPolicy accessPolicy,
        ILogger<StoreOrderProductController> logger
    ) : base(accessPolicy)
    {
        _productPickerSlice = productPickerSlice;
        _logger = logger;
    }

    /// <summary>
    /// 获取商品列表 (支持货号搜索和分类筛选)
    /// </summary>
    [HttpPost("products")]
    public async Task<IActionResult> GetProducts([FromBody] StoreOrderFilterDto filter)
    {
        var totalSw = Stopwatch.StartNew();
        try
        {
            var permissionSw = Stopwatch.StartNew();
            var forbidden = ForbidIf(
                await AccessPolicy.RequireProductPickerReadAsync(
                    filter.StoreCode,
                    filter.ExcludeOrderGUID,
                    ScanOrderFlowCheckType
                )
            );
            permissionSw.Stop();
            if (forbidden != null)
            {
                _logger.LogInformation(
                    "[shop-home-perf] stage=products.controller.forbidden storeCode={StoreCode} pageNumber={PageNumber} pageSize={PageSize} permissionMs={PermissionMs} totalMs={TotalMs}",
                    filter.StoreCode,
                    filter.PageNumber,
                    filter.PageSize,
                    permissionSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );
                return forbidden;
            }

            var serviceSw = Stopwatch.StartNew();
            var result = await _productPickerSlice.GetPagedListAsync(filter);
            serviceSw.Stop();

            _logger.LogInformation(
                "[shop-home-perf] stage=products.controller.done storeCode={StoreCode} pageNumber={PageNumber} pageSize={PageSize} itemCount={ItemCount} total={Total} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                filter.StoreCode,
                filter.PageNumber,
                filter.PageSize,
                result.Items?.Count ?? 0,
                result.Total,
                permissionSw.ElapsedMilliseconds,
                serviceSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds
            );
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[shop-home-perf] stage=products.controller.error message=GetProducts failed storeCode={StoreCode} pageNumber={PageNumber} pageSize={PageSize} totalMs={TotalMs}",
                filter.StoreCode,
                filter.PageNumber,
                filter.PageSize,
                totalSw.ElapsedMilliseconds
            );
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    [HttpPost("products/batch-lookup")]
    public async Task<IActionResult> BatchLookupProducts(
        [FromBody] StoreOrderBatchLookupRequestDto request
    )
    {
        try
        {
            var forbidden = ForbidIf(await AccessPolicy.RequireOrderReadAsync());
            if (forbidden != null)
            {
                return forbidden;
            }

            var result = await _productPickerSlice.BatchLookupProductsAsync(request);
            if (result.Success)
            {
                return Ok(new { success = true, data = result.Data });
            }
            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchLookupProducts failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    [HttpPost("products/scan-lookup")]
    public async Task<IActionResult> ScanLookupProducts(
        [FromBody] StoreOrderScanLookupRequestDto request
    )
    {
        var totalSw = Stopwatch.StartNew();
        var traceId = GetScanTraceId();
        try
        {
            var permissionSw = Stopwatch.StartNew();
            var forbidden = ForbidIf(
                await AccessPolicy.RequireProductPickerReadAsync(
                    request.StoreCode,
                    excludedOrderGuid: null,
                    ScanOrderFlowCheckType
                )
            );
            permissionSw.Stop();
            if (forbidden != null)
            {
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.controller.forbidden storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} permissionMs={PermissionMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(request.Barcode),
                    GetBarcodeLength(request.Barcode),
                    permissionSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );
                return forbidden;
            }

            var serviceSw = Stopwatch.StartNew();
            var result = await _productPickerSlice.ScanLookupProductsAsync(request);
            serviceSw.Stop();
            _logger.LogInformation(
                "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.controller.done storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} success={Success} itemCount={ItemCount} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                traceId,
                request.StoreCode,
                GetBarcodeTail(request.Barcode),
                GetBarcodeLength(request.Barcode),
                result.Success,
                result.Data?.Items?.Count ?? 0,
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
                "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.controller.error storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} totalMs={TotalMs}",
                traceId,
                request.StoreCode,
                GetBarcodeTail(request.Barcode),
                GetBarcodeLength(request.Barcode),
                totalSw.ElapsedMilliseconds
            );
            _logger.LogError(ex, "ScanLookupProducts failed");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    private string GetScanTraceId()
    {
        // 前端扫码链路透传 traceId，缺失时使用 ASP.NET TraceIdentifier 兜底。
        return GetExplicitScanTraceId() ?? HttpContext.TraceIdentifier;
    }

    private string? GetExplicitScanTraceId()
    {
        var traceId = Request.Headers[ScanTraceHeaderName].FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(traceId) ? null : traceId;
    }

    private static string GetBarcodeTail(string? barcode)
    {
        var trimmed = barcode?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "empty";
        }

        return trimmed.Length <= 6 ? trimmed : trimmed[^6..];
    }

    private static int GetBarcodeLength(string? barcode)
    {
        return barcode?.Trim().Length ?? 0;
    }
}
