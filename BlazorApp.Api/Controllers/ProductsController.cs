using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Services;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(IProductService productService, ILogger<ProductsController> logger)
        {
            _productService = productService;
            _logger = logger;
        }

        /// <summary>
        /// 获取商品列表（分页）
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProducts([FromQuery] ProductFilterDto filter)
        {
            try
            {
                var result = await _productService.GetAllAsync(filter);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品列表失败");
                return StatusCode(500, new { success = false, message = "获取商品列表失败" });
            }
        }

        /// <summary>
        /// 根据ID获取商品详情
        /// </summary>
        [HttpGet("{productGuid}")]
        public async Task<IActionResult> GetProduct(string productGuid)
        {
            try
            {
                var product = await _productService.GetByIdAsync(productGuid);
                return Ok(new { success = true, data = product });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: {ProductGuid}", productGuid);
                return StatusCode(500, new { success = false, message = "获取商品详情失败" });
            }
        }

        /// <summary>
        /// 根据分类获取商品
        /// </summary>
        [HttpGet("category/{categoryGuid}")]
        public async Task<IActionResult> GetProductsByCategory(string categoryGuid)
        {
            try
            {
                var products = await _productService.GetByCategoryAsync(categoryGuid);
                return Ok(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据分类获取商品失败: {CategoryGuid}", categoryGuid);
                return StatusCode(500, new { success = false, message = "根据分类获取商品失败" });
            }
        }

        /// <summary>
        /// 根据仓库分类获取商品
        /// </summary>
        [HttpGet("warehouse-category/{warehouseCategoryGuid}")]
        public async Task<IActionResult> GetProductsByWarehouseCategory(string warehouseCategoryGuid)
        {
            try
            {
                var products = await _productService.GetByWarehouseCategoryAsync(warehouseCategoryGuid);
                return Ok(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据仓库分类获取商品失败: {WarehouseCategoryGuid}", warehouseCategoryGuid);
                return StatusCode(500, new { success = false, message = "根据仓库分类获取商品失败" });
            }
        }

        /// <summary>
        /// 根据条码搜索商品
        /// </summary>
        [HttpGet("search/barcode/{barcode}")]
        public async Task<IActionResult> SearchProductsByBarcode(string barcode)
        {
            try
            {
                var products = await _productService.SearchByBarcodeAsync(barcode);
                return Ok(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据条码搜索商品失败: {Barcode}", barcode);
                return StatusCode(500, new { success = false, message = "根据条码搜索商品失败" });
            }
        }

        /// <summary>
        /// 创建新商品
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateProduct([FromBody] CreateProductDto createDto)
        {
            try
            {
                var product = await _productService.CreateAsync(createDto);
                return CreatedAtAction(nameof(GetProduct), new { productGuid = product.ProductCode },
                    new { success = true, data = product });
            }
            catch (ValidationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                return StatusCode(500, new { success = false, message = "创建商品失败" });
            }
        }

        /// <summary>
        /// 更新商品
        /// </summary>
        [HttpPut("{productGuid}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateProduct(string productGuid, [FromBody] UpdateProductDto updateDto)
        {
            try
            {
                updateDto.ProductCode = productGuid; // 确保路径参数与DTO一致
                var product = await _productService.UpdateAsync(updateDto);
                return Ok(new { success = true, data = product });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (ValidationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品失败: {ProductGuid}", productGuid);
                return StatusCode(500, new { success = false, message = "更新商品失败" });
            }
        }

        /// <summary>
        /// 删除商品
        /// </summary>
        [HttpDelete("{productGuid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProduct(string productGuid)
        {
            try
            {
                var result = await _productService.DeleteAsync(productGuid);
                if (result)
                {
                    return Ok(new { success = true, message = "商品删除成功" });
                }
                return BadRequest(new { success = false, message = "商品删除失败" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品失败: {ProductGuid}", productGuid);
                return StatusCode(500, new { success = false, message = "删除商品失败" });
            }
        }

        /// <summary>
        /// 切换商品状态
        /// </summary>
        [HttpPatch("{productGuid}/toggle-status")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ToggleProductStatus(string productGuid)
        {
            try
            {
                var result = await _productService.ToggleActiveStatusAsync(productGuid);
                if (result)
                {
                    return Ok(new { success = true, message = "商品状态切换成功" });
                }
                return BadRequest(new { success = false, message = "商品状态切换失败" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换商品状态失败: {ProductGuid}", productGuid);
                return StatusCode(500, new { success = false, message = "切换商品状态失败" });
            }
        }

        /// <summary>
        /// 检查商品编码是否存在
        /// </summary>
        [HttpGet("exists/{productCode}")]
        public async Task<IActionResult> CheckProductExists(string productCode)
        {
            try
            {
                var exists = await _productService.ExistsAsync(productCode);
                return Ok(new { success = true, data = new { exists } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查商品编码是否存在失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "检查商品编码是否存在失败" });
            }
        }

        /// <summary>
        /// 根据商品编码列表批量获取商品详情
        /// </summary>
        [HttpPost("batch-by-codes")]
        public async Task<IActionResult> GetProductsByCodes([FromBody] List<string> productCodes)
        {
            try
            {
                if (productCodes == null || !productCodes.Any())
                {
                    return BadRequest(new { success = false, message = "商品编码列表不能为空" });
                }

                _logger.LogInformation("批量获取 {Count} 个商品的详情", productCodes.Count);

                var products = await _productService.GetByCodesAsync(productCodes);
                return Ok(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量获取商品详情失败");
                return StatusCode(500, new { success = false, message = "批量获取商品详情失败" });
            }
        }
    }
}
