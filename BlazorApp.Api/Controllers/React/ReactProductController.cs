using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// React Product管理控制器
    /// 提供产品的CRUD操作、分页查询、排序和过滤功能
    /// </summary>
    [ApiController]
    [Route("api/react/v1/products")]
    [Authorize]
    public class ReactProductController : ControllerBase
    {
        private readonly IProductReactService _service;
        private readonly IProductStoreSyncService _productStoreSyncService;
        private readonly ILogger<ReactProductController> _logger;

        public ReactProductController(
            IProductReactService service,
            IProductStoreSyncService productStoreSyncService,
            ILogger<ReactProductController> logger
        )
        {
            _service = service;
            _productStoreSyncService = productStoreSyncService;
            _logger = logger;
        }

        /// <summary>
        /// 分页查询商品列表（支持排序和过滤）
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页列表</returns>
        [HttpPost("list")]
        public async Task<IActionResult> GetPagedList([FromBody] ProductReactFilterDto query)
        {
            try
            {
                var result = await _service.GetPagedListAsync(query);
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Items,
                        total = result.Total,
                        pageNumber = result.PageNumber,
                        pageSize = result.PageSize,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分页查询商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取商品详情
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>商品详情</returns>
        [HttpGet("{productCode}")]
        public async Task<IActionResult> GetById(string productCode)
        {
            try
            {
                var result = await _service.GetByIdAsync(productCode);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return NotFound(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 创建商品
        /// </summary>
        /// <param name="dto">创建DTO</param>
        /// <returns>创建结果</returns>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "请求参数验证失败" });
                }

                var result = await _service.CreateAsync(dto);
                if (result.Success)
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = result.Data,
                            message = "创建成功",
                        }
                    );
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="dto">更新DTO</param>
        /// <returns>更新结果</returns>
        [HttpPut("{productCode}")]
        public async Task<IActionResult> Update(string productCode, [FromBody] UpdateProductDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "请求参数验证失败" });
                }

                var result = await _service.UpdateAsync(productCode, dto);
                if (result.Success)
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = result.Data,
                            message = "更新成功",
                        }
                    );
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 删除商品（支持软删除和物理删除）
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="mode">删除模式：soft=软删除（默认），hard=物理删除</param>
        /// <returns>删除结果</returns>
        [HttpDelete("{productCode}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string productCode, [FromQuery] string mode = "soft")
        {
            try
            {
                var isSoftDelete = mode.ToLower() != "hard";
                var result = await _service.DeleteAsync(productCode, isSoftDelete);
                if (result.Success)
                {
                    return Ok(new { success = true, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品失败: {ProductCode}", productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量更新商品（使用事务）
        /// </summary>
        /// <param name="request">批量更新请求</param>
        /// <returns>批量操作结果</returns>
        [HttpPost("batch-update")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateRequest request)
        {
            try
            {
                if (request == null || request.Items == null || !request.Items.Any())
                {
                    return BadRequest(new { success = false, message = "请求数据不能为空" });
                }

                var result = await _service.BatchUpdateAsync(request.Items);
                if (result.Success)
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = result.Data,
                            successCount = result.Data?.SuccessCount,
                            failedCount = result.Data?.FailedCount,
                            errors = result.Data?.Errors,
                            message = result.Message,
                        }
                    );
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量删除商品（使用事务，支持软删除和物理删除）
        /// </summary>
        /// <param name="request">批量删除请求</param>
        /// <returns>批量操作结果</returns>
        [HttpPost("batch-delete")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequest request)
        {
            try
            {
                if (request == null || request.ProductCodes == null || !request.ProductCodes.Any())
                {
                    return BadRequest(new { success = false, message = "请求数据不能为空" });
                }

                var isSoftDelete = request.Mode?.ToLower() != "hard";
                var result = await _service.BatchDeleteAsync(request.ProductCodes, isSoftDelete);
                if (result.Success)
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = result.Data,
                            successCount = result.Data?.SuccessCount,
                            failedCount = result.Data?.FailedCount,
                            errors = result.Data?.Errors,
                            message = result.Message,
                        }
                    );
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 高级过滤查询商品列表（支持商品信息表与分店价格表的组合过滤）
        /// 所有过滤参数通过 QueryString 传递,支持同时生效
        /// 对货价、零售价使用 BETWEEN 语法,支持闭区间
        /// 分店价格表过滤需先按分店 ID 过滤,再与商品主表 INNER JOIN
        /// 返回 DTO 需包含商品主表全部字段 + 对应分店价格字段
        /// </summary>
        /// <param name="search">搜索关键词（商品名称、货号、条码）,示例: "iPhone"</param>
        /// <param name="localSupplierCode">本地供应商代码过滤,示例: "SUP001"</param>
        /// <param name="isActive">是否启用状态过滤,示例: true</param>
        /// <param name="isSpecialProduct">是否特殊产品过滤,示例: false</param>
        /// <param name="warehouseCategoryGUID">仓库类别GUID过滤,示例: "12345678-1234-1234-1234-123456789012"</param>
        /// <param name="productType">产品类型过滤,示例: 1</param>
        /// <param name="updatedBy">更新人过滤,示例: "admin"</param>
        /// <param name="productPurchasePriceMin">商品主表最低进货价,使用 BETWEEN 语法,示例: 0</param>
        /// <param name="productPurchasePriceMax">商品主表最高进货价,使用 BETWEEN 语法,示例: 999999.99</param>
        /// <param name="productRetailPriceMin">商品主表最低零售价,使用 BETWEEN 语法,示例: 0</param>
        /// <param name="productRetailPriceMax">商品主表最高零售价,使用 BETWEEN 语法,示例: 999999.99</param>
        /// <param name="storeCodes">分店代码数组过滤,与商品主表执行 INNER JOIN,支持多分店,示例: STORE001,STORE002</param>
        /// <param name="storePurchasePriceMin">分店价格表最低进货价,使用 BETWEEN 语法,示例: 0</param>
        /// <param name="storePurchasePriceMax">分店价格表最高进货价,使用 BETWEEN 语法,示例: 999999.99</param>
        /// <param name="storeRetailPriceMin">分店价格表最低零售价,使用 BETWEEN 语法,示例: 0</param>
        /// <param name="storeRetailPriceMax">分店价格表最高零售价,使用 BETWEEN 语法,示例: 999999.99</param>
        /// <param name="storeDiscountRateMin">分店价格最低折扣率,使用 BETWEEN 语法,示例: 0</param>
        /// <param name="storeDiscountRateMax">分店价格最高折扣率,使用 BETWEEN 语法,示例: 1</param>
        /// <param name="storeIsActive">分店价格是否启用过滤,示例: true</param>
        /// <param name="storeIsAutoPricing">分店价格是否自动定价过滤,示例: false</param>
        /// <param name="pageNumber">页码（从1开始）,示例: 1</param>
        /// <param name="pageSize">每页大小,支持值: 20,50,100,200,500,1000,示例: 50</param>
        /// <param name="sortBy">排序字段,支持: ProductCode,ProductName,ProductPurchasePrice,ProductRetailPrice,StorePurchasePrice,StoreRetailPrice,StoreDiscountRate,CreatedAt,UpdatedAt,示例: "StoreRetailPrice"</param>
        /// <param name="sortOrder">排序方向: asc 或 desc,示例: "asc"</param>
        /// <returns>分页列表,包含商品主表全部字段 + 对应分店价格字段</returns>
        [HttpGet("price-filter")]
        public async Task<IActionResult> GetPriceFilteredProducts(
            [FromQuery] string? search = null,
            [FromQuery] string? localSupplierCode = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] bool? isSpecialProduct = null,
            [FromQuery] string? warehouseCategoryGUID = null,
            [FromQuery] int? productType = null,
            [FromQuery] string? updatedBy = null,
            [FromQuery] decimal? productPurchasePriceMin = null,
            [FromQuery] decimal? productPurchasePriceMax = null,
            [FromQuery] decimal? productRetailPriceMin = null,
            [FromQuery] decimal? productRetailPriceMax = null,
            [FromQuery] string storeCodes = "",
            [FromQuery] decimal? storePurchasePriceMin = null,
            [FromQuery] decimal? storePurchasePriceMax = null,
            [FromQuery] decimal? storeRetailPriceMin = null,
            [FromQuery] decimal? storeRetailPriceMax = null,
            [FromQuery] decimal? storeDiscountRateMin = null,
            [FromQuery] decimal? storeDiscountRateMax = null,
            [FromQuery] bool? storeIsActive = null,
            [FromQuery] bool? storeIsAutoPricing = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? sortBy = null,
            [FromQuery] string sortOrder = "asc"
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(storeCodes))
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = "分店代码数组不能为空,至少需要提供一个分店代码",
                        }
                    );
                }

                var filter = new ProductPriceFilterDto
                {
                    Search = search,
                    LocalSupplierCode = localSupplierCode,
                    IsActive = isActive,
                    IsSpecialProduct = isSpecialProduct,
                    WarehouseCategoryGUID = warehouseCategoryGUID,
                    ProductType = productType,
                    UpdatedBy = updatedBy,
                    ProductPurchasePriceMin = productPurchasePriceMin,
                    ProductPurchasePriceMax = productPurchasePriceMax,
                    ProductRetailPriceMin = productRetailPriceMin,
                    ProductRetailPriceMax = productRetailPriceMax,
                    StoreCodes = storeCodes
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .ToList(),
                    StorePurchasePriceMin = storePurchasePriceMin,
                    StorePurchasePriceMax = storePurchasePriceMax,
                    StoreRetailPriceMin = storeRetailPriceMin,
                    StoreRetailPriceMax = storeRetailPriceMax,
                    StoreDiscountRateMin = storeDiscountRateMin,
                    StoreDiscountRateMax = storeDiscountRateMax,
                    StoreIsActive = storeIsActive,
                    StoreIsAutoPricing = storeIsAutoPricing,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    SortBy = sortBy,
                    SortOrder = sortOrder,
                };

                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = "请求参数验证失败",
                            errors = ModelState,
                        }
                    );
                }

                var result = await _service.GetPriceFilteredPagedListAsync(filter);
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Items,
                        total = result.Total,
                        pageNumber = result.PageNumber,
                        pageSize = result.PageSize,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "高级过滤查询商品失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        #region 请求包装类

        /// <summary>
        /// 批量更新请求
        /// </summary>
        public class BatchUpdateRequest
        {
            public List<BatchUpdateProductReactDto> Items { get; set; } = new();
        }

        /// <summary>
        /// 批量删除请求
        /// </summary>
        public class BatchDeleteRequest
        {
            public List<string> ProductCodes { get; set; } = new();
            /// <summary>
            /// 删除模式：soft=软删除（默认），hard=物理删除
            /// </summary>
            public string? Mode { get; set; } = "soft";
        }

        #endregion

        /// <summary>
        /// 从HQ同步商品到本地（含增删改 + 关联表同步）
        /// </summary>
        [HttpPost("sync-from-hq")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SyncFromHq()
        {
            try
            {
                _logger.LogInformation("收到从HQ同步商品的请求");
                var result = await _service.SyncProductsFromHqAsync();
                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message
                    });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从HQ同步商品失败");
                return StatusCode(500, new { success = false, message = "从HQ同步商品失败：" + ex.Message });
            }
        }

        /// <summary>
        /// 同步商品到分店
        /// </summary>
        [HttpPost("sync-to-stores")]
        public async Task<IActionResult> SyncProductsToStores(
            [FromBody] SyncProductsToStoresRequest request
        )
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                _logger.LogInformation(
                    "同步商品到分店: {ProductCount} 个商品, {StoreCount} 个分店",
                    request.ProductCodes?.Count ?? 0,
                    request.StoreCodes?.Count ?? 0
                );

                var result = await _productStoreSyncService.SyncProductsToStoresAsync(request);

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
                else
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = result.Message,
                            errorCode = result.ErrorCode,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步商品到分店失败");
                return StatusCode(500, new { success = false, message = "同步商品到分店失败" });
            }
        }
    }
}
