using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreOrderReactService : IStoreOrderReactService
    {
        private const int HomePageWarmUpCommandTimeoutSeconds = 30;
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

            if (IsDefaultHomePageProductFilter(filter, normalizedGrades))
            {
                return await GetDefaultHomePageProductPageAsync(filter, normalizedGrades);
            }

            var q = CreateDefaultWarehouseProductQuery(_db);

            if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
            {
                var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
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

            if (!string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID))
            {
                var orderGuid = filter.ExcludeOrderGUID.Trim();
                q = q.Where(
                    (p, wp, wc, ls) =>
                        !SqlFunc.Subqueryable<WareHouseOrderDetails>()
                            .Where(d =>
                                d.OrderGUID == orderGuid
                                && d.ProductCode == p.ProductCode
                                && !d.IsDeleted
                            )
                            .Any()
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.ItemNumber))
            {
                var keyword = filter.ItemNumber.Trim().ToLower();
                q = q.Where(
                    (p, wp, wc, ls) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.ProductName))
            {
                var keyword = filter.ProductName.Trim().ToLower();
                q = q.Where(
                    (p, wp, wc, ls) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(keyword)
                );
            }

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

            if (!string.IsNullOrWhiteSpace(filter.SortBy))
            {
                switch (filter.SortBy.ToLower())
                {
                    case "priceasc":
                        q = q.OrderBy((p, wp, wc, ls) => wp.OEMPrice, OrderByType.Asc);
                        break;
                    case "pricedesc":
                        q = q.OrderBy((p, wp, wc, ls) => wp.OEMPrice, OrderByType.Desc);
                        break;
                    case "name":
                        q = q.OrderBy((p, wp, wc, ls) => p.ProductName, OrderByType.Asc);
                        break;
                    default:
                        q = q.OrderBy((p, wp, wc, ls) => p.ItemNumber, OrderByType.Asc);
                        break;
                }
            }
            else
            {
                q = q.OrderBy((p, wp, wc, ls) => p.ItemNumber, OrderByType.Asc);
            }

            var countSw = Stopwatch.StartNew();
            var total = await q.CountAsync();
            countSw.Stop();

            var listSw = Stopwatch.StartNew();
            var items = await q.Select(
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
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();
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

            if (!string.IsNullOrWhiteSpace(filter.ItemNumber))
            {
                var keyword = filter.ItemNumber.Trim().ToLower();
                q = q.Where(
                    (p, wc, ls) =>
                        (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.ProductName))
            {
                var keyword = filter.ProductName.Trim().ToLower();
                q = q.Where(
                    (p, wc, ls) =>
                        p.ProductName != null && p.ProductName.ToLower().Contains(keyword)
                );
            }

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

            if (!string.IsNullOrWhiteSpace(filter.SortBy))
            {
                switch (filter.SortBy.ToLower())
                {
                    case "priceasc":
                        q = q.OrderBy((p, wc, ls) => p.PurchasePrice, OrderByType.Asc);
                        break;
                    case "pricedesc":
                        q = q.OrderBy((p, wc, ls) => p.PurchasePrice, OrderByType.Desc);
                        break;
                    case "name":
                        q = q.OrderBy((p, wc, ls) => p.ProductName, OrderByType.Asc);
                        break;
                    default:
                        q = q.OrderBy((p, wc, ls) => p.ItemNumber, OrderByType.Asc);
                        break;
                }
            }
            else
            {
                q = q.OrderBy((p, wc, ls) => p.ItemNumber, OrderByType.Asc);
            }

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

        private ISugarQueryable<Product, WarehouseProduct, WarehouseCategory, HBLocalSupplier>
            CreateDefaultWarehouseProductQuery(ISqlSugarClient db)
        {
            return CreateDefaultWarehouseProductBaseQuery(db)
                .LeftJoin<WarehouseCategory>(
                    (p, wp, wc) => p.WarehouseCategoryGUID == wc.CategoryGUID
                )
                .LeftJoin<HBLocalSupplier>(
                    (p, wp, wc, ls) => p.LocalSupplierCode == ls.LocalSupplierCode && !ls.IsDeleted
                );
        }

        private ISugarQueryable<Product, WarehouseProduct> CreateDefaultWarehouseProductBaseQuery(
            ISqlSugarClient db
        )
        {
            return db.Queryable<Product>()
                .InnerJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode)
                .Where((p, wp) => p.IsActive && !p.IsDeleted && !wp.IsDeleted && wp.IsActive);
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

        private static bool IsDefaultHomePageProductFilter(
            StoreOrderFilterDto filter,
            List<string> normalizedGrades
        )
        {
            return string.IsNullOrWhiteSpace(filter.CategoryGUID)
                && string.IsNullOrWhiteSpace(filter.LocalSupplierCode)
                && string.IsNullOrWhiteSpace(filter.ItemNumber)
                && string.IsNullOrWhiteSpace(filter.ProductName)
                && string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID)
                && (
                    string.IsNullOrWhiteSpace(filter.SortBy)
                    || filter.SortBy.Equals("default", StringComparison.OrdinalIgnoreCase)
                )
                && normalizedGrades.Count == 0;
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
                    .Where(
                        (p, wp, wc, pg) => p.IsActive && !p.IsDeleted && !wp.IsDeleted && wp.IsActive
                    )
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

                // 保持数据库字段可走索引：扫码只做精确匹配，不再对字段执行 ToLower() 兜底。
                long fallbackQueryMs = 0;

                var buildSw = Stopwatch.StartNew();
                // ProductGrade 可能存在多行，扫码候选按商品去重，避免前端弹出重复商品。
                var distinctMatches = allMatches
                    .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();

                buildSw.Stop();

                _logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.service.done storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} matchType={MatchType} rawCount={RawCount} itemCount={ItemCount} exactQueryMs={ExactQueryMs} fallbackQueryMs={FallbackQueryMs} buildMs={BuildMs} totalMs={TotalMs}",
                    traceId,
                    request.StoreCode,
                    GetBarcodeTail(barcode),
                    GetBarcodeLength(barcode),
                    matchType ?? "none",
                    allMatches.Count,
                    distinctMatches.Count,
                    exactQuerySw.ElapsedMilliseconds,
                    fallbackQueryMs,
                    buildSw.ElapsedMilliseconds,
                    totalSw.ElapsedMilliseconds
                );

                return new ApiResponse<StoreOrderScanLookupResultDto>
                {
                    Success = true,
                    Data = new StoreOrderScanLookupResultDto
                    {
                        Barcode = barcode,
                        MatchType = matchType,
                        Items = distinctMatches,
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
                .LeftJoin<ProductGrade>(
                    (p, wp, wc, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted
                )
                .Where(
                    (p, wp, wc, pg) => p.IsActive && !p.IsDeleted && !wp.IsDeleted && wp.IsActive
                );

            query = matchField switch
            {
                "barcode" => query
                    .WhereIF(
                        useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc, pg) =>
                            // 生产库字段本身已是 CI 排序规则，直接等值比较才能稳定走条码索引。
                            p.Barcode != null && p.Barcode == barcode
                    )
                    .WhereIF(
                        !useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc, pg) => p.Barcode != null && lookupCodes.Contains(p.Barcode)
                    ),
                "itemNumber" => query
                    .WhereIF(
                        useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc, pg) =>
                            p.ItemNumber != null && p.ItemNumber == barcode
                    )
                    .WhereIF(
                        !useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc, pg) =>
                            p.ItemNumber != null && lookupCodes.Contains(p.ItemNumber)
                    ),
                "productCode" => query
                    .WhereIF(
                        useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc, pg) =>
                            p.ProductCode != null && p.ProductCode == barcode
                    )
                    .WhereIF(
                        !useSqlServerCaseInsensitiveCollation,
                        (p, wp, wc, pg) =>
                            p.ProductCode != null && lookupCodes.Contains(p.ProductCode)
                    ),
                _ => throw new ArgumentOutOfRangeException(nameof(matchField), matchField, null),
            };

            return await query
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
        }

        private List<string> GetAllSubCategoryIds(string categoryGuid)
        {
            try
            {
                var allCategories = _db.Queryable<WarehouseCategory>().ToList();
                var result = new List<string> { categoryGuid };
                GetSubCategoriesRecursive(categoryGuid, allCategories, result);
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
            List<WarehouseCategory> allCategories,
            List<string> result
        )
        {
            var children = allCategories.Where(c => c.ParentGUID == parentGuid).ToList();
            foreach (var child in children)
            {
                if (
                    !string.IsNullOrEmpty(child.CategoryGUID)
                    && !result.Contains(child.CategoryGUID)
                )
                {
                    result.Add(child.CategoryGUID);
                    GetSubCategoriesRecursive(child.CategoryGUID, allCategories, result);
                }
            }
        }

        public async Task<ApiResponse<StoreOrderCartDto?>> GetActiveCartAsync(string storeCode)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();

            // FlowStatus = 0 代表购物车
            var orderSw = Stopwatch.StartNew();
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted)
                .FirstAsync();
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

        public async Task<ApiResponse<StoreOrderCartDto?>> AddToCartAsync(AddToCartRequestDto request)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 1. 获取或创建订单 (FlowStatus = 0)
                var orderSw = Stopwatch.StartNew();
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    )
                    .FirstAsync();

                if (order == null)
                {
                    order = new WareHouseOrder
                    {
                        OrderGUID = UuidHelper.GenerateUuid7(),
                        StoreCode = request.StoreCode,
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

        public async Task<ApiResponse<StoreOrderCartDto?>> UpdateCartItemAsync(AddToCartRequestDto request)
        {
            var totalSw = Stopwatch.StartNew();
            var traceId = GetScanTraceId();
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                // 1. 获取购物车
                var orderSw = Stopwatch.StartNew();
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    )
                    .FirstAsync();

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

                // 2. 获取商品信息 (获取贴牌价)
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

        public async Task<ApiResponse<bool>> RemoveFromCartAsync(RemoveFromCartRequestDto request)
        {
            try
            {
                var detail = await _db.Queryable<WareHouseOrderDetails>()
                    .Where(d => d.DetailGUID == request.DetailGUID && !d.IsDeleted)
                    .FirstAsync();

                if (detail == null)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Cart item not found",
                    };
                }

                var orderGuid = detail.OrderGUID;
                await SoftDeleteOrderDetailAsync(
                    detail,
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System",
                    DateTime.Now
                );

                // 更新主表
                if (!string.IsNullOrEmpty(orderGuid))
                {
                    await UpdateOrderTotalAsync(orderGuid);
                }

                return new ApiResponse<bool> { Success = true, Data = true };
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
                var cart = await _db.Queryable<WareHouseOrder>()
                    .Where(o => o.StoreCode == storeCode && o.FlowStatus == 0 && !o.IsDeleted)
                    .FirstAsync();

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
                var order = await _db.Queryable<WareHouseOrder>()
                    .Where(o =>
                        o.StoreCode == request.StoreCode && o.FlowStatus == 0 && !o.IsDeleted
                    )
                    .FirstAsync();

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitOrder failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        private async Task UpdateOrderTotalAsync(string orderGuid)
        {
            // 只需要主表汇总时直接在数据库聚合，避免加购后把整车明细拉回内存。
            var totals = await _db.Queryable<WareHouseOrderDetails>()
                .Where(d => d.OrderGUID == orderGuid && !d.IsDeleted)
                .Select(d => new
                {
                    TotalOEM = SqlFunc.AggregateSum(d.OEMAmount),
                    TotalImport = SqlFunc.AggregateSum(d.ImportAmount),
                })
                .FirstAsync();

            var totalOEM = totals?.TotalOEM ?? 0;
            var totalImport = totals?.TotalImport ?? 0;

            await _db.Updateable<WareHouseOrder>()
                .SetColumns(o => new WareHouseOrder
                {
                    OEMTotalAmount = totalOEM,
                    ImportTotalAmount = totalImport,
                    UpdatedAt = DateTime.Now,
                })
                .Where(o => o.OrderGUID == orderGuid)
                .ExecuteCommandAsync();
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
                // 1. 获取购物车数量 (FlowStatus = 0)，在数据库侧聚合，避免把整车明细拉回内存。
                var cartItems = await _db.Queryable<WareHouseOrderDetails>()
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
                    )
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
                        .Where((d, p) => p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                        .Select(d => d.OrderGUID)
                        .Distinct()
                        .ToListAsync();

                    matchedGuids.AddRange(detailMatchedGuids);
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

                                if (userStoreCodes.Any())
                                {
                                    q = q.Where(o => userStoreCodes.Contains(o.StoreCode));
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

                var total = await q.Clone().CountAsync();

                // 动态排序处理
                var sortBy = (filter.SortBy ?? "default").Trim().ToLower();
                var orderType =
                    (filter.SortDescending ?? true) ? OrderByType.Desc : OrderByType.Asc;

                ISugarQueryable<WareHouseOrder> orderedQuery = q;

                switch (sortBy)
                {
                    case "orderno":
                        orderedQuery = q.OrderBy(o => o.OrderNo, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    case "orderdate":
                        orderedQuery = q.OrderBy(
                            $"OrderDate {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderNo {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
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
                    case "totalorderamount":
                        orderedQuery = q.OrderBy(
                            $"(SELECT ISNULL(SUM(d.Quantity * d.ImportPrice), 0) FROM WareHouseOrderDetails d WHERE d.OrderGUID = WareHouseOrder.OrderGUID) {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
                        break;
                    case "totalquantity":
                        orderedQuery = q.OrderBy(
                            $"(SELECT ISNULL(SUM(d.Quantity), 0) FROM WareHouseOrderDetails d WHERE d.OrderGUID = WareHouseOrder.OrderGUID) {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
                        break;
                    case "totalallocquantity":
                        orderedQuery = q.OrderBy(
                            $"(SELECT ISNULL(SUM(d.AllocQuantity), 0) FROM WareHouseOrderDetails d WHERE d.OrderGUID = WareHouseOrder.OrderGUID) {(orderType == OrderByType.Desc ? "DESC" : "ASC")}, OrderGUID {(orderType == OrderByType.Desc ? "DESC" : "ASC")}"
                        );
                        break;
                    case "remarks":
                        orderedQuery = q.OrderBy(o => o.Remarks, orderType)
                            .OrderBy(o => o.OrderGUID, orderType);
                        break;
                    default:
                        orderedQuery = q.OrderBy("FlowStatus ASC, OrderDate DESC, OrderNo DESC");
                        break;
                }

                var items = await orderedQuery
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .Select(o => new StoreOrderListItemDto
                    {
                        OrderGUID = o.OrderGUID,
                        OrderNo = o.OrderNo,
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
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.ImportPrice ?? 0)),
                        //发货 预计销售sales
                        OEMTotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.OEMAmount ?? 0)),

                        // ImportTotalAmount -> 发货金额 (Alloc Qty * OEMPrice)
                        ImportTotalAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.AllocQuantity ?? 0) * (d.ImportPrice ?? 0)),

                        // TotalOrderAmount -> 订货金额 (Order Qty * OEMPrice)
                        TotalOrderAmount = SqlFunc
                            .Subqueryable<WareHouseOrderDetails>()
                            .Where(d => d.OrderGUID == o.OrderGUID)
                            .Sum(d => (d.Quantity ?? 0) * (d.ImportPrice ?? 0)),
                        //订货数量
                        TotalQuantity = (int)(
                            SqlFunc
                                .Subqueryable<WareHouseOrderDetails>()
                                .Where(d => d.OrderGUID == o.OrderGUID)
                                .Sum(d => d.Quantity)
                            ?? 0
                        ),

                        // TotalAllocQuantity -> 发货数量 (Alloc Qty)
                        TotalAllocQuantity = (int)(
                            SqlFunc
                                .Subqueryable<WareHouseOrderDetails>()
                                .Where(d => d.OrderGUID == o.OrderGUID)
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

                    var orderGuids = items.Select(x => x.OrderGUID).Distinct().ToList();
                    var volumeRows = await _db.Queryable<WareHouseOrderDetails>()
                        .LeftJoin<WarehouseProduct>((d, wp) => d.ProductCode == wp.ProductCode)
                        .LeftJoin<DomesticProduct>((d, wp, dp) => wp.ProductCode == dp.ProductCode)
                        .Where(
                            (d, wp, dp) => d.OrderGUID != null && orderGuids.Contains(d.OrderGUID)
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
                                TotalOrderVolume = group.Sum(x =>
                                    (x.UnitVolume ?? 0) * (x.Quantity ?? 0)
                                ),
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
                        TotalAmount = result.Data.TotalAmount,
                        TotalQuantity = result.Data.TotalQuantity,
                        TotalImportAmount = result.Data.TotalImportAmount,
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
                .Select((o, s) => new { Order = o, StoreAddress = s.Address, StoreContactEmail = s.ContactEmail })
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
                detailQuery = detailQuery.Where((d, p, wp, dp) => p.IsActive);
            }
            else if (statFilter is "inactive" or "0" or "false")
            {
                detailQuery = detailQuery.Where((d, p, wp, dp) => !p.IsActive);
            }

            var itemsTotal = await detailQuery.CountAsync();
            var orderType = normalizedQuery.SortDescending ? OrderByType.Desc : OrderByType.Asc;
            detailQuery = (normalizedQuery.SortBy ?? string.Empty).Trim().ToLowerInvariant() switch
            {
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
                "importamount" => detailQuery.OrderBy((d, p, wp, dp) => d.ImportAmount, orderType),
                "isactive" => detailQuery.OrderBy((d, p, wp, dp) => p.IsActive, orderType),
                "locationcode" => detailQuery.OrderBy((d, p, wp, dp) => d.ProductCode, orderType),
                _ => detailQuery.OrderBy((d, p, wp, dp) => p.ItemNumber, orderType),
            };
            detailQuery = detailQuery.OrderBy((d, p, wp, dp) => d.DetailGUID, OrderByType.Asc);

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
                            ImportAmount =
                                d.ImportAmount
                                ?? (
                                    (d.ImportPrice ?? (wp.ImportPrice ?? 0))
                                    * (d.AllocQuantity ?? d.Quantity ?? 0)
                                ),
                            Volume =
                                (dp.PackingQuantity > 0)
                                    ? (dp.UnitVolume / dp.PackingQuantity)
                                    : dp.UnitVolume,
                            MinOrderQuantity = wp.MinOrderQuantity ?? 1,
                            IsActive = p.IsActive,
                            RRP = p.RetailPrice,
                        }
                );

            if (!loadAllItems)
            {
                pageQuery = pageQuery
                    .Skip((normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize)
                    .Take(normalizedQuery.PageSize);
            }

            var pageDetails = await pageQuery.ToListAsync();

            await FillLocationCodesAsync(pageDetails);
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
                                d.ImportAmount
                                    ?? (
                                        (d.ImportPrice ?? (wp.ImportPrice ?? 0))
                                        * (d.AllocQuantity ?? d.Quantity ?? 0)
                                    )
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

            var dto = new StoreOrderDetailDto
            {
                OrderGUID = order.Order.OrderGUID,
                OrderNo = order.Order.OrderNo,
                StoreCode = order.Order.StoreCode,
                OrderDate = order.Order.OrderDate,
                OutboundDate = order.Order.OutboundDate,
                TotalAmount = order.Order.OEMTotalAmount ?? 0,
                TotalQuantity = (int)(summary?.TotalQuantity ?? 0),
                TotalAllocQuantity = (int)(summary?.TotalAllocQuantity ?? 0),
                TotalSKU = summary?.TotalSKU ?? 0,
                TotalImportAmount = summary?.TotalImportAmount ?? 0,
                TotalVolume = summary?.TotalOrderVolume ?? 0,
                TotalOrderVolume = summary?.TotalOrderVolume ?? 0,
                TotalAllocVolume = summary?.TotalAllocVolume ?? 0,
                Remarks = order.Order.Remarks,
                StoreAddress = order.StoreAddress,
                StoreContactEmail = order.StoreContactEmail,
                ShippingFee = order.Order.ShippingFee,
                FlowStatus = order.Order.FlowStatus,
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
                Keyword = query?.Keyword,
                StatFilter = query?.StatFilter,
                SortBy = query?.SortBy,
                SortDescending = query?.SortDescending ?? false,
            };
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

                // 4. 按分店代码排序
                result = result.OrderBy(b => b.Code).ToList();

                return new ApiResponse<List<BranchDto>> { Success = true, Data = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUsedBranchesAsync failed");
                return new ApiResponse<List<BranchDto>> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<string>> CreateOrderAsync(CreateStoreOrderDto request)
        {
            try
            {
                var now = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                var order = new WareHouseOrder
                {
                    OrderGUID = UuidHelper.GenerateUuid7(),
                    StoreCode = request.StoreCode,
                    OrderDate = now,
                    FlowStatus = 1, // Submitted
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
                return new ApiResponse<string> { Success = true, Data = order.OrderGUID };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateOrderAsync failed");
                return new ApiResponse<string> { Success = false, Message = ex.Message };
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

                foreach (
                    var item in request.Items.Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                )
                {
                    await PasteReplaceDetailAsync(order, item, request.TargetField);
                }

                await UpdateOrderTotalAsync(order.OrderGUID);
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

                await AddOrUpdateDetailAsync(
                    order,
                    request.ProductCode,
                    request.Quantity,
                    request.ImportPrice,
                    isUpdate: true
                );
                await UpdateOrderTotalAsync(order.OrderGUID);

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

                foreach (var item in request.Items)
                {
                    await AddOrUpdateDetailAsync(
                        order,
                        item.ProductCode,
                        item.Quantity ?? 0, // 如果未传Quantity，在AddOrUpdateDetailAsync内部需要处理（或者不允许单独更新价格而不传数量？）
                        // 注意：AddOrUpdateDetailAsync 目前设计是接收 Quantity。如果是只更新价格，需要重构。
                        // 重构 AddOrUpdateDetailAsync 以支持可选参数。
                        item.ImportPrice,
                        isUpdate: true,
                        isBatch: true, // 标记为批量操作
                        originalQuantity: item.Quantity // 传递原始数量，如果是null则表示不更新数量
                    );
                }

                await UpdateOrderTotalAsync(order.OrderGUID);
                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateOrderLineAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
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
                    newDetail.ImportAmount = newDetail.AllocQuantity * newDetail.ImportPrice;

                    newDetails.Add(newDetail);
                }

                newOrder.OEMTotalAmount = newDetails.Sum(d => d.OEMAmount);
                newOrder.ImportTotalAmount = newDetails.Sum(d => d.ImportAmount);

                try
                {
                    _db.Ado.BeginTran();

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "CopyOrderAsync failed");
                return new ApiResponse<CopyOrderResultDto>
                {
                    Success = false,
                    Message = ex.Message,
                };
            }
        }

        private async Task<WareHouseOrder?> GetEditableOrderAsync(string orderGuid)
        {
            var order = await _db.Queryable<WareHouseOrder>()
                .Where(o => o.OrderGUID == orderGuid && !o.IsDeleted)
                .FirstAsync();

            // 允许编辑 FlowStatus 0 (购物车) 和 1 (已提交)
            if (order != null && (order.FlowStatus == 0 || order.FlowStatus == 1))
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
            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

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

            if (detail == null && targetQuantity <= 0)
            {
                return;
            }

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

            if (
                string.Equals(
                    targetField,
                    StoreOrderPasteTargetFields.Quantity,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                detail.Quantity = targetQuantity;
            }
            else
            {
                detail.AllocQuantity = targetQuantity;
            }

            var allocQuantity = detail.AllocQuantity ?? 0;
            detail.OEMAmount = allocQuantity * (detail.OEMPrice ?? 0);
            detail.ImportAmount = allocQuantity * (detail.ImportPrice ?? 0);
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
                    ImportAmount = finalImportPrice * minQty,
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
                    // 使用最新的 ImportPrice 计算
                    detail.ImportAmount = detail.AllocQuantity * detail.ImportPrice;
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

                order.FlowStatus = 2;
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.FlowStatus,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

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

                order.FlowStatus = 3;
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.FlowStatus,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

                return new ApiResponse<bool> { Success = true, Data = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartPickingAsync failed");
                return new ApiResponse<bool> { Success = false, Message = ex.Message };
            }
        }

        public async Task<ApiResponse<bool>> UpdateOrderStatusAsync(string orderGuid, int newStatus)
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

                if (order.FlowStatus == newStatus)
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Status is already the target status",
                    };
                }

                var userId = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                _logger.LogInformation(
                    "Updating order {OrderGUID} status from {OldStatus} to {NewStatus}",
                    orderGuid,
                    order.FlowStatus,
                    newStatus
                );

                order.FlowStatus = newStatus;
                order.UpdatedAt = DateTime.Now;
                order.UpdatedBy = userId;

                await _db.Updateable(order)
                    .UpdateColumns(o => new
                    {
                        o.FlowStatus,
                        o.UpdatedAt,
                        o.UpdatedBy,
                    })
                    .ExecuteCommandAsync();

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
            int newStatus
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

                var userId = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                _logger.LogInformation(
                    "Batch updating {Count} orders to status {NewStatus}",
                    orderGuids.Count,
                    newStatus
                );

                var updatedCount = await _db.Updateable<WareHouseOrder>()
                    .SetColumns(o => new WareHouseOrder
                    {
                        FlowStatus = newStatus,
                        UpdatedAt = DateTime.Now,
                        UpdatedBy = userId,
                    })
                    .Where(o => orderGuids.Contains(o.OrderGUID) && !o.IsDeleted)
                    .ExecuteCommandAsync();

                return new ApiResponse<int>
                {
                    Success = true,
                    Data = updatedCount,
                    Message = $"Updated {updatedCount} orders",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchUpdateOrderStatusAsync failed");
                return new ApiResponse<int> { Success = false, Message = ex.Message };
            }
        }
    }
}
