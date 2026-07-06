using BlazorApp.Api.Cache;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
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
        private readonly SqlSugarContext _dbContext;
        private readonly IUserService _userService;
        private readonly IAuthorizationService _authorizationService;
        private readonly ICurrentUserManageableStoreScopeService _storeScopeService;
        private readonly IStoreOrderSyncJobService _storeOrderSyncJobService;
        private readonly IStoreOrderInvoiceEmailJobService _invoiceEmailJobService;
        private readonly IStoreOrderPasteReplaceJobService _pasteReplaceJobService;
        private readonly IStoreOrderInvoiceEmailTextTranslationService _invoiceEmailTextTranslationService;

        private const string ScanTraceHeaderName = "X-Scan-Trace-Id";
        private const string CartFlowCheckType = "cart-flow";
        private const string ScanOrderFlowCheckType = "scan-order-flow";
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan AuthorizationSuccessCacheDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan AuthorizationFailureCacheDuration = TimeSpan.FromSeconds(5);
        private static readonly string[] OrderReadPermissions =
        {
            Permissions.OrderFront.View,
            Permissions.Orders.View,
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private static readonly string[] OrderCreatePermissions =
        {
            Permissions.Orders.Create,
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private static readonly string[] CartWritePermissions =
        {
            Permissions.OrderFront.View,
            Permissions.Orders.Create,
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private static readonly string[] OrderEditPermissions =
        {
            Permissions.Orders.Edit,
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private static readonly string[] OrderDeletePermissions =
        {
            Permissions.Orders.Delete,
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private static readonly string[] WarehouseOrderSyncPermissions =
        {
            Permissions.Warehouse.ManageOrders,
            Permissions.Warehouse.Manage,
        };
        private static readonly string[] ImportPriceRefreshRoles =
        {
            "Admin",
            "管理员",
            "WarehouseManager",
            "仓库经理",
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
            return GetExplicitScanTraceId() ?? HttpContext.TraceIdentifier;
        }

        private string? GetExplicitScanTraceId()
        {
            var traceId = Request.Headers[ScanTraceHeaderName].FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(traceId) ? null : traceId;
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
            SqlSugarContext dbContext,
            IUserService userService,
            IStoreService storeService,
            IAuthorizationService authorizationService,
            ICurrentUserManageableStoreScopeService storeScopeService,
            IStoreOrderSyncJobService storeOrderSyncJobService,
            IStoreOrderInvoiceEmailJobService invoiceEmailJobService,
            IStoreOrderPasteReplaceJobService pasteReplaceJobService,
            IStoreOrderInvoiceEmailTextTranslationService invoiceEmailTextTranslationService
        )
        {
            _service = service;
            _logger = logger;
            _cache = cache;
            _dbContext = dbContext;
            _userService = userService;
            _authorizationService = authorizationService;
            _storeScopeService = storeScopeService;
            _storeOrderSyncJobService = storeOrderSyncJobService;
            _invoiceEmailJobService = invoiceEmailJobService;
            _pasteReplaceJobService = pasteReplaceJobService;
            _invoiceEmailTextTranslationService = invoiceEmailTextTranslationService;
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
            // 权限 policy 不依赖门店；门店范围由 assigned-store-scope 另行缓存，扫码热路径才能复用商品页预热结果。
            var permissionsCacheKey = BuildAuthorizationCacheKey(
                "permissions",
                userId,
                "any",
                "any",
                string.Join("|", permissions)
            );

            if (
                !string.IsNullOrWhiteSpace(userId)
                && _cache.TryGetValue<bool>(permissionsCacheKey, out var cachedPermissionsResult)
            )
            {
                LogScanAuthorizationMetric(
                    "authorization.permissions",
                    normalizedStoreCode,
                    checkType,
                    true,
                    0
                );
                return cachedPermissionsResult;
            }

            var sw = Stopwatch.StartNew();
            var isAllowed = false;
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
                    isAllowed = true;
                    break;
                }
            }

            sw.Stop();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                // 扫码会连续做 lookup/add，整组权限结果短 TTL 复用，语义仍由逐 policy 判断决定。
                SetAuthorizationCache(permissionsCacheKey, isAllowed);
            }

            LogScanAuthorizationMetric(
                "authorization.permissions",
                normalizedStoreCode,
                checkType,
                false,
                sw.ElapsedMilliseconds
            );
            return isAllowed;
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

        private bool IsWarehouseStaffOnly()
        {
            return HasAnyRole("WarehouseStaff", "仓库员工")
                && !IsRealAdmin()
                && !HasAnyRole("WarehouseManager", "仓库经理");
        }

        private async Task<IActionResult?> RequireOrderManagementActionPermissionAsync(
            params string[] permissions
        )
        {
            if (IsWarehouseStaffOnly())
            {
                // 纯 WarehouseStaff 不能借旧 Warehouse.Manage 执行普通订货写动作；购物车动作走 RequireCartWritePermissionAsync。
                return Forbid();
            }

            return await RequireAnyPermissionAsync(permissions);
        }

        private async Task<IActionResult?> RequireOrderManagementActionPermissionAsync(
            string? storeCode,
            string checkType,
            params string[] permissions
        )
        {
            if (IsWarehouseStaffOnly())
            {
                // 纯 WarehouseStaff 不能借旧 Warehouse.Manage 执行普通订货写动作；购物车动作走 RequireCartWritePermissionAsync。
                return Forbid();
            }

            return await RequireAnyPermissionAsync(storeCode, checkType, permissions);
        }

        private async Task<IActionResult?> RequireCartWritePermissionAsync(string? storeCode, string checkType)
        {
            var permissions = IsWarehouseStaffOnly()
                ? new[] { Permissions.Orders.Create }
                : CartWritePermissions;

            // WarehouseStaff 的购物车写入只认显式 Orders.Create，不把旧 Warehouse.Manage 当成订货写权限。
            return await RequireAnyPermissionAsync(storeCode, checkType, permissions)
                ?? await RequireAssignedStoreScopeAsync(storeCode);
        }

        private Task<IActionResult?> RequireCartWritePermissionAsync(AddToCartRequestDto request)
        {
            return RequireCartWritePermissionAsync(request.StoreCode, ScanOrderFlowCheckType);
        }

        private async Task<IActionResult?> RequireCreateOrderPermissionAsync(string? storeCode)
        {
            if (IsWarehouseStaffOnly())
            {
                // 仓库员工代建订单走正式建单路径，只认显式 Orders.Create，不继承旧 Warehouse.Manage。
                return await RequireAnyPermissionAsync(Permissions.Orders.Create);
            }

            return await RequireOrderManagementActionPermissionAsync(OrderCreatePermissions)
                ?? await RequireStoreScopeAsync(storeCode);
        }

        private async Task<IActionResult?> RequireOrderLineMutationPermissionAsync(string orderGuid)
        {
            if (IsWarehouseStaffOnly())
            {
                // 仓库员工维护代建正式订单明细，只认显式 Orders.Edit，不混用分店购物车或单店 scope。
                return await RequireAnyPermissionAsync(Permissions.Orders.Edit);
            }

            return await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
                ?? await RequireOrderScopeAsync(orderGuid);
        }

        private async Task<bool> AuthorizePolicyWithCacheAsync(
            string? userId,
            string normalizedStoreCode,
            string checkType,
            string permission
        )
        {
            var sw = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(userId))
            {
                var uncached = await _authorizationService.AuthorizeAsync(User, null, permission);
                sw.Stop();
                LogScanAuthorizationMetric(
                    "authorization.policy",
                    normalizedStoreCode,
                    checkType,
                    false,
                    sw.ElapsedMilliseconds,
                    permission
                );
                return uncached.Succeeded;
            }

            var cacheKey = BuildAuthorizationCacheKey(
                "policy",
                userId,
                "any",
                "any",
                permission
            );
            if (_cache.TryGetValue<bool>(cacheKey, out var cachedResult))
            {
                sw.Stop();
                LogScanAuthorizationMetric(
                    "authorization.policy",
                    normalizedStoreCode,
                    checkType,
                    true,
                    sw.ElapsedMilliseconds,
                    permission
                );
                return cachedResult;
            }

            var result = await _authorizationService.AuthorizeAsync(User, null, permission);
            sw.Stop();
            SetAuthorizationCache(cacheKey, result.Succeeded);
            LogScanAuthorizationMetric(
                "authorization.policy",
                normalizedStoreCode,
                checkType,
                false,
                sw.ElapsedMilliseconds,
                permission
            );
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

        private async Task<bool> HasGlobalWarehouseOrderScopeAsync()
        {
            var traceId = GetExplicitScanTraceId();
            var sw = Stopwatch.StartNew();
            // 仅真实管理员或显式仓库订货管理权限拥有全分店订货范围；角色名本身不绕过订单 scope。
            var hasScope = IsRealAdmin()
                || await HasAnyPermissionAsync(new[]
                {
                    Permissions.Warehouse.ManageOrders,
                    Permissions.Warehouse.Manage
                });
            sw.Stop();

            if (!string.IsNullOrWhiteSpace(traceId))
            {
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=authorization.global-scope allowed={Allowed} elapsedMs={ElapsedMs}",
                    traceId,
                    hasScope,
                    sw.ElapsedMilliseconds
                );
            }

            return hasScope;
        }

        private async Task<IActionResult?> RequireStoreScopeAsync(string? storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return null;
            }

            if (await HasGlobalWarehouseOrderScopeAsync())
            {
                // 分店订货控制器内，仓库订货管理权限可跨分店处理订单，不影响其它模块的分店范围服务。
                return null;
            }

            return await _storeScopeService.CanAccessStoreCodeAsync(storeCode) ? null : Forbid();
        }

        private async Task<IActionResult?> RequireAssignedStoreScopeAsync(string? storeCode)
        {
            if (!string.IsNullOrWhiteSpace(storeCode))
            {
                if (await HasGlobalWarehouseOrderScopeAsync())
                {
                    // 拥有仓库订货管理权限时无需再落到已分配分店兜底判断。
                    return null;
                }

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
                    LogAssignedStoreScopeMetric(normalizedStoreCode, true, 0, null);
                    return cachedScopeAllowed ? null : Forbid();
                }

                var sw = Stopwatch.StartNew();
                var isAllowed =
                    await _storeScopeService.CanAccessStoreCodeAsync(storeCode)
                    || await CanAccessAssignedStoreCodeAsync(storeCode);
                sw.Stop();

                if (!string.IsNullOrWhiteSpace(userGuid))
                {
                    // 扫码链路会连续 lookup/add，同用户同门店的 scope 判断短 TTL 复用，避免重复查权限与用户分店。
                    SetAuthorizationCache(cacheKey, isAllowed);
                }

                LogAssignedStoreScopeMetric(normalizedStoreCode, false, sw.ElapsedMilliseconds, isAllowed);
                return isAllowed ? null : Forbid();
            }

            return await HasGlobalWarehouseOrderScopeAsync() ? null : Forbid();
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

        private void LogScanAuthorizationMetric(
            string stage,
            string normalizedStoreCode,
            string checkType,
            bool cacheHit,
            long elapsedMs,
            string? permission = null
        )
        {
            var traceId = GetExplicitScanTraceId();
            if (string.IsNullOrWhiteSpace(traceId) || !IsScanAuthorizationCheckType(checkType))
            {
                return;
            }

            _logger.LogInformation(
                "[shop-scan-perf] traceId={TraceId} stage={Stage} storeCode={StoreCode} checkType={CheckType} permission={Permission} cacheHit={CacheHit} elapsedMs={ElapsedMs}",
                traceId,
                stage,
                normalizedStoreCode,
                checkType,
                permission ?? "all",
                cacheHit,
                elapsedMs
            );
        }

        private void LogAssignedStoreScopeMetric(
            string normalizedStoreCode,
            bool cacheHit,
            long elapsedMs,
            bool? isAllowed
        )
        {
            var traceId = GetExplicitScanTraceId();
            if (string.IsNullOrWhiteSpace(traceId))
            {
                return;
            }

            _logger.LogInformation(
                "[shop-scan-perf] traceId={TraceId} stage=authorization.assigned-store-scope storeCode={StoreCode} cacheHit={CacheHit} allowed={Allowed} elapsedMs={ElapsedMs}",
                traceId,
                normalizedStoreCode,
                cacheHit,
                isAllowed,
                elapsedMs
            );
        }

        private static bool IsScanAuthorizationCheckType(string checkType)
        {
            return string.Equals(checkType, ScanOrderFlowCheckType, StringComparison.Ordinal)
                || string.Equals(checkType, CartFlowCheckType, StringComparison.Ordinal);
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

            var sw = Stopwatch.StartNew();
            var storesResult = await _userService.GetUserStoresAsync(userGuid);
            sw.Stop();
            var traceId = GetExplicitScanTraceId();
            if (!string.IsNullOrWhiteSpace(traceId))
            {
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=authorization.user-stores-query elapsedMs={ElapsedMs} success={Success} storeCount={StoreCount}",
                    traceId,
                    sw.ElapsedMilliseconds,
                    storesResult.Success,
                    storesResult.Data?.Count ?? 0
                );
            }
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
            if (!IsStoreScopedUser() || await HasGlobalWarehouseOrderScopeAsync())
            {
                // 仓库订货管理权限用户不按店长分店范围收窄同步条件。
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
            if (!IsStoreScopedUser() || await HasGlobalWarehouseOrderScopeAsync())
            {
                // 增量 HQ 同步同样以仓库订货管理权限作为全局订货范围。
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
            if (await HasGlobalWarehouseOrderScopeAsync())
            {
                // 订单 GUID 类写接口同样按仓库订货管理权限放行全局订货范围。
                return null;
            }

            return await _storeScopeService.CanAccessOrderAsync(orderGuid) ? null : Forbid();
        }

        private async Task<IActionResult?> RequireOrderReadScopeAsync(string orderGuid)
        {
            var forbidden = await RequireOrderScopeAsync(orderGuid);
            if (forbidden == null)
            {
                return null;
            }

            var storeCode = await _dbContext.Db.Queryable<WareHouseOrder>()
                .Where(item => item.OrderGUID == orderGuid && !item.IsDeleted)
                .Select(item => item.StoreCode)
                .FirstAsync();

            // 移动端订单列表按已分配分店授权；详情查看保持同一读权限语义。
            return await RequireAssignedStoreScopeAsync(storeCode);
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

        private bool IsScanCartMutationRoute()
        {
            // 同一个 action 绑定普通和扫码路由；只让 scan-* 路径走轻量响应，保持普通 API 兼容。
            return Request.Path.Value?.Contains("/cart/scan-", StringComparison.OrdinalIgnoreCase)
                == true;
        }

        /// <summary>
        /// 获取商品列表 (支持货号搜索和分类筛选)
        /// </summary>
        [HttpPost("products")]
        public async Task<IActionResult> GetProducts([FromBody] StoreOrderFilterDto filter)
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden =
                    await RequireAnyPermissionAsync(OrderReadPermissions)
                    ?? await RequireAssignedStoreScopeAsync(filter.StoreCode);
                permissionSw.Stop();
                if (forbidden != null)
                {
                    _logger.LogInformation(
                        "[shop-home-perf] stage=products.controller.forbidden storeCode={StoreCode} pageNumber={PageNumber} pageSize={PageSize} permissionMs={PermissionMs} totalMs={TotalMs}",
                        filter.StoreCode,
                        filter.PageNumber,
                        filter.PageSize,
                        permissionSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
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
                    && string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID)
                    && string.IsNullOrWhiteSpace(filter.SupplierCode);

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
                    _logger.LogInformation(
                        "[shop-home-perf] stage=products.controller.cache-hit storeCode={StoreCode} pageNumber={PageNumber} pageSize={PageSize} itemCount={ItemCount} total={Total} permissionMs={PermissionMs} totalMs={TotalMs}",
                        filter.StoreCode,
                        filter.PageNumber,
                        filter.PageSize,
                        cachedResult?.Items?.Count ?? 0,
                        cachedResult?.Total ?? 0,
                        permissionSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return Ok(new { success = true, data = cachedResult });
                }

                // 缓存未命中，从服务获取
                _logger.LogDebug("缓存未命中，从服务获取商品列表: {CacheKey}", cacheKey);
                var serviceSw = Stopwatch.StartNew();
                var result = await _service.GetPagedListAsync(filter);
                serviceSw.Stop();

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

                _logger.LogInformation(
                    "[shop-home-perf] stage=products.controller.done storeCode={StoreCode} pageNumber={PageNumber} pageSize={PageSize} cacheHit={CacheHit} itemCount={ItemCount} total={Total} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                    filter.StoreCode,
                    filter.PageNumber,
                    filter.PageSize,
                    false,
                    result.Items?.Count ?? 0,
                    result.Total,
                    permissionSw.ElapsedMilliseconds,
                    serviceSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[shop-home-perf] stage=products.controller.error message=GetProducts failed storeCode={StoreCode} pageNumber={PageNumber} pageSize={PageSize} totalMs={TotalMs}",
                    filter.StoreCode,
                    filter.PageNumber,
                    filter.PageSize,
                    totalSw.ElapsedMilliseconds
                );
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
        /// 扫码查询并加购：单命中时一次请求完成加购，0/多命中只返回候选。
        /// </summary>
        [HttpPost("cart/scan-lookup-add")]
        public async Task<IActionResult> ScanLookupAndAddToCart(
            [FromBody] StoreOrderScanLookupAddRequestDto request
        )
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden = await RequireCartWritePermissionAsync(
                    request.StoreCode,
                    ScanOrderFlowCheckType
                );
                permissionSw.Stop();
                if (forbidden != null)
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=scan.lookup-add.controller.forbidden storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} permissionMs={PermissionMs} totalMs={TotalMs}",
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
                var result = await _service.ScanLookupAndAddToCartMutationAsync(request);
                serviceSw.Stop();
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup-add.controller.done storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} success={Success} itemCount={ItemCount} added={Added} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(request.Barcode),
                    GetBarcodeLength(request.Barcode),
                    result.Success,
                    result.Data?.Items?.Count ?? 0,
                    result.Data?.Added ?? false,
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
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup-add.controller.error storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(request.Barcode),
                    GetBarcodeLength(request.Barcode),
                    totalSw.ElapsedMilliseconds
                );
                _logger.LogError(ex, "ScanLookupAndAddToCart failed");
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
        /// 获取分店当前购物车的轻量汇总
        /// </summary>
        [HttpGet("cart/{storeCode}/summary")]
        public async Task<IActionResult> GetActiveCartSummary(string storeCode)
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

                var result = await _service.GetActiveCartSummaryAsync(storeCode);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetActiveCartSummary failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 添加到购物车
        /// </summary>
        [HttpPost("cart/add")]
        [HttpPost("cart/scan-add")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartRequestDto request)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden =
                    await RequireCartWritePermissionAsync(request);
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
                if (IsScanCartMutationRoute())
                {
                    var mutationResult = await _service.AddToCartMutationAsync(request);
                    serviceSw.Stop();
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.add.controller.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} totalQuantity={TotalQuantity} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        request.Quantity,
                        mutationResult.Success,
                        mutationResult.Data?.Summary.TotalQuantity ?? 0,
                        permissionSw.ElapsedMilliseconds,
                        serviceSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    if (mutationResult.Success)
                    {
                        return Ok(new { success = true, data = mutationResult.Data });
                    }
                    return BadRequest(new { success = false, message = mutationResult.Message });
                }

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
        [HttpPost("cart/scan-update")]
        public async Task<IActionResult> UpdateCartItem([FromBody] AddToCartRequestDto request)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden =
                    await RequireCartWritePermissionAsync(request);
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
                if (IsScanCartMutationRoute())
                {
                    var mutationResult = await _service.UpdateCartItemMutationAsync(request);
                    serviceSw.Stop();
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.update.controller.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} totalQuantity={TotalQuantity} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        request.Quantity,
                        mutationResult.Success,
                        mutationResult.Data?.Summary.TotalQuantity ?? 0,
                        permissionSw.ElapsedMilliseconds,
                        serviceSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    if (mutationResult.Success)
                    {
                        return Ok(new { success = true, data = mutationResult.Data });
                    }
                    return BadRequest(new { success = false, message = mutationResult.Message });
                }

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
                    await RequireOrderManagementActionPermissionAsync(CartWritePermissions)
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
                    await RequireCartWritePermissionAsync(request.StoreCode, CartFlowCheckType);
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
                if (IsWarehouseStaffOnly())
                {
                    // 仓库员工不提交分店 FlowStatus=0 购物车；代建订单必须走 CreateOrder 生成正式单。
                    return Forbid();
                }

                var forbidden = await RequireCartWritePermissionAsync(
                    request.StoreCode,
                    CartFlowCheckType
                );
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
            var totalSw = Stopwatch.StartNew();
            try
            {
                var permissionSw = Stopwatch.StartNew();
                var forbidden =
                    await RequireAnyPermissionAsync(OrderReadPermissions)
                    ?? await RequireAssignedStoreScopeAsync(request.StoreCode);
                permissionSw.Stop();
                if (forbidden != null)
                {
                    _logger.LogInformation(
                        "[shop-home-perf] stage=dynamic-data.controller.forbidden storeCode={StoreCode} requestCount={RequestCount} permissionMs={PermissionMs} totalMs={TotalMs}",
                        request.StoreCode,
                        request.ProductCodes?.Count ?? 0,
                        permissionSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return forbidden;
                }

                var serviceSw = Stopwatch.StartNew();
                var result = await _service.GetProductsDynamicDataAsync(request);
                serviceSw.Stop();
                _logger.LogInformation(
                    "[shop-home-perf] stage=dynamic-data.controller.done storeCode={StoreCode} requestCount={RequestCount} success={Success} resultCount={ResultCount} permissionMs={PermissionMs} serviceMs={ServiceMs} totalMs={TotalMs}",
                    request.StoreCode,
                    request.ProductCodes?.Count ?? 0,
                    result.Success,
                    result.Data?.Count ?? 0,
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
                    "[shop-home-perf] stage=dynamic-data.controller.error message=GetDynamicData failed storeCode={StoreCode} requestCount={RequestCount} totalMs={TotalMs}",
                    request.StoreCode,
                    request.ProductCodes?.Count ?? 0,
                    totalSw.ElapsedMilliseconds
                );
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
                    forbidden = await RequireAssignedStoreScopeAsync(filter.StoreCode);
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
        /// 获取首次货柜进货价基准差异统计。
        /// </summary>
        [HttpPost("import-price-variance")]
        public async Task<IActionResult> GetImportPriceVariance(
            [FromBody] StoreOrderImportPriceVarianceQueryDto query
        )
        {
            try
            {
                query ??= new StoreOrderImportPriceVarianceQueryDto();

                // 首柜价差异统计是仓库管理员报表，不能用 WarehouseStaff 的只读订货权限直接查询。
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetImportPriceVarianceAsync(query);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetImportPriceVariance failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取首次货柜进货价基准差异单商品订单明细。
        /// </summary>
        [HttpPost("import-price-variance/details")]
        public async Task<IActionResult> GetImportPriceVarianceDetails(
            [FromBody] StoreOrderImportPriceVarianceDetailQueryDto query
        )
        {
            try
            {
                query ??= new StoreOrderImportPriceVarianceDetailQueryDto();

                // 明细同样会暴露跨分店基准差异，必须和统计页保持仓库管理员权限一致。
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetImportPriceVarianceDetailsAsync(query);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetImportPriceVarianceDetails failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新首次货柜价差异统计页展示的仓库当前国内价格。
        /// </summary>
        [HttpPost("import-price-variance/domestic-price")]
        public async Task<IActionResult> UpdateImportPriceVarianceDomesticPrice(
            [FromBody] StoreOrderImportPriceVarianceDomesticPriceUpdateDto request
        )
        {
            try
            {
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateImportPriceVarianceDomesticPriceAsync(
                    request ?? new StoreOrderImportPriceVarianceDomesticPriceUpdateDto()
                );
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateImportPriceVarianceDomesticPrice failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新首次货柜价差异统计页展示的仓库当前进货价格。
        /// </summary>
        [HttpPost("import-price-variance/warehouse-import-price")]
        public async Task<IActionResult> UpdateImportPriceVarianceWarehouseImportPrice(
            [FromBody] StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto request
        )
        {
            try
            {
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateImportPriceVarianceWarehouseImportPriceAsync(
                    request ?? new StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto()
                );
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateImportPriceVarianceWarehouseImportPrice failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量更新首次货柜价差异统计页展示的仓库当前进货价格。
        /// </summary>
        [HttpPost("import-price-variance/warehouse-import-price/batch")]
        public async Task<IActionResult> UpdateImportPriceVarianceWarehouseImportPriceBatch(
            [FromBody] StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto request
        )
        {
            try
            {
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateImportPriceVarianceWarehouseImportPriceBatchAsync(
                    request ?? new StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto()
                );
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateImportPriceVarianceWarehouseImportPriceBatch failed");
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
                    ?? await RequireOrderReadScopeAsync(orderGuid);
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
                    ?? await RequireOrderReadScopeAsync(orderGuid);
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
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
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
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var job = await _invoiceEmailJobService.StartJobAsync(
                    request,
                    HttpContext.RequestAborted
                );
                return Ok(new { success = true, message = job.Message, data = job });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendInvoiceEmail failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 翻译订单发票邮件弹窗中的主题和正文。
        /// </summary>
        [HttpPost("invoice/email/translate-text")]
        public async Task<IActionResult> TranslateInvoiceEmailText(
            [FromBody] StoreOrderInvoiceEmailTextTranslationRequestDto request
        )
        {
            try
            {
                var forbidden =
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _invoiceEmailTextTranslationService.TranslateAsync(
                    request,
                    HttpContext.RequestAborted
                );
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Message });
                }

                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TranslateInvoiceEmailText failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取订单发票邮件发送 job 状态。
        /// </summary>
        [HttpGet("invoice/email/jobs/{jobId}")]
        public async Task<IActionResult> GetInvoiceEmailJob(string jobId)
        {
            try
            {
                var job = await _invoiceEmailJobService.GetJobAsync(
                    jobId,
                    HttpContext.RequestAborted
                );
                if (job == null)
                {
                    return NotFound(new { success = false, message = "任务不存在" });
                }

                var forbidden =
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(job.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetInvoiceEmailJob failed");
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
                var forbidden = await RequireCreateOrderPermissionAsync(request.StoreCode);
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
                var forbidden = await RequireOrderLineMutationPermissionAsync(request.OrderGUID);
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
                var forbidden = await RequireOrderLineMutationPermissionAsync(request.OrderGUID);
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
                var forbidden = await RequireOrderLineMutationPermissionAsync(request.OrderGUID);
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
        /// 创建 Excel 粘贴覆盖订单行后台 job
        /// </summary>
        [HttpPost("line/paste-replace/jobs")]
        public async Task<IActionResult> CreatePasteReplaceOrderLinesJob(
            [FromBody] PasteReplaceOrderLinesDto request
        )
        {
            try
            {
                var forbidden = await RequireOrderLineMutationPermissionAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var job = await _pasteReplaceJobService.StartJobAsync(
                    request,
                    HttpContext.RequestAborted
                );
                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreatePasteReplaceOrderLinesJob failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取 Excel 粘贴覆盖订单行后台 job 状态
        /// </summary>
        [HttpGet("line/paste-replace/jobs/{jobId}")]
        public async Task<IActionResult> GetPasteReplaceOrderLinesJob(string jobId)
        {
            try
            {
                var job = await _pasteReplaceJobService.GetJobAsync(
                    jobId,
                    HttpContext.RequestAborted
                );
                if (job == null)
                {
                    return NotFound(new { success = false, message = "任务不存在" });
                }

                var forbidden = await RequireOrderLineMutationPermissionAsync(job.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                return Ok(new { success = true, data = job });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPasteReplaceOrderLinesJob failed");
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
                var forbidden = await RequireOrderLineMutationPermissionAsync(request.OrderGUID);
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
                var forbidden = await RequireOrderLineMutationPermissionAsync(request.OrderGUID);
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
                var forbidden = await RequireOrderLineMutationPermissionAsync(request.OrderGUID);
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
        /// 从仓库商品表刷新订单明细进口价，允许管理员/仓库管理员修正已完成订单成本。
        /// </summary>
        [HttpPost("line/refresh-import-prices")]
        public async Task<IActionResult> RefreshOrderLineImportPrices(
            [FromBody] RefreshStoreOrderImportPricesDto request
        )
        {
            try
            {
                if (!HasAnyRole(ImportPriceRefreshRoles))
                {
                    return Forbid();
                }

                var forbidden = await RequireOrderScopeAsync(request.OrderGUID);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.RefreshOrderLineImportPricesAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RefreshOrderLineImportPrices failed");
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
                var forbidden = await RequireOrderManagementActionPermissionAsync(OrderEditPermissions);
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
                var forbidden = await RequireOrderManagementActionPermissionAsync(OrderEditPermissions);
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
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
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
        /// 更新订单出库日期，可选同步完成订单。
        /// </summary>
        [HttpPost("outbound-date")]
        public async Task<IActionResult> UpdateOrderOutboundDate(
            [FromBody] UpdateOrderOutboundDateDto request
        )
        {
            try
            {
                var forbidden =
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
                    ?? await RequireOrderScopeAsync(request.OrderGuid);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.UpdateOrderOutboundDateAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderOutboundDate failed");
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
        /// 获取订单中未能匹配本地分店的分店标识聚合。
        /// </summary>
        [HttpGet("unmatched-store-groups")]
        public async Task<IActionResult> GetUnmatchedStoreGroups()
        {
            try
            {
                var forbidden = await RequireAnyPermissionAsync(OrderReadPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                var result = await _service.GetUnmatchedStoreOrderGroupsAsync();
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUnmatchedStoreGroups failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量将订单旧分店 GUID/标识修复为本地分店编码。
        /// </summary>
        [HttpPost("batch-map-store-code")]
        public async Task<IActionResult> BatchMapStoreCode(
            [FromBody] BatchMapStoreOrderStoreCodeDto request
        )
        {
            try
            {
                var forbidden = await RequireOrderManagementActionPermissionAsync(OrderEditPermissions);
                if (forbidden != null)
                {
                    return forbidden;
                }

                request ??= new BatchMapStoreOrderStoreCodeDto();
                var targetStoreCodes = request.Mappings
                    .Select(item => item.TargetStoreCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var targetStoreCode in targetStoreCodes)
                {
                    var storeForbidden = await RequireStoreScopeAsync(targetStoreCode);
                    if (storeForbidden != null)
                    {
                        return storeForbidden;
                    }
                }

                var result = await _service.BatchMapStoreOrderStoreCodeAsync(request);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchMapStoreCode failed");
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
                    await RequireOrderManagementActionPermissionAsync(OrderDeletePermissions)
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
                    await RequireOrderManagementActionPermissionAsync(OrderCreatePermissions)
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
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
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
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
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
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
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

                if (IsStoreScopedUser() && !await HasGlobalWarehouseOrderScopeAsync())
                {
                    // 仓库订货管理权限创建的全局同步任务，读取状态时也不能再按店长分店范围拦截。
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
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
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
                var forbidden = await RequireOrderManagementActionPermissionAsync(WarehouseOrderSyncPermissions);
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

                if (IsStoreScopedUser() && !await HasGlobalWarehouseOrderScopeAsync())
                {
                    // 增量 HQ 同步 job 状态读取跟随仓库订货管理权限，全量 job 仍由上面的真实 Admin 分支控制。
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
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
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
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
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
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
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
                    await RequireOrderManagementActionPermissionAsync(OrderEditPermissions)
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
