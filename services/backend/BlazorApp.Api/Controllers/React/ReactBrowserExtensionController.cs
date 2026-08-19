using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Authorize]
[Route("api/react/v1/browser-extension")]
public sealed class ReactBrowserExtensionController : ControllerBase
{
    private readonly IBrowserExtensionService _service;
    private readonly IBrowserExtensionAccessService _accessService;
    private readonly ILogger<ReactBrowserExtensionController> _logger;

    public ReactBrowserExtensionController(
        IBrowserExtensionService service,
        IBrowserExtensionAccessService accessService,
        ILogger<ReactBrowserExtensionController> logger
    )
    {
        _service = service;
        _accessService = accessService;
        _logger = logger;
    }

    [HttpGet("release")]
    public async Task<IActionResult> GetRelease()
    {
        if (!await _accessService.CanAccessAsync(User))
        {
            return Forbid();
        }

        return Ok(ApiResponse<BrowserExtensionReleaseDto>.OK(_service.GetRelease(), "查询成功"));
    }

    [HttpGet("supplier-profiles")]
    public async Task<IActionResult> GetSupplierProfiles()
    {
        if (!await _accessService.CanAccessAsync(User))
        {
            return Forbid();
        }

        return Ok(
            ApiResponse<BrowserExtensionSupplierProfilesDto>.OK(
                _service.GetSupplierProfiles(),
                "查询成功"
            )
        );
    }

    [HttpPost("product-purchase-cycle-summary/batch")]
    public async Task<IActionResult> GetProductSummaries(
        [FromBody] BrowserExtensionProductSummaryBatchRequestDto request
    )
    {
        if (request == null)
        {
            return BadRequest(
                ApiResponse<BrowserExtensionProductSummaryBatchDto>.Error(
                    "请求参数不能为空。",
                    "INVALID_REQUEST"
                )
            );
        }

        if (!await _accessService.CanAccessAsync(User, request.StoreCode))
        {
            return Forbid();
        }

        try
        {
            var data = await _service.GetProductSummariesAsync(request);
            return Ok(ApiResponse<BrowserExtensionProductSummaryBatchDto>.OK(data, "查询成功"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(
                ApiResponse<BrowserExtensionProductSummaryBatchDto>.Error(
                    ex.Message,
                    "INVALID_REQUEST"
                )
            );
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(
                ApiResponse<BrowserExtensionProductSummaryBatchDto>.Error(
                    ex.Message,
                    "NOT_FOUND"
                )
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "浏览器订货助手批量摘要查询失败 StoreCode={StoreCode} SupplierCode={SupplierCode}",
                request.StoreCode,
                request.SupplierCode
            );
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<BrowserExtensionProductSummaryBatchDto>.Error(
                    "商品采购摘要查询失败。",
                    "QUERY_ERROR"
                )
            );
        }
    }

    [HttpPost("product-purchase-cycles")]
    public async Task<IActionResult> GetPurchaseCycles(
        [FromBody] BrowserExtensionPurchaseCyclesRequestDto request
    )
    {
        if (request == null)
        {
            return BadRequest(
                ApiResponse<BrowserExtensionPurchaseCyclesDto>.Error(
                    "请求参数不能为空。",
                    "INVALID_REQUEST"
                )
            );
        }

        if (!await _accessService.CanAccessAsync(User, request.StoreCode))
        {
            return Forbid();
        }

        try
        {
            var data = await _service.GetPurchaseCyclesAsync(request);
            return Ok(ApiResponse<BrowserExtensionPurchaseCyclesDto>.OK(data, "查询成功"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(
                ApiResponse<BrowserExtensionPurchaseCyclesDto>.Error(
                    ex.Message,
                    "INVALID_REQUEST"
                )
            );
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(
                ApiResponse<BrowserExtensionPurchaseCyclesDto>.Error(ex.Message, "NOT_FOUND")
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "浏览器订货助手采购周期查询失败 StoreCode={StoreCode} SupplierCode={SupplierCode}",
                request.StoreCode,
                request.SupplierCode
            );
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<BrowserExtensionPurchaseCyclesDto>.Error(
                    "商品采购周期查询失败。",
                    "QUERY_ERROR"
                )
            );
        }
    }
}
