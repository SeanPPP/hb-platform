using BlazorApp.Api.Cache;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// React 订货页面专用控制器
    /// </summary>
    [ApiController]
    [Route("api/react/v1/store-order")]
    [Authorize]
    public class ReactStoreOrderController : ControllerBase
    {
        private readonly IStoreOrderReactService _service;
        private readonly ILogger<ReactStoreOrderController> _logger;
        private readonly IMemoryCache _cache;
        private readonly IAuthorizationService _authorizationService;
        private readonly ICurrentUserManageableStoreScopeService _storeScopeService;

        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly string[] OrderReadPermissions =
        {
            Permissions.OrderFront.View,
            Permissions.Orders.View,
            Permissions.Warehouse.ManageOrders,
        };
        private static readonly string[] OrderCreatePermissions =
        {
            Permissions.Orders.Create,
            Permissions.Warehouse.ManageOrders,
        };
        private static readonly string[] OrderEditPermissions =
        {
            Permissions.Orders.Edit,
            Permissions.Warehouse.ManageOrders,
        };
        private static readonly string[] OrderDeletePermissions =
        {
            Permissions.Orders.Delete,
            Permissions.Warehouse.ManageOrders,
        };

        public ReactStoreOrderController(
            IStoreOrderReactService service,
            ILogger<ReactStoreOrderController> logger,
            IMemoryCache cache,
            IUserService userService,
            IStoreService storeService,
            IAuthorizationService authorizationService,
            ICurrentUserManageableStoreScopeService storeScopeService
        )
        {
            _service = service;
            _logger = logger;
            _cache = cache;
            _authorizationService = authorizationService;
            _storeScopeService = storeScopeService;
        }

        private async Task<bool> HasAnyPermissionAsync(params string[] permissions)
        {
            foreach (var permission in permissions)
            {
                var result = await _authorizationService.AuthorizeAsync(User, null, permission);
                if (result.Succeeded)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<IActionResult?> RequireAnyPermissionAsync(params string[] permissions)
        {
            return await HasAnyPermissionAsync(permissions) ? null : Forbid();
        }

        private async Task<IActionResult?> RequireStoreScopeAsync(string? storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return null;
            }

            return await _storeScopeService.CanAccessStoreCodeAsync(storeCode) ? null : Forbid();
        }

        private async Task<IActionResult?> RequireOrderScopeAsync(string orderGuid)
        {
            return await _storeScopeService.CanAccessOrderAsync(orderGuid) ? null : Forbid();
        }

        private async Task<IActionResult?> RequireOrderScopesAsync(IEnumerable<string> orderGuids)
        {
            foreach (var orderGuid in orderGuids.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                var forbidden = await RequireOrderScopeAsync(orderGuid);
                if (forbidden != null)
                {
                    return forbidden;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取商品列表 (支持货号搜索和分类筛选)
        /// </summary>
        [HttpPost("products")]
        public async Task<IActionResult> GetProducts([FromBody] StoreOrderFilterDto filter)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(filter.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                // 生成缓存键
                var cacheKey = StoreOrderCacheKeys.Products(filter);

                //// 尝试从缓存获取
                if (
                    _cache.TryGetValue<PagedListReactDto<StoreOrderProductDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                )
                {
                    _logger.LogDebug("从缓存获取商品列表: {CacheKey}", cacheKey);
                    return Ok(new { success = true, data = cachedResult });
                }

                // 缓存未命中，从服务获取
                _logger.LogDebug("缓存未命中，从服务获取商品列表: {CacheKey}", cacheKey);
                var result = await _service.GetPagedListAsync(filter);

                // 将结果存入缓存
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(CACHE_DURATION)
                    .SetPriority(CacheItemPriority.Normal);

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogDebug(
                    "商品列表已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(CACHE_DURATION)
                );

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProducts failed");
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
                var forbidden = await RequireAnyPermissionAsync(Permissions.OrderFront.View);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.BatchLookupProductsAsync(request);
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
            try
            {
                var forbidden = await RequireAnyPermissionAsync(Permissions.OrderFront.View);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.ScanLookupProductsAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScanLookupProducts failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取分店当前的购物车
        /// </summary>
        [HttpGet("cart/{storeCode}")]
        public async Task<IActionResult> GetActiveCart(string storeCode)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(storeCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetActiveCartAsync(storeCode);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetActiveCart failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 添加到购物车
        /// </summary>
        [HttpPost("cart/add")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartRequestDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.AddToCartAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddToCart failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新购物车项数量 (覆盖)
        /// </summary>
        [HttpPost("cart/update")]
        public async Task<IActionResult> UpdateCartItem([FromBody] AddToCartRequestDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateCartItemAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateCartItem failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 移除购物车项
        /// </summary>
        [HttpPost("cart/remove")]
        public async Task<IActionResult> RemoveFromCart([FromBody] RemoveFromCartRequestDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.RemoveFromCartAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveFromCart failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 清空购物车
        /// </summary>
        [HttpPost("cart/clear")]
        public async Task<IActionResult> ClearCart([FromBody] ClearCartRequestDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.ClearCartAsync(request.StoreCode);
                if (result.Success)
                {
                    return Ok(result);
                }
                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ClearCart failed");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Internal server error"
                });
            }
        }

        /// <summary>
        /// 提交订单
        /// </summary>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitOrder([FromBody] SubmitStoreOrderRequestDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.SubmitOrderAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitOrder failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取商品动态数据 (历史订单 + 购物车数量)
        /// </summary>
        [HttpPost("dynamic-data")]
        public async Task<IActionResult> GetDynamicData(
            [FromBody] StoreOrderDynamicDataRequestDto request
        )
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.OrderFront.View)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetProductsDynamicDataAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDynamicData failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取订单列表
        /// </summary>
        [HttpPost("list")]
        public async Task<IActionResult> GetOrderList([FromBody] StoreOrderListFilterDto filter)
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(OrderReadPermissions);
                if (forbidden == null)
                {
                    forbidden = await RequireStoreScopeAsync(filter.StoreCode);
                }
                if (forbidden == null && filter.StoreCodes != null)
                {
                    foreach (var storeCode in filter.StoreCodes)
                    {
                        forbidden = await RequireStoreScopeAsync(storeCode);
                        if (forbidden != null)
                        {
                            break;
                        }
                    }
                }
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetOrderListAsync(filter);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetOrderList failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取订单详情
        /// </summary>
        [HttpGet("detail/{orderGuid}")]
        public async Task<IActionResult> GetOrderDetail(string orderGuid)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderReadPermissions)
                    ?? await RequireOrderScopeAsync(orderGuid);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetOrderDetailAsync(orderGuid);
                return Ok(
                    new
                    {
                        success = result.Success,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetOrderDetail failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 创建新订单 (FlowStatus=1)
        /// </summary>
        [HttpPost("create")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateStoreOrderDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderCreatePermissions)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.CreateOrderAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateOrder failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 添加商品到指定订单
        /// </summary>
        [HttpPost("line/add")]
        public async Task<IActionResult> AddOrderLine([FromBody] AddOrderLineDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.AddOrderLineAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddOrderLine failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量添加商品到指定订单
        /// </summary>
        [HttpPost("line/batch-add")]
        public async Task<IActionResult> BatchAddOrderLine([FromBody] BatchAddOrderLineDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.BatchAddOrderLineAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchAddOrderLine failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// Excel 粘贴覆盖订单行
        /// </summary>
        [HttpPost("line/paste-replace")]
        public async Task<IActionResult> PasteReplaceOrderLines(
            [FromBody] PasteReplaceOrderLinesDto request
        )
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.PasteReplaceOrderLinesAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PasteReplaceOrderLines failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新指定订单行数量
        /// </summary>
        [HttpPost("line/update")]
        public async Task<IActionResult> UpdateOrderLine([FromBody] UpdateOrderLineDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateOrderLineAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderLine failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 软删除指定订单行
        /// </summary>
        [HttpPost("line/remove")]
        public async Task<IActionResult> RemoveOrderLine([FromBody] RemoveOrderLineDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.RemoveOrderLineAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveOrderLine failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量更新订单行数量或价格
        /// </summary>
        [HttpPost("line/batch-update")]
        public async Task<IActionResult> BatchUpdateOrderLine(
            [FromBody] BatchUpdateOrderLineDto request
        )
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.BatchUpdateOrderLineAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateOrderLine failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新商品状态 (单个)
        /// </summary>
        [HttpPost("product/status")]
        public async Task<IActionResult> UpdateProductStatus(
            [FromBody] UpdateProductStatusDto request
        )
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(OrderEditPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateProductStatusAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateProductStatus failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量更新商品状态
        /// </summary>
        [HttpPost("product/batch-status")]
        public async Task<IActionResult> BatchUpdateProductStatus(
            [FromBody] BatchUpdateProductStatusDto request
        )
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(OrderEditPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.BatchUpdateProductStatusAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateProductStatus failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新订单头信息
        /// </summary>
        [HttpPost("header/update")]
        public async Task<IActionResult> UpdateOrderHeader([FromBody] UpdateOrderHeaderDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGuid);
                if (forbidden == null)
                {
                    forbidden = await RequireStoreScopeAsync(request.StoreCode);
                }
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateOrderHeaderAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderHeader failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取订单中使用过的分店信息
        /// </summary>
        [HttpGet("used-branches")]
        public async Task<IActionResult> GetUsedBranches()
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(OrderReadPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetUsedBranchesAsync();
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUsedBranches failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取当前用户可访问的分店代码列表
        /// 管理员和仓库管理员返回所有分店代码，普通用户返回其关联的分店代码列表
        /// GET api/react/v1/store-order/accessible-branches
        /// </summary>
        [HttpGet("accessible-branches")]
        public async Task<IActionResult> GetAccessibleBranches()
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(OrderReadPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var branchCodes = await _storeScopeService.GetAccessibleStoreCodesAsync();
                return Ok(new { success = true, data = branchCodes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAccessibleBranches failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 删除订单 (软删除)
        /// </summary>
        [HttpDelete("{orderGuid}")]
        public async Task<IActionResult> DeleteOrder(string orderGuid)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderDeletePermissions)
                    ?? await RequireOrderScopeAsync(orderGuid);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.DeleteOrderAsync(orderGuid);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteOrder failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 复制订单到另一个分店
        /// </summary>
        [HttpPost("copy")]
        public async Task<IActionResult> CopyOrder([FromBody] CopyOrderDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderCreatePermissions)
                    ?? await RequireOrderScopeAsync(request.SourceOrderGUID)
                    ?? await RequireStoreScopeAsync(request.TargetStoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.CopyOrderAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CopyOrder failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 从 HQ 同步本地不存在的仓库订单（主表 + 明细表）
        /// </summary>
        [HttpPost("sync-missing-orders")]
        public async Task<IActionResult> SyncMissingOrders(
            [FromBody] SyncMissingOrdersRequestDto? request
        )
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(Permissions.Warehouse.ManageOrders)
                    ?? await RequireStoreScopeAsync(request?.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var storeCode = request?.StoreCode;
                var result = await _service.SyncMissingOrdersFromHqAsync(storeCode);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SyncMissingOrders failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 完成订单 (FlowStatus -> 2)
        /// </summary>
        [HttpPost("complete/{orderGuid}")]
        public async Task<IActionResult> CompleteOrder(string orderGuid)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(orderGuid);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.CompleteOrderAsync(orderGuid);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CompleteOrder failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 开始配货 (FlowStatus -> 3)
        /// </summary>
        [HttpPost("start-picking/{orderGuid}")]
        public async Task<IActionResult> StartPicking(string orderGuid)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(orderGuid);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.StartPickingAsync(orderGuid);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartPicking failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新订单状态 (Submitted ↔ Completed)
        /// </summary>
        [HttpPost("status")]
        public async Task<IActionResult> UpdateOrderStatus([FromBody] UpdateOrderStatusDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateOrderStatusAsync(request.OrderGUID, request.NewStatus);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderStatus failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量更新订单状态
        /// </summary>
        [HttpPost("batch-status")]
        public async Task<IActionResult> BatchUpdateOrderStatus([FromBody] BatchUpdateOrderStatusDto request)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopesAsync(request.OrderGUIDs);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.BatchUpdateOrderStatusAsync(request.OrderGUIDs, request.NewStatus);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateOrderStatus failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }
}
