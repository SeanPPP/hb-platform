using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreOrderReactService : IStoreOrderReactService
    {
        private const int HomePageWarmUpCommandTimeoutSeconds = 30;
        private const int OrderListAggregateChunkSize = 500;
        private const int ImportPriceVarianceWarehouseImportPriceBatchLimit = 500;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> CartMutationLocks = new();
        private static readonly AsyncLocal<HashSet<string>?> CartMutationLockScope = new();
        private readonly ISqlSugarClient _db;
        private readonly ILogger<StoreOrderReactService> _logger;
        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;
        private readonly IOrderNumberGenerator _orderNumberGenerator;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly IInvoiceEmailService _invoiceEmailService;
        private Func<ISqlSugarClient> _createHqConnection;

        private sealed class StoreOrderDynamicHistoryRow
        {
            public string? ProductCode { get; set; }
            public DateTime? OrderDate { get; set; }
            public decimal? Quantity { get; set; }
            public decimal? AllocQuantity { get; set; }
        }

        private sealed class UnmatchedStoreOrderGroupRow
        {
            public string SourceStoreCode { get; set; } = string.Empty;
            public int OrderCount { get; set; }
            public DateTime? LatestOrderDate { get; set; }
        }

        private sealed class StoreOrderDomesticSupplierCandidate
        {
            public string? ProductCode { get; set; }
            public string? HBProductNo { get; set; }
            public string? Barcode { get; set; }
            public string? SupplierCode { get; set; }
            public string? SupplierName { get; set; }
        }

        private sealed class ProductSearchFilter
        {
            public string? UnifiedKeyword { get; set; }
            public string? ItemOrBarcodeKeyword { get; set; }
            public string? ProductNameKeyword { get; set; }
            public string Mode { get; set; } = "none";
        }

        private sealed class StoreOrderProductPageKey
        {
            public string ProductCode { get; set; } = string.Empty;
        }

        private sealed class StoreOrderDetailLocationSortRow
        {
            public string ProductCode { get; set; } = string.Empty;
            public string? LocationSortCode { get; set; }
        }

        private sealed class StoreOrderCartMutationSummaryRow
        {
            public long CartRevision { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal TotalImportAmount { get; set; }
            public decimal TotalQuantity { get; set; }
            public int TotalSku { get; set; }
        }

        private sealed class StoreOrderScanLookupCoreResult
        {
            public string Barcode { get; init; } = string.Empty;
            public string? MatchType { get; init; }
            public List<StoreOrderProductDto> Items { get; init; } = new();
            public int RawCount { get; init; }
            public long ExactQueryMs { get; init; }
            public long BuildMs { get; init; }
        }

        private sealed class StoreOrderImportPriceVarianceSummarySqlRow
        {
            public int TotalRows { get; set; }
            public decimal OriginalImportAmountTotal { get; set; }
            public decimal BaselineImportAmountTotal { get; set; }
            public decimal VarianceAmountTotal { get; set; }
        }

        private sealed class StoreOrderImportPriceVarianceSqlBuildResult
        {
            public string SummarySql { get; init; } = string.Empty;
            public string PagedSql { get; init; } = string.Empty;
            public string SupplierSummarySql { get; init; } = string.Empty;
            public List<SugarParameter> Parameters { get; init; } = new();
        }

        private string GetScanTraceId()
        {
            // 同一次扫码的前后端日志使用同一个 traceId，方便按链路聚合耗时。
            return _httpContextAccessor.HttpContext?.Request.Headers["X-Scan-Trace-Id"].FirstOrDefault()
                ?? _httpContextAccessor.HttpContext?.TraceIdentifier
                ?? "no-trace";
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

        private static string GetCartMutationLockKey(string? storeCode, string? cartOwnerUserGuid)
        {
            var normalized = storeCode?.Trim();
            var storeKey = string.IsNullOrWhiteSpace(normalized)
                ? "unknown"
                : normalized.ToUpperInvariant();
            var ownerKey = string.IsNullOrWhiteSpace(cartOwnerUserGuid)
                ? "store"
                : cartOwnerUserGuid.Trim().ToUpperInvariant();

            return $"{storeKey}:{ownerKey}";
        }

        private static bool ShouldRollbackCartMutationResult<T>(T result)
        {
            // ponytail: 只识别本服务的购物车 ApiResponse；失败响应不提交前面可能创建的空订单。
            return result switch
            {
                ApiResponse<StoreOrderCartDto?> response => !response.Success,
                ApiResponse<StoreOrderCartMutationResultDto?> response => !response.Success,
                ApiResponse<bool> response => !response.Success,
                _ => false,
            };
        }

        private async Task<T> RunCartMutationLockedAsync<T>(
            string? storeCode,
            Func<Task<T>> action
        )
        {
            var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
            var lockKey = GetCartMutationLockKey(storeCode, cartOwnerUserGuid);
            var heldLocks = CartMutationLockScope.Value;
            if (heldLocks?.Contains(lockKey) == true)
            {
                return await action();
            }

            var gate = CartMutationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

            // ponytail: 进程内同店锁，跨实例时用数据库行锁/唯一约束升级。
            await gate.WaitAsync();
            var previousLocks = CartMutationLockScope.Value;
            var nextLocks = previousLocks == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(previousLocks, StringComparer.OrdinalIgnoreCase);
            nextLocks.Add(lockKey);
            CartMutationLockScope.Value = nextLocks;
            try
            {
                var transactionStarted = false;
                try
                {
                    await _db.Ado.BeginTranAsync();
                    transactionStarted = true;
                    // ponytail: 同店同 owner 购物车写入统一进数据库应用锁；跨实例也按购物车归属串行。
                    await AcquireCartMutationDatabaseLockAsync(_db, storeCode, cartOwnerUserGuid);
                    var result = await action();
                    if (ShouldRollbackCartMutationResult(result))
                    {
                        await _db.Ado.RollbackTranAsync();
                        transactionStarted = false;
                        return result;
                    }

                    await _db.Ado.CommitTranAsync();
                    transactionStarted = false;
                    return result;
                }
                catch
                {
                    if (transactionStarted)
                    {
                        await _db.Ado.RollbackTranAsync();
                    }

                    throw;
                }
            }
            finally
            {
                CartMutationLockScope.Value = previousLocks;
                gate.Release();
            }
        }

        private static async Task AcquireCartMutationDatabaseLockAsync(
            ISqlSugarClient db,
            string? storeCode,
            string? cartOwnerUserGuid
        )
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return;
            }

            var lockResult = await db.Ado.SqlQuerySingleAsync<int>(
                """
                DECLARE @Result INT;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 2000;
                SELECT @Result;
                """,
                new SugarParameter(
                    "@Resource",
                    $"StoreOrderCart:{GetCartMutationLockKey(storeCode, cartOwnerUserGuid)}"
                )
            );

            if (lockResult < 0)
            {
                throw new InvalidOperationException("购物车正在更新，请稍后重试");
            }
        }

        public StoreOrderReactService(
            SqlSugarContext context,
            ILogger<StoreOrderReactService> logger,
            Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor,
            IOrderNumberGenerator orderNumberGenerator,
            IConfiguration configuration,
            IMapper mapper,
            IInvoiceEmailService invoiceEmailService
        )
        {
            _db = context.Db;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _orderNumberGenerator = orderNumberGenerator;
            _configuration = configuration;
            _mapper = mapper;
            _invoiceEmailService = invoiceEmailService;
            _createHqConnection = () => HqSqlSugarContext.CreateConcurrentConnection(_configuration);
        }

        private static decimal? CalculateVolume(decimal? unitVolume, decimal quantity)
        {
            return unitVolume.HasValue ? unitVolume.Value * quantity : null;
        }

        private bool HasRole(string role)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
            {
                return false;
            }

            return user.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && claim.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
            );
        }

        private bool HasAnyRole(params string[] roles)
        {
            return roles.Any(HasRole);
        }

        private bool IsWarehouseStaffOnly()
        {
            return HasAnyRole("WarehouseStaff", "仓库员工")
                && !HasAnyRole("Admin", "管理员")
                && !HasAnyRole("WarehouseManager", "仓库经理");
        }

        private string ResolveCurrentUserGuid()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst("userId")?.Value
                ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst("userGuid")?.Value
                ?? user?.FindFirst("userGUID")?.Value
                ?? user?.FindFirst("UserGuid")?.Value
                ?? user?.FindFirst("sub")?.Value
                ?? string.Empty;
        }

        private string? ResolveActiveCartOwnerUserGuid()
        {
            if (!IsWarehouseStaffOnly())
            {
                return null;
            }

            var userGuid = ResolveCurrentUserGuid().Trim();
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                throw new InvalidOperationException("无法识别当前仓库员工");
            }

            return userGuid;
        }

        private static ISugarQueryable<WareHouseOrder> ApplyActiveCartScope(
            ISugarQueryable<WareHouseOrder> query,
            string? cartOwnerUserGuid
        )
        {
            if (string.IsNullOrWhiteSpace(cartOwnerUserGuid))
            {
                // 关键逻辑：NULL/空 owner 是原分店共享购物车，避免仓库员工专用车混入门店购物车。
                return query.Where(o => SqlFunc.IsNullOrEmpty(o.CartOwnerUserGuid));
            }

            return query.Where(o => o.CartOwnerUserGuid == cartOwnerUserGuid);
        }

        private bool HasElevatedOrderAccess()
        {
            return HasRole("Admin")
                || HasRole("Manager")
                || HasRole("WarehouseManager")
                || HasRole("WarehouseStaff");
        }

        private async Task<List<string>?> GetAccessibleStoreCodesAsync()
        {
            if (HasElevatedOrderAccess())
            {
                return null;
            }

            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var userGuid = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                var username = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(username))
                {
                    return new List<string>();
                }

                userGuid = await _db.Queryable<User>()
                    .Where(u => u.Username == username)
                    .Select(u => u.UserGUID)
                    .FirstAsync();
            }

            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return new List<string>();
            }

            return await _db.Queryable<UserStore>()
                .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                .Where((us, s) => us.UserGUID == userGuid)
                .Select((us, s) => s.StoreCode)
                .ToListAsync();
        }

        public async Task<PagedListReactDto<StoreOrderProductDto>> GetPagedListAsync(
            StoreOrderFilterDto filter
        )
        {
            var totalSw = Stopwatch.StartNew();
            var normalizedGrades = (filter.Grade ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (filter.ExcludeExistingWarehouseProducts)
            {
                return await GetProductMasterRowsNotInWarehouseAsync(filter, normalizedGrades);
            }

            if (!string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID))
            {
                return await GetWarehouseProductRowsForOrderPickerAsync(filter, normalizedGrades);
            }

            if (IsDefaultHomePageProductFilter(filter, normalizedGrades))
            {
                return await GetDefaultHomePageProductPageAsync(filter, normalizedGrades);
            }

            var includeInactiveForQuickAdd = ShouldIncludeInactiveWarehouseProductsForQuickAdd(
                filter
            );
            var q = CreateDefaultWarehouseProductQuery(_db, includeInactiveForQuickAdd);
            var searchFilter = CreateProductSearchFilter(filter);
            var categoryFilterCount = 0;

            if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
            {
                var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
                categoryFilterCount = categoryIds.Count;
                _logger.LogInformation(
                    "Category Filter: Found {Count} categories (including self) for root {CategoryGUID}",
                    categoryIds.Count,
                    filter.CategoryGUID
                );
                q = q.Where(
                    (p, wp, wc, ls) =>
                        p.WarehouseCategoryGUID != null
                        && categoryIds.Contains(p.WarehouseCategoryGUID)
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.LocalSupplierCode))
            {
                var supplierCode = filter.LocalSupplierCode.Trim();
                q = q.Where((p, wp, wc, ls) => p.LocalSupplierCode == supplierCode);
            }

            if (!string.IsNullOrWhiteSpace(filter.SupplierCode))
            {
                var supplierCode = filter.SupplierCode.Trim();
                q = q.Where(
                    (p, wp, wc, ls) =>
                        SqlFunc.Subqueryable<DomesticProduct>()
                            .Where(dp =>
                                dp.ProductCode == p.ProductCode
                                && dp.SupplierCode == supplierCode
                                && !dp.IsDeleted
                            )
                    .Any()
                );
            }

            q = ApplyWarehouseProductSearch(q, searchFilter);

            if (normalizedGrades.Count > 0)
            {
                q = q.Where(
                    (p, wp, wc, ls) =>
                        SqlFunc.Subqueryable<ProductGrade>()
                            .Where(pg =>
                                pg.ProductCode == p.ProductCode
                                && !pg.IsDeleted
                                && normalizedGrades.Contains(pg.Grade)
                            )
                            .Any()
                );
            }

            q = ApplyWarehouseProductColumnFilters(q, filter.ColumnFilters);
            q = ApplyWarehouseProductSort(q, filter);

            var countSw = Stopwatch.StartNew();
            var total = await q.Clone().CountAsync();
            countSw.Stop();

            var listSw = Stopwatch.StartNew();
            var items = await QueryWarehouseProductItemsByPagedProductCodesAsync(q, filter);
            listSw.Stop();

            var gradeSw = Stopwatch.StartNew();
            await PopulateGradesAsync(_db, items, normalizedGrades);
            gradeSw.Stop();

            totalSw.Stop();
            _logger.LogInformation(
                "[shop-home-perf] stage=products.service.done pageNumber={PageNumber} pageSize={PageSize} category={CategoryGUID} categoryCount={CategoryCount} searchMode={SearchMode} keywordLength={KeywordLength} gradeCount={GradeCount} total={Total} itemCount={ItemCount} countMs={CountMs} listMs={ListMs} gradeMs={GradeMs} totalMs={TotalMs}",
                filter.PageNumber,
                filter.PageSize,
                filter.CategoryGUID,
                categoryFilterCount,
                searchFilter.Mode,
                filter.ItemNumber?.Length ?? 0,
                normalizedGrades.Count,
                total,
                items.Count,
                countSw.ElapsedMilliseconds,
                listSw.ElapsedMilliseconds,
                gradeSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds
            );

            return new PagedListReactDto<StoreOrderProductDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        /// <summary>
        /// 首页缓存预热只需要首屏商品，走独立短超时连接，避免重查询长期占住后台线程。
        /// </summary>
        public async Task<PagedListReactDto<StoreOrderProductDto>> GetHomePageWarmUpPageAsync(
            int pageSize,
            CancellationToken cancellationToken = default
        )
        {
            if (pageSize <= 0)
            {
                throw new ValidationException("首页预热页大小必须大于 0");
            }

            // 预热使用独立查询连接，并把命令超时压到 30 秒左右，避免后台预热拖住调用方。
            using var warmUpDb = CreateHomePageWarmUpQueryConnection();
            var originalTimeout = warmUpDb.Ado.CommandTimeOut;

            try
            {
                warmUpDb.Ado.CommandTimeOut = originalTimeout > 0
                    ? Math.Min(originalTimeout, HomePageWarmUpCommandTimeoutSeconds)
                    : HomePageWarmUpCommandTimeoutSeconds;

                var items = await QueryDefaultHomePageProductItemsAsync(
                    warmUpDb,
                    pageNumber: 1,
                    pageSize,
                    cancellationToken
                );

                cancellationToken.ThrowIfCancellationRequested();
                await PopulateGradesAsync(
                    warmUpDb,
                    items,
                    new List<string>(capacity: 0),
                    cancellationToken
                );

                return new PagedListReactDto<StoreOrderProductDto>
                {
                    Items = items,
                    // 预热缓存只保证首屏可用，避免为估算总数再追加一次重 Count 查询。
                    Total = items.Count,
                    PageNumber = 1,
                    PageSize = pageSize,
                };
            }
            finally
            {
                warmUpDb.Ado.CommandTimeOut = originalTimeout;
            }
        }

        /// <summary>
        /// 首页正常缓存预热需要准确 Total，使用独立连接和命令超时，避免后台预热占住请求连接。
        /// </summary>
        public async Task<PagedListReactDto<StoreOrderProductDto>> GetHomePageCachePageAsync(
            int pageSize,
            CancellationToken cancellationToken = default
        )
        {
            if (pageSize <= 0)
            {
                throw new ValidationException("首页缓存页大小必须大于 0");
            }

            using var homePageDb = CreateHomePageWarmUpQueryConnection();
            var originalTimeout = homePageDb.Ado.CommandTimeOut;

            try
            {
                homePageDb.Ado.CommandTimeOut = originalTimeout > 0
                    ? Math.Min(originalTimeout, HomePageWarmUpCommandTimeoutSeconds)
                    : HomePageWarmUpCommandTimeoutSeconds;

                var q = CreateDefaultWarehouseProductBaseQuery(homePageDb);
                var total = await q.CountAsync();
                cancellationToken.ThrowIfCancellationRequested();

                var items = await QueryDefaultHomePageProductItemsAsync(
                    homePageDb,
                    pageNumber: 1,
                    pageSize,
                    cancellationToken
                );

                cancellationToken.ThrowIfCancellationRequested();
                await PopulateGradesAsync(
                    homePageDb,
                    items,
                    new List<string>(capacity: 0),
                    cancellationToken
                );

                return new PagedListReactDto<StoreOrderProductDto>
                {
                    Items = items,
                    Total = total,
                    PageNumber = 1,
                    PageSize = pageSize,
                };
            }
            finally
            {
                homePageDb.Ado.CommandTimeOut = originalTimeout;
            }
        }

        private async Task<PagedListReactDto<StoreOrderProductDto>>
            GetProductMasterRowsNotInWarehouseAsync(
                StoreOrderFilterDto filter,
                List<string> normalizedGrades
            )
        {
            // 选择商品弹窗需要基于 Product 主档做候选集，再显式排除仍有有效仓库记录的商品。
            var q = _db.Queryable<Product>()
                .LeftJoin<WarehouseCategory>((p, wc) => p.WarehouseCategoryGUID == wc.CategoryGUID)
                .LeftJoin<HBLocalSupplier>(
                    (p, wc, ls) => p.LocalSupplierCode == ls.LocalSupplierCode && !ls.IsDeleted
                )
                .Where(
                    (p, wc, ls) =>
                        p.IsActive
                        && !p.IsDeleted
                        && p.ProductCode != null
                        && !SqlFunc.Subqueryable<WarehouseProduct>()
                            .Where(wp => wp.ProductCode == p.ProductCode && !wp.IsDeleted)
                            .Any()
                );

            if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
            {
                var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
                q = q.Where(
                    (p, wc, ls) =>
                        p.WarehouseCategoryGUID != null
                        && categoryIds.Contains(p.WarehouseCategoryGUID)
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.LocalSupplierCode))
            {
                var supplierCode = filter.LocalSupplierCode.Trim();
                q = q.Where((p, wc, ls) => p.LocalSupplierCode == supplierCode);
            }

            if (!string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID))
            {
                var orderGuid = filter.ExcludeOrderGUID.Trim();
                q = q.Where(
                    (p, wc, ls) =>
                        !SqlFunc.Subqueryable<WareHouseOrderDetails>()
                            .Where(d =>
                                d.OrderGUID == orderGuid
                                && d.ProductCode == p.ProductCode
                                && !d.IsDeleted
                            )
                            .Any()
                );
            }

            q = ApplyProductMasterSearch(q, CreateProductSearchFilter(filter));

            if (normalizedGrades.Count > 0)
            {
                q = q.Where(
                    (p, wc, ls) =>
                        SqlFunc.Subqueryable<ProductGrade>()
                            .Where(pg =>
                                pg.ProductCode == p.ProductCode
                                && !pg.IsDeleted
                                && normalizedGrades.Contains(pg.Grade)
                            )
                            .Any()
                );
            }

            q = ApplyProductMasterColumnFilters(q, filter.ColumnFilters);
            q = ApplyProductMasterSort(q, filter);

            var total = await q.CountAsync();
            var items = await q.Select(
                    (p, wc, ls) =>
                        new StoreOrderProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            CategoryName = wc.CategoryName,
                            WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                            LocalSupplierCode = p.LocalSupplierCode,
                            LocalSupplierName = ls.Name,
                            OEMPrice = 0,
                            MinOrderQuantity = 1,
                            StockQuantity = 0,
                            PackQty = p.MiddlePackageQuantity,
                            ImportPrice = p.PurchasePrice ?? 0,
                        }
                )
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            await FillProductGradesAsync(items, normalizedGrades);

            return new PagedListReactDto<StoreOrderProductDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        private async Task<PagedListReactDto<StoreOrderProductDto>>
            GetWarehouseProductRowsForOrderPickerAsync(
                StoreOrderFilterDto filter,
                List<string> normalizedGrades
            )
        {
            var orderGuid = filter.ExcludeOrderGUID!.Trim();
            // 后台“选择商品”弹窗需要看到已入仓但下架的商品；只排除删除记录和当前订单已有明细。
            var q = _db.Queryable<Product>()
                .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                .LeftJoin<WarehouseCategory>(
                    (p, wp, wc) => p.WarehouseCategoryGUID == wc.CategoryGUID
                )
                .Where(
                    (p, wp, wc) =>
                        !p.IsDeleted
                        && !wp.IsDeleted
                        && p.ProductCode != null
                        && !SqlFunc.Subqueryable<WareHouseOrderDetails>()
                            .Where(d =>
                                d.OrderGUID == orderGuid
                                && d.ProductCode == p.ProductCode
                                && !d.IsDeleted
                            )
                            .Any()
                );

            if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
            {
                var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
                q = q.Where(
                    (p, wp, wc) =>
                        p.WarehouseCategoryGUID != null
                        && categoryIds.Contains(p.WarehouseCategoryGUID)
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.SupplierCode))
            {
                var supplierCode = filter.SupplierCode.Trim();
                q = q.Where(
                    (p, wp, wc) =>
                        SqlFunc.Subqueryable<DomesticProduct>()
                            .Where(dp =>
                                dp.SupplierCode == supplierCode
                                && !dp.IsDeleted
                                && SqlFunc.Subqueryable<ChinaSupplier>()
                                    .Where(cs =>
                                        cs.SupplierCode == dp.SupplierCode && !cs.IsDeleted
                                    )
                                    .Any()
                                && (
                                    dp.ProductCode == p.ProductCode
                                    || (
                                        dp.HBProductNo != null
                                        && p.ItemNumber != null
                                        && dp.HBProductNo == p.ItemNumber
                                    )
                                    || (
                                        dp.Barcode != null
                                        && p.Barcode != null
                                        && dp.Barcode == p.Barcode
                                    )
                                )
                            )
                            .Any()
                );
            }

            q = ApplyOrderPickerProductSearch(q, CreateProductSearchFilter(filter));

            if (normalizedGrades.Count > 0)
            {
                q = q.Where(
                    (p, wp, wc) =>
                        SqlFunc.Subqueryable<ProductGrade>()
                            .Where(pg =>
                                pg.ProductCode == p.ProductCode
                                && !pg.IsDeleted
                                && normalizedGrades.Contains(pg.Grade)
                            )
                            .Any()
                );
            }

            q = ApplyWarehouseProductColumnFilters(q, filter.ColumnFilters);
            q = ApplyWarehouseProductSort(q, filter);

            var total = await q.CountAsync();
            var items = await q.Select(
                    (p, wp, wc) =>
                        new StoreOrderProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            CategoryName = wc.CategoryName,
                            WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                            LocalSupplierCode = p.LocalSupplierCode,
                            OEMPrice = wp.OEMPrice,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            StockQuantity = wp.StockQuantity ?? 0,
                            PackQty = p.MiddlePackageQuantity,
                            ImportPrice = wp.ImportPrice,
                        }
                )
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            await PopulateDomesticSuppliersForOrderPickerAsync(items);
            await PopulateGradesAsync(_db, items, normalizedGrades);

            return new PagedListReactDto<StoreOrderProductDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        private async Task PopulateDomesticSuppliersForOrderPickerAsync(
            List<StoreOrderProductDto> items
        )
        {
            if (items.Count == 0)
            {
                return;
            }

            var productCodes = items
                .Select(item => NormalizeMatchKey(item.ProductCode))
                .Where(key => key != null)
                .Select(key => key!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var itemNumbers = items
                .Select(item => NormalizeMatchKey(item.ItemNumber))
                .Where(key => key != null)
                .Select(key => key!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var barcodes = items
                .Select(item => NormalizeMatchKey(item.Barcode))
                .Where(key => key != null)
                .Select(key => key!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (productCodes.Count == 0 && itemNumbers.Count == 0 && barcodes.Count == 0)
            {
                return;
            }

            // 不要把 C# 布尔开关放进 SqlSugar 表达式；SQL Server 会生成裸布尔条件并报 4145。
            var supplierMatchExpression = Expressionable.Create<DomesticProduct, ChinaSupplier>();
            if (productCodes.Count > 0)
            {
                supplierMatchExpression = supplierMatchExpression.Or(
                    (dp, cs) => dp.ProductCode != null && productCodes.Contains(dp.ProductCode)
                );
            }

            if (itemNumbers.Count > 0)
            {
                supplierMatchExpression = supplierMatchExpression.Or(
                    (dp, cs) => dp.HBProductNo != null && itemNumbers.Contains(dp.HBProductNo)
                );
            }

            if (barcodes.Count > 0)
            {
                supplierMatchExpression = supplierMatchExpression.Or(
                    (dp, cs) => dp.Barcode != null && barcodes.Contains(dp.Barcode)
                );
            }

            var candidates = await _db.Queryable<DomesticProduct>()
                .InnerJoin<ChinaSupplier>(
                    (dp, cs) => dp.SupplierCode == cs.SupplierCode && !cs.IsDeleted
                )
                .Where((dp, cs) => !dp.IsDeleted)
                .Where(supplierMatchExpression.ToExpression())
                .Select(
                    (dp, cs) =>
                        new StoreOrderDomesticSupplierCandidate
                        {
                            ProductCode = dp.ProductCode,
                            HBProductNo = dp.HBProductNo,
                            Barcode = dp.Barcode,
                            SupplierCode = dp.SupplierCode,
                            SupplierName = cs.SupplierName,
                        }
                )
                .ToListAsync();

            var orderedCandidates = candidates
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.SupplierCode)
                    || !string.IsNullOrWhiteSpace(candidate.SupplierName)
                )
                .OrderBy(candidate => candidate.SupplierCode ?? string.Empty)
                .ThenBy(candidate => candidate.ProductCode ?? string.Empty)
                .ToList();

            foreach (var item in items)
            {
                // 真实 HBweb 数据里国内商品编码和仓库商品编码经常不一致，需按货号/条码兜底补齐供应商。
                var candidate = FindDomesticSupplierCandidate(item, orderedCandidates);
                if (candidate == null)
                {
                    continue;
                }

                item.DomesticSupplierCode = candidate.SupplierCode;
                item.DomesticSupplierName = candidate.SupplierName;
            }
        }

        private static StoreOrderDomesticSupplierCandidate? FindDomesticSupplierCandidate(
            StoreOrderProductDto item,
            List<StoreOrderDomesticSupplierCandidate> candidates
        )
        {
            return candidates.FirstOrDefault(candidate =>
                    MatchNonEmpty(candidate.ProductCode, item.ProductCode)
                )
                ?? candidates.FirstOrDefault(candidate =>
                    MatchNonEmpty(candidate.HBProductNo, item.ItemNumber)
                )
                ?? candidates.FirstOrDefault(candidate => MatchNonEmpty(candidate.Barcode, item.Barcode));
        }

        private static string? NormalizeMatchKey(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static bool MatchNonEmpty(string? left, string? right)
        {
            var normalizedLeft = NormalizeMatchKey(left);
            var normalizedRight = NormalizeMatchKey(right);
            return normalizedLeft != null
                && normalizedRight != null
                && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier>
            CreateDefaultWarehouseProductQuery(
                ISqlSugarClient db,
                bool includeInactiveWarehouseProducts = false
            )
        {
            return CreateDefaultWarehouseProductBaseQuery(db, includeInactiveWarehouseProducts)
                .LeftJoin<WarehouseCategory>(
                    (p, wp, wc) => p.WarehouseCategoryGUID == wc.CategoryGUID
                )
                .LeftJoin<HBLocalSupplier>(
                    (p, wp, wc, ls) => p.LocalSupplierCode == ls.LocalSupplierCode && !ls.IsDeleted
                );
        }

        private ISugarQueryable<Product, WarehouseProduct> CreateDefaultWarehouseProductBaseQuery(
            ISqlSugarClient db,
            bool includeInactiveWarehouseProducts = false
        )
        {
            var query = db.Queryable<Product>()
                .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                .Where((p, wp) => !p.IsDeleted && !wp.IsDeleted);

            if (!includeInactiveWarehouseProducts)
            {
                // 默认列表仍只展示上架商品；快速加入会显式放开上下架限制。
                query = query.Where((p, wp) => p.IsActive && wp.IsActive);
            }

            return query;
        }

        private async Task<PagedListReactDto<StoreOrderProductDto>> GetDefaultHomePageProductPageAsync(
            StoreOrderFilterDto filter,
            List<string> normalizedGrades
        )
        {
            var totalSw = Stopwatch.StartNew();
            var countSw = Stopwatch.StartNew();
            var q = CreateDefaultWarehouseProductBaseQuery(_db);
            var total = await q.CountAsync();
            countSw.Stop();

            var listSw = Stopwatch.StartNew();
            var items = await QueryDefaultHomePageProductItemsAsync(
                _db,
                filter.PageNumber,
                filter.PageSize
            );
            listSw.Stop();

            var gradeSw = Stopwatch.StartNew();
            await PopulateGradesAsync(_db, items, normalizedGrades);
            gradeSw.Stop();

            totalSw.Stop();
            _logger.LogInformation(
                "[shop-home-perf] stage=products.service.done pageNumber={PageNumber} pageSize={PageSize} category={CategoryGUID} keywordLength={KeywordLength} gradeCount={GradeCount} total={Total} itemCount={ItemCount} countMs={CountMs} listMs={ListMs} gradeMs={GradeMs} totalMs={TotalMs}",
                filter.PageNumber,
                filter.PageSize,
                filter.CategoryGUID,
                filter.ItemNumber?.Length ?? 0,
                normalizedGrades.Count,
                total,
                items.Count,
                countSw.ElapsedMilliseconds,
                listSw.ElapsedMilliseconds,
                gradeSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds
            );

            return new PagedListReactDto<StoreOrderProductDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        private async Task<List<StoreOrderProductDto>> QueryDefaultHomePageProductItemsAsync(
            ISqlSugarClient db,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default
        )
        {
            var normalizedPageNumber = Math.Max(pageNumber, 1);
            var normalizedPageSize = Math.Max(pageSize, 1);
            var pageKeys = await CreateDefaultWarehouseProductBaseQuery(db)
                .OrderBy((p, wp) => p.ItemNumber, OrderByType.Asc)
                .Select(
                    (p, wp) =>
                        new
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                        }
                )
                .Skip((normalizedPageNumber - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync();

            cancellationToken.ThrowIfCancellationRequested();

            var productCodes = pageKeys
                .Select(item => item.ProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (productCodes.Count == 0)
            {
                return new List<StoreOrderProductDto>();
            }

            var orderMap = pageKeys
                .Select((item, index) => new { item.ProductCode, Index = index })
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Index,
                    StringComparer.OrdinalIgnoreCase
                );

            // 首页默认列表先分页出 ProductCode，再只回查首屏展示字段，避免 join 后投影整个商品池。
            var items = await CreateDefaultWarehouseProductQuery(db)
                .Where((p, wp, wc, ls) => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                .Select(
                    (p, wp, wc, ls) =>
                        new StoreOrderProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            CategoryName = wc.CategoryName,
                            WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                            LocalSupplierCode = p.LocalSupplierCode,
                            LocalSupplierName = ls.Name,
                            OEMPrice = wp.OEMPrice,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            StockQuantity = wp.StockQuantity ?? 0,
                            PackQty = p.MiddlePackageQuantity,
                            ImportPrice = wp.ImportPrice,
                        }
                )
                .ToListAsync();

            cancellationToken.ThrowIfCancellationRequested();

            return items
                .OrderBy(item =>
                    orderMap.TryGetValue(item.ProductCode, out var order) ? order : int.MaxValue
                )
                .ToList();
        }

        private async Task<List<StoreOrderProductDto>> QueryWarehouseProductItemsByPagedProductCodesAsync(
            ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier> query,
            StoreOrderFilterDto filter
        )
        {
            var normalizedPageNumber = Math.Max(filter.PageNumber, 1);
            var normalizedPageSize = Math.Max(filter.PageSize, 1);
            var pageKeys = await query
                .Clone()
                .Select(
                    (p, wp, wc, ls) =>
                        new StoreOrderProductPageKey
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                        }
                )
                .Skip((normalizedPageNumber - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync();

            var productCodes = pageKeys
                .Select(item => item.ProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (productCodes.Count == 0)
            {
                return new List<StoreOrderProductDto>();
            }

            var orderMap = pageKeys
                .Select((item, index) => new { item.ProductCode, Index = index })
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Index,
                    StringComparer.OrdinalIgnoreCase
                );

            // 先分页 ProductCode，再回查首屏字段，避免分类/关键词路径投影整个商品池。
            var items = await query
                .Clone()
                .Where((p, wp, wc, ls) => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                .Select(
                    (p, wp, wc, ls) =>
                        new StoreOrderProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            CategoryName = wc.CategoryName,
                            WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                            LocalSupplierCode = p.LocalSupplierCode,
                            LocalSupplierName = ls.Name,
                            OEMPrice = wp.OEMPrice,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            StockQuantity = wp.StockQuantity ?? 0,
                            PackQty = p.MiddlePackageQuantity,
                            ImportPrice = wp.ImportPrice,
                        }
                )
                .ToListAsync();

            return items
                .OrderBy(item =>
                    orderMap.TryGetValue(item.ProductCode, out var order) ? order : int.MaxValue
                )
                .ToList();
        }

        private static bool IsDefaultHomePageProductFilter(
            StoreOrderFilterDto filter,
            List<string> normalizedGrades
        )
        {
            return string.IsNullOrWhiteSpace(filter.CategoryGUID)
                && string.IsNullOrWhiteSpace(filter.LocalSupplierCode)
                && string.IsNullOrWhiteSpace(filter.SupplierCode)
                && string.IsNullOrWhiteSpace(filter.ItemNumber)
                && string.IsNullOrWhiteSpace(filter.ProductName)
                && string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID)
                && !filter.IncludeInactiveWarehouseProducts
                && (
                    string.IsNullOrWhiteSpace(filter.SortBy)
                    || filter.SortBy.Equals("default", StringComparison.OrdinalIgnoreCase)
                )
                && !filter.SortDescending
                && filter.ColumnFilters == null
                && normalizedGrades.Count == 0;
        }

        private static string? NormalizeColumnFilterText(string? value)
        {
            var trimmed = value?.Trim().ToLower();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static bool NumberRangeAllowsValue(int? min, int? max, int value)
        {
            return (!min.HasValue || value >= min.Value) && (!max.HasValue || value <= max.Value);
        }

        private static ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier>
            ApplyWarehouseProductColumnFilters(
                ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier> query,
                StoreOrderProductColumnFiltersDto? filters
            )
        {
            if (filters == null)
            {
                return query;
            }

            var itemNumber = NormalizeColumnFilterText(filters.ItemNumber);
            if (itemNumber != null)
            {
                query = query.Where(
                    (p, wp, wc, ls) =>
                        p.ItemNumber != null && p.ItemNumber.ToLower().Contains(itemNumber)
                );
            }

            var productName = NormalizeColumnFilterText(filters.ProductName);
            if (productName != null)
            {
                query = query.Where(
                    (p, wp, wc, ls) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(productName)
                );
            }

            var barcode = NormalizeColumnFilterText(filters.Barcode);
            if (barcode != null)
            {
                query = query.Where(
                    (p, wp, wc, ls) => p.Barcode != null && p.Barcode.ToLower().Contains(barcode)
                );
            }

            var supplierKeyword = NormalizeColumnFilterText(filters.SupplierKeyword);
            if (supplierKeyword != null)
            {
                // 国内供应商是后置补齐字段，列过滤必须按国内商品表的多键匹配逻辑执行。
                // 与后置显示保持一致：只有能关联到有效 ChinaSupplier 的国内商品才参与供应商筛选。
                query = query.Where(
                    (p, wp, wc, ls) =>
                        SqlFunc.Subqueryable<DomesticProduct>()
                            .Where(dp =>
                                !dp.IsDeleted
                                && SqlFunc.Subqueryable<ChinaSupplier>()
                                    .Where(cs =>
                                        cs.SupplierCode == dp.SupplierCode
                                        && !cs.IsDeleted
                                        && cs.Status == 1
                                        && (
                                            (
                                                cs.SupplierCode != null
                                                && cs.SupplierCode.ToLower().Contains(supplierKeyword)
                                            )
                                            || (
                                                cs.SupplierName != null
                                                && cs.SupplierName.ToLower().Contains(supplierKeyword)
                                            )
                                            || (
                                                cs.ShopNumber != null
                                                && cs.ShopNumber.ToLower().Contains(supplierKeyword)
                                            )
                                        )
                                    )
                                    .Any()
                                && (
                                    dp.ProductCode == p.ProductCode
                                    || (
                                        dp.HBProductNo != null
                                        && p.ItemNumber != null
                                        && dp.HBProductNo == p.ItemNumber
                                    )
                                    || (
                                        dp.Barcode != null
                                        && p.Barcode != null
                                        && dp.Barcode == p.Barcode
                                    )
                                )
                            )
                            .Any()
                );
            }

            if (filters.StockQuantityMin.HasValue)
            {
                query = query.Where(
                    (p, wp, wc, ls) => (wp.StockQuantity ?? 0) >= filters.StockQuantityMin.Value
                );
            }

            if (filters.StockQuantityMax.HasValue)
            {
                query = query.Where(
                    (p, wp, wc, ls) => (wp.StockQuantity ?? 0) <= filters.StockQuantityMax.Value
                );
            }

            if (filters.MinOrderQuantityMin.HasValue)
            {
                query = query.Where(
                    (p, wp, wc, ls) =>
                        (wp.MinOrderQuantity ?? 1) >= filters.MinOrderQuantityMin.Value
                );
            }

            if (filters.MinOrderQuantityMax.HasValue)
            {
                query = query.Where(
                    (p, wp, wc, ls) =>
                        (wp.MinOrderQuantity ?? 1) <= filters.MinOrderQuantityMax.Value
                );
            }

            if (filters.ImportPriceMin.HasValue)
            {
                query = query.Where(
                    (p, wp, wc, ls) =>
                        wp.ImportPrice.HasValue && wp.ImportPrice.Value >= filters.ImportPriceMin.Value
                );
            }

            if (filters.ImportPriceMax.HasValue)
            {
                query = query.Where(
                    (p, wp, wc, ls) =>
                        wp.ImportPrice.HasValue && wp.ImportPrice.Value <= filters.ImportPriceMax.Value
                );
            }

            return query;
        }

        private static ISugarQueryable<Product, WarehouseProduct, WarehouseCategory>
            ApplyWarehouseProductColumnFilters(
                ISugarQueryable<Product, WarehouseProduct, WarehouseCategory> query,
                StoreOrderProductColumnFiltersDto? filters
            )
        {
            if (filters == null)
            {
                return query;
            }

            var itemNumber = NormalizeColumnFilterText(filters.ItemNumber);
            if (itemNumber != null)
            {
                query = query.Where(
                    (p, wp, wc) =>
                        p.ItemNumber != null && p.ItemNumber.ToLower().Contains(itemNumber)
                );
            }

            var productName = NormalizeColumnFilterText(filters.ProductName);
            if (productName != null)
            {
                query = query.Where(
                    (p, wp, wc) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(productName)
                );
            }

            var barcode = NormalizeColumnFilterText(filters.Barcode);
            if (barcode != null)
            {
                query = query.Where(
                    (p, wp, wc) => p.Barcode != null && p.Barcode.ToLower().Contains(barcode)
                );
            }

            var supplierKeyword = NormalizeColumnFilterText(filters.SupplierKeyword);
            if (supplierKeyword != null)
            {
                // 订单选择弹窗供应商列来自国内商品匹配，过滤也要走同一套多键兜底。
                // 与后置显示保持一致：只有能关联到有效 ChinaSupplier 的国内商品才参与供应商筛选。
                query = query.Where(
                    (p, wp, wc) =>
                        SqlFunc.Subqueryable<DomesticProduct>()
                            .Where(dp =>
                                !dp.IsDeleted
                                && SqlFunc.Subqueryable<ChinaSupplier>()
                                    .Where(cs =>
                                        cs.SupplierCode == dp.SupplierCode
                                        && !cs.IsDeleted
                                        && cs.Status == 1
                                        && (
                                            (
                                                cs.SupplierCode != null
                                                && cs.SupplierCode.ToLower().Contains(supplierKeyword)
                                            )
                                            || (
                                                cs.SupplierName != null
                                                && cs.SupplierName.ToLower().Contains(supplierKeyword)
                                            )
                                            || (
                                                cs.ShopNumber != null
                                                && cs.ShopNumber.ToLower().Contains(supplierKeyword)
                                            )
                                        )
                                    )
                                    .Any()
                                && (
                                    dp.ProductCode == p.ProductCode
                                    || (
                                        dp.HBProductNo != null
                                        && p.ItemNumber != null
                                        && dp.HBProductNo == p.ItemNumber
                                    )
                                    || (
                                        dp.Barcode != null
                                        && p.Barcode != null
                                        && dp.Barcode == p.Barcode
                                    )
                                )
                            )
                            .Any()
                );
            }

            if (filters.StockQuantityMin.HasValue)
            {
                query = query.Where(
                    (p, wp, wc) => (wp.StockQuantity ?? 0) >= filters.StockQuantityMin.Value
                );
            }

            if (filters.StockQuantityMax.HasValue)
            {
                query = query.Where(
                    (p, wp, wc) => (wp.StockQuantity ?? 0) <= filters.StockQuantityMax.Value
                );
            }

            if (filters.MinOrderQuantityMin.HasValue)
            {
                query = query.Where(
                    (p, wp, wc) => (wp.MinOrderQuantity ?? 1) >= filters.MinOrderQuantityMin.Value
                );
            }

            if (filters.MinOrderQuantityMax.HasValue)
            {
                query = query.Where(
                    (p, wp, wc) => (wp.MinOrderQuantity ?? 1) <= filters.MinOrderQuantityMax.Value
                );
            }

            if (filters.ImportPriceMin.HasValue)
            {
                query = query.Where(
                    (p, wp, wc) =>
                        wp.ImportPrice.HasValue && wp.ImportPrice.Value >= filters.ImportPriceMin.Value
                );
            }

            if (filters.ImportPriceMax.HasValue)
            {
                query = query.Where(
                    (p, wp, wc) =>
                        wp.ImportPrice.HasValue && wp.ImportPrice.Value <= filters.ImportPriceMax.Value
                );
            }

            return query;
        }

        private static ISugarQueryable<Product, WarehouseCategory, HBLocalSupplier>
            ApplyProductMasterColumnFilters(
                ISugarQueryable<Product, WarehouseCategory, HBLocalSupplier> query,
                StoreOrderProductColumnFiltersDto? filters
            )
        {
            if (filters == null)
            {
                return query;
            }

            var itemNumber = NormalizeColumnFilterText(filters.ItemNumber);
            if (itemNumber != null)
            {
                query = query.Where(
                    (p, wc, ls) =>
                        p.ItemNumber != null && p.ItemNumber.ToLower().Contains(itemNumber)
                );
            }

            var productName = NormalizeColumnFilterText(filters.ProductName);
            if (productName != null)
            {
                query = query.Where(
                    (p, wc, ls) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(productName)
                );
            }

            var barcode = NormalizeColumnFilterText(filters.Barcode);
            if (barcode != null)
            {
                query = query.Where(
                    (p, wc, ls) => p.Barcode != null && p.Barcode.ToLower().Contains(barcode)
                );
            }

            var supplierKeyword = NormalizeColumnFilterText(filters.SupplierKeyword);
            if (supplierKeyword != null)
            {
                // 商品主档分支没有库存行，供应商列同样依赖有效国内供应商的后置匹配。
                query = query.Where(
                    (p, wc, ls) =>
                        SqlFunc.Subqueryable<DomesticProduct>()
                            .Where(dp =>
                                !dp.IsDeleted
                                && SqlFunc.Subqueryable<ChinaSupplier>()
                                    .Where(cs =>
                                        cs.SupplierCode == dp.SupplierCode
                                        && !cs.IsDeleted
                                        && cs.Status == 1
                                        && (
                                            (
                                                cs.SupplierCode != null
                                                && cs.SupplierCode.ToLower().Contains(supplierKeyword)
                                            )
                                            || (
                                                cs.SupplierName != null
                                                && cs.SupplierName.ToLower().Contains(supplierKeyword)
                                            )
                                            || (
                                                cs.ShopNumber != null
                                                && cs.ShopNumber.ToLower().Contains(supplierKeyword)
                                            )
                                        )
                                    )
                                    .Any()
                                && (
                                    dp.ProductCode == p.ProductCode
                                    || (
                                        dp.HBProductNo != null
                                        && p.ItemNumber != null
                                        && dp.HBProductNo == p.ItemNumber
                                    )
                                    || (
                                        dp.Barcode != null
                                        && p.Barcode != null
                                        && dp.Barcode == p.Barcode
                                    )
                                )
                            )
                            .Any()
                );
            }

            if (!NumberRangeAllowsValue(filters.StockQuantityMin, filters.StockQuantityMax, 0))
            {
                query = query.Where((p, wc, ls) => false);
            }

            if (!NumberRangeAllowsValue(filters.MinOrderQuantityMin, filters.MinOrderQuantityMax, 1))
            {
                query = query.Where((p, wc, ls) => false);
            }

            if (filters.ImportPriceMin.HasValue)
            {
                query = query.Where(
                    (p, wc, ls) =>
                        p.PurchasePrice.HasValue
                        && p.PurchasePrice.Value >= filters.ImportPriceMin.Value
                );
            }

            if (filters.ImportPriceMax.HasValue)
            {
                query = query.Where(
                    (p, wc, ls) =>
                        p.PurchasePrice.HasValue
                        && p.PurchasePrice.Value <= filters.ImportPriceMax.Value
                );
            }

            return query;
        }

        private static ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier>
            ApplyWarehouseProductSort(
                ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier> query,
                StoreOrderFilterDto filter
            )
        {
            var sortBy = (filter.SortBy ?? "default").Trim().ToLower();
            var orderType = filter.SortDescending ? OrderByType.Desc : OrderByType.Asc;

            return sortBy switch
            {
                "priceasc" => query.OrderBy((p, wp, wc, ls) => wp.OEMPrice, OrderByType.Asc)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "pricedesc" => query.OrderBy((p, wp, wc, ls) => wp.OEMPrice, OrderByType.Desc)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "name" => query.OrderBy((p, wp, wc, ls) => p.ProductName, OrderByType.Asc)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "productname" => query.OrderBy((p, wp, wc, ls) => p.ProductName, orderType)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "barcode" => query.OrderBy((p, wp, wc, ls) => p.Barcode, orderType)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "stockquantity" => query.OrderBy((p, wp, wc, ls) => wp.StockQuantity ?? 0, orderType)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "minorderquantity" => query
                    .OrderBy((p, wp, wc, ls) => wp.MinOrderQuantity ?? 1, orderType)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "importprice" => query.OrderBy((p, wp, wc, ls) => wp.ImportPrice ?? 0, orderType)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                "itemnumber" => query.OrderBy((p, wp, wc, ls) => p.ItemNumber, orderType)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
                _ => query.OrderBy((p, wp, wc, ls) => p.ItemNumber, OrderByType.Asc)
                    .OrderBy((p, wp, wc, ls) => p.ProductCode, OrderByType.Asc),
            };
        }

        private static ISugarQueryable<Product, WarehouseProduct, WarehouseCategory>
            ApplyWarehouseProductSort(
                ISugarQueryable<Product, WarehouseProduct, WarehouseCategory> query,
                StoreOrderFilterDto filter
            )
        {
            var sortBy = (filter.SortBy ?? "default").Trim().ToLower();
            var orderType = filter.SortDescending ? OrderByType.Desc : OrderByType.Asc;

            return sortBy switch
            {
                "priceasc" => query.OrderBy((p, wp, wc) => wp.OEMPrice, OrderByType.Asc)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "pricedesc" => query.OrderBy((p, wp, wc) => wp.OEMPrice, OrderByType.Desc)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "name" => query.OrderBy((p, wp, wc) => p.ProductName, OrderByType.Asc)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "productname" => query.OrderBy((p, wp, wc) => p.ProductName, orderType)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "barcode" => query.OrderBy((p, wp, wc) => p.Barcode, orderType)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "stockquantity" => query.OrderBy((p, wp, wc) => wp.StockQuantity ?? 0, orderType)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "minorderquantity" => query.OrderBy((p, wp, wc) => wp.MinOrderQuantity ?? 1, orderType)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "importprice" => query.OrderBy((p, wp, wc) => wp.ImportPrice ?? 0, orderType)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                "itemnumber" => query.OrderBy((p, wp, wc) => p.ItemNumber, orderType)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
                _ => query.OrderBy((p, wp, wc) => p.ItemNumber, OrderByType.Asc)
                    .OrderBy((p, wp, wc) => p.ProductCode, OrderByType.Asc),
            };
        }

        private static ISugarQueryable<Product, WarehouseCategory, HBLocalSupplier>
            ApplyProductMasterSort(
                ISugarQueryable<Product, WarehouseCategory, HBLocalSupplier> query,
                StoreOrderFilterDto filter
            )
        {
            var sortBy = (filter.SortBy ?? "default").Trim().ToLower();
            var orderType = filter.SortDescending ? OrderByType.Desc : OrderByType.Asc;

            return sortBy switch
            {
                "priceasc" => query.OrderBy((p, wc, ls) => p.PurchasePrice, OrderByType.Asc)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
                "pricedesc" => query.OrderBy((p, wc, ls) => p.PurchasePrice, OrderByType.Desc)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
                "name" => query.OrderBy((p, wc, ls) => p.ProductName, OrderByType.Asc)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
                "productname" => query.OrderBy((p, wc, ls) => p.ProductName, orderType)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
                "barcode" => query.OrderBy((p, wc, ls) => p.Barcode, orderType)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
                "importprice" => query.OrderBy((p, wc, ls) => p.PurchasePrice ?? 0, orderType)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
                "itemnumber" => query.OrderBy((p, wc, ls) => p.ItemNumber, orderType)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
                _ => query.OrderBy((p, wc, ls) => p.ItemNumber, OrderByType.Asc)
                    .OrderBy((p, wc, ls) => p.ProductCode, OrderByType.Asc),
            };
        }

        private static ProductSearchFilter CreateProductSearchFilter(StoreOrderFilterDto filter)
        {
            if (TryGetUnifiedProductSearchKeyword(filter, out var unifiedKeyword))
            {
                return new ProductSearchFilter
                {
                    UnifiedKeyword = unifiedKeyword,
                    Mode = "unified",
                };
            }

            var itemOrBarcodeKeyword = NormalizeProductSearchKeyword(filter.ItemNumber);
            var productNameKeyword = NormalizeProductSearchKeyword(filter.ProductName);

            return new ProductSearchFilter
            {
                ItemOrBarcodeKeyword = itemOrBarcodeKeyword,
                ProductNameKeyword = productNameKeyword,
                Mode = itemOrBarcodeKeyword != null && productNameKeyword != null
                    ? "split"
                    : itemOrBarcodeKeyword != null
                        ? "item-or-barcode"
                        : productNameKeyword != null
                            ? "product-name"
                            : "none",
            };
        }

        private static string? NormalizeProductSearchKeyword(string? value)
        {
            var keyword = value?.Trim().ToLower();
            return string.IsNullOrWhiteSpace(keyword) ? null : keyword;
        }

        private static ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier>
            ApplyWarehouseProductSearch(
                ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier> query,
                ProductSearchFilter search
            )
        {
            if (!string.IsNullOrWhiteSpace(search.UnifiedKeyword))
            {
                var keyword = search.UnifiedKeyword;
                return query.Where(
                    (p, wp, wc, ls) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                        || (p.ProductName != null && p.ProductName.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(search.ItemOrBarcodeKeyword))
            {
                var keyword = search.ItemOrBarcodeKeyword;
                // Home 单搜索框使用这条路径，语义固定为货号或条码。
                query = query.Where(
                    (p, wp, wc, ls) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(search.ProductNameKeyword))
            {
                var keyword = search.ProductNameKeyword;
                query = query.Where(
                    (p, wp, wc, ls) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(keyword)
                );
            }

            return query;
        }

        private static ISugarQueryable<Product, WarehouseCategory, HBLocalSupplier>
            ApplyProductMasterSearch(
                ISugarQueryable<Product, WarehouseCategory, HBLocalSupplier> query,
                ProductSearchFilter search
            )
        {
            if (!string.IsNullOrWhiteSpace(search.UnifiedKeyword))
            {
                var keyword = search.UnifiedKeyword;
                return query.Where(
                    (p, wc, ls) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                        || (p.ProductName != null && p.ProductName.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(search.ItemOrBarcodeKeyword))
            {
                var keyword = search.ItemOrBarcodeKeyword;
                query = query.Where(
                    (p, wc, ls) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(search.ProductNameKeyword))
            {
                var keyword = search.ProductNameKeyword;
                query = query.Where(
                    (p, wc, ls) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(keyword)
                );
            }

            return query;
        }

        private static ISugarQueryable<Product, WarehouseProduct, WarehouseCategory>
            ApplyOrderPickerProductSearch(
                ISugarQueryable<Product, WarehouseProduct, WarehouseCategory> query,
                ProductSearchFilter search
            )
        {
            if (!string.IsNullOrWhiteSpace(search.UnifiedKeyword))
            {
                var keyword = search.UnifiedKeyword;
                return query.Where(
                    (p, wp, wc) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                        || (p.ProductName != null && p.ProductName.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(search.ItemOrBarcodeKeyword))
            {
                var keyword = search.ItemOrBarcodeKeyword;
                query = query.Where(
                    (p, wp, wc) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(search.ProductNameKeyword))
            {
                var keyword = search.ProductNameKeyword;
                query = query.Where(
                    (p, wp, wc) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(keyword)
                );
            }

            return query;
        }

        private static bool ShouldIncludeInactiveWarehouseProductsForQuickAdd(
            StoreOrderFilterDto filter
        )
        {
            // 下架商品只对后台订货“货号快速加入”放开，避免共享 DTO 被其它商品列表查询误用。
            return filter.IncludeInactiveWarehouseProducts
                && !string.IsNullOrWhiteSpace(filter.ItemNumber)
                && string.IsNullOrWhiteSpace(filter.ProductName)
                && string.IsNullOrWhiteSpace(filter.CategoryGUID)
                && string.IsNullOrWhiteSpace(filter.LocalSupplierCode)
                && string.IsNullOrWhiteSpace(filter.SupplierCode)
                && string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID)
                && !filter.ExcludeExistingWarehouseProducts;
        }

        private static bool TryGetUnifiedProductSearchKeyword(
            StoreOrderFilterDto filter,
            out string keyword
        )
        {
            keyword = string.Empty;
            var itemNumberKeyword = filter.ItemNumber?.Trim();
            var productNameKeyword = filter.ProductName?.Trim();

            if (
                string.IsNullOrWhiteSpace(itemNumberKeyword)
                || string.IsNullOrWhiteSpace(productNameKeyword)
                || !string.Equals(
                    itemNumberKeyword,
                    productNameKeyword,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            // 前端商品弹窗的单搜索框会把同一关键词同时传给货号和商品名称，后端需保持 OR 语义。
            keyword = itemNumberKeyword.ToLower();
            return true;
        }

        private ISqlSugarClient CreateHomePageWarmUpQueryConnection()
        {
            var config = _db.CurrentConnectionConfig;
            var moreSettings = config.MoreSettings;
            var concurrentDb = new SqlSugarClient(
                new ConnectionConfig
                {
                    ConnectionString = config.ConnectionString,
                    DbType = config.DbType,
                    IsAutoCloseConnection = false,
                    InitKeyType = config.InitKeyType,
                    MoreSettings = new ConnMoreSettings
                    {
                        IsAutoRemoveDataCache = moreSettings?.IsAutoRemoveDataCache ?? false,
                        IsWithNoLockQuery = moreSettings?.IsWithNoLockQuery ?? false,
                        SqlServerCodeFirstNvarchar =
                            moreSettings?.SqlServerCodeFirstNvarchar ?? false,
                        DefaultCacheDurationInSeconds = 0,
                    },
                    ConfigureExternalServices = config.ConfigureExternalServices,
                }
            );
            concurrentDb.Ado.CommandTimeOut = _db.Ado.CommandTimeOut;

            return concurrentDb;
        }

        private async Task PopulateGradesAsync(
            ISqlSugarClient db,
            List<StoreOrderProductDto> items,
            List<string> normalizedGrades,
            CancellationToken cancellationToken = default
        )
        {
            var productCodes = items
                .Select(item => item.ProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (productCodes.Count == 0)
            {
                return;
            }

            var gradeRows = await db.Queryable<ProductGrade>()
                .Where(pg => productCodes.Contains(pg.ProductCode) && !pg.IsDeleted)
                .OrderBy(pg => pg.Grade)
                .Select(pg => new { pg.ProductCode, pg.Grade })
                .ToListAsync();

            cancellationToken.ThrowIfCancellationRequested();

            var gradeMap = gradeRows
                .GroupBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        normalizedGrades.Count > 0
                            ? group.FirstOrDefault(row => normalizedGrades.Contains(row.Grade))?.Grade
                                ?? group.First().Grade
                            : group.First().Grade,
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var item in items)
            {
                if (gradeMap.TryGetValue(item.ProductCode, out var grade))
                {
                    item.Grade = grade;
                }
            }
        }

        private async Task FillProductGradesAsync(
            List<StoreOrderProductDto> items,
            List<string> normalizedGrades
        )
        {
            var productCodes = items
                .Select(item => item.ProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (productCodes.Count == 0)
            {
                return;
            }

            var gradeRows = await _db.Queryable<ProductGrade>()
                .Where(pg => productCodes.Contains(pg.ProductCode) && !pg.IsDeleted)
                .OrderBy(pg => pg.Grade)
                .Select(pg => new { pg.ProductCode, pg.Grade })
                .ToListAsync();

            var gradeMap = gradeRows
                .GroupBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        normalizedGrades.Count > 0
                            ? group.FirstOrDefault(row => normalizedGrades.Contains(row.Grade))?.Grade
                                ?? group.First().Grade
                            : group.First().Grade,
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var item in items)
            {
                if (gradeMap.TryGetValue(item.ProductCode, out var grade))
                {
                    item.Grade = grade;
                }
            }
        }

        public async Task<ApiResponse<List<StoreOrderBatchLookupItemDto>>> BatchLookupProductsAsync(
            StoreOrderBatchLookupRequestDto request
        )
        {
            try
            {
                var codes = request
                    .Codes.Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (codes.Count == 0)
                {
                    return new ApiResponse<List<StoreOrderBatchLookupItemDto>>
                    {
                        Success = true,
                        Data = new List<StoreOrderBatchLookupItemDto>(),
                    };
                }

                var products = await _db.Queryable<Product>()
                    .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                    .LeftJoin<WarehouseCategory>(
                        (p, wp, wc) => p.WarehouseCategoryGUID == wc.CategoryGUID
                    )
                    .LeftJoin<ProductGrade>(
                        (p, wp, wc, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted
                    )
                    .Where((p, wp, wc, pg) => !p.IsDeleted && !wp.IsDeleted)
                    .Where(
                        (p, wp, wc, pg) =>
                            (p.ItemNumber != null && codes.Contains(p.ItemNumber))
                            || (p.Barcode != null && codes.Contains(p.Barcode))
                            || (p.ProductCode != null && codes.Contains(p.ProductCode))
                    )
                    .Select(
                        (p, wp, wc, pg) =>
                            new StoreOrderProductDto
                            {
                                ProductCode = p.ProductCode ?? string.Empty,
                                ItemNumber = p.ItemNumber,
                                Barcode = p.Barcode,
                                ProductName = p.ProductName,
                                ProductImage = p.ProductImage,
                                CategoryName = wc.CategoryName,
                                WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                                OEMPrice = wp.OEMPrice,
                                MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                                StockQuantity = wp.StockQuantity ?? 0,
                                PackQty = p.MiddlePackageQuantity,
                                ImportPrice = wp.ImportPrice,
                                Grade = pg.Grade,
                            }
                    )
                    .ToListAsync();

                var results = codes
                    .Select(code =>
                    {
                        var match =
                            products.FirstOrDefault(p =>
                                string.Equals(
                                    p.ItemNumber,
                                    code,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            ?? products.FirstOrDefault(p =>
                                string.Equals(p.Barcode, code, StringComparison.OrdinalIgnoreCase)
                            )
                            ?? products.FirstOrDefault(p =>
                                string.Equals(
                                    p.ProductCode,
                                    code,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            );

                        return new StoreOrderBatchLookupItemDto
                        {
                            LookupCode = code,
                            Product = match,
                        };
                    })
                    .ToList();

                return new ApiResponse<List<StoreOrderBatchLookupItemDto>>
                {
                    Success = true,
                    Data = results,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchLookupProductsAsync failed");
                return new ApiResponse<List<StoreOrderBatchLookupItemDto>>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<ApiResponse<StoreOrderScanLookupResultDto>> ScanLookupProductsAsync(
            StoreOrderScanLookupRequestDto request
        )
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var barcode = request.Barcode?.Trim();
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.service.invalid storeCode={StoreCode} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        totalSw.ElapsedMilliseconds
                    );
                    return new ApiResponse<StoreOrderScanLookupResultDto>
                    {
                        Success = false,
                        Message = "Barcode is required.",
                    };
                }

                var lookupResult = await LookupScanProductsCoreAsync(barcode);

                // 保持数据库字段可走索引：扫码只做精确匹配，不再对字段执行 ToLower() 兜底。
                long fallbackQueryMs = 0;

                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.service.done storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} matchType={MatchType} rawCount={RawCount} itemCount={ItemCount} exactQueryMs={ExactQueryMs} fallbackQueryMs={FallbackQueryMs} buildMs={BuildMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(barcode),
                    GetBarcodeLength(barcode),
                    lookupResult.MatchType ?? "none",
                    lookupResult.RawCount,
                    lookupResult.Items.Count,
                    lookupResult.ExactQueryMs,
                    fallbackQueryMs,
                    lookupResult.BuildMs,
                    totalSw.ElapsedMilliseconds
                );

                return new ApiResponse<StoreOrderScanLookupResultDto>
                {
                    Success = true,
                    Data = new StoreOrderScanLookupResultDto
                    {
                        Barcode = barcode,
                        MatchType = lookupResult.MatchType,
                        Items = lookupResult.Items,
                    },
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.service.error storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(request.Barcode),
                    GetBarcodeLength(request.Barcode),
                    totalSw.ElapsedMilliseconds
                );
                _logger.LogError(ex, "ScanLookupProductsAsync failed");
                return new ApiResponse<StoreOrderScanLookupResultDto>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<ApiResponse<StoreOrderScanLookupAddResultDto>> ScanLookupAndAddToCartMutationAsync(
            StoreOrderScanLookupAddRequestDto request
        )
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var barcode = request.Barcode?.Trim();
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=scan.lookup-add.service.invalid storeCode={StoreCode} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        totalSw.ElapsedMilliseconds
                    );
                    return new ApiResponse<StoreOrderScanLookupAddResultDto>
                    {
                        Success = false,
                        Message = "Barcode is required.",
                    };
                }

                var lookupStartedAt = Stopwatch.StartNew();
                var lookupResult = await LookupScanProductsCoreAsync(barcode);
                lookupStartedAt.Stop();

                var response = new StoreOrderScanLookupAddResultDto
                {
                    Barcode = barcode,
                    MatchType = lookupResult.MatchType,
                    Items = lookupResult.Items,
                    Added = false,
                    Cart = null,
                };

                if (lookupResult.Items.Count != 1)
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=scan.lookup-add.service.done storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} matchType={MatchType} itemCount={ItemCount} added={Added} lookupMs={LookupMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        GetBarcodeTail(barcode),
                        GetBarcodeLength(barcode),
                        lookupResult.MatchType ?? "none",
                        lookupResult.Items.Count,
                        false,
                        lookupStartedAt.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return ApiResponse<StoreOrderScanLookupAddResultDto>.OK(response);
                }

                var product = lookupResult.Items[0];
                var quantity = request.Quantity.GetValueOrDefault();
                if (quantity <= 0)
                {
                    quantity = product.MinOrderQuantity > 0 ? product.MinOrderQuantity : 1;
                }

                var addStartedAt = Stopwatch.StartNew();
                var cartResult = await AddToCartMutationCoreAsync(
                    new AddToCartRequestDto
                    {
                        StoreCode = request.StoreCode,
                        ProductCode = product.ProductCode,
                        Quantity = quantity,
                        ImportPrice = product.ImportPrice,
                    },
                    product
                );
                addStartedAt.Stop();

                if (!cartResult.Success)
                {
                    return new ApiResponse<StoreOrderScanLookupAddResultDto>
                    {
                        Success = false,
                        Message = cartResult.Message,
                    };
                }

                response.Added = true;
                response.Cart = cartResult.Data;

                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup-add.service.done storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} matchType={MatchType} itemCount={ItemCount} added={Added} productCode={ProductCode} quantity={Quantity} lookupMs={LookupMs} addMs={AddMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(barcode),
                    GetBarcodeLength(barcode),
                    lookupResult.MatchType ?? "none",
                    lookupResult.Items.Count,
                    true,
                    product.ProductCode,
                    quantity,
                    lookupStartedAt.ElapsedMilliseconds,
                    addStartedAt.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );

                return ApiResponse<StoreOrderScanLookupAddResultDto>.OK(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup-add.service.error storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(request.Barcode),
                    GetBarcodeLength(request.Barcode),
                    totalSw.ElapsedMilliseconds
                );
                return new ApiResponse<StoreOrderScanLookupAddResultDto>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        private async Task<StoreOrderScanLookupCoreResult> LookupScanProductsCoreAsync(string barcode)
        {
            var lookupCodes = new[] { barcode, barcode.ToUpperInvariant(), barcode.ToLowerInvariant() }
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var useSqlServerCaseInsensitiveCollation =
                _db.CurrentConnectionConfig.DbType == DbType.SqlServer;

            var exactQuerySw = Stopwatch.StartNew();
            var allMatches = await QueryScanLookupMatchesAsync(
                barcode,
                lookupCodes,
                "barcode",
                useSqlServerCaseInsensitiveCollation
            );
            var matchType = allMatches.Count > 0 ? "barcode" : null;

            if (allMatches.Count == 0)
            {
                allMatches = await QueryScanLookupMatchesAsync(
                    barcode,
                    lookupCodes,
                    "itemNumber",
                    useSqlServerCaseInsensitiveCollation
                );
                matchType = allMatches.Count > 0 ? "fallback" : null;
            }

            if (allMatches.Count == 0)
            {
                allMatches = await QueryScanLookupMatchesAsync(
                    barcode,
                    lookupCodes,
                    "productCode",
                    useSqlServerCaseInsensitiveCollation
                );
                matchType = allMatches.Count > 0 ? "fallback" : null;
            }
            exactQuerySw.Stop();

            var buildSw = Stopwatch.StartNew();
            // ProductGrade 可能存在多行，先按商品去重，再按候选批量补等级，避免主查询被等级表放大。
            var distinctMatches = allMatches
                .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            await FillProductGradesAsync(distinctMatches, new List<string>());
            buildSw.Stop();

            return new StoreOrderScanLookupCoreResult
            {
                Barcode = barcode,
                MatchType = matchType,
                Items = distinctMatches,
                RawCount = allMatches.Count,
                ExactQueryMs = exactQuerySw.ElapsedMilliseconds,
                BuildMs = buildSw.ElapsedMilliseconds,
            };
        }

        private async Task<List<StoreOrderProductDto>> QueryScanLookupMatchesAsync(
            string barcode,
            List<string> lookupCodes,
            string matchField,
            bool useSqlServerCaseInsensitiveCollation
        )
        {
            var query = _db.Queryable<Product>()
                .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                .LeftJoin<WarehouseCategory>(
                    (p, wp, wc) => p.WarehouseCategoryGUID == wc.CategoryGUID
                )
                .Where(
                    (p, wp, wc) => p.IsActive && !p.IsDeleted && !wp.IsDeleted && wp.IsActive
                );

            query = matchField switch
            {
                "barcode" => query
                    .WhereIF(
                        useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc) =>
                            // 生产库字段本身已是 CI 排序规则，直接等值比较才能稳定走条码索引。
                            p.Barcode != null && p.Barcode == barcode
                    )
                    .WhereIF(
                        !useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc) => p.Barcode != null && lookupCodes.Contains(p.Barcode)
                    ),
                "itemNumber" => query
                    .WhereIF(
                        useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc) =>
                            p.ItemNumber != null && p.ItemNumber == barcode
                    )
                    .WhereIF(
                        !useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc) =>
                            p.ItemNumber != null && lookupCodes.Contains(p.ItemNumber)
                    ),
                "productCode" => query
                    .WhereIF(
                        useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc) =>
                            p.ProductCode != null && p.ProductCode == barcode
                    )
                    .WhereIF(
                        !useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc) =>
                            p.ProductCode != null && lookupCodes.Contains(p.ProductCode)
                    ),
                _ => throw new ArgumentOutOfRangeException(nameof(matchField), matchField, null),
            };

            return await query
                .Select(
                    (p, wp, wc) =>
                        new StoreOrderProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            CategoryName = wc.CategoryName,
                            WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                            OEMPrice = wp.OEMPrice,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            StockQuantity = wp.StockQuantity ?? 0,
                            PackQty = p.MiddlePackageQuantity,
                            ImportPrice = wp.ImportPrice,
                        }
                )
                .ToListAsync();
        }

        private List<string> GetAllSubCategoryIds(string categoryGuid)
        {
            try
            {
                var allCategories = _db.Queryable<WarehouseCategory>()
                    .Select(c => new WarehouseCategory
                    {
                        CategoryGUID = c.CategoryGUID,
                        ParentGUID = c.ParentGUID,
                    })
                    .ToList();
                var result = new List<string> { categoryGuid };
                var seen = new HashSet<string>(StringComparer.Ordinal) { categoryGuid };
                var childrenByParent = allCategories
                    .Where(c =>
                        !string.IsNullOrWhiteSpace(c.ParentGUID)
                        && !string.IsNullOrWhiteSpace(c.CategoryGUID)
                    )
                    .GroupBy(c => c.ParentGUID!, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(c => c.CategoryGUID).ToList(),
                        StringComparer.Ordinal
                    );
                GetSubCategoriesRecursive(categoryGuid, childrenByParent, seen, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get subcategories for {CategoryGuid}",
                    categoryGuid
                );
                return new List<string> { categoryGuid };
            }
        }

        private void GetSubCategoriesRecursive(
            string parentGuid,
            IReadOnlyDictionary<string, List<string>> childrenByParent,
            HashSet<string> seen,
            List<string> result
        )
        {
            if (!childrenByParent.TryGetValue(parentGuid, out var children))
            {
                return;
            }

            foreach (var childGuid in children)
            {
                if (!string.IsNullOrEmpty(childGuid) && seen.Add(childGuid))
                {
                    result.Add(childGuid);
                    GetSubCategoriesRecursive(childGuid, childrenByParent, seen, result);
                }
            }
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartAsync(string storeCode)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();

            // FlowStatus = 0 代表购物车
            var orderSw = Stopwatch.StartNew();
            var orderQuery = _db.Queryable<WareHouseOrder>()
                .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted);
            var order = await ApplyActiveCartScope(orderQuery, cartOwnerUserGuid).FirstAsync();
            orderSw.Stop();

            if (order == null)
            {
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=cart.reload.service.empty storeCode={StoreCode} orderMs={OrderMs} totalMs={TotalMs}",
                    traceId,
                    storeCode,
                    orderSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );
                return new ApiResponse<StoreOrderCartDto?> { Success = true, Data = null };
            }

            var store = await GetStoreByCodeOrGuidAsync(order.StoreCode);

            var detailsQuerySw = Stopwatch.StartNew();
            var details = await _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                .LeftJoin<WarehouseProduct>((d, p, wp) => d.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((d, p, wp, dp) => wp.ProductCode == dp.ProductCode)
                .LeftJoin<ProductGrade>((d, p, wp, dp, pg) => d.ProductCode == pg.ProductCode && !pg.IsDeleted)
                .Where(d => d.OrderGUID == order.OrderGUID && !d.IsDeleted)
                .Select(
                    (d, p, wp, dp, pg) =>
                        new StoreOrderCartItemDto
                        {
                            DetailGUID = d.DetailGUID,
                            ProductCode = d.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            Grade = pg.Grade,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            Price = d.OEMPrice ?? 0,
                            Quantity = d.Quantity ?? 0,
                            AllocQuantity = d.AllocQuantity,
                            Amount = d.OEMAmount ?? 0,
                            ImportPrice = d.ImportPrice ?? (wp.ImportPrice ?? 0),
                            ImportAmount =
                                d.ImportAmount
                                ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0)),
                            // 发货/发票金额单独返回，避免复用订货金额字段造成页面误读。
                            AllocatedImportAmount = (d.ImportPrice ?? (wp.ImportPrice ?? 0))
                                * (d.AllocQuantity ?? 0),
                            // 计算单件体积: 如果装箱数 > 0，则用箱体积 / 装箱数，否则直接用 UnitVolume
                            Volume =
                                (dp.PackingQuantity > 0)
                                    ? (dp.UnitVolume / dp.PackingQuantity)
                                    : dp.UnitVolume,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                        }
                )
                .ToListAsync();
            detailsQuerySw.Stop();

            var buildSw = Stopwatch.StartNew();
            // 计算小计体积和总计
            foreach (var item in details)
            {
                if (item.Volume.HasValue)
                {
                    item.OrderVolume = CalculateVolume(item.Volume, item.Quantity);
                    item.AllocVolume = CalculateVolume(item.Volume, item.AllocQuantity ?? 0);
                    item.TotalVolume = item.OrderVolume;
                }
            }

            var dto = new StoreOrderCartDto
            {
                OrderGUID = order.OrderGUID,
                OrderNo = order.OrderNo,
                StoreCode = order.StoreCode,
                TotalAmount = order.OEMTotalAmount ?? 0,
                TotalQuantity = (int)details.Sum(x => x.Quantity),
                TotalSKU = details
                    .Select(x => x.ProductCode)
                    .Where(productCode => !string.IsNullOrWhiteSpace(productCode))
                    .Distinct()
                    .Count(),
                TotalImportAmount = details.Sum(x => x.ImportAmount),
                TotalAllocatedImportAmount = details.Sum(x => x.AllocatedImportAmount),
                TotalVolume = details.Sum(x => x.TotalVolume ?? 0),
                TotalOrderVolume = details.Sum(x => x.OrderVolume ?? 0),
                TotalAllocVolume = details.Sum(x => x.AllocVolume ?? 0),
                Remarks = order.Remarks,
                StoreAddress = store?.Address,
                StoreContactEmail = store?.ContactEmail,
                ShippingFee = order.ShippingFee,
                OrderDate = order.OrderDate,
                TotalAllocQuantity = (int)details.Sum(x => x.AllocQuantity ?? 0),
                FlowStatus = order.FlowStatus,
                Items = details,
            };
            buildSw.Stop();

            _logger.LogInformation(
                "[shop-scan-perf] traceId={TraceId} stage=cart.reload.service.done storeCode={StoreCode} itemCount={ItemCount} totalQuantity={TotalQuantity} totalSku={TotalSku} orderMs={OrderMs} detailsQueryMs={DetailsQueryMs} buildMs={BuildMs} totalMs={TotalMs}",
                traceId,
                storeCode,
                details.Count,
                dto.TotalQuantity,
                dto.TotalSKU,
                orderSw.ElapsedMilliseconds,
                detailsQuerySw.ElapsedMilliseconds,
                buildSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds
            );

            return new ApiResponse<StoreOrderCartDto?> { Success = true, Data = dto };
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartSummaryAsync(string storeCode)
        {
            var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
            var orderQuery = _db.Queryable<WareHouseOrder>()
                .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted);
            var order = await ApplyActiveCartScope(orderQuery, cartOwnerUserGuid).FirstAsync();

            if (order == null)
            {
                return new ApiResponse<StoreOrderCartDto?> { Success = true, Data = null };
            }

            var store = await GetStoreByCodeOrGuidAsync(order.StoreCode);
            var detailRows = await _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<DomesticProduct>((d, dp) => d.ProductCode == dp.ProductCode)
                .Where(d => d.OrderGUID == order.OrderGUID && !d.IsDeleted)
                .Select(
                    (d, dp) =>
                        new
                        {
                            d.ProductCode,
                            Quantity = d.Quantity ?? 0,
                            AllocQuantity = d.AllocQuantity ?? 0,
                            ImportAmount = d.ImportAmount ?? ((d.ImportPrice ?? 0) * (d.Quantity ?? 0)),
                            AllocatedImportAmount = (d.ImportPrice ?? 0) * (d.AllocQuantity ?? 0),
                            UnitVolume = (dp.PackingQuantity > 0)
                                ? (dp.UnitVolume / dp.PackingQuantity)
                                : dp.UnitVolume,
                        }
                )
                .ToListAsync();

            var totalVolume = detailRows.Sum(row => (row.UnitVolume ?? 0) * row.Quantity);
            var totalAllocVolume = detailRows.Sum(row => (row.UnitVolume ?? 0) * row.AllocQuantity);

            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = true,
                Data = new StoreOrderCartDto
                {
                    OrderGUID = order.OrderGUID,
                    OrderNo = order.OrderNo,
                    StoreCode = order.StoreCode,
                    TotalAmount = order.OEMTotalAmount ?? 0,
                    TotalQuantity = (int)detailRows.Sum(row => row.Quantity),
                    TotalImportAmount = detailRows.Sum(row => row.ImportAmount),
                    TotalAllocatedImportAmount = detailRows.Sum(row => row.AllocatedImportAmount),
                    TotalVolume = totalVolume,
                    TotalOrderVolume = totalVolume,
                    TotalAllocVolume = totalAllocVolume,
                    Remarks = order.Remarks,
                    StoreAddress = store?.Address,
                    StoreContactEmail = store?.ContactEmail,
                    ShippingFee = order.ShippingFee,
                    OrderDate = order.OrderDate,
                    TotalAllocQuantity = (int)detailRows.Sum(row => row.AllocQuantity),
                    TotalSKU = detailRows
                        .Select(row => row.ProductCode)
                        .Where(productCode => !string.IsNullOrWhiteSpace(productCode))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    FlowStatus = order.FlowStatus,
                    // 关键逻辑：summary 故意跳过商品明细字段，保证登录/切店首屏更快。
                    Items = new List<StoreOrderCartItemDto>(),
                },
            };
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> AddToCartAsync(AddToCartRequestDto request)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                return await RunCartMutationLockedAsync(request.StoreCode, async () =>
                {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 1. 获取或创建订单 (FlowStatus = 0)
                var orderSw = Stopwatch.StartNew();
                var orderQuery = _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    );
                var order = await ApplyActiveCartScope(orderQuery, cartOwnerUserGuid).FirstAsync();

                if (order == null)
                {
                    order = new WareHouseOrder
                    {
                        OrderGUID = UuidHelper.GenerateUuid7(),
                        StoreCode = request.StoreCode,
                        CartOwnerUserGuid = cartOwnerUserGuid,
                        OrderDate = now,
                        FlowStatus = 0, // 购物车状态
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        UpdatedBy = currentUser,
                        OEMTotalAmount = 0,
                        ImportTotalAmount = 0,
                        ShippingFee = 0,
                    };
                    await _db.Insertable(order).ExecuteCommandAsync();
                }
                orderSw.Stop();

                // 2. 一次性获取商品与仓库价格，减少加购链路中的往返查询。
                var productSw = Stopwatch.StartNew();
                var productInfo = await _db.Queryable<Product>()
                    .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                    .Where((p, wp) => p.ProductCode == request.ProductCode)
                    .Select((p, wp) => new
                    {
                        wp.OEMPrice,
                        wp.ImportPrice,
                    })
                    .FirstAsync();
                productSw.Stop();

                if (productInfo == null)
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.add.service.product-missing storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} orderMs={OrderMs} productMs={ProductMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        request.Quantity,
                        orderSw.ElapsedMilliseconds,
                        productSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return new ApiResponse<StoreOrderCartDto?> { Success = false, Message = "商品不存在" };
                }

                decimal price = productInfo.OEMPrice ?? 0;
                decimal importPrice = productInfo.ImportPrice ?? 0; // 记录ImportPrice以便统计

                // 3. 检查明细是否已存在
                var detailLookupSw = Stopwatch.StartNew();
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d =>
                        d.OrderGUID == order.OrderGUID
                        && d.ProductCode == request.ProductCode
                        && !d.IsDeleted
                    )
                    .FirstAsync();
                detailLookupSw.Stop();

                var detailWriteSw = Stopwatch.StartNew();
                if (detail == null)
                {
                    // 新增明细
                    detail = new WareHouseOrderDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        OrderGUID = order.OrderGUID,
                        StoreCode = request.StoreCode,
                        ProductCode = request.ProductCode,
                        Quantity = request.Quantity,
                        OEMPrice = price,
                        OEMAmount = price * request.Quantity,
                        ImportPrice = importPrice,
                        ImportAmount = importPrice * request.Quantity,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = currentUser,
                        UpdatedBy = currentUser,
                    };
                    await _db.Insertable(detail).ExecuteCommandAsync();
                }
                else
                {
                    // 更新明细
                    detail.Quantity += request.Quantity;
                    // 如果数量 <= 0，则删除
                    if (detail.Quantity <= 0)
                    {
                        await SoftDeleteOrderDetailAsync(detail, currentUser, now);
                    }
                    else
                    {
                        detail.OEMAmount = detail.Quantity * detail.OEMPrice;
                        detail.ImportAmount = detail.Quantity * detail.ImportPrice;
                        detail.UpdatedAt = now;
                        detail.UpdatedBy = currentUser;
                        await _db.Updateable(detail).ExecuteCommandAsync();
                    }
                }
                detailWriteSw.Stop();

                // 4. 更新主表总金额
                var totalUpdateSw = Stopwatch.StartNew();
                await UpdateOrderTotalAsync(order.OrderGUID);
                totalUpdateSw.Stop();

                var cartReloadSw = Stopwatch.StartNew();
                var cartResult = await GetActiveCartAsync(request.StoreCode);
                cartReloadSw.Stop();
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=cart.add.service.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} totalQuantity={TotalQuantity} totalSku={TotalSku} orderMs={OrderMs} productMs={ProductMs} detailLookupMs={DetailLookupMs} detailWriteMs={DetailWriteMs} recalculateMs={RecalculateMs} cartReloadMs={CartReloadMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    cartResult.Success,
                    cartResult.Data?.TotalQuantity ?? 0,
                    cartResult.Data?.TotalSKU ?? 0,
                    orderSw.ElapsedMilliseconds,
                    productSw.ElapsedMilliseconds,
                    detailLookupSw.ElapsedMilliseconds,
                    detailWriteSw.ElapsedMilliseconds,
                    totalUpdateSw.ElapsedMilliseconds,
                    cartReloadSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );

                return cartResult;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[shop-scan-perf] traceId={TraceId} stage=cart.add.service.error storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    totalSw.ElapsedMilliseconds
                );
                _logger.LogError(ex, "AddToCart failed");
                return new ApiResponse<StoreOrderCartDto?> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<StoreOrderCartMutationResultDto?>> AddToCartMutationAsync(
            AddToCartRequestDto request
        )
        {
            return await AddToCartMutationCoreAsync(request);
        }

        private async Task<ApiResponse<StoreOrderCartMutationResultDto?>> AddToCartMutationCoreAsync(
            AddToCartRequestDto request,
            StoreOrderProductDto? lookupProduct = null
        )
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                return await RunCartMutationLockedAsync(request.StoreCode, async () =>
                {
                        var now = DateTime.Now;
                        var currentUser =
                            _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                        var orderSw = Stopwatch.StartNew();
                        var orderQuery = _db.Queryable<WareHouseOrder>()
                            .Where(o =>
                                o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                            );
                        var order = await ApplyActiveCartScope(orderQuery, cartOwnerUserGuid).FirstAsync();

                        if (order == null)
                        {
                            order = new WareHouseOrder
                            {
                                OrderGUID = UuidHelper.GenerateUuid7(),
                                StoreCode = request.StoreCode,
                                CartOwnerUserGuid = cartOwnerUserGuid,
                                OrderDate = now,
                                FlowStatus = 0,
                                IsDeleted = false,
                                CreatedAt = now,
                                UpdatedAt = now,
                                UpdatedBy = currentUser,
                                OEMTotalAmount = 0,
                                ImportTotalAmount = 0,
                                ShippingFee = 0,
                            };
                            await _db.Insertable(order).ExecuteCommandAsync();
                        }
                        orderSw.Stop();

                        var productSw = Stopwatch.StartNew();
                        decimal price;
                        decimal importPrice;
                        if (
                            lookupProduct != null
                            && string.Equals(
                                lookupProduct.ProductCode,
                                request.ProductCode,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            // 扫码合并接口已经查过 Product + WarehouseProduct，单命中直接复用价格。
                            price = lookupProduct.OEMPrice ?? 0;
                            importPrice = lookupProduct.ImportPrice ?? 0;
                            productSw.Stop();
                        }
                        else
                        {
                            var productInfo = await _db.Queryable<Product>()
                                .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                                .Where((p, wp) => p.ProductCode == request.ProductCode)
                                .Select((p, wp) => new
                                {
                                    wp.OEMPrice,
                                    wp.ImportPrice,
                                })
                                .FirstAsync();
                            productSw.Stop();

                            if (productInfo == null)
                            {
                                _logger.LogInformation(
                                    "[shop-scan-perf] traceId={TraceId} stage=cart.add.mutation.product-missing storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} orderMs={OrderMs} productMs={ProductMs} totalMs={TotalMs}",
                                    traceId,
                                    request.StoreCode,
                                    request.ProductCode,
                                    request.Quantity,
                                    orderSw.ElapsedMilliseconds,
                                    productSw.ElapsedMilliseconds,
                                    totalSw.ElapsedMilliseconds
                                );
                                return new ApiResponse<StoreOrderCartMutationResultDto?>
                                {
                                    Success = false,
                                    Message = "商品不存在",
                                };
                            }

                            price = productInfo.OEMPrice ?? 0;
                            importPrice = productInfo.ImportPrice ?? 0;
                        }

                        var detailLookupSw = Stopwatch.StartNew();
                        var detail = await _db.Queryable<WareHouseOrderDetails>()
                            .Where(d =>
                                d.OrderGUID == order.OrderGUID
                                && d.ProductCode == request.ProductCode
                                && !d.IsDeleted
                            )
                            .FirstAsync();
                        detailLookupSw.Stop();

                        var removed = false;
                        string? detailGuid = detail?.DetailGUID;
                        var detailWriteSw = Stopwatch.StartNew();
                        if (detail == null)
                        {
                            if (request.Quantity <= 0)
                            {
                                // 扫码轻量接口遇到无存量的非正数加购时，不创建无效明细，只返回空变更行。
                                removed = true;
                            }
                            else
                            {
                                detail = new WareHouseOrderDetails
                                {
                                    DetailGUID = UuidHelper.GenerateUuid7(),
                                    OrderGUID = order.OrderGUID,
                                    StoreCode = request.StoreCode,
                                    ProductCode = request.ProductCode,
                                    Quantity = request.Quantity,
                                    OEMPrice = price,
                                    OEMAmount = price * request.Quantity,
                                    ImportPrice = importPrice,
                                    ImportAmount = importPrice * request.Quantity,
                                    IsDeleted = false,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                    CreatedBy = currentUser,
                                    UpdatedBy = currentUser,
                                };
                                detailGuid = detail.DetailGUID;
                                await _db.Insertable(detail).ExecuteCommandAsync();
                            }
                        }
                        else
                        {
                            detail.Quantity += request.Quantity;
                            if (detail.Quantity <= 0)
                            {
                                removed = true;
                                await SoftDeleteOrderDetailAsync(detail, currentUser, now);
                            }
                            else
                            {
                                detail.OEMAmount = detail.Quantity * detail.OEMPrice;
                                detail.ImportAmount = detail.Quantity * detail.ImportPrice;
                                detail.UpdatedAt = now;
                                detail.UpdatedBy = currentUser;
                                await _db.Updateable(detail).ExecuteCommandAsync();
                            }
                        }
                        detailWriteSw.Stop();

                        var totalUpdateSw = Stopwatch.StartNew();
                        var summaryRow = await UpdateOrderTotalAsync(order.OrderGUID, order.UpdatedAt);
                        totalUpdateSw.Stop();

                        var payloadSw = Stopwatch.StartNew();
                        var result = await BuildCartMutationResultAsync(
                            order.OrderGUID,
                            request.StoreCode,
                            request.ProductCode,
                            detailGuid,
                            removed,
                            traceId,
                            summaryRow
                        );
                        payloadSw.Stop();

                        _logger.LogInformation(
                            "[shop-scan-perf] traceId={TraceId} stage=cart.add.mutation.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} removed={Removed} totalQuantity={TotalQuantity} totalSku={TotalSku} orderMs={OrderMs} productMs={ProductMs} detailLookupMs={DetailLookupMs} detailWriteMs={DetailWriteMs} recalculateMs={RecalculateMs} mutationPayloadMs={MutationPayloadMs} totalMs={TotalMs}",
                            traceId,
                            request.StoreCode,
                            request.ProductCode,
                            request.Quantity,
                            result.Success,
                            result.Data?.Removed ?? false,
                            result.Data?.Summary.TotalQuantity ?? 0,
                            result.Data?.Summary.TotalSku ?? 0,
                            orderSw.ElapsedMilliseconds,
                            productSw.ElapsedMilliseconds,
                            detailLookupSw.ElapsedMilliseconds,
                            detailWriteSw.ElapsedMilliseconds,
                            totalUpdateSw.ElapsedMilliseconds,
                            payloadSw.ElapsedMilliseconds,
                            totalSw.ElapsedMilliseconds
                        );

                        return result;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[shop-scan-perf] traceId={TraceId} stage=cart.add.mutation.error storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    totalSw.ElapsedMilliseconds
                );
                _logger.LogError(ex, "AddToCartMutationAsync failed");
                return new ApiResponse<StoreOrderCartMutationResultDto?>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> UpdateCartItemAsync(AddToCartRequestDto request)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                return await RunCartMutationLockedAsync(request.StoreCode, async () =>
                {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 1. 获取购物车
                var orderSw = Stopwatch.StartNew();
                var orderQuery = _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    );
                var order = await ApplyActiveCartScope(orderQuery, cartOwnerUserGuid).FirstAsync();

                if (order == null)
                {
                    orderSw.Stop();
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.update.service.fallback-add storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} orderMs={OrderMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        request.Quantity,
                        orderSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    // 如果购物车不存在，则尝试当作添加处理 (或返回错误，取决于业务)
                    // 这里我们选择直接调用 AddToCart
                    return await AddToCartAsync(request);
                }
                orderSw.Stop();

                // 2. 获取商品信息 (获取零售价)
                var productSw = Stopwatch.StartNew();
                var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                    .Where(wp => wp.ProductCode == request.ProductCode)
                    .FirstAsync();
                productSw.Stop();

                if (warehouseProduct == null)
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.update.service.product-missing storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} orderMs={OrderMs} productMs={ProductMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        request.Quantity,
                        orderSw.ElapsedMilliseconds,
                        productSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return new ApiResponse<StoreOrderCartDto?> { Success = false, Message = "商品不存在" };
                }

                decimal price = warehouseProduct.OEMPrice ?? 0;
                decimal importPrice = warehouseProduct.ImportPrice ?? 0;

                // 3. 检查明细
                var detailLookupSw = Stopwatch.StartNew();
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d =>
                        d.OrderGUID == order.OrderGUID
                        && d.ProductCode == request.ProductCode
                        && !d.IsDeleted
                    )
                    .FirstAsync();
                detailLookupSw.Stop();

                var detailWriteSw = Stopwatch.StartNew();
                if (detail == null)
                {
                    // 如果明细不存在，创建新的
                    detail = new WareHouseOrderDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        OrderGUID = order.OrderGUID,
                        StoreCode = request.StoreCode,
                        ProductCode = request.ProductCode,
                        Quantity = request.Quantity,
                        OEMPrice = price,
                        OEMAmount = price * request.Quantity,
                        ImportPrice = importPrice,
                        ImportAmount = importPrice * request.Quantity,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = currentUser,
                        UpdatedBy = currentUser,
                    };
                    await _db.Insertable(detail).ExecuteCommandAsync();
                }
                else
                {
                    // 更新数量
                    detail.Quantity = request.Quantity;
                    // 如果数量 <= 0，则删除
                    if (detail.Quantity <= 0)
                    {
                        await SoftDeleteOrderDetailAsync(detail, currentUser, now);
                    }
                    else
                    {
                        detail.OEMAmount = detail.Quantity * detail.OEMPrice;
                        detail.ImportAmount = detail.Quantity * detail.ImportPrice;
                        detail.UpdatedAt = now;
                        detail.UpdatedBy = currentUser;
                        await _db.Updateable(detail).ExecuteCommandAsync();
                    }
                }
                detailWriteSw.Stop();

                // 4. 更新主表总金额
                var totalUpdateSw = Stopwatch.StartNew();
                await UpdateOrderTotalAsync(order.OrderGUID);
                totalUpdateSw.Stop();

                var cartReloadSw = Stopwatch.StartNew();
                var cartResult = await GetActiveCartAsync(request.StoreCode);
                cartReloadSw.Stop();
                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=cart.update.service.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} totalQuantity={TotalQuantity} totalSku={TotalSku} orderMs={OrderMs} productMs={ProductMs} detailLookupMs={DetailLookupMs} detailWriteMs={DetailWriteMs} recalculateMs={RecalculateMs} cartReloadMs={CartReloadMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    cartResult.Success,
                    cartResult.Data?.TotalQuantity ?? 0,
                    cartResult.Data?.TotalSKU ?? 0,
                    orderSw.ElapsedMilliseconds,
                    productSw.ElapsedMilliseconds,
                    detailLookupSw.ElapsedMilliseconds,
                    detailWriteSw.ElapsedMilliseconds,
                    totalUpdateSw.ElapsedMilliseconds,
                    cartReloadSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );

                return cartResult;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[shop-scan-perf] traceId={TraceId} stage=cart.update.service.error storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    totalSw.ElapsedMilliseconds
                );
                _logger.LogError(ex, "UpdateCartItemAsync failed");
                return new ApiResponse<StoreOrderCartDto?> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<StoreOrderCartMutationResultDto?>> UpdateCartItemMutationAsync(
            AddToCartRequestDto request
        )
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                return await RunCartMutationLockedAsync(request.StoreCode, async () =>
                {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                var orderSw = Stopwatch.StartNew();
                var orderQuery = _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    );
                var order = await ApplyActiveCartScope(orderQuery, cartOwnerUserGuid).FirstAsync();
                orderSw.Stop();

                if (order == null)
                {
                    if (request.Quantity <= 0)
                    {
                        return new ApiResponse<StoreOrderCartMutationResultDto?>
                        {
                            Success = true,
                            Data = new StoreOrderCartMutationResultDto
                            {
                                ProductCode = request.ProductCode,
                                Removed = true,
                                Summary = new StoreOrderCartMutationSummaryDto
                                {
                                    StoreCode = request.StoreCode,
                                },
                            },
                        };
                    }

                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.update.mutation.fallback-add storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} orderMs={OrderMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        request.Quantity,
                        orderSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return await AddToCartMutationAsync(request);
                }

                var productSw = Stopwatch.StartNew();
                var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                    .Where(wp => wp.ProductCode == request.ProductCode)
                    .FirstAsync();
                productSw.Stop();

                if (warehouseProduct == null)
                {
                    _logger.LogInformation(
                        "[shop-scan-perf] traceId={TraceId} stage=cart.update.mutation.product-missing storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} orderMs={OrderMs} productMs={ProductMs} totalMs={TotalMs}",
                        traceId,
                        request.StoreCode,
                        request.ProductCode,
                        request.Quantity,
                        orderSw.ElapsedMilliseconds,
                        productSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds
                    );
                    return new ApiResponse<StoreOrderCartMutationResultDto?>
                    {
                        Success = false,
                        Message = "商品不存在",
                    };
                }

                var price = warehouseProduct.OEMPrice ?? 0;
                var importPrice = warehouseProduct.ImportPrice ?? 0;

                var detailLookupSw = Stopwatch.StartNew();
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d =>
                        d.OrderGUID == order.OrderGUID
                        && d.ProductCode == request.ProductCode
                        && !d.IsDeleted
                    )
                    .FirstAsync();
                detailLookupSw.Stop();

                var removed = false;
                string? detailGuid = detail?.DetailGUID;
                var detailWriteSw = Stopwatch.StartNew();
                if (detail == null)
                {
                    if (request.Quantity <= 0)
                    {
                        // 扫码更新为 0 时没有现存行，无需插入再删除，直接返回 removed。
                        removed = true;
                    }
                    else
                    {
                        detail = new WareHouseOrderDetails
                        {
                            DetailGUID = UuidHelper.GenerateUuid7(),
                            OrderGUID = order.OrderGUID,
                            StoreCode = request.StoreCode,
                            ProductCode = request.ProductCode,
                            Quantity = request.Quantity,
                            OEMPrice = price,
                            OEMAmount = price * request.Quantity,
                            ImportPrice = importPrice,
                            ImportAmount = importPrice * request.Quantity,
                            IsDeleted = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = currentUser,
                            UpdatedBy = currentUser,
                        };
                        detailGuid = detail.DetailGUID;
                        await _db.Insertable(detail).ExecuteCommandAsync();
                    }
                }
                else
                {
                    detail.Quantity = request.Quantity;
                    if (detail.Quantity <= 0)
                    {
                        removed = true;
                        await SoftDeleteOrderDetailAsync(detail, currentUser, now);
                    }
                    else
                    {
                        detail.OEMAmount = detail.Quantity * detail.OEMPrice;
                        detail.ImportAmount = detail.Quantity * detail.ImportPrice;
                        detail.UpdatedAt = now;
                        detail.UpdatedBy = currentUser;
                        await _db.Updateable(detail).ExecuteCommandAsync();
                    }
                }
                detailWriteSw.Stop();

                var totalUpdateSw = Stopwatch.StartNew();
                var summaryRow = await UpdateOrderTotalAsync(order.OrderGUID, order.UpdatedAt);
                totalUpdateSw.Stop();

                var payloadSw = Stopwatch.StartNew();
                var result = await BuildCartMutationResultAsync(
                    order.OrderGUID,
                    request.StoreCode,
                    request.ProductCode,
                    detailGuid,
                    removed,
                    traceId,
                    summaryRow
                );
                payloadSw.Stop();

                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=cart.update.mutation.done storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} success={Success} removed={Removed} totalQuantity={TotalQuantity} totalSku={TotalSku} orderMs={OrderMs} productMs={ProductMs} detailLookupMs={DetailLookupMs} detailWriteMs={DetailWriteMs} recalculateMs={RecalculateMs} mutationPayloadMs={MutationPayloadMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    result.Success,
                    result.Data?.Removed ?? false,
                    result.Data?.Summary.TotalQuantity ?? 0,
                    result.Data?.Summary.TotalSku ?? 0,
                    orderSw.ElapsedMilliseconds,
                    productSw.ElapsedMilliseconds,
                    detailLookupSw.ElapsedMilliseconds,
                    detailWriteSw.ElapsedMilliseconds,
                    totalUpdateSw.ElapsedMilliseconds,
                    payloadSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );

                return result;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[shop-scan-perf] traceId={TraceId} stage=cart.update.mutation.error storeCode={StoreCode} productCode={ProductCode} quantity={Quantity} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    request.ProductCode,
                    request.Quantity,
                    totalSw.ElapsedMilliseconds
                );
                _logger.LogError(ex, "UpdateCartItemMutationAsync failed");
                return new ApiResponse<StoreOrderCartMutationResultDto?>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        private async Task<ApiResponse<StoreOrderCartMutationResultDto?>> BuildCartMutationResultAsync(
            string orderGuid,
            string storeCode,
            string productCode,
            string? detailGuid,
            bool removed,
            string traceId,
            StoreOrderCartMutationSummaryRow? knownSummaryRow = null
        )
        {
            var summarySw = Stopwatch.StartNew();
            var summaryRow = knownSummaryRow;
            if (summaryRow == null)
            {
                summaryRow = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == orderGuid && !d.IsDeleted)
                    .Select(d => new StoreOrderCartMutationSummaryRow
                    {
                        TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0),
                        TotalSku = SqlFunc.AggregateDistinctCount(d.ProductCode),
                        TotalAmount = SqlFunc.AggregateSum(d.OEMAmount ?? 0),
                        TotalImportAmount = SqlFunc.AggregateSum(d.ImportAmount ?? 0),
                    })
                    .FirstAsync();
            }
            summarySw.Stop();

            StoreOrderCartItemDto? changedItem = null;
            var changedItemSw = Stopwatch.StartNew();
            if (!removed)
            {
                changedItem = await QueryCartMutationChangedItemAsync(orderGuid, productCode, detailGuid);
            }
            changedItemSw.Stop();

            // 轻量响应只校准摘要和当前商品行，移动端本地缓存负责合并其它行。
            var result = new StoreOrderCartMutationResultDto
            {
                ProductCode = productCode,
                Removed = removed || changedItem == null,
                Summary = new StoreOrderCartMutationSummaryDto
                {
                    OrderGUID = orderGuid,
                    StoreCode = storeCode,
                    TotalAmount = summaryRow?.TotalAmount ?? 0,
                    TotalImportAmount = summaryRow?.TotalImportAmount ?? 0,
                    TotalQuantity = (int)(summaryRow?.TotalQuantity ?? 0),
                    TotalSku = summaryRow?.TotalSku ?? 0,
                    CartRevision = summaryRow?.CartRevision ?? 0,
                },
                ChangedItem = changedItem,
            };

            _logger.LogInformation(
                "[shop-scan-perf] traceId={TraceId} stage=cart.mutation.payload.done storeCode={StoreCode} productCode={ProductCode} removed={Removed} totalQuantity={TotalQuantity} totalSku={TotalSku} summaryQueryMs={SummaryQueryMs} changedItemQueryMs={ChangedItemQueryMs}",
                traceId,
                storeCode,
                productCode,
                result.Removed,
                result.Summary.TotalQuantity,
                result.Summary.TotalSku,
                summarySw.ElapsedMilliseconds,
                changedItemSw.ElapsedMilliseconds
            );

            return new ApiResponse<StoreOrderCartMutationResultDto?>
            {
                Success = true,
                Data = result,
            };
        }

        private async Task<StoreOrderCartItemDto?> QueryCartMutationChangedItemAsync(
            string orderGuid,
            string productCode,
            string? detailGuid
        )
        {
            var query = _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                .LeftJoin<WarehouseProduct>((d, p, wp) => d.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((d, p, wp, dp) => wp.ProductCode == dp.ProductCode)
                .LeftJoin<ProductGrade>((d, p, wp, dp, pg) => d.ProductCode == pg.ProductCode && !pg.IsDeleted)
                .Where(d =>
                    d.OrderGUID == orderGuid
                    && d.ProductCode == productCode
                    && !d.IsDeleted
                );

            if (!string.IsNullOrWhiteSpace(detailGuid))
            {
                query = query.Where(d => d.DetailGUID == detailGuid);
            }

            var item = await query
                .Select(
                    (d, p, wp, dp, pg) =>
                        new StoreOrderCartItemDto
                        {
                            DetailGUID = d.DetailGUID,
                            ProductCode = d.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            Grade = pg.Grade,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            Price = d.OEMPrice ?? 0,
                            Quantity = d.Quantity ?? 0,
                            AllocQuantity = d.AllocQuantity,
                            Amount = d.OEMAmount ?? 0,
                            ImportPrice = d.ImportPrice ?? (wp.ImportPrice ?? 0),
                            ImportAmount =
                                d.ImportAmount
                                ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0)),
                            // 发货/发票金额单独返回，避免复用订货金额字段造成页面误读。
                            AllocatedImportAmount = (d.ImportPrice ?? (wp.ImportPrice ?? 0))
                                * (d.AllocQuantity ?? 0),
                            Volume =
                                (dp.PackingQuantity > 0)
                                    ? (dp.UnitVolume / dp.PackingQuantity)
                                    : dp.UnitVolume,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                        }
                )
                .FirstAsync();

            if (item?.Volume.HasValue == true)
            {
                item.OrderVolume = CalculateVolume(item.Volume, item.Quantity);
                item.AllocVolume = CalculateVolume(item.Volume, item.AllocQuantity ?? 0);
                item.TotalVolume = item.OrderVolume;
            }

            return item;
        }

        public async Task<ApiResponse<bool>> RemoveFromCartAsync(RemoveFromCartRequestDto request)
        {
            try
            {
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .InnerJoin<WareHouseOrder>((d, o) => d.OrderGUID == o.OrderGUID)
                    .Where((d, o) =>
                        d.DetailGUID == request.DetailGUID
                        && o.StoreCode == request.StoreCode
                        && d.StoreCode == request.StoreCode
                        && !d.IsDeleted
                        && o.FlowStatus == 0
                        && !o.IsDeleted
                    )
                    .WhereIF(
                        string.IsNullOrWhiteSpace(cartOwnerUserGuid),
                        (d, o) => SqlFunc.IsNullOrEmpty(o.CartOwnerUserGuid)
                    )
                    .WhereIF(
                        !string.IsNullOrWhiteSpace(cartOwnerUserGuid),
                        (d, o) => o.CartOwnerUserGuid == cartOwnerUserGuid
                    )
                    .Select((d, o) => d)
                    .FirstAsync();

                if (detail == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Cart item not found",
                    };
                }

                return await RunCartMutationLockedAsync(detail.StoreCode, async () =>
                {
                var lockedDetail = await _db.Queryable<WareHouseOrderDetails>()
                    .InnerJoin<WareHouseOrder>((d, o) => d.OrderGUID == o.OrderGUID)
                    .Where((d, o) =>
                        d.DetailGUID == request.DetailGUID
                        && o.StoreCode == request.StoreCode
                        && d.StoreCode == request.StoreCode
                        && !d.IsDeleted
                        && o.FlowStatus == 0
                        && !o.IsDeleted
                    )
                    .WhereIF(
                        string.IsNullOrWhiteSpace(cartOwnerUserGuid),
                        (d, o) => SqlFunc.IsNullOrEmpty(o.CartOwnerUserGuid)
                    )
                    .WhereIF(
                        !string.IsNullOrWhiteSpace(cartOwnerUserGuid),
                        (d, o) => o.CartOwnerUserGuid == cartOwnerUserGuid
                    )
                    .Select((d, o) => d)
                    .FirstAsync();

                if (lockedDetail == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Cart item not found",
                    };
                }

                var orderGuid = lockedDetail.OrderGUID;
                await SoftDeleteOrderDetailAsync(
                    lockedDetail,
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System",
                    DateTime.Now
                );

                // 更新主表
                if (!string.IsNullOrEmpty(orderGuid))
                {
                    await UpdateOrderTotalAsync(orderGuid);
                }

                return new ApiResponse<bool> { Success = true, Data = true };
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveFromCart failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> ClearCartAsync(string storeCode)
        {
            try
            {
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                return await RunCartMutationLockedAsync(storeCode, async () =>
                {
                var cartQuery = _db.Queryable<WareHouseOrder>()
                    .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted);
                var cart = await ApplyActiveCartScope(cartQuery, cartOwnerUserGuid).FirstAsync();

                if (cart == null)
                {
                    return new ApiResponse<StoreOrderCartDto?>
                    {
                        Success = true,
                        Data = null,
                        Message = "Cart is already empty",
                    };
                }

                await _db.Deleteable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == cart.OrderGUID)
                    .ExecuteCommandAsync();

                await _db.Deleteable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == cart.OrderGUID)
                    .ExecuteCommandAsync();

                _logger.LogInformation("Cleared cart for store: {StoreCode}", storeCode);

                return new ApiResponse<StoreOrderCartDto?>
                {
                    Success = true,
                    Data = null,
                    Message = "Cart cleared successfully",
                };
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cart for store: {StoreCode}", storeCode);
                return new ApiResponse<StoreOrderCartDto?>
                {
                    Success = false,
                    Message = "Failed to clear cart",
                };
            }
        }

        public async Task<ApiResponse<bool>> SubmitOrderAsync(SubmitStoreOrderRequestDto request)
        {
            try
            {
                var bypassPreorderGate = request.BypassPreorderGate || IsWarehouseStaffOnly();
                var storeResource = bypassPreorderGate
                    ? string.Empty
                    : await PreorderGateEvaluator.ResolveStoreLockResourceFailClosedAsync(
                        _db,
                        request.StoreCode,
                        _logger
                    );
                await using IAsyncDisposable? storeLock = bypassPreorderGate
                    ? null
                    : await PreorderMutationLock.AcquireProcessAsync(storeResource);
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                return await RunCartMutationLockedAsync(request.StoreCode, async () =>
                {
                if (!bypassPreorderGate)
                {
                    var preorderGate = await PreorderGateEvaluator.EvaluateLockedFailClosedAsync(
                        _db,
                        storeResource,
                        request.StoreCode,
                        TimeProvider.System,
                        _logger
                    );
                    if (preorderGate.IsBlocked)
                    {
                        return new ApiResponse<bool>
                        {
                            Success = false,
                            ErrorCode = "PREORDER_REQUIRED",
                            Message = "请先完成当前有效的 Preorder，再提交普通订货",
                        };
                    }
                }
                var orderQuery = _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    );
                var order = await ApplyActiveCartScope(orderQuery, cartOwnerUserGuid).FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "No active cart found",
                    };
                }

                // 检查是否有明细
                var count = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == order.OrderGUID && !d.IsDeleted)
                    .CountAsync();

                if (count == 0)
                {
                    return new ApiResponse<bool> { Success = false, Message = "Cart is empty" };
                }

                // 更新状态 0 -> 1 (审核中/已提交)
                order.FlowStatus = 1;
                order.Remarks = request.Remarks;
                order.OrderDate = DateTime.Now; // 更新下单时间
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 生成正式订单号 (ORD-YYYY-NNNN 格式，从1000开始递增)
                order.OrderNo = await _orderNumberGenerator.GetNextOrderNoAsync();

                await _db.Updateable(order).ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
                });
            }
            catch (PreorderBusinessException ex)
            {
                _logger.LogWarning(ex, "SubmitOrder Preorder gate unavailable");
                return new ApiResponse<bool>
                {
                    Success = false,
                    ErrorCode = ex.ErrorCode,
                    Message = ex.Message,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitOrder failed");
                return new ApiResponse<bool> { Success = false, Message = "订单提交失败，请稍后重试" };
            }
        }

        private static (DateTime RevisionAt, long CartRevision) ResolveNextCartRevision(
            DateTime? previousUpdatedAt,
            DateTime? nowOverride = null
        )
        {
            var now = nowOverride ?? DateTime.Now;
            var nowRevision = ToCartRevision(now);
            var previousRevision = previousUpdatedAt.HasValue ? ToCartRevision(previousUpdatedAt.Value) : 0;
            var nextRevision = Math.Max(nowRevision, previousRevision + 1);

            return (
                DateTimeOffset.FromUnixTimeMilliseconds(nextRevision).LocalDateTime,
                nextRevision
            );
        }

        private static long ToCartRevision(DateTime revisionAt)
        {
            return new DateTimeOffset(revisionAt).ToUnixTimeMilliseconds();
        }

        private static decimal CalculateOrderImportAmount(decimal? quantity, decimal? importPrice)
        {
            return (quantity ?? 0) * (importPrice ?? 0);
        }

        private async Task<StoreOrderCartMutationSummaryRow?> UpdateOrderTotalAsync(
            string orderGuid,
            DateTime? previousUpdatedAt = null
        )
        {
            // 同一次聚合同时产出主表金额和轻量购物车摘要，避免 mutation 响应再查一遍明细汇总。
            var summaryRow = await _db.Queryable<WareHouseOrderDetails>()
                .Where(d => d.OrderGUID == orderGuid && !d.IsDeleted)
                .Select(d => new StoreOrderCartMutationSummaryRow
                {
                    TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0),
                    TotalSku = SqlFunc.AggregateDistinctCount(d.ProductCode),
                    TotalAmount = SqlFunc.AggregateSum(d.OEMAmount ?? 0),
                    TotalImportAmount = SqlFunc.AggregateSum(d.ImportAmount ?? 0),
                })
                .FirstAsync();

            var totalOEM = summaryRow?.TotalAmount ?? 0;
            var totalImport = summaryRow?.TotalImportAmount ?? 0;
            // ponytail: 复用订单头 UpdatedAt 做同购物车提交序号；需要跨表审计时再加独立 revision 列。
            var (revisionAt, cartRevision) = ResolveNextCartRevision(previousUpdatedAt);
            if (summaryRow != null)
            {
                summaryRow.CartRevision = cartRevision;
            }

            await _db.Updateable<WareHouseOrder>()
                .SetColumns(o => new WareHouseOrder
                {
                    OEMTotalAmount = totalOEM,
                    ImportTotalAmount = totalImport,
                    UpdatedAt = revisionAt,
                })
                .Where(o => o.OrderGUID == orderGuid)
                .ExecuteCommandAsync();

            return summaryRow;
        }

        public async Task<ApiResponse<List<StoreOrderDynamicDataDto>>> GetProductsDynamicDataAsync(
            StoreOrderDynamicDataRequestDto request
        )
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                var productCodes = (request.ProductCodes ?? new List<string>())
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (productCodes.Count == 0)
                {
                    return new ApiResponse<List<StoreOrderDynamicDataDto>>
                    {
                        Success = true,
                        Data = new List<StoreOrderDynamicDataDto>(),
                    };
                }

                var cartSw = Stopwatch.StartNew();
                var cartOwnerUserGuid = ResolveActiveCartOwnerUserGuid();
                // 1. 获取购物车数量 (FlowStatus = 0)，在数据库侧聚合，避免把整车明细拉回内存。
                var cartQuery = _db.Queryable<WareHouseOrderDetails>()
                    .InnerJoin<WareHouseOrder>((d, o) => d.OrderGUID == o.OrderGUID)
                    .Where(
                        (d, o) =>
                            o.StoreCode == request.StoreCode
                            && o.FlowStatus == 0
                            && !o.IsDeleted
                            && !d.IsDeleted
                    )
                    .Where(
                        (d, o) =>
                            d.ProductCode != null && productCodes.Contains(d.ProductCode)
                    );
                cartQuery = string.IsNullOrWhiteSpace(cartOwnerUserGuid)
                    ? cartQuery.Where((d, o) => SqlFunc.IsNullOrEmpty(o.CartOwnerUserGuid))
                    : cartQuery.Where((d, o) => o.CartOwnerUserGuid == cartOwnerUserGuid);
                var cartItems = await cartQuery
                    .GroupBy((d, o) => d.ProductCode)
                    .Select(
                        (d, o) =>
                            new
                            {
                                ProductCode = d.ProductCode,
                                CartQuantity = SqlFunc.AggregateSum(d.Quantity),
                            }
                    )
                    .ToListAsync();
                cartSw.Stop();

                var latestDateSw = Stopwatch.StartNew();
                // 2. 先在数据库侧按商品聚合最近历史订单日期，再回查覆盖这些最新日期的历史窗口。
                var latestOrderDates = await _db.Queryable<WareHouseOrderDetails>()
                    .InnerJoin<WareHouseOrder>((d, o) => d.OrderGUID == o.OrderGUID)
                    .Where(
                        (d, o) =>
                            o.StoreCode == request.StoreCode
                            && o.FlowStatus > 0
                            && !o.IsDeleted
                            && !d.IsDeleted
                    )
                    .Where(
                        (d, o) =>
                            d.ProductCode != null && productCodes.Contains(d.ProductCode)
                    )
                    .GroupBy((d, o) => d.ProductCode)
                    .Select(
                        (d, o) =>
                            new
                            {
                                ProductCode = d.ProductCode,
                                LastOrderDate = SqlFunc.AggregateMax(o.OrderDate),
                            }
                    )
                    .ToListAsync();
                latestDateSw.Stop();

                var historySw = Stopwatch.StartNew();
                var latestDateMap = latestOrderDates
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item.ProductCode) && item.LastOrderDate.HasValue
                    )
                    .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().LastOrderDate!.Value,
                        StringComparer.OrdinalIgnoreCase
                    );
                // 按首屏 ProductCode 一次性回查历史候选，再按 ProductCode 取最新行，避免产生 N 次 FirstAsync。
                var historyCandidates = latestDateMap.Count == 0
                    ? new List<StoreOrderDynamicHistoryRow>()
                    : await _db.Queryable<WareHouseOrderDetails>()
                        .InnerJoin<WareHouseOrder>((d, o) => d.OrderGUID == o.OrderGUID)
                        .Where(
                            (d, o) =>
                                o.StoreCode == request.StoreCode
                                && o.FlowStatus > 0
                                && !o.IsDeleted
                                && !d.IsDeleted
                        )
                        .Where(
                            (d, o) =>
                                d.ProductCode != null
                                && productCodes.Contains(d.ProductCode)
                                && o.OrderDate != null
                        )
                        .OrderBy((d, o) => o.OrderDate, OrderByType.Desc)
                        .Select(
                            (d, o) =>
                                new StoreOrderDynamicHistoryRow
                                {
                                    ProductCode = d.ProductCode,
                                    OrderDate = o.OrderDate,
                                    Quantity = d.Quantity,
                                    AllocQuantity = d.AllocQuantity,
                                }
                        )
                        .ToListAsync();
                var historyItems = historyCandidates
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item.ProductCode)
                        && item.OrderDate.HasValue
                        && latestDateMap.TryGetValue(item.ProductCode!, out var latestDate)
                        && item.OrderDate.Value == latestDate
                    )
                    .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                historySw.Stop();

                var cartQuantityMap = cartItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                    .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(item => item.CartQuantity ?? 0m),
                        StringComparer.OrdinalIgnoreCase
                    );
                var latestHistoryMap = historyItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                    .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First(),
                        StringComparer.OrdinalIgnoreCase
                    );

                // 3. 按请求顺序组装结果，字典查找避免页面商品多时反复线性扫描。
                var result = new List<StoreOrderDynamicDataDto>();
                foreach (var code in productCodes)
                {
                    var dto = new StoreOrderDynamicDataDto { ProductCode = code };

                    // 填充购物车数量
                    if (cartQuantityMap.TryGetValue(code, out var cartQuantity))
                    {
                        dto.CartQuantity = cartQuantity;
                    }

                    // 填充历史信息
                    if (latestHistoryMap.TryGetValue(code, out var historyItem))
                    {
                        dto.LastOrderDate = historyItem.OrderDate;
                        dto.LastQuantity = historyItem.Quantity;
                        dto.LastAllocQuantity = historyItem.AllocQuantity;
                    }

                    result.Add(dto);
                }

                totalSw.Stop();
                _logger.LogInformation(
                    "[shop-home-perf] stage=dynamic-data.service.done storeCode={StoreCode} requestCount={RequestCount} cartRows={CartRows} latestDateRows={LatestDateRows} historyRows={HistoryRows} cartMs={CartMs} latestDateMs={LatestDateMs} historyMs={HistoryMs} totalMs={TotalMs}",
                    request.StoreCode,
                    productCodes.Count,
                    cartItems.Count,
                    latestOrderDates.Count,
                    historyItems.Count,
                    cartSw.ElapsedMilliseconds,
                    latestDateSw.ElapsedMilliseconds,
                    historySw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );

                return new ApiResponse<List<StoreOrderDynamicDataDto>>
                {
                    Success = true,
                    Data = result,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProductsDynamicDataAsync failed");
                return new ApiResponse<List<StoreOrderDynamicDataDto>>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<PagedListReactDto<StoreOrderListItemDto>> GetOrderListAsync(
            StoreOrderListFilterDto filter
        )
        {
            try
            {
                var accessibleStoreCodes = await GetAccessibleStoreCodesAsync();
                ISugarQueryable<WareHouseOrder> q;

                // 0. 关键字筛选 (订单号 或 分店代码 或 商品货号)
                // 使用 Union 优化查询性能：分别查询主表和关联表，利用各自的索引
                if (!string.IsNullOrWhiteSpace(filter.Keyword))
                {
                    var keyword = filter.Keyword.Trim();

                    var matchedGuids = await _db.Queryable<WareHouseOrder>()
                        .Where(o =>
                            !o.IsDeleted
                            && (
                                (o.OrderNo != null && o.OrderNo.Contains(keyword))
                                || (o.StoreCode != null && o.StoreCode.Contains(keyword))
                            )
                        )
                        .Select(o => o.OrderGUID)
                        .ToListAsync();

                    var detailMatchedGuids = await _db.Queryable<WareHouseOrderDetails>()
                        .InnerJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                        .Where(
                            (d, p) =>
                                !d.IsDeleted
                                && !p.IsDeleted
                                && d.OrderGUID != null
                                && p.ItemNumber != null
                                && p.ItemNumber.Contains(keyword)
                        )
                        .Select(d => d.OrderGUID)
                        .Distinct()
                        .ToListAsync();

                    matchedGuids.AddRange(
                        detailMatchedGuids
                            .Where(guid => !string.IsNullOrWhiteSpace(guid))
                            .Select(guid => guid!)
                    );
                    matchedGuids = matchedGuids.Distinct().ToList();

                    q = _db.Queryable<WareHouseOrder>()
                        .Where(o => !o.IsDeleted && matchedGuids.Contains(o.OrderGUID));
                }
                else
                {
                    q = _db.Queryable<WareHouseOrder>().Where(o => !o.IsDeleted);
                }

                // 1. 分店筛选
                if (filter.StoreCodes != null && filter.StoreCodes.Any())
                {
                    var requestedStoreCodes = filter.StoreCodes
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (accessibleStoreCodes != null)
                    {
                        requestedStoreCodes = requestedStoreCodes
                            .Intersect(accessibleStoreCodes, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    if (!requestedStoreCodes.Any())
                    {
                        return new PagedListReactDto<StoreOrderListItemDto>
                        {
                            Items = new List<StoreOrderListItemDto>(),
                            Total = 0,
                            PageNumber = filter.PageNumber,
                            PageSize = filter.PageSize,
                        };
                    }

                    q = q.Where(o =>
                        o.StoreCode != null && requestedStoreCodes.Contains(o.StoreCode)
                    );
                }
                else if (!string.IsNullOrWhiteSpace(filter.StoreCode))
                {
                    var requestedStoreCode = filter.StoreCode.Trim();

                    if (
                        accessibleStoreCodes != null
                        && !accessibleStoreCodes.Contains(
                            requestedStoreCode,
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                    {
                        return new PagedListReactDto<StoreOrderListItemDto>
                        {
                            Items = new List<StoreOrderListItemDto>(),
                            Total = 0,
                            PageNumber = filter.PageNumber,
                            PageSize = filter.PageSize,
                        };
                    }

                    q = q.Where(o => o.StoreCode == requestedStoreCode);
                }
                else
                {
                    // 如果未指定 StoreCode，则只返回当前用户关联的分店订单
                    var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
                    if (!string.IsNullOrEmpty(currentUser))
                    {
                        // 假设从 UserStore 表获取用户关联的分店
                        // 注意：这里需要注入 IUserService 或直接查询 UserStore 表
                        // 由于未直接注入，这里使用 _db 查询
                        var userGuid = await _db.Queryable<User>()
                            .Where(u => u.Username == currentUser)
                            .Select(u => u.UserGUID)
                            .FirstAsync();

                        if (!string.IsNullOrEmpty(userGuid))
                        {
                            var userRoles = await _db.Queryable<UserRole>()
                                .InnerJoin<Role>((ur, r) => ur.RoleGUID == r.RoleGUID)
                                .Where((ur, r) => ur.UserGUID == userGuid && r.IsActive)
                                .Select((ur, r) => r.RoleName)
                                .ToListAsync();

                            bool isAdminOrManager = userRoles.Any(role =>
                                role == "Admin" || role == "Manager"
                            );

                            if (!isAdminOrManager)
                            {
                                var userStoreCodes = await _db.Queryable<UserStore>()
                                    .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                                    .Where((us, s) => us.UserGUID == userGuid)
                                    .Select((us, s) => s.StoreCode)
                                    .ToListAsync();
                                userStoreCodes = userStoreCodes
                                    .Where(code => !string.IsNullOrWhiteSpace(code))
                                    .Select(code => code!)
                                    .ToList();

                                if (userStoreCodes.Any())
                                {
                                    q = q.Where(o =>
                                        o.StoreCode != null && userStoreCodes.Contains(o.StoreCode)
                                    );
                                }
                            }
                        }
                    }
                }

                // 2. 状态筛选
                if (filter.StatusList != null && filter.StatusList.Any())
                {
                    // 不包含状态 2，直接使用 StatusList
                    q = q.Where(o =>
                        o.FlowStatus != null && filter.StatusList.Contains(o.FlowStatus.Value)
                    );
                }

                // 3. 日期筛选
                if (filter.StartDate.HasValue)
                {
                    var start = filter.StartDate.Value.Date;
                    q = q.Where(o => o.OrderDate >= start);
                }
                if (filter.EndDate.HasValue)
                {
                    var end = filter.EndDate.Value.Date.AddDays(1).AddMilliseconds(-1);
                    q = q.Where(o => o.OrderDate <= end);
                }

                // 动态排序处理
                var sortBy = (filter.SortBy ?? "default").Trim().ToLower();
                var orderType =
                    (filter.SortDescending ?? true) ? OrderByType.Desc : OrderByType.Asc;

                q = ApplyOrderListMainColumnFilters(q, filter.ColumnFilters);

                if (ShouldUseAggregateOrderListPipeline(filter.ColumnFilters, sortBy))
                {
                    return await BuildAggregateSortedOrderListAsync(q, filter, sortBy, orderType);
                }

                var total = await q.Clone().CountAsync();

                ISugarQueryable<WareHouseOrder> orderedQuery = q;

                switch (sortBy)
                {
                    case "orderno":
                        orderedQuery = q.OrderBy(o => o.OrderNo, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "orderdate":
                        orderedQuery = q.OrderBy(o => o.OrderDate, orderType)
                            .OrderBy(o => o.OrderNo, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "storecode":
                        orderedQuery = q.OrderBy(o => o.StoreCode, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "flowstatus":
                        orderedQuery = q.OrderBy(o => o.FlowStatus, orderType)
                            .OrderByDescending(o => o.OrderDate)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "createdat":
                        // 商品等级“加入仓库订单”下拉复用订单列表接口，必须先按创建时间排序再分页，避免首屏不是最新订单。
                        orderedQuery = q.OrderBy(o => o.CreatedAt, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "totalamount":
                        orderedQuery = q.OrderBy(o => o.ImportTotalAmount ?? 0, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "oemtotalamount":
                        orderedQuery = q.OrderBy(o => o.OEMTotalAmount ?? 0, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "importtotalamount":
                        orderedQuery = q.OrderBy(o => o.ImportTotalAmount ?? 0, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "remarks":
                        orderedQuery = q.OrderBy(o => o.Remarks, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    default:
                        orderedQuery = q.OrderBy(o => o.FlowStatus, OrderByType.Asc)
                            .OrderBy(o => o.OrderDate, OrderByType.Desc)
                            .OrderBy(o => o.OrderNo, OrderByType.Desc);
                        break;
                }

                var items = await orderedQuery
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .Select(o => new StoreOrderListItemDto
                    {
                        OrderGUID = o.OrderGUID,
                        OrderNo = o.OrderNo ?? string.Empty,
                        StoreCode = o.StoreCode,
                        StoreName = SqlFunc
                            .Subqueryable<Store>()
                            .Where(s => s.StoreCode == o.StoreCode || s.StoreGUID == o.StoreCode)
                            .Select(s => s.StoreName),
                        OrderDate = o.OrderDate,
                        OutboundDate = o.OutboundDate,
                        FlowStatus = o.FlowStatus ?? 0,

                        // TotalAmount -> 实际发货金额
                        TotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID && !d.IsDeleted)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.ImportPrice ?? 0)),
                        //发货 预计销售sales
                        OEMTotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID && !d.IsDeleted)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.OEMAmount ?? 0)),

                        // ImportTotalAmount -> 发货金额 (Alloc Qty * OEMPrice)
                        ImportTotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID && !d.IsDeleted)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.ImportPrice ?? 0)),

                        // TotalOrderAmount -> 订货金额 (Order Qty * OEMPrice)
                        TotalOrderAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID && !d.IsDeleted)
                            .Sum(d => (d.Quantity ?? 0) * (d.ImportPrice ?? 0)),
                        //订货数量
                        TotalQuantity = (int)(
                            SqlFunc
                                .Subqueryable<WareHouseOrderDetails>()
                                .Where(d => d.OrderGUID == o.OrderGUID && !d.IsDeleted)
                                .Sum(d => d.Quantity)
                            ?? 0
                        ),

                        // TotalAllocQuantity -> 发货数量 (Alloc Qty)
                        TotalAllocQuantity = (int)(
                            SqlFunc
                                .Subqueryable<WareHouseOrderDetails>()
                                .Where(d => d.OrderGUID == o.OrderGUID && !d.IsDeleted)
                                .Sum(d => d.AllocQuantity)
                            ?? 0
                        ),

                        Remarks = o.Remarks,

                        CreatedAt = o.CreatedAt,
                        CreatedBy = o.CreatedBy,
                        UpdatedAt = o.UpdatedAt,
                        UpdatedBy = o.UpdatedBy,
                    })
                    .ToListAsync();

                // 内存排序兜底，解决 SQL Server 分页后外层无 Order By 导致的乱序问题
                if (items.Any())
                {
                    switch (sortBy)
                    {
                        case "orderno":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.OrderNo)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.OrderNo)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "orderdate":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.OrderDate)
                                        .ThenByDescending(x => x.OrderNo)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.OrderDate)
                                        .ThenBy(x => x.OrderNo)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "storecode":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.StoreCode)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.StoreCode)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "flowstatus":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.FlowStatus)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.FlowStatus)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "createdat":
                            // SQL Server 分页后这里再次按创建时间稳定排序，保证下拉翻页追加顺序一致。
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.CreatedAt)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.CreatedAt)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "totalamount":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.TotalAmount)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.TotalAmount)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "oemtotalamount":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.OEMTotalAmount)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.OEMTotalAmount)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        case "importtotalamount":
                            items =
                                orderType == OrderByType.Desc
                                    ? items
                                        .OrderByDescending(x => x.ImportTotalAmount)
                                        .ThenByDescending(x => x.OrderGUID)
                                        .ToList()
                                    : items
                                        .OrderBy(x => x.ImportTotalAmount)
                                        .ThenBy(x => x.OrderGUID)
                                        .ToList();
                            break;
                        default:
                            items = items
                                .OrderByDescending(x => x.OrderDate)
                                .ThenByDescending(x => x.OrderNo)
                                .ThenByDescending(x => x.OrderGUID)
                                .ToList();
                            break;
                    }

                    await FillOrderListVolumesAsync(items);
                }

                return new PagedListReactDto<StoreOrderListItemDto>
                {
                    Items = items,
                    Total = total,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetOrderListAsync failed");
                throw;
            }
        }

        public async Task<ApiResponse<StoreOrderImportPriceVarianceResultDto>> GetImportPriceVarianceAsync(
            StoreOrderImportPriceVarianceQueryDto query
        )
        {
            query ??= new StoreOrderImportPriceVarianceQueryDto();
            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 500);

            try
            {
                var (requestedStoreCodes, noAccessibleStores) =
                    await ResolveStoreOrderImportPriceVarianceStoreCodesAsync(query);
                if (noAccessibleStores)
                {
                    return ApiResponse<StoreOrderImportPriceVarianceResultDto>.OK(
                        CreateEmptyImportPriceVarianceResult(pageNumber, pageSize)
                    );
                }

                var sql = BuildStoreOrderImportPriceVarianceSummarySql(
                    query,
                    requestedStoreCodes,
                    pageNumber,
                    pageSize,
                    _db.CurrentConnectionConfig.DbType == DbType.Sqlite
                );
                var summaryRow = (
                    await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceSummarySqlRow>(
                        sql.SummarySql,
                        sql.Parameters.ToArray()
                    )
                ).FirstOrDefault() ?? new StoreOrderImportPriceVarianceSummarySqlRow();
                var pageItems = await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceItemDto>(
                    sql.PagedSql,
                    sql.Parameters.ToArray()
                );
                var supplierSummaries = await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceSupplierSummaryDto>(
                    sql.SupplierSummarySql,
                    sql.Parameters.ToArray()
                );

                return ApiResponse<StoreOrderImportPriceVarianceResultDto>.OK(
                    new StoreOrderImportPriceVarianceResultDto
                    {
                        Items = pageItems,
                        Total = summaryRow.TotalRows,
                        PageNumber = pageNumber,
                        PageSize = pageSize,
                        Summary = new StoreOrderImportPriceVarianceSummaryDto
                        {
                            TotalRows = summaryRow.TotalRows,
                            OriginalImportAmountTotal = summaryRow.OriginalImportAmountTotal,
                            BaselineImportAmountTotal = summaryRow.BaselineImportAmountTotal,
                            VarianceAmountTotal = summaryRow.VarianceAmountTotal,
                        },
                        SupplierSummaries = supplierSummaries,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetImportPriceVarianceAsync failed");
                return new ApiResponse<StoreOrderImportPriceVarianceResultDto>
                {
                    Success = false,
                    Message = ex.Message,
                    Data = CreateEmptyImportPriceVarianceResult(pageNumber, pageSize),
                };
            }
        }

        public async Task<ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>> GetImportPriceVarianceDetailsAsync(
            StoreOrderImportPriceVarianceDetailQueryDto query
        )
        {
            query ??= new StoreOrderImportPriceVarianceDetailQueryDto();
            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 500);
            var productCode = query.ProductCode?.Trim();

            if (string.IsNullOrWhiteSpace(productCode))
            {
                return ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>.OK(
                    CreateEmptyImportPriceVarianceDetailResult(pageNumber, pageSize)
                );
            }

            try
            {
                var (requestedStoreCodes, noAccessibleStores) =
                    await ResolveStoreOrderImportPriceVarianceStoreCodesAsync(query);
                if (noAccessibleStores)
                {
                    return ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>.OK(
                        CreateEmptyImportPriceVarianceDetailResult(pageNumber, pageSize)
                    );
                }

                var sql = BuildStoreOrderImportPriceVarianceDetailSql(
                    query,
                    productCode,
                    requestedStoreCodes,
                    pageNumber,
                    pageSize,
                    _db.CurrentConnectionConfig.DbType == DbType.Sqlite
                );
                var summaryRow = (
                    await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceSummarySqlRow>(
                        sql.SummarySql,
                        sql.Parameters.ToArray()
                    )
                ).FirstOrDefault() ?? new StoreOrderImportPriceVarianceSummarySqlRow();
                var pageItems = await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceDetailItemDto>(
                    sql.PagedSql,
                    sql.Parameters.ToArray()
                );

                return ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>.OK(
                    new StoreOrderImportPriceVarianceDetailResultDto
                    {
                        Items = pageItems,
                        Total = summaryRow.TotalRows,
                        PageNumber = pageNumber,
                        PageSize = pageSize,
                        Summary = new StoreOrderImportPriceVarianceSummaryDto
                        {
                            TotalRows = summaryRow.TotalRows,
                            OriginalImportAmountTotal = summaryRow.OriginalImportAmountTotal,
                            BaselineImportAmountTotal = summaryRow.BaselineImportAmountTotal,
                            VarianceAmountTotal = summaryRow.VarianceAmountTotal,
                        },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetImportPriceVarianceDetailsAsync failed");
                return new ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>
                {
                    Success = false,
                    Message = ex.Message,
                    Data = CreateEmptyImportPriceVarianceDetailResult(pageNumber, pageSize),
                };
            }
        }

        public async Task<ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>> UpdateImportPriceVarianceDomesticPriceAsync(
            StoreOrderImportPriceVarianceDomesticPriceUpdateDto request
        )
        {
            var productCode = request?.ProductCode?.Trim();
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "商品编码不能为空",
                };
            }

            if (!request!.DomesticPrice.HasValue)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "国内价格不能为空",
                };
            }

            if (request.DomesticPrice.Value < 0)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "国内价格不能小于 0",
                };
            }

            try
            {
                var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                    .FirstAsync(wp => wp.ProductCode == productCode && !wp.IsDeleted);
                if (warehouseProduct == null)
                {
                    return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
                    {
                        Success = false,
                        Message = "未找到仓库商品，无法更新国内价格",
                    };
                }

                var normalizedPrice = Math.Round(request.DomesticPrice.Value, 2, MidpointRounding.AwayFromZero);
                warehouseProduct.DomesticPrice = normalizedPrice;

                // 该页面编辑的是仓库商品国内价格，严格只回写 WarehouseProduct.DomesticPrice。
                await _db.Updateable(warehouseProduct)
                    .UpdateColumns(wp => new
                    {
                        wp.DomesticPrice,
                    })
                    .ExecuteCommandAsync();

                return ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>.OK(
                    new StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto
                    {
                        ProductCode = productCode,
                        DomesticPrice = normalizedPrice,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateImportPriceVarianceDomesticPriceAsync failed for {ProductCode}", productCode);
                return new ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "保存国内价格失败",
                };
            }
        }

        public async Task<ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>> UpdateImportPriceVarianceWarehouseImportPriceAsync(
            StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto request
        )
        {
            var productCode = request?.ProductCode?.Trim();
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "商品编码不能为空",
                };
            }

            var requestedWarehouseImportPrice = request?.WarehouseImportPrice;
            if (!requestedWarehouseImportPrice.HasValue)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "仓库进货价格不能为空",
                };
            }

            if (requestedWarehouseImportPrice.Value < 0)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "仓库进货价格不能小于 0",
                };
            }

            try
            {
                var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                    .FirstAsync(wp => wp.ProductCode == productCode && !wp.IsDeleted);
                if (warehouseProduct == null)
                {
                    return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
                    {
                        Success = false,
                        Message = "未找到仓库商品，无法更新仓库进货价格",
                    };
                }

                var normalizedPrice = Math.Round(requestedWarehouseImportPrice.Value, 2, MidpointRounding.AwayFromZero);
                var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                warehouseProduct.ImportPrice = normalizedPrice;
                warehouseProduct.UpdatedAt = DateTime.UtcNow;
                warehouseProduct.UpdatedBy = currentUser;

                // 该页面编辑的是仓库商品当前进货价格，只更新 WarehouseProduct.ImportPrice。
                await _db.Updateable(warehouseProduct)
                    .UpdateColumns(wp => new
                    {
                        wp.ImportPrice,
                        wp.UpdatedAt,
                        wp.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                return ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>.OK(
                    new StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto
                    {
                        ProductCode = productCode,
                        WarehouseImportPrice = normalizedPrice,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateImportPriceVarianceWarehouseImportPriceAsync failed for {ProductCode}", productCode);
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
                {
                    Success = false,
                    Message = "保存仓库进货价格失败",
                };
            }
        }

        public async Task<ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>> UpdateImportPriceVarianceWarehouseImportPriceBatchAsync(
            StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto request
        )
        {
            var productCodes = (request?.ProductCodes ?? new List<string>())
                .Select(code => code?.Trim())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(code => code!)
                .ToList();

            if (!productCodes.Any())
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
                {
                    Success = false,
                    Message = "请选择商品",
                };
            }

            // 批量查询会生成 SQL 参数；这里主动限流，避免跨页选择过多时撞数据库参数上限。
            if (productCodes.Count > ImportPriceVarianceWarehouseImportPriceBatchLimit)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
                {
                    Success = false,
                    Message = $"一次最多选择 {ImportPriceVarianceWarehouseImportPriceBatchLimit} 个商品",
                };
            }

            var requestedBatchWarehouseImportPrice = request?.WarehouseImportPrice;
            if (!requestedBatchWarehouseImportPrice.HasValue)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
                {
                    Success = false,
                    Message = "仓库进货价格不能为空",
                };
            }

            if (requestedBatchWarehouseImportPrice.Value < 0)
            {
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
                {
                    Success = false,
                    Message = "仓库进货价格不能小于 0",
                };
            }

            var normalizedPrice = Math.Round(requestedBatchWarehouseImportPrice.Value, 2, MidpointRounding.AwayFromZero);
            var transactionStarted = false;
            try
            {
                var warehouseProducts = await _db.Queryable<WarehouseProduct>()
                    .Where(wp => productCodes.Contains(wp.ProductCode) && !wp.IsDeleted)
                    .ToListAsync();
                var existingCodeSet = warehouseProducts
                    .Select(wp => wp.ProductCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingCodes = productCodes.Where(code => !existingCodeSet.Contains(code)).ToList();
                if (missingCodes.Any())
                {
                    return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
                    {
                        Success = false,
                        Message = $"未找到仓库商品，无法批量更新仓库进货价格：{string.Join(", ", missingCodes)}",
                    };
                }

                var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                var now = DateTime.UtcNow;
                foreach (var warehouseProduct in warehouseProducts)
                {
                    warehouseProduct.ImportPrice = normalizedPrice;
                    warehouseProduct.UpdatedAt = now;
                    warehouseProduct.UpdatedBy = currentUser;
                }

                _db.Ado.BeginTran();
                transactionStarted = true;

                // 该批量入口只修改 WarehouseProduct 当前参考进货价，避免误联动商品主档、分店价格或历史订单。
                await _db.Updateable(warehouseProducts)
                    .UpdateColumns(wp => new
                    {
                        wp.ImportPrice,
                        wp.UpdatedAt,
                        wp.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                _db.Ado.CommitTran();
                transactionStarted = false;

                return ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>.OK(
                    new StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto
                    {
                        UpdatedCount = warehouseProducts.Count,
                        WarehouseImportPrice = normalizedPrice,
                        ProductCodes = productCodes,
                    }
                );
            }
            catch (Exception ex)
            {
                if (transactionStarted)
                {
                    _db.Ado.RollbackTran();
                }

                _logger.LogError(
                    ex,
                    "UpdateImportPriceVarianceWarehouseImportPriceBatchAsync failed for {ProductCodes}",
                    string.Join(", ", productCodes)
                );
                return new ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
                {
                    Success = false,
                    Message = "批量保存仓库进货价格失败",
                };
            }
        }

        private async Task<(List<string> StoreCodes, bool NoAccessibleStores)> ResolveStoreOrderImportPriceVarianceStoreCodesAsync(
            StoreOrderImportPriceVarianceQueryDto query
        )
        {
            var accessibleStoreCodes = await GetAccessibleStoreCodesAsync();
            var requestedStoreCodes = NormalizeStoreOrderImportPriceVarianceStoreCodes(query);
            if (accessibleStoreCodes == null)
            {
                return (requestedStoreCodes, false);
            }

            requestedStoreCodes = requestedStoreCodes.Any()
                ? requestedStoreCodes
                    .Intersect(accessibleStoreCodes, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : accessibleStoreCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            return (requestedStoreCodes, !requestedStoreCodes.Any());
        }

        private static List<string> NormalizeStoreOrderImportPriceVarianceStoreCodes(
            StoreOrderImportPriceVarianceQueryDto query
        )
        {
            var storeCodes = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.StoreCode))
            {
                storeCodes.Add(query.StoreCode.Trim());
            }

            if (query.StoreCodes != null)
            {
                storeCodes.AddRange(
                    query.StoreCodes
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code.Trim())
                );
            }

            return storeCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static StoreOrderImportPriceVarianceSqlBuildResult BuildStoreOrderImportPriceVarianceSummarySql(
            StoreOrderImportPriceVarianceQueryDto query,
            IReadOnlyList<string> requestedStoreCodes,
            int pageNumber,
            int pageSize,
            bool isSqlite
        )
        {
            var parameters = new List<SugarParameter>
            {
                new("@Offset", (pageNumber - 1) * pageSize),
                new("@PageSize", pageSize),
            };
            var orderDirection = query.SortDescending ? "DESC" : "ASC";
            var orderExpression = ResolveStoreOrderImportPriceVarianceSummaryOrderExpression(query.SortBy);
            var paginationSql = isSqlite
                ? "LIMIT @PageSize OFFSET @Offset"
                : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var baseSql = BuildStoreOrderImportPriceVarianceBaseSql(
                query,
                requestedStoreCodes,
                parameters,
                isSqlite,
                productCodeFilter: null
            );

            var groupedSql =
                baseSql
                + """
,
GroupedRows AS (
    SELECT
        ProductCode,
        MAX(ItemNumber) AS ItemNumber,
        MAX(ProductName) AS ProductName,
        MAX(ProductImage) AS ProductImage,
        MAX(SupplierCode) AS SupplierCode,
        MAX(SupplierName) AS SupplierName,
        MAX(DomesticPrice) AS DomesticPrice,
        MAX(WarehouseImportPrice) AS WarehouseImportPrice,
        MAX(UnitVolume) AS UnitVolume,
        MAX(PackingQuantity) AS PackingQuantity,
        MAX(FirstContainerImportPrice) AS FirstContainerImportPrice,
        CAST(COALESCE(SUM(AllocQuantity), 0) AS decimal(18, 2)) AS AllocQuantityTotal,
        CAST(COALESCE(SUM(OriginalImportAmount), 0) AS decimal(18, 2)) AS OriginalImportAmountTotal,
        CAST(COALESCE(SUM(BaselineImportAmount), 0) AS decimal(18, 2)) AS BaselineImportAmountTotal,
        CAST(COALESCE(SUM(VarianceAmount), 0) AS decimal(18, 2)) AS VarianceAmountTotal,
        CAST(COUNT(1) AS int) AS DetailCount,
        MAX(FirstContainerCode) AS FirstContainerCode,
        MAX(FirstContainerNumber) AS FirstContainerNumber,
        MAX(FirstContainerDate) AS FirstContainerDate
    FROM FinalRows
    GROUP BY ProductCode
)
""";

            return new StoreOrderImportPriceVarianceSqlBuildResult
            {
                Parameters = parameters,
                SummarySql =
                    groupedSql
                    + """
SELECT
    CAST(COUNT(1) AS int) AS TotalRows,
    CAST(COALESCE(SUM(OriginalImportAmountTotal), 0) AS decimal(18, 2)) AS OriginalImportAmountTotal,
    CAST(COALESCE(SUM(BaselineImportAmountTotal), 0) AS decimal(18, 2)) AS BaselineImportAmountTotal,
    CAST(COALESCE(SUM(VarianceAmountTotal), 0) AS decimal(18, 2)) AS VarianceAmountTotal
FROM GroupedRows
""",
                PagedSql =
                    groupedSql
                    + $"""
SELECT
    ProductCode,
    ItemNumber,
    ProductName,
    ProductImage,
    SupplierCode,
    SupplierName,
    DomesticPrice,
    WarehouseImportPrice,
    UnitVolume,
    PackingQuantity,
    FirstContainerImportPrice,
    AllocQuantityTotal,
    OriginalImportAmountTotal,
    BaselineImportAmountTotal,
    VarianceAmountTotal,
    DetailCount,
    FirstContainerCode,
    FirstContainerNumber,
    FirstContainerDate
FROM GroupedRows
ORDER BY {orderExpression} {orderDirection}, ProductCode ASC
{paginationSql}
""",
                SupplierSummarySql =
                    baseSql
                    + """
,
SupplierRows AS (
    SELECT
        SupplierCode,
        SupplierName,
        CAST(COUNT(DISTINCT ProductCode) AS int) AS ProductCount,
        CAST(COUNT(1) AS int) AS DetailCount,
        CAST(COALESCE(SUM(OriginalImportAmount), 0) AS decimal(18, 2)) AS OriginalImportAmountTotal,
        CAST(COALESCE(SUM(BaselineImportAmount), 0) AS decimal(18, 2)) AS BaselineImportAmountTotal,
        CAST(COALESCE(SUM(CASE WHEN VarianceAmount > 0 THEN VarianceAmount ELSE 0 END), 0) AS decimal(18, 2)) AS IncreaseVarianceAmountTotal,
        CAST(COALESCE(SUM(CASE WHEN VarianceAmount < 0 THEN -VarianceAmount ELSE 0 END), 0) AS decimal(18, 2)) AS DecreaseVarianceAmountTotal,
        CAST(COALESCE(SUM(VarianceAmount), 0) AS decimal(18, 2)) AS VarianceAmountTotal
    FROM FinalRows
    GROUP BY SupplierCode, SupplierName
)
SELECT
    SupplierCode,
    SupplierName,
    ProductCount,
    DetailCount,
    OriginalImportAmountTotal,
    BaselineImportAmountTotal,
    IncreaseVarianceAmountTotal,
    DecreaseVarianceAmountTotal,
    VarianceAmountTotal
FROM SupplierRows
-- 返回当前筛选条件下全部供应商，页面本地分页和排序。
ORDER BY ABS(VarianceAmountTotal) DESC, SupplierCode ASC
""",
            };
        }

        private static StoreOrderImportPriceVarianceSqlBuildResult BuildStoreOrderImportPriceVarianceDetailSql(
            StoreOrderImportPriceVarianceDetailQueryDto query,
            string productCode,
            IReadOnlyList<string> requestedStoreCodes,
            int pageNumber,
            int pageSize,
            bool isSqlite
        )
        {
            var parameters = new List<SugarParameter>
            {
                new("@Offset", (pageNumber - 1) * pageSize),
                new("@PageSize", pageSize),
            };
            var orderBy = BuildStoreOrderImportPriceVarianceDetailOrderBy(
                query.SortBy,
                query.SortDescending
            );
            var paginationSql = isSqlite
                ? "LIMIT @PageSize OFFSET @Offset"
                : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var baseSql = BuildStoreOrderImportPriceVarianceBaseSql(
                query,
                requestedStoreCodes,
                parameters,
                isSqlite,
                productCodeFilter: productCode
            );

            return new StoreOrderImportPriceVarianceSqlBuildResult
            {
                Parameters = parameters,
                SummarySql =
                    baseSql
                    + """
SELECT
    CAST(COUNT(1) AS int) AS TotalRows,
    CAST(COALESCE(SUM(OriginalImportAmount), 0) AS decimal(18, 2)) AS OriginalImportAmountTotal,
    CAST(COALESCE(SUM(BaselineImportAmount), 0) AS decimal(18, 2)) AS BaselineImportAmountTotal,
    CAST(COALESCE(SUM(VarianceAmount), 0) AS decimal(18, 2)) AS VarianceAmountTotal
FROM FinalRows
""",
                PagedSql =
                    baseSql
                    + $"""
SELECT
    OrderGUID,
    DetailGUID,
    OrderNo,
    OrderDate,
    StoreCode,
    StoreName,
    ProductCode,
    ItemNumber,
    ProductName,
    OrderImportPrice,
    FirstContainerImportPrice,
    AllocQuantity,
    OriginalImportAmount,
    BaselineImportAmount,
    VarianceAmount,
    FirstContainerCode,
    FirstContainerNumber,
    FirstContainerDate
FROM FinalRows
ORDER BY {orderBy}
{paginationSql}
""",
            };
        }

        private static string BuildStoreOrderImportPriceVarianceBaseSql(
            StoreOrderImportPriceVarianceQueryDto query,
            IReadOnlyList<string> requestedStoreCodes,
            List<SugarParameter> parameters,
            bool isSqlite,
            string? productCodeFilter
        )
        {
            var rowFilters = new StringBuilder();

            if (requestedStoreCodes.Any())
            {
                var names = new List<string>();
                for (var index = 0; index < requestedStoreCodes.Count; index += 1)
                {
                    var parameterName = $"@StoreCode{index}";
                    names.Add(parameterName);
                    parameters.Add(new SugarParameter(parameterName, requestedStoreCodes[index]));
                }
                rowFilters.AppendLine($"    AND o.StoreCode IN ({string.Join(", ", names)})");
            }

            if (!string.IsNullOrWhiteSpace(productCodeFilter))
            {
                parameters.Add(new SugarParameter("@ProductCode", productCodeFilter.Trim()));
                rowFilters.AppendLine("    AND d.ProductCode = @ProductCode");
            }

            if (!string.IsNullOrWhiteSpace(query.SupplierCode))
            {
                parameters.Add(new SugarParameter("@SupplierCode", query.SupplierCode.Trim()));
                // 国内供应商过滤只匹配 DomesticProduct.SupplierCode，不能误用 Product.LocalSupplierCode。
                rowFilters.AppendLine("    AND dp.SupplierCode = @SupplierCode");
            }

            if (!string.IsNullOrWhiteSpace(query.OrderNo))
            {
                parameters.Add(new SugarParameter("@OrderNo", $"%{query.OrderNo.Trim()}%"));
                rowFilters.AppendLine("    AND o.OrderNo LIKE @OrderNo");
            }

            if (query.StartDate.HasValue)
            {
                parameters.Add(new SugarParameter("@StartDate", query.StartDate.Value.Date));
                rowFilters.AppendLine("    AND o.OrderDate >= @StartDate");
            }

            if (query.EndDate.HasValue)
            {
                parameters.Add(new SugarParameter("@EndDate", query.EndDate.Value.Date.AddDays(1).AddTicks(-1)));
                rowFilters.AppendLine("    AND o.OrderDate <= @EndDate");
            }

            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                parameters.Add(new SugarParameter("@Keyword", $"%{query.Keyword.Trim()}%"));
                rowFilters.AppendLine(
                    """
    AND (
        o.OrderNo LIKE @Keyword
        OR o.StoreCode LIKE @Keyword
        OR s.StoreName LIKE @Keyword
        OR d.ProductCode LIKE @Keyword
        OR p.ItemNumber LIKE @Keyword
        OR dp.HBProductNo LIKE @Keyword
        OR p.ProductName LIKE @Keyword
        OR dp.ProductName LIKE @Keyword
        OR dp.SupplierCode LIKE @Keyword
        OR cs.SupplierName LIKE @Keyword
    )
"""
                );
            }

            var directionFilter = (query.VarianceDirection ?? "all").Trim().ToLowerInvariant() switch
            {
                "increase" => "WHERE VarianceAmount > 0",
                "decrease" => "WHERE VarianceAmount < 0",
                _ => string.Empty,
            };
            var containerNumberLengthFilter = isSqlite
                ? "LENGTH(c.ContainerNumber) > 10"
                : "LEN(c.ContainerNumber) > 10";

            return
                """
WITH FirstContainerRanked AS (
    SELECT
        cd.ProductCode AS ProductCode,
        cd.ImportPrice AS FirstContainerImportPrice,
        c.ContainerCode AS FirstContainerCode,
        c.ContainerNumber AS FirstContainerNumber,
        c.LoadingDate AS FirstContainerDate,
        ROW_NUMBER() OVER (
            PARTITION BY cd.ProductCode
            ORDER BY c.LoadingDate ASC, c.ContainerCode ASC, cd.DetailCode ASC
        ) AS RowNumber
    FROM [ContainerDetail] cd
    INNER JOIN [Container] c ON cd.ContainerCode = c.ContainerCode
    WHERE
        (cd.IsDeleted = 0 OR cd.IsDeleted IS NULL)
        AND (c.IsDeleted = 0 OR c.IsDeleted IS NULL)
        AND cd.ProductCode IS NOT NULL
        AND cd.ProductCode <> ''
        AND cd.ImportPrice IS NOT NULL
        AND cd.ImportPrice > 0
        -- 有效首次货柜必须有长度大于 10 的货柜编号，排除临时或异常短编号。
        AND c.ContainerNumber IS NOT NULL
        AND c.ContainerNumber <> ''
"""
                + $"        AND {containerNumberLengthFilter}\n"
                + """
        AND c.LoadingDate IS NOT NULL
),
FirstContainer AS (
    SELECT
        ProductCode,
        FirstContainerImportPrice,
        FirstContainerCode,
        FirstContainerNumber,
        FirstContainerDate
    FROM FirstContainerRanked
    WHERE RowNumber = 1
),
FilteredRows AS (
    SELECT
        o.OrderGUID AS OrderGUID,
        d.DetailGUID AS DetailGUID,
        o.OrderNo AS OrderNo,
        o.OrderDate AS OrderDate,
        o.StoreCode AS StoreCode,
        s.StoreName AS StoreName,
        d.ProductCode AS ProductCode,
        -- 商品展示字段按确认来源取值；图片空字符串也要回退国内商品图片。
        COALESCE(NULLIF(p.ItemNumber, ''), dp.HBProductNo) AS ItemNumber,
        COALESCE(NULLIF(p.ProductName, ''), dp.ProductName) AS ProductName,
        COALESCE(NULLIF(p.ProductImage, ''), dp.ProductImage) AS ProductImage,
        dp.SupplierCode AS SupplierCode,
        cs.SupplierName AS SupplierName,
        CAST(COALESCE(wp.DomesticPrice, dp.DomesticPrice) AS decimal(18, 2)) AS DomesticPrice,
        CAST(wp.ImportPrice AS decimal(18, 2)) AS WarehouseImportPrice,
        CAST(COALESCE(wp.Volume, dp.UnitVolume) AS decimal(18, 4)) AS UnitVolume,
        COALESCE(wp.PackingQuantity, dp.PackingQuantity) AS PackingQuantity,
        CAST(d.ImportPrice AS decimal(18, 2)) AS OrderImportPrice,
        CAST(fc.FirstContainerImportPrice AS decimal(18, 2)) AS FirstContainerImportPrice,
        CAST(COALESCE(d.AllocQuantity, 0) AS decimal(18, 2)) AS AllocQuantity,
        CAST(COALESCE(d.AllocQuantity, 0) * d.ImportPrice AS decimal(18, 2)) AS OriginalImportAmount,
        CAST(COALESCE(d.AllocQuantity, 0) * fc.FirstContainerImportPrice AS decimal(18, 2)) AS BaselineImportAmount,
        -- 首次有效货柜价是基准价；原始金额按仓库订单进货价乘发货数量计算，不使用明细存储金额。
        CAST(
            (COALESCE(d.AllocQuantity, 0) * d.ImportPrice)
            - (COALESCE(d.AllocQuantity, 0) * fc.FirstContainerImportPrice)
            AS decimal(18, 2)
        ) AS VarianceAmount,
        fc.FirstContainerCode AS FirstContainerCode,
        fc.FirstContainerNumber AS FirstContainerNumber,
        fc.FirstContainerDate AS FirstContainerDate
    FROM [WareHouseOrderDetails] d
    INNER JOIN [WareHouseOrder] o ON d.OrderGUID = o.OrderGUID
    INNER JOIN FirstContainer fc ON d.ProductCode = fc.ProductCode
    LEFT JOIN [Product] p
        ON d.ProductCode = p.ProductCode
        AND (p.IsDeleted = 0 OR p.IsDeleted IS NULL)
    LEFT JOIN [WarehouseProduct] wp
        ON d.ProductCode = wp.ProductCode
        AND (wp.IsDeleted = 0 OR wp.IsDeleted IS NULL)
    LEFT JOIN [DomesticProduct] dp
        ON d.ProductCode = dp.ProductCode
        AND (dp.IsDeleted = 0 OR dp.IsDeleted IS NULL)
    LEFT JOIN [ChinaSupplier] cs
        ON dp.SupplierCode = cs.SupplierCode
        AND (cs.IsDeleted = 0 OR cs.IsDeleted IS NULL)
    LEFT JOIN [Store] s
        ON (o.StoreCode = s.StoreCode OR o.StoreCode = s.StoreGUID)
        AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
    WHERE
        (d.IsDeleted = 0 OR d.IsDeleted IS NULL)
        AND (o.IsDeleted = 0 OR o.IsDeleted IS NULL)
        AND d.ProductCode IS NOT NULL
        AND d.ProductCode <> ''
        AND d.ImportPrice IS NOT NULL
        AND d.ImportPrice > 0
        AND o.OrderDate > fc.FirstContainerDate
        AND d.ImportPrice <> fc.FirstContainerImportPrice
        -- 订单价和首次货柜价相差超过 10 倍视为异常数据，不纳入统计。
        AND d.ImportPrice <= fc.FirstContainerImportPrice * 10
        AND fc.FirstContainerImportPrice <= d.ImportPrice * 10
"""
                + rowFilters
                + """
),
FinalRows AS (
    SELECT *
    FROM FilteredRows
"""
                + (string.IsNullOrWhiteSpace(directionFilter) ? string.Empty : $"    {directionFilter}\n")
                + ")\n";
        }

        private static string ResolveStoreOrderImportPriceVarianceSummaryOrderExpression(string? sortBy)
        {
            return (sortBy ?? "absoluteVarianceAmount").Trim().ToLowerInvariant() switch
            {
                "productcode" => "ProductCode",
                "itemnumber" => "ItemNumber",
                "suppliercode" => "SupplierCode",
                "suppliername" => "SupplierName",
                "domesticprice" => "DomesticPrice",
                "warehouseimportprice" => "WarehouseImportPrice",
                "unitvolume" => "UnitVolume",
                "packingquantity" => "PackingQuantity",
                "firstcontainerimportprice" => "FirstContainerImportPrice",
                "allocquantitytotal" => "AllocQuantityTotal",
                "originalimportamounttotal" => "OriginalImportAmountTotal",
                "baselineimportamounttotal" => "BaselineImportAmountTotal",
                "varianceamounttotal" => "VarianceAmountTotal",
                "detailcount" => "DetailCount",
                "firstcontainerdate" => "FirstContainerDate",
                _ => "ABS(VarianceAmountTotal)",
            };
        }

        private static string ResolveStoreOrderImportPriceVarianceDetailOrderExpression(string? sortBy)
        {
            return (sortBy ?? "absoluteVarianceAmount").Trim().ToLowerInvariant() switch
            {
                "orderdate" => "OrderDate",
                "orderno" => "OrderNo",
                "storecode" => "StoreCode",
                "productcode" => "ProductCode",
                "itemnumber" => "ItemNumber",
                "orderimportprice" => "OrderImportPrice",
                "firstcontainerimportprice" => "FirstContainerImportPrice",
                "allocquantity" => "AllocQuantity",
                "originalimportamount" => "OriginalImportAmount",
                "baselineimportamount" => "BaselineImportAmount",
                "varianceamount" => "VarianceAmount",
                "firstcontainerdate" => "FirstContainerDate",
                _ => "ABS(VarianceAmount)",
            };
        }

        internal static string BuildStoreOrderImportPriceVarianceDetailOrderBy(
            string? sortBy,
            bool sortDescending
        )
        {
            var orderDirection = sortDescending ? "DESC" : "ASC";
            var terms = new List<(string Expression, string Direction)>
            {
                (ResolveStoreOrderImportPriceVarianceDetailOrderExpression(sortBy), orderDirection),
            };

            void AddFallback(string expression, string direction)
            {
                if (terms.Any(term => string.Equals(term.Expression, expression, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                terms.Add((expression, direction));
            }

            // SQL Server 不允许 ORDER BY 中重复同一列；二级稳定排序必须按主排序列去重。
            AddFallback("OrderDate", "DESC");
            AddFallback("OrderNo", "DESC");
            AddFallback("DetailGUID", "ASC");

            return string.Join(", ", terms.Select(term => $"{term.Expression} {term.Direction}"));
        }

        private static StoreOrderImportPriceVarianceResultDto CreateEmptyImportPriceVarianceResult(
            int pageNumber,
            int pageSize
        )
        {
            return new StoreOrderImportPriceVarianceResultDto
            {
                Items = new List<StoreOrderImportPriceVarianceItemDto>(),
                Total = 0,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Summary = new StoreOrderImportPriceVarianceSummaryDto(),
                SupplierSummaries = new List<StoreOrderImportPriceVarianceSupplierSummaryDto>(),
            };
        }

        private static StoreOrderImportPriceVarianceDetailResultDto CreateEmptyImportPriceVarianceDetailResult(
            int pageNumber,
            int pageSize
        )
        {
            return new StoreOrderImportPriceVarianceDetailResultDto
            {
                Items = new List<StoreOrderImportPriceVarianceDetailItemDto>(),
                Total = 0,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Summary = new StoreOrderImportPriceVarianceSummaryDto(),
            };
        }

        private static ISugarQueryable<WareHouseOrder> ApplyOrderListMainColumnFilters(
            ISugarQueryable<WareHouseOrder> q,
            StoreOrderListColumnFilterDto? filters
        )
        {
            if (filters == null)
            {
                return q;
            }

            if (!string.IsNullOrWhiteSpace(filters.OrderNo))
            {
                var keyword = filters.OrderNo.Trim();
                q = q.Where(o => o.OrderNo != null && o.OrderNo.Contains(keyword));
            }

            if (filters.OutboundDateStart.HasValue)
            {
                var start = filters.OutboundDateStart.Value.Date;
                q = q.Where(o => o.OutboundDate >= start);
            }
            if (filters.OutboundDateEnd.HasValue)
            {
                var end = filters.OutboundDateEnd.Value.Date.AddDays(1).AddMilliseconds(-1);
                q = q.Where(o => o.OutboundDate <= end);
            }

            if (!string.IsNullOrWhiteSpace(filters.Remarks))
            {
                var keyword = filters.Remarks.Trim();
                q = q.Where(o => o.Remarks != null && o.Remarks.Contains(keyword));
            }

            if (filters.CreatedAtStart.HasValue)
            {
                var start = filters.CreatedAtStart.Value.Date;
                q = q.Where(o => o.CreatedAt >= start);
            }
            if (filters.CreatedAtEnd.HasValue)
            {
                var end = filters.CreatedAtEnd.Value.Date.AddDays(1).AddMilliseconds(-1);
                q = q.Where(o => o.CreatedAt <= end);
            }

            if (!string.IsNullOrWhiteSpace(filters.UpdatedBy))
            {
                var keyword = filters.UpdatedBy.Trim();
                q = q.Where(o => o.UpdatedBy != null && o.UpdatedBy.Contains(keyword));
            }

            if (filters.UpdatedAtStart.HasValue)
            {
                var start = filters.UpdatedAtStart.Value.Date;
                q = q.Where(o => o.UpdatedAt >= start);
            }
            if (filters.UpdatedAtEnd.HasValue)
            {
                var end = filters.UpdatedAtEnd.Value.Date.AddDays(1).AddMilliseconds(-1);
                q = q.Where(o => o.UpdatedAt <= end);
            }

            return q;
        }

        private static bool ShouldUseAggregateOrderListPipeline(
            StoreOrderListColumnFilterDto? filters,
            string sortBy
        )
        {
            return IsAggregateOrderListSortField(sortBy) || HasAggregateOrderListColumnFilters(filters);
        }

        private static bool HasAggregateOrderListColumnFilters(StoreOrderListColumnFilterDto? filters)
        {
            if (filters == null)
            {
                return false;
            }

            return filters.TotalQuantityMin.HasValue
                || filters.TotalQuantityMax.HasValue
                || filters.TotalOrderAmountMin.HasValue
                || filters.TotalOrderAmountMax.HasValue
                || filters.TotalOrderVolumeMin.HasValue
                || filters.TotalOrderVolumeMax.HasValue
                || filters.TotalAllocVolumeMin.HasValue
                || filters.TotalAllocVolumeMax.HasValue
                || filters.TotalAllocQuantityMin.HasValue
                || filters.TotalAllocQuantityMax.HasValue
                || filters.ImportTotalAmountMin.HasValue
                || filters.ImportTotalAmountMax.HasValue;
        }

        private static bool HasVolumeOrderListColumnFilters(StoreOrderListColumnFilterDto? filters)
        {
            if (filters == null)
            {
                return false;
            }

            return filters.TotalOrderVolumeMin.HasValue
                || filters.TotalOrderVolumeMax.HasValue
                || filters.TotalAllocVolumeMin.HasValue
                || filters.TotalAllocVolumeMax.HasValue;
        }

        private static bool IsAggregateOrderListSortField(string sortBy)
        {
            return sortBy
                is "totalorderamount"
                    or "totalquantity"
                    or "totalallocquantity"
                    or "importtotalamount";
        }

        private async Task<PagedListReactDto<StoreOrderListItemDto>> BuildAggregateSortedOrderListAsync(
            ISugarQueryable<WareHouseOrder> q,
            StoreOrderListFilterDto filter,
            string sortBy,
            OrderByType orderType
        )
        {
            var orders = await q.ToListAsync();
            var items = await BuildOrderListItemsFromOrdersAsync(orders);
            var needsVolumeFilters = HasVolumeOrderListColumnFilters(filter.ColumnFilters);
            if (needsVolumeFilters)
            {
                await FillOrderListVolumesAsync(items);
            }

            // 这些排序字段来自订单明细汇总，不直接存在于主表。
            // 统一在 C# 内存中聚合和排序，避免手写 SQL 依赖数据库表名或别名。
            items = ApplyAggregateOrderListColumnFilters(items, filter.ColumnFilters).ToList();
            var total = items.Count;
            items = SortAggregateOrderListItems(items, sortBy, orderType)
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToList();

            if (!needsVolumeFilters)
            {
                await FillOrderListVolumesAsync(items);
            }

            return new PagedListReactDto<StoreOrderListItemDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        private static IEnumerable<StoreOrderListItemDto> SortAggregateOrderListItems(
            List<StoreOrderListItemDto> items,
            string sortBy,
            OrderByType orderType
        )
        {
            return (sortBy, orderType) switch
            {
                ("totalorderamount", OrderByType.Desc) => items
                    .OrderByDescending(x => x.TotalOrderAmount)
                    .ThenByDescending(x => x.OrderGUID),
                ("totalorderamount", _) => items
                    .OrderBy(x => x.TotalOrderAmount)
                    .ThenBy(x => x.OrderGUID),
                ("totalquantity", OrderByType.Desc) => items
                    .OrderByDescending(x => x.TotalQuantity)
                    .ThenByDescending(x => x.OrderGUID),
                ("totalquantity", _) => items
                    .OrderBy(x => x.TotalQuantity)
                    .ThenBy(x => x.OrderGUID),
                ("totalallocquantity", OrderByType.Desc) => items
                    .OrderByDescending(x => x.TotalAllocQuantity)
                    .ThenByDescending(x => x.OrderGUID),
                ("totalallocquantity", _) => items
                    .OrderBy(x => x.TotalAllocQuantity)
                    .ThenBy(x => x.OrderGUID),
                ("importtotalamount", OrderByType.Desc) => items
                    .OrderByDescending(x => x.ImportTotalAmount)
                    .ThenByDescending(x => x.OrderGUID),
                ("importtotalamount", _) => items
                    .OrderBy(x => x.ImportTotalAmount)
                    .ThenBy(x => x.OrderGUID),
                ("orderno", OrderByType.Desc) => items
                    .OrderByDescending(x => x.OrderNo)
                    .ThenByDescending(x => x.OrderGUID),
                ("orderno", _) => items
                    .OrderBy(x => x.OrderNo)
                    .ThenBy(x => x.OrderGUID),
                ("orderdate", OrderByType.Desc) => items
                    .OrderByDescending(x => x.OrderDate)
                    .ThenByDescending(x => x.OrderNo)
                    .ThenByDescending(x => x.OrderGUID),
                ("orderdate", _) => items
                    .OrderBy(x => x.OrderDate)
                    .ThenBy(x => x.OrderNo)
                    .ThenBy(x => x.OrderGUID),
                ("storecode", OrderByType.Desc) => items
                    .OrderByDescending(x => x.StoreCode)
                    .ThenByDescending(x => x.OrderGUID),
                ("storecode", _) => items
                    .OrderBy(x => x.StoreCode)
                    .ThenBy(x => x.OrderGUID),
                ("flowstatus", OrderByType.Desc) => items
                    .OrderByDescending(x => x.FlowStatus)
                    .ThenByDescending(x => x.OrderGUID),
                ("flowstatus", _) => items
                    .OrderBy(x => x.FlowStatus)
                    .ThenBy(x => x.OrderGUID),
                ("remarks", OrderByType.Desc) => items
                    .OrderByDescending(x => x.Remarks)
                    .ThenByDescending(x => x.OrderGUID),
                ("remarks", _) => items
                    .OrderBy(x => x.Remarks)
                    .ThenBy(x => x.OrderGUID),
                _ => items.OrderByDescending(x => x.OrderDate)
                    .ThenByDescending(x => x.OrderNo)
                    .ThenByDescending(x => x.OrderGUID),
            };
        }

        private static IEnumerable<StoreOrderListItemDto> ApplyAggregateOrderListColumnFilters(
            IEnumerable<StoreOrderListItemDto> items,
            StoreOrderListColumnFilterDto? filters
        )
        {
            if (filters == null)
            {
                return items;
            }

            // 聚合字段来自订单明细或体积计算，必须在汇总后过滤，才能保证分页总数正确。
            if (filters.TotalQuantityMin.HasValue)
            {
                items = items.Where(item => item.TotalQuantity >= filters.TotalQuantityMin.Value);
            }
            if (filters.TotalQuantityMax.HasValue)
            {
                items = items.Where(item => item.TotalQuantity <= filters.TotalQuantityMax.Value);
            }
            if (filters.TotalOrderAmountMin.HasValue)
            {
                items = items.Where(item => item.TotalOrderAmount >= filters.TotalOrderAmountMin.Value);
            }
            if (filters.TotalOrderAmountMax.HasValue)
            {
                items = items.Where(item => item.TotalOrderAmount <= filters.TotalOrderAmountMax.Value);
            }
            if (filters.TotalOrderVolumeMin.HasValue)
            {
                items = items.Where(item => item.TotalOrderVolume >= filters.TotalOrderVolumeMin.Value);
            }
            if (filters.TotalOrderVolumeMax.HasValue)
            {
                items = items.Where(item => item.TotalOrderVolume <= filters.TotalOrderVolumeMax.Value);
            }
            if (filters.TotalAllocVolumeMin.HasValue)
            {
                items = items.Where(item => item.TotalAllocVolume >= filters.TotalAllocVolumeMin.Value);
            }
            if (filters.TotalAllocVolumeMax.HasValue)
            {
                items = items.Where(item => item.TotalAllocVolume <= filters.TotalAllocVolumeMax.Value);
            }
            if (filters.TotalAllocQuantityMin.HasValue)
            {
                items = items.Where(item => item.TotalAllocQuantity >= filters.TotalAllocQuantityMin.Value);
            }
            if (filters.TotalAllocQuantityMax.HasValue)
            {
                items = items.Where(item => item.TotalAllocQuantity <= filters.TotalAllocQuantityMax.Value);
            }
            if (filters.ImportTotalAmountMin.HasValue)
            {
                items = items.Where(item => item.ImportTotalAmount >= filters.ImportTotalAmountMin.Value);
            }
            if (filters.ImportTotalAmountMax.HasValue)
            {
                items = items.Where(item => item.ImportTotalAmount <= filters.ImportTotalAmountMax.Value);
            }

            return items;
        }

        private async Task<List<StoreOrderListItemDto>> BuildOrderListItemsFromOrdersAsync(
            List<WareHouseOrder> orders
        )
        {
            var orderGuids = orders.Select(x => x.OrderGUID).Distinct().ToList();
            var storeCodes = orders
                .Select(x => x.StoreCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var stores = storeCodes.Count == 0
                ? new List<Store>()
                : await _db.Queryable<Store>()
                    .Where(s =>
                        (s.StoreCode != null && storeCodes.Contains(s.StoreCode))
                        || (s.StoreGUID != null && storeCodes.Contains(s.StoreGUID))
                    )
                    .ToListAsync();

            var storeNameMap = stores
                .SelectMany(store =>
                    new[]
                    {
                        new { Key = store.StoreCode, store.StoreName },
                        new { Key = store.StoreGUID, store.StoreName },
                    }
                )
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().StoreName, StringComparer.OrdinalIgnoreCase);

            var details = await QueryActiveOrderDetailsByOrderGuidsAsync(orderGuids);

            var totalsMap = details
                .Where(x => !string.IsNullOrWhiteSpace(x.OrderGUID))
                .GroupBy(x => x.OrderGUID!)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        TotalAmount = group.Sum(x => (x.AllocQuantity ?? 0) * (x.ImportPrice ?? 0)),
                        OEMTotalAmount = group.Sum(x => (x.AllocQuantity ?? 0) * (x.OEMAmount ?? 0)),
                        ImportTotalAmount = group.Sum(x => (x.AllocQuantity ?? 0) * (x.ImportPrice ?? 0)),
                        TotalOrderAmount = group.Sum(x => (x.Quantity ?? 0) * (x.ImportPrice ?? 0)),
                        TotalQuantity = (int)(group.Sum(x => x.Quantity) ?? 0),
                        TotalAllocQuantity = (int)(group.Sum(x => x.AllocQuantity) ?? 0),
                    }
                );

            return orders
                .Select(order =>
                {
                    totalsMap.TryGetValue(order.OrderGUID, out var totals);
                    var storeName =
                        !string.IsNullOrWhiteSpace(order.StoreCode)
                        && storeNameMap.TryGetValue(order.StoreCode, out var name)
                            ? name
                            : null;

                    return new StoreOrderListItemDto
                    {
                        OrderGUID = order.OrderGUID,
                        OrderNo = order.OrderNo ?? string.Empty,
                        StoreCode = order.StoreCode,
                        StoreName = storeName,
                        OrderDate = order.OrderDate,
                        OutboundDate = order.OutboundDate,
                        FlowStatus = order.FlowStatus ?? 0,
                        TotalAmount = totals?.TotalAmount ?? 0,
                        OEMTotalAmount = totals?.OEMTotalAmount ?? 0,
                        ImportTotalAmount = totals?.ImportTotalAmount ?? 0,
                        TotalOrderAmount = totals?.TotalOrderAmount ?? 0,
                        TotalQuantity = totals?.TotalQuantity ?? 0,
                        TotalAllocQuantity = totals?.TotalAllocQuantity ?? 0,
                        Remarks = order.Remarks,
                        CreatedAt = order.CreatedAt,
                        CreatedBy = order.CreatedBy,
                        UpdatedAt = order.UpdatedAt,
                        UpdatedBy = order.UpdatedBy,
                    };
                })
                .ToList();
        }

        private async Task<List<WareHouseOrderDetails>> QueryActiveOrderDetailsByOrderGuidsAsync(
            List<string> orderGuids
        )
        {
            var result = new List<WareHouseOrderDetails>();
            if (orderGuids.Count == 0)
            {
                return result;
            }

            // SQL Server 单条 IN 参数有上限；汇总排序要覆盖筛选后的全部订单，所以分块拉取明细。
            foreach (var chunk in orderGuids.Chunk(OrderListAggregateChunkSize))
            {
                var chunkGuids = chunk.ToList();
                var rows = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d =>
                        d.OrderGUID != null
                        && chunkGuids.Contains(d.OrderGUID)
                        && !d.IsDeleted
                    )
                    .ToListAsync();
                result.AddRange(rows);
            }

            return result;
        }

        private async Task FillOrderListVolumesAsync(List<StoreOrderListItemDto> items)
        {
            var orderGuids = items.Select(x => x.OrderGUID).Distinct().ToList();
            if (orderGuids.Count == 0)
            {
                return;
            }

            var volumeRows = await _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<WarehouseProduct>((d, wp) => d.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((d, wp, dp) => wp.ProductCode == dp.ProductCode)
                .Where((d, wp, dp) =>
                    d.OrderGUID != null && orderGuids.Contains(d.OrderGUID) && !d.IsDeleted
                )
                .Select(
                    (d, wp, dp) =>
                        new
                        {
                            d.OrderGUID,
                            UnitVolume = (dp.PackingQuantity > 0)
                                ? (dp.UnitVolume / dp.PackingQuantity)
                                : dp.UnitVolume,
                            d.Quantity,
                            d.AllocQuantity,
                        }
                )
                .ToListAsync();

            var volumeMap = volumeRows
                .Where(x => !string.IsNullOrWhiteSpace(x.OrderGUID))
                .GroupBy(x => x.OrderGUID)
                .ToDictionary(
                    group => group.Key!,
                    group => new
                    {
                        TotalOrderVolume = group.Sum(x => (x.UnitVolume ?? 0) * (x.Quantity ?? 0)),
                        TotalAllocVolume = group.Sum(x =>
                            (x.UnitVolume ?? 0) * (x.AllocQuantity ?? 0)
                        ),
                    }
                );

            foreach (var item in items)
            {
                if (volumeMap.TryGetValue(item.OrderGUID, out var totals))
                {
                    item.TotalOrderVolume = totals.TotalOrderVolume;
                    item.TotalAllocVolume = totals.TotalAllocVolume;
                }
            }
        }

        public async Task<ApiResponse<StoreOrderDetailDto?>> GetOrderDetailAsync(
            string orderGuid,
            StoreOrderDetailQueryDto? query = null
        )
        {
            return await GetOrderDetailCoreAsync(orderGuid, query, loadAllItems: false);
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> GetOrderDetailFullAsync(string orderGuid)
        {
            var result = await GetOrderDetailCoreAsync(orderGuid, null, loadAllItems: true);
            return new ApiResponse<StoreOrderCartDto?>
            {
                Success = result.Success,
                Data = result.Data == null
                    ? null
                    : new StoreOrderCartDto
                    {
                        OrderGUID = result.Data.OrderGUID,
                        OrderNo = result.Data.OrderNo,
                        StoreCode = result.Data.StoreCode,
                        StoreName = result.Data.StoreName,
                        TotalAmount = result.Data.TotalAmount,
                        TotalQuantity = result.Data.TotalQuantity,
                        TotalImportAmount = result.Data.TotalImportAmount,
                        TotalAllocatedImportAmount = result.Data.TotalAllocatedImportAmount,
                        TotalVolume = result.Data.TotalVolume,
                        TotalOrderVolume = result.Data.TotalOrderVolume,
                        TotalAllocVolume = result.Data.TotalAllocVolume,
                        ShippingFee = result.Data.ShippingFee,
                        Remarks = result.Data.Remarks,
                        StoreAddress = result.Data.StoreAddress,
                        StoreContactEmail = result.Data.StoreContactEmail,
                        OrderDate = result.Data.OrderDate,
                        OutboundDate = result.Data.OutboundDate,
                        TotalAllocQuantity = result.Data.TotalAllocQuantity,
                        TotalSKU = result.Data.TotalSKU,
                        FlowStatus = result.Data.FlowStatus,
                        InvoiceEmailSentInfo = result.Data.InvoiceEmailSentInfo,
                        Items = result.Data.Items,
                    },
                Message = result.Message,
            };
        }

        public async Task<ApiResponse<StoreOrderStoreContactDto>> UpdateStoreContactAsync(
            UpdateStoreOrderStoreContactDto request
        )
        {
            var normalizedOrderGuid = request.OrderGUID.Trim();
            var normalizedStoreCode = request.StoreCode.Trim();
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(o => o.OrderGUID == normalizedOrderGuid && !o.IsDeleted)
                .FirstAsync();

            if (order == null)
            {
                return ApiResponse<StoreOrderStoreContactDto>.Error(
                    "订单不存在",
                    "STORE_ORDER_NOT_FOUND"
                );
            }

            var store = await GetStoreByCodeOrGuidAsync(normalizedStoreCode);
            if (store == null)
            {
                return ApiResponse<StoreOrderStoreContactDto>.Error(
                    "分店不存在",
                    "STORE_NOT_FOUND"
                );
            }

            if (!DoesOrderMatchStore(order, store))
            {
                return ApiResponse<StoreOrderStoreContactDto>.Error(
                    "订单与分店不匹配",
                    "STORE_ORDER_STORE_MISMATCH"
                );
            }

            // 前端未传字段时保留旧值；传空字符串时允许清空对应分店信息。
            store.Address = request.Address == null ? store.Address : TrimLen(request.Address, 500);
            store.ContactEmail = request.ContactEmail == null
                ? store.ContactEmail
                : NormalizeOptionalEmail(request.ContactEmail);
            store.UpdatedAt = DateTime.UtcNow;
            store.UpdatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

            await _db.Updateable(store)
                .UpdateColumns(s => new
                {
                    s.Address,
                    s.ContactEmail,
                    s.UpdatedAt,
                    s.UpdatedBy,
                })
                .ExecuteCommandAsync();

            return ApiResponse<StoreOrderStoreContactDto>.OK(
                new StoreOrderStoreContactDto
                {
                    OrderGUID = normalizedOrderGuid,
                    StoreCode = store.StoreCode,
                    Address = store.Address,
                    ContactEmail = store.ContactEmail,
                },
                "更新分店联系信息成功"
            );
        }

        public async Task<ApiResponse<List<string>>> GetOrderDetailProductCodesAsync(string orderGuid)
        {
            var accessibleStoreCodes = await GetAccessibleStoreCodesAsync();
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                .FirstAsync();

            if (order == null)
            {
                return new ApiResponse<List<string>>
                {
                    Success = false,
                    Message = "Order not found",
                    Data = new List<string>(),
                };
            }

            if (
                accessibleStoreCodes != null
                && !string.IsNullOrWhiteSpace(order.StoreCode)
                && !accessibleStoreCodes.Contains(order.StoreCode, StringComparer.OrdinalIgnoreCase)
            )
            {
                return new ApiResponse<List<string>>
                {
                    Success = false,
                    Message = "You do not have access to this order",
                    Data = new List<string>(),
                };
            }

            // 只读取商品编码，避免分页详情页为了跨页去重再次加载完整明细。
            var productCodes = await _db.Queryable<WareHouseOrderDetails>()
                .Where(d => d.OrderGUID == orderGuid && !d.IsDeleted && d.ProductCode != null)
                .Select(d => d.ProductCode!)
                .Distinct()
                .ToListAsync();

            return new ApiResponse<List<string>>
            {
                Success = true,
                Data = productCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };
        }

        private async Task<ApiResponse<StoreOrderDetailDto?>> GetOrderDetailCoreAsync(
            string orderGuid,
            StoreOrderDetailQueryDto? query,
            bool loadAllItems
        )
        {
            var normalizedQuery = NormalizeStoreOrderDetailQuery(query);
            var accessibleStoreCodes = await GetAccessibleStoreCodesAsync();
            var order = await _db.Queryable<WareHouseOrder>()
                .LeftJoin<Store>(
                    (o, s) => o.StoreCode == s.StoreCode || o.StoreCode == s.StoreGUID
                )
                .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                .Select(
                    (o, s) =>
                        new
                        {
                            Order = o,
                            // WarehouseStaff 不能加载完整分店列表，明细接口直接带出当前分店名称。
                            StoreName = s.StoreName,
                            StoreAddress = s.Address,
                            StoreContactEmail = s.ContactEmail,
                        }
                )
                .FirstAsync();

            if (order == null)
            {
                return new ApiResponse<StoreOrderDetailDto?>
                {
                    Success = false,
                    Message = "Order not found",
                };
            }

            // 1. 获取基本明细
            if (
                accessibleStoreCodes != null
                && !string.IsNullOrWhiteSpace(order.Order.StoreCode)
                && !accessibleStoreCodes.Contains(
                    order.Order.StoreCode,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                return new ApiResponse<StoreOrderDetailDto?>
                {
                    Success = false,
                    Message = "You do not have access to this order",
                };
            }

            var detailQuery = _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                .LeftJoin<WarehouseProduct>((d, p, wp) => d.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((d, p, wp, dp) => wp.ProductCode == dp.ProductCode)
                .Where(d => d.OrderGUID == order.Order.OrderGUID && !d.IsDeleted);

            var keyword = normalizedQuery.Keyword?.Trim();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) =>
                        (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                        || (p.ProductName != null && p.ProductName.Contains(keyword))
                        || (p.Barcode != null && p.Barcode.Contains(keyword))
                        || (d.ProductCode != null && d.ProductCode.Contains(keyword))
                );
            }

            var itemNumberFilter = normalizedQuery.ItemNumber;
            if (!string.IsNullOrWhiteSpace(itemNumberFilter))
            {
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => p.ItemNumber != null && p.ItemNumber.Contains(itemNumberFilter)
                );
            }

            var productNameFilter = normalizedQuery.ProductName;
            if (!string.IsNullOrWhiteSpace(productNameFilter))
            {
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => p.ProductName != null && p.ProductName.Contains(productNameFilter)
                );
            }

            var barcodeFilter = normalizedQuery.Barcode;
            if (!string.IsNullOrWhiteSpace(barcodeFilter))
            {
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => p.Barcode != null && p.Barcode.Contains(barcodeFilter)
                );
            }

            var locationCodeFilter = normalizedQuery.LocationCode;
            if (!string.IsNullOrWhiteSpace(locationCodeFilter))
            {
                // 货位表不在明细主联表里；用 SQL 子查询过滤，避免宽泛货位条件生成过大的 IN 列表。
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) =>
                        d.ProductCode != null
                        && SqlFunc.Subqueryable<ProductLocation>()
                            .InnerJoin<Location>((pl, l) => pl.LocationGuid == l.LocationGuid)
                            .Where(
                                (pl, l) =>
                                    pl.ProductCode == d.ProductCode
                                    && !pl.IsDeleted
                                    && !l.IsDeleted
                                    && l.LocationType == 1
                                    && l.LocationCode != null
                                    && l.LocationCode.Contains(locationCodeFilter)
                            )
                            .Any()
                );
            }

            if (normalizedQuery.QuantityMin.HasValue)
            {
                var min = normalizedQuery.QuantityMin.Value;
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => d.Quantity != null && d.Quantity >= min
                );
            }

            if (normalizedQuery.QuantityMax.HasValue)
            {
                var max = normalizedQuery.QuantityMax.Value;
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => d.Quantity != null && d.Quantity <= max
                );
            }

            if (normalizedQuery.AllocQuantityMin.HasValue)
            {
                var min = normalizedQuery.AllocQuantityMin.Value;
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => d.AllocQuantity != null && d.AllocQuantity >= min
                );
            }

            if (normalizedQuery.AllocQuantityMax.HasValue)
            {
                var max = normalizedQuery.AllocQuantityMax.Value;
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => d.AllocQuantity != null && d.AllocQuantity <= max
                );
            }

            if (normalizedQuery.ImportPriceMin.HasValue)
            {
                var min = normalizedQuery.ImportPriceMin.Value;
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) =>
                        (d.ImportPrice != null && d.ImportPrice >= min)
                        || (
                            d.ImportPrice == null
                            && wp.ImportPrice != null
                            && wp.ImportPrice >= min
                        )
                );
            }

            if (normalizedQuery.ImportPriceMax.HasValue)
            {
                var max = normalizedQuery.ImportPriceMax.Value;
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) =>
                        (d.ImportPrice != null && d.ImportPrice <= max)
                        || (
                            d.ImportPrice == null
                            && wp.ImportPrice != null
                            && wp.ImportPrice <= max
                        )
                );
            }

            if (normalizedQuery.IsActive.HasValue)
            {
                var isActive = normalizedQuery.IsActive.Value;
                // SqlSugar 在 SQLite 下布尔等值翻译不稳定；这里沿用现有状态筛选的布尔表达式口径。
                detailQuery = isActive
                    ? detailQuery.Where((d, p, wp, dp) => wp.IsActive)
                    : detailQuery.Where((d, p, wp, dp) => !wp.IsActive);
            }

            var statFilter = normalizedQuery.StatFilter?.Trim().ToLowerInvariant();
            if (statFilter == "orderednotshipped")
            {
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => (d.Quantity ?? 0) > 0 && (d.AllocQuantity ?? 0) == 0
                );
            }
            else if (statFilter == "shippedwithoutorder")
            {
                detailQuery = detailQuery.Where(
                    (d, p, wp, dp) => (d.Quantity ?? 0) <= 0 && (d.AllocQuantity ?? 0) > 0
                );
            }
            else if (statFilter is "active" or "1" or "true")
            {
                // 订单明细状态展示仓库商品上下架口径，筛选必须和返回字段保持一致。
                detailQuery = detailQuery.Where((d, p, wp, dp) => wp.IsActive);
            }
            else if (statFilter is "inactive" or "0" or "false")
            {
                detailQuery = detailQuery.Where((d, p, wp, dp) => !wp.IsActive);
            }

            var itemsTotal = await detailQuery.CountAsync();
            var sortBy = (normalizedQuery.SortBy ?? string.Empty).Trim().ToLowerInvariant();
            var isLocationCodeSort = sortBy == "locationcode";
            var orderType = normalizedQuery.SortDescending ? OrderByType.Desc : OrderByType.Asc;
            if (!isLocationCodeSort)
            {
                detailQuery = sortBy switch
                {
                    "itemnumber" => detailQuery.OrderBy((d, p, wp, dp) => p.ItemNumber, orderType),
                    "productcode" => detailQuery.OrderBy((d, p, wp, dp) => d.ProductCode, orderType),
                    "barcode" => detailQuery.OrderBy((d, p, wp, dp) => p.Barcode, orderType),
                    "productname" => detailQuery.OrderBy((d, p, wp, dp) => p.ProductName, orderType),
                    "quantity" => detailQuery.OrderBy((d, p, wp, dp) => d.Quantity, orderType),
                    "allocquantity" => detailQuery.OrderBy((d, p, wp, dp) => d.AllocQuantity, orderType),
                    "price" => detailQuery.OrderBy((d, p, wp, dp) => d.OEMPrice, orderType),
                    "amount" => detailQuery.OrderBy((d, p, wp, dp) => d.OEMAmount, orderType),
                    "importprice" => detailQuery.OrderBy(
                        (d, p, wp, dp) => d.ImportPrice ?? wp.ImportPrice,
                        orderType
                    ),
                    "importamount" => detailQuery.OrderBy(
                        // 订货金额排序跟 ImportAmount 返回字段一致，历史持久金额优先。
                        (d, p, wp, dp) =>
                            d.ImportAmount
                            ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0)),
                        orderType
                    ),
                    "allocatedimportamount" => detailQuery.OrderBy(
                        // 发货/发票金额排序用独立字段，避免和订货金额混用。
                        (d, p, wp, dp) => (d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.AllocQuantity ?? 0),
                        orderType
                    ),
                    "isactive" => detailQuery.OrderBy((d, p, wp, dp) => wp.IsActive, orderType),
                    _ => detailQuery.OrderBy((d, p, wp, dp) => p.ItemNumber, orderType),
                };
                detailQuery = detailQuery.OrderBy((d, p, wp, dp) => d.DetailGUID, OrderByType.Asc);
            }

            // SqlSugar 多表查询在 Skip/Take 后会丢失泛型联表形态，先投影再分页更稳定。
            var pageQuery = detailQuery.Select(
                    (d, p, wp, dp) =>
                        new StoreOrderCartItemDto
                        {
                            DetailGUID = d.DetailGUID,
                            ProductCode = d.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            Grade = SqlFunc.Subqueryable<ProductGrade>()
                                .Where(pg => pg.ProductCode == d.ProductCode && !pg.IsDeleted)
                                .OrderBy(pg => pg.Grade)
                                .Select(pg => pg.Grade),
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            Price = d.OEMPrice ?? 0,
                            Quantity = d.Quantity ?? 0,
                            AllocQuantity = d.AllocQuantity,
                            Amount = d.OEMAmount ?? 0,
                            ImportPrice = d.ImportPrice ?? (wp.ImportPrice ?? 0),
                            // ImportAmount 保持订货金额，发票金额走 AllocatedImportAmount。
                            ImportAmount =
                                d.ImportAmount
                                ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0)),
                            AllocatedImportAmount = (d.ImportPrice ?? (wp.ImportPrice ?? 0))
                                * (d.AllocQuantity ?? 0),
                            Volume =
                                (dp.PackingQuantity > 0)
                                    ? (dp.UnitVolume / dp.PackingQuantity)
                                    : dp.UnitVolume,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            // 前端“上架/下架”列对应仓库表 WarehouseProduct.IsActive。
                            IsActive = wp.IsActive,
                            RRP = p.RetailPrice,
                        }
                );

            List<StoreOrderCartItemDto> pageDetails;
            if (isLocationCodeSort)
            {
                var locationSortQuery = _db.Queryable<ProductLocation>()
                    .InnerJoin<Location>((pl, l) => pl.LocationGuid == l.LocationGuid)
                    .Where(
                        (pl, l) =>
                            pl.ProductCode != null
                            && !pl.IsDeleted
                            && !l.IsDeleted
                            && l.LocationType == 1
                            && l.LocationCode != null
                    )
                    .GroupBy((pl, l) => pl.ProductCode)
                    .Select(
                        (pl, l) =>
                            new StoreOrderDetailLocationSortRow
                            {
                                ProductCode = pl.ProductCode ?? string.Empty,
                                // 多货位商品以最小货位编码参与排序，页内展示仍由 FillLocationCodesAsync 补完整列表。
                                LocationSortCode = SqlFunc.AggregateMin(l.LocationCode),
                            }
                    )
                    .MergeTable();

                var locationPageQuery = detailQuery
                    .LeftJoin<StoreOrderDetailLocationSortRow>(
                        locationSortQuery,
                        (d, p, wp, dp, ls) => d.ProductCode == ls.ProductCode
                    )
                    // 空货位固定排在前面；非空货位按请求方向排序，保证分页发生在数据库端。
                    .OrderBy(
                        (d, p, wp, dp, ls) =>
                            SqlFunc.IIF(
                                ls.LocationSortCode == null || ls.LocationSortCode == string.Empty,
                                0,
                                1
                            ),
                        OrderByType.Asc
                    )
                    .OrderBy(
                        (d, p, wp, dp, ls) => ls.LocationSortCode,
                        normalizedQuery.SortDescending ? OrderByType.Desc : OrderByType.Asc
                    )
                    .OrderBy((d, p, wp, dp, ls) => p.ItemNumber, OrderByType.Asc)
                    .OrderBy((d, p, wp, dp, ls) => d.DetailGUID, OrderByType.Asc)
                    .Select(
                        (d, p, wp, dp, ls) =>
                            new StoreOrderCartItemDto
                            {
                                DetailGUID = d.DetailGUID,
                                ProductCode = d.ProductCode ?? string.Empty,
                                ItemNumber = p.ItemNumber,
                                Barcode = p.Barcode,
                                Grade = SqlFunc.Subqueryable<ProductGrade>()
                                    .Where(pg => pg.ProductCode == d.ProductCode && !pg.IsDeleted)
                                    .OrderBy(pg => pg.Grade)
                                    .Select(pg => pg.Grade),
                                ProductName = p.ProductName,
                                ProductImage = p.ProductImage,
                                Price = d.OEMPrice ?? 0,
                                Quantity = d.Quantity ?? 0,
                                AllocQuantity = d.AllocQuantity,
                                Amount = d.OEMAmount ?? 0,
                                ImportPrice = d.ImportPrice ?? (wp.ImportPrice ?? 0),
                                // ImportAmount 保持订货金额，发票金额走 AllocatedImportAmount。
                                ImportAmount =
                                    d.ImportAmount
                                    ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0)),
                                AllocatedImportAmount = (d.ImportPrice ?? (wp.ImportPrice ?? 0))
                                    * (d.AllocQuantity ?? 0),
                                Volume =
                                    (dp.PackingQuantity > 0)
                                        ? (dp.UnitVolume / dp.PackingQuantity)
                                        : dp.UnitVolume,
                                MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                                // 前端“上架/下架”列对应仓库表 WarehouseProduct.IsActive。
                                IsActive = wp.IsActive,
                                RRP = p.RetailPrice,
                            }
                    );

                if (!loadAllItems)
                {
                    locationPageQuery = locationPageQuery
                        .Skip((normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize)
                        .Take(normalizedQuery.PageSize);
                }

                pageDetails = await locationPageQuery.ToListAsync();
                await FillLocationCodesAsync(pageDetails);
            }
            else
            {
                if (!loadAllItems)
                {
                    pageQuery = pageQuery
                        .Skip((normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize)
                        .Take(normalizedQuery.PageSize);
                }

                pageDetails = await pageQuery.ToListAsync();
                await FillLocationCodesAsync(pageDetails);
            }

            FillVolumeFields(pageDetails);

            // 汇总永远按整单计算，不能被当前页、关键词或状态筛选影响；用数据库聚合避免翻页时拉取全量明细。
            var summary = await _db.Queryable<WareHouseOrderDetails>()
                .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                .LeftJoin<WarehouseProduct>((d, p, wp) => d.ProductCode == wp.ProductCode)
                .LeftJoin<DomesticProduct>((d, p, wp, dp) => wp.ProductCode == dp.ProductCode)
                .Where(d => d.OrderGUID == order.Order.OrderGUID && !d.IsDeleted)
                .Select(
                    (d, p, wp, dp) =>
                        new
                        {
                            TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0),
                            TotalAllocQuantity = SqlFunc.AggregateSum(d.AllocQuantity ?? 0),
                            TotalSKU = SqlFunc.AggregateDistinctCount(d.ProductCode),
                            TotalImportAmount = SqlFunc.AggregateSum(
                                // 订货金额保留历史持久金额，缺失时按订货数量兜底。
                                d.ImportAmount
                                    ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0))
                            ),
                            TotalAllocatedImportAmount = SqlFunc.AggregateSum(
                                // 发货/发票金额按发货数量重算，保证发票 Sub-Total 等于明细小计合计。
                                (d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.AllocQuantity ?? 0)
                            ),
                            TotalOrderVolume = SqlFunc.AggregateSum(
                                (
                                    (dp.PackingQuantity > 0)
                                        ? (dp.UnitVolume / dp.PackingQuantity)
                                        : dp.UnitVolume
                                )
                                    * (d.Quantity ?? 0)
                            ),
                            TotalAllocVolume = SqlFunc.AggregateSum(
                                (
                                    (dp.PackingQuantity > 0)
                                        ? (dp.UnitVolume / dp.PackingQuantity)
                                        : dp.UnitVolume
                                )
                                    * (d.AllocQuantity ?? 0)
                            ),
                            OrderedNotShippedCount = SqlFunc.AggregateCount(
                                SqlFunc.IIF(
                                    (d.Quantity ?? 0) > 0 && (d.AllocQuantity ?? 0) == 0,
                                    d.DetailGUID,
                                    null
                                )
                            ),
                            ShippedWithoutOrderCount = SqlFunc.AggregateCount(
                                SqlFunc.IIF(
                                    (d.Quantity ?? 0) <= 0 && (d.AllocQuantity ?? 0) > 0,
                                    d.DetailGUID,
                                    null
                                )
                            ),
                        }
                )
                .FirstAsync();

            // 关键位置：详情接口始终带回最近一次成功发送的发票邮件记录，避免前端重复推导发送状态。
            var latestInvoiceEmailSentRecord = await _db.Queryable<StoreOrderInvoiceEmailSendRecord>()
                .Where(r => r.StoreOrderUuid == order.Order.OrderGUID)
                .OrderBy(r => r.SentAtUtc, OrderByType.Desc)
                .OrderBy(r => r.CreatedAtUtc, OrderByType.Desc)
                .FirstAsync();

            var dto = new StoreOrderDetailDto
            {
                OrderGUID = order.Order.OrderGUID,
                OrderNo = order.Order.OrderNo,
                StoreCode = order.Order.StoreCode,
                StoreName = order.StoreName,
                OrderDate = order.Order.OrderDate,
                OutboundDate = order.Order.OutboundDate,
                TotalAmount = order.Order.OEMTotalAmount ?? 0,
                TotalQuantity = (int)(summary?.TotalQuantity ?? 0),
                TotalAllocQuantity = (int)(summary?.TotalAllocQuantity ?? 0),
                TotalSKU = summary?.TotalSKU ?? 0,
                TotalImportAmount = summary?.TotalImportAmount ?? 0,
                TotalAllocatedImportAmount = summary?.TotalAllocatedImportAmount ?? 0,
                TotalVolume = summary?.TotalOrderVolume ?? 0,
                TotalOrderVolume = summary?.TotalOrderVolume ?? 0,
                TotalAllocVolume = summary?.TotalAllocVolume ?? 0,
                Remarks = order.Order.Remarks,
                StoreAddress = order.StoreAddress,
                StoreContactEmail = order.StoreContactEmail,
                ShippingFee = order.Order.ShippingFee,
                FlowStatus = order.Order.FlowStatus,
                InvoiceEmailSentInfo = latestInvoiceEmailSentRecord == null
                    ? new StoreOrderInvoiceEmailSentInfoDto()
                    : new StoreOrderInvoiceEmailSentInfoDto
                    {
                        HasSent = true,
                        SentAt = DateTime.SpecifyKind(
                            latestInvoiceEmailSentRecord.SentAtUtc,
                            DateTimeKind.Utc
                        ),
                        ToEmail = latestInvoiceEmailSentRecord.ToEmail,
                        JobId = latestInvoiceEmailSentRecord.JobId,
                    },
                Items = pageDetails,
                Total = loadAllItems ? pageDetails.Count : itemsTotal,
                ItemsTotal = loadAllItems ? pageDetails.Count : itemsTotal,
                PageNumber = loadAllItems ? 1 : normalizedQuery.PageNumber,
                PageSize = loadAllItems ? pageDetails.Count : normalizedQuery.PageSize,
                OrderedNotShippedCount = summary?.OrderedNotShippedCount ?? 0,
                ShippedWithoutOrderCount = summary?.ShippedWithoutOrderCount ?? 0,
            };

            return new ApiResponse<StoreOrderDetailDto?> { Success = true, Data = dto };
        }

        private static StoreOrderDetailQueryDto NormalizeStoreOrderDetailQuery(
            StoreOrderDetailQueryDto? query
        )
        {
            var pageNumber = Math.Max(
                StoreOrderDetailQueryDto.DefaultPageNumber,
                query?.PageNumber ?? StoreOrderDetailQueryDto.DefaultPageNumber
            );
            var requestedPageSize = query?.PageSize ?? StoreOrderDetailQueryDto.DefaultPageSize;
            var pageSize = Math.Clamp(
                requestedPageSize <= 0
                    ? StoreOrderDetailQueryDto.DefaultPageSize
                    : requestedPageSize,
                1,
                StoreOrderDetailQueryDto.MaxPageSize
            );

            return new StoreOrderDetailQueryDto
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                Keyword = NormalizeStoreOrderDetailTextFilter(query?.Keyword),
                StatFilter = NormalizeStoreOrderDetailTextFilter(query?.StatFilter),
                ItemNumber = NormalizeStoreOrderDetailTextFilter(query?.ItemNumber),
                ProductName = NormalizeStoreOrderDetailTextFilter(query?.ProductName),
                Barcode = NormalizeStoreOrderDetailTextFilter(query?.Barcode),
                LocationCode = NormalizeStoreOrderDetailTextFilter(query?.LocationCode),
                QuantityMin = query?.QuantityMin,
                QuantityMax = query?.QuantityMax,
                AllocQuantityMin = query?.AllocQuantityMin,
                AllocQuantityMax = query?.AllocQuantityMax,
                ImportPriceMin = query?.ImportPriceMin,
                ImportPriceMax = query?.ImportPriceMax,
                IsActive = query?.IsActive,
                SortBy = NormalizeStoreOrderDetailTextFilter(query?.SortBy),
                SortDescending = query?.SortDescending ?? false,
            };
        }

        private static string? NormalizeStoreOrderDetailTextFilter(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private async Task FillLocationCodesAsync(List<StoreOrderCartItemDto> items)
        {
            var productCodes = items
                .Select(x => x.ProductCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!productCodes.Any())
            {
                return;
            }

            var locations = await _db.Queryable<ProductLocation>()
                .InnerJoin<Location>((pl, l) => pl.LocationGuid == l.LocationGuid)
                .Where(
                    (pl, l) =>
                        pl.ProductCode != null
                        && productCodes.Contains(pl.ProductCode)
                        && !pl.IsDeleted
                        && !l.IsDeleted
                        && l.LocationType == 1
                        && l.LocationCode != null
                )
                .Select((pl, l) => new { ProductCode = pl.ProductCode!, LocationCode = l.LocationCode! })
                .ToListAsync();

            // 将货位一次性分组到字典，避免每一行明细都扫描完整货位列表。
            var locationMap = locations
                .GroupBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(
                        ", ",
                        group.Select(x => x.LocationCode)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x)
                    ),
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var item in items)
            {
                if (locationMap.TryGetValue(item.ProductCode, out var locationCode))
                {
                    item.LocationCode = locationCode;
                }
            }
        }

        private static void FillVolumeFields(List<StoreOrderCartItemDto> items)
        {
            foreach (var item in items)
            {
                if (!item.Volume.HasValue)
                {
                    continue;
                }

                item.OrderVolume = CalculateVolume(item.Volume, item.Quantity);
                item.AllocVolume = CalculateVolume(item.Volume, item.AllocQuantity ?? 0);
                item.TotalVolume = item.OrderVolume;
            }
        }

        private static bool IsOrderedNotShipped(StoreOrderCartItemDto item)
        {
            return item.Quantity > 0 && (item.AllocQuantity ?? 0) == 0;
        }

        private static bool IsShippedWithoutOrder(StoreOrderCartItemDto item)
        {
            return item.Quantity <= 0 && (item.AllocQuantity ?? 0) > 0;
        }

        public async Task<ApiResponse<List<BranchDto>>> GetUsedBranchesAsync()
        {
            try
            {
                // 1. 从订单表获取所有使用过的分店代码 (distinct)
                var usedStoreCodes = await _db.Queryable<WareHouseOrder>()
                    .Where(o => !o.IsDeleted && !string.IsNullOrEmpty(o.StoreCode))
                    .Select(o => o.StoreCode)
                    .Distinct()
                    .ToListAsync();

                if (!usedStoreCodes.Any())
                {
                    return new ApiResponse<List<BranchDto>>
                    {
                        Success = true,
                        Data = new List<BranchDto>(),
                    };
                }

                // HQ 订单的“分店代码”可能是本地数字门店代码，也可能是外购客户 HGUID。
                var guidCodes = usedStoreCodes.Where(c => Guid.TryParse(c, out _)).ToList();
                var normalCodes = usedStoreCodes.Where(c => !Guid.TryParse(c, out _)).ToList();
                _logger.LogInformation(
                    "分店订货筛选分店解析开始：订单标识 {Total} 个，数字分店代码 {StoreCodeCount} 个，外购客户 HGUID {ExternalCustomerCount} 个",
                    usedStoreCodes.Count,
                    normalCodes.Count,
                    guidCodes.Count
                );

                // 2. 根据数字分店代码查询本地分店表
                var branches = await _db.Queryable<Store>()
                    .Where(s => normalCodes.Contains(s.StoreCode))
                    .Select(s => new
                    {
                        Guid = s.StoreGUID,
                        Code = s.StoreCode,
                        Name = s.StoreName,
                    })
                    .ToListAsync();

                var externalCustomers = new List<BranchDto>();
                if (guidCodes.Count > 0)
                {
                    try
                    {
                        using var hqDb = _createHqConnection();
                        externalCustomers = await hqDb.Queryable<CPT_DIC_外购客户信息表>()
                            .Where(x => SqlFunc.HasValue(x.HGUID) && guidCodes.Contains(x.HGUID!))
                            .Select(x => new BranchDto
                            {
                                Guid = x.HGUID!,
                                Code = x.HGUID!,
                                Name = x.客户名称 ?? x.HGUID!,
                            })
                            .ToListAsync();
                    }
                    catch (Exception ex) when (IsRemoteBranchLookupConnectionFailure(ex))
                    {
                        // HQ 外购客户库短暂不可达时，只降级外购客户筛选，保留本地分店筛选可用。
                        _logger.LogWarning(
                            ex,
                            "外购客户分店筛选查询失败，已降级为仅返回本地分店。外购客户标识 {Count} 个，示例: {Codes}",
                            guidCodes.Count,
                            string.Join(", ", guidCodes.Take(5))
                        );
                    }
                }

                // 3. 构建结果列表
                var result = new List<BranchDto>();
                var missingCodes = new List<string>();

                foreach (var code in usedStoreCodes)
                {
                    var branch = branches.FirstOrDefault(b => b.Code == code);

                    if (branch != null)
                    {
                        result.Add(
                            new BranchDto
                            {
                                Guid = branch.Guid,
                                Code = branch.Code,
                                Name = branch.Name,
                            }
                        );
                        continue;
                    }

                    var externalCustomer = externalCustomers.FirstOrDefault(item => item.Code == code);
                    if (externalCustomer != null)
                    {
                        result.Add(externalCustomer);
                    }
                    else
                    {
                        missingCodes.Add(code ?? string.Empty);
                    }
                }

                if (missingCodes.Count > 0)
                {
                    _logger.LogWarning(
                        "订单中存在 {Count} 个无法匹配分店表的分店标识，已从筛选列表忽略。示例: {Codes}",
                        missingCodes.Count,
                        string.Join(", ", missingCodes.Take(5))
                    );
                }

                _logger.LogInformation(
                    "分店订货筛选分店解析完成：本地分店匹配 {StoreMatchedCount}/{StoreCodeCount}，外购客户匹配 {ExternalMatchedCount}/{ExternalCustomerCount}，未匹配 {MissingCount}",
                    branches.Count,
                    normalCodes.Count,
                    externalCustomers.Count,
                    guidCodes.Count,
                    missingCodes.Count
                );

                // 4. 筛选下拉按分店名称排序，名称一致时再按代码兜底，便于人工查找。
                result = result
                    .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(b => b.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ApiResponse<List<BranchDto>> { Success = true, Data = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUsedBranchesAsync failed");
                return new ApiResponse<List<BranchDto>> { Success = false, Message = ex.Message };
            }
        }

        private static bool IsRemoteBranchLookupConnectionFailure(Exception ex)
        {
            var message = ex.ToString();
            return message.Contains("Connection open error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("连接数据库过程中发生错误", StringComparison.OrdinalIgnoreCase)
                || message.Contains("network-related or instance-specific error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("server was not found", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ApiResponse<List<UnmatchedStoreOrderGroupDto>>> GetUnmatchedStoreOrderGroupsAsync()
        {
            try
            {
                var unmatchedCodes = await GetUnmatchedOrderStoreCodesAsync();
                if (unmatchedCodes.Count == 0)
                {
                    return ApiResponse<List<UnmatchedStoreOrderGroupDto>>.OK(
                        new List<UnmatchedStoreOrderGroupDto>()
                    );
                }

                var groupRows = await _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        !o.IsDeleted
                        && o.StoreCode != null
                        && unmatchedCodes.Contains(o.StoreCode)
                    )
                    .GroupBy(o => o.StoreCode)
                    .Select(o => new UnmatchedStoreOrderGroupRow
                    {
                        SourceStoreCode = o.StoreCode!,
                        OrderCount = SqlFunc.AggregateCount(o.OrderGUID),
                        LatestOrderDate = SqlFunc.AggregateMax(o.OrderDate),
                    })
                    .ToListAsync();

                var sourceNameMap = await LoadExternalCustomerNameMapAsync(unmatchedCodes);
                var result = groupRows
                    .Where(item => !string.IsNullOrWhiteSpace(item.SourceStoreCode))
                    .Select(item =>
                    {
                        sourceNameMap.TryGetValue(item.SourceStoreCode, out var sourceName);
                        return new UnmatchedStoreOrderGroupDto
                        {
                            SourceStoreCode = item.SourceStoreCode,
                            SourceStoreName = sourceName,
                            OrderCount = item.OrderCount,
                            LatestOrderDate = item.LatestOrderDate,
                        };
                    })
                    .OrderByDescending(item => item.OrderCount)
                    .ThenByDescending(item => item.LatestOrderDate)
                    .ThenBy(item => item.SourceStoreCode, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return ApiResponse<List<UnmatchedStoreOrderGroupDto>>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUnmatchedStoreOrderGroupsAsync failed");
                return ApiResponse<List<UnmatchedStoreOrderGroupDto>>.Error(ex.Message);
            }
        }

        public async Task<ApiResponse<BatchMapStoreOrderStoreCodeResultDto>> BatchMapStoreOrderStoreCodeAsync(
            BatchMapStoreOrderStoreCodeDto request
        )
        {
            try
            {
                var mappings = (request?.Mappings ?? new List<StoreOrderStoreCodeMappingDto>())
                    .Select(item => new StoreOrderStoreCodeMappingDto
                    {
                        SourceStoreCode = item.SourceStoreCode?.Trim() ?? string.Empty,
                        TargetStoreCode = item.TargetStoreCode?.Trim() ?? string.Empty,
                    })
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item.SourceStoreCode)
                        && !string.IsNullOrWhiteSpace(item.TargetStoreCode)
                    )
                    .GroupBy(item => item.SourceStoreCode, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();

                if (mappings.Count == 0)
                {
                    return ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.Error(
                        "请至少选择一个需要修复的分店标识"
                    );
                }

                var targetCodes = mappings
                    .Select(item => item.TargetStoreCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                // 历史订单 GUID 修复允许映射到停用但仍存在的本地分店，保存值仍统一写 StoreCode。
                var targetStores = await _db.Queryable<Store>()
                    .Where(store =>
                        targetCodes.Contains(store.StoreCode)
                        && !store.IsDeleted
                    )
                    .Select(store => store.StoreCode)
                    .ToListAsync();
                var targetStoreSet = targetStores.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingTargets = targetCodes
                    .Where(code => !targetStoreSet.Contains(code))
                    .ToList();
                if (missingTargets.Count > 0)
                {
                    return ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.Error(
                        $"目标分店不存在：{string.Join(", ", missingTargets)}"
                    );
                }

                var unmatchedSourceSet = await GetUnmatchedOrderStoreCodesAsync();
                var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                var now = DateTime.Now;
                var result = new BatchMapStoreOrderStoreCodeResultDto();

                foreach (var mapping in mappings)
                {
                    var itemResult = new StoreOrderStoreCodeMappingResultItemDto
                    {
                        SourceStoreCode = mapping.SourceStoreCode,
                        TargetStoreCode = mapping.TargetStoreCode,
                    };

                    var sourceCount = await _db.Queryable<WareHouseOrder>()
                        .Where(order =>
                            !order.IsDeleted
                            && order.StoreCode == mapping.SourceStoreCode
                        )
                        .CountAsync();

                    if (!unmatchedSourceSet.Contains(mapping.SourceStoreCode))
                    {
                        itemResult.SkippedCount = sourceCount;
                        result.SkippedCount += itemResult.SkippedCount;
                        result.Items.Add(itemResult);
                        continue;
                    }

                    // 只修正订单主表 StoreCode，目标值统一写本地分店编码，避免继续保留 GUID 混用。
                    itemResult.UpdatedCount = await _db.Updateable<WareHouseOrder>()
                        .SetColumns(order => new WareHouseOrder
                        {
                            StoreCode = mapping.TargetStoreCode,
                            UpdatedBy = currentUser,
                            UpdatedAt = now,
                        })
                        .Where(order =>
                            !order.IsDeleted
                            && order.StoreCode == mapping.SourceStoreCode
                        )
                        .ExecuteCommandAsync();
                    itemResult.SkippedCount = Math.Max(0, sourceCount - itemResult.UpdatedCount);

                    result.UpdatedCount += itemResult.UpdatedCount;
                    result.SkippedCount += itemResult.SkippedCount;
                    result.Items.Add(itemResult);
                }

                return ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.OK(
                    result,
                    $"已修复 {result.UpdatedCount} 张订单"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchMapStoreOrderStoreCodeAsync failed");
                return ApiResponse<BatchMapStoreOrderStoreCodeResultDto>.Error(ex.Message);
            }
        }

        private async Task<HashSet<string>> GetUnmatchedOrderStoreCodesAsync()
        {
            var usedStoreCodes = await _db.Queryable<WareHouseOrder>()
                .Where(order => !order.IsDeleted && order.StoreCode != null && order.StoreCode != "")
                .Select(order => order.StoreCode)
                .Distinct()
                .ToListAsync();

            var normalizedUsedStoreCodes = usedStoreCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedUsedStoreCodes.Count == 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var matchedStores = await _db.Queryable<Store>()
                .Where(store =>
                    (!string.IsNullOrEmpty(store.StoreCode) && normalizedUsedStoreCodes.Contains(store.StoreCode))
                    || (!string.IsNullOrEmpty(store.StoreGUID) && normalizedUsedStoreCodes.Contains(store.StoreGUID))
                )
                .Select(store => new
                {
                    store.StoreCode,
                    store.StoreGUID,
                })
                .ToListAsync();

            var matchedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var store in matchedStores)
            {
                if (!string.IsNullOrWhiteSpace(store.StoreCode))
                {
                    matchedSet.Add(store.StoreCode);
                }
                if (!string.IsNullOrWhiteSpace(store.StoreGUID))
                {
                    matchedSet.Add(store.StoreGUID);
                }
            }

            // 未匹配集合只保留订单旧值，不包含任何已能解析为本地分店编码/GUID 的标识。
            return normalizedUsedStoreCodes
                .Where(code => !matchedSet.Contains(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<string, string>> LoadExternalCustomerNameMapAsync(
            HashSet<string> sourceCodes
        )
        {
            var hqGuids = sourceCodes
                .Where(code => Guid.TryParse(code, out _))
                .ToList();
            if (hqGuids.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var hqDb = _createHqConnection();
            var customers = await hqDb.Queryable<CPT_DIC_外购客户信息表>()
                .Where(item => item.HGUID != null && hqGuids.Contains(item.HGUID))
                .Select(item => new
                {
                    item.HGUID,
                    item.客户名称,
                })
                .ToListAsync();

            return customers
                .Where(item => !string.IsNullOrWhiteSpace(item.HGUID))
                .GroupBy(item => item.HGUID!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().客户名称 ?? group.Key,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        public async Task<ApiResponse<string>> CreateOrderAsync(CreateStoreOrderDto request)
        {
            try
            {
                var bypassPreorderGate = request.BypassPreorderGate || IsWarehouseStaffOnly();
                var storeResource = bypassPreorderGate
                    ? string.Empty
                    : await PreorderGateEvaluator.ResolveStoreLockResourceFailClosedAsync(
                        _db,
                        request.StoreCode,
                        _logger
                    );
                await using IAsyncDisposable? storeLock = bypassPreorderGate
                    ? null
                    : await PreorderMutationLock.AcquireProcessAsync(storeResource);
                ApiResponse<string>? response = null;
                var transaction = await _db.Ado.UseTranAsync(async () =>
                {
                    if (!bypassPreorderGate)
                    {
                        var preorderGate = await PreorderGateEvaluator.EvaluateLockedFailClosedAsync(
                            _db,
                            storeResource,
                            request.StoreCode,
                            TimeProvider.System,
                            _logger
                        );
                        if (preorderGate.IsBlocked)
                        {
                            response = new ApiResponse<string>
                            {
                                Success = false,
                                ErrorCode = "PREORDER_REQUIRED",
                                Message = "请先完成当前有效的 Preorder，再创建普通订单",
                            };
                            return;
                        }
                    }

                    var now = DateTime.Now;
                    var currentUser =
                        _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                    var order = new WareHouseOrder
                    {
                        OrderGUID = UuidHelper.GenerateUuid7(),
                        StoreCode = request.StoreCode,
                        OrderDate = now,
                        FlowStatus = 1,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        UpdatedBy = currentUser,
                        OEMTotalAmount = 0,
                        ImportTotalAmount = 0,
                        ShippingFee = 0,
                        OrderNo = await _orderNumberGenerator.GetNextOrderNoAsync(),
                        Remarks = request.Remarks,
                    };
                    await _db.Insertable(order).ExecuteCommandAsync();
                    response = new ApiResponse<string> { Success = true, Data = order.OrderGUID };
                });
                if (!transaction.IsSuccess)
                {
                    throw transaction.ErrorException ?? new InvalidOperationException("创建订单事务失败");
                }
                return response ?? new ApiResponse<string> { Success = false, Message = "创建订单失败" };
            }
            catch (PreorderBusinessException ex)
            {
                _logger.LogWarning(ex, "CreateOrder Preorder gate unavailable");
                return new ApiResponse<string>
                {
                    Success = false,
                    ErrorCode = ex.ErrorCode,
                    Message = ex.Message,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateOrderAsync failed");
                return new ApiResponse<string> { Success = false, Message = "订单创建失败，请稍后重试" };
            }
        }

        public async Task<ApiResponse<bool>> AddOrderLineAsync(AddOrderLineDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                await AddOrUpdateDetailAsync(
                    order,
                    request.ProductCode,
                    request.Quantity,
                    null, // importPrice
                    isUpdate: false
                );
                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> BatchAddOrderLineAsync(BatchAddOrderLineDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                foreach (var item in request.Items)
                {
                    await AddOrUpdateDetailAsync(
                        order,
                        item.ProductCode,
                        item.Quantity,
                        item.ImportPrice, // importPrice
                        isUpdate: false
                    );
                }

                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchAddOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> PasteReplaceOrderLinesAsync(
            PasteReplaceOrderLinesDto request
        )
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                if (!IsSupportedPasteTargetField(request.TargetField))
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Unsupported paste target field",
                    };
                }

                if (request.Items.Any(item => !IsSupportedPasteAction(item.Action)))
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Unsupported paste action",
                    };
                }

                await PasteReplaceDetailsBatchAsync(order, request.Items, request.TargetField);
                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PasteReplaceOrderLinesAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateOrderLineAsync(UpdateOrderLineDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };

                _db.Ado.BeginTran();
                try
                {
                    await AddOrUpdateDetailAsync(
                        order,
                        request.ProductCode,
                        request.Quantity,
                        request.ImportPrice,
                        isUpdate: true
                    );

                    if (request.SyncImportPrice == true && request.ImportPrice.HasValue)
                    {
                        await SyncOrderImportPriceToProductTablesAsync(
                            request.ProductCode,
                            request.ImportPrice.Value
                        );
                    }

                    await UpdateOrderTotalAsync(order.OrderGUID);
                    _db.Ado.CommitTran();
                }
                catch
                {
                    _db.Ado.RollbackTran();
                    throw;
                }

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> RemoveOrderLineAsync(RemoveOrderLineDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d =>
                        d.OrderGUID == request.OrderGUID
                        && d.DetailGUID == request.DetailGUID
                        && !d.IsDeleted
                    )
                    .FirstAsync();

                if (detail == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order line not found",
                    };
                }

                detail.IsDeleted = true;
                detail.UpdatedAt = DateTime.Now;
                detail.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(detail).ExecuteCommandAsync();
                await UpdateOrderTotalAsync(order.OrderGUID);

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdateOrderLineAsync(
            BatchUpdateOrderLineDto request
        )
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGUID);
                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };
                }

                if (CanUseDetailGuidQuantityBatchUpdate(request))
                {
                    return await BatchUpdateOrderLineByDetailGuidAsync(order, request);
                }

                _db.Ado.BeginTran();
                try
                {
                    foreach (var item in request.Items)
                    {
                        await AddOrUpdateDetailAsync(
                            order,
                            item.ProductCode,
                            item.Quantity ?? 0,
                            item.ImportPrice,
                            isUpdate: true,
                            isBatch: true,
                            originalQuantity: item.Quantity
                        );

                        if (item.SyncImportPrice == true && item.ImportPrice.HasValue)
                        {
                            await SyncOrderImportPriceToProductTablesAsync(
                                item.ProductCode,
                                item.ImportPrice.Value
                            );
                        }
                    }

                    await UpdateOrderTotalAsync(order.OrderGUID);
                    _db.Ado.CommitTran();
                }
                catch
                {
                    _db.Ado.RollbackTran();
                    throw;
                }

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        private static bool CanUseDetailGuidQuantityBatchUpdate(BatchUpdateOrderLineDto request)
        {
            return request.Items.Count > 0
                && request.Items.All(item =>
                    !string.IsNullOrWhiteSpace(item.DetailGUID)
                    && item.Quantity.HasValue
                    && !item.ImportPrice.HasValue
                    && item.SyncImportPrice != true
                );
        }

        private async Task<ApiResponse<bool>> BatchUpdateOrderLineByDetailGuidAsync(
            WareHouseOrder order,
            BatchUpdateOrderLineDto request
        )
        {
            var detailGuids = request.Items
                .Select(item => item.DetailGUID!.Trim())
                .ToList();

            if (detailGuids.Count != detailGuids.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = "Duplicate order line detailGUID",
                };
            }

            if (request.Items.Any(item => item.Quantity.GetValueOrDefault() < 0))
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = "Quantity cannot be negative",
                };
            }

            var details = await _db.Queryable<WareHouseOrderDetails>()
                .Where(detail =>
                    detail.OrderGUID == order.OrderGUID
                    && detailGuids.Contains(detail.DetailGUID)
                    && !detail.IsDeleted
                )
                .ToListAsync();

            if (details.Count != detailGuids.Count)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = "Some order lines were not found",
                };
            }

            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
            var detailMap = details.ToDictionary(
                detail => detail.DetailGUID,
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var item in request.Items)
            {
                var detail = detailMap[item.DetailGUID!.Trim()];
                var allocQuantity = item.Quantity!.Value;
                detail.AllocQuantity = allocQuantity;
                detail.OEMAmount = allocQuantity * (detail.OEMPrice ?? 0);
                // ImportAmount 是订货金额；发货/发票金额只在 DTO 中按 AllocQuantity 派生。
                detail.ImportAmount = CalculateOrderImportAmount(detail.Quantity, detail.ImportPrice);
                detail.UpdatedAt = now;
                detail.UpdatedBy = currentUser;

                // 复制 0 发货数后，订货和发货都为 0 的明细沿用原批量更新规则做软删除。
                if ((detail.Quantity ?? 0) <= 0 && allocQuantity <= 0)
                {
                    detail.IsDeleted = true;
                }
            }

            _db.Ado.BeginTran();
            try
            {
                await _db.Updateable(details).ExecuteCommandAsync();
                await UpdateOrderTotalAsync(order.OrderGUID);
                _db.Ado.CommitTran();
            }
            catch
            {
                _db.Ado.RollbackTran();
                throw;
            }

            return new ApiResponse<bool> { Success = true, Data = true };
        }

        public async Task<ApiResponse<RefreshStoreOrderImportPricesResultDto>> RefreshOrderLineImportPricesAsync(
            RefreshStoreOrderImportPricesDto request
        )
        {
            try
            {
                var orderGuid = request.OrderGUID?.Trim();
                if (string.IsNullOrWhiteSpace(orderGuid))
                {
                    return new ApiResponse<RefreshStoreOrderImportPricesResultDto>
                    {
                        Success = false,
                        Message = "OrderGUID is required",
                    };
                }

                var orderExists = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .AnyAsync();
                if (!orderExists)
                {
                    return new ApiResponse<RefreshStoreOrderImportPricesResultDto>
                    {
                        Success = false,
                        Message = "Order not found",
                    };
                }

                var detailGuids = (request.DetailGUIDs ?? new List<string>())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var detailQuery = _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == orderGuid && !d.IsDeleted);
                if (detailGuids.Count > 0)
                {
                    detailQuery = detailQuery.Where(d => detailGuids.Contains(d.DetailGUID));
                }

                var details = await detailQuery.ToListAsync();
                if (details.Count == 0)
                {
                    return new ApiResponse<RefreshStoreOrderImportPricesResultDto>
                    {
                        Success = true,
                        Data = new RefreshStoreOrderImportPricesResultDto(),
                    };
                }

                var productCodes = details
                    .Select(item => item.ProductCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var warehousePrices = productCodes.Count == 0
                    ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    : (await _db.Queryable<WarehouseProduct>()
                        .Where(wp => productCodes.Contains(wp.ProductCode) && !wp.IsDeleted)
                        .Select(wp => new { wp.ProductCode, wp.ImportPrice })
                        .ToListAsync())
                    .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                    .ToDictionary(
                        item => item.ProductCode,
                        item => item.ImportPrice ?? 0,
                        StringComparer.OrdinalIgnoreCase
                    );

                var now = DateTime.Now;
                var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                var result = new RefreshStoreOrderImportPricesResultDto();
                var changedDetails = new List<WareHouseOrderDetails>();

                foreach (var detail in details)
                {
                    if (
                        string.IsNullOrWhiteSpace(detail.ProductCode)
                        || !warehousePrices.TryGetValue(detail.ProductCode, out var warehouseImportPrice)
                        || warehouseImportPrice <= 0
                    )
                    {
                        result.SkippedCount += 1;
                        result.MissingWarehousePriceCount += 1;
                        continue;
                    }

                    var expectedImportAmount = CalculateOrderImportAmount(detail.Quantity, warehouseImportPrice);
                    var importPriceMatches = detail.ImportPrice.HasValue && detail.ImportPrice.Value == warehouseImportPrice;
                    var importAmountMatches = detail.ImportAmount.HasValue && detail.ImportAmount.Value == expectedImportAmount;
                    if (importPriceMatches && importAmountMatches)
                    {
                        result.UnchangedCount += 1;
                        continue;
                    }

                    // 受控地从仓库商品表回填订单明细进口价；即使价格相同，也要校正历史不准的订货金额。
                    detail.ImportPrice = warehouseImportPrice;
                    detail.ImportAmount = expectedImportAmount;
                    detail.UpdatedAt = now;
                    detail.UpdatedBy = currentUser;
                    changedDetails.Add(detail);
                }

                if (changedDetails.Count > 0)
                {
                    await _db.Updateable(changedDetails).ExecuteCommandAsync();
                    await UpdateOrderTotalAsync(orderGuid);
                }

                result.UpdatedCount = changedDetails.Count;
                return new ApiResponse<RefreshStoreOrderImportPricesResultDto>
                {
                    Success = true,
                    Data = result,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RefreshOrderLineImportPricesAsync failed");
                return new ApiResponse<RefreshStoreOrderImportPricesResultDto>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        public async Task<ApiResponse<bool>> UpdateOrderHeaderAsync(UpdateOrderHeaderDto request)
        {
            try
            {
                var order = await GetEditableOrderAsync(request.OrderGuid);
                if (order == null)
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found or not editable",
                    };

                order.Remarks = request.Remarks;
                order.ShippingFee = request.ShippingFee;
                if (request.OrderDate.HasValue)
                {
                    order.OrderDate = request.OrderDate.Value;
                }

                // 处理 StoreCode 更新
                if (
                    !string.IsNullOrEmpty(request.StoreCode)
                    && order.StoreCode != request.StoreCode
                )
                {
                    order.StoreCode = request.StoreCode;

                    // 更新明细中的 StoreCode
                    await _db.Updateable<WareHouseOrderDetails>()
                        .SetColumns(d => d.StoreCode == request.StoreCode)
                        .Where(d => d.OrderGUID == request.OrderGuid)
                        .ExecuteCommandAsync();
                }

                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.Remarks,
                        o.ShippingFee,
                        o.OrderDate,
                        o.StoreCode,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderHeaderAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateOrderOutboundDateAsync(
            UpdateOrderOutboundDateDto request
        )
        {
            try
            {
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == request.OrderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Order not found",
                    };
                }

                order.OutboundDate = request.OutboundDate;
                if (request.CompleteOrder)
                {
                    // 出库日期接口可同步完成订单，但仍必须遵守订单状态机，避免绕过完成订单专用接口校验。
                    if (order.FlowStatus != 1 && order.FlowStatus != 3)
                    {
                        return new ApiResponse<bool>
                        {
                            Success = false,
                            Message = "只有已提交或配货中状态的订单才能标记为完成",
                        };
                    }

                    order.FlowStatus = 2;
                }
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.OutboundDate,
                        o.FlowStatus,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderOutboundDateAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> DeleteOrderAsync(string orderGuid)
        {
            try
            {
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "Order not found" };
                }

                // 软删除主表
                order.IsDeleted = true;
                order.UpdatedBy = currentUser;
                order.UpdatedAt = DateTime.Now;

                // 开启事务，同时软删除明细
                try
                {
                    _db.Ado.BeginTran();

                    await _db.Updateable(order).ExecuteCommandAsync();

                    await _db.Updateable<WareHouseOrderDetails>()
                        .SetColumns(d => new WareHouseOrderDetails
                        {
                            IsDeleted = true,
                            UpdatedBy = currentUser,
                            UpdatedAt = DateTime.Now,
                        })
                        .Where(d => d.OrderGUID == orderGuid)
                        .ExecuteCommandAsync();

                    _db.Ado.CommitTran();
                    return new ApiResponse<bool> { Success = true, Data = true };
                }
                catch (Exception)
                {
                    _db.Ado.RollbackTran();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteOrderAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateProductStatusAsync(
            UpdateProductStatusDto request
        )
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 更新 Product 表
                var result = await _db.Updateable<Product>()
                    .SetColumns(p => new Product
                    {
                        IsActive = request.IsActive,
                        UpdatedAt = now,
                        UpdatedBy = currentUser,
                    })
                    .Where(p => p.ProductCode == request.ProductCode)
                    .ExecuteCommandAsync();

                if (result > 0)
                {
                    return new ApiResponse<bool> { Success = true, Data = true };
                }
                return new ApiResponse<bool> { Success = false, Message = "Product not found" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateProductStatusAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdateProductStatusAsync(
            BatchUpdateProductStatusDto request
        )
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                if (request.ProductCodes == null || !request.ProductCodes.Any())
                {
                    return new ApiResponse<bool> { Success = true, Data = true };
                }

                // 批量更新 Product 表
                await _db.Updateable<Product>()
                    .SetColumns(p => new Product
                    {
                        IsActive = request.IsActive,
                        UpdatedAt = now,
                        UpdatedBy = currentUser,
                    })
                    .Where(p =>
                        p.ProductCode != null && request.ProductCodes.Contains(p.ProductCode)
                    )
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateProductStatusAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<CopyOrderResultDto>> CopyOrderAsync(CopyOrderDto request)
        {
            try
            {
                var bypassPreorderGate = request.BypassPreorderGate || IsWarehouseStaffOnly();
                var storeResource = bypassPreorderGate
                    ? string.Empty
                    : await PreorderGateEvaluator.ResolveStoreLockResourceFailClosedAsync(
                        _db,
                        request.TargetStoreCode,
                        _logger
                    );
                await using IAsyncDisposable? storeLock = bypassPreorderGate
                    ? null
                    : await PreorderMutationLock.AcquireProcessAsync(storeResource);
                var sourceOrder = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == request.SourceOrderGUID && !o.IsDeleted)
                    .FirstAsync();

                if (sourceOrder == null)
                {
                    return new ApiResponse<CopyOrderResultDto>
                    {
                        Success = false,
                        Message = "Source order not found",
                    };
                }

                var sourceDetails = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.OrderGUID == request.SourceOrderGUID && !d.IsDeleted)
                    .ToListAsync();

                if (sourceDetails == null || !sourceDetails.Any())
                {
                    return new ApiResponse<CopyOrderResultDto>
                    {
                        Success = false,
                        Message = "Source order has no items",
                    };
                }

                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                var newOrder = new WareHouseOrder
                {
                    OrderGUID = UuidHelper.GenerateUuid7(),
                    StoreCode = request.TargetStoreCode,
                    OrderDate = now,
                    FlowStatus = 1,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = currentUser,
                    OEMTotalAmount = 0,
                    ImportTotalAmount = 0,
                    ShippingFee = 0,
                    OrderNo = await _orderNumberGenerator.GetNextOrderNoAsync(),
                    Remarks = $"Copied from {sourceOrder.OrderNo}",
                };

                var newDetails = new List<WareHouseOrderDetails>();
                foreach (var srcDetail in sourceDetails)
                {
                    var newDetail = new WareHouseOrderDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        OrderGUID = newOrder.OrderGUID,
                        StoreCode = request.TargetStoreCode,
                        ProductCode = srcDetail.ProductCode,
                        Quantity = request.CopyOrderQuantity ? srcDetail.Quantity : 0,
                        OEMPrice = srcDetail.OEMPrice,
                        OEMAmount = 0,
                        AllocQuantity = request.CopyAllocQuantity ? srcDetail.AllocQuantity : 0,
                        ImportPrice = srcDetail.ImportPrice,
                        ImportAmount = 0,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = currentUser,
                        UpdatedBy = currentUser,
                    };

                    newDetail.OEMAmount = newDetail.AllocQuantity * newDetail.OEMPrice;
                    newDetail.ImportAmount = CalculateOrderImportAmount(
                        newDetail.Quantity,
                        newDetail.ImportPrice
                    );

                    newDetails.Add(newDetail);
                }

                newOrder.OEMTotalAmount = newDetails.Sum(d => d.OEMAmount);
                newOrder.ImportTotalAmount = newDetails.Sum(d => d.ImportAmount);

                try
                {
                    _db.Ado.BeginTran();

                    if (!bypassPreorderGate)
                    {
                        var preorderGate = await PreorderGateEvaluator.EvaluateLockedFailClosedAsync(
                            _db,
                            storeResource,
                            request.TargetStoreCode,
                            TimeProvider.System,
                            _logger
                        );
                        if (preorderGate.IsBlocked)
                        {
                            _db.Ado.RollbackTran();
                            return new ApiResponse<CopyOrderResultDto>
                            {
                                Success = false,
                                ErrorCode = "PREORDER_REQUIRED",
                                Message = "请先完成当前有效的 Preorder，再复制普通订单",
                            };
                        }
                    }

                    await _db.Insertable(newOrder).ExecuteCommandAsync();
                    await _db.Insertable(newDetails).ExecuteCommandAsync();

                    _db.Ado.CommitTran();
                }
                catch (Exception)
                {
                    _db.Ado.RollbackTran();
                    throw;
                }

                return new ApiResponse<CopyOrderResultDto>
                {
                    Success = true,
                    Data = new CopyOrderResultDto
                    {
                        OrderGUID = newOrder.OrderGUID,
                        OrderNo = newOrder.OrderNo,
                    },
                };
            }
            catch (PreorderBusinessException ex)
            {
                _logger.LogWarning(ex, "CopyOrder Preorder gate unavailable");
                return new ApiResponse<CopyOrderResultDto>
                {
                    Success = false,
                    ErrorCode = ex.ErrorCode,
                    Message = ex.Message,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CopyOrderAsync failed");
                return new ApiResponse<CopyOrderResultDto>
                {
                    Success = false,
                    Message = "订单复制失败，请稍后重试",
                };
            }
        }

        private async Task<WareHouseOrder?> GetEditableOrderAsync(string orderGuid)
        {
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                .FirstAsync();

            // 允许购物车、已提交、配货中继续编辑；已完成订单保持只读，避免完成后被误改。
            if (order != null && (order.FlowStatus == 0 || order.FlowStatus == 1 || order.FlowStatus == 3))
            {
                return order;
            }
            return null;
        }

        private bool IsSupportedPasteTargetField(string targetField)
        {
            return string.Equals(
                    targetField,
                    StoreOrderPasteTargetFields.Quantity,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    targetField,
                    StoreOrderPasteTargetFields.AllocQuantity,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private async Task PasteReplaceDetailAsync(
            WareHouseOrder order,
            ProductQuantityDto item,
            string targetField
        )
        {
            var targetQuantity = item.Quantity;
            var action = NormalizePasteAction(item.Action);
            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

            if (
                string.Equals(
                    action,
                    StoreOrderPasteActions.Skip,
                    StringComparison.OrdinalIgnoreCase
                )
                || targetQuantity <= 0
            )
            {
                return;
            }

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .Where(wp => wp.ProductCode == item.ProductCode)
                .FirstAsync();

            if (warehouseProduct == null)
            {
                var product = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == item.ProductCode)
                    .FirstAsync();
                if (product == null)
                {
                    throw new Exception($"Product {item.ProductCode} not found");
                }

                warehouseProduct = new WarehouseProduct
                {
                    OEMPrice = 0,
                    ImportPrice = 0,
                    MinOrderQuantity = 1,
                };
            }

            var detail = await _db.Queryable<WareHouseOrderDetails>()
                .Where(d => d.OrderGUID == order.OrderGUID && d.ProductCode == item.ProductCode)
                .FirstAsync();

            if (detail == null)
            {
                detail = new WareHouseOrderDetails
                {
                    DetailGUID = UuidHelper.GenerateUuid7(),
                    OrderGUID = order.OrderGUID,
                    StoreCode = order.StoreCode,
                    ProductCode = item.ProductCode,
                    Quantity = 0,
                    AllocQuantity = 0,
                    OEMPrice = warehouseProduct.OEMPrice ?? 0,
                    OEMAmount = 0,
                    ImportPrice = item.ImportPrice ?? warehouseProduct.ImportPrice ?? 0,
                    ImportAmount = 0,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = currentUser,
                    UpdatedBy = currentUser,
                };
            }
            else if (item.ImportPrice.HasValue)
            {
                detail.ImportPrice = item.ImportPrice.Value;
            }

            var nextQuantity = targetQuantity;
            if (
                string.Equals(
                    action,
                    StoreOrderPasteActions.Append,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                // 追加必须基于数据库当前明细值，避免前端预览数据过期导致覆盖用户刚改的数量。
                nextQuantity =
                    string.Equals(
                        targetField,
                        StoreOrderPasteTargetFields.Quantity,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? (detail.Quantity ?? 0) + targetQuantity
                        : (detail.AllocQuantity ?? 0) + targetQuantity;
            }

            if (
                string.Equals(
                    targetField,
                    StoreOrderPasteTargetFields.Quantity,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                detail.Quantity = nextQuantity;
            }
            else
            {
                detail.AllocQuantity = nextQuantity;
            }

            var allocQuantity = detail.AllocQuantity ?? 0;
            detail.OEMAmount = allocQuantity * (detail.OEMPrice ?? 0);
            // ImportAmount 是订货金额；发货/发票金额只在 DTO 中按 AllocQuantity 派生。
            detail.ImportAmount = CalculateOrderImportAmount(detail.Quantity, detail.ImportPrice);
            detail.UpdatedAt = now;
            detail.UpdatedBy = currentUser;

            if (detail.Quantity <= 0 && allocQuantity <= 0)
            {
                if (!string.IsNullOrWhiteSpace(detail.DetailGUID))
                {
                    await _db.Deleteable(detail).ExecuteCommandAsync();
                }
                return;
            }

            if (
                await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.DetailGUID == detail.DetailGUID)
                    .AnyAsync()
            )
            {
                await _db.Updateable(detail).ExecuteCommandAsync();
            }
            else
            {
                await _db.Insertable(detail).ExecuteCommandAsync();
            }
        }

        private async Task PasteReplaceDetailsBatchAsync(
            WareHouseOrder order,
            IReadOnlyCollection<ProductQuantityDto> items,
            string targetField
        )
        {
            var importableItems = items
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.ProductCode)
                    // Excel 粘贴允许 0 写入已有明细用于清零；负数仍过滤。
                    && item.Quantity >= 0
                    && !string.Equals(
                        NormalizePasteAction(item.Action),
                        StoreOrderPasteActions.Skip,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(item => new ProductQuantityDto
                {
                    ProductCode = item.ProductCode.Trim(),
                    Quantity = item.Quantity,
                    ImportPrice = item.ImportPrice,
                    Action = NormalizePasteAction(item.Action),
                })
                .ToList();

            if (importableItems.Count == 0)
            {
                await UpdateOrderTotalAsync(order.OrderGUID);
                return;
            }

            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
            var productCodes = importableItems
                .Select(item => item.ProductCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Excel 粘贴常见 200+ 行，必须批量读取基础资料和现有明细，避免逐行查询拖慢后台任务。
            var existingDetails = await _db.Queryable<WareHouseOrderDetails>()
                .Where(detail =>
                    detail.OrderGUID == order.OrderGUID
                    && detail.ProductCode != null
                    && productCodes.Contains(detail.ProductCode)
                )
                .ToListAsync();
            var detailByProductCode = existingDetails
                .Where(detail => !string.IsNullOrWhiteSpace(detail.ProductCode))
                .GroupBy(detail => detail.ProductCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(detail => detail.IsDeleted ? 1 : 0)
                        .First(),
                    StringComparer.OrdinalIgnoreCase
                );

            var warehouseProducts = await _db.Queryable<WarehouseProduct>()
                .Where(product => productCodes.Contains(product.ProductCode))
                .ToListAsync();
            var warehouseProductByCode = warehouseProducts
                .GroupBy(product => product.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase
                );

            var productCodesMissingWarehouse = productCodes
                .Where(code => !warehouseProductByCode.ContainsKey(code))
                .ToList();
            var productMasterCodes = productCodesMissingWarehouse.Count == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (await _db.Queryable<Product>()
                    .Where(product =>
                        product.ProductCode != null
                        && productCodesMissingWarehouse.Contains(product.ProductCode)
                    )
                    .Select(product => product.ProductCode!)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var productCode in productCodesMissingWarehouse)
            {
                if (!productMasterCodes.Contains(productCode))
                {
                    throw new Exception($"Product {productCode} not found");
                }
            }

            var insertedDetails = new List<WareHouseOrderDetails>();
            var touchedDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var detailsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in importableItems)
            {
                var warehouseProduct = warehouseProductByCode.TryGetValue(
                    item.ProductCode,
                    out var existingWarehouseProduct
                )
                    ? existingWarehouseProduct
                    : new WarehouseProduct
                    {
                        ProductCode = item.ProductCode,
                        OEMPrice = 0,
                        ImportPrice = 0,
                        MinOrderQuantity = 1,
                    };

                var isNewDetail = false;
                if (!detailByProductCode.TryGetValue(item.ProductCode, out var detail))
                {
                    isNewDetail = true;
                    detail = new WareHouseOrderDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        OrderGUID = order.OrderGUID,
                        StoreCode = order.StoreCode,
                        ProductCode = item.ProductCode,
                        Quantity = 0,
                        AllocQuantity = 0,
                        OEMPrice = warehouseProduct.OEMPrice ?? 0,
                        OEMAmount = 0,
                        ImportPrice = item.ImportPrice ?? warehouseProduct.ImportPrice ?? 0,
                        ImportAmount = 0,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = currentUser,
                        UpdatedBy = currentUser,
                    };
                    detailByProductCode[item.ProductCode] = detail;
                    insertedDetails.Add(detail);
                }
                else if (item.ImportPrice.HasValue)
                {
                    detail.ImportPrice = item.ImportPrice.Value;
                }

                // 删除明细是软删；再次粘贴同商品时应复活该行，否则任务成功但刷新后仍不可见。
                detail.IsDeleted = false;

                var nextQuantity = item.Quantity;
                if (
                    string.Equals(
                        item.Action,
                        StoreOrderPasteActions.Append,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    // 追加基于本轮已合并的明细值，兼容同一批 Excel 内重复货号的顺序叠加。
                    nextQuantity =
                        string.Equals(
                            targetField,
                            StoreOrderPasteTargetFields.Quantity,
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? (detail.Quantity ?? 0) + item.Quantity
                            : (detail.AllocQuantity ?? 0) + item.Quantity;
                }

                if (
                    string.Equals(
                        targetField,
                        StoreOrderPasteTargetFields.Quantity,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    detail.Quantity = nextQuantity;
                }
                else
                {
                    detail.AllocQuantity = nextQuantity;
                }

                var allocQuantity = detail.AllocQuantity ?? 0;
                detail.OEMAmount = allocQuantity * (detail.OEMPrice ?? 0);
                // ImportAmount 是订货金额；发货/发票金额只在 DTO 中按 AllocQuantity 派生。
                detail.ImportAmount = CalculateOrderImportAmount(detail.Quantity, detail.ImportPrice);
                detail.UpdatedAt = now;
                detail.UpdatedBy = currentUser;

                if ((detail.Quantity ?? 0) <= 0 && allocQuantity <= 0)
                {
                    if (!isNewDetail && !string.IsNullOrWhiteSpace(detail.DetailGUID))
                    {
                        detailsToDelete.Add(detail.DetailGUID);
                    }
                    continue;
                }

                if (!isNewDetail && !string.IsNullOrWhiteSpace(detail.DetailGUID))
                {
                    touchedDetails.Add(detail.DetailGUID);
                }
            }

            var detailsToInsert = insertedDetails
                .Where(detail =>
                    (detail.Quantity ?? 0) > 0
                    || (detail.AllocQuantity ?? 0) > 0
                )
                .ToList();
            var detailsToUpdate = existingDetails
                .Where(detail =>
                    !string.IsNullOrWhiteSpace(detail.DetailGUID)
                    && touchedDetails.Contains(detail.DetailGUID)
                    && !detailsToDelete.Contains(detail.DetailGUID)
                )
                .ToList();

            try
            {
                _db.Ado.BeginTran();

                if (detailsToDelete.Count > 0)
                {
                    var detailGuids = detailsToDelete.ToList();
                    await _db.Deleteable<WareHouseOrderDetails>()
                        .Where(detail => detailGuids.Contains(detail.DetailGUID))
                        .ExecuteCommandAsync();
                }

                if (detailsToUpdate.Count > 0)
                {
                    await _db.Updateable(detailsToUpdate).ExecuteCommandAsync();
                }

                if (detailsToInsert.Count > 0)
                {
                    await _db.Insertable(detailsToInsert).ExecuteCommandAsync();
                }

                await UpdateOrderTotalAsync(order.OrderGUID);
                _db.Ado.CommitTran();
            }
            catch
            {
                _db.Ado.RollbackTran();
                throw;
            }
        }

        private static string NormalizePasteAction(string? action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return StoreOrderPasteActions.Replace;
            }

            if (
                string.Equals(
                    action,
                    StoreOrderPasteActions.Append,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return StoreOrderPasteActions.Append;
            }

            if (
                string.Equals(
                    action,
                    StoreOrderPasteActions.Skip,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return StoreOrderPasteActions.Skip;
            }

            return StoreOrderPasteActions.Replace;
        }

        private static bool IsSupportedPasteAction(string? action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return true;
            }

            return string.Equals(
                    action,
                    StoreOrderPasteActions.Replace,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    action,
                    StoreOrderPasteActions.Append,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    action,
                    StoreOrderPasteActions.Skip,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private async Task SyncOrderImportPriceToProductTablesAsync(
            string productCode,
            decimal importPrice
        )
        {
            var normalizedProductCode = productCode.Trim();
            if (string.IsNullOrWhiteSpace(normalizedProductCode) || importPrice <= 0)
            {
                return;
            }

            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
            var product = await _db.Queryable<Product>()
                .Where(item => item.ProductCode == normalizedProductCode && !item.IsDeleted)
                .FirstAsync();
            if (product == null)
            {
                throw new Exception($"Product {normalizedProductCode} not found");
            }

            product.PurchasePrice = importPrice;
            product.UpdatedAt = now;
            product.UpdatedBy = currentUser;
            await _db.Updateable(product).ExecuteCommandAsync();

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .Where(item => item.ProductCode == normalizedProductCode && !item.IsDeleted)
                .FirstAsync();
            if (warehouseProduct == null)
            {
                warehouseProduct = new WarehouseProduct
                {
                    ProductCode = normalizedProductCode,
                    OEMPrice = 0,
                    ImportPrice = importPrice,
                    MinOrderQuantity = 1,
                    IsActive = product.IsActive,
                    CreatedAt = now,
                    CreatedBy = currentUser,
                    UpdatedAt = now,
                    UpdatedBy = currentUser,
                    IsDeleted = false,
                };
                await _db.Insertable(warehouseProduct).ExecuteCommandAsync();
            }
            else
            {
                warehouseProduct.ImportPrice = importPrice;
                warehouseProduct.UpdatedAt = now;
                warehouseProduct.UpdatedBy = currentUser;
                await _db.Updateable(warehouseProduct).ExecuteCommandAsync();
            }

            await UpsertActiveStoreRetailPurchasePricesAsync(
                product,
                normalizedProductCode,
                importPrice,
                now,
                currentUser
            );
        }

        private async Task UpsertActiveStoreRetailPurchasePricesAsync(
            Product product,
            string productCode,
            decimal importPrice,
            DateTime now,
            string currentUser
        )
        {
            var activeStoreCodes = (await _db.Queryable<Store>()
                    .Where(store => store.IsActive && !store.IsDeleted)
                    .Select(store => store.StoreCode)
                    .ToListAsync())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (activeStoreCodes.Count == 0)
            {
                return;
            }

            var existingPrices = await _db.Queryable<StoreRetailPrice>()
                .Where(price =>
                    price.ProductCode == productCode
                    && price.StoreCode != null
                    && activeStoreCodes.Contains(price.StoreCode)
                    && !price.IsDeleted
                )
                .ToListAsync();
            var existingStoreCodes = existingPrices
                .Select(price => price.StoreCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var price in existingPrices)
            {
                // 订货单同步只覆盖进货价，分店零售价保持人工/策略设置不变。
                price.PurchasePrice = importPrice;
                price.UpdatedAt = now;
                price.UpdatedBy = currentUser;
            }

            if (existingPrices.Count > 0)
            {
                await _db.Updateable(existingPrices).ExecuteCommandAsync();
            }

            var pricesToInsert = activeStoreCodes
                .Where(storeCode => !existingStoreCodes.Contains(storeCode))
                .Select(storeCode => new StoreRetailPrice
                {
                    UUID = UuidHelper.GenerateUuid7(),
                    StoreCode = storeCode,
                    ProductCode = productCode,
                    StoreProductCode = storeCode + productCode,
                    SupplierCode = product.LocalSupplierCode,
                    PurchasePrice = importPrice,
                    StoreRetailPriceValue = product.RetailPrice,
                    DiscountRate = null,
                    IsActive = product.IsActive,
                    IsAutoPricing = product.IsAutoPricing,
                    IsSpecialProduct = product.IsSpecialProduct,
                    CreatedAt = now,
                    CreatedBy = currentUser,
                    UpdatedAt = now,
                    UpdatedBy = currentUser,
                    IsDeleted = false,
                })
                .ToList();

            if (pricesToInsert.Count > 0)
            {
                await _db.Insertable(pricesToInsert).ExecuteCommandAsync();
            }
        }

        private async Task AddOrUpdateDetailAsync(
            WareHouseOrder order,
            string productCode,
            decimal quantity,
            decimal? importPrice, // 新增参数
            bool isUpdate,
            bool isBatch = false, // 新增参数
            decimal? originalQuantity = null // 新增参数
        )
        {
            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

            var warehouseProduct = await _db.Queryable<WarehouseProduct>()
                .Where(wp => wp.ProductCode == productCode)
                .FirstAsync();

            if (warehouseProduct == null)
            {
                // 如果找不到 WarehouseProduct，尝试找 Product
                var prod = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .FirstAsync();
                if (prod == null)
                    throw new Exception($"Product {productCode} not found");
                // 如果没有 WarehouseProduct 数据，使用默认值
                warehouseProduct = new WarehouseProduct
                {
                    OEMPrice = 0,
                    ImportPrice = 0,
                    MinOrderQuantity = 1,
                };
            }

            // 检查最小起订量
            var minQty = warehouseProduct.MinOrderQuantity ?? 1;
            if (minQty < 1)
                minQty = 1;

            if (isUpdate)
            {
                // 如果是批量操作且未提供数量，跳过数量检查
                if (isBatch && originalQuantity == null)
                {
                    // Do nothing for quantity check
                }
                else if (quantity < 0)
                {
                    throw new Exception($"商品数量 {quantity}");
                }
            }
            else
            {
                // 添加模式（累加）
                // ... (原有逻辑保持不变，但要注意 importPrice)
            }

            decimal price = warehouseProduct.OEMPrice ?? 0;
            // 优先使用传入的 importPrice，否则使用 warehouseProduct 的
            decimal finalImportPrice = importPrice ?? warehouseProduct.ImportPrice ?? 0;

            var detail = await _db.Queryable<WareHouseOrderDetails>()
                .Where(d =>
                    d.OrderGUID == order.OrderGUID && d.ProductCode == productCode && !d.IsDeleted
                )
                .FirstAsync();

            if (detail == null)
            {
                // 新增
                detail = new WareHouseOrderDetails
                {
                    DetailGUID = UuidHelper.GenerateUuid7(),
                    OrderGUID = order.OrderGUID,
                    StoreCode = order.StoreCode,
                    ProductCode = productCode,
                    Quantity = 0,
                    OEMPrice = price,
                    OEMAmount = price * minQty,
                    AllocQuantity = minQty,
                    ImportPrice = finalImportPrice,
                    ImportAmount = 0,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = currentUser,
                    UpdatedBy = currentUser,
                };
                await _db.Insertable(detail).ExecuteCommandAsync();
            }
            else
            {
                // 更新
                if (isUpdate)
                {
                    // 如果传入了数量 (对于批量操作，originalQuantity 不为 null)
                    if (!isBatch || originalQuantity != null)
                    {
                        detail.AllocQuantity = quantity;
                    }

                    // 如果传入了 importPrice，更新它
                    if (importPrice.HasValue)
                    {
                        detail.ImportPrice = importPrice.Value;
                    }
                }
                else
                {
                    detail.AllocQuantity += minQty;
                }

                if (detail.AllocQuantity <= 0 && detail.Quantity <= 0)
                { //订货数量和发货数量都为空 可以删除
                    await SoftDeleteOrderDetailAsync(detail, currentUser, now);
                }
                else
                {
                    detail.OEMAmount = detail.AllocQuantity * detail.OEMPrice;
                    // 使用最新的 ImportPrice 计算订货金额；发货金额走 DTO 派生。
                    detail.ImportAmount = CalculateOrderImportAmount(detail.Quantity, detail.ImportPrice);
                    detail.UpdatedAt = now;
                    detail.UpdatedBy = currentUser;
                    await _db.Updateable(detail).ExecuteCommandAsync();
                }
            }
        }

        private async Task SoftDeleteOrderDetailAsync(
            WareHouseOrderDetails detail,
            string currentUser,
            DateTime? now = null
        )
        {
            detail.IsDeleted = true;
            detail.UpdatedAt = now ?? DateTime.Now;
            detail.UpdatedBy = currentUser;
            await _db.Updateable(detail).ExecuteCommandAsync();
        }

        public async Task<SyncMissingOrdersResultDto> SyncMissingOrdersFromHqAsync(
            SyncMissingOrdersRequestDto? request
        )
        {
            var result = new SyncMissingOrdersResultDto { Success = true, Message = string.Empty };
            var storeCodes = NormalizeSyncStoreCodes(request);
            var hasStoreFilter = storeCodes.Count > 0;
            const int batchSize = 200;

            try
            {
                var localOrders = await _db.Queryable<WareHouseOrder>()
                    .WhereIF(hasStoreFilter, x => storeCodes.Contains(x.StoreCode!))
                    .Select(x => new { x.OrderGUID, x.StoreCode, x.UpdatedAt, x.IsDeleted })
                    .ToListAsync();

                var activeOrderGuids = localOrders
                    .Where(x => !x.IsDeleted)
                    .Select(x => x.OrderGUID)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var deletedOrderGuids = localOrders
                    .Where(x => x.IsDeleted)
                    .Select(x => x.OrderGUID)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var allLocalOrderGuids = localOrders
                    .Select(x => x.OrderGUID)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var localUpdatedAtMap = localOrders
                    .Where(x => x.UpdatedAt.HasValue)
                    .ToDictionary(x => x.OrderGUID, x => x.UpdatedAt!.Value, StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation(
                    "本地已存在订单数量: {Count}, 分店代码: {StoreCodes}",
                    allLocalOrderGuids.Count,
                    hasStoreFilter ? string.Join(",", storeCodes) : "全部"
                );

                using var hqDb = _createHqConnection();

                var allHqOrders = await hqDb.Queryable<CBP_RED_分店订货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .WhereIF(hasStoreFilter, x => storeCodes.Contains(x.分店代码!))
                    .ToListAsync();

                if (!allHqOrders.Any())
                {
                    _logger.LogInformation(
                        "HQ 订单读取完成：总数 0，数字分店订单 0，外购客户订单 0，分店筛选 {StoreCodes}",
                        hasStoreFilter ? string.Join(",", storeCodes) : "全部"
                    );
                    result.Message = "没有需要同步的订单";
                    return result;
                }

                allHqOrders = allHqOrders
                    .GroupBy(x => x.HGUID, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                var hqExternalCustomerOrderCount = allHqOrders.Count(x =>
                    Guid.TryParse(x.分店代码, out _)
                );
                _logger.LogInformation(
                    "HQ 订单读取完成：总数 {Total}，数字分店订单 {StoreOrderCount}，外购客户订单 {ExternalCustomerOrderCount}，分店筛选 {StoreCodes}",
                    allHqOrders.Count,
                    allHqOrders.Count - hqExternalCustomerOrderCount,
                    hqExternalCustomerOrderCount,
                    hasStoreFilter ? string.Join(",", storeCodes) : "全部"
                );

                var allHqOrderGuids = allHqOrders
                    .Select(x => x.HGUID!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 先读取轻量明细指纹行，精确判定目标订单；完整明细只在确定有差异后拉取。
                var hqDetailFingerprints = await QueryHqOrderDetailFingerprintsAsync(
                    hqDb,
                    allHqOrderGuids
                );
                var hqOrderStoreCodeMap = allHqOrders
                    .ToDictionary(x => x.HGUID!, x => x.分店代码, StringComparer.OrdinalIgnoreCase);
                var hqDetailOrderCount = hqDetailFingerprints
                    .Where(x => !string.IsNullOrWhiteSpace(x.OrderGuid))
                    .Select(x => x.OrderGuid!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var hqExternalCustomerFingerprintCount = hqDetailFingerprints.Count(x =>
                    !string.IsNullOrWhiteSpace(x.OrderGuid)
                    && hqOrderStoreCodeMap.TryGetValue(x.OrderGuid, out var storeCode)
                    && Guid.TryParse(storeCode, out _)
                );
                _logger.LogInformation(
                    "HQ 轻量明细指纹读取完成：订单 {OrderCount} 个，有明细订单 {DetailOrderCount} 个，轻量明细 {DetailCount} 条，外购客户轻量明细 {ExternalCustomerDetailCount} 条",
                    allHqOrderGuids.Count,
                    hqDetailOrderCount,
                    hqDetailFingerprints.Count,
                    hqExternalCustomerFingerprintCount
                );

                var localDetailFingerprints = await QueryLocalOrderDetailFingerprintsAsync(
                    allHqOrderGuids
                );
                _logger.LogInformation(
                    "本地轻量明细指纹读取完成：轻量明细 {DetailCount} 条",
                    localDetailFingerprints.Count
                );

                var detailChangedOrderGuids = GetDetailChangedOrderGuids(
                    hqDetailFingerprints,
                    localDetailFingerprints
                );

                var missingHqOrders = allHqOrders
                    .Where(x => !allLocalOrderGuids.Contains(x.HGUID!))
                    .ToList();
                var missingOrderGuidSet = missingHqOrders
                    .Select(x => x.HGUID!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var reactivatedHqOrders = allHqOrders
                    .Where(x => deletedOrderGuids.Contains(x.HGUID!))
                    .ToList();
                var reactivatedOrderGuidSet = reactivatedHqOrders
                    .Select(x => x.HGUID!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var updatedHqOrders = allHqOrders
                    .Where(x => activeOrderGuids.Contains(x.HGUID!))
                    .Where(x => IsHqOrderNewerThanLocal(x, localUpdatedAtMap))
                    .ToList();
                var updatedOrderGuidSet = updatedHqOrders
                    .Select(x => x.HGUID!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var detailOnlyOrderGuidSet = detailChangedOrderGuids
                    .Where(orderGuid =>
                        activeOrderGuids.Contains(orderGuid) && !updatedOrderGuidSet.Contains(orderGuid)
                    )
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var targetOrderGuidSet = missingOrderGuidSet
                    .Concat(reactivatedOrderGuidSet)
                    .Concat(updatedOrderGuidSet)
                    .Concat(detailOnlyOrderGuidSet)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation(
                    "分店订货同步目标判定完成：新增 {NewCount} 个，恢复 {RestoreCount} 个，主表更新 {OrderUpdatedCount} 个，明细变更 {DetailOnlyCount} 个，目标订单 {TargetCount} 个",
                    missingOrderGuidSet.Count,
                    reactivatedOrderGuidSet.Count,
                    updatedOrderGuidSet.Count,
                    detailOnlyOrderGuidSet.Count,
                    targetOrderGuidSet.Count
                );

                if (targetOrderGuidSet.Count == 0)
                {
                    _logger.LogInformation(
                        "分店订货同步无需拉取完整明细：目标订单为空，分店筛选 {StoreCodes}",
                        hasStoreFilter ? string.Join(",", storeCodes) : "全部"
                    );
                    result.Message = "所有订单已是最新，无需同步";
                    return result;
                }

                var targetHqOrders = allHqOrders
                    .Where(x => targetOrderGuidSet.Contains(x.HGUID!))
                    .ToList();

                var targetOrderGuids = targetHqOrders
                    .Select(x => x.HGUID!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var hqDetails = await QueryHqOrderDetailsAsync(hqDb, targetOrderGuids);
                var hqExternalCustomerDetailCount = hqDetails.Count(x =>
                    Guid.TryParse(x.分店代码, out _)
                );
                _logger.LogInformation(
                    "HQ 目标订单完整明细读取完成：目标订单 {OrderCount} 个，明细 {DetailCount} 条，外购客户明细 {ExternalCustomerDetailCount} 条",
                    targetOrderGuids.Count,
                    hqDetails.Count,
                    hqExternalCustomerDetailCount
                );
                var hqDetailsByOrder = hqDetails
                    .GroupBy(x => x.主表GUID!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                var localDetails = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(x => targetOrderGuids.Contains(x.OrderGUID!))
                    .ToListAsync();
                var localDetailByGuid = localDetails
                    .GroupBy(x => x.DetailGUID, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var ordersSynced = 0;
                var ordersUpdated = 0;
                var detailsSynced = 0;
                var detailsUpdated = 0;

                var transactionResult = await _db.Ado.UseTranAsync(async () =>
                {
                    var ordersToInsert = new List<WareHouseOrder>();
                    var ordersToUpdate = new List<WareHouseOrder>();

                    foreach (var hqOrder in targetHqOrders)
                    {
                        var order = MapHqOrder(hqOrder);

                        if (missingOrderGuidSet.Contains(hqOrder.HGUID!))
                        {
                            ordersToInsert.Add(order);
                        }
                        else if (
                            reactivatedOrderGuidSet.Contains(hqOrder.HGUID!)
                            || updatedOrderGuidSet.Contains(hqOrder.HGUID!)
                        )
                        {
                            ordersToUpdate.Add(order);
                        }
                    }

                    var detailsToInsert = new List<WareHouseOrderDetails>();
                    var detailsToUpdate = new List<WareHouseOrderDetails>();

                    foreach (var hqDetail in targetHqOrders
                        .SelectMany(order =>
                            hqDetailsByOrder.TryGetValue(order.HGUID!, out var details)
                                ? details
                                : new List<CBP_RED_分店订单详情表Store>()
                        )
                        .GroupBy(x => x.HGUID, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First()))
                    {
                        var detail = MapHqDetail(hqDetail);
                        if (!localDetailByGuid.TryGetValue(detail.DetailGUID, out var localDetail))
                        {
                            detailsToInsert.Add(detail);
                        }
                        else if (IsHqDetailChanged(hqDetail, localDetail))
                        {
                            detailsToUpdate.Add(detail);
                        }
                    }

                    ordersSynced = ordersToInsert.Count + reactivatedOrderGuidSet.Count;
                    ordersUpdated = ordersToUpdate.Count - reactivatedOrderGuidSet.Count;
                    detailsSynced = detailsToInsert.Count;
                    detailsUpdated = detailsToUpdate.Count;

                    _logger.LogInformation(
                        "分店订货同步准备写入：新增订单 {InsertOrderCount} 个，更新订单 {UpdateOrderCount} 个，新增明细 {InsertDetailCount} 条，更新明细 {UpdateDetailCount} 条，批大小 {BatchSize}",
                        ordersToInsert.Count,
                        ordersToUpdate.Count,
                        detailsToInsert.Count,
                        detailsToUpdate.Count,
                        batchSize
                    );

                    await ExecuteInsertInBatchesAsync(ordersToInsert, batchSize);
                    await ExecuteUpdateInBatchesAsync(ordersToUpdate, batchSize);
                    await ExecuteInsertInBatchesAsync(detailsToInsert, batchSize);
                    await ExecuteUpdateInBatchesAsync(detailsToUpdate, batchSize);
                });

                if (!transactionResult.IsSuccess)
                {
                    var transactionError = transactionResult.ErrorException;
                    throw new InvalidOperationException(
                        transactionError?.Message ?? "同步订单事务失败",
                        transactionError
                    );
                }

                result.OrdersSynced = ordersSynced;
                result.OrdersUpdated = ordersUpdated;
                result.DetailsSynced = detailsSynced;
                result.DetailsUpdated = detailsUpdated;
                _logger.LogInformation(
                    "分店订货同步完成：新增订单 {OrdersSynced}、更新订单 {OrdersUpdated}、新增明细 {DetailsSynced}、更新明细 {DetailsUpdated}",
                    result.OrdersSynced,
                    result.OrdersUpdated,
                    result.DetailsSynced,
                    result.DetailsUpdated
                );

                var hasChanges =
                    result.OrdersSynced > 0
                    || result.DetailsSynced > 0
                    || result.OrdersUpdated > 0
                    || result.DetailsUpdated > 0;

                if (hasChanges)
                {
                    result.Message =
                        $"同步成功：新增订单 {result.OrdersSynced} 条、详情 {result.DetailsSynced} 条；"
                        + $"更新订单 {result.OrdersUpdated} 条、详情 {result.DetailsUpdated} 条";
                }
                else
                {
                    result.Message = "所有订单已是最新，无需同步";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"同步失败：{ex.Message}";
                _logger.LogError(
                    ex,
                    "同步缺失订单失败，分店代码：{StoreCodes}",
                    storeCodes.Count > 0 ? string.Join(",", storeCodes) : "全部"
                );
                return result;
            }
        }

        private static List<string> NormalizeSyncStoreCodes(SyncMissingOrdersRequestDto? request)
        {
            var source =
                request?.StoreCodes?.Where(item => !string.IsNullOrWhiteSpace(item))
                ?? Enumerable.Empty<string>();
            var storeCodes = source
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (storeCodes.Count == 0 && !string.IsNullOrWhiteSpace(request?.StoreCode))
            {
                storeCodes.Add(request.StoreCode.Trim());
            }

            return storeCodes;
        }

        private async Task<List<CBP_RED_分店订单详情表Store>> QueryHqOrderDetailsAsync(
            ISqlSugarClient hqDb,
            List<string> orderGuids
        )
        {
            if (orderGuids.Count == 0)
            {
                return new List<CBP_RED_分店订单详情表Store>();
            }

            var result = new List<CBP_RED_分店订单详情表Store>();
            foreach (var batch in orderGuids.Chunk(500))
            {
                var batchOrderGuids = batch.ToList();
                var batchRows = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID) && SqlFunc.HasValue(x.主表GUID))
                    .Where(x => batchOrderGuids.Contains(x.主表GUID!))
                    .ToListAsync();
                result.AddRange(batchRows);
            }

            return result;
        }

        private async Task<List<HqOrderDetailFingerprint>> QueryHqOrderDetailFingerprintsAsync(
            ISqlSugarClient hqDb,
            List<string> orderGuids
        )
        {
            if (orderGuids.Count == 0)
            {
                return new List<HqOrderDetailFingerprint>();
            }

            var result = new List<HqOrderDetailFingerprint>();
            foreach (var batch in orderGuids.Chunk(500))
            {
                var batchOrderGuids = batch.ToList();
                var batchRows = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID) && SqlFunc.HasValue(x.主表GUID))
                    .Where(x => batchOrderGuids.Contains(x.主表GUID!))
                    .Select(x => new HqOrderDetailFingerprint
                    {
                        DetailGuid = x.HGUID,
                        OrderGuid = x.主表GUID,
                        StoreCode = x.分店代码,
                        StoreProductCode = x.分店商品编码,
                        ProductCode = x.商品编码,
                        Quantity = x.数量,
                        AllocQuantity = x.配货数量,
                        LastCost = x.上次成本,
                        ImportPrice = x.进口价格,
                        ImportAmount = x.合计进口金额,
                        OemPrice = x.贴牌价格,
                        OemAmount = x.合计贴牌金额,
                        UpdatedAt = x.FGC_LastModifyDate,
                    })
                    .ToListAsync();
                result.AddRange(batchRows);
            }

            return result;
        }

        private async Task<List<LocalOrderDetailFingerprint>> QueryLocalOrderDetailFingerprintsAsync(
            List<string> orderGuids
        )
        {
            if (orderGuids.Count == 0)
            {
                return new List<LocalOrderDetailFingerprint>();
            }

            var result = new List<LocalOrderDetailFingerprint>();

            foreach (var batch in orderGuids.Chunk(500))
            {
                var batchOrderGuids = batch.ToList();
                var rows = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(x => batchOrderGuids.Contains(x.OrderGUID!))
                    .Select(x => new LocalOrderDetailFingerprint
                    {
                        DetailGuid = x.DetailGUID,
                        OrderGuid = x.OrderGUID,
                        StoreCode = x.StoreCode,
                        StoreProductCode = x.StoreProductCode,
                        ProductCode = x.ProductCode,
                        Quantity = x.Quantity,
                        AllocQuantity = x.AllocQuantity,
                        LastCost = x.LastCost,
                        ImportPrice = x.ImportPrice,
                        ImportAmount = x.ImportAmount,
                        OemPrice = x.OEMPrice,
                        OemAmount = x.OEMAmount,
                        UpdatedAt = x.UpdatedAt,
                        IsDeleted = x.IsDeleted,
                    })
                    .ToListAsync();

                result.AddRange(rows);
            }

            return result;
        }

        private static bool IsHqOrderNewerThanLocal(
            CBP_RED_分店订货单主表Store hqOrder,
            Dictionary<string, DateTime> localUpdatedAtMap
        )
        {
            if (!localUpdatedAtMap.TryGetValue(hqOrder.HGUID!, out var localUpdated))
            {
                return true;
            }

            return hqOrder.FGC_LastModifyDate.HasValue
                && hqOrder.FGC_LastModifyDate.Value > localUpdated;
        }

        private static HashSet<string> GetDetailChangedOrderGuids(
            List<HqOrderDetailFingerprint> hqDetails,
            List<LocalOrderDetailFingerprint> localDetails
        )
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localDetailMap = localDetails
                .Where(x => !string.IsNullOrWhiteSpace(x.DetailGuid))
                .GroupBy(x => x.DetailGuid!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var hqDetail in hqDetails)
            {
                if (
                    string.IsNullOrWhiteSpace(hqDetail.OrderGuid)
                    || string.IsNullOrWhiteSpace(hqDetail.DetailGuid)
                )
                {
                    continue;
                }

                if (!localDetailMap.TryGetValue(hqDetail.DetailGuid, out var localDetail))
                {
                    result.Add(hqDetail.OrderGuid);
                    continue;
                }

                if (IsHqDetailFingerprintChanged(hqDetail, localDetail))
                {
                    result.Add(hqDetail.OrderGuid);
                }
            }

            return result;
        }

        private static bool IsHqDetailFingerprintChanged(
            HqOrderDetailFingerprint hqDetail,
            LocalOrderDetailFingerprint localDetail
        )
        {
            if (localDetail.IsDeleted || !localDetail.UpdatedAt.HasValue)
            {
                return true;
            }

            if (
                hqDetail.UpdatedAt.HasValue
                && (
                    !localDetail.UpdatedAt.HasValue
                    || hqDetail.UpdatedAt.Value > localDetail.UpdatedAt.Value
                )
            )
            {
                return true;
            }

            // HQ 明细偶发只改字段、不推进 FGC_LastModifyDate；轻量行逐字段精确比对兜底。
            return !SameText(localDetail.OrderGuid, TrimLen(hqDetail.OrderGuid, 50))
                || !SameText(localDetail.StoreCode, TrimLen(hqDetail.StoreCode, 50))
                || !SameText(localDetail.StoreProductCode, TrimLen(hqDetail.StoreProductCode, 50))
                || !SameText(localDetail.ProductCode, TrimLen(hqDetail.ProductCode, 50))
                || !SameDecimal(localDetail.Quantity, hqDetail.Quantity)
                || !SameDecimal(localDetail.AllocQuantity, hqDetail.AllocQuantity)
                || !SameDecimal(localDetail.LastCost, hqDetail.LastCost)
                || !SameDecimal(localDetail.ImportPrice, hqDetail.ImportPrice)
                || !SameDecimal(localDetail.ImportAmount, hqDetail.ImportAmount)
                || !SameDecimal(localDetail.OemPrice, hqDetail.OemPrice)
                || !SameDecimal(localDetail.OemAmount, hqDetail.OemAmount);
        }

        private static bool IsHqDetailChanged(
            CBP_RED_分店订单详情表Store hqDetail,
            WareHouseOrderDetails localDetail
        )
        {
            if (localDetail.IsDeleted || !localDetail.UpdatedAt.HasValue)
            {
                return true;
            }

            if (
                hqDetail.FGC_LastModifyDate.HasValue
                && hqDetail.FGC_LastModifyDate.Value > localDetail.UpdatedAt.Value
            )
            {
                return true;
            }

            return !SameText(localDetail.OrderGUID, TrimLen(hqDetail.主表GUID, 50))
                || !SameText(localDetail.StoreCode, TrimLen(hqDetail.分店代码, 50))
                || !SameText(localDetail.StoreProductCode, TrimLen(hqDetail.分店商品编码, 50))
                || !SameText(localDetail.ProductCode, TrimLen(hqDetail.商品编码, 50))
                || !SameDecimal(localDetail.Quantity, hqDetail.数量)
                || !SameDecimal(localDetail.AllocQuantity, hqDetail.配货数量)
                || !SameDecimal(localDetail.LastCost, hqDetail.上次成本)
                || !SameDecimal(localDetail.ImportPrice, hqDetail.进口价格)
                || !SameDecimal(localDetail.ImportAmount, hqDetail.合计进口金额)
                || !SameDecimal(localDetail.OEMPrice, hqDetail.贴牌价格)
                || !SameDecimal(localDetail.OEMAmount, hqDetail.合计贴牌金额);
        }

        private static bool SameText(string? left, string? right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameDecimal(decimal? left, decimal? right)
        {
            return left == right;
        }

        private async Task<Store?> GetStoreByCodeOrGuidAsync(string? storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return null;
            }

            var normalizedStoreCode = storeCode.Trim();
            return await _db.Queryable<Store>()
                .Where(s =>
                    !s.IsDeleted
                    && (s.StoreCode == normalizedStoreCode || s.StoreGUID == normalizedStoreCode)
                )
                .FirstAsync();
        }

        private static bool DoesOrderMatchStore(WareHouseOrder order, Store store)
        {
            return SameText(order.StoreCode, store.StoreCode) || SameText(order.StoreCode, store.StoreGUID);
        }

        private static string? NormalizeOptionalEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            return email.Trim();
        }

        private static string? TrimLen(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private WareHouseOrder MapHqOrder(CBP_RED_分店订货单主表Store hqOrder)
        {
            var order = _mapper.Map<WareHouseOrder>(hqOrder);
            order.IsDeleted = false;
            order.CreatedAt = hqOrder.FGC_CreateDate ?? DateTime.Now;
            order.UpdatedAt = hqOrder.FGC_LastModifyDate ?? DateTime.Now;
            order.CreatedBy = hqOrder.FGC_Creator ?? "HQ同步";
            order.UpdatedBy = hqOrder.FGC_LastModifier ?? "HQ同步";
            return order;
        }

        private WareHouseOrderDetails MapHqDetail(CBP_RED_分店订单详情表Store hqDetail)
        {
            var detail = _mapper.Map<WareHouseOrderDetails>(hqDetail);
            detail.IsDeleted = false;
            detail.CreatedAt = hqDetail.FGC_CreateDate ?? DateTime.Now;
            detail.UpdatedAt = hqDetail.FGC_LastModifyDate ?? DateTime.Now;
            detail.CreatedBy = hqDetail.FGC_Creator ?? "HQ同步";
            detail.UpdatedBy = hqDetail.FGC_LastModifier ?? "HQ同步";
            return detail;
        }

        // 分批写入，避免单次 SQL 过大，同时保留外层事务统一提交/回滚。
        private async Task ExecuteInsertInBatchesAsync<T>(List<T> entities, int size)
            where T : class, new()
        {
            foreach (var batch in entities.Chunk(size))
            {
                await _db.Insertable(batch.ToList()).ExecuteCommandAsync();
            }
        }

        // 分批更新，沿用现有整实体覆盖方式，但把数据库往返次数压到更低。
        private async Task ExecuteUpdateInBatchesAsync<T>(List<T> entities, int size)
            where T : class, new()
        {
            foreach (var batch in entities.Chunk(size))
            {
                await _db.Updateable(batch.ToList()).ExecuteCommandAsync();
            }
        }

        private sealed class HqOrderDetailFingerprint
        {
            public string? DetailGuid { get; set; }
            public string? OrderGuid { get; set; }
            public string? StoreCode { get; set; }
            public string? StoreProductCode { get; set; }
            public string? ProductCode { get; set; }
            public decimal? Quantity { get; set; }
            public decimal? AllocQuantity { get; set; }
            public decimal? LastCost { get; set; }
            public decimal? ImportPrice { get; set; }
            public decimal? ImportAmount { get; set; }
            public decimal? OemPrice { get; set; }
            public decimal? OemAmount { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }

        private sealed class LocalOrderDetailFingerprint
        {
            public string? DetailGuid { get; set; }
            public string? OrderGuid { get; set; }
            public string? StoreCode { get; set; }
            public string? StoreProductCode { get; set; }
            public string? ProductCode { get; set; }
            public decimal? Quantity { get; set; }
            public decimal? AllocQuantity { get; set; }
            public decimal? LastCost { get; set; }
            public decimal? ImportPrice { get; set; }
            public decimal? ImportAmount { get; set; }
            public decimal? OemPrice { get; set; }
            public decimal? OemAmount { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public bool IsDeleted { get; set; }
        }

        public async Task<ApiResponse<bool>> CompleteOrderAsync(string orderGuid)
        {
            try
            {
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "订单不存在" };
                }

                if (order.FlowStatus != 1)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "只有已提交状态的订单才能标记为完成",
                    };
                }

                var updatedAt = DateTime.Now;
                var updatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                var affected = await _db.Updateable<WareHouseOrder>()
                    .SetColumns(o => new WareHouseOrder
                    {
                        FlowStatus = 2,
                        UpdatedAt = updatedAt,
                        UpdatedBy = updatedBy,
                    })
                    .Where(o =>
                        o.OrderGUID == orderGuid
                        && !o.IsDeleted
                        && o.FlowStatus == 1
                    )
                    .ExecuteCommandAsync();
                if (affected != 1)
                {
                    return CreateOrderStatusConflictResponse<bool>();
                }

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CompleteOrderAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> StartPickingAsync(string orderGuid)
        {
            try
            {
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "订单不存在" };
                }

                if (order.FlowStatus == 2 || order.FlowStatus == 3)
                {
                    return new ApiResponse<bool> { Success = true, Data = true };
                }

                if (order.FlowStatus != 1)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "只有已提交状态的订单才能开始配货",
                    };
                }

                var updatedAt = DateTime.Now;
                var updatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                var affected = await _db.Updateable<WareHouseOrder>()
                    .SetColumns(o => new WareHouseOrder
                    {
                        FlowStatus = 3,
                        UpdatedAt = updatedAt,
                        UpdatedBy = updatedBy,
                    })
                    .Where(o =>
                        o.OrderGUID == orderGuid
                        && !o.IsDeleted
                        && o.FlowStatus == 1
                    )
                    .ExecuteCommandAsync();
                if (affected != 1)
                {
                    return CreateOrderStatusConflictResponse<bool>();
                }

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartPickingAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateOrderStatusAsync(
            string orderGuid,
            int newStatus,
            bool bypassPreorderGate = false
        )
        {
            try
            {
                if (newStatus != 1 && newStatus != 2)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message =
                            "Invalid status. Only 1 (Submitted) or 2 (Completed) are allowed.",
                    };
                }

                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                    .FirstAsync();

                if (order == null)
                {
                    return new ApiResponse<bool> { Success = false, Message = "Order not found" };
                }

                var canBypassPreorderGate = bypassPreorderGate || IsWarehouseStaffOnly();
                if (order.FlowStatus == newStatus)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Status is already the target status",
                    };
                }
                var transitionError = ValidateOrderStatusTransition(
                    order.FlowStatus,
                    newStatus,
                    canBypassPreorderGate
                );
                if (transitionError.HasValue)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        ErrorCode = transitionError.Value.ErrorCode,
                        Message = transitionError.Value.Message,
                    };
                }

                var userId = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                _logger.LogInformation(
                    "Updating order {OrderGUID} status from {OldStatus} to {NewStatus}",
                    orderGuid,
                    order.FlowStatus,
                    newStatus
                );

                var updatedAt = DateTime.Now;
                var affected = await _db.Updateable<WareHouseOrder>()
                    .SetColumns(o => new WareHouseOrder
                    {
                        FlowStatus = newStatus,
                        UpdatedAt = updatedAt,
                        UpdatedBy = userId,
                    })
                    .Where(o =>
                        o.OrderGUID == orderGuid
                        && !o.IsDeleted
                        && o.FlowStatus == order.FlowStatus
                    )
                    .ExecuteCommandAsync();
                if (affected != 1)
                {
                    return CreateOrderStatusConflictResponse<bool>();
                }

                var statusText = newStatus == 1 ? "Submitted" : "Completed";
                return new ApiResponse<bool>
                {
                    Success = true,
                    Data = true,
                    Message = $"Status changed to {statusText}",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "UpdateOrderStatusAsync failed for order {OrderGUID}",
                    orderGuid
                );
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<int>> BatchUpdateOrderStatusAsync(
            List<string> orderGuids,
            int newStatus,
            bool bypassPreorderGate = false
        )
        {
            try
            {
                if (newStatus != 1 && newStatus != 2)
                {
                    return new ApiResponse<int>
                    {
                        Success = false,
                        Message =
                            "Invalid status. Only 1 (Submitted) or 2 (Completed) are allowed.",
                    };
                }

                if (orderGuids == null || orderGuids.Count == 0)
                {
                    return new ApiResponse<int>
                    {
                        Success = false,
                        Message = "No orders specified",
                    };
                }

                var canBypassPreorderGate = bypassPreorderGate || IsWarehouseStaffOnly();
                var distinctOrderGuids = orderGuids
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (distinctOrderGuids.Count == 0)
                {
                    return new ApiResponse<int>
                    {
                        Success = false,
                        Message = "No orders specified",
                    };
                }

                _logger.LogInformation(
                    "Batch updating {Count} orders to status {NewStatus}",
                    distinctOrderGuids.Count,
                    newStatus
                );
                ApiResponse<int>? response = null;
                var transaction = await _db.Ado.UseTranAsync(async () =>
                {
                    var orders = await _db.Queryable<WareHouseOrder>()
                        .Where(o => distinctOrderGuids.Contains(o.OrderGUID) && !o.IsDeleted)
                        .ToListAsync();
                    if (orders.Count != distinctOrderGuids.Count)
                    {
                        response = new ApiResponse<int>
                        {
                            Success = false,
                            ErrorCode = "ORDER_NOT_FOUND",
                            Message = "部分订单不存在或已删除",
                        };
                        return;
                    }

                    foreach (var order in orders)
                    {
                        if (order.FlowStatus == newStatus)
                        {
                            // 批量接口保持原有幂等兼容：同目标状态仍计入本次处理数量。
                            continue;
                        }
                        var transitionError = ValidateOrderStatusTransition(
                            order.FlowStatus,
                            newStatus,
                            canBypassPreorderGate
                        );
                        if (transitionError.HasValue)
                        {
                            response = new ApiResponse<int>
                            {
                                Success = false,
                                ErrorCode = transitionError.Value.ErrorCode,
                                Message = transitionError.Value.Message,
                            };
                            return;
                        }
                    }

                    var userId =
                        _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                    var updatedAt = DateTime.Now;
                    var updatedCount = 0;
                    // 按读取到的源状态分组 CAS；任一组数量不符都会抛错并回滚之前已更新的组。
                    foreach (var group in orders.GroupBy(item => item.FlowStatus))
                    {
                        var sourceStatus = group.Key;
                        var groupOrderGuids = group.Select(item => item.OrderGUID).ToList();
                        var affected = await _db.Updateable<WareHouseOrder>()
                            .SetColumns(o => new WareHouseOrder
                            {
                                FlowStatus = newStatus,
                                UpdatedAt = updatedAt,
                                UpdatedBy = userId,
                            })
                            .Where(o =>
                                groupOrderGuids.Contains(o.OrderGUID)
                                && !o.IsDeleted
                                && o.FlowStatus == sourceStatus
                            )
                            .ExecuteCommandAsync();
                        if (affected != groupOrderGuids.Count)
                        {
                            throw new OrderStatusConcurrencyException();
                        }
                        updatedCount += affected;
                    }

                    response = new ApiResponse<int>
                    {
                        Success = true,
                        Data = updatedCount,
                        Message = $"Updated {updatedCount} orders",
                    };
                });
                if (!transaction.IsSuccess)
                {
                    if (transaction.ErrorException is OrderStatusConcurrencyException)
                    {
                        return CreateOrderStatusConflictResponse<int>();
                    }
                    throw transaction.ErrorException
                        ?? new InvalidOperationException("批量更新订单状态事务失败");
                }
                return response
                    ?? new ApiResponse<int> { Success = false, Message = "批量更新订单状态失败" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateOrderStatusAsync failed");
                return new ApiResponse<int> { Success = false, Message = ex.Message };
            }
        }

        private static (string ErrorCode, string Message)? ValidateOrderStatusTransition(
            int? currentStatus,
            int targetStatus,
            bool canBypassPreorderGate
        )
        {
            if (targetStatus == 1)
            {
                if (currentStatus == 2 || (currentStatus == 0 && canBypassPreorderGate))
                {
                    return null;
                }
                if (currentStatus == 0)
                {
                    return (
                        "PREORDER_SUBMIT_ENDPOINT_REQUIRED",
                        "草稿订单必须通过正式提交接口提交，以完成 Preorder 门禁检查"
                    );
                }
            }
            else if (targetStatus == 2 && currentStatus == 1)
            {
                return null;
            }

            return (
                "INVALID_ORDER_STATUS_TRANSITION",
                currentStatus == 0 && targetStatus == 2
                    ? "草稿订单不能直接标记为已完成"
                    : "当前订单状态不允许切换到目标状态"
            );
        }

        private static ApiResponse<T> CreateOrderStatusConflictResponse<T>() => new()
        {
            Success = false,
            ErrorCode = "ORDER_STATUS_CONFLICT",
            Message = "订单状态已被其他操作更新，请刷新后重试",
        };

        private sealed class OrderStatusConcurrencyException : Exception
        {
            public OrderStatusConcurrencyException()
                : base("订单状态已被其他操作更新") { }
        }
    }
}
