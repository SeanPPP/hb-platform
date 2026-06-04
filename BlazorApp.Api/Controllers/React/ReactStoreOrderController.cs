using BlazorApp.Api.Cache;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using System.Diagnostics;
using System.Security.Claims;
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
        private readonly IUserService _userService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ICurrentUserManageableStoreScopeService _storeScopeService;
        private readonly IStoreOrderSyncJobService _storeOrderSyncJobService;

        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan AuthorizationSuccessCacheDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan AuthorizationFailureCacheDuration = TimeSpan.FromSeconds(5);
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
        private static readonly string[] CartWritePermissions =
        {
            Permissions.OrderFront.View,
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
        private static readonly string[] WarehouseOrderSyncPermissions =
        {
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private static readonly string[] GlobalStoreScopeRoles =
        {
            "Admin",
            "管理员",
            "WarehouseManager",
            "仓库经理",
        };
        private static readonly string[] ScopedStoreRoles =
        {
            "StoreManager",
            "店长",
            "经理",
        };

        private string GetScanTraceId()
        {
            // 前端扫码链路透传 traceId，缺失时使用 ASP.NET TraceIdentifier 兜底。
            return Request.Headers["X-Scan-Trace-Id"].FirstOrDefault()
                ?? HttpContext.TraceIdentifier;
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

        public ReactStoreOrderController(
            IStoreOrderReactService service,
            ILogger<ReactStoreOrderController> logger,
            IMemoryCache cache,
            IUserService userService,
            IStoreService storeService,
            IAuthorizationService authorizationService,
            ICurrentUserManageableStoreScopeService storeScopeService,
            IStoreOrderSyncJobService storeOrderSyncJobService
        )
        {
            _service = service;
            _logger = logger;
            _cache = cache;
            _userService = userService;
            _authorizationService = authorizationService;
            _storeScopeService = storeScopeService;
            _storeOrderSyncJobService = storeOrderSyncJobService;
        }

        private async Task<bool> HasAnyPermissionAsync(params string[] permissions)
        {
            return await HasAnyPermissionAsync(null, "global", permissions);
        }

        private async Task<bool> HasAnyPermissionAsync(
            string? storeCode,
            string checkType,
            params string[] permissions
        )
        {
            var userId = GetCurrentUserId();
            var normalizedStoreCode = NormalizeAuthorizationStoreCode(storeCode);
            foreach (var permission in permissions)
            {
                if (
                    await AuthorizePolicyWithCacheAsync(
                        userId,
                        normalizedStoreCode,
                        checkType,
                        permission
                    )
                )
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

        private async Task<IActionResult?> RequireAnyPermissionAsync(
            string? storeCode,
            string checkType,
            params string[] permissions
        )
        {
            return await HasAnyPermissionAsync(storeCode, checkType, permissions) ? null : Forbid();
        }

        private async Task<bool> AuthorizePolicyWithCacheAsync(
            string? userId,
            string normalizedStoreCode,
            string checkType,
            string permission
        )
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                var uncached = await _authorizationService.AuthorizeAsync(User, null, permission);
                return uncached.Succeeded;
            }

            var cacheKey = BuildAuthorizationCacheKey(
                "policy",
                userId,
                normalizedStoreCode,
                checkType,
                permission
            );
            if (_cache.TryGetValue<bool>(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            var result = await _authorizationService.AuthorizeAsync(User, null, permission);
            SetAuthorizationCache(cacheKey, result.Succeeded);
            return result.Succeeded;
        }

        private bool HasAnyRole(params string[] roleNames)
        {
            return User?.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && roleNames.Any(role =>
                    string.Equals(role, claim.Value, StringComparison.OrdinalIgnoreCase)
                )
            ) == true;
        }

        private bool IsStoreScopedUser()
        {
            return HasAnyRole(ScopedStoreRoles) && !HasAnyRole(GlobalStoreScopeRoles);
        }

        private bool IsRealAdmin()
        {
            return HasAnyRole("Admin", "管理员");
        }

        private async Task<IActionResult?> RequireStoreScopeAsync(string? storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return null;
            }

            return await _storeScopeService.CanAccessStoreCodeAsync(storeCode) ? null : Forbid();
        }

        private async Task<IActionResult?> RequireAssignedStoreScopeAsync(string? storeCode)
        {
            if (!string.IsNullOrWhiteSpace(storeCode))
            {
                var userGuid = GetCurrentUserId();
                var normalizedStoreCode = NormalizeAuthorizationStoreCode(storeCode);
                var cacheKey = BuildAuthorizationCacheKey(
                    "assigned-store-scope",
                    userGuid,
                    normalizedStoreCode,
                    "RequireAssignedStoreScopeAsync",
                    "manage-or-assigned"
                );

                if (
                    !string.IsNullOrWhiteSpace(userGuid)
                    && _cache.TryGetValue<bool>(cacheKey, out var cachedScopeAllowed)
                )
                {
                    return cachedScopeAllowed ? null : Forbid();
                }

                var isAllowed =
                    await _storeScopeService.CanAccessStoreCodeAsync(storeCode)
                    || await CanAccessAssignedStoreCodeAsync(storeCode);

                if (!string.IsNullOrWhiteSpace(userGuid))
                {
                    // 扫码链路会连续 lookup/add，同用户同门店的 scope 判断短 TTL 复用，避免重复查权限与用户分店。
                    SetAuthorizationCache(cacheKey, isAllowed);
                }

                return isAllowed ? null : Forbid();
            }

            return HasAnyRole(GlobalStoreScopeRoles) ? null : Forbid();
        }

        private static string NormalizeAuthorizationStoreCode(string? storeCode)
        {
            return string.IsNullOrWhiteSpace(storeCode)
                ? "none"
                : storeCode.Trim().ToUpperInvariant();
        }

        private static string BuildAuthorizationCacheKey(
            string cacheType,
            string? userId,
            string normalizedStoreCode,
            string checkType,
            string permissionOrScope
        )
        {
            return string.Join(
                ':',
                "ReactStoreOrderController",
                "authorization",
                cacheType,
                userId?.Trim() ?? "anonymous",
                normalizedStoreCode,
                checkType,
                permissionOrScope
            );
        }

        private void SetAuthorizationCache(string cacheKey, bool isAllowed)
        {
            var duration = isAllowed
                ? AuthorizationSuccessCacheDuration
                : AuthorizationFailureCacheDuration;

            _cache.Set(
                cacheKey,
                isAllowed,
                new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(duration)
                    .SetPriority(CacheItemPriority.Low)
            );
        }

        private async Task<bool> CanAccessAssignedStoreCodeAsync(string storeCode)
        {
            var userGuid = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return false;
            }

            var storesResult = await _userService.GetUserStoresAsync(userGuid);
            if (!storesResult.Success || storesResult.Data == null)
            {
                return false;
            }

            // 订货前台按“已分配分店”访问商品与购物车，不复用店长管理分店的 IsPrimary 限制。
            return storesResult.Data.Any(store =>
                !string.IsNullOrWhiteSpace(store.StoreCode)
                && store.StoreCode.Equals(storeCode.Trim(), StringComparison.OrdinalIgnoreCase)
            );
        }

        private async Task<(IActionResult? Forbidden, SyncMissingOrdersRequestDto? Request)>
            BuildScopedSyncRequestAsync(SyncMissingOrdersRequestDto? request)
        {
            var storeCodes = NormalizeSyncRequestStoreCodes(request);
            if (!IsStoreScopedUser())
            {
                return (null, request);
            }

            if (storeCodes.Count > 0)
            {
                foreach (var storeCode in storeCodes)
                {
                    var forbidden = await RequireStoreScopeAsync(storeCode);
                    if (forbidden != null)
                    {
                        return (forbidden, request);
                    }
                }

                return (null, request);
            }

            var scope = await _storeScopeService.GetScopeAsync();
            if (!scope.IsAllowed)
            {
                return (Forbid(), request);
            }

            if (scope.IsAdmin)
            {
                return (null, request);
            }

            var scopedStoreCodes = scope.StoreCodes
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (scopedStoreCodes.Count == 0)
            {
                return (Forbid(), request);
            }

            var scopedRequest = request ?? new SyncMissingOrdersRequestDto();
            scopedRequest.StoreCodes = scopedStoreCodes;
            return (null, scopedRequest);
        }

        private static List<string> NormalizeSyncRequestStoreCodes(
            SyncMissingOrdersRequestDto? request
        )
        {
            var storeCodes = (request?.StoreCodes ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (storeCodes.Count == 0 && !string.IsNullOrWhiteSpace(request?.StoreCode))
            {
                storeCodes.Add(request.StoreCode.Trim());
            }

            return storeCodes;
        }

        private async Task<(IActionResult? Forbidden, StoreOrderHqSyncRequestDto? Request)>
            BuildScopedHqSyncRequestAsync(StoreOrderHqSyncRequestDto? request)
        {
            var storeCodes = NormalizeHqSyncRequestStoreCodes(request);
            if (!IsStoreScopedUser())
            {
                return (null, request);
            }

            if (storeCodes.Count > 0)
            {
                foreach (var storeCode in storeCodes)
                {
                    var forbidden = await RequireStoreScopeAsync(storeCode);
                    if (forbidden != null)
                    {
                        return (forbidden, request);
                    }
                }

                return (null, request);
            }

            var scope = await _storeScopeService.GetScopeAsync();
            if (!scope.IsAllowed || scope.IsAdmin)
            {
                return (Forbid(), request);
            }

            var scopedStoreCodes = scope.StoreCodes
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (scopedStoreCodes.Count == 0)
            {
                return (Forbid(), request);
            }

            var scopedRequest = request ?? new StoreOrderHqSyncRequestDto();
            scopedRequest.StoreCodes = scopedStoreCodes;
            return (null, scopedRequest);
        }

        private static List<string> NormalizeHqSyncRequestStoreCodes(
            StoreOrderHqSyncRequestDto? request
        )
        {
            var storeCodes = (request?.StoreCodes ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (storeCodes.Count == 0 && !string.IsNullOrWhiteSpace(request?.StoreCode))
            {
                storeCodes.Add(request.StoreCode.Trim());
            }

            return storeCodes;
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

        private string? GetCurrentUserId()
        {
            return User?.FindFirst("userId")?.Value
                ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
                    await RequireAnyPermissionAsync(OrderReadPermissions)
                    ?? await RequireAssignedStoreScopeAsync(filter.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                if (!string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID))
                {
                    var orderForbidden = await RequireOrderScopeAsync(filter.ExcludeOrderGUID.Trim());
                    if (orderForbidden != null)
                    {
                        return orderForbidden;
                    }
                }

                var shouldUseProductCache =
                    !filter.ExcludeExistingWarehouseProducts
                    && string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID);

                string? cacheKey = null;
                if (shouldUseProductCache)
                {
                    cacheKey = StoreOrderCacheKeys.Products(filter);
                }

                //// 尝试从缓存获取
                if (
                    shouldUseProductCache
                    &&
                    _cache.TryGetValue<PagedListReactDto<StoreOrderProductDto>>(
                        cacheKey!,
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
                if (shouldUseProductCache)
                {
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(CACHE_DURATION)
                        .SetPriority(CacheItemPriority.Normal);

                    _cache.Set(cacheKey!, result, cacheOptions);
                    _logger.LogDebug(
                        "商品列表已缓存: {CacheKey}, 过期时间: {Expiration}",
                        cacheKey,
                        DateTime.Now.Add(CACHE_DURATION)
                    );
                }

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
                var forbidden = await RequireAnyPermissionAsync(OrderReadPermissions);
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
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden =
                    await RequireAnyPermissionAsync(
                        request.StoreCode,
                        "scan-order-flow",
                        OrderReadPermissions
                    )
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
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
                var result = await _service.ScanLookupProductsAsync(request);
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

        /// <summary>
        /// 获取分店当前的购物车
        /// </summary>
        [HttpGet("cart/{storeCode}")]
        public async Task<IActionResult> GetActiveCart(string storeCode)
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderReadPermissions)
                    ?? await RequireAssignedStoreScopeAsync(storeCode);
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
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden =
                    await RequireAnyPermissionAsync(
                        request.StoreCode,
                        "scan-order-flow",
                        CartWritePermissions
                    )
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
                permissionSw.Stop();
                if (forbidden != null)
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.add.controller.forbidden storeCode={StoreCode} productCode={ProductCode} permissionMs={PermissionMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        permissionSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return forbidden;
                }

                var serviceSw = Stopwatch.StartNew();
                var result = await _service.AddToCartAsync(request);
                serviceSw.Stop();
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=cart.add.controller.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} totalQuantity={TotalQuantity} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    result.Success,
                    result.Data?.TotalQuantity ?? 0,
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
                    "[shop-scan-perf] traceId={TraceId} stage=cart.add.controller.error storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    totalSw.ElapsedMilliseconds
                );
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
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden =
                    await RequireAnyPermissionAsync(
                        request.StoreCode,
                        "scan-order-flow",
                        CartWritePermissions
                    )
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
                permissionSw.Stop();
                if (forbidden != null)
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.update.controller.forbidden storeCode={StoreCode} productCode={ProductCode} permissionMs={PermissionMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        permissionSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return forbidden;
                }

                var serviceSw = Stopwatch.StartNew();
                var result = await _service.UpdateCartItemAsync(request);
                serviceSw.Stop();
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=cart.update.controller.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} totalQuantity={TotalQuantity} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    result.Success,
                    result.Data?.TotalQuantity ?? 0,
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
                    "[shop-scan-perf] traceId={TraceId} stage=cart.update.controller.error storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    totalSw.ElapsedMilliseconds
                );
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
                    await RequireAnyPermissionAsync(CartWritePermissions)
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
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
                    await RequireAnyPermissionAsync(CartWritePermissions)
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
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
                    await RequireAnyPermissionAsync(CartWritePermissions)
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
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
                    await RequireAnyPermissionAsync(OrderReadPermissions)
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
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
        public async Task<IActionResult> GetOrderDetail(
            string orderGuid,
            [FromQuery] StoreOrderDetailQueryDto query
        )
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

                var result = await _service.GetOrderDetailAsync(orderGuid, query);
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
        /// 获取订单全量详情，供打印和发票页面使用。
        /// </summary>
        [HttpGet("detail/{orderGuid}/full")]
        public async Task<IActionResult> GetOrderDetailFull(string orderGuid)
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

                var result = await _service.GetOrderDetailFullAsync(orderGuid);
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
        /// 更新订单关联分店的联系信息。
        /// </summary>
        [HttpPost("store-contact/update")]
        public async Task<IActionResult> UpdateStoreContact(
            [FromBody] UpdateStoreOrderStoreContactDto request
        )
        {
            try
            {
                var forbidden =
                    await RequireAnyPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID)
                    ?? await RequireStoreScopeAsync(request.StoreCode);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateStoreContactAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Message });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateStoreContact failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 发送订单发票邮件。
        /// </summary>
        [HttpPost("invoice/email")]
        public async Task<IActionResult> SendInvoiceEmail(
            [FromBody] SendStoreOrderInvoiceEmailDto request
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

                var result = await _service.SendInvoiceEmailAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, message = result.Message });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendInvoiceEmail failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取订单已包含商品编码，供分页明细页跨页去重使用。
        /// </summary>
        [HttpGet("detail/{orderGuid}/product-codes")]
        public async Task<IActionResult> GetOrderDetailProductCodes(string orderGuid)
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

                var result = await _service.GetOrderDetailProductCodesAsync(orderGuid);
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
                _logger.LogError(ex, "GetOrderDetailProductCodes failed");
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
                var forbidden = await RequireAnyPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var scoped = await BuildScopedSyncRequestAsync(request);
                if (scoped.Forbidden != null)
                {
                    return scoped.Forbidden;
                }

                var result = await _service.SyncMissingOrdersFromHqAsync(scoped.Request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SyncMissingOrders failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 创建分店订货缺失订单同步 job。
        /// </summary>
        [HttpPost("sync-missing-orders/jobs")]
        public async Task<IActionResult> CreateSyncMissingOrdersJob(
            [FromBody] SyncMissingOrdersRequestDto? request
        )
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var scoped = await BuildScopedSyncRequestAsync(request);
                if (scoped.Forbidden != null)
                {
                    return scoped.Forbidden;
                }

                var userId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized(new { success = false, message = "未获取到当前用户" });
                }

                var job = await _storeOrderSyncJobService.StartJobAsync(
                    userId,
                    scoped.Request,
                    HttpContext.RequestAborted
                );
                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException invalidOperation
                    && invalidOperation.Message.Contains("已有分店订货同步任务", StringComparison.Ordinal))
                {
                    return Conflict(new { success = false, message = invalidOperation.Message });
                }

                _logger.LogError(ex, "CreateSyncMissingOrdersJob failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取分店订货缺失订单同步 job 状态。
        /// </summary>
        [HttpGet("sync-missing-orders/jobs/{jobId}")]
        public async Task<IActionResult> GetSyncMissingOrdersJob(string jobId)
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var job = await _storeOrderSyncJobService.GetJobAsync(
                    jobId,
                    HttpContext.RequestAborted
                );
                if (job == null)
                {
                    return NotFound(new { success = false, message = "任务不存在" });
                }

                if (IsStoreScopedUser())
                {
                    if (job.StoreCodes.Count == 0)
                    {
                        return Forbid();
                    }

                    foreach (var storeCode in job.StoreCodes)
                    {
                        var scopeForbidden = await RequireStoreScopeAsync(storeCode);
                        if (scopeForbidden != null)
                        {
                            return scopeForbidden;
                        }
                    }
                }

                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSyncMissingOrdersJob failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 创建分店订货 HQ 全量同步 job。全量同步只允许真实 Admin，且忽略前端筛选条件。
        /// </summary>
        [HttpPost("hq-sync/full/jobs")]
        public async Task<IActionResult> CreateStoreOrderHqFullSyncJob(
            [FromBody] StoreOrderHqSyncRequestDto? request
        )
        {
            try
            {
                if (!IsRealAdmin())
                {
                    return Forbid();
                }

                var userId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized(new { success = false, message = "未获取到当前用户" });
                }

                // 全量同步是全库行为，服务端主动丢弃页面分店/客户筛选。
                var job = await _storeOrderSyncJobService.StartHqSyncJobAsync(
                    userId,
                    StoreOrderHqSyncMode.Full,
                    new StoreOrderHqSyncRequestDto(),
                    HttpContext.RequestAborted
                );
                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException invalidOperation
                    && invalidOperation.Message.Contains("已有分店订货同步任务", StringComparison.Ordinal))
                {
                    return Conflict(new { success = false, message = invalidOperation.Message });
                }

                _logger.LogError(ex, "CreateStoreOrderHqFullSyncJob failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 创建分店订货 HQ 增量同步 job。
        /// </summary>
        [HttpPost("hq-sync/incremental/jobs")]
        public async Task<IActionResult> CreateStoreOrderHqIncrementalSyncJob(
            [FromBody] StoreOrderHqSyncRequestDto? request
        )
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var scoped = await BuildScopedHqSyncRequestAsync(request);
                if (scoped.Forbidden != null)
                {
                    return scoped.Forbidden;
                }

                var userId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized(new { success = false, message = "未获取到当前用户" });
                }

                var job = await _storeOrderSyncJobService.StartHqSyncJobAsync(
                    userId,
                    StoreOrderHqSyncMode.Incremental,
                    scoped.Request,
                    HttpContext.RequestAborted
                );
                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException invalidOperation
                    && invalidOperation.Message.Contains("已有分店订货同步任务", StringComparison.Ordinal))
                {
                    return Conflict(new { success = false, message = invalidOperation.Message });
                }

                _logger.LogError(ex, "CreateStoreOrderHqIncrementalSyncJob failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取分店订货 HQ 同步 job 状态。
        /// </summary>
        [HttpGet("hq-sync/jobs/{jobId}")]
        public async Task<IActionResult> GetStoreOrderHqSyncJob(string jobId)
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var job = await _storeOrderSyncJobService.GetJobAsync(
                    jobId,
                    HttpContext.RequestAborted
                );
                if (job == null)
                {
                    return NotFound(new { success = false, message = "任务不存在" });
                }

                if (
                    string.Equals(job.Mode, StoreOrderHqSyncMode.Full.ToString(), StringComparison.OrdinalIgnoreCase)
                )
                {
                    return IsRealAdmin()
                        ? Ok(new { success = true, data = job })
                        : Forbid();
                }

                if (IsStoreScopedUser())
                {
                    if (job.StoreCodes.Count == 0)
                    {
                        return Forbid();
                    }

                    foreach (var storeCode in job.StoreCodes)
                    {
                        var scopeForbidden = await RequireStoreScopeAsync(storeCode);
                        if (scopeForbidden != null)
                        {
                            return scopeForbidden;
                        }
                    }
                }

                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStoreOrderHqSyncJob failed");
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
