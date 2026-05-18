using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 国内商品管理控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class DomesticProductsController : ControllerBase
    {
        private readonly IDomesticProductService _domesticProductService;
        private readonly ILogger<DomesticProductsController> _logger;

        public DomesticProductsController(
            IDomesticProductService domesticProductService,
            ILogger<DomesticProductsController> logger)
        {
            _domesticProductService = domesticProductService;
            _logger = logger;
        }

        /// <summary>
        /// 获取国内商品分页列表
        /// </summary>
        /// <param name="search">搜索关键词</param>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="productType">商品类型</param>
        /// <param name="isActive">是否启用</param>
        /// <param name="minPrice">最小价格</param>
        /// <param name="maxPrice">最大价格</param>
        /// <param name="page">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="supplierName">供应商名称</param>
        /// <param name="productName">商品名称</param>
        /// <param name="productNo">商品货号</param>
        /// <param name="sortBy">排序字段</param>
        /// <param name="sortDirection">排序方向</param>
        /// <returns>国内商品分页列表</returns>
        [HttpGet]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetDomesticProducts(
            [FromQuery] string? search = null,
            [FromQuery] string? supplierCode = null,
            [FromQuery] string? supplierName = null,
            [FromQuery] string? productName = null,
            [FromQuery] string? productNo = null,
            [FromQuery] int? productType = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] decimal? minPrice = null,
            [FromQuery] decimal? maxPrice = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = null,
            [FromQuery] string? sortDirection = null)
        {
            try
            {
                var query = new DomesticProductQueryDto
                {
                    Search = search,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    ProductName = productName,
                    ProductNo = productNo,
                    ProductType = productType,
                    IsActive = isActive,
                    MinPrice = minPrice,
                    MaxPrice = maxPrice,
                    Page = page,
                    PageSize = pageSize,
                    SortBy = sortBy,
                    SortDirection = sortDirection
                };

                var result = await _domesticProductService.GetDomesticProductsAsync(query);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取国内商品列表失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取国内商品分页列表（高级过滤）
        /// </summary>
        /// <param name="query">高级查询条件</param>
        /// <returns>国内商品分页列表</returns>
        [HttpPost("advanced-search")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetDomesticProductsAdvanced([FromBody] DomesticProductAdvancedQueryDto query)
        {
            try
            {
                var result = await _domesticProductService.GetDomesticProductsAdvancedAsync(query);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取国内商品列表失败（高级过滤）");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取字段信息（用于构建过滤界面）
        /// </summary>
        /// <returns>字段信息列表</returns>
        [HttpGet("field-info")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetFieldInfo()
        {
            try
            {
                var result = await _domesticProductService.GetFieldInfoAsync();

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取字段信息失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据编码获取国内商品详情
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>国内商品详情</returns>
        [HttpGet("{productCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetDomesticProductByCode(string productCode)
        {
            try
            {
                var result = await _domesticProductService.GetDomesticProductByCodeAsync(productCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取国内商品详情失败，ProductCode: {ProductCode}", productCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据供应商编码获取商品列表
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>商品列表</returns>
        [HttpGet("supplier/{supplierCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetProductsBySupplierCode(string supplierCode)
        {
            try
            {
                var result = await _domesticProductService.GetProductsBySupplierCodeAsync(supplierCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供应商商品列表失败，SupplierCode: {SupplierCode}", supplierCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取启用的商品列表（用于下拉选择）
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <param name="productType">商品类型（可选）</param>
        /// <returns>启用的商品列表</returns>
        [HttpGet("active")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetActiveProducts(
            [FromQuery] string? supplierCode = null,
            [FromQuery] int? productType = null)
        {
            try
            {
                var result = await _domesticProductService.GetActiveProductsAsync(supplierCode, productType);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取启用商品列表失败，SupplierCode: {SupplierCode}, ProductType: {ProductType}", supplierCode, productType);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 创建国内商品
        /// </summary>
        /// <param name="dto">创建国内商品DTO</param>
        /// <returns>创建的国内商品</returns>
        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CreateDomesticProduct([FromBody] CreateDomesticProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                var result = await _domesticProductService.CreateDomesticProductAsync(dto);

                if (result.Success)
                {
                    return CreatedAtAction(nameof(GetDomesticProductByCode),
                        new { productCode = result.Data?.ProductCode }, result);
                }

                if (result.ErrorCode == "SUPPLIER_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                if (result.ErrorCode == "HB_PRODUCT_NO_EXISTS" || result.ErrorCode == "BARCODE_EXISTS")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建国内商品失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 更新国内商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="dto">更新国内商品DTO</param>
        /// <returns>更新的国内商品</returns>
        [HttpPut("{productCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UpdateDomesticProduct(string productCode, [FromBody] UpdateDomesticProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                var result = await _domesticProductService.UpdateDomesticProductAsync(productCode, dto);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新国内商品失败，ProductCode: {ProductCode}", productCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 删除国内商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{productCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> DeleteDomesticProduct(string productCode)
        {
            try
            {
                var result = await _domesticProductService.DeleteDomesticProductAsync(productCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                if (result.ErrorCode == "PRODUCT_IN_USE")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除国内商品失败，ProductCode: {ProductCode}", productCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 切换国内商品状态
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="isActive">是否启用</param>
        /// <returns>更新的国内商品</returns>
        [HttpPatch("{productCode}/status/{isActive}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> ToggleProductStatus(string productCode, bool isActive)
        {
            try
            {
                var result = await _domesticProductService.ToggleProductStatusAsync(productCode, isActive);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换国内商品状态失败，ProductCode: {ProductCode}", productCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 检查HB货号是否存在
        /// </summary>
        /// <param name="hbProductNo">HB货号</param>
        /// <param name="excludeProductCode">排除的商品编码</param>
        /// <returns>是否存在</returns>
        [HttpGet("check-hb-product-no")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CheckHBProductNoExists(
            [FromQuery] string hbProductNo,
            [FromQuery] string? excludeProductCode = null)
        {
            try
            {
                var result = await _domesticProductService.CheckHBProductNoExistsAsync(hbProductNo, excludeProductCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查HB货号是否存在失败，HBProductNo: {HBProductNo}", hbProductNo);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 检查条形码是否存在
        /// </summary>
        /// <param name="barcode">条形码</param>
        /// <param name="excludeProductCode">排除的商品编码</param>
        /// <returns>是否存在</returns>
        [HttpGet("check-barcode")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CheckBarcodeExists(
            [FromQuery] string barcode,
            [FromQuery] string? excludeProductCode = null)
        {
            try
            {
                var result = await _domesticProductService.CheckBarcodeExistsAsync(barcode, excludeProductCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查条形码是否存在失败，Barcode: {Barcode}", barcode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 生成下一个商品货号
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="prefixCode">前缀代码</param>
        /// <returns>生成的商品货号</returns>
        [HttpGet("generate-product-no")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GenerateNextProductNo(
            [FromQuery] string supplierCode,
            [FromQuery] string? prefixCode = null)
        {
            try
            {
                var result = await _domesticProductService.GenerateNextProductNoAsync(supplierCode, prefixCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成商品货号失败，SupplierCode: {SupplierCode}, PrefixCode: {PrefixCode}",
                    supplierCode, prefixCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 生成商品条形码
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="productType">商品类型</param>
        /// <returns>生成的条形码</returns>
        [HttpGet("generate-barcode")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GenerateProductBarcode(
            [FromQuery] string supplierCode,
            [FromQuery] int productType)
        {
            try
            {
                var result = await _domesticProductService.GenerateProductBarcodeAsync(supplierCode, productType);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成商品条码失败，SupplierCode: {SupplierCode}, ProductType: {ProductType}",
                    supplierCode, productType);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量创建国内商品
        /// </summary>
        /// <param name="dto">批量创建DTO</param>
        /// <returns>创建结果</returns>
        [HttpPost("batch")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchCreateDomesticProducts([FromBody] BatchCreateDomesticProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
                }

                var result = await _domesticProductService.BatchCreateDomesticProductsAsync(dto);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SUPPLIER_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建国内商品失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量删除国内商品
        /// </summary>
        /// <param name="productCodes">商品编码列表</param>
        /// <returns>删除结果</returns>
        [HttpDelete("batch")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchDeleteDomesticProducts([FromBody] List<string> productCodes)
        {
            try
            {
                if (productCodes == null || !productCodes.Any())
                {
                    return BadRequest(ApiResponse<object>.Error("商品编码列表不能为空", "EMPTY_PRODUCT_CODES"));
                }

                var result = await _domesticProductService.BatchDeleteDomesticProductsAsync(productCodes);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SOME_PRODUCTS_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                if (result.ErrorCode == "PRODUCTS_IN_USE")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除国内商品失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量更新商品状态
        /// </summary>
        /// <param name="request">批量状态更新请求</param>
        /// <returns>更新结果</returns>
        [HttpPut("batch-status")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchUpdateProductStatus([FromBody] BatchUpdateStatusRequest request)
        {
            try
            {
                if (request?.ProductCodes == null || !request.ProductCodes.Any())
                {
                    return BadRequest(ApiResponse<object>.Error("商品编码列表不能为空", "EMPTY_PRODUCT_CODES"));
                }

                var result = await _domesticProductService.BatchUpdateProductStatusAsync(request.ProductCodes, request.IsActive);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SOME_PRODUCTS_NOT_FOUND")
                {
                    return BadRequest(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品状态失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据商品类型获取统计信息
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <returns>统计信息</returns>
        [HttpGet("statistics/product-types")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetProductTypeStatistics([FromQuery] string? supplierCode = null)
        {
            try
            {
                var result = await _domesticProductService.GetProductTypeStatisticsAsync(supplierCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品类型统计失败，SupplierCode: {SupplierCode}", supplierCode);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取商品价格统计
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <param name="productType">商品类型（可选）</param>
        /// <returns>价格统计</returns>
        [HttpGet("statistics/prices")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetProductPriceStatistics(
            [FromQuery] string? supplierCode = null,
            [FromQuery] int? productType = null)
        {
            try
            {
                var result = await _domesticProductService.GetProductPriceStatisticsAsync(supplierCode, productType);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品价格统计失败，SupplierCode: {SupplierCode}, ProductType: {ProductType}",
                    supplierCode, productType);
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量检测商品信息 - 通过货号和供应商编码匹配现有数据
        /// </summary>
        /// <param name="dto">批量检测DTO</param>
        /// <returns>检测结果</returns>
        [HttpPost("batch-detect")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchDetectProducts([FromBody] BatchProductDetectionDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _domesticProductService.BatchDetectProductsAsync(dto);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量检测商品失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量创建和更新商品
        /// </summary>
        /// <param name="dto">批量操作DTO</param>
        /// <returns>操作结果</returns>
        [HttpPost("batch-create-update")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchCreateAndUpdateProducts([FromBody] BatchProductOperationDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _domesticProductService.BatchCreateAndUpdateProductsAsync(dto);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建和更新商品失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 修复重复的图片URL（管理员工具）
        /// 用于修复数据库中已存在的重复URL问题
        /// 例如：https://domain.com/path/https://domain.com/path/file.jpg 修复为 https://domain.com/path/file.jpg
        /// </summary>
        /// <param name="dryRun">是否仅模拟运行（不实际修改数据库）</param>
        /// <returns>修复结果统计</returns>
        [HttpPost("fix-duplicate-image-urls")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> FixDuplicateImageUrls([FromQuery] bool dryRun = true)
        {
            try
            {
                _logger.LogInformation("开始修复重复图片URL，dryRun={DryRun}", dryRun);

                var result = await _domesticProductService.FixDuplicateImageUrlsAsync(dryRun);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修复重复图片URL失败");
                return StatusCode(500, ApiResponse<object>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        // ==================== React AG Grid 专用接口 ====================

        /// <summary>
        /// 获取国内商品数据（AG Grid 服务端模式）
        /// </summary>
        /// <param name="request">AG Grid 请求</param>
        /// <returns>国内商品数据</returns>
        [HttpPost("grid")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetGridData([FromBody] GridRequestDto request)
        {
            try
            {
                _logger.LogInformation("AG Grid 请求: StartRow={StartRow}, EndRow={EndRow}, FilterModel={FilterModel}, SortModel={SortModel}",
                    request.StartRow, request.EndRow,
                    System.Text.Json.JsonSerializer.Serialize(request.FilterModel),
                    System.Text.Json.JsonSerializer.Serialize(request.SortModel));

                var result = await _domesticProductService.GetGridDataAsync(request);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AG Grid 获取数据失败");
                return StatusCode(500, new GridResponseDto<DomesticProductDto>
                {
                    Success = false,
                    Message = "服务器内部错误"
                });
            }
        }

        /// <summary>
        /// 批量删除国内商品（React 专用）
        /// </summary>
        /// <param name="request">批量删除请求</param>
        /// <returns>删除结果</returns>
        [HttpDelete("batch-delete")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequestDto request)
        {
            try
            {
                _logger.LogInformation("批量删除国内商品: {Count} 件", request.ProductCodes.Count);

                var result = await _domesticProductService.BatchDeleteAsync(request.ProductCodes);

                if (result.Success)
                {
                    return Ok(new { success = true, message = $"成功删除 {request.ProductCodes.Count} 件商品" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取套装商品信息列表
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>套装信息列表</returns>
        [HttpGet("{productCode}/set-items")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetSetItems(string productCode)
        {
            try
            {
                _logger.LogInformation("获取套装商品信息: ProductCode={ProductCode}", productCode);

                var result = await _domesticProductService.GetSetItemsAsync(productCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取套装商品信息失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新套装商品信息
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="request">更新请求</param>
        /// <returns>更新结果</returns>
        [HttpPut("{productCode}/set-items")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UpdateSetItems(string productCode, [FromBody] UpdateSetItemsRequestDto request)
        {
            try
            {
                _logger.LogInformation("更新套装商品信息: ProductCode={ProductCode}, Items={Count}", productCode, request.Items.Count);

                var result = await _domesticProductService.UpdateSetItemsAsync(productCode, request.Items);

                if (result.Success)
                {
                    return Ok(new { success = true, message = "保存成功" });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新套装商品信息失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }

}
