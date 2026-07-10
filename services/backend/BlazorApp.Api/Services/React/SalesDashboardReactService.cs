using System.Collections.Concurrent;
using System.Globalization;
using AutoMapper;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 产品查询结果投影（用于统一类型）
    /// </summary>
    internal class ProductInfo
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public string? ProductImage { get; set; }
        public string? ProductName { get; set; }
        public bool? IsActive { get; set; }
        public int? MinOrderQuantity { get; set; }
    }

    internal class BestSellerAggregateRow
    {
        public string ProductCode { get; set; } = string.Empty;
        public int TotalQuantity { get; set; }
        public decimal TotalSalesAmount { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? GrossProfit { get; set; }
        public string? CostSource { get; set; }
    }

    internal class BestSellerBranchAggregateRow
    {
        public string ProductCode { get; set; } = string.Empty;
        public string BranchCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal SalesAmount { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? GrossProfit { get; set; }
        public string? CostSource { get; set; }
    }

    internal class ProductReportProductAggregateRow
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal SalesAmount { get; set; }
        public int OrderCount { get; set; }
    }

    internal class StatisticDateBranchRow
    {
        public DateTime Date { get; set; }
        public string? BranchCode { get; set; }
    }

    internal class StatisticDateBranchHourRow
    {
        public DateTime Date { get; set; }
        public string? BranchCode { get; set; }
        public int Hour { get; set; }
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    internal class ProductBranchAggregateRow
    {
        public string BranchCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal SalesAmount { get; set; }
    }

    internal class SupplierBranchAggregateRow
    {
        public DateTime? Date { get; set; }
        public string SupplierCode { get; set; } = string.Empty;
        public string? BranchCode { get; set; }
        public decimal TotalAmount { get; set; }
        public int TotalQuantity { get; set; }
        public int? OrderCount { get; set; }
    }

    internal class ProductSupplierBranchAggregateRow : SupplierBranchAggregateRow
    {
        public string ProductCode { get; set; } = string.Empty;
    }

    internal class SupplierRankAggregateRow
    {
        public string SupplierCode { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public int TotalQuantity { get; set; }
        public int OrderCount { get; set; }
        public int StoreCount { get; set; }
    }

    /// <summary>
    /// 销售仪表板 React 服务
    /// 为 React 前端提供销售统计数据的查询功能
    /// </summary>
    public class SalesDashboardReactService : ISalesDashboardReactService
    {
        private readonly SqlSugarContext _context;
        private readonly POSMSqlSugarContext _posmContext;
        private readonly IMapper _mapper;
        private readonly ILogger<SalesDashboardReactService> _logger;
        private readonly IMemoryCache _cache;
        private readonly IServiceScopeFactory? _serviceScopeFactory;

        private static readonly TimeSpan SUMMARY_CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RANKING_CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan BEST_SELLERS_CACHE_DURATION = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan DETAIL_CACHE_DURATION = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan REPORT_STATISTICS_REFRESH_WAIT = TimeSpan.FromMilliseconds(2200);
        private const int REPORT_STATISTICS_REFRESH_MAX_DAYS = 35;
        private const int LEGACY_CHINA_SUPPLIER_PRODUCT_FILTER_LIMIT = 2000;
        private const string CHINA_LOCAL_SUPPLIER_CODE = "200";
        private const string CHINA_LOCAL_SUPPLIER_FALLBACK_NAME = "hotbargain";
        private static readonly ConcurrentDictionary<string, byte> REPORT_STATISTICS_REFRESHING_KEYS = new();

        private enum StatisticsRefreshState
        {
            NotNeeded,
            Completed,
            Pending,
        }

        public SalesDashboardReactService(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            IMapper mapper,
            ILogger<SalesDashboardReactService> logger,
            IMemoryCache cache,
            IServiceScopeFactory? serviceScopeFactory = null
        )
        {
            _context = context;
            _posmContext = posmContext;
            _mapper = mapper;
            _logger = logger;
            _cache = cache;
            _serviceScopeFactory = serviceScopeFactory;
        }

        /// <summary>
        /// 验证数据库连接和指定实体表是否存在
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <returns>验证成功返回 true，否则返回 false</returns>
        private bool ValidateDatabaseConnection<TEntity>()
        {
            if (_context?.Db == null)
            {
                _logger.LogError("数据库上下文为空 (SqlSugarContext.Db is null)");
                return false;
            }

            try
            {
                var tableName = _context.Db.EntityMaintenance.GetTableName<TEntity>();
                if (!_context.Db.DbMaintenance.IsAnyTable(tableName))
                {
                    _logger.LogError($"表 {tableName} 不存在");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"验证数据库连接或表 {typeof(TEntity).Name} 时出错");
                return false;
            }
        }

        /// <summary>
        /// 验证日期范围的有效性
        /// </summary>
        /// <param name="dateRange">日期范围对象</param>
        /// <exception cref="ArgumentNullException">当 dateRange 为 null 时抛出</exception>
        /// <exception cref="ArgumentException">当日期范围无效时抛出</exception>
        private void ValidateDateRange(DateRangeDto dateRange)
        {
            if (dateRange == null)
                throw new ArgumentNullException(nameof(dateRange));

            if (dateRange.StartDate > dateRange.EndDate)
                throw new ArgumentException("开始日期不能晚于结束日期");

            if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
            {
                if (dateRange.CompareStartDate.Value > dateRange.CompareEndDate.Value)
                    throw new ArgumentException("比较开始日期不能晚于比较结束日期");
            }
        }

        /// <summary>
        /// 获取仪表板汇总数据
        /// 包含总销售额、总销量、订单数、SKU数、客户数、平均订单价值等指标
        /// 支持与对比期间的环比分析
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <returns>仪表板汇总数据</returns>
        public async Task<DashboardSummaryDto?> GetDashboardSummaryAsync(DateRangeDto dateRange)
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.Summary(dateRange, null);

                if (
                    _cache.TryGetValue<DashboardSummaryDto>(cacheKey, out var cachedResult)
                    && cachedResult != null
                )
                {
                    _logger.LogInformation("从缓存获取仪表板汇总数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<DailySalesStatistic>())
                {
                    return null;
                }

                var currentData = await _context
                    .Db.Queryable<DailySalesStatistic>()
                    .Where(s =>
                        s.Date >= dateRange.StartDate.Date && s.Date <= dateRange.EndDate.Date
                    )
                    .Select(s => new
                    {
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        SkuCount = SqlFunc.AggregateSum(s.SkuCount),
                        CustomerCount = SqlFunc.AggregateSum(s.CustomerCount),
                        AvgOrderValue = SqlFunc.AggregateAvg(s.AverageOrderValue),
                    })
                    .FirstAsync();

                var result = new DashboardSummaryDto
                {
                    StartDate = dateRange.StartDate,
                    EndDate = dateRange.EndDate,
                    TotalAmount = currentData.TotalAmount,
                    TotalQuantity = currentData.TotalQuantity,
                    OrderCount = currentData.OrderCount,
                    SkuCount = currentData.SkuCount,
                    CustomerCount = currentData.CustomerCount,
                    AverageOrderValue = currentData.AvgOrderValue,
                };

                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var compareStartDate = dateRange.CompareStartDate.Value.Date;
                    var compareEndDate = dateRange.CompareEndDate.Value.Date;

                    var compareData = await _context
                        .Db.Queryable<DailySalesStatistic>()
                        .Where(s => s.Date >= compareStartDate && s.Date <= compareEndDate)
                        //  .GroupBy(s => 1)
                        .Select(s => new
                        {
                            TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                            TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                            OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                            SkuCount = SqlFunc.AggregateSum(s.SkuCount),
                            CustomerCount = SqlFunc.AggregateSum(s.CustomerCount),
                            AvgOrderValue = SqlFunc.AggregateAvg(s.AverageOrderValue),
                        })
                        .FirstAsync();

                    result.CompareTotalAmount = compareData.TotalAmount;
                    result.CompareTotalQuantity = compareData.TotalQuantity;
                    result.CompareOrderCount = compareData.OrderCount;
                    result.CompareSkuCount = compareData.SkuCount;
                    result.CompareCustomerCount = compareData.CustomerCount;
                    result.CompareAvgOrderValue = compareData.AvgOrderValue;

                    result.TotalAmountGrowth = CalculateGrowth(
                        currentData.TotalAmount,
                        compareData.TotalAmount
                    );
                    result.TotalQuantityGrowth = CalculateGrowth(
                        currentData.TotalQuantity,
                        compareData.TotalQuantity
                    );
                    result.OrderCountGrowth = CalculateGrowth(
                        currentData.OrderCount,
                        compareData.OrderCount
                    );
                    result.SkuCountGrowth = CalculateGrowth(
                        currentData.SkuCount,
                        compareData.SkuCount
                    );
                    result.CustomerCountGrowth = CalculateGrowth(
                        currentData.CustomerCount,
                        compareData.CustomerCount
                    );
                    result.AvgOrderValueGrowth = CalculateGrowth(
                        currentData.AvgOrderValue,
                        compareData.AvgOrderValue
                    );
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(SUMMARY_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "仪表板汇总数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(SUMMARY_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDashboardSummaryAsync failed");
                return null;
            }
        }

        /// <summary>
        /// 获取小时销售数据
        /// 按小时统计销售额、销量、客户数等指标
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>小时销售数据列表</returns>
        public async Task<List<HourlySalesDto>> GetHourlySalesAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.Hourly(dateRange, branchCodes, null);

                if (
                    _cache.TryGetValue<List<HourlySalesDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取小时销售数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<HourlySalesStatistic>())
                {
                    return new List<HourlySalesDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var currentQuery = _context
                    .Db.Queryable<HourlySalesStatistic>()
                    .Where(s => s.BranchCode != "ALL" && s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    currentQuery = currentQuery.Where(s => branchCodes.Contains(s.BranchCode!));
                }

                var currentData = await currentQuery
                    .GroupBy(s => new { s.Hour, s.BranchCode })
                    .Select(s => new
                    {
                        Hour = s.Hour,
                        BranchCode = s.BranchCode,

                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        CustomerCount = SqlFunc.AggregateSum(s.CustomerCount),
                        AvgOrderValue = SqlFunc.AggregateAvg(s.AverageOrderValue),
                    })
                    .OrderBy(s => s.Hour)
                    .ToListAsync();

                Dictionary<(int Hour, string? BranchCode), dynamic> compareDict = new();

                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var compareStartDate = dateRange.CompareStartDate.Value.Date;
                    var compareEndDate = dateRange.CompareEndDate.Value.Date;

                    var compareQuery = _context
                        .Db.Queryable<HourlySalesStatistic>()
                        .Where(s => s.Date >= compareStartDate && s.Date <= compareEndDate);

                    if (branchCodes != null && branchCodes.Any())
                    {
                        compareQuery = compareQuery.Where(s => branchCodes.Contains(s.BranchCode!));
                    }

                    var compareData = await compareQuery
                        .GroupBy(s => new { s.Hour, s.BranchCode })
                        .Select(s => new
                        {
                            Hour = s.Hour,
                            BranchCode = s.BranchCode,
                            TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                            TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                            CustomerCount = SqlFunc.AggregateSum(s.CustomerCount),
                            AvgOrderValue = SqlFunc.AggregateAvg(s.AverageOrderValue),
                        })
                        .ToListAsync();

                    compareDict = compareData.ToDictionary(
                        d => (d.Hour, d.BranchCode),
                        d => (dynamic)d
                    );
                }

                var result = new List<HourlySalesDto>();

                foreach (var item in currentData)
                {
                    var dto = new HourlySalesDto
                    {
                        Date = startDate,
                        Hour = item.Hour,
                        BranchCode = item.BranchCode,
                        BranchName = item.BranchCode,
                        TotalAmount = item.TotalAmount,
                        TotalQuantity = item.TotalQuantity,
                        CustomerCount = item.CustomerCount,
                        AverageOrderValue = item.AvgOrderValue,
                    };

                    if (
                        item.BranchCode != null
                        && compareDict.TryGetValue(
                            (item.Hour, item.BranchCode),
                            out var compareData
                        )
                    )
                    {
                        dto.CompareTotalAmount = compareData.TotalAmount;
                        dto.CompareTotalQuantity = compareData.TotalQuantity;
                        dto.CompareCustomerCount = compareData.CustomerCount;
                        dto.CompareAvgOrderValue = compareData.AvgOrderValue;
                    }

                    result.Add(dto);
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "小时销售数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetHourlySalesAsync failed");
                return new List<HourlySalesDto>();
            }
        }

        /// <summary>
        /// 获取分店销售排名
        /// 按销售额降序排列，返回前 N 名分店
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="topN">返回前 N 名，默认 50</param>
        /// <returns>分店销售排名列表</returns>
        public async Task<List<StoreSalesRankDto>> GetStoreSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 50
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.StoreRank(dateRange, branchCodes, topN);

                if (
                    _cache.TryGetValue<List<StoreSalesRankDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取分店销售排名: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<StoreSalesStatistic>())
                {
                    return new List<StoreSalesRankDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var query = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s =>
                        s.BranchCode != null
                        && s.BranchCode != "ALL"
                        && s.Date >= startDate
                        && s.Date <= endDate
                    );

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var currentData = await query
                    .GroupBy(s => new { s.BranchCode, s.BranchName })
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        BranchName = s.BranchName,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        CustomerCount = SqlFunc.AggregateSum(s.CustomerCount),
                        AvgOrderValue = SqlFunc.AggregateAvg(s.AverageOrderValue),
                    })
                    .OrderByDescending(s => s.TotalAmount)
                    .Take(topN)
                    .ToListAsync();

                var result = new List<StoreSalesRankDto>();

                // 批量获取对比数据
                Dictionary<string, dynamic> compareDict = new Dictionary<string, dynamic>();

                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var compareStartDate = dateRange.CompareStartDate.Value.Date;
                    var compareEndDate = dateRange.CompareEndDate.Value.Date;
                    var branchCodesList = currentData.Select(s => s.BranchCode).ToList();

                    if (branchCodesList.Any())
                    {
                        var compareQuery = _context
                            .Db.Queryable<StoreSalesStatistic>()
                            .Where(s =>
                                s.Date >= compareStartDate
                                && s.Date <= compareEndDate
                                && branchCodesList.Contains(s.BranchCode)
                            );

                        if (branchCodes != null && branchCodes.Any())
                        {
                            compareQuery = compareQuery.Where(s =>
                                branchCodes.Contains(s.BranchCode)
                            );
                        }

                        var compareDataList = await compareQuery
                            .GroupBy(s => s.BranchCode)
                            .Select(s => new
                            {
                                BranchCode = s.BranchCode,
                                TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                                TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                                OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                                AvgOrderValue = SqlFunc.AggregateAvg(s.AverageOrderValue),
                            })
                            .ToListAsync();

                        compareDict = compareDataList.ToDictionary(
                            s => s.BranchCode,
                            s => (dynamic)s
                        );
                    }
                }

                foreach (var item in currentData)
                {
                    var dto = new StoreSalesRankDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        BranchCode = item.BranchCode,
                        BranchName = item.BranchName,
                        TotalAmount = item.TotalAmount,
                        TotalQuantity = item.TotalQuantity,
                        OrderCount = item.OrderCount,
                        CustomerCount = item.CustomerCount,
                        AverageOrderValue = item.AvgOrderValue,
                    };

                    if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                    {
                        decimal compareTotalAmount = 0;
                        int compareTotalQuantity = 0;
                        int compareOrderCount = 0;
                        decimal compareAvgOrderValue = 0;

                        if (compareDict.TryGetValue(item.BranchCode, out var compareData))
                        {
                            compareTotalAmount = compareData.TotalAmount;
                            compareTotalQuantity = compareData.TotalQuantity;
                            compareOrderCount = compareData.OrderCount;
                            compareAvgOrderValue = compareData.AvgOrderValue;
                        }

                        dto.CompareTotalAmount = compareTotalAmount;
                        dto.CompareTotalQuantity = compareTotalQuantity;
                        dto.CompareOrderCount = compareOrderCount;
                        dto.CompareAvgOrderValue = compareAvgOrderValue;

                        dto.TotalAmountGrowth = CalculateGrowth(
                            item.TotalAmount,
                            compareTotalAmount
                        );
                        dto.TotalQuantityGrowth = CalculateGrowth(
                            item.TotalQuantity,
                            compareTotalQuantity
                        );
                        dto.OrderCountGrowth = CalculateGrowth(item.OrderCount, compareOrderCount);
                        dto.AvgOrderValueGrowth = CalculateGrowth(
                            item.AvgOrderValue,
                            compareAvgOrderValue
                        );
                    }

                    result.Add(dto);
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "分店销售排名已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStoreSalesRankAsync failed");
                return new List<StoreSalesRankDto>();
            }
        }

        /// <summary>
        /// 获取供应商销售排名
        /// 从日商品分店统计表按供应商汇总
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="topN">返回前 N 名，默认 20</param>
        /// <param name="supplierCode">供应商代码（可选，用于联动过滤）</param>
        /// <returns>供应商销售排名列表</returns>
        public async Task<List<SupplierSalesRankDto>> GetSupplierSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 200,
            string? supplierCode = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.SupplierRank(
                    dateRange,
                    branchCodes,
                    topN,
                    supplierCode
                );

                if (
                    _cache.TryGetValue<List<SupplierSalesRankDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取供应商销售排名: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;
                var supplierCodes = string.IsNullOrWhiteSpace(supplierCode)
                    ? null
                    : new List<string> { supplierCode.Trim() };
                var chinaSupplierCodeSet = await GetChinaSupplierCodeSetAsync();
                var shouldIncludeChinaLocalSupplier = supplierCodes == null
                    || supplierCodes.Any(IsChinaLocalSupplierCode);
                var currentRows = new List<SupplierBranchAggregateRow>();
                var query = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes
                );
                query = ApplyAustralianSupplierStatisticFilter(query, supplierCodes, chinaSupplierCodeSet);
                var rawCurrentRows = await query
                    .GroupBy(s => new { s.Date, s.SupplierCode, s.BranchCode })
                    .Select(s => new SupplierBranchAggregateRow
                    {
                        Date = s.Date,
                        SupplierCode = s.SupplierCode,
                        BranchCode = s.BranchCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();
                currentRows = ResolveAustralianSupplierBranchRows(
                    rawCurrentRows,
                    chinaSupplierCodeSet,
                    preserveDate: true
                );
                if (!currentRows.Any())
                {
                    // 商品统计异步生成期间，供应商表先用已存在的供应商分店统计兜底，避免页面空白。
                    currentRows = await QueryAustralianSupplierStoreAggregateRowsAsync(
                        startDate,
                        endDate,
                        branchCodes,
                        supplierCodes
                    );
                }
                currentRows = await AddChinaLocalSupplierFallbackRowsIfMissingAsync(
                    currentRows,
                    startDate,
                    endDate,
                    branchCodes,
                    shouldIncludeChinaLocalSupplier,
                    chinaSupplierCodeSet
                );

                var currentData = BuildSupplierRankAggregates(currentRows)
                    .OrderByDescending(row => row.TotalAmount)
                    .Take(topN)
                    .ToList();

                var currentSupplierCodes = currentData.Select(s => s.SupplierCode).ToList();
                var supplierNameMap = await GetAustralianSupplierNameMapAsync(currentSupplierCodes);
                var compareDict = new Dictionary<string, (decimal TotalAmount, int OrderCount)>(
                    StringComparer.OrdinalIgnoreCase
                );

                if (
                    dateRange.CompareStartDate.HasValue
                    && dateRange.CompareEndDate.HasValue
                    && currentSupplierCodes.Any()
                )
                {
                    var compareQuery = await BuildProductReportStatisticQueryAsync(
                        dateRange.CompareStartDate.Value.Date,
                        dateRange.CompareEndDate.Value.Date,
                        branchCodes
                    );
                    compareQuery = ApplyAustralianSupplierStatisticFilter(
                        compareQuery,
                        currentSupplierCodes,
                        chinaSupplierCodeSet
                    );
                    var rawCompareRows = await compareQuery
                            .GroupBy(s => new { s.Date, s.SupplierCode, s.BranchCode })
                            .Select(s => new SupplierBranchAggregateRow
                            {
                                Date = s.Date,
                                SupplierCode = s.SupplierCode,
                                BranchCode = s.BranchCode,
                                TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                                OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                            })
                            .ToListAsync();
                    var compareRows = ResolveAustralianSupplierBranchRows(
                        rawCompareRows,
                        chinaSupplierCodeSet,
                        preserveDate: true
                    );

                    if (!compareRows.Any())
                    {
                        compareRows = await QueryAustralianSupplierStoreAggregateRowsAsync(
                            dateRange.CompareStartDate.Value.Date,
                            dateRange.CompareEndDate.Value.Date,
                            branchCodes,
                            currentSupplierCodes
                        );
                    }
                    compareRows = await AddChinaLocalSupplierFallbackRowsIfMissingAsync(
                        compareRows,
                        dateRange.CompareStartDate.Value.Date,
                        dateRange.CompareEndDate.Value.Date,
                        branchCodes,
                        currentSupplierCodes.Any(IsChinaLocalSupplierCode),
                        chinaSupplierCodeSet
                    );

                    compareDict = BuildSupplierCompareDict(compareRows, currentSupplierCodes);
                }

                var result = currentData.Select(item =>
                {
                    compareDict.TryGetValue(item.SupplierCode, out var compare);
                    var hasCompare = dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue;
                    var dto = new SupplierSalesRankDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        SupplierCode = item.SupplierCode,
                        SupplierName = supplierNameMap.TryGetValue(item.SupplierCode, out var supplierName)
                            ? supplierName
                            : item.SupplierCode,
                        TotalAmount = item.TotalAmount,
                        TotalQuantity = item.TotalQuantity,
                        OrderCount = item.OrderCount,
                        AverageTransaction = item.OrderCount > 0 ? item.TotalAmount / item.OrderCount : 0,
                        StoreCount = item.StoreCount,
                        CompareTotalAmount = hasCompare ? compare.TotalAmount : null,
                    };

                    if (hasCompare)
                    {
                        dto.CompareOrderCount = compare.OrderCount;
                        dto.CompareAverageTransaction =
                            compare.OrderCount > 0 ? compare.TotalAmount / compare.OrderCount : 0;
                        dto.TotalAmountGrowth = CalculateGrowth(item.TotalAmount, compare.TotalAmount);
                    }

                    return dto;
                }).ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "供应商销售排名已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSupplierSalesRankAsync failed");
                return new List<SupplierSalesRankDto>();
            }
        }

        /// <summary>
        /// 获取中国供应商销售排名
        /// 从日商品分店统计表按 POSM 中国供应商映射汇总
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="topN">返回前 N 名，默认 20</param>
        /// <param name="supplierCode">供应商代码（可选，用于联动过滤）</param>
        /// <returns>中国供应商销售排名列表</returns>
        public async Task<List<ChinaSupplierSalesRankDto>> GetChinaSupplierSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 20,
            string? supplierCode = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.ChinaSupplierRank(
                    dateRange,
                    branchCodes,
                    topN,
                    supplierCode
                );

                if (
                    _cache.TryGetValue<List<ChinaSupplierSalesRankDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取中国供应商销售排名: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;
                var requestedSupplierCodes = NormalizeCodes(
                    string.IsNullOrWhiteSpace(supplierCode)
                        ? null
                        : new List<string> { supplierCode.Trim() }
                );
                var chinaProductMap = await GetChinaSupplierProductMapAsync(
                    requestedSupplierCodes.Any() ? requestedSupplierCodes : null
                );
                var targetChinaSupplierCodes = requestedSupplierCodes.Any()
                    ? requestedSupplierCodes.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : await GetChinaSupplierCodeSetAsync(chinaProductMap.Values);

                var query = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes
                );
                query = ApplyChinaSupplierStatisticFilter(
                    query,
                    targetChinaSupplierCodes,
                    chinaProductMap,
                    limitLegacy200Products: requestedSupplierCodes.Any()
                );
                var currentRows = await query
                    .GroupBy(s => new { s.ProductCode, s.BranchCode, s.SupplierCode })
                    .Select(s => new ProductSupplierBranchAggregateRow
                    {
                        ProductCode = s.ProductCode,
                        BranchCode = s.BranchCode,
                        SupplierCode = s.SupplierCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();

                var resolvedCurrentRows = ResolveChinaSupplierBranchRows(
                    currentRows,
                    chinaProductMap,
                    targetChinaSupplierCodes
                );
                var hasCurrentProductStatisticRows = currentRows.Any()
                    || (
                        requestedSupplierCodes.Any()
                        && await HasChinaProductStatisticRowsAsync(
                            startDate,
                            endDate,
                            branchCodes,
                            targetChinaSupplierCodes
                        )
                    );

                if (!hasCurrentProductStatisticRows)
                {
                    // 商品统计未覆盖当日时，中国供应商表用独立统计表兜底，避免只查到 POSM 映射后返回空。
                    resolvedCurrentRows = await QueryChinaSupplierStoreAggregateRowsAsync(
                        startDate,
                        endDate,
                        branchCodes,
                        targetChinaSupplierCodes
                    );
                }

                var currentData = BuildSupplierRankAggregates(resolvedCurrentRows)
                    .OrderByDescending(row => row.TotalAmount)
                    .Take(topN)
                    .ToList();

                var currentSupplierCodes = currentData.Select(s => s.SupplierCode).ToList();
                var supplierNameMap = await GetChinaSupplierNameMapAsync(currentSupplierCodes);
                var compareDict = new Dictionary<string, (decimal TotalAmount, int OrderCount)>(
                    StringComparer.OrdinalIgnoreCase
                );

                if (
                    dateRange.CompareStartDate.HasValue
                    && dateRange.CompareEndDate.HasValue
                    && currentSupplierCodes.Any()
                )
                {
                    var compareQuery = await BuildProductReportStatisticQueryAsync(
                        dateRange.CompareStartDate.Value.Date,
                        dateRange.CompareEndDate.Value.Date,
                        branchCodes
                    );
                    compareQuery = ApplyChinaSupplierStatisticFilter(
                        compareQuery,
                        targetChinaSupplierCodes,
                        chinaProductMap,
                        limitLegacy200Products: requestedSupplierCodes.Any()
                    );
                    var compareRows = await compareQuery
                        .GroupBy(s => new { s.ProductCode, s.BranchCode, s.SupplierCode })
                        .Select(s => new ProductSupplierBranchAggregateRow
                        {
                            ProductCode = s.ProductCode,
                            BranchCode = s.BranchCode,
                            SupplierCode = s.SupplierCode,
                            TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                            OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        })
                        .ToListAsync();

                    var resolvedCompareRows = ResolveChinaSupplierBranchRows(
                        compareRows,
                        chinaProductMap,
                        targetChinaSupplierCodes
                    );
                    var hasCompareProductStatisticRows = compareRows.Any()
                        || (
                            requestedSupplierCodes.Any()
                            && await HasChinaProductStatisticRowsAsync(
                                dateRange.CompareStartDate.Value.Date,
                                dateRange.CompareEndDate.Value.Date,
                                branchCodes,
                                targetChinaSupplierCodes
                            )
                        );

                    if (!hasCompareProductStatisticRows)
                    {
                        resolvedCompareRows = await QueryChinaSupplierStoreAggregateRowsAsync(
                            dateRange.CompareStartDate.Value.Date,
                            dateRange.CompareEndDate.Value.Date,
                            branchCodes,
                            currentSupplierCodes
                        );
                    }

                    compareDict = BuildSupplierCompareDict(resolvedCompareRows, currentSupplierCodes);
                }

                var hasCompare = dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue;
                var result = currentData.Select(item =>
                {
                    compareDict.TryGetValue(item.SupplierCode, out var compare);
                    var dto = new ChinaSupplierSalesRankDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        SupplierCode = item.SupplierCode,
                        SupplierName = supplierNameMap.TryGetValue(item.SupplierCode, out var supplierName)
                            ? supplierName
                            : item.SupplierCode,
                        TotalAmount = item.TotalAmount,
                        TotalQuantity = item.TotalQuantity,
                        OrderCount = item.OrderCount,
                        AverageTransaction = item.OrderCount > 0 ? item.TotalAmount / item.OrderCount : 0,
                        StoreCount = item.StoreCount,
                        CompareTotalAmount = hasCompare ? compare.TotalAmount : null,
                    };

                    if (hasCompare)
                    {
                        dto.CompareOrderCount = compare.OrderCount;
                        dto.CompareAverageTransaction =
                            compare.OrderCount > 0 ? compare.TotalAmount / compare.OrderCount : 0;
                        dto.TotalAmountGrowth = CalculateGrowth(item.TotalAmount, compare.TotalAmount);
                    }

                    return dto;
                }).ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "中国供应商销售排名已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChinaSupplierSalesRankAsync failed");
                return new List<ChinaSupplierSalesRankDto>();
            }
        }

        /// <summary>
        /// 获取指定供应商在所有分店的销售数据
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="supplierCodes">供应商代码列表</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>供应商分店销售数据列表</returns>
        public async Task<List<SupplierStoreSalesDto>> GetSupplierStoreSalesAsync(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes = null
        )
        {
            try
            {
                var targetSupplierCodes = NormalizeCodes(supplierCodes);
                if (!targetSupplierCodes.Any())
                    throw new ArgumentException("供应商代码不能为空", nameof(supplierCodes));

                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.SupplierStore(
                    dateRange,
                    targetSupplierCodes,
                    branchCodes
                );

                if (
                    _cache.TryGetValue<List<SupplierStoreSalesDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取供应商分店销售数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;
                var chinaSupplierCodeSet = targetSupplierCodes.Any(IsChinaLocalSupplierCode)
                    ? await GetChinaSupplierCodeSetAsync()
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 商品报告统一从日商品分店统计表汇总，避免供应商分店弹窗和商品明细口径不一致。
                var currentQuery = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes
                );
                currentQuery = ApplyAustralianSupplierStatisticFilter(
                    currentQuery,
                    targetSupplierCodes,
                    chinaSupplierCodeSet
                );
                var rawCurrentData = await currentQuery
                    .GroupBy(s => new { s.Date, s.BranchCode, s.SupplierCode })
                    .Select(s => new SupplierBranchAggregateRow
                    {
                        Date = s.Date,
                        BranchCode = s.BranchCode,
                        SupplierCode = s.SupplierCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();
                var currentData = ResolveAustralianSupplierBranchRows(
                    rawCurrentData,
                    chinaSupplierCodeSet,
                    preserveDate: true
                );

                if (!currentData.Any())
                {
                    // 商品统计尚未生成时，供应商下钻用供应商分店统计兜底，保证弹窗有同口径数据。
                    currentData = await QueryAustralianSupplierStoreAggregateRowsAsync(
                        startDate,
                        endDate,
                        branchCodes,
                        targetSupplierCodes
                    );
                }
                currentData = await AddChinaLocalSupplierFallbackRowsIfMissingAsync(
                    currentData,
                    startDate,
                    endDate,
                    branchCodes,
                    targetSupplierCodes.Any(IsChinaLocalSupplierCode),
                    chinaSupplierCodeSet
                );

                var currentBranchCodes = currentData
                    .Select(d => d.BranchCode?.Trim())
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var storeDict = await GetStoreNameMapAsync(currentBranchCodes);
                var supplierNameMap = await GetAustralianSupplierNameMapAsync(
                    currentData.Select(d => d.SupplierCode)
                );

                // 批量获取对比数据
                Dictionary<string, (decimal TotalAmount, int OrderCount)> compareDict =
                    new Dictionary<string, (decimal TotalAmount, int OrderCount)>(
                        StringComparer.OrdinalIgnoreCase
                    );

                if (
                    dateRange.CompareStartDate.HasValue
                    && dateRange.CompareEndDate.HasValue
                    && currentBranchCodes.Any()
                )
                {
                    var compareQuery = await BuildProductReportStatisticQueryAsync(
                        dateRange.CompareStartDate.Value.Date,
                        dateRange.CompareEndDate.Value.Date,
                        currentBranchCodes.ToList()
                    );
                    compareQuery = ApplyAustralianSupplierStatisticFilter(
                        compareQuery,
                        targetSupplierCodes,
                        chinaSupplierCodeSet
                    );
                    var rawCompareDataList = await compareQuery
                        .GroupBy(s => new { s.Date, s.BranchCode, s.SupplierCode })
                        .Select(s => new SupplierBranchAggregateRow
                        {
                            Date = s.Date,
                            BranchCode = s.BranchCode,
                            SupplierCode = s.SupplierCode,
                            TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                            OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        })
                        .ToListAsync();
                    var compareDataList = ResolveAustralianSupplierBranchRows(
                        rawCompareDataList,
                        chinaSupplierCodeSet,
                        preserveDate: true
                    );

                    if (!compareDataList.Any())
                    {
                        compareDataList = await QueryAustralianSupplierStoreAggregateRowsAsync(
                            dateRange.CompareStartDate.Value.Date,
                            dateRange.CompareEndDate.Value.Date,
                            currentBranchCodes.ToList(),
                            targetSupplierCodes
                        );
                    }
                    compareDataList = await AddChinaLocalSupplierFallbackRowsIfMissingAsync(
                        compareDataList,
                        dateRange.CompareStartDate.Value.Date,
                        dateRange.CompareEndDate.Value.Date,
                        currentBranchCodes.ToList(),
                        targetSupplierCodes.Any(IsChinaLocalSupplierCode),
                        chinaSupplierCodeSet
                    );

                    compareDict = BuildSupplierBranchCompareDict(compareDataList);
                }

                var hasCompare = dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue;
                var result = currentData
                    .Select(item =>
                    {
                        var branchCode = item.BranchCode ?? string.Empty;
                        var supplierCode = item.SupplierCode ?? string.Empty;
                        var orderCount = item.OrderCount ?? 0;
                        var compareKey = $"{branchCode}|{supplierCode}";
                        compareDict.TryGetValue(compareKey, out var compare);

                        var dto = new SupplierStoreSalesDto
                        {
                            StartDate = startDate,
                            EndDate = endDate,
                            SupplierCode = supplierCode,
                            SupplierName = supplierNameMap.TryGetValue(supplierCode, out var supplierName)
                                ? supplierName
                                : supplierCode,
                            BranchCode = branchCode,
                            BranchName = storeDict.TryGetValue(branchCode, out var branchName)
                                ? branchName
                                : branchCode,
                            TotalAmount = item.TotalAmount,
                            TotalQuantity = item.TotalQuantity,
                            // 分店下钻展示同一供应商在单店的客单数和客单价。
                            OrderCount = orderCount,
                            AverageTransaction =
                                orderCount > 0 ? item.TotalAmount / orderCount : 0,
                        };

                        if (hasCompare)
                        {
                            dto.CompareTotalAmount = compare.TotalAmount;
                            dto.CompareOrderCount = compare.OrderCount;
                            dto.CompareAverageTransaction =
                                compare.OrderCount > 0 ? compare.TotalAmount / compare.OrderCount : 0;
                            dto.TotalAmountGrowth = CalculateGrowth(
                                item.TotalAmount,
                                compare.TotalAmount
                            );
                        }

                        return dto;
                    })
                    .OrderByDescending(r => r.TotalAmount)
                    .ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "供应商分店销售数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSupplierStoreSalesAsync failed");
                throw;
            }
        }

        /// <summary>
        /// 获取指定分店的供应商销售排名
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表</param>
        /// <param name="topN">返回前 N 名，默认 20</param>
        /// <returns>分店供应商销售数据列表</returns>
        public async Task<List<StoreSupplierSalesDto>> GetStoreSupplierSalesAsync(
            DateRangeDto dateRange,
            List<string> branchCodes,
            int topN = 20
        )
        {
            try
            {
                if (branchCodes == null || !branchCodes.Any())
                    throw new ArgumentException("分店代码不能为空", nameof(branchCodes));

                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.StoreSupplier(dateRange, branchCodes, topN);

                if (
                    _cache.TryGetValue<List<StoreSupplierSalesDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取分店供应商销售数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<StoreSupplierSalesDetail>())
                {
                    return new List<StoreSupplierSalesDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var query = _context
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s =>
                        s.Date >= startDate
                        && s.Date <= endDate
                        && branchCodes.Contains(s.BranchCode)
                    );

                var currentData = await query
                    .GroupBy(s => new
                    {
                        s.BranchCode,
                        s.SupplierCode,
                        s.SupplierName,
                    })
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName ?? s.SupplierCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    })
                    .OrderByDescending(s => s.TotalAmount)
                    .ToListAsync();

                var branchCodeList = branchCodes.ToList();
                var stores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => branchCodeList.Contains(s.StoreCode))
                    .ToListAsync();
                var storeDict = stores.ToDictionary(s => s.StoreCode, s => s.StoreName);

                var result = new List<StoreSupplierSalesDto>();

                // 查询中国供应商数据
                var chinaSupplierQuery = _context
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s =>
                        s.Date >= startDate
                        && s.Date <= endDate
                        && s.IsDomestic == true
                        && branchCodes.Contains(s.BranchCode)
                    );

                var chinaSupplierDataList = await chinaSupplierQuery
                    .GroupBy(s => new { s.BranchCode })
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    })
                    .ToListAsync();

                var chinaSupplierDict = chinaSupplierDataList.ToDictionary(
                    s => s.BranchCode,
                    s => new { s.TotalAmount, s.TotalQuantity }
                );

                // 批量获取对比数据
                Dictionary<string, decimal> compareDict = new Dictionary<string, decimal>();
                Dictionary<string, decimal> chinaCompareDict = new Dictionary<string, decimal>();

                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var compareStartDate = dateRange.CompareStartDate.Value.Date;
                    var compareEndDate = dateRange.CompareEndDate.Value.Date;
                    var supplierCodes = currentData.Select(s => s.SupplierCode).ToList();

                    if (supplierCodes.Any())
                    {
                        var compareQuery = _context
                            .Db.Queryable<StoreSupplierSalesDetail>()
                            .Where(s =>
                                s.Date >= compareStartDate
                                && s.Date <= compareEndDate
                                && s.IsDomestic != true
                                && branchCodes.Contains(s.BranchCode)
                                && supplierCodes.Contains(s.SupplierCode)
                            );

                        var compareDataList = await compareQuery
                            .GroupBy(s => s.SupplierCode)
                            .Select(s => new
                            {
                                SupplierCode = s.SupplierCode,
                                TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                            })
                            .ToListAsync();

                        compareDict = compareDataList.ToDictionary(
                            s => s.SupplierCode,
                            s => s.TotalAmount
                        );
                    }

                    // 获取中国供应商对比数据（按分店）
                    var chinaCompareQuery = _context
                        .Db.Queryable<StoreSupplierSalesDetail>()
                        .Where(s =>
                            s.Date >= compareStartDate
                            && s.Date <= compareEndDate
                            && s.IsDomestic == true
                            && branchCodes.Contains(s.BranchCode)
                        );

                    var chinaCompareDataList = await chinaCompareQuery
                        .GroupBy(s => new { s.BranchCode })
                        .Select(s => new
                        {
                            BranchCode = s.BranchCode,
                            TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        })
                        .ToListAsync();

                    chinaCompareDict = chinaCompareDataList.ToDictionary(
                        s => s.BranchCode,
                        s => s.TotalAmount
                    );
                }

                var groupedByBranch = currentData.GroupBy(x => x.BranchCode);

                foreach (var branchGroup in groupedByBranch)
                {
                    var topSuppliers = branchGroup.OrderByDescending(x => x.TotalAmount).Take(topN);

                    foreach (var item in topSuppliers)
                    {
                        var branchName = storeDict.TryGetValue(item.BranchCode, out var storeName)
                            ? storeName
                            : item.BranchCode;

                        decimal totalAmount = item.TotalAmount;
                        int totalQuantity = item.TotalQuantity;
                        decimal? compareTotalAmount = null;

                        // 如果供应商代码为"200"，将中国供应商的数据合并到该供应商
                        if (
                            item.SupplierCode == "200"
                            && chinaSupplierDict.TryGetValue(item.BranchCode, out var chinaData)
                        )
                        {
                            totalAmount += chinaData.TotalAmount;
                            totalQuantity += chinaData.TotalQuantity;
                            compareTotalAmount = chinaCompareDict.TryGetValue(
                                item.BranchCode,
                                out var chinaCompare
                            )
                                ? chinaCompare
                                : null;
                        }

                        var dto = new StoreSupplierSalesDto
                        {
                            StartDate = startDate,
                            EndDate = endDate,
                            BranchCode = item.BranchCode,
                            BranchName = branchName,
                            SupplierCode = item.SupplierCode,
                            SupplierName = item.SupplierName,
                            TotalAmount = totalAmount,
                            TotalQuantity = totalQuantity,
                        };

                        if (
                            dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue
                        )
                        {
                            decimal supplierCompareAmount = 0;
                            if (compareDict.TryGetValue(item.SupplierCode, out var amount))
                            {
                                supplierCompareAmount = amount;
                            }

                            dto.CompareTotalAmount =
                                supplierCompareAmount + (compareTotalAmount ?? 0);
                            dto.TotalAmountGrowth = CalculateGrowth(
                                totalAmount,
                                dto.CompareTotalAmount ?? 0
                            );
                        }

                        result.Add(dto);
                    }
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "分店供应商销售数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStoreSupplierSalesAsync failed");
                return new List<StoreSupplierSalesDto>();
            }
        }

        /// <summary>
        /// 获取销售产品明细数据（分页）
        /// 从 POSM 数据库的 SalesOrder 和 SalesOrderDetail 表查询
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="supplierCodes">供应商代码列表（可选）</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页大小，默认 100</param>
        /// <returns>分页的产品销售明细</returns>
        public async Task<PagedSalesProductDetailDto> GetSalesProductDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null,
            int pageIndex = 1,
            int pageSize = 100
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.ProductDetail(
                    dateRange,
                    branchCodes,
                    supplierCodes,
                    pageIndex,
                    pageSize
                );

                if (
                    _cache.TryGetValue<PagedSalesProductDetailDto>(cacheKey, out var cachedResult)
                    && cachedResult != null
                )
                {
                    _logger.LogInformation("从缓存获取产品销售明细: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date.AddDays(1);

                var filteredSupplierCodes = supplierCodes?
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct()
                    .ToList();
                var hasSupplierFilter = filteredSupplierCodes?.Count > 0;
                int totalCount;

                if (hasSupplierFilter)
                {
                    var supplierQuery = _posmContext
                        .Db.Queryable<SalesOrder, SalesOrderDetail, PosmProductSupplierMapping>(
                            (o, d, m) =>
                                o.OrderGuid == d.OrderGuid
                                && d.ProductCode == m.ProductCode
                                && !m.IsDeleted
                        )
                        .Where((o, d, m) => o.OrderTime >= startDate && o.OrderTime < endDate);

                    if (branchCodes != null && branchCodes.Any())
                    {
                        supplierQuery = supplierQuery.Where(
                            (o, d, m) => branchCodes.Contains(o.BranchCode ?? string.Empty)
                        );
                    }

                    var requiredSupplierCodes = filteredSupplierCodes!;
                    supplierQuery = supplierQuery.Where(
                        (o, d, m) =>
                            m.ChinaSupplierCode != null
                            && requiredSupplierCodes.Contains(m.ChinaSupplierCode)
                    );

                    totalCount = await supplierQuery.CountAsync();
                    var skip = (pageIndex - 1) * pageSize;

                    var salesData = await supplierQuery
                        .OrderByDescending((o, d, m) => d.Quantity)
                        .Select(
                            (o, d, m) =>
                                new
                                {
                                    ProductCode = d.ProductCode ?? string.Empty,
                                    ProductName = d.ProductName,
                                    Quantity = d.Quantity ?? 0,
                                    ActualAmount = d.ActualAmount ?? 0,
                                    Price = d.Price ?? 0,
                                    Subtotal = d.Subtotal,
                                    Qty = d.Quantity,
                                }
                        )
                        .Skip(skip)
                        .Take(pageSize)
                        .ToListAsync();

                    var productCodes = salesData
                        .Where(s => !string.IsNullOrEmpty(s.ProductCode))
                        .Select(s => s.ProductCode)
                        .Distinct()
                        .ToList();

                    var products = new List<Product>();
                    if (productCodes.Any())
                    {
                        products = await _context
                            .Db.Queryable<Product>()
                            .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                            .ToListAsync();
                    }

                    var productDict = products.ToDictionary(p => p.ProductCode!);

                    var data = salesData
                        .Select(s => new SalesProductDetailDto
                        {
                            ProductCode = productDict.TryGetValue(s.ProductCode, out var product)
                                ? product.ItemNumber ?? s.ProductCode
                                : s.ProductCode,
                            ProductImage = productDict.TryGetValue(
                                s.ProductCode,
                                out var productWithImage
                            )
                                ? productWithImage.ProductImage
                                : null,
                            ProductName = s.ProductName,
                            Quantity = s.Qty ?? 0,
                            SalesAmount = s.ActualAmount,
                            UnitPrice = s.Price,
                            OriginalPrice =
                                s.Subtotal.HasValue && s.Qty.HasValue && s.Qty.Value > 0
                                    ? s.Subtotal.Value / s.Qty.Value
                                    : null,
                        })
                        .ToList();

                    var result = new PagedSalesProductDetailDto
                    {
                        Data = data,
                        Total = totalCount,
                        PageIndex = pageIndex,
                        PageSize = pageSize,
                    };

                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(DETAIL_CACHE_DURATION)
                        .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                    _cache.Set(cacheKey, result, cacheOptions);
                    _logger.LogInformation(
                        "产品销售明细已缓存: {CacheKey}, 过期时间: {Expiration}",
                        cacheKey,
                        DateTime.Now.Add(DETAIL_CACHE_DURATION)
                    );

                    return result;
                }
                else
                {
                    var baseQuery = _posmContext
                        .Db.Queryable<SalesOrder, SalesOrderDetail>(
                            (o, d) => o.OrderGuid == d.OrderGuid
                        )
                        .Where((o, d) => o.OrderTime >= startDate && o.OrderTime < endDate);

                    if (branchCodes != null && branchCodes.Any())
                    {
                        baseQuery = baseQuery.Where(
                            (o, d) => branchCodes.Contains(o.BranchCode ?? string.Empty)
                        );
                    }

                    totalCount = await baseQuery.CountAsync();
                    var skip = (pageIndex - 1) * pageSize;

                    var salesData = await baseQuery
                        .OrderByDescending((o, d) => d.Quantity)
                        .Select(
                            (o, d) =>
                                new
                                {
                                    ProductCode = d.ProductCode ?? string.Empty,
                                    ProductName = d.ProductName,
                                    Quantity = d.Quantity ?? 0,
                                    ActualAmount = d.ActualAmount ?? 0,
                                    Price = d.Price ?? 0,
                                    Subtotal = d.Subtotal,
                                    Qty = d.Quantity,
                                }
                        )
                        .Skip(skip)
                        .Take(pageSize)
                        .ToListAsync();

                    var productCodes = salesData
                        .Where(s => !string.IsNullOrEmpty(s.ProductCode))
                        .Select(s => s.ProductCode)
                        .Distinct()
                        .ToList();

                    var products = new List<Product>();
                    if (productCodes.Any())
                    {
                        products = await _context
                            .Db.Queryable<Product>()
                            .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                            .ToListAsync();
                    }

                    var productDict = products.ToDictionary(p => p.ProductCode ?? string.Empty);

                    var data = salesData
                        .Select(s => new SalesProductDetailDto
                        {
                            ProductCode = productDict.TryGetValue(s.ProductCode, out var product)
                                ? product.ItemNumber ?? s.ProductCode
                                : s.ProductCode,
                            ProductImage = productDict.TryGetValue(
                                s.ProductCode,
                                out var productWithImage
                            )
                                ? productWithImage.ProductImage
                                : null,
                            ProductName = s.ProductName,
                            Quantity = s.Qty ?? 0,
                            SalesAmount = s.ActualAmount,
                            UnitPrice = s.Price,
                            OriginalPrice =
                                s.Subtotal.HasValue && s.Qty.HasValue && s.Qty.Value > 0
                                    ? s.Subtotal.Value / s.Qty.Value
                                    : null,
                        })
                        .ToList();

                    var result = new PagedSalesProductDetailDto
                    {
                        Data = data,
                        Total = totalCount,
                        PageIndex = pageIndex,
                        PageSize = pageSize,
                    };

                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(DETAIL_CACHE_DURATION)
                        .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                    _cache.Set(cacheKey, result, cacheOptions);
                    _logger.LogInformation(
                        "产品销售明细已缓存: {CacheKey}, 过期时间: {Expiration}",
                        cacheKey,
                        DateTime.Now.Add(DETAIL_CACHE_DURATION)
                    );

                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSalesProductDetailsAsync failed");
                return new PagedSalesProductDetailDto
                {
                    Data = new List<SalesProductDetailDto>(),
                    Total = 0,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                };
            }
        }

        /// <summary>
        /// 计算增长率（decimal 类型）
        /// </summary>
        /// <param name="current">当前值</param>
        /// <param name="compare">对比值</param>
        /// <returns>增长率百分比，若对比值为 0 且当前值大于 0 返回 100，否则返回 0</returns>
        private decimal? CalculateGrowth(decimal current, decimal compare)
        {
            if (compare == 0)
                return current > 0 ? 100 : 0;
            return ((current - compare) / compare) * 100;
        }

        /// <summary>
        /// 计算增长率（int 类型）
        /// </summary>
        /// <param name="current">当前值</param>
        /// <param name="compare">对比值</param>
        /// <returns>增长率百分比，若对比值为 0 且当前值大于 0 返回 100，否则返回 0</returns>
        private decimal? CalculateGrowth(int current, int compare)
        {
            if (compare == 0)
                return current > 0 ? 100 : 0;
            return ((decimal)(current - compare) / compare) * 100;
        }

        /// <summary>
        /// 获取增强版销售产品明细数据（含折扣信息，分页）
        /// 支持按本地供应商和中国供应商过滤
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="localSupplierCodes">本地供应商代码列表（可选）</param>
        /// <param name="chinaSupplierCodes">中国供应商代码列表（可选）</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页大小，默认 100</param>
        /// <param name="productSearch">商品货号/条码搜索词（可选）</param>
        /// <returns>分页的含折扣信息的产品销售明细</returns>
        public async Task<PagedSalesProductDetailWithDiscountDto> GetEnhancedSalesProductDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? localSupplierCodes = null,
            List<string>? chinaSupplierCodes = null,
            int pageIndex = 1,
            int pageSize = 100,
            string? productSearch = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);
                pageIndex = Math.Max(1, pageIndex);
                pageSize = Math.Clamp(pageSize, 1, 100);
                var normalizedProductSearch = productSearch?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedProductSearch))
                {
                    normalizedProductSearch = null;
                }

                _logger.LogInformation(
                    "[GetEnhancedSalesProductDetailsAsync] Processing request: StartDate={StartDate}, EndDate={EndDate}, CompareStartDate={CompareStartDate}, CompareEndDate={CompareEndDate}, HasSupplierFilter={HasSupplierFilter}, HasProductSearch={HasProductSearch}",
                    dateRange.StartDate,
                    dateRange.EndDate,
                    dateRange.CompareStartDate,
                    dateRange.CompareEndDate,
                    (localSupplierCodes != null && localSupplierCodes.Any())
                        || (chinaSupplierCodes != null && chinaSupplierCodes.Any()),
                    normalizedProductSearch != null
                );

                var cacheKey = SalesDashboardCacheKeys.EnhancedProductDetail(
                    dateRange,
                    branchCodes,
                    localSupplierCodes,
                    chinaSupplierCodes,
                    pageIndex,
                    pageSize,
                    normalizedProductSearch
                );

                if (
                    _cache.TryGetValue<PagedSalesProductDetailWithDiscountDto>(
                        cacheKey,
                        out var cachedResult
                    )
                    && cachedResult != null
                )
                {
                    _logger.LogInformation("从缓存获取增强产品销售明细: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                // 商品报告统一读取日商品分店统计表，避免订单明细和供应商统计表口径不一致。
                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;
                var compareStartDate = dateRange.CompareStartDate?.Date;
                var compareEndDate = dateRange.CompareEndDate?.Date;
                var normalizedChinaSupplierCodes = NormalizeCodes(chinaSupplierCodes);
                var chinaProductMap = normalizedChinaSupplierCodes.Any()
                    ? await GetChinaSupplierProductMapAsync(normalizedChinaSupplierCodes)
                    : new Dictionary<string, string>();
                var currentData = await QueryProductReportProductAggregatesAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    normalizedChinaSupplierCodes,
                    chinaProductMap,
                    normalizedProductSearch
                );

                var compareData = new List<ProductReportProductAggregateRow>();
                if (compareStartDate.HasValue && compareEndDate.HasValue)
                {
                    compareData = await QueryProductReportProductAggregatesAsync(
                        compareStartDate.Value,
                        compareEndDate.Value,
                        branchCodes,
                        localSupplierCodes,
                        normalizedChinaSupplierCodes,
                        chinaProductMap,
                        normalizedProductSearch
                    );
                }

                var currentDataDict = currentData
                    .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                    .ToDictionary(x => x.ProductCode, StringComparer.OrdinalIgnoreCase);
                var compareDataDict = compareData
                    .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                    .ToDictionary(x => x.ProductCode, StringComparer.OrdinalIgnoreCase);
                // 商品报告必须把当前期和同期商品取并集，否则同期独有商品会在主表里消失。
                var productCodes = currentDataDict.Keys
                    .Union(compareDataDict.Keys, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var totalCount = productCodes.Count;
                var skip = (pageIndex - 1) * pageSize;
                var pageRows = productCodes
                    .Select(code => new
                    {
                        ProductCode = code,
                        Current = currentDataDict.GetValueOrDefault(code),
                        Compare = compareDataDict.GetValueOrDefault(code),
                    })
                    .OrderByDescending(x => x.Current?.SalesAmount ?? 0)
                    .ThenByDescending(x => x.Compare?.SalesAmount ?? 0)
                    .ThenBy(x => x.ProductCode)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToList();
                var pageProductCodes = pageRows.Select(x => x.ProductCode).ToList();
                var products = pageProductCodes.Any()
                    ? await _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ProductCode != null && pageProductCodes.Contains(p.ProductCode))
                        .Select(p => new ProductInfo
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ItemNumber = p.ItemNumber,
                            ProductImage = p.ProductImage,
                        })
                        .ToListAsync()
                    : new List<ProductInfo>();
                var productDict = products.ToDictionary(p => p.ProductCode, StringComparer.OrdinalIgnoreCase);

                var data = pageRows
                    .Select(row =>
                    {
                        var x = row.Current;
                        var compareData = row.Compare;
                        var result = new SalesProductDetailWithDiscountDto
                        {
                            ProductCode = row.ProductCode,
                            ItemNumber = productDict.TryGetValue(row.ProductCode, out var p)
                                ? (string?)p.ItemNumber
                                : null,
                            ProductImage = productDict.TryGetValue(row.ProductCode, out var p2)
                                ? (string?)p2.ProductImage
                                : null,
                            ProductName = x?.ProductName ?? compareData?.ProductName,
                            Quantity = x?.Quantity ?? 0,
                            DiscountedQuantity = 0,
                            SalesAmount = x?.SalesAmount ?? 0,
                            AverageUnitPrice = x is { Quantity: > 0 } ? x.SalesAmount / x.Quantity : 0,
                            AverageOriginalPrice = null,
                            OrderCount = x?.OrderCount ?? 0,
                        };

                        if (compareData != null)
                        {
                            result.QuantityLY = compareData.Quantity;
                            result.DiscountedQuantityLY = 0;
                            result.SalesAmountLY = compareData.SalesAmount;
                            result.AverageUnitPriceLY =
                                compareData.Quantity > 0 ? compareData.SalesAmount / compareData.Quantity : 0;
                            result.AverageOriginalPriceLY = null;
                            result.OrderCountLY = compareData.OrderCount;
                        }

                        return result;
                    })
                    .ToList();

                var resultPage = new PagedSalesProductDetailWithDiscountDto
                {
                    Data = data,
                    Total = totalCount,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                };

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(DETAIL_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                _cache.Set(cacheKey, resultPage, cacheOptions);
                _logger.LogInformation(
                    "增强产品销售明细已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(DETAIL_CACHE_DURATION)
                );

                return resultPage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetEnhancedSalesProductDetailsAsync failed");
                return new PagedSalesProductDetailWithDiscountDto
                {
                    Data = new List<SalesProductDetailWithDiscountDto>(),
                    Total = 0,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                };
            }
        }

        /// <summary>
        /// 获取指定产品在所有分店的销售数据
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="productCode">产品代码</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>产品在各分店的销售数据列表</returns>
        public async Task<List<ProductBranchSalesDto>> GetProductSalesByAllBranchesAsync(
            DateRangeDto dateRange,
            string productCode,
            List<string>? branchCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.ProductBranch(
                    dateRange,
                    productCode,
                    branchCodes
                );

                if (
                    _cache.TryGetValue<List<ProductBranchSalesDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取产品各分店销售数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;
                var compareStartDate = dateRange.CompareStartDate?.Date;
                var compareEndDate = dateRange.CompareEndDate?.Date;

                var salesQuery = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes
                );
                salesQuery = salesQuery.Where(s => s.ProductCode == productCode);

                var salesData = await salesQuery
                    .GroupBy(s => s.BranchCode)
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        Quantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        SalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    })
                    .ToListAsync();

                var compareSalesData = new List<ProductBranchAggregateRow>();
                if (compareStartDate.HasValue && compareEndDate.HasValue)
                {
                    var compareQuery = await BuildProductReportStatisticQueryAsync(
                        compareStartDate.Value,
                        compareEndDate.Value,
                        branchCodes
                    );
                    var compareRows = await compareQuery
                        .Where(s => s.ProductCode == productCode)
                        .GroupBy(s => s.BranchCode)
                        .Select(s => new
                        {
                            BranchCode = s.BranchCode,
                            Quantity = SqlFunc.AggregateSum(s.TotalQuantity),
                            SalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        })
                        .ToListAsync();
                    compareSalesData = compareRows
                        .Select(x => new ProductBranchAggregateRow
                        {
                            BranchCode = x.BranchCode ?? string.Empty,
                            Quantity = x.Quantity,
                            SalesAmount = x.SalesAmount,
                        })
                        .ToList();
                }

                var currentSalesByBranch = salesData
                    .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
                    .ToDictionary(
                        x => x.BranchCode ?? string.Empty,
                        x => new ProductBranchAggregateRow
                        {
                            BranchCode = x.BranchCode ?? string.Empty,
                            Quantity = x.Quantity,
                            SalesAmount = x.SalesAmount,
                        },
                        StringComparer.OrdinalIgnoreCase
                    );
                var compareSalesByBranch = compareSalesData
                    .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
                    .ToDictionary(x => x.BranchCode, StringComparer.OrdinalIgnoreCase);
                // 弹窗按当前期和同期分店取并集，保证同期独有分店也能看到。
                var branchCodeSet = currentSalesByBranch.Keys
                    .Union(compareSalesByBranch.Keys, StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var storeNameMap = await GetStoreNameMapAsync(branchCodeSet);

                var result = branchCodeSet
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(branchCode =>
                    {
                        currentSalesByBranch.TryGetValue(branchCode, out var current);
                        compareSalesByBranch.TryGetValue(branchCode, out var compare);

                        return new ProductBranchSalesDto
                        {
                            BranchCode = branchCode,
                            BranchName = storeNameMap.TryGetValue(branchCode, out var storeName)
                                ? storeName
                                : branchCode,
                            Quantity = current?.Quantity ?? 0,
                            DiscountedQuantity = 0,
                            SalesAmount = current?.SalesAmount ?? 0,
                            CompareQuantity = compare?.Quantity ?? 0,
                            CompareSalesAmount = compare?.SalesAmount ?? 0,
                            AverageUnitPrice =
                                current is { Quantity: > 0 } ? current.SalesAmount / current.Quantity : 0,
                            CompareAverageUnitPrice =
                                compare is { Quantity: > 0 } ? compare.SalesAmount / compare.Quantity : 0,
                        };
                    })
                    .OrderByDescending(x => x.SalesAmount)
                    .ThenByDescending(x => x.CompareSalesAmount)
                    .ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(DETAIL_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "产品各分店销售数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(DETAIL_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProductSalesByAllBranchesAsync failed");
                return new List<ProductBranchSalesDto>();
            }
        }

        /// <summary>
        /// 获取中国供应商在各分店的销售数据
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="supplierCodes">供应商代码列表（中国供应商代码）</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>中国供应商分店销售数据列表</returns>
        public async Task<List<ChinaSupplierStoreSalesDto>> GetChinaSupplierStoreSalesAsync(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes = null
        )
        {
            try
            {
                var targetSupplierCodes = NormalizeCodes(supplierCodes);
                if (!targetSupplierCodes.Any())
                    throw new ArgumentException("供应商代码不能为空", nameof(supplierCodes));

                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.ChinaSupplierStore(
                    dateRange,
                    targetSupplierCodes,
                    branchCodes
                );

                if (
                    _cache.TryGetValue<List<ChinaSupplierStoreSalesDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation(
                        "从缓存获取中国供应商分店销售数据: {CacheKey}",
                        cacheKey
                    );
                    return cachedResult;
                }

                var chinaProductMap = await GetChinaSupplierProductMapAsync(targetSupplierCodes);
                var targetChinaSupplierCodes = targetSupplierCodes.ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                // 中国供应商兼容两种统计编码：新统计直接写中国供应商，旧统计写 200 后按 POSM 商品映射。
                var currentQuery = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes
                );
                currentQuery = ApplyChinaSupplierStatisticFilter(
                    currentQuery,
                    targetChinaSupplierCodes,
                    chinaProductMap,
                    limitLegacy200Products: true
                );
                var currentAggregateRows = await currentQuery
                    .GroupBy(s => new { s.ProductCode, s.BranchCode, s.SupplierCode })
                    .Select(s => new
                    {
                        ProductCode = s.ProductCode,
                        BranchCode = s.BranchCode,
                        SupplierCode = s.SupplierCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();
                var currentRows = currentAggregateRows
                    .Select(row => new ProductSupplierBranchAggregateRow
                    {
                        ProductCode = row.ProductCode,
                        BranchCode = row.BranchCode,
                        SupplierCode = row.SupplierCode,
                        TotalAmount = row.TotalAmount,
                        TotalQuantity = row.TotalQuantity,
                        OrderCount = row.OrderCount,
                    })
                    .ToList();

                var currentData = ResolveChinaSupplierBranchRows(
                    currentRows,
                    chinaProductMap,
                    targetChinaSupplierCodes
                );
                var hasCurrentProductStatisticRows = currentRows.Any()
                    || await HasChinaProductStatisticRowsAsync(
                        startDate,
                        endDate,
                        branchCodes,
                        targetChinaSupplierCodes
                    );

                if (!hasCurrentProductStatisticRows)
                {
                    // 商品统计未命中时，下钻弹窗使用中国供应商分店统计表兜底。
                    currentData = await QueryChinaSupplierStoreAggregateRowsAsync(
                        startDate,
                        endDate,
                        branchCodes,
                        targetChinaSupplierCodes
                    );
                }

                var currentBranchCodes = currentData
                    .Select(d => d.BranchCode?.Trim())
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var storeDict = await GetStoreNameMapAsync(currentBranchCodes);
                var supplierNameMap = await GetChinaSupplierNameMapAsync(
                    currentData.Select(d => d.SupplierCode)
                );

                // 中国供应商分店下钻需要同期金额、客单数和客单价，供移动端第二行显示。
                Dictionary<string, (decimal TotalAmount, int OrderCount)> compareDict =
                    new Dictionary<string, (decimal TotalAmount, int OrderCount)>(
                        StringComparer.OrdinalIgnoreCase
                    );

                if (
                    dateRange.CompareStartDate.HasValue
                    && dateRange.CompareEndDate.HasValue
                    && currentBranchCodes.Any()
                )
                {
                    var compareQuery = await BuildProductReportStatisticQueryAsync(
                        dateRange.CompareStartDate.Value.Date,
                        dateRange.CompareEndDate.Value.Date,
                        currentBranchCodes.ToList()
                    );
                    compareQuery = ApplyChinaSupplierStatisticFilter(
                        compareQuery,
                        targetChinaSupplierCodes,
                        chinaProductMap,
                        limitLegacy200Products: true
                    );
                    var compareAggregateRows = await compareQuery
                        .GroupBy(s => new { s.ProductCode, s.BranchCode, s.SupplierCode })
                        .Select(s => new
                        {
                            ProductCode = s.ProductCode,
                            BranchCode = s.BranchCode,
                            SupplierCode = s.SupplierCode,
                            TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                            OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        })
                        .ToListAsync();
                    var compareRows = compareAggregateRows
                        .Select(row => new ProductSupplierBranchAggregateRow
                        {
                            ProductCode = row.ProductCode,
                            BranchCode = row.BranchCode,
                            SupplierCode = row.SupplierCode,
                            TotalAmount = row.TotalAmount,
                            OrderCount = row.OrderCount,
                        })
                        .ToList();

                    var compareDataList = ResolveChinaSupplierBranchRows(
                        compareRows,
                        chinaProductMap,
                        targetChinaSupplierCodes
                    );
                    var hasCompareProductStatisticRows = compareRows.Any()
                        || await HasChinaProductStatisticRowsAsync(
                            dateRange.CompareStartDate.Value.Date,
                            dateRange.CompareEndDate.Value.Date,
                            currentBranchCodes.ToList(),
                            targetChinaSupplierCodes
                        );

                    if (!hasCompareProductStatisticRows)
                    {
                        compareDataList = await QueryChinaSupplierStoreAggregateRowsAsync(
                            dateRange.CompareStartDate.Value.Date,
                            dateRange.CompareEndDate.Value.Date,
                            currentBranchCodes.ToList(),
                            targetChinaSupplierCodes
                        );
                    }

                    compareDict = BuildSupplierBranchCompareDict(compareDataList);
                }

                var hasCompare = dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue;
                var result = currentData
                    .Select(item =>
                    {
                        var branchCode = item.BranchCode ?? string.Empty;
                        var supplierCode = item.SupplierCode ?? string.Empty;
                        var orderCount = item.OrderCount ?? 0;
                        var compareKey = $"{branchCode}|{supplierCode}";
                        compareDict.TryGetValue(compareKey, out var compare);

                        var dto = new ChinaSupplierStoreSalesDto
                        {
                            StartDate = startDate,
                            EndDate = endDate,
                            SupplierCode = supplierCode,
                            SupplierName = supplierNameMap.TryGetValue(supplierCode, out var supplierName)
                                ? supplierName
                                : supplierCode,
                            BranchCode = branchCode,
                            BranchName = storeDict.TryGetValue(branchCode, out var branchName)
                                ? branchName
                                : branchCode,
                            TotalAmount = item.TotalAmount,
                            TotalQuantity = item.TotalQuantity,
                            OrderCount = orderCount,
                            AverageTransaction =
                                orderCount > 0 ? item.TotalAmount / orderCount : 0,
                        };

                        if (hasCompare)
                        {
                            dto.CompareTotalAmount = compare.TotalAmount;
                            dto.CompareOrderCount = compare.OrderCount;
                            dto.CompareAverageTransaction =
                                compare.OrderCount > 0 ? compare.TotalAmount / compare.OrderCount : 0;
                            dto.TotalAmountGrowth = CalculateGrowth(
                                item.TotalAmount,
                                compare.TotalAmount
                            );
                        }

                        return dto;
                    })
                    .OrderByDescending(r => r.TotalAmount)
                    .ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "中国供应商分店销售数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChinaSupplierStoreSalesAsync failed");
                throw;
            }
        }

        /// <summary>
        /// 获取澳洲供应商分店销售明细
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表</param>
        /// <param name="supplierCodes">供应商代码列表</param>
        /// <returns>澳洲供应商分店销售明细列表</returns>
        public async Task<
            List<AustralianSupplierStoreSalesDetailDto>
        > GetAustralianSupplierStoreSalesDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                // 明细数据不缓存或使用短时间缓存，考虑到数据量可能较大，这里暂不缓存或仅短时缓存
                // 如果需要缓存，需要设计合适的 CacheKey

                if (!ValidateDatabaseConnection<AustralianSupplierStoreSalesDetail>())
                {
                    return new List<AustralianSupplierStoreSalesDetailDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var query = _context
                    .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(s => supplierCodes.Contains(s.SupplierCode));
                }

                var result = await query
                    .OrderByDescending(s => s.TotalAmount)
                    .Select(s => new AustralianSupplierStoreSalesDetailDto
                    {
                        Date = s.Date,
                        BranchCode = s.BranchCode,
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName,
                        TotalAmount = s.TotalAmount,
                        TotalQuantity = s.TotalQuantity,
                        OrderCount = s.OrderCount,
                        UpdateTime = s.UpdateTime,
                    })
                    .ToListAsync();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAustralianSupplierStoreSalesDetailsAsync failed");
                return new List<AustralianSupplierStoreSalesDetailDto>();
            }
        }

        /// <summary>
        /// 获取中国供应商分店销售明细
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表</param>
        /// <param name="supplierCodes">供应商代码列表</param>
        /// <returns>中国供应商分店销售明细列表</returns>
        public async Task<
            List<ChinaSupplierStoreSalesDetailDto>
        > GetChinaSupplierStoreSalesDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                if (!ValidateDatabaseConnection<ChinaSupplierStoreSalesDetail>())
                {
                    return new List<ChinaSupplierStoreSalesDetailDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var query = _context
                    .Db.Queryable<ChinaSupplierStoreSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(s => supplierCodes.Contains(s.SupplierCode));
                }

                var result = await query
                    .OrderByDescending(s => s.TotalAmount)
                    .Select(s => new ChinaSupplierStoreSalesDetailDto
                    {
                        Date = s.Date,
                        BranchCode = s.BranchCode,
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName,
                        TotalAmount = s.TotalAmount,
                        TotalQuantity = s.TotalQuantity,
                        OrderCount = s.OrderCount,
                        UpdateTime = s.UpdateTime,
                    })
                    .ToListAsync();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChinaSupplierStoreSalesDetailsAsync failed");
                return new List<ChinaSupplierStoreSalesDetailDto>();
            }
        }

        /// <summary>
        /// 获取分店销售聚合数据
        /// 用于 SalesDetailAnalysisV2 门店分布卡，直接返回分店级别的聚合数据
        /// 避免前端接收大量明细数据后再进行聚合
        /// </summary>
        /// <param name="dateRange">日期范围（当前期）</param>
        /// <param name="compareDateRange">日期范围（去年同期，可选）</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="supplierCodes">供应商代码列表（可选，用于过滤）</param>
        /// <returns>分店销售聚合数据列表</returns>
        public async Task<List<BranchSalesAggregateDto>> GetBranchSalesAggregateAsync(
            DateRangeDto dateRange,
            DateRangeDto? compareDateRange = null,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                if (!ValidateDatabaseConnection<AustralianSupplierStoreSalesDetail>())
                {
                    return new List<BranchSalesAggregateDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var query = _context
                    .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(s => supplierCodes.Contains(s.SupplierCode));
                }

                var currentData = await query
                    .GroupBy(s => s.BranchCode)
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        TotalRevenue = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();

                var hbRevenueQuery = _context
                    .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    hbRevenueQuery = hbRevenueQuery.Where(s => branchCodes.Contains(s.BranchCode));
                }

                hbRevenueQuery = hbRevenueQuery.Where(s =>
                    s.SupplierCode == "#200" || s.SupplierCode == "200"
                );

                var hbRevenueData = await hbRevenueQuery
                    .GroupBy(s => s.BranchCode)
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        HbRevenue = SqlFunc.AggregateSum(s.TotalAmount),
                    })
                    .ToListAsync();

                var hbRevenueDict = hbRevenueData.ToDictionary(x => x.BranchCode, x => x.HbRevenue);

                var branchCodesList = currentData.Select(s => s.BranchCode).ToList();

                var branchNameQuery = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => branchCodesList.Contains(s.BranchCode))
                    .Select(s => new { s.BranchCode, s.BranchName })
                    .Distinct();

                var branchNames = await branchNameQuery.ToListAsync();
                var branchNameDict = branchNames.ToDictionary(x => x.BranchCode, x => x.BranchName);

                var result = currentData
                    .Select(s => new BranchSalesAggregateDto
                    {
                        BranchCode = s.BranchCode,
                        BranchName = branchNameDict.GetValueOrDefault(s.BranchCode, s.BranchCode),
                        TotalRevenue = s.TotalRevenue,
                        TotalRevenueLY = 0,
                        TotalQuantity = s.TotalQuantity,
                        TotalQuantityLY = 0,
                        OrderCount = (int)(s.OrderCount ?? 0),
                        OrderCountLY = 0,
                        HbRevenue = hbRevenueDict.GetValueOrDefault(s.BranchCode, 0),
                        HbRevenueLY = 0,
                    })
                    .ToList();

                if (
                    compareDateRange != null
                    && compareDateRange.CompareStartDate.HasValue
                    && compareDateRange.CompareEndDate.HasValue
                )
                {
                    var compareStartDate = compareDateRange.CompareStartDate.Value.Date;
                    var compareEndDate = compareDateRange.CompareEndDate.Value.Date;

                    var compareQuery = _context
                        .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                        .Where(s => s.Date >= compareStartDate && s.Date <= compareEndDate);

                    if (branchCodes != null && branchCodes.Any())
                    {
                        compareQuery = compareQuery.Where(s => branchCodes.Contains(s.BranchCode));
                    }

                    if (supplierCodes != null && supplierCodes.Any())
                    {
                        compareQuery = compareQuery.Where(s =>
                            supplierCodes.Contains(s.SupplierCode)
                        );
                    }

                    var compareData = await compareQuery
                        .GroupBy(s => s.BranchCode)
                        .Select(s => new
                        {
                            BranchCode = s.BranchCode,
                            TotalRevenue = SqlFunc.AggregateSum(s.TotalAmount),
                            TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                            OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        })
                        .ToListAsync();

                    var compareHbRevenueQuery = _context
                        .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                        .Where(s => s.Date >= compareStartDate && s.Date <= compareEndDate);

                    if (branchCodes != null && branchCodes.Any())
                    {
                        compareHbRevenueQuery = compareHbRevenueQuery.Where(s =>
                            branchCodes.Contains(s.BranchCode)
                        );
                    }

                    compareHbRevenueQuery = compareHbRevenueQuery.Where(s =>
                        s.SupplierCode == "#200" || s.SupplierCode == "200"
                    );

                    var compareHbRevenueData = await compareHbRevenueQuery
                        .GroupBy(s => s.BranchCode)
                        .Select(s => new
                        {
                            BranchCode = s.BranchCode,
                            HbRevenue = SqlFunc.AggregateSum(s.TotalAmount),
                        })
                        .ToListAsync();

                    var compareHbRevenueDict = compareHbRevenueData.ToDictionary(
                        x => x.BranchCode,
                        x => x.HbRevenue
                    );
                    var compareDataDict = compareData.ToDictionary(x => x.BranchCode);

                    foreach (var item in result)
                    {
                        if (compareDataDict.TryGetValue(item.BranchCode, out var compareItem))
                        {
                            item.TotalRevenueLY = compareItem.TotalRevenue;
                            item.TotalQuantityLY = compareItem.TotalQuantity;
                            item.OrderCountLY = (int)(compareItem.OrderCount ?? 0);
                            item.HbRevenueLY = compareHbRevenueDict.GetValueOrDefault(
                                item.BranchCode,
                                0
                            );
                        }
                    }
                }

                return result.OrderByDescending(r => r.TotalRevenue).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBranchSalesAggregateAsync failed");
                return new List<BranchSalesAggregateDto>();
            }
        }

        /// <summary>
        /// 获取 Executive Dashboard 分店业绩排名
        /// 用于 Executive Sales Intelligence 页面
        /// 查询分店的销售总额，按销售额排序并计算 YoY
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="topN">返回前N条记录</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>分店业绩排名列表</returns>
        public async Task<List<ExecutiveBranchPerformanceDto>> GetExecutiveBranchPerformanceAsync(
            DateRangeDto dateRange,
            int? topN = null,
            List<string>? branchCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);
                var normalizedBranchCodes = NormalizeBranchCodes(branchCodes);
                if (branchCodes != null && normalizedBranchCodes.Count == 0)
                    return new List<ExecutiveBranchPerformanceDto>();

                var statisticsRefreshState = await EnsureStoreSalesStatisticsAsync(
                    dateRange,
                    normalizedBranchCodes
                );

                var compareStartStr = dateRange.CompareStartDate?.ToString("yyyyMMdd") ?? "null";
                var compareEndStr = dateRange.CompareEndDate?.ToString("yyyyMMdd") ?? "null";
                var cacheKey =
                    $"ExecutiveBranchPerformance_{dateRange.StartDate:yyyyMMdd}_{dateRange.EndDate:yyyyMMdd}_{compareStartStr}_{compareEndStr}_{topN?.ToString() ?? "all"}_{string.Join(",", normalizedBranchCodes)}";

                if (
                    statisticsRefreshState == StatisticsRefreshState.NotNeeded
                    &&
                    _cache.TryGetValue<List<ExecutiveBranchPerformanceDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取 Executive 分店业绩: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                // 使用 StoreSalesStatistic 表查询分店销售数据
                var branchCurrentQuery = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (normalizedBranchCodes.Count > 0)
                {
                    branchCurrentQuery = branchCurrentQuery.Where(s =>
                        normalizedBranchCodes.Contains(s.BranchCode)
                    );
                }

                var currentData = await branchCurrentQuery
                    .GroupBy(s => s.BranchCode)
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        BranchName = SqlFunc.AggregateMax(s.BranchName),
                        Revenue = SqlFunc.AggregateSum(s.TotalAmount),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();

                var lyDict = new Dictionary<string, (decimal RevenueLY, int OrderCountLY)>(
                    StringComparer.OrdinalIgnoreCase
                );
                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var lyStartDate = dateRange.CompareStartDate.Value.Date;
                    var lyEndDate = dateRange.CompareEndDate.Value.Date;

                    var lyQuery = _context
                        .Db.Queryable<StoreSalesStatistic>()
                        .Where(s => s.Date >= lyStartDate && s.Date <= lyEndDate);

                    if (normalizedBranchCodes.Count > 0)
                    {
                        lyQuery = lyQuery.Where(s => normalizedBranchCodes.Contains(s.BranchCode));
                    }

                    var lyData = await lyQuery
                        .GroupBy(s => s.BranchCode)
                        .Select(s => new
                        {
                            BranchCode = s.BranchCode,
                            RevenueLY = SqlFunc.AggregateSum(s.TotalAmount),
                            OrderCountLY = SqlFunc.AggregateSum(s.OrderCount),
                        })
                        .ToListAsync();

                    lyDict = lyData.ToDictionary(
                        s => s.BranchCode,
                        s => (s.RevenueLY, s.OrderCountLY),
                        StringComparer.OrdinalIgnoreCase
                    );
                }

                // 构建结果并排序
                IEnumerable<ExecutiveBranchPerformanceDto> result = currentData
                    .Select(
                        (item, index) =>
                            new ExecutiveBranchPerformanceDto
                            {
                                Rank = index + 1,
                                BranchCode = item.BranchCode,
                                BranchName = string.IsNullOrWhiteSpace(item.BranchName)
                                    ? item.BranchCode
                                    : item.BranchName,
                                Revenue = item.Revenue,
                                RevenueLY = lyDict.TryGetValue(item.BranchCode, out var lyItem)
                                    ? lyItem.RevenueLY
                                    : 0,
                                OrderCount = item.OrderCount,
                                OrderCountLY = lyDict.TryGetValue(item.BranchCode, out var lyItem2)
                                    ? lyItem2.OrderCountLY
                                    : 0,
                                // AOV 在内存侧计算，避免统计表出现 0 单数时触发数据库除零。
                                Aov = item.OrderCount > 0 ? item.Revenue / item.OrderCount : 0,
                                AovLY = lyDict.TryGetValue(item.BranchCode, out var lyItem3)
                                    && lyItem3.OrderCountLY > 0
                                        ? lyItem3.RevenueLY / lyItem3.OrderCountLY
                                        : 0,
                            }
                    )
                    .OrderByDescending(x => x.Revenue);

                if (topN.HasValue && topN.Value > 0)
                {
                    result = result.Take(topN.Value);
                }

                var rankedResult = result
                    .Select(
                        (item, index) =>
                            new ExecutiveBranchPerformanceDto
                            {
                                Rank = index + 1,
                                BranchCode = item.BranchCode,
                                BranchName = item.BranchName,
                                Revenue = item.Revenue,
                                RevenueLY = item.RevenueLY,
                                OrderCount = item.OrderCount,
                                OrderCountLY = item.OrderCountLY,
                                Aov = item.Aov,
                                AovLY = item.AovLY,
                            }
                    )
                    .ToList();

                // 缓存结果
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                if (statisticsRefreshState != StatisticsRefreshState.Pending)
                {
                    _cache.Set(cacheKey, rankedResult, cacheOptions);
                }

                return rankedResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExecutiveBranchPerformanceAsync failed");
                throw;
            }
        }

        /// <summary>
        /// 获取 Executive Dashboard 每小时流量密度
        /// 用于 Executive Sales Intelligence 页面的每小时流量展示
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>每小时流量密度列表</returns>
        public async Task<List<ExecutiveHourlyTrafficDto>> GetExecutiveHourlyTrafficAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);
                var normalizedBranchCodes = NormalizeBranchCodes(branchCodes);
                if (branchCodes != null && normalizedBranchCodes.Count == 0)
                    return new List<ExecutiveHourlyTrafficDto>();

                var statisticsRefreshState = await EnsureHourlySalesStatisticsAsync(
                    dateRange,
                    normalizedBranchCodes
                );

                var compareStartStr = dateRange.CompareStartDate?.ToString("yyyyMMdd") ?? "null";
                var compareEndStr = dateRange.CompareEndDate?.ToString("yyyyMMdd") ?? "null";
                var cacheKey =
                    $"ExecutiveHourlyTraffic_{dateRange.StartDate:yyyyMMdd}_{dateRange.EndDate:yyyyMMdd}_{compareStartStr}_{compareEndStr}_{string.Join(",", normalizedBranchCodes)}";

                if (
                    statisticsRefreshState == StatisticsRefreshState.NotNeeded
                    &&
                    _cache.TryGetValue<List<ExecutiveHourlyTrafficDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取 Executive 每小时流量: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var query = _context
                    .Db.Queryable<HourlySalesStatistic>()
                    .Where(s =>
                        s.Date >= startDate
                        && s.Date <= endDate
                        && s.BranchCode != null
                        && s.BranchCode != "ALL"
                    );

                if (normalizedBranchCodes.Count > 0)
                {
                    query = query.Where(s =>
                        s.BranchCode != null && normalizedBranchCodes.Contains(s.BranchCode)
                    );
                }

                var hourlyData = (await query
                    .GroupBy(s => new { s.BranchCode, s.Hour })
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        BranchName = SqlFunc.AggregateMax(s.BranchName),
                        Hour = s.Hour,
                        Revenue = SqlFunc.AggregateSum(s.TotalAmount),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount ?? 0),
                    })
                    .OrderBy(s => s.BranchCode)
                    .ToListAsync())
                    .OrderBy(x => x.BranchCode)
                    .ThenBy(x => x.Hour)
                    .ToList();

                var lyDict = new Dictionary<(string? BranchCode, int Hour), (decimal RevenueLY, int OrderCountLY)>();
                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var lyStartDate = dateRange.CompareStartDate.Value.Date;
                    var lyEndDate = dateRange.CompareEndDate.Value.Date;

                    var lyQuery = _context
                        .Db.Queryable<HourlySalesStatistic>()
                        .Where(s =>
                            s.Date >= lyStartDate
                            && s.Date <= lyEndDate
                            && s.BranchCode != null
                            && s.BranchCode != "ALL"
                        );

                    if (normalizedBranchCodes.Count > 0)
                    {
                        lyQuery = lyQuery.Where(s =>
                            s.BranchCode != null && normalizedBranchCodes.Contains(s.BranchCode)
                        );
                    }

                    var lyHourlyData = await lyQuery
                        .GroupBy(s => new { s.BranchCode, s.Hour })
                        .Select(s => new
                        {
                            BranchCode = s.BranchCode,
                            Hour = s.Hour,
                            RevenueLY = SqlFunc.AggregateSum(s.TotalAmount),
                            OrderCountLY = SqlFunc.AggregateSum(s.OrderCount ?? 0),
                        })
                        .ToListAsync();

                    lyDict = lyHourlyData.ToDictionary(
                        s => (s.BranchCode, s.Hour),
                        s => (s.RevenueLY, s.OrderCountLY)
                    );
                }

                var result = hourlyData
                    .GroupBy(h => h.BranchCode)
                    .Select(branchGroup =>
                    {
                        var branchCode = branchGroup.Key ?? string.Empty;
                        var branchName = branchGroup
                            .Select(item => item.BranchName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                            ?? branchCode;
                        var branchMaxRevenue = branchGroup.Max(x => x.Revenue);
                        var peakThreshold = branchMaxRevenue * 0.8m;

                        return branchGroup.Select(item => new ExecutiveHourlyTrafficDto
                        {
                            Hour = $"{item.Hour:D2}:00",
                            BranchCode = branchCode,
                            BranchName = branchName,
                            Revenue = item.Revenue,
                            RevenueLY = lyDict.TryGetValue((branchCode, item.Hour), out var ly)
                                ? ly.RevenueLY
                                : 0,
                            OrderCount = item.OrderCount,
                            OrderCountLY = lyDict.TryGetValue((branchCode, item.Hour), out var lyOrder)
                                ? lyOrder.OrderCountLY
                                : 0,
                            Percentage =
                                branchMaxRevenue > 0
                                    ? (int)(item.Revenue * 100 / branchMaxRevenue)
                                    : 0,
                            IsPeak = item.Revenue >= peakThreshold,
                        });
                    })
                    .SelectMany(x => x)
                    .ToList();

                // 缓存结果
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                if (statisticsRefreshState != StatisticsRefreshState.Pending)
                {
                    _cache.Set(cacheKey, result, cacheOptions);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExecutiveHourlyTrafficAsync failed");
                throw;
            }
        }

        /// <summary>
        /// 获取分店每日营业额
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>分店每日营业额列表</returns>
        public async Task<List<BranchDailyPerformanceDto>> GetBranchDailyPerformanceAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);
                var normalizedBranchCodes = NormalizeBranchCodes(branchCodes);
                if (branchCodes != null && normalizedBranchCodes.Count == 0)
                    return new List<BranchDailyPerformanceDto>();

                var statisticsRefreshState = await EnsureStoreSalesStatisticsAsync(
                    dateRange,
                    normalizedBranchCodes
                );

                var compareStartStr = dateRange.CompareStartDate?.ToString("yyyyMMdd") ?? "null";
                var compareEndStr = dateRange.CompareEndDate?.ToString("yyyyMMdd") ?? "null";
                var cacheKey =
                    $"BranchDailyPerformance_{dateRange.StartDate:yyyyMMdd}_{dateRange.EndDate:yyyyMMdd}_{compareStartStr}_{compareEndStr}_{string.Join(",", normalizedBranchCodes)}";

                if (
                    statisticsRefreshState == StatisticsRefreshState.NotNeeded
                    &&
                    _cache.TryGetValue<List<BranchDailyPerformanceDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取分店每日营业额: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var currentQuery = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (normalizedBranchCodes.Count > 0)
                {
                    currentQuery = currentQuery.Where(s =>
                        normalizedBranchCodes.Contains(s.BranchCode)
                    );
                }

                var currentData = await currentQuery
                    .GroupBy(s => new { s.Date, s.BranchCode, s.BranchName })
                    .Select(s => new
                    {
                        Date = s.Date,
                        BranchCode = s.BranchCode,
                        BranchName = s.BranchName,
                        Revenue = SqlFunc.AggregateSum(s.TotalAmount),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();

                var lyDict = new Dictionary<(string BranchCode, DateTime Date), (decimal Revenue, int OrderCount)>();
                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var lyStartDate = dateRange.CompareStartDate.Value.Date;
                    var lyEndDate = dateRange.CompareEndDate.Value.Date;

                    var lyQuery = _context
                        .Db.Queryable<StoreSalesStatistic>()
                        .Where(s => s.Date >= lyStartDate && s.Date <= lyEndDate);

                    if (normalizedBranchCodes.Count > 0)
                    {
                        lyQuery = lyQuery.Where(s => normalizedBranchCodes.Contains(s.BranchCode));
                    }

                    var lyData = await lyQuery
                        .GroupBy(s => new { s.Date, s.BranchCode })
                        .Select(s => new
                        {
                            Date = s.Date,
                            BranchCode = s.BranchCode,
                            Revenue = SqlFunc.AggregateSum(s.TotalAmount),
                            OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        })
                        .ToListAsync();

                    lyDict = lyData.ToDictionary(
                        row => (row.BranchCode, row.Date.Date),
                        row => (row.Revenue, row.OrderCount)
                    );
                }

                // 按当前区间和对比区间的相同偏移天数配对，兼容同周和同月份规则。
                var compareStartDate = dateRange.CompareStartDate?.Date;
                var result = currentData
                    .Select(row =>
                    {
                        var compareDate = compareStartDate?.AddDays((row.Date.Date - startDate).Days);
                        var ly = compareDate.HasValue
                            && lyDict.TryGetValue((row.BranchCode, compareDate.Value), out var matched)
                                ? matched
                                : (Revenue: 0m, OrderCount: 0);

                        return new BranchDailyPerformanceDto
                        {
                            Date = row.Date.Date,
                            BranchCode = row.BranchCode,
                            BranchName = row.BranchName,
                            Revenue = row.Revenue,
                            RevenueLY = ly.Revenue,
                            OrderCount = row.OrderCount,
                            OrderCountLY = ly.OrderCount,
                        };
                    })
                    .OrderBy(row => row.Date)
                    .ThenByDescending(row => row.Revenue)
                    .ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                if (statisticsRefreshState != StatisticsRefreshState.Pending)
                {
                    _cache.Set(cacheKey, result, cacheOptions);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBranchDailyPerformanceAsync failed");
                throw;
            }
        }

        /// <summary>
        /// 获取周业绩层级数据
        /// 用于 Executive Sales Intelligence 页面的 Weekly Performance Hierarchy 组件
        /// 返回周→分店→日期三层嵌套结构
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>周业绩层级数据列表</returns>
        public async Task<List<WeeklyPerformanceHierarchyDto>> GetWeeklyPerformanceHierarchyAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var cacheKey =
                    $"WeeklyPerformanceHierarchy_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}_{string.Join(",", branchCodes ?? new List<string>())}";

                if (
                    _cache.TryGetValue<List<WeeklyPerformanceHierarchyDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取周业绩层级数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var query = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var currentData = await query
                    .Select(s => new
                    {
                        s.Date,
                        s.BranchCode,
                        s.BranchName,
                        s.TotalAmount,
                        s.TotalQuantity,
                        s.OrderCount,
                        s.AverageOrderValue,
                    })
                    .ToListAsync();

                var currentYear = ISOWeek.GetYear(startDate);
                var currentWeekStart = ISOWeek.GetWeekOfYear(startDate);
                var currentWeekEnd = ISOWeek.GetWeekOfYear(endDate);
                var lastYear = currentYear - 1;

                var firstDayOfWeek = ISOWeek.ToDateTime(
                    lastYear,
                    currentWeekStart,
                    DayOfWeek.Monday
                );
                var lastDayOfWeek = ISOWeek.ToDateTime(lastYear, currentWeekEnd, DayOfWeek.Sunday);

                var lyQuery = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => s.Date >= firstDayOfWeek && s.Date <= lastDayOfWeek);

                if (branchCodes != null && branchCodes.Any())
                {
                    lyQuery = lyQuery.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var lastYearData = await lyQuery
                    .Select(s => new
                    {
                        s.Date,
                        s.BranchCode,
                        s.BranchName,
                        s.TotalAmount,
                        s.TotalQuantity,
                        s.OrderCount,
                        s.AverageOrderValue,
                    })
                    .ToListAsync();

                var lyDataDict = lastYearData.ToDictionary(
                    x =>
                        $"{ISOWeek.GetYear(x.Date)}-W{ISOWeek.GetWeekOfYear(x.Date):D2}|{x.BranchCode}|{x.Date:yyyy-MM-dd}",
                    x => x
                );

                var lyWeekBranchDict = lastYearData
                    .GroupBy(x =>
                        $"{ISOWeek.GetYear(x.Date)}-W{ISOWeek.GetWeekOfYear(x.Date):D2}|{x.BranchCode}"
                    )
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            Revenue = g.Sum(x => x.TotalAmount),
                            Orders = g.Sum(x => x.OrderCount),
                        }
                    );

                var lyWeekDict = lastYearData
                    .GroupBy(x => $"{ISOWeek.GetYear(x.Date)}-W{ISOWeek.GetWeekOfYear(x.Date):D2}")
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            Revenue = g.Sum(x => x.TotalAmount),
                            Orders = g.Sum(x => x.OrderCount),
                        }
                    );

                var storeDict = currentData
                    .GroupBy(s => s.BranchCode)
                    .ToDictionary(g => g.Key, g => g.First().BranchName);

                var weekGroups = currentData
                    .GroupBy(s => new
                    {
                        Year = ISOWeek.GetYear(s.Date),
                        Week = ISOWeek.GetWeekOfYear(s.Date),
                        s.BranchCode,
                    })
                    .OrderByDescending(g => g.Key.Year)
                    .ThenByDescending(g => g.Key.Week)
                    .ThenByDescending(g => g.Sum(x => x.TotalAmount))
                    .ToList();

                var result = new List<WeeklyPerformanceHierarchyDto>();

                foreach (var weekGroup in weekGroups)
                {
                    var weekKey = $"w{weekGroup.Key.Year}-{weekGroup.Key.Week:D2}";
                    var weekLabel = $"{weekGroup.Key.Year}-W{weekGroup.Key.Week:D2}";

                    var weekRevenue = weekGroup.Sum(x => x.TotalAmount);
                    var weekOrders = weekGroup.Sum(x => x.OrderCount);
                    var weekAov = weekOrders > 0 ? weekRevenue / weekOrders : 0;

                    var weekDto = result.FirstOrDefault(w => w.Key == weekKey);
                    if (weekDto == null)
                    {
                        var lyWeekKey = $"{weekGroup.Key.Year - 1}-W{weekGroup.Key.Week:D2}";
                        lyWeekDict.TryGetValue(lyWeekKey, out var lyWeekData);

                        var revenueLY = lyWeekData?.Revenue ?? 0;
                        var ordersLY = lyWeekData?.Orders ?? 0;
                        var aovLY = ordersLY > 0 ? revenueLY / ordersLY : 0;
                        var yoyChange =
                            revenueLY > 0
                                ? ((weekRevenue - revenueLY) / revenueLY) * 100
                                : (decimal?)null;

                        weekDto = new WeeklyPerformanceHierarchyDto
                        {
                            Key = weekKey,
                            Level = "week",
                            Hierarchy = weekLabel,
                            Revenue = weekRevenue,
                            RevenueLY = revenueLY,
                            Orders = weekOrders,
                            OrdersLY = ordersLY,
                            Aov = weekAov,
                            AovLY = aovLY,
                            YoYChange = yoyChange,
                            Children = new List<WeeklyPerformanceHierarchyDto>(),
                        };
                        result.Add(weekDto);
                    }

                    var branchGroups = weekGroup
                        .GroupBy(s => s.BranchCode)
                        .OrderByDescending(g => g.Sum(x => x.TotalAmount))
                        .ToList();

                    if (weekDto == null)
                    {
                        continue;
                    }

                    foreach (var branchGroup in branchGroups)
                    {
                        var branchCode = branchGroup.Key;
                        var branchName = storeDict.GetValueOrDefault(branchCode, branchCode);

                        var branchRevenue = branchGroup.Sum(x => x.TotalAmount);
                        var branchOrders = branchGroup.Sum(x => x.OrderCount);
                        var branchAov = branchOrders > 0 ? branchRevenue / branchOrders : 0;

                        var branchDto = weekDto.Children!.FirstOrDefault(b =>
                            b.Key == $"{weekKey}-{branchCode}"
                        );
                        if (branchDto == null)
                        {
                            var lyBranchKey =
                                $"{weekGroup.Key.Year - 1}-W{weekGroup.Key.Week:D2}|{branchCode}";
                            lyWeekBranchDict.TryGetValue(lyBranchKey, out var lyBranchData);

                            var branchRevenueLY = lyBranchData?.Revenue ?? 0;
                            var branchOrdersLY = lyBranchData?.Orders ?? 0;
                            var branchAovLY =
                                branchOrdersLY > 0 ? branchRevenueLY / branchOrdersLY : 0;
                            var branchYoYChange =
                                branchRevenueLY > 0
                                    ? ((branchRevenue - branchRevenueLY) / branchRevenueLY) * 100
                                    : (decimal?)null;

                            branchDto = new WeeklyPerformanceHierarchyDto
                            {
                                Key = $"{weekKey}-{branchCode}",
                                Level = "branch",
                                Hierarchy = branchName,
                                Revenue = branchRevenue,
                                RevenueLY = branchRevenueLY,
                                Orders = branchOrders,
                                OrdersLY = branchOrdersLY,
                                Aov = branchAov,
                                AovLY = branchAovLY,
                                YoYChange = branchYoYChange,
                                Children = new List<WeeklyPerformanceHierarchyDto>(),
                            };
                            weekDto.Children ??= new List<WeeklyPerformanceHierarchyDto>();
                            weekDto.Children.Add(branchDto);
                        }

                        var dateGroups = branchGroup.OrderByDescending(x => x.Date).ToList();

                        foreach (var dateItem in dateGroups)
                        {
                            var dateKey = $"{weekKey}-{branchCode}-{dateItem.Date:yyyyMMdd}";
                            var dateAov =
                                dateItem.OrderCount > 0
                                    ? dateItem.TotalAmount / dateItem.OrderCount
                                    : 0;

                            var lastYearDate = GetLastYearSameWeekday(dateItem.Date);
                            var lyDateKey =
                                $"{weekGroup.Key.Year - 1}-W{weekGroup.Key.Week:D2}|{branchCode}|{lastYearDate:yyyy-MM-dd}";
                            lyDataDict.TryGetValue(lyDateKey, out var lyDateData);

                            var dateRevenueLY = lyDateData?.TotalAmount ?? 0;
                            var dateOrdersLY = lyDateData?.OrderCount ?? 0;
                            var dateAovLY = lyDateData?.AverageOrderValue ?? 0;
                            var dateYoYChange =
                                dateRevenueLY > 0
                                    ? ((dateItem.TotalAmount - dateRevenueLY) / dateRevenueLY) * 100
                                    : (decimal?)null;

                            var dateDto = new WeeklyPerformanceHierarchyDto
                            {
                                Key = dateKey,
                                Level = "date",
                                Hierarchy = dateItem.Date.ToString("yyyy-MM-dd"),
                                Revenue = dateItem.TotalAmount,
                                RevenueLY = dateRevenueLY,
                                Orders = dateItem.OrderCount,
                                OrdersLY = dateOrdersLY,
                                Aov = dateAov,
                                AovLY = dateAovLY,
                                YoYChange = dateYoYChange,
                                Children = null,
                            };
                            branchDto.Children!.Add(dateDto);
                        }

                        branchDto.Revenue = branchDto.Children!.Sum(c => c.Revenue);
                        branchDto.Orders = branchDto.Children!.Sum(c => c.Orders);
                        branchDto.RevenueLY = branchDto.Children!.Sum(c => c.RevenueLY);
                        branchDto.OrdersLY = branchDto.Children!.Sum(c => c.OrdersLY);
                        branchDto.Aov =
                            branchDto.Orders > 0 ? branchDto.Revenue / branchDto.Orders : 0;
                        branchDto.AovLY =
                            branchDto.OrdersLY > 0 ? branchDto.RevenueLY / branchDto.OrdersLY : 0;
                    }

                    weekDto.Revenue = weekDto.Children!.Sum(c => c.Revenue);
                    weekDto.Orders = weekDto.Children!.Sum(c => c.Orders);
                    weekDto.RevenueLY = weekDto.Children!.Sum(c => c.RevenueLY);
                    weekDto.OrdersLY = weekDto.Children!.Sum(c => c.OrdersLY);
                    weekDto.Aov = weekDto.Orders > 0 ? weekDto.Revenue / weekDto.Orders : 0;
                    weekDto.AovLY = weekDto.OrdersLY > 0 ? weekDto.RevenueLY / weekDto.OrdersLY : 0;
                    weekDto.YoYChange =
                        weekDto.RevenueLY > 0
                            ? ((weekDto.Revenue - weekDto.RevenueLY) / weekDto.RevenueLY) * 100
                            : (decimal?)null;

                    if (
                        weekDto.Children!.Count > 0
                        && weekDto.Children!.Any(c => c.Children!.Count > 0)
                    )
                    {
                        var firstBranchWithDates = weekDto.Children!.FirstOrDefault(c =>
                            c.Children!.Count > 0
                        );
                        if (firstBranchWithDates != null)
                        {
                            weekDto.Children = weekDto
                                .Children!.OrderByDescending(c => c.Revenue)
                                .ToList();
                        }
                    }
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetWeeklyPerformanceHierarchyAsync failed");
                return new List<WeeklyPerformanceHierarchyDto>();
            }
        }

        private DateTime GetLastYearSameWeekday(DateTime date)
        {
            var compareDate = date.AddYears(-1);
            var originalDayOfWeek = date.DayOfWeek;
            var compareDayOfWeek = compareDate.DayOfWeek;

            if (originalDayOfWeek != compareDayOfWeek)
            {
                var diff = (int)originalDayOfWeek - (int)compareDayOfWeek;
                if (diff > 3)
                    diff -= 7;
                if (diff < -3)
                    diff += 7;
                compareDate = compareDate.AddDays(diff);
            }

            return compareDate;
        }

        private async Task<StatisticsRefreshState> EnsureStoreSalesStatisticsAsync(
            DateRangeDto dateRange,
            List<string> branchCodes
        )
        {
            var missingDates = await GetMissingStoreStatisticDatesAsync(
                dateRange.StartDate.Date,
                dateRange.EndDate.Date,
                branchCodes
            );

            if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
            {
                missingDates.AddRange(await GetMissingStoreStatisticDatesAsync(
                    dateRange.CompareStartDate.Value.Date,
                    dateRange.CompareEndDate.Value.Date,
                    branchCodes
                ));
            }

            return await RefreshMissingStatisticsAsync(
                "store",
                "分店营业额",
                missingDates,
                branchCodes,
                (service, date) => service.UpdateStoreStatistics(
                    date,
                    branchCodes.Count > 0 ? branchCodes : null
                )
            );
        }

        private async Task<StatisticsRefreshState> EnsureHourlySalesStatisticsAsync(
            DateRangeDto dateRange,
            List<string> branchCodes
        )
        {
            var missingDates = await GetMissingHourlyStatisticDatesAsync(
                dateRange.StartDate.Date,
                dateRange.EndDate.Date,
                branchCodes
            );

            if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
            {
                missingDates.AddRange(await GetMissingHourlyStatisticDatesAsync(
                    dateRange.CompareStartDate.Value.Date,
                    dateRange.CompareEndDate.Value.Date,
                    branchCodes
                ));
            }

            return await RefreshMissingStatisticsAsync(
                "hourly",
                "分时营业额",
                missingDates,
                new List<string>(),
                (service, date) => service.UpdateHourlyStatistics(date)
            );
        }

        private async Task<List<DateTime>> GetMissingStoreStatisticDatesAsync(
            DateTime startDate,
            DateTime endDate,
            List<string> branchCodes
        )
        {
            var expectedDates = EnumerateReportDates(startDate, endDate);
            if (expectedDates.Count == 0)
                return new List<DateTime>();

            var query = _context.Db.Queryable<StoreSalesStatistic>()
                .Where(s => s.Date >= startDate && s.Date <= endDate);
            if (branchCodes.Count > 0)
            {
                query = query.Where(s => s.BranchCode != null && branchCodes.Contains(s.BranchCode));
            }

            var rows = await query
                .Select(s => new StatisticDateBranchRow { Date = s.Date, BranchCode = s.BranchCode })
                .ToListAsync();
            Dictionary<DateTime, int>? expectedBranchCounts = null;
            if (branchCodes.Count == 0)
            {
                expectedBranchCounts = await GetPosmSalesBranchCountsAsync(startDate, endDate);
                if (expectedBranchCounts == null)
                {
                    // 全分店覆盖无法确认时，保守触发营业额统计重算，避免把部分分店统计误判为完整并写入缓存。
                    return expectedDates;
                }
            }

            return GetMissingDatesFromRows(expectedDates, branchCodes, rows, expectedBranchCounts);
        }

        private async Task<List<DateTime>> GetMissingHourlyStatisticDatesAsync(
            DateTime startDate,
            DateTime endDate,
            List<string> branchCodes
        )
        {
            var expectedDates = EnumerateReportDates(startDate, endDate);
            if (expectedDates.Count == 0)
                return new List<DateTime>();

            var query = _context.Db.Queryable<HourlySalesStatistic>()
                .Where(s => s.Date >= startDate && s.Date <= endDate);
            if (branchCodes.Count > 0)
            {
                query = query.Where(s => s.BranchCode != null && branchCodes.Contains(s.BranchCode));
            }

            var rows = await query
                .Select(s => new StatisticDateBranchHourRow
                {
                    Date = s.Date,
                    BranchCode = s.BranchCode,
                    Hour = s.Hour,
                    OrderCount = s.OrderCount ?? 0,
                    TotalAmount = s.TotalAmount,
                })
                .ToListAsync();
            return GetMissingHourlyDatesFromRows(
                expectedDates,
                branchCodes,
                rows
            );
        }

        private async Task<Dictionary<DateTime, int>?> GetPosmSalesBranchCountsAsync(
            DateTime startDate,
            DateTime endDate
        )
        {
            try
            {
                var nextDate = endDate.Date.AddDays(1);
                var rows = await _posmContext.Db.Queryable<SalesOrder>()
                    .Where(so =>
                        so.Status != null
                        && (so.Status == 1 || so.Status == 4)
                        && so.OrderTime != null
                        && so.OrderTime >= startDate
                        && so.OrderTime < nextDate
                        && so.BranchCode != null
                        && so.BranchCode != ""
                    )
                    .GroupBy(so => new
                    {
                        Date = so.OrderTime!.Value.Date,
                        so.BranchCode,
                    })
                    .Select(so => new StatisticDateBranchRow
                    {
                        Date = so.OrderTime!.Value.Date,
                        BranchCode = so.BranchCode,
                    })
                    .ToListAsync();

                return rows
                    .GroupBy(row => row.Date.Date)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .Select(row => row.BranchCode!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count()
                    );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 POSM 分店销售覆盖范围失败，保守触发全分店营业额统计重算");
                return null;
            }
        }

        private async Task<StatisticsRefreshState> RefreshMissingStatisticsAsync(
            string kind,
            string label,
            IEnumerable<DateTime> missingDates,
            List<string> branchCodes,
            Func<SalesStatisticsJobService, DateTime, Task> refreshAsync
        )
        {
            var dates = missingDates
                .Select(date => date.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();
            if (dates.Count == 0)
                return StatisticsRefreshState.NotNeeded;

            if (_serviceScopeFactory == null)
            {
                _logger.LogWarning("{Label}统计缺失但无法自动重算，缺少 IServiceScopeFactory", label);
                return StatisticsRefreshState.Pending;
            }

            var branchKey = branchCodes.Count > 0
                ? string.Join(",", branchCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase))
                : "ALL";
            var pendingItems = dates
                .Select(date => new
                {
                    Date = date,
                    Key = BuildReportStatisticsRefreshKey(kind, date, branchKey),
                })
                .Where(item => REPORT_STATISTICS_REFRESHING_KEYS.TryAdd(item.Key, 0))
                .ToList();

            if (pendingItems.Count == 0)
                return StatisticsRefreshState.Pending;

            var refreshTask = Task.Run(async () =>
            {
                var allSucceeded = true;
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var statisticsJobService = scope.ServiceProvider.GetRequiredService<SalesStatisticsJobService>();

                    foreach (var item in pendingItems)
                    {
                        try
                        {
                            // 报表请求只触发缺口日期重算，实际统计口径复用后台统计任务。
                            await refreshAsync(statisticsJobService, item.Date);
                        }
                        catch (Exception ex)
                        {
                            allSucceeded = false;
                            _logger.LogWarning(ex, "{Label}统计自动重算失败: {Date}", label, item.Date);
                        }
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    _logger.LogWarning(ex, "{Label}统计自动重算任务启动失败", label);
                }
                finally
                {
                    foreach (var item in pendingItems)
                    {
                        REPORT_STATISTICS_REFRESHING_KEYS.TryRemove(item.Key, out _);
                    }
                }

                return allSucceeded;
            });

            var completedTask = await Task.WhenAny(
                refreshTask,
                Task.Delay(REPORT_STATISTICS_REFRESH_WAIT)
            );
            if (completedTask == refreshTask)
            {
                return await refreshTask
                    ? StatisticsRefreshState.Completed
                    : StatisticsRefreshState.Pending;
            }

            // 查询接口最多短等一小段时间；慢重算留给后台继续，避免移动端卡超过 3 秒。
            _logger.LogInformation(
                "{Label}统计缺失，已触发后台重算: {Dates}",
                label,
                string.Join(",", pendingItems.Select(item => item.Date.ToString("yyyy-MM-dd")))
            );
            return StatisticsRefreshState.Pending;
        }

        private static List<DateTime> EnumerateReportDates(DateTime startDate, DateTime endDate)
        {
            startDate = startDate.Date;
            endDate = endDate.Date;
            if (startDate > endDate)
                return new List<DateTime>();

            var days = (endDate - startDate).Days + 1;
            if (days > REPORT_STATISTICS_REFRESH_MAX_DAYS)
                return new List<DateTime>();

            return Enumerable.Range(0, days)
                .Select(offset => startDate.AddDays(offset))
                .ToList();
        }

        private static List<DateTime> GetMissingDatesFromRows(
            List<DateTime> expectedDates,
            List<string> branchCodes,
            IEnumerable<StatisticDateBranchRow> rows,
            Dictionary<DateTime, int>? expectedBranchCounts = null
        )
        {
            if (branchCodes.Count == 0)
            {
                if (expectedBranchCounts != null && expectedBranchCounts.Count > 0)
                {
                    var foundBranchCounts = rows
                        .Where(row =>
                            !string.IsNullOrWhiteSpace(row.BranchCode)
                            && !string.Equals(row.BranchCode, "ALL", StringComparison.OrdinalIgnoreCase)
                        )
                        .GroupBy(row => row.Date.Date)
                        .ToDictionary(
                            group => group.Key,
                            group => group
                                .Select(row => row.BranchCode!)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count()
                        );

                    return expectedDates
                        .Where(date =>
                            expectedBranchCounts.TryGetValue(date, out var expectedCount)
                            && foundBranchCounts.GetValueOrDefault(date) < expectedCount
                        )
                        .ToList();
                }

                var datesWithRows = rows.Select(row => row.Date.Date).ToHashSet();
                return expectedDates
                    .Where(date => !datesWithRows.Contains(date))
                    .ToList();
            }

            var branchSet = branchCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var foundByDate = rows
                .Where(row => !string.IsNullOrWhiteSpace((string?)row.BranchCode))
                .GroupBy(row => row.Date.Date)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(row => row.BranchCode!)
                        .Where(code => branchSet.Contains(code))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count()
                );

            return expectedDates
                .Where(date => foundByDate.GetValueOrDefault(date) < branchSet.Count)
                .ToList();
        }

        private static List<DateTime> GetMissingHourlyDatesFromRows(
            List<DateTime> expectedDates,
            List<string> branchCodes,
            IEnumerable<StatisticDateBranchHourRow> rows,
            Dictionary<DateTime, int>? expectedBranchCounts = null
        )
        {
            var rowList = rows.ToList();
            var missingDates = GetMissingDatesFromRows(
                    expectedDates,
                    branchCodes,
                    rowList.Select(row => new StatisticDateBranchRow
                    {
                        Date = row.Date,
                        BranchCode = row.BranchCode,
                    }),
                    expectedBranchCounts
                )
                .ToHashSet();

            if (branchCodes.Count == 0)
            {
                var datesWithStoreRows = rowList
                    .Where(row =>
                        !string.IsNullOrWhiteSpace(row.BranchCode)
                        && !string.Equals(row.BranchCode, "ALL", StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(row => row.Date.Date)
                    .ToHashSet();

                foreach (var date in expectedDates.Where(date => !datesWithStoreRows.Contains(date)))
                {
                    // 分时营业额不能把 ALL 汇总行当成真实分店数据。
                    missingDates.Add(date);
                }
            }

            foreach (var date in rowList
                         .Where(row =>
                             !string.IsNullOrWhiteSpace(row.BranchCode)
                             && !string.Equals(row.BranchCode, "ALL", StringComparison.OrdinalIgnoreCase)
                             && row.TotalAmount > 0
                             && row.OrderCount <= 0
                         )
                         .Select(row => row.Date.Date))
            {
                // 营业额报表只信营业额统计表；若统计行已有金额却没有客单数，说明该日统计口径需要重算。
                missingDates.Add(date);
            }

            return missingDates
                .OrderBy(date => date)
                .ToList();
        }

        private static string BuildReportStatisticsRefreshKey(
            string kind,
            DateTime date,
            string branchKey
        )
        {
            return $"ReportStatisticsRefresh_{kind}_{date:yyyyMMdd}_{branchKey}";
        }

        private static List<string> NormalizeBranchCodes(List<string>? branchCodes)
        {
            return branchCodes?
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!.Trim())
                    .Distinct()
                    .ToList()
                ?? new List<string>();
        }

        private static List<string> NormalizeCodes(IEnumerable<string>? codes)
        {
            return codes?
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                ?? new List<string>();
        }

        private async Task<List<string>> GetProductCodesForSearchAsync(string? productSearch)
        {
            if (string.IsNullOrWhiteSpace(productSearch))
                return new List<string>();

            var searchText = productSearch.Trim();
            return (await _context
                    .Db.Queryable<Product>()
                    .Where(p =>
                        (p.ItemNumber != null && p.ItemNumber.Contains(searchText))
                        || (p.Barcode != null && p.Barcode.Contains(searchText))
                    )
                    .Select(p => p.ProductCode ?? string.Empty)
                    .ToListAsync())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<Dictionary<string, string>> GetChinaSupplierProductMapAsync(
            IEnumerable<string>? chinaSupplierCodes = null
        )
        {
            var supplierCodes = NormalizeCodes(chinaSupplierCodes);
            var query = _posmContext
                .Db.Queryable<PosmProductSupplierMapping>()
                .Where(m =>
                    !m.IsDeleted
                    && m.LocalSupplierCode == "200"
                    && m.ChinaSupplierCode != null
                    && m.ChinaSupplierCode != ""
                );

            if (supplierCodes.Any())
            {
                query = query.Where(m =>
                    m.ChinaSupplierCode != null && supplierCodes.Contains(m.ChinaSupplierCode)
                );
            }

            var rows = await query
                .Select(m => new
                {
                    m.ProductCode,
                    ChinaSupplierCode = m.ChinaSupplierCode ?? string.Empty,
                })
                .ToListAsync();

            return rows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.ProductCode)
                    && !string.IsNullOrWhiteSpace(row.ChinaSupplierCode)
                )
                .GroupBy(row => row.ProductCode)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().ChinaSupplierCode,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private async Task<HashSet<string>> GetChinaSupplierCodeSetAsync(
            IEnumerable<string>? seedCodes = null
        )
        {
            var codes = NormalizeCodes(seedCodes).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var suppliers = await _context
                .Db.Queryable<ChinaSupplier>()
                .Where(s => !s.IsDeleted && s.SupplierCode != null && s.SupplierCode != "")
                .Select(s => s.SupplierCode ?? string.Empty)
                .ToListAsync();

            foreach (var supplierCode in suppliers)
            {
                if (!string.IsNullOrWhiteSpace(supplierCode))
                    codes.Add(supplierCode.Trim());
            }

            return codes;
        }

        private static string? ResolveChinaSupplierCodeFromStatistic(
            string? statisticSupplierCode,
            string? productCode,
            Dictionary<string, string> chinaProductMap,
            HashSet<string> chinaSupplierCodes
        )
        {
            var supplierCode = statisticSupplierCode?.Trim() ?? string.Empty;
            if (string.Equals(supplierCode, "200", StringComparison.OrdinalIgnoreCase))
            {
                // 旧日统计把中国商品写成本地供应商 200，需要通过 POSM 商品映射还原中国供应商。
                return !string.IsNullOrWhiteSpace(productCode)
                    && chinaProductMap.TryGetValue(productCode, out var mappedSupplierCode)
                    ? mappedSupplierCode
                    : null;
            }

            // 新日统计直接把中国供应商编码写入 SupplierCode。
            return chinaSupplierCodes.Contains(supplierCode) ? supplierCode : null;
        }

        private static bool IsChinaLocalSupplierCode(string? supplierCode)
        {
            return string.Equals(
                supplierCode?.Trim(),
                CHINA_LOCAL_SUPPLIER_CODE,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string ResolveAustralianSupplierCode(
            string? statisticSupplierCode,
            HashSet<string> chinaSupplierCodes
        )
        {
            var supplierCode = statisticSupplierCode?.Trim() ?? string.Empty;
            if (
                IsChinaLocalSupplierCode(supplierCode)
                || chinaSupplierCodes.Contains(supplierCode)
            )
            {
                return CHINA_LOCAL_SUPPLIER_CODE;
            }

            return supplierCode;
        }

        private static List<SupplierBranchAggregateRow> ResolveAustralianSupplierBranchRows(
            IEnumerable<SupplierBranchAggregateRow> rows,
            HashSet<string> chinaSupplierCodes,
            bool preserveDate = false
        )
        {
            var resolvedRows = rows.Select(row => new SupplierBranchAggregateRow
            {
                Date = row.Date?.Date,
                SupplierCode = ResolveAustralianSupplierCode(row.SupplierCode, chinaSupplierCodes),
                BranchCode = row.BranchCode,
                TotalAmount = row.TotalAmount,
                TotalQuantity = row.TotalQuantity,
                OrderCount = row.OrderCount,
            });

            return preserveDate
                ? AggregateSupplierDateBranchRows(resolvedRows)
                : AggregateSupplierBranchRows(resolvedRows);
        }

        private static ISugarQueryable<ProductStoreDailySalesStatistic> ApplyAustralianSupplierStatisticFilter(
            ISugarQueryable<ProductStoreDailySalesStatistic> query,
            IEnumerable<string>? supplierCodes,
            HashSet<string>? chinaSupplierCodes = null
        )
        {
            var codes = NormalizeCodes(supplierCodes);
            if (!codes.Any())
                return query;

            if (!codes.Any(IsChinaLocalSupplierCode))
                return query.Where(s => codes.Contains(s.SupplierCode));

            var localSupplierCodes = codes
                .Where(code => !IsChinaLocalSupplierCode(code))
                .ToList();
            var supplierFilterCodes = localSupplierCodes
                .Concat(new[] { CHINA_LOCAL_SUPPLIER_CODE })
                .Concat(chinaSupplierCodes ?? Enumerable.Empty<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 200 汇总行兼容新旧日统计：旧统计写 200，新统计直写中国供应商编码。
            return query.Where(s => supplierFilterCodes.Contains(s.SupplierCode));
        }

        private static IEnumerable<SupplierBranchAggregateRow> MapChinaSupplierRowsToLocal200(
            IEnumerable<SupplierBranchAggregateRow> rows
        )
        {
            return rows.Select(row => new SupplierBranchAggregateRow
            {
                Date = row.Date?.Date,
                SupplierCode = CHINA_LOCAL_SUPPLIER_CODE,
                BranchCode = row.BranchCode,
                TotalAmount = row.TotalAmount,
                TotalQuantity = row.TotalQuantity,
                OrderCount = row.OrderCount,
            });
        }

        private static string NormalizeCoverageBranchKey(string? branchCode)
        {
            return branchCode?.Trim() ?? string.Empty;
        }

        private static string BuildDateBranchCoverageKey(DateTime date, string? branchCode)
        {
            return $"{date:yyyyMMdd}|{NormalizeCoverageBranchKey(branchCode)}";
        }

        private static List<SupplierBranchAggregateRow> FilterUncoveredChinaLocalFallbackRows(
            IEnumerable<SupplierBranchAggregateRow> rows,
            IEnumerable<SupplierBranchAggregateRow> chinaFallbackRows
        )
        {
            var chinaLocalRows = rows
                .Where(row => IsChinaLocalSupplierCode(row.SupplierCode))
                .ToList();
            if (!chinaLocalRows.Any())
                return chinaFallbackRows.ToList();

            var coversAllFallback = chinaLocalRows.Any(row =>
                !row.Date.HasValue && string.IsNullOrWhiteSpace(row.BranchCode)
            );
            if (coversAllFallback)
                return new List<SupplierBranchAggregateRow>();

            var coveredBranches = chinaLocalRows
                .Where(row => !row.Date.HasValue && !string.IsNullOrWhiteSpace(row.BranchCode))
                .Select(row => NormalizeCoverageBranchKey(row.BranchCode))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var coveredDateBranches = chinaLocalRows
                .Where(row => row.Date.HasValue)
                .Select(row => BuildDateBranchCoverageKey(row.Date!.Value.Date, row.BranchCode))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return chinaFallbackRows
                .Where(row =>
                    !coveredBranches.Contains(NormalizeCoverageBranchKey(row.BranchCode))
                    && row.Date.HasValue
                    && !coveredDateBranches.Contains(
                        BuildDateBranchCoverageKey(row.Date.Value.Date, row.BranchCode)
                    )
                )
                .ToList();
        }

        private async Task<List<SupplierBranchAggregateRow>> AddChinaLocalSupplierFallbackRowsIfMissingAsync(
            List<SupplierBranchAggregateRow> rows,
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            bool includeChinaLocalSupplier,
            HashSet<string> chinaSupplierCodes
        )
        {
            if (includeChinaLocalSupplier)
            {
                // 中国拆分表和澳洲 200 可能覆盖同一批销售；按日期+分店只补缺片段，避免部分统计时漏数或重复。
                var chinaFallbackRows = await QueryChinaSupplierStoreDateBranchAggregateRowsAsync(
                    startDate,
                    endDate,
                    branchCodes
                );
                var fallbackRows = FilterUncoveredChinaLocalFallbackRows(rows, chinaFallbackRows);
                rows.AddRange(MapChinaSupplierRowsToLocal200(fallbackRows));
            }

            return ResolveAustralianSupplierBranchRows(rows, chinaSupplierCodes);
        }

        private static ISugarQueryable<ProductStoreDailySalesStatistic> ApplyChinaSupplierStatisticFilter(
            ISugarQueryable<ProductStoreDailySalesStatistic> query,
            HashSet<string> targetChinaSupplierCodes,
            Dictionary<string, string> chinaProductMap,
            bool limitLegacy200Products
        )
        {
            var legacyProductCodes = limitLegacy200Products
                ? chinaProductMap.Keys
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            if (
                legacyProductCodes.Any()
                && legacyProductCodes.Count <= LEGACY_CHINA_SUPPLIER_PRODUCT_FILTER_LIMIT
            )
            {
                // 指定中国供应商时，旧 200 统计只查该供应商映射商品，避免把当天所有 200 商品拉回内存。
                return query.Where(s =>
                    targetChinaSupplierCodes.Contains(s.SupplierCode)
                    || (s.SupplierCode == "200" && legacyProductCodes.Contains(s.ProductCode))
                );
            }

            if (limitLegacy200Products && !legacyProductCodes.Any())
            {
                // 没有 POSM 商品映射时，200 行无法还原成目标中国供应商，不参与主查询。
                return query.Where(s => targetChinaSupplierCodes.Contains(s.SupplierCode));
            }

            return query.Where(s =>
                s.SupplierCode == "200" || targetChinaSupplierCodes.Contains(s.SupplierCode)
            );
        }

        private async Task<bool> HasChinaProductStatisticRowsAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            HashSet<string> targetChinaSupplierCodes
        )
        {
            var query = await BuildProductReportStatisticQueryAsync(startDate, endDate, branchCodes);
            query = query.Where(s =>
                s.SupplierCode == "200" || targetChinaSupplierCodes.Contains(s.SupplierCode)
            );

            // 只在快路径查不到商品统计时执行，用于区分“统计未生成”和“POSM 映射缺失”。
            return await query.AnyAsync();
        }

        private async Task<List<ProductReportProductAggregateRow>> QueryProductReportProductAggregatesAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            List<string>? localSupplierCodes,
            List<string> normalizedChinaSupplierCodes,
            Dictionary<string, string> chinaProductMap,
            string? productSearch
        )
        {
            var normalizedProductSearch = productSearch?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedProductSearch))
            {
                normalizedProductSearch = null;
            }

            var searchProductCodes = normalizedProductSearch == null
                ? new List<string>()
                : await GetProductCodesForSearchAsync(normalizedProductSearch);
            var normalizedLocalSupplierCodes = NormalizeCodes(localSupplierCodes);
            if (
                !normalizedChinaSupplierCodes.Any()
                && normalizedLocalSupplierCodes.Any(IsChinaLocalSupplierCode)
            )
            {
                var query = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes: null,
                    chinaProductCodes: null,
                    normalizedProductSearch,
                    chinaSupplierCodes: null
                );
                query = ApplyAustralianSupplierStatisticFilter(
                    query,
                    normalizedLocalSupplierCodes,
                    await GetChinaSupplierCodeSetAsync()
                );
                return await QueryProductReportProductAggregatesAsync(query);
            }
            var targetChinaSupplierCodes = normalizedChinaSupplierCodes.ToHashSet(
                StringComparer.OrdinalIgnoreCase
            );
            if (!targetChinaSupplierCodes.Any())
            {
                var query = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    chinaProductCodes: null,
                    normalizedProductSearch,
                    chinaSupplierCodes: null
                );
                return await QueryProductReportProductAggregatesAsync(query);
            }

            var legacyProductCodes = chinaProductMap.Keys
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedProductSearch != null)
            {
                return await QueryChinaSupplierProductAggregatesWithSearchAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    targetChinaSupplierCodes,
                    legacyProductCodes,
                    searchProductCodes,
                    normalizedProductSearch
                );
            }

            if (legacyProductCodes.Count <= LEGACY_CHINA_SUPPLIER_PRODUCT_FILTER_LIMIT)
            {
                var query = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    chinaProductCodes: null,
                    normalizedProductSearch,
                    chinaSupplierCodes: null
                );
                query = ApplyChinaSupplierStatisticFilter(
                    query,
                    targetChinaSupplierCodes,
                    chinaProductMap,
                    limitLegacy200Products: true
                );
                return await QueryProductReportProductAggregatesAsync(query);
            }

            var rows = new List<ProductReportProductAggregateRow>();
            var directQuery = await BuildProductReportStatisticQueryAsync(
                startDate,
                endDate,
                branchCodes,
                localSupplierCodes,
                chinaProductCodes: null,
                normalizedProductSearch,
                chinaSupplierCodes: null
            );
            directQuery = directQuery.Where(s => targetChinaSupplierCodes.Contains(s.SupplierCode));
            rows.AddRange(await QueryProductReportProductAggregatesAsync(directQuery));

            foreach (var chunk in legacyProductCodes.Chunk(LEGACY_CHINA_SUPPLIER_PRODUCT_FILTER_LIMIT))
            {
                var chunkProductCodes = chunk.ToList();
                if (searchProductCodes.Any())
                {
                    // 大映射 + 宽泛搜索时先在内存求交集，避免同一条 SQL 同时携带两组大 ProductCode IN 参数。
                    chunkProductCodes = chunkProductCodes
                        .Intersect(searchProductCodes, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (!chunkProductCodes.Any())
                        continue;
                }

                var legacyQuery = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    chinaProductCodes: null,
                    searchProductCodes.Any() ? null : normalizedProductSearch,
                    chinaSupplierCodes: null
                );
                // 大供应商映射超过 SQL Server 参数预算时，旧 200 统计按商品码分批查询再合并。
                legacyQuery = legacyQuery.Where(s =>
                    s.SupplierCode == "200" && chunkProductCodes.Contains(s.ProductCode)
                );
                rows.AddRange(await QueryProductReportProductAggregatesAsync(legacyQuery));
            }

            return MergeProductReportProductAggregates(rows);
        }

        private async Task<List<ProductReportProductAggregateRow>> QueryChinaSupplierProductAggregatesWithSearchAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            List<string>? localSupplierCodes,
            HashSet<string> targetChinaSupplierCodes,
            List<string> legacyProductCodes,
            List<string> searchProductCodes,
            string productSearch
        )
        {
            var rows = new List<ProductReportProductAggregateRow>();
            var searchProductCodeSet = searchProductCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var directProductCodes = new HashSet<string>(searchProductCodeSet, StringComparer.OrdinalIgnoreCase);
            var directBarcodeQuery = await BuildProductReportStatisticQueryAsync(
                startDate,
                endDate,
                branchCodes,
                localSupplierCodes,
                chinaProductCodes: null,
                productSearch: null,
                chinaSupplierCodes: null
            );
            directBarcodeQuery = directBarcodeQuery.Where(s =>
                targetChinaSupplierCodes.Contains(s.SupplierCode)
                && s.Barcode != null
                && s.Barcode.Contains(productSearch)
            );
            foreach (var productCode in await directBarcodeQuery.Select(s => s.ProductCode).ToListAsync())
            {
                if (!string.IsNullOrWhiteSpace(productCode))
                    directProductCodes.Add(productCode.Trim());
            }

            foreach (var chunk in directProductCodes.Chunk(LEGACY_CHINA_SUPPLIER_PRODUCT_FILTER_LIMIT))
            {
                var productCodes = chunk.ToList();
                var directQuery = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    chinaProductCodes: null,
                    productSearch: null,
                    chinaSupplierCodes: null
                );
                directQuery = directQuery.Where(s =>
                    targetChinaSupplierCodes.Contains(s.SupplierCode)
                    && productCodes.Contains(s.ProductCode)
                );
                rows.AddRange(await QueryProductReportProductAggregatesAsync(directQuery));
            }

            foreach (var chunk in legacyProductCodes.Chunk(LEGACY_CHINA_SUPPLIER_PRODUCT_FILTER_LIMIT))
            {
                var mappedProductCodes = chunk.ToList();
                var targetProductCodes = mappedProductCodes
                    .Where(searchProductCodeSet.Contains)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var legacyBarcodeQuery = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    chinaProductCodes: null,
                    productSearch: null,
                    chinaSupplierCodes: null
                );
                legacyBarcodeQuery = legacyBarcodeQuery.Where(s =>
                    s.SupplierCode == "200"
                    && mappedProductCodes.Contains(s.ProductCode)
                    && s.Barcode != null
                    && s.Barcode.Contains(productSearch)
                );
                foreach (var productCode in await legacyBarcodeQuery.Select(s => s.ProductCode).ToListAsync())
                {
                    if (!string.IsNullOrWhiteSpace(productCode))
                        targetProductCodes.Add(productCode.Trim());
                }

                if (!targetProductCodes.Any())
                    continue;

                var productCodes = targetProductCodes.ToList();
                var legacyQuery = await BuildProductReportStatisticQueryAsync(
                    startDate,
                    endDate,
                    branchCodes,
                    localSupplierCodes,
                    chinaProductCodes: null,
                    productSearch: null,
                    chinaSupplierCodes: null
                );
                // 搜索命中的商品码先与供应商映射求交集，再分批查询，避免搜索和映射两组大 IN 参数叠加。
                legacyQuery = legacyQuery.Where(s =>
                    s.SupplierCode == "200" && productCodes.Contains(s.ProductCode)
                );
                rows.AddRange(await QueryProductReportProductAggregatesAsync(legacyQuery));
            }

            return MergeProductReportProductAggregates(rows);
        }

        private static async Task<List<ProductReportProductAggregateRow>> QueryProductReportProductAggregatesAsync(
            ISugarQueryable<ProductStoreDailySalesStatistic> query
        )
        {
            return await query
                .GroupBy(s => s.ProductCode)
                .Select(s => new ProductReportProductAggregateRow
                {
                    ProductCode = s.ProductCode,
                    ProductName = SqlFunc.AggregateMax(s.ProductName),
                    Quantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    SalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                })
                .MergeTable()
                .ToListAsync();
        }

        private static List<ProductReportProductAggregateRow> MergeProductReportProductAggregates(
            IEnumerable<ProductReportProductAggregateRow> rows
        )
        {
            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
                .GroupBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ProductReportProductAggregateRow
                {
                    ProductCode = group.Key,
                    ProductName = group
                        .Select(row => row.ProductName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                    Quantity = group.Sum(row => row.Quantity),
                    SalesAmount = group.Sum(row => row.SalesAmount),
                    OrderCount = group.Sum(row => row.OrderCount),
                })
                .ToList();
        }

        private async Task<List<SupplierBranchAggregateRow>> QueryAustralianSupplierStoreAggregateRowsAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes = null,
            IEnumerable<string>? supplierCodes = null
        )
        {
            var normalizedBranchCodes = NormalizeCodes(branchCodes);
            var normalizedSupplierCodes = NormalizeCodes(supplierCodes);

            var query = _context.Db.Queryable<AustralianSupplierStoreSalesDetail>()
                .Where(s => s.Date >= startDate && s.Date <= endDate);

            if (normalizedBranchCodes.Any())
            {
                query = query.Where(s => normalizedBranchCodes.Contains(s.BranchCode));
            }

            if (normalizedSupplierCodes.Any())
            {
                query = query.Where(s => normalizedSupplierCodes.Contains(s.SupplierCode));
            }

            return await query
                .GroupBy(s => new { s.SupplierCode, s.BranchCode })
                .Select(s => new SupplierBranchAggregateRow
                {
                    SupplierCode = s.SupplierCode,
                    BranchCode = s.BranchCode,
                    TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                })
                .ToListAsync();
        }

        private async Task<List<SupplierBranchAggregateRow>> QueryChinaSupplierStoreAggregateRowsAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes = null,
            IEnumerable<string>? supplierCodes = null
        )
        {
            var normalizedBranchCodes = NormalizeCodes(branchCodes);
            var normalizedSupplierCodes = NormalizeCodes(supplierCodes);

            var query = _context.Db.Queryable<ChinaSupplierStoreSalesDetail>()
                .Where(s => s.Date >= startDate && s.Date <= endDate);

            if (normalizedBranchCodes.Any())
            {
                query = query.Where(s => normalizedBranchCodes.Contains(s.BranchCode));
            }

            if (normalizedSupplierCodes.Any())
            {
                query = query.Where(s => normalizedSupplierCodes.Contains(s.SupplierCode));
            }

            return await query
                .GroupBy(s => new { s.SupplierCode, s.BranchCode })
                .Select(s => new SupplierBranchAggregateRow
                {
                    SupplierCode = s.SupplierCode,
                    BranchCode = s.BranchCode,
                    TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                })
                .ToListAsync();
        }

        private async Task<List<SupplierBranchAggregateRow>> QueryChinaSupplierStoreDateBranchAggregateRowsAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes = null,
            IEnumerable<string>? supplierCodes = null
        )
        {
            var normalizedBranchCodes = NormalizeCodes(branchCodes);
            var normalizedSupplierCodes = NormalizeCodes(supplierCodes);

            var query = _context.Db.Queryable<ChinaSupplierStoreSalesDetail>()
                .Where(s => s.Date >= startDate && s.Date <= endDate);

            if (normalizedBranchCodes.Any())
            {
                query = query.Where(s => normalizedBranchCodes.Contains(s.BranchCode));
            }

            if (normalizedSupplierCodes.Any())
            {
                query = query.Where(s => normalizedSupplierCodes.Contains(s.SupplierCode));
            }

            return await query
                .GroupBy(s => new { s.Date, s.SupplierCode, s.BranchCode })
                .Select(s => new SupplierBranchAggregateRow
                {
                    Date = s.Date,
                    SupplierCode = s.SupplierCode,
                    BranchCode = s.BranchCode,
                    TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                })
                .ToListAsync();
        }

        private static List<SupplierBranchAggregateRow> AggregateSupplierBranchRows(
            IEnumerable<SupplierBranchAggregateRow> rows
        )
        {
            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.SupplierCode))
                .GroupBy(row => new
                {
                    SupplierCode = row.SupplierCode.Trim(),
                    BranchCode = row.BranchCode?.Trim() ?? string.Empty,
                })
                .Select(group => new SupplierBranchAggregateRow
                {
                    SupplierCode = group.Key.SupplierCode,
                    BranchCode = group.Key.BranchCode,
                    TotalAmount = group.Sum(row => row.TotalAmount),
                    TotalQuantity = group.Sum(row => row.TotalQuantity),
                    OrderCount = group.Sum(row => row.OrderCount ?? 0),
                })
                .ToList();
        }

        private static List<SupplierBranchAggregateRow> AggregateSupplierDateBranchRows(
            IEnumerable<SupplierBranchAggregateRow> rows
        )
        {
            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.SupplierCode))
                .GroupBy(row => new
                {
                    Date = row.Date?.Date,
                    SupplierCode = row.SupplierCode.Trim(),
                    BranchCode = row.BranchCode?.Trim() ?? string.Empty,
                })
                .Select(group => new SupplierBranchAggregateRow
                {
                    Date = group.Key.Date,
                    SupplierCode = group.Key.SupplierCode,
                    BranchCode = group.Key.BranchCode,
                    TotalAmount = group.Sum(row => row.TotalAmount),
                    TotalQuantity = group.Sum(row => row.TotalQuantity),
                    OrderCount = group.Sum(row => row.OrderCount ?? 0),
                })
                .ToList();
        }

        private static List<SupplierBranchAggregateRow> ResolveChinaSupplierBranchRows(
            IEnumerable<ProductSupplierBranchAggregateRow> rows,
            Dictionary<string, string> chinaProductMap,
            HashSet<string> targetChinaSupplierCodes
        )
        {
            var resolvedRows = rows
                .Select(row => new SupplierBranchAggregateRow
                {
                    SupplierCode =
                        ResolveChinaSupplierCodeFromStatistic(
                            row.SupplierCode,
                            row.ProductCode,
                            chinaProductMap,
                            targetChinaSupplierCodes
                        ) ?? string.Empty,
                    BranchCode = row.BranchCode,
                    TotalAmount = row.TotalAmount,
                    TotalQuantity = row.TotalQuantity,
                    OrderCount = row.OrderCount,
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.SupplierCode));

            return AggregateSupplierBranchRows(resolvedRows);
        }

        private static List<SupplierRankAggregateRow> BuildSupplierRankAggregates(
            IEnumerable<SupplierBranchAggregateRow> rows
        )
        {
            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.SupplierCode))
                .GroupBy(row => row.SupplierCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new SupplierRankAggregateRow
                {
                    SupplierCode = group.Key,
                    TotalAmount = group.Sum(row => row.TotalAmount),
                    TotalQuantity = group.Sum(row => row.TotalQuantity),
                    OrderCount = group.Sum(row => row.OrderCount ?? 0),
                    StoreCount = group
                        .Select(row => row.BranchCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                })
                .ToList();
        }

        private static Dictionary<string, (decimal TotalAmount, int OrderCount)> BuildSupplierCompareDict(
            IEnumerable<SupplierBranchAggregateRow> rows,
            IEnumerable<string>? allowedSupplierCodes = null
        )
        {
            var allowedSet = NormalizeCodes(allowedSupplierCodes).ToHashSet(
                StringComparer.OrdinalIgnoreCase
            );

            return rows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.SupplierCode)
                    && (!allowedSet.Any() || allowedSet.Contains(row.SupplierCode))
                )
                .GroupBy(row => row.SupplierCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (
                        group.Sum(row => row.TotalAmount),
                        group.Sum(row => row.OrderCount ?? 0)
                    ),
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private static Dictionary<string, (decimal TotalAmount, int OrderCount)> BuildSupplierBranchCompareDict(
            IEnumerable<SupplierBranchAggregateRow> rows
        )
        {
            return AggregateSupplierBranchRows(rows)
                .ToDictionary(
                    row => $"{row.BranchCode}|{row.SupplierCode}",
                    row => (row.TotalAmount, row.OrderCount ?? 0),
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private async Task<ISugarQueryable<ProductStoreDailySalesStatistic>> BuildProductReportStatisticQueryAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes = null,
            List<string>? localSupplierCodes = null,
            List<string>? chinaProductCodes = null,
            string? productSearch = null,
            List<string>? chinaSupplierCodes = null
        )
        {
            var query = _context.Db.Queryable<ProductStoreDailySalesStatistic>()
                .Where(s => s.Date >= startDate && s.Date <= endDate);

            var normalizedBranchCodes = NormalizeCodes(branchCodes);
            if (normalizedBranchCodes.Any())
            {
                query = query.Where(s => normalizedBranchCodes.Contains(s.BranchCode));
            }

            var normalizedLocalSupplierCodes = NormalizeCodes(localSupplierCodes);
            if (normalizedLocalSupplierCodes.Any())
            {
                query = query.Where(s => normalizedLocalSupplierCodes.Contains(s.SupplierCode));
            }

            var normalizedChinaSupplierCodes = NormalizeCodes(chinaSupplierCodes);
            var normalizedChinaProductCodes = NormalizeCodes(chinaProductCodes);
            if (normalizedChinaSupplierCodes.Any() && normalizedChinaProductCodes.Any())
            {
                // 中国供应商商品明细兼容新旧日统计编码，避免排行和下方明细口径分裂。
                query = query.Where(s =>
                    normalizedChinaSupplierCodes.Contains(s.SupplierCode)
                    || (s.SupplierCode == "200" && normalizedChinaProductCodes.Contains(s.ProductCode))
                );
            }
            else if (normalizedChinaSupplierCodes.Any())
            {
                query = query.Where(s => normalizedChinaSupplierCodes.Contains(s.SupplierCode));
            }
            else if (normalizedChinaProductCodes.Any())
            {
                query = query.Where(s =>
                    s.SupplierCode == "200" && normalizedChinaProductCodes.Contains(s.ProductCode)
                );
            }

            var normalizedProductSearch = productSearch?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedProductSearch))
            {
                var searchText = normalizedProductSearch!;
                var searchProductCodes = await GetProductCodesForSearchAsync(searchText);
                query = searchProductCodes.Any()
                    ? query.Where(s =>
                        searchProductCodes.Contains(s.ProductCode)
                        || (s.Barcode != null && s.Barcode.Contains(searchText))
                    )
                    : query.Where(s => s.Barcode != null && s.Barcode.Contains(searchText));
            }

            return query;
        }

        private async Task<Dictionary<string, string>> GetLocalSupplierNameMapAsync(
            IEnumerable<string> supplierCodes
        )
        {
            var codes = NormalizeCodes(supplierCodes);
            if (!codes.Any())
                return new Dictionary<string, string>();

            var suppliers = await _context
                .Db.Queryable<HBLocalSupplier>()
                .Where(s => !s.IsDeleted && codes.Contains(s.LocalSupplierCode))
                .Select(s => new { s.LocalSupplierCode, s.Name })
                .ToListAsync();

            return suppliers
                .GroupBy(s => s.LocalSupplierCode)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private async Task<Dictionary<string, string>> GetAustralianSupplierNameMapAsync(
            IEnumerable<string> supplierCodes
        )
        {
            var codes = NormalizeCodes(supplierCodes);
            var nameMap = await GetLocalSupplierNameMapAsync(codes);
            if (
                codes.Any(IsChinaLocalSupplierCode)
                && (
                    !nameMap.TryGetValue(CHINA_LOCAL_SUPPLIER_CODE, out var supplierName)
                    || string.IsNullOrWhiteSpace(supplierName)
                )
            )
            {
                // 200 是中国货在澳洲供应商报表中的固定汇总行，资料缺失时仍显示业务名称。
                nameMap[CHINA_LOCAL_SUPPLIER_CODE] = CHINA_LOCAL_SUPPLIER_FALLBACK_NAME;
            }

            return nameMap;
        }

        private async Task<Dictionary<string, string>> GetChinaSupplierNameMapAsync(
            IEnumerable<string> supplierCodes
        )
        {
            var codes = NormalizeCodes(supplierCodes);
            if (!codes.Any())
                return new Dictionary<string, string>();

            var suppliers = await _context
                .Db.Queryable<ChinaSupplier>()
                .Where(s => !s.IsDeleted && s.SupplierCode != null && codes.Contains(s.SupplierCode))
                .Select(s => new
                {
                    SupplierCode = s.SupplierCode ?? string.Empty,
                    SupplierName = s.SupplierName ?? s.SupplierCode ?? string.Empty,
                })
                .ToListAsync();

            return suppliers
                .Where(s => !string.IsNullOrWhiteSpace(s.SupplierCode))
                .GroupBy(s => s.SupplierCode)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().SupplierName,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private async Task<Dictionary<string, string>> GetStoreNameMapAsync(
            HashSet<string> branchCodes
        )
        {
            if (!branchCodes.Any())
                return new Dictionary<string, string>();

            var stores = await _context
                .Db.Queryable<Store>()
                .Where(s => branchCodes.Contains(s.StoreCode))
                .Select(s => new { s.StoreCode, s.StoreName })
                .ToListAsync();

            return stores.ToDictionary(
                s => s.StoreCode ?? string.Empty,
                s => s.StoreName ?? string.Empty
            );
        }

        /// <summary>
        /// 获取 Best Sellers 商品列表（销量排名）
        /// 用于前端 StoreFront 的 Best Sellers 页面
        /// </summary>
        public async Task<BestSellerResponseDto> GetBestSellersAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int pageIndex = 1,
            int pageSize = 50
        )
        {
            try
            {
                _logger.LogInformation(
                    "[BestSellers] Getting best sellers: DateRange={StartDate}~{EndDate}, BranchCodes={BranchCodes}, Page={Page}, PageSize={PageSize}",
                    dateRange.StartDate, dateRange.EndDate,
                    branchCodes != null ? string.Join(",", branchCodes) : "All",
                    pageIndex, pageSize
                );

                if (branchCodes != null && !branchCodes.Any())
                {
                    return new BestSellerResponseDto
                    {
                        Products = new List<BestSellerProductDto>(),
                        Total = 0,
                        PageIndex = pageIndex,
                        PageSize = pageSize,
                    };
                }

                var cacheKey = branchCodes == null
                    ? SalesDashboardCacheKeys.BestSellers(dateRange, pageIndex, pageSize)
                    : null;
                if (
                    cacheKey != null
                    && _cache.TryGetValue<BestSellerResponseDto>(cacheKey, out var cachedResult)
                    && cachedResult != null
                )
                {
                    _logger.LogInformation("从缓存获取热销商品全平台排名: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                try
                {
                    var statisticResult = await GetBestSellersFromStatisticsAsync(
                        dateRange,
                        branchCodes,
                        pageIndex,
                        pageSize
                    );
                    if (ShouldCacheBestSellerResult(statisticResult))
                    {
                        CacheBestSellerResult(cacheKey, statisticResult);
                    }
                    return statisticResult;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[BestSellers] Statistic query failed, POSM fallback disabled");
                    return CreateEmptyBestSellerResponse(
                        pageIndex,
                        pageSize,
                        SalesStatisticRefreshStatus.Failed,
                        $"商品统计查询失败，请先修复或重新生成统计表: {ex.Message}"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BestSellers] GetBestSellersAsync failed");
                throw;
            }
        }

        private static bool ShouldCacheBestSellerResult(BestSellerResponseDto result)
        {
            // 只缓存已生成且有数据的统计结果，避免短暂空表把热销页空结果固定 30 分钟。
            return result.StatisticStatus == SalesStatisticRefreshStatus.Fresh
                && result.Total > 0
                && result.Products.Any();
        }

        private static BestSellerResponseDto CreateEmptyBestSellerResponse(
            int pageIndex,
            int pageSize,
            string? statisticStatus,
            string? statisticMessage
        )
        {
            return new BestSellerResponseDto
            {
                Products = new List<BestSellerProductDto>(),
                Total = 0,
                PageIndex = pageIndex,
                PageSize = pageSize,
                StatisticStatus = statisticStatus,
                StatisticMessage = statisticMessage,
            };
        }

        private void CacheBestSellerResult(string? cacheKey, BestSellerResponseDto result)
        {
            if (cacheKey == null)
                return;

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(BEST_SELLERS_CACHE_DURATION)
                .SetSlidingExpiration(TimeSpan.FromMinutes(5));

            _cache.Set(cacheKey, result, cacheOptions);
            _logger.LogInformation(
                "热销商品全平台排名已缓存: {CacheKey}, 过期时间: {Expiration}",
                cacheKey,
                DateTime.Now.Add(BEST_SELLERS_CACHE_DURATION)
            );
        }

        private async Task<BestSellerResponseDto> GetBestSellersFromStatisticsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            int pageIndex,
            int pageSize
        )
        {
            var startDate = dateRange.StartDate.Date;
            var endDate = dateRange.EndDate.Date;
            var statisticStatus = await GetProductStatisticStatusAsync(startDate, endDate);
            if (statisticStatus.Status == SalesStatisticRefreshStatus.Failed)
            {
                _logger.LogWarning(
                    "[BestSellers] Product statistic status failed, POSM fallback disabled: {Message}",
                    statisticStatus.Message
                );
                return CreateEmptyBestSellerResponse(
                    pageIndex,
                    pageSize,
                    SalesStatisticRefreshStatus.Failed,
                    statisticStatus.Message ?? "商品统计状态失败，请先修复或重新生成统计表。"
                );
            }

            if (statisticStatus.Status != SalesStatisticRefreshStatus.Fresh)
            {
                // 非 Fresh 说明日期范围不完整或水位过期，不能把部分统计当完整排名展示。
                return CreateEmptyBestSellerResponse(
                    pageIndex,
                    pageSize,
                    statisticStatus.Status,
                    statisticStatus.Message ?? "商品统计未完整生成，请先生成商品统计。"
                );
            }

            var query = _context.Db.Queryable<ProductStoreDailySalesStatistic>()
                .Where(s =>
                    s.Date >= startDate
                    && s.Date <= endDate
                    && s.SupplierCode == "200"
                );

            if (branchCodes != null && branchCodes.Any())
            {
                query = query.Where(s => branchCodes.Contains(s.BranchCode));
            }

            // 数据库分页：热销页优先读取商品分店每日统计表，避免实时扫 POSM 明细。
            var groupedQuery = query
                .GroupBy(s => s.ProductCode)
                .Select(s => new BestSellerAggregateRow
                {
                    ProductCode = s.ProductCode,
                    TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    TotalSalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    TotalCost = SqlFunc.AggregateSum(s.TotalCost),
                    GrossProfit = SqlFunc.AggregateSum(s.GrossProfit),
                });

            var total = await groupedQuery.Clone().CountAsync();
            if (total <= 0)
            {
                return CreateEmptyBestSellerResponse(
                    pageIndex,
                    pageSize,
                    SalesStatisticRefreshStatus.Fresh,
                    null
                );
            }

            var pagedData = await query.Clone()
                .GroupBy(s => s.ProductCode)
                .OrderBy(s => SqlFunc.AggregateSum(s.TotalQuantity), OrderByType.Desc)
                .Select(s => new BestSellerAggregateRow
                {
                    ProductCode = s.ProductCode,
                    TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    TotalSalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    TotalCost = SqlFunc.AggregateSum(s.TotalCost),
                    GrossProfit = SqlFunc.AggregateSum(s.GrossProfit),
                })
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return await BuildBestSellerResponseAsync(
                pagedData,
                total,
                pageIndex,
                pageSize,
                statisticStatus.Status,
                statisticStatus.Message,
                productCodes => GetBranchSalesFromStatisticsAsync(startDate, endDate, branchCodes, productCodes),
                productCodes => GetBestSellerInfoFromStatisticsAsync(startDate, endDate, branchCodes, productCodes)
            );
        }

        private async Task<BestSellerResponseDto> BuildBestSellerResponseAsync(
            List<BestSellerAggregateRow> pagedData,
            int total,
            int pageIndex,
            int pageSize,
            string? statisticStatus,
            string? statisticMessage,
            Func<List<string>, Task<List<BestSellerBranchAggregateRow>>> loadBranchSales,
            Func<List<string>, Task<Dictionary<string, (string? Barcode, string? ProductName)>>> loadStatisticInfo
        )
        {
            if (!pagedData.Any())
            {
                return new BestSellerResponseDto
                {
                    Products = new List<BestSellerProductDto>(),
                    Total = 0,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    StatisticStatus = statisticStatus,
                    StatisticMessage = statisticMessage,
                };
            }

            var productCodes = pagedData
                .Select(x => x.ProductCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();
            var productDict = await GetBestSellerProductInfoMapAsync(productCodes);
            var statisticInfo = await loadStatisticInfo(productCodes);
            var branchSalesData = await loadBranchSales(productCodes);
            var branchCodesInPage = branchSalesData
                .Select(x => x.BranchCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();
            var storeNameMap = branchCodesInPage.Any()
                ? await GetStoreNameMapAsync(branchCodesInPage.ToHashSet())
                : new Dictionary<string, string>();
            var branchSalesMap = branchSalesData
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode) && !string.IsNullOrWhiteSpace(x.BranchCode))
                .GroupBy(x => x.ProductCode)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(x => x.Quantity)
                        .ThenBy(x => x.BranchCode)
                        .Select(x => new BestSellerBranchSaleDto
                        {
                            BranchCode = x.BranchCode,
                            BranchName = storeNameMap.TryGetValue(x.BranchCode, out var storeName) ? storeName : x.BranchCode,
                            Quantity = x.Quantity,
                            SalesAmount = x.SalesAmount,
                            TotalCost = x.TotalCost,
                            GrossProfit = x.GrossProfit,
                    GrossMarginRate = x.SalesAmount > 0m && x.GrossProfit.HasValue
                        ? x.GrossProfit.Value / x.SalesAmount
                        : null,
                    CostSource = x.CostSource,
                })
                        .ToList()
                );

            var products = pagedData.Select((item, index) =>
            {
                productDict.TryGetValue(item.ProductCode, out var productInfo);
                statisticInfo.TryGetValue(item.ProductCode, out var statInfo);
                var branchSales = branchSalesMap.TryGetValue(item.ProductCode, out var rows)
                    ? rows
                    : new List<BestSellerBranchSaleDto>();
                return new BestSellerProductDto
                {
                    ProductCode = item.ProductCode,
                    ItemNumber = productInfo?.ItemNumber,
                    Barcode = !string.IsNullOrWhiteSpace(productInfo?.Barcode)
                        ? productInfo.Barcode
                        : statInfo.Barcode,
                    ProductImage = productInfo?.ProductImage,
                    ProductName = !string.IsNullOrWhiteSpace(productInfo?.ProductName)
                        ? productInfo.ProductName
                        : statInfo.ProductName,
                    Quantity = item.TotalQuantity,
                    SalesAmount = item.TotalSalesAmount,
                    TotalCost = item.TotalCost,
                    GrossProfit = item.GrossProfit,
                    GrossMarginRate = item.TotalSalesAmount > 0m && item.GrossProfit.HasValue
                        ? item.GrossProfit.Value / item.TotalSalesAmount
                        : null,
                    CostSource = ResolveCostSource(branchSales),
                    Rank = (pageIndex - 1) * pageSize + index + 1,
                    IsActive = productInfo?.IsActive,
                    MinOrderQuantity = productInfo?.MinOrderQuantity,
                    BranchSalesCount = branchSales.Count,
                    BranchSales = branchSales,
                    StatisticStatus = statisticStatus,
                };
            }).ToList();

            return new BestSellerResponseDto
            {
                Products = products,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize,
                StatisticStatus = statisticStatus,
                StatisticMessage = statisticMessage,
            };
        }

        private async Task<Dictionary<string, ProductInfo>> GetBestSellerProductInfoMapAsync(List<string> productCodes)
        {
            if (!productCodes.Any())
                return new Dictionary<string, ProductInfo>();

            // 仓库库存表状态：热销展示的上下架和起订量必须以 WarehouseProduct 为准。
            var productsInfo = await _context.Db.Queryable<Product>()
                .LeftJoin<WarehouseProduct>((p, wp) => p.ProductCode == wp.ProductCode && wp.IsDeleted == false)
                .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode))
                .Where(p => p.IsDeleted == false)
                .Select((p, wp) => new ProductInfo
                {
                    ProductCode = p.ProductCode ?? string.Empty,
                    ItemNumber = p.ItemNumber,
                    Barcode = p.Barcode,
                    ProductImage = p.ProductImage,
                    ProductName = p.ProductName,
                    IsActive = wp.IsActive,
                    MinOrderQuantity = wp.MinOrderQuantity,
                })
                .ToListAsync();

            return productsInfo
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => x.ProductCode)
                .ToDictionary(x => x.Key, x => x.First());
        }

        private async Task<List<BestSellerBranchAggregateRow>> GetBranchSalesFromStatisticsAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            List<string> productCodes
        )
        {
            if (!productCodes.Any())
                return new List<BestSellerBranchAggregateRow>();

            var query = _context.Db.Queryable<ProductStoreDailySalesStatistic>()
                .Where(s =>
                    s.Date >= startDate
                    && s.Date <= endDate
                    && s.SupplierCode == "200"
                    && productCodes.Contains(s.ProductCode)
                );
            if (branchCodes != null && branchCodes.Any())
            {
                query = query.Where(s => branchCodes.Contains(s.BranchCode));
            }

            var rows = await query
                .GroupBy(s => new { s.ProductCode, s.BranchCode, s.CostSource })
                .Select(s => new BestSellerBranchAggregateRow
                {
                    ProductCode = s.ProductCode,
                    BranchCode = s.BranchCode,
                    Quantity = SqlFunc.AggregateSum(s.TotalQuantity),
                    SalesAmount = SqlFunc.AggregateSum(s.TotalAmount),
                    TotalCost = SqlFunc.AggregateSum(s.TotalCost),
                    GrossProfit = SqlFunc.AggregateSum(s.GrossProfit),
                    CostSource = s.CostSource,
                })
                .ToListAsync();

            // 分店销量先按成本来源下推聚合，再在内存合并少量 CostSource 分组以保留 Mixed 语义。
            return rows
                .GroupBy(s => new { s.ProductCode, s.BranchCode })
                .Select(group => new BestSellerBranchAggregateRow
                {
                    ProductCode = group.Key.ProductCode,
                    BranchCode = group.Key.BranchCode,
                    Quantity = group.Sum(x => x.Quantity),
                    SalesAmount = group.Sum(x => x.SalesAmount),
                    TotalCost = group.Any(x => x.TotalCost.HasValue) ? group.Sum(x => x.TotalCost ?? 0m) : null,
                    GrossProfit = group.Any(x => x.GrossProfit.HasValue) ? group.Sum(x => x.GrossProfit ?? 0m) : null,
                    CostSource = ResolveCostSource(group.Select(x => x.CostSource)),
                })
                .ToList();
        }

        private async Task<Dictionary<string, (string? Barcode, string? ProductName)>> GetBestSellerInfoFromStatisticsAsync(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            List<string> productCodes
        )
        {
            if (!productCodes.Any())
                return new Dictionary<string, (string? Barcode, string? ProductName)>();

            var query = _context.Db.Queryable<ProductStoreDailySalesStatistic>()
                .Where(s =>
                    s.Date >= startDate
                    && s.Date <= endDate
                    && s.SupplierCode == "200"
                    && productCodes.Contains(s.ProductCode)
                );
            if (branchCodes != null && branchCodes.Any())
            {
                query = query.Where(s => branchCodes.Contains(s.BranchCode));
            }

            var rows = await query
                .Select(s => new { s.ProductCode, s.Barcode, s.ProductName })
                .ToListAsync();

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => x.ProductCode)
                .ToDictionary(
                    x => x.Key,
                    x => (
                        x.Select(row => row.Barcode).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                        x.Select(row => row.ProductName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    )
                );
        }

        private async Task<(string Status, string? Message)> GetProductStatisticStatusAsync(
            DateTime startDate,
            DateTime endDate
        )
        {
            var states = await _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s =>
                    s.StatisticType == SalesStatisticType.ProductStoreDaily
                    && s.Date >= startDate
                    && s.Date <= endDate
                )
                .ToListAsync();

            if (!states.Any())
                return (SalesStatisticRefreshStatus.Pending, "商品统计尚未回填完整。");

            if (states.Any(s => s.Status == SalesStatisticRefreshStatus.Failed))
                return (SalesStatisticRefreshStatus.Failed, states.FirstOrDefault(s => s.Status == SalesStatisticRefreshStatus.Failed)?.ErrorMessage);

            if (states.Any(s => s.Status == SalesStatisticRefreshStatus.Queued || s.Status == SalesStatisticRefreshStatus.Running))
                return (SalesStatisticRefreshStatus.Pending, "商品统计正在重算中。");

            if (states.Any(s => s.Status == SalesStatisticRefreshStatus.Stale))
                return (SalesStatisticRefreshStatus.Stale, "商品统计正在等待延迟上传数据补算。");

            if (states.Any(s => s.Status == SalesStatisticRefreshStatus.Pending))
                return (SalesStatisticRefreshStatus.Pending, "商品统计正在生成中。");

            var expectedDays = (int)(endDate - startDate).TotalDays + 1;
            if (states.Select(s => s.Date.Date).Distinct().Count() < expectedDays)
                return (SalesStatisticRefreshStatus.Pending, "日期范围内仍有商品统计未生成。");

            // Best Sellers 请求链路只读统计状态表；源数据水位由统计任务写入状态，避免页面触发 POSM 重查询。

            return (SalesStatisticRefreshStatus.Fresh, null);
        }

        private static string? ResolveCostSource(List<BestSellerBranchSaleDto> branchSales)
        {
            if (!branchSales.Any())
                return null;

            return ResolveCostSource(branchSales.Select(x => x.CostSource));
        }

        private static string? ResolveCostSource(IEnumerable<string?> costSources)
        {
            var sources = costSources
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Select(source => source!)
                .Distinct()
                .ToList();
            if (!sources.Any())
                return null;

            return sources.Count == 1 ? sources[0] : "Mixed";
        }

    }
}
