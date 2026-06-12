using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 套装商品管理控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Roles = "Admin,WarehouseManager")]
    public class DomesticSetProductsController : ControllerBase
    {
        private readonly IDomesticSetProductService _domesticSetProductService;
        private readonly ILogger<DomesticSetProductsController> _logger;

        public DomesticSetProductsController(
            IDomesticSetProductService domesticSetProductService,
            ILogger<DomesticSetProductsController> logger)
        {
            _domesticSetProductService = domesticSetProductService;
            _logger = logger;
        }

        /// <summary>
        /// 获取套装商品分页列表
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页结果</returns>
        [HttpGet]
        public async Task<IActionResult> GetDomesticSetProducts([FromQuery] DomesticSetProductQueryDto query)
        {
            try
            {
                var result = await _domesticSetProductService.GetDomesticSetProductsAsync(query);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取套装商品列表失败");
                return StatusCode(500, ApiResponse<PagedResult<DomesticSetProductDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据编码获取套装商品详情
        /// </summary>
        /// <param name="setProductCode">套装商品编码</param>
        /// <returns>套装商品详情</returns>
        [HttpGet("{setProductCode}")]
        public async Task<IActionResult> GetDomesticSetProductByCode(string setProductCode)
        {
            try
            {
                var result = await _domesticSetProductService.GetDomesticSetProductByCodeAsync(setProductCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SET_PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取套装商品详情失败，SetProductCode: {SetProductCode}", setProductCode);
                return StatusCode(500, ApiResponse<DomesticSetProductDetailDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据商品编码获取套装商品列表
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>套装商品列表</returns>
        [HttpGet("by-product/{productCode}")]
        public async Task<IActionResult> GetSetProductsByProductCode(string productCode)
        {
            try
            {
                var result = await _domesticSetProductService.GetSetProductsByProductCodeAsync(productCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品套装列表失败，ProductCode: {ProductCode}", productCode);
                return StatusCode(500, ApiResponse<List<DomesticSetProductDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 根据供应商编码获取套装商品列表
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>套装商品列表</returns>
        [HttpGet("by-supplier/{supplierCode}")]
        public async Task<IActionResult> GetSetProductsBySupplierCode(string supplierCode)
        {
            try
            {
                var result = await _domesticSetProductService.GetSetProductsBySupplierCodeAsync(supplierCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取供应商套装商品列表失败，SupplierCode: {SupplierCode}", supplierCode);
                return StatusCode(500, ApiResponse<List<DomesticSetProductDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 创建套装商品
        /// </summary>
        /// <param name="dto">创建套装商品DTO</param>
        /// <returns>创建的套装商品</returns>
        [HttpPost]
        public async Task<IActionResult> CreateDomesticSetProduct([FromBody] CreateDomesticSetProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<DomesticSetProductDto>.Error("请求数据验证失败", "VALIDATION_ERROR"));
                }

                var result = await _domesticSetProductService.CreateDomesticSetProductAsync(dto);

                if (result.Success)
                {
                    return CreatedAtAction(
                        nameof(GetDomesticSetProductByCode),
                        new { setProductCode = result.Data!.SetProductCode },
                        result);
                }

                if (result.ErrorCode == "PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                if (result.ErrorCode == "SET_PRODUCT_NO_EXISTS" || result.ErrorCode == "SET_BARCODE_EXISTS")
                {
                    return Conflict(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建套装商品失败");
                return StatusCode(500, ApiResponse<DomesticSetProductDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 更新套装商品
        /// </summary>
        /// <param name="setProductCode">套装商品编码</param>
        /// <param name="dto">更新套装商品DTO</param>
        /// <returns>更新的套装商品</returns>
        [HttpPut("{setProductCode}")]
        public async Task<IActionResult> UpdateDomesticSetProduct(string setProductCode, [FromBody] UpdateDomesticSetProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<DomesticSetProductDto>.Error("请求数据验证失败", "VALIDATION_ERROR"));
                }

                var result = await _domesticSetProductService.UpdateDomesticSetProductAsync(setProductCode, dto);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SET_PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新套装商品失败，SetProductCode: {SetProductCode}", setProductCode);
                return StatusCode(500, ApiResponse<DomesticSetProductDto>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 删除套装商品
        /// </summary>
        /// <param name="setProductCode">套装商品编码</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{setProductCode}")]
        public async Task<IActionResult> DeleteDomesticSetProduct(string setProductCode)
        {
            try
            {
                var result = await _domesticSetProductService.DeleteDomesticSetProductAsync(setProductCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SET_PRODUCT_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除套装商品失败，SetProductCode: {SetProductCode}", setProductCode);
                return StatusCode(500, ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 生成下一个套装货号
        /// </summary>
        /// <param name="baseItemNumber">基础商品货号</param>
        /// <returns>生成的套装货号</returns>
        [HttpGet("generate-next-set-product-no")]
        public async Task<IActionResult> GenerateNextSetProductNo([FromQuery] string baseItemNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseItemNumber))
                {
                    return BadRequest(ApiResponse<string>.Error("基础商品货号不能为空", "INVALID_BASE_ITEM_NUMBER"));
                }

                var result = await _domesticSetProductService.GenerateNextSetProductNoAsync(baseItemNumber);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成套装货号失败，BaseItemNumber: {BaseItemNumber}", baseItemNumber);
                return StatusCode(500, ApiResponse<string>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 生成套装商品条形码
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>生成的条形码</returns>
        [HttpGet("generate-set-product-barcode")]
        public async Task<IActionResult> GenerateSetProductBarcode([FromQuery] string supplierCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(supplierCode))
                {
                    return BadRequest(ApiResponse<string>.Error("供应商编码不能为空", "INVALID_SUPPLIER_CODE"));
                }

                var result = await _domesticSetProductService.GenerateSetProductBarcodeAsync(supplierCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成套装条码失败，SupplierCode: {SupplierCode}", supplierCode);
                return StatusCode(500, ApiResponse<string>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 检查套装货号是否存在
        /// </summary>
        /// <param name="setProductNo">套装货号</param>
        /// <param name="excludeSetProductCode">排除的套装商品编码</param>
        /// <returns>是否存在</returns>
        [HttpGet("check-set-product-no-exists")]
        public async Task<IActionResult> CheckSetProductNoExists(
            [FromQuery] string setProductNo,
            [FromQuery] string? excludeSetProductCode = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(setProductNo))
                {
                    return BadRequest(ApiResponse<bool>.Error("套装货号不能为空", "INVALID_SET_PRODUCT_NO"));
                }

                var result = await _domesticSetProductService.CheckSetProductNoExistsAsync(setProductNo, excludeSetProductCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查套装货号是否存在失败，SetProductNo: {SetProductNo}", setProductNo);
                return StatusCode(500, ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 检查套装条形码是否存在
        /// </summary>
        /// <param name="setBarcode">套装条形码</param>
        /// <param name="excludeSetProductCode">排除的套装商品编码</param>
        /// <returns>是否存在</returns>
        [HttpGet("check-set-barcode-exists")]
        public async Task<IActionResult> CheckSetBarcodeExists(
            [FromQuery] string setBarcode,
            [FromQuery] string? excludeSetProductCode = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(setBarcode))
                {
                    return BadRequest(ApiResponse<bool>.Error("套装条形码不能为空", "INVALID_SET_BARCODE"));
                }

                var result = await _domesticSetProductService.CheckSetBarcodeExistsAsync(setBarcode, excludeSetProductCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查套装条形码是否存在失败，SetBarcode: {SetBarcode}", setBarcode);
                return StatusCode(500, ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量创建套装商品
        /// </summary>
        /// <param name="dto">批量创建DTO</param>
        /// <returns>创建结果</returns>
        [HttpPost("batch")]
        public async Task<IActionResult> BatchCreateDomesticSetProducts([FromBody] BatchCreateDomesticSetProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<List<DomesticSetProductDto>>.Error("请求数据验证失败", "VALIDATION_ERROR"));
                }

                var result = await _domesticSetProductService.BatchCreateDomesticSetProductsAsync(dto);

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
                _logger.LogError(ex, "批量创建套装商品失败");
                return StatusCode(500, ApiResponse<List<DomesticSetProductDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 批量删除套装商品
        /// </summary>
        /// <param name="request">删除请求</param>
        /// <returns>删除结果</returns>
        [HttpDelete("batch")]
        public async Task<IActionResult> BatchDeleteDomesticSetProducts([FromBody] BatchDeleteSetProductsRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<bool>.Error("请求数据验证失败", "VALIDATION_ERROR"));
                }

                var result = await _domesticSetProductService.BatchDeleteDomesticSetProductsAsync(request.SetProductCodes);

                if (result.Success)
                {
                    return Ok(result);
                }

                if (result.ErrorCode == "SOME_SET_PRODUCTS_NOT_FOUND")
                {
                    return NotFound(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除套装商品失败");
                return StatusCode(500, ApiResponse<bool>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 复制套装商品结构
        /// </summary>
        /// <param name="request">复制请求</param>
        /// <returns>复制结果</returns>
        [HttpPost("copy-structure")]
        public async Task<IActionResult> CopySetProductStructure([FromBody] CopySetProductStructureRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponse<List<DomesticSetProductDto>>.Error("请求数据验证失败", "VALIDATION_ERROR"));
                }

                var result = await _domesticSetProductService.CopySetProductStructureAsync(request.SourceProductCode, request.TargetProductCode);

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
                _logger.LogError(ex, "复制套装商品结构失败");
                return StatusCode(500, ApiResponse<List<DomesticSetProductDto>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }

        /// <summary>
        /// 获取套装商品价格统计
        /// </summary>
        /// <param name="productCode">商品编码（可选）</param>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <returns>价格统计</returns>
        [HttpGet("price-statistics")]
        public async Task<IActionResult> GetSetProductPriceStatistics(
            [FromQuery] string? productCode = null,
            [FromQuery] string? supplierCode = null)
        {
            try
            {
                var result = await _domesticSetProductService.GetSetProductPriceStatisticsAsync(productCode, supplierCode);

                if (result.Success)
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取套装商品价格统计失败");
                return StatusCode(500, ApiResponse<Dictionary<string, decimal?>>.Error("服务器内部错误", "INTERNAL_SERVER_ERROR"));
            }
        }
    }

    /// <summary>
    /// 批量删除套装商品请求
    /// </summary>
    public class BatchDeleteSetProductsRequest
    {
        /// <summary>
        /// 套装商品编码列表
        /// </summary>
        [Required(ErrorMessage = "套装商品编码列表不能为空")]
        [MinLength(1, ErrorMessage = "至少需要一个套装商品编码")]
        public List<string> SetProductCodes { get; set; } = new();
    }

    /// <summary>
    /// 复制套装商品结构请求
    /// </summary>
    public class CopySetProductStructureRequest
    {
        /// <summary>
        /// 源商品编码
        /// </summary>
        [Required(ErrorMessage = "源商品编码不能为空")]
        public string SourceProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 目标商品编码
        /// </summary>
        [Required(ErrorMessage = "目标商品编码不能为空")]
        public string TargetProductCode { get; set; } = string.Empty;
    }
}
