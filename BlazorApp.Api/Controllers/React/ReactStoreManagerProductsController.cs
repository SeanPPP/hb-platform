using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-manager/products")]
    [Authorize(Roles = "StoreManager")]
    [Obsolete("Candidate for removal after confirming no StoreManager frontend or external usage.")]
    public class ReactStoreManagerProductsController : ControllerBase
    {
        private readonly IStoreManagerProductReactService _service;
        private readonly ILogger<ReactStoreManagerProductsController> _logger;

        public ReactStoreManagerProductsController(
            IStoreManagerProductReactService service,
            ILogger<ReactStoreManagerProductsController> logger
        )
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("authorized-stores")]
        public async Task<IActionResult> GetAuthorizedStores()
        {
            try
            {
                var userGuid = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return Unauthorized(new { success = false, message = "未找到用户信息" });
                }

                var result = await _service.GetAuthorizedStoresAsync(userGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取有权限的分店列表失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProductPagedList(
            [FromQuery] List<string>? storeCodes,
            [FromQuery] string? search,
            [FromQuery] string? supplierName,
            [FromQuery] bool? isAutoPricing,
            [FromQuery] decimal? minPurchasePrice,
            [FromQuery] decimal? maxPurchasePrice,
            [FromQuery] decimal? minRetailPrice,
            [FromQuery] decimal? maxRetailPrice,
            [FromQuery] decimal? minDiscountRate,
            [FromQuery] decimal? maxDiscountRate,
            [FromQuery] string? sortBy,
            [FromQuery] string sortOrder = "asc",
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var userGuid = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return Unauthorized(new { success = false, message = "未找到用户信息" });
                }

                if (storeCodes == null || !storeCodes.Any())
                {
                    return BadRequest(new { success = false, message = "分店列表不能为空" });
                }

                var filter = new StoreManagerProductFilterDto
                {
                    StoreCodes = storeCodes,
                    Search = search,
                    SupplierName = supplierName,
                    IsAutoPricing = isAutoPricing,
                    MinPurchasePrice = minPurchasePrice,
                    MaxPurchasePrice = maxPurchasePrice,
                    MinRetailPrice = minRetailPrice,
                    MaxRetailPrice = maxRetailPrice,
                    MinDiscountRate = minDiscountRate,
                    MaxDiscountRate = maxDiscountRate,
                    SortBy = sortBy,
                    SortOrder = sortOrder,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                var result = await _service.GetProductPagedListAsync(filter);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品列表失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("{productCode}")]
        public async Task<IActionResult> GetProductDetail(string productCode, [FromQuery] List<string> storeCodes)
        {
            try
            {
                var userGuid = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return Unauthorized(new { success = false, message = "未找到用户信息" });
                }

                if (storeCodes == null || !storeCodes.Any())
                {
                    return BadRequest(new { success = false, message = "分店列表不能为空" });
                }

                var result = await _service.GetProductDetailAsync(productCode, storeCodes);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPut("store-prices/{uuid}")]
        public async Task<IActionResult> UpdateStorePrice(string uuid, [FromBody] StoreManagerUpdatePriceDto dto)
        {
            try
            {
                var userGuid = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return Unauthorized(new { success = false, message = "未找到用户信息" });
                }

                if (string.IsNullOrWhiteSpace(uuid))
                {
                    return BadRequest(new { success = false, message = "UUID不能为空" });
                }

                var result = await _service.UpdateStorePriceAsync(uuid, dto, userGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店价格失败: {UUID}", uuid);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("store-prices/batch")]
        public async Task<IActionResult> BatchUpdateStorePrices([FromBody] List<StoreManagerUpdatePriceDto> items)
        {
            try
            {
                var userGuid = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return Unauthorized(new { success = false, message = "未找到用户信息" });
                }

                if (items == null || !items.Any())
                {
                    return BadRequest(new { success = false, message = "更新列表不能为空" });
                }

                var result = await _service.BatchUpdateStorePricesAsync(items, userGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新分店价格失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPut("multi-code-prices/{uuid}")]
        public async Task<IActionResult> UpdateMultiCodePrice(string uuid, [FromBody] StoreManagerUpdateMultiCodePriceDto dto)
        {
            try
            {
                var userGuid = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return Unauthorized(new { success = false, message = "未找到用户信息" });
                }

                if (string.IsNullOrWhiteSpace(uuid))
                {
                    return BadRequest(new { success = false, message = "UUID不能为空" });
                }

                var result = await _service.UpdateMultiCodePriceAsync(uuid, dto, userGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新多码价格失败: {UUID}", uuid);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("multi-code-prices/batch")]
        public async Task<IActionResult> BatchUpdateMultiCodePrices([FromBody] List<StoreManagerUpdateMultiCodePriceDto> items)
        {
            try
            {
                var userGuid = User.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userGuid))
                {
                    return Unauthorized(new { success = false, message = "未找到用户信息" });
                }

                if (items == null || !items.Any())
                {
                    return BadRequest(new { success = false, message = "更新列表不能为空" });
                }

                var result = await _service.BatchUpdateMultiCodePricesAsync(items, userGuid);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新多码价格失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
