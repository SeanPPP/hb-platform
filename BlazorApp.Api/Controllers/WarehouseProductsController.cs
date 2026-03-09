using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 仓库商品管理控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class WarehouseProductsController : ControllerBase
    {
        private readonly IWarehouseProductService _warehouseProductService;
        private readonly ILogger<WarehouseProductsController> _logger;

        public WarehouseProductsController(
            IWarehouseProductService warehouseProductService,
            ILogger<WarehouseProductsController> logger)
        {
            _warehouseProductService = warehouseProductService;
            _logger = logger;
        }

        /// <summary>
        /// 分页查询仓库商品
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页结果</returns>
        [HttpGet]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetPagedProducts([FromQuery] WarehouseProductQueryDto query)
        {
            try
            {
                _logger.LogInformation("开始分页查询仓库商品，查询条件：{@Query}", query);

                var result = await _warehouseProductService.GetPagedProductsAsync(query);

                _logger.LogInformation("分页查询仓库商品成功，返回 {Count} 条记录", result.Items.Count);

                return Ok(new ApiResponse<WarehouseProductPagedResultDto>
                {
                    Success = true,
                    Data = result,
                    Message = "查询成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分页查询仓库商品失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "查询失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 根据商品编码获取商品详情
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>商品详情</returns>
        [HttpGet("{productCode}")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetProductByCode(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品编码不能为空"
                    });
                }

                var product = await _warehouseProductService.GetProductByCodeAsync(productCode);

                if (product == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品不存在"
                    });
                }

                return Ok(new ApiResponse<WarehouseProductDto>
                {
                    Success = true,
                    Data = product,
                    Message = "获取成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败，商品编码：{ProductCode}", productCode);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 创建商品
        /// </summary>
        /// <param name="productDto">商品数据</param>
        /// <returns>创建的商品</returns>
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateProduct([FromBody] CreateWarehouseProductDto productDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "数据验证失败",
                        Data = ModelState
                    });
                }

                // 检查条码是否已存在
                if (!string.IsNullOrEmpty(productDto.Barcode))
                {
                    var barcodeExists = await _warehouseProductService.IsBarcodeExistsAsync(productDto.Barcode);
                    if (barcodeExists)
                    {
                        return Conflict(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "条码已存在"
                        });
                    }
                }

                var product = await _warehouseProductService.CreateProductAsync(productDto);

                return CreatedAtAction(
                    nameof(GetProductByCode),
                    new { productCode = product.ProductCode },
                    new ApiResponse<WarehouseProductDto>
                    {
                        Success = true,
                        Data = product,
                        Message = "创建成功"
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "创建失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 更新商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="productDto">商品数据</param>
        /// <returns>更新的商品</returns>
        [HttpPut("{productCode}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateProduct(string productCode, [FromBody] UpdateWarehouseProductDto productDto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品编码不能为空"
                    });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "数据验证失败",
                        Data = ModelState
                    });
                }

                // 检查条码是否已存在（排除当前商品）
                if (!string.IsNullOrEmpty(productDto.Barcode))
                {
                    var barcodeExists = await _warehouseProductService.IsBarcodeExistsAsync(productDto.Barcode, productCode);
                    if (barcodeExists)
                    {
                        return Conflict(new ApiResponse<object>
                        {
                            Success = false,
                            Message = "条码已存在"
                        });
                    }
                }

                var product = await _warehouseProductService.UpdateProductAsync(productCode, productDto);

                if (product == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品不存在"
                    });
                }

                return Ok(new ApiResponse<WarehouseProductDto>
                {
                    Success = true,
                    Data = product,
                    Message = "更新成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品失败，商品编码：{ProductCode}", productCode);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "更新失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 删除商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{productCode}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteProduct(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品编码不能为空"
                    });
                }

                var success = await _warehouseProductService.DeleteProductAsync(productCode);

                if (!success)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品不存在或删除失败"
                    });
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "删除成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品失败，商品编码：{ProductCode}", productCode);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "删除失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 批量更新商品状态
        /// </summary>
        /// <param name="request">批量更新请求</param>
        /// <returns>更新结果</returns>
        [HttpPatch("batch-status")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> BatchUpdateStatus([FromBody] BatchUpdateStatusRequest request)
        {
            try
            {
                if (request?.ProductCodes == null || !request.ProductCodes.Any())
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品编码列表不能为空"
                    });
                }

                var updateCount = await _warehouseProductService.BatchUpdateProductStatusAsync(
                    request.ProductCodes, request.IsActive);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new { UpdateCount = updateCount },
                    Message = $"成功更新 {updateCount} 个商品状态"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品状态失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "批量更新失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 获取库存预警商品
        /// </summary>
        /// <param name="locationGuids">仓库位置GUID列表（可选）</param>
        /// <returns>预警商品列表</returns>
        [HttpGet("stock-alerts")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetStockAlerts([FromQuery] List<string>? locationGuids = null)
        {
            try
            {
                var products = await _warehouseProductService.GetStockAlertProductsAsync(locationGuids);

                return Ok(new ApiResponse<List<WarehouseProductListDto>>
                {
                    Success = true,
                    Data = products,
                    Message = $"获取到 {products.Count} 个库存预警商品"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取库存预警商品失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 更新商品库存
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="request">库存更新请求</param>
        /// <returns>更新结果</returns>
        [HttpPatch("{productCode}/stock")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateStock(string productCode, [FromBody] UpdateStockRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品编码不能为空"
                    });
                }

                if (request.StockQuantity < 0)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "库存数量不能为负数"
                    });
                }

                var success = await _warehouseProductService.UpdateProductStockAsync(
                    productCode, request.StockQuantity, request.StockValue);

                if (!success)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品不存在或更新失败"
                    });
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "库存更新成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品库存失败，商品编码：{ProductCode}", productCode);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "更新失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 根据条码搜索商品
        /// </summary>
        /// <param name="barcode">条码</param>
        /// <returns>商品列表</returns>
        [HttpGet("search/barcode/{barcode}")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> SearchByBarcode(string barcode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "条码不能为空"
                    });
                }

                var products = await _warehouseProductService.SearchProductsByBarcodeAsync(barcode);

                return Ok(new ApiResponse<List<WarehouseProductListDto>>
                {
                    Success = true,
                    Data = products,
                    Message = $"找到 {products.Count} 个商品"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据条码搜索商品失败，条码：{Barcode}", barcode);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "搜索失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 获取商品统计信息
        /// </summary>
        /// <param name="categoryGuid">分类GUID（可选）</param>
        /// <param name="locationGuids">仓库位置GUID列表（可选）</param>
        /// <returns>统计信息</returns>
        [HttpGet("stats")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetStats([FromQuery] string? categoryGuid = null, [FromQuery] List<string>? locationGuids = null)
        {
            try
            {
                var stats = await _warehouseProductService.GetProductStatsAsync(categoryGuid, locationGuids);

                return Ok(new ApiResponse<WarehouseProductStatsDto>
                {
                    Success = true,
                    Data = stats,
                    Message = "获取统计信息成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品统计信息失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 导出商品数据
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>导出数据</returns>
        [HttpPost("export")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ExportProducts([FromBody] WarehouseProductQueryDto query)
        {
            try
            {
                var products = await _warehouseProductService.ExportProductsAsync(query);

                return Ok(new ApiResponse<List<WarehouseProductListDto>>
                {
                    Success = true,
                    Data = products,
                    Message = $"导出 {products.Count} 条商品数据"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出商品数据失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "导出失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 检查商品编码是否存在
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>检查结果</returns>
        [HttpGet("check/product-code/{productCode}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CheckProductCode(string productCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "商品编码不能为空"
                    });
                }

                var exists = await _warehouseProductService.IsProductCodeExistsAsync(productCode);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new { Exists = exists },
                    Message = exists ? "商品编码已存在" : "商品编码可用"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查商品编码失败，商品编码：{ProductCode}", productCode);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "检查失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 检查条码是否存在
        /// </summary>
        /// <param name="barcode">条码</param>
        /// <param name="excludeProductCode">排除的商品编码</param>
        /// <returns>检查结果</returns>
        [HttpGet("check/barcode/{barcode}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CheckBarcode(string barcode, [FromQuery] string? excludeProductCode = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "条码不能为空"
                    });
                }

                var exists = await _warehouseProductService.IsBarcodeExistsAsync(barcode, excludeProductCode);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new { Exists = exists },
                    Message = exists ? "条码已存在" : "条码可用"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查条码失败，条码：{Barcode}", barcode);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "检查失败，请稍后重试"
                });
            }
        }
    }

    /// <summary>
    /// 批量更新状态请求
    /// </summary>
    public class BatchUpdateStatusRequest
    {
        /// <summary>
        /// 商品编码列表
        /// </summary>
        public List<string> ProductCodes { get; set; } = new List<string>();

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 更新库存请求
    /// </summary>
    public class UpdateStockRequest
    {
        /// <summary>
        /// 库存数量
        /// </summary>
        public int StockQuantity { get; set; }

        /// <summary>
        /// 库存金额
        /// </summary>
        public decimal? StockValue { get; set; }
    }
}
