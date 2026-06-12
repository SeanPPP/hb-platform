using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 商品前缀管理控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class ProductPrefixCodesController : ControllerBase
    {
        private readonly IProductPrefixCodeService _productPrefixCodeService;
        private readonly ILogger<ProductPrefixCodesController> _logger;

        public ProductPrefixCodesController(
            IProductPrefixCodeService productPrefixCodeService,
            ILogger<ProductPrefixCodesController> logger)
        {
            _productPrefixCodeService = productPrefixCodeService;
            _logger = logger;
        }

        /// <summary>
        /// 获取商品前缀分页列表
        /// </summary>
        /// <param name="search">搜索关键词</param>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="isActive">是否启用</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="sortField">排序字段</param>
        /// <param name="sortDirection">排序方向</param>
        /// <returns>商品前缀分页列表</returns>
        [HttpGet]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetProductPrefixCodes(
            [FromQuery] string? search = null,
            [FromQuery] string? supplierCode = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sortField = null,
            [FromQuery] string? sortDirection = null)
        {
            try
            {
                var query = new ProductPrefixCodeQueryDto
                {
                    Search = search,
                    SupplierCode = supplierCode,
                    IsActive = isActive,
                    Page = page,
                    PageSize = pageSize,
                    SortField = sortField,
                    SortDirection = sortDirection
                };

                var result = await _productPrefixCodeService.GetProductPrefixCodesAsync(query);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品前缀列表失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据编码获取商品前缀详情
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <returns>商品前缀详情</returns>
        [HttpGet("{prefixCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetProductPrefixCodeByCode(string prefixCode)
        {
            try
            {
                var result = await _productPrefixCodeService.GetProductPrefixCodeByCodeAsync(prefixCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PREFIX_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品前缀详情失败，PrefixCode: {PrefixCode}", prefixCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据供应商编码获取前缀列表
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>前缀列表</returns>
        [HttpGet("supplier/{supplierCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetPrefixesBySupplierCode(string supplierCode)
        {
            try
            {
                var result = await _productPrefixCodeService.GetPrefixesBySupplierCodeAsync(supplierCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供应商前缀列表失败，SupplierCode: {SupplierCode}", supplierCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取启用的前缀列表（用于下拉选择）
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <returns>启用的前缀列表</returns>
        [HttpGet("active")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetActivePrefixes([FromQuery] string? supplierCode = null)
        {
            try
            {
                var result = await _productPrefixCodeService.GetActivePrefixesAsync(supplierCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取启用前缀列表失败，SupplierCode: {SupplierCode}", supplierCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 创建商品前缀
        /// </summary>
        /// <param name="dto">创建商品前缀DTO</param>
        /// <returns>创建的商品前缀</returns>
        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CreateProductPrefixCode([FromBody] CreateProductPrefixCodeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                var result = await _productPrefixCodeService.CreateProductPrefixCodeAsync(dto);

                if (result.Success)
                {
                    return CreatedAtAction(nameof(GetProductPrefixCodeByCode),
                        new { prefixCode = result.Data?.PrefixCode }, result);
                }

                if (result.ErrorCode == "SUPPLIER_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                if (result.ErrorCode == "PREFIX_NAME_EXISTS")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品前缀失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 更新商品前缀
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <param name="dto">更新商品前缀DTO</param>
        /// <returns>更新的商品前缀</returns>
        [HttpPut("{prefixCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UpdateProductPrefixCode(string prefixCode, [FromBody] UpdateProductPrefixCodeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                var result = await _productPrefixCodeService.UpdateProductPrefixCodeAsync(prefixCode, dto);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PREFIX_NOT_FOUND")
                {
                    return NotFound(result);
                }

                if (result.ErrorCode == "PREFIX_NAME_EXISTS")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品前缀失败，PrefixCode: {PrefixCode}", prefixCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 删除商品前缀
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{prefixCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> DeleteProductPrefixCode(string prefixCode)
        {
            try
            {
                var result = await _productPrefixCodeService.DeleteProductPrefixCodeAsync(prefixCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PREFIX_NOT_FOUND")
                {
                    return NotFound(result);
                }

                if (result.ErrorCode == "PREFIX_IN_USE")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品前缀失败，PrefixCode: {PrefixCode}", prefixCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 切换商品前缀状态
        /// </summary>
        /// <param name="prefixCode">前缀编码</param>
        /// <param name="isActive">是否启用</param>
        /// <returns>更新的商品前缀</returns>
        [HttpPatch("{prefixCode}/status/{isActive}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> TogglePrefixStatus(string prefixCode, bool isActive)
        {
            try
            {
                var result = await _productPrefixCodeService.TogglePrefixStatusAsync(prefixCode, isActive);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PREFIX_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换商品前缀状态失败，PrefixCode: {PrefixCode}", prefixCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 检查前缀代码是否存在
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="prefixName">前缀代码</param>
        /// <param name="excludePrefixCode">排除的前缀编码</param>
        /// <returns>是否存在</returns>
        [HttpGet("check-exists")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CheckPrefixNameExists(
            [FromQuery] string supplierCode,
            [FromQuery] string prefixName,
            [FromQuery] string? excludePrefixCode = null)
        {
            try
            {
                var result = await _productPrefixCodeService.CheckPrefixNameExistsAsync(supplierCode, prefixName, excludePrefixCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查前缀代码是否存在失败，SupplierCode: {SupplierCode}, PrefixName: {PrefixName}",
                    supplierCode, prefixName);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量创建商品前缀
        /// </summary>
        /// <param name="dto">批量创建DTO</param>
        /// <returns>创建结果</returns>
        [HttpPost("batch")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchCreateProductPrefixCodes([FromBody] BatchCreateProductPrefixCodeDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                var result = await _productPrefixCodeService.BatchCreateProductPrefixCodesAsync(dto);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SUPPLIER_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                if (result.ErrorCode == "PREFIX_NAMES_EXISTS")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建商品前缀失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量删除商品前缀
        /// </summary>
        /// <param name="prefixCodes">前缀编码列表</param>
        /// <returns>删除结果</returns>
        [HttpDelete("batch")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchDeleteProductPrefixCodes([FromBody] List<string> prefixCodes)
        {
            try
            {
                if (prefixCodes == null || !prefixCodes.Any())
                {
                    return BadRequest(ApiResponse<object>.Error("前缀编码列表不能为空", "EMPTY_PREFIX_CODES"));
                }

                var result = await _productPrefixCodeService.BatchDeleteProductPrefixCodesAsync(prefixCodes);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SOME_PREFIXES_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                if (result.ErrorCode == "PREFIXES_IN_USE")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除商品前缀失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 更新前缀排序
        /// </summary>
        /// <param name="prefixCodes">前缀编码和排序顺序的字典</param>
        /// <returns>更新结果</returns>
        [HttpPut("sort-order")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UpdatePrefixSortOrder([FromBody] Dictionary<string, int> prefixCodes)
        {
            try
            {
                if (prefixCodes == null || !prefixCodes.Any())
                {
                    return BadRequest(ApiResponse<object>.Error("前缀编码列表不能为空", "EMPTY_PREFIX_CODES"));
                }

                var result = await _productPrefixCodeService.UpdatePrefixSortOrderAsync(prefixCodes);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SOME_PREFIXES_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新前缀排序失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }
    }
}
