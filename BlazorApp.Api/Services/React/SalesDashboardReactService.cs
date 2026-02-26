using AutoMapper;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Caching.Memory;
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
        public string? ProductImage { get; set; }
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

        private static readonly TimeSpan SUMMARY_CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RANKING_CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DETAIL_CACHE_DURATION = TimeSpan.FromMinutes(3);

        public SalesDashboardReactService(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            IMapper mapper,
            ILogger<SalesDashboardReactService> logger,
            IMemoryCache cache
        )
        {
            _context = context;
            _posmContext = posmContext;
            _mapper = mapper;
            _logger = logger;
            _cache = cache;
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

                if (_cache.TryGetValue<DashboardSummaryDto>(cacheKey, out var cachedResult))
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

                if (_cache.TryGetValue<List<HourlySalesDto>>(cacheKey, out var cachedResult))
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

                if (_cache.TryGetValue<List<StoreSalesRankDto>>(cacheKey, out var cachedResult))
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
        /// 按销售额降序排列，返回前 N 名供应商
        /// 特殊处理：将中国供应商数据合并到供应商代码为"200"的记录中
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="topN">返回前 N 名，默认 20</param>
        /// <returns>供应商销售排名列表</returns>
        public async Task<List<SupplierSalesRankDto>> GetSupplierSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 20
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.SupplierRank(dateRange, branchCodes, topN);

                if (_cache.TryGetValue<List<SupplierSalesRankDto>>(cacheKey, out var cachedResult))
                {
                    _logger.LogInformation("从缓存获取供应商销售排名: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<StoreSupplierSalesDetail>())
                {
                    return new List<SupplierSalesRankDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                _logger.LogInformation(
                    "开始查询供应商销售排名: {StartDate} - {EndDate}, BranchCodes: {BranchCodes}, TopN: {TopN}",
                    startDate,
                    endDate,
                    branchCodes != null ? string.Join(",", branchCodes) : "All",
                    topN
                );

                var query = _context
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate && s.IsDomestic != true);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var currentData = await query
                    .GroupBy(s => new { s.SupplierCode, s.SupplierName })
                    .Select(s => new
                    {
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName ?? s.SupplierCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        StoreCount = SqlFunc.AggregateCount(s.BranchCode),
                    })
                    .OrderByDescending(s => s.TotalAmount)
                    .Take(topN)
                    .ToListAsync();

                _logger.LogInformation("查询到 {Count} 条普通供应商数据", currentData.Count);

                // 查询中国供应商数据
                var chinaSupplierQuery = _context
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate && s.IsDomestic == true);

                // 如果指定了分店代码，则进行过滤
                if (branchCodes != null && branchCodes.Any())
                {
                    chinaSupplierQuery = chinaSupplierQuery.Where(s =>
                        branchCodes.Contains(s.BranchCode)
                    );
                }

                // 获取中国供应商的总金额、总数量和分店数量
                var chinaSupplierDataList = await chinaSupplierQuery
                    .Select(s => new
                    {
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        StoreCount = SqlFunc.AggregateCount(s.BranchCode),
                    })
                    .ToListAsync();

                var chinaSupplierData = chinaSupplierDataList.FirstOrDefault();
                int chinaSupplierStoreCount = chinaSupplierData?.StoreCount ?? 0;
                decimal chinaSupplierTotalAmount = chinaSupplierData?.TotalAmount ?? 0;
                int chinaSupplierTotalQuantity = chinaSupplierData?.TotalQuantity ?? 0;

                _logger.LogInformation(
                    "中国供应商汇总数据: StoreCount={StoreCount}, TotalAmount={TotalAmount}, TotalQuantity={TotalQuantity}",
                    chinaSupplierStoreCount,
                    chinaSupplierTotalAmount,
                    chinaSupplierTotalQuantity
                );

                // 初始化对比数据变量
                var chinaCompareData = (decimal?)null;
                Dictionary<string, decimal> compareDict = new Dictionary<string, decimal>();

                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var compareStartDate = dateRange.CompareStartDate.Value.Date;
                    var compareEndDate = dateRange.CompareEndDate.Value.Date;

                    _logger.LogInformation(
                        "查询对比数据: {CompareStartDate} - {CompareEndDate}",
                        compareStartDate,
                        compareEndDate
                    );

                    // 获取中国供应商对比数据
                    var chinaCompareQuery = _context
                        .Db.Queryable<StoreSupplierSalesDetail>()
                        .Where(s =>
                            s.Date >= compareStartDate
                            && s.Date <= compareEndDate
                            && s.IsDomestic == true
                        );

                    if (branchCodes != null && branchCodes.Any())
                    {
                        chinaCompareQuery = chinaCompareQuery.Where(s =>
                            branchCodes.Contains(s.BranchCode)
                        );
                    }

                    var compareResultList = await chinaCompareQuery
                        .Select(s => new { TotalAmount = SqlFunc.AggregateSum(s.TotalAmount) })
                        .ToListAsync();
                    var compareResult = compareResultList.FirstOrDefault();
                    chinaCompareData = compareResult?.TotalAmount ?? 0;

                    _logger.LogInformation(
                        "中国供应商对比数据: CompareAmount={CompareAmount}",
                        chinaCompareData
                    );

                    // 批量获取普通供应商对比数据
                    var supplierCodes = currentData.Select(s => s.SupplierCode).ToList();
                    if (supplierCodes.Any())
                    {
                        var compareQuery = _context
                            .Db.Queryable<StoreSupplierSalesDetail>()
                            .Where(s =>
                                s.Date >= compareStartDate
                                && s.Date <= compareEndDate
                                && s.IsDomestic != true
                                && supplierCodes.Contains(s.SupplierCode)
                            );

                        if (branchCodes != null && branchCodes.Any())
                        {
                            compareQuery = compareQuery.Where(s =>
                                branchCodes.Contains(s.BranchCode)
                            );
                        }

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
                }

                var result = new List<SupplierSalesRankDto>();

                // 遍历当前数据，构建供应商排名列表
                foreach (var item in currentData)
                {
                    decimal totalAmount = item.TotalAmount;
                    int totalQuantity = item.TotalQuantity;
                    int storeCount = item.StoreCount;
                    decimal? compareTotalAmount = null;

                    // 如果供应商代码为"200"，将中国供应商的数据合并到该供应商
                    if (item.SupplierCode == "200")
                    {
                        totalAmount += chinaSupplierTotalAmount;
                        totalQuantity += chinaSupplierTotalQuantity;
                        storeCount = item.StoreCount + chinaSupplierStoreCount;
                        compareTotalAmount = chinaCompareData;
                    }

                    var dto = new SupplierSalesRankDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        SupplierCode = item.SupplierCode,
                        SupplierName = item.SupplierName,
                        TotalAmount = totalAmount,
                        TotalQuantity = totalQuantity,
                        StoreCount = storeCount,
                    };

                    if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                    {
                        // 获取当前供应商的对比数据
                        decimal supplierCompareAmount = 0;
                        if (compareDict.TryGetValue(item.SupplierCode, out var amount))
                        {
                            supplierCompareAmount = amount;
                        }

                        // 合并 "200" 的对比数据
                        dto.CompareTotalAmount = supplierCompareAmount + (compareTotalAmount ?? 0);

                        dto.TotalAmountGrowth = CalculateGrowth(
                            totalAmount,
                            dto.CompareTotalAmount ?? 0
                        );
                    }

                    result.Add(dto);
                }

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
        /// 专门统计 IsDomestic 为 true 的中国供应商数据
        /// </summary>
        /// <param name="dateRange">日期范围</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="topN">返回前 N 名，默认 20</param>
        /// <returns>中国供应商销售排名列表</returns>
        public async Task<List<ChinaSupplierSalesRankDto>> GetChinaSupplierSalesRankAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            int topN = 20
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.ChinaSupplierRank(
                    dateRange,
                    branchCodes,
                    topN
                );

                if (
                    _cache.TryGetValue<List<ChinaSupplierSalesRankDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                )
                {
                    _logger.LogInformation("从缓存获取中国供应商销售排名: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<StoreSupplierSalesDetail>())
                {
                    return new List<ChinaSupplierSalesRankDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var query = _context
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate && s.IsDomestic == true);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var currentData = await query
                    .GroupBy(s => new { s.SupplierCode, s.SupplierName })
                    .Select(s => new
                    {
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName ?? s.SupplierCode,
                        TotalAmount = SqlFunc.AggregateSum(s.TotalAmount),
                        TotalQuantity = SqlFunc.AggregateSum(s.TotalQuantity),
                        StoreCount = SqlFunc.AggregateCount(s.BranchCode),
                    })
                    .OrderByDescending(s => s.TotalAmount)
                    .Take(topN)
                    .ToListAsync();

                var result = new List<ChinaSupplierSalesRankDto>();

                // 批量获取对比数据
                Dictionary<string, decimal> compareDict = new Dictionary<string, decimal>();

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
                                && s.IsDomestic == true
                                && supplierCodes.Contains(s.SupplierCode)
                            );

                        if (branchCodes != null && branchCodes.Any())
                        {
                            compareQuery = compareQuery.Where(s =>
                                branchCodes.Contains(s.BranchCode)
                            );
                        }

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
                }

                foreach (var item in currentData)
                {
                    var dto = new ChinaSupplierSalesRankDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        SupplierCode = item.SupplierCode,
                        SupplierName = item.SupplierName,
                        TotalAmount = item.TotalAmount,
                        TotalQuantity = item.TotalQuantity,
                        StoreCount = item.StoreCount,
                    };

                    if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                    {
                        decimal compareTotalAmount = 0;
                        if (compareDict.TryGetValue(item.SupplierCode, out var amount))
                        {
                            compareTotalAmount = amount;
                        }

                        dto.CompareTotalAmount = compareTotalAmount;
                        dto.TotalAmountGrowth = CalculateGrowth(
                            item.TotalAmount,
                            compareTotalAmount
                        );
                    }

                    result.Add(dto);
                }

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
                if (supplierCodes == null || !supplierCodes.Any())
                    throw new ArgumentException("供应商代码不能为空", nameof(supplierCodes));

                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.SupplierStore(
                    dateRange,
                    supplierCodes,
                    branchCodes
                );

                if (_cache.TryGetValue<List<SupplierStoreSalesDto>>(cacheKey, out var cachedResult))
                {
                    _logger.LogInformation("从缓存获取供应商分店销售数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<StoreSupplierSalesDetail>())
                {
                    return new List<SupplierStoreSalesDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var currentQuery = _context
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s =>
                        s.Date >= startDate
                        && s.Date <= endDate
                        && supplierCodes.Contains(s.SupplierCode)
                    );

                if (branchCodes != null && branchCodes.Any())
                {
                    currentQuery = currentQuery.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var currentData = await currentQuery
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
                    .ToListAsync();

                var branchCodesList = currentData.Select(d => d.BranchCode).Distinct().ToList();
                var stores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => branchCodesList.Contains(s.StoreCode))
                    .ToListAsync();
                var storeDict = stores.ToDictionary(s => s.StoreCode, s => s.StoreName);

                var result = new List<SupplierStoreSalesDto>();

                // 批量获取对比数据
                Dictionary<string, decimal> compareDict = new Dictionary<string, decimal>();

                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var compareStartDate = dateRange.CompareStartDate.Value.Date;
                    var compareEndDate = dateRange.CompareEndDate.Value.Date;

                    if (branchCodesList.Any())
                    {
                        var compareQuery = _context
                            .Db.Queryable<StoreSupplierSalesDetail>()
                            .Where(s =>
                                s.Date >= compareStartDate
                                && s.Date <= compareEndDate
                                && supplierCodes.Contains(s.SupplierCode)
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
                            })
                            .ToListAsync();

                        compareDict = compareDataList.ToDictionary(
                            s => s.BranchCode,
                            s => s.TotalAmount
                        );
                    }
                }

                foreach (var item in currentData)
                {
                    var branchName = storeDict.GetValueOrDefault(item.BranchCode, item.BranchCode);
                    var dto = new SupplierStoreSalesDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        SupplierCode = item.SupplierCode,
                        SupplierName = item.SupplierName,
                        BranchCode = item.BranchCode,
                        BranchName = branchName,
                        TotalAmount = item.TotalAmount,
                        TotalQuantity = item.TotalQuantity,
                    };

                    if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                    {
                        decimal compareTotalAmount = 0;
                        if (compareDict.TryGetValue(item.BranchCode, out var amount))
                        {
                            compareTotalAmount = amount;
                        }

                        dto.CompareTotalAmount = compareTotalAmount;
                        dto.TotalAmountGrowth = CalculateGrowth(
                            item.TotalAmount,
                            compareTotalAmount
                        );
                    }

                    result.Add(dto);
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "供应商分店销售数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result.OrderByDescending(r => r.TotalAmount).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSupplierStoreSalesAsync failed");
                return new List<SupplierStoreSalesDto>();
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

                if (_cache.TryGetValue<List<StoreSupplierSalesDto>>(cacheKey, out var cachedResult))
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

                if (_cache.TryGetValue<PagedSalesProductDetailDto>(cacheKey, out var cachedResult))
                {
                    _logger.LogInformation("从缓存获取产品销售明细: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date.AddDays(1);

                var hasSupplierFilter = supplierCodes != null && supplierCodes.Any();
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

                    supplierQuery = supplierQuery.Where(
                        (o, d, m) => supplierCodes.Contains(m.ChinaSupplierCode ?? string.Empty)
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
                            .Where(p => productCodes.Contains(p.ProductCode))
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
                            .Where(p => productCodes.Contains(p.ProductCode))
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
        /// <returns>分页的含折扣信息的产品销售明细</returns>
        public async Task<PagedSalesProductDetailWithDiscountDto> GetEnhancedSalesProductDetailsAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes = null,
            List<string>? localSupplierCodes = null,
            List<string>? chinaSupplierCodes = null,
            int pageIndex = 1,
            int pageSize = 100
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                // 步骤 1: 生成缓存键
                var cacheKey = SalesDashboardCacheKeys.EnhancedProductDetail(
                    dateRange,
                    branchCodes,
                    localSupplierCodes,
                    chinaSupplierCodes,
                    pageIndex,
                    pageSize
                );

                // 步骤 2: 检查缓存是否存在，存在则直接返回
                if (
                    _cache.TryGetValue<PagedSalesProductDetailWithDiscountDto>(
                        cacheKey,
                        out var cachedResult
                    )
                )
                {
                    _logger.LogInformation("从缓存获取增强产品销售明细: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                // 步骤 3: 准备日期范围
                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date.AddDays(1);

                // 步骤 4: 判断是否有供应商过滤条件
                var hasSupplierFilter =
                    (localSupplierCodes != null && localSupplierCodes.Any())
                    || (chinaSupplierCodes != null && chinaSupplierCodes.Any());

                List<SalesProductDetailWithDiscountDto> data;
                int totalCount;

                if (hasSupplierFilter)
                {
                    // 步骤 5.1: 构建三表连接查询（有供应商过滤）
                    var baseQuery = _posmContext
                        .Db.Queryable<SalesOrder, SalesOrderDetail, PosmProductSupplierMapping>(
                            (o, d, m) =>
                                o.OrderGuid == d.OrderGuid
                                && d.ProductCode == m.ProductCode
                                && !m.IsDeleted
                        )
                        .Where((o, d, m) => o.OrderTime >= startDate && o.OrderTime < endDate);

                    // 步骤 5.2: 添加分店过滤条件
                    if (branchCodes != null && branchCodes.Any())
                    {
                        baseQuery = baseQuery.Where(
                            (o, d, m) => branchCodes.Contains(o.BranchCode ?? string.Empty)
                        );
                    }

                    // 步骤 5.3: 添加本地供应商过滤条件
                    if (localSupplierCodes != null && localSupplierCodes.Any())
                    {
                        baseQuery = baseQuery.Where(
                            (o, d, m) => localSupplierCodes.Contains(m.LocalSupplierCode)
                        );
                    }

                    // 步骤 5.4: 添加中国供应商过滤条件
                    if (chinaSupplierCodes != null && chinaSupplierCodes.Any())
                    {
                        baseQuery = baseQuery.Where(
                            (o, d, m) =>
                                chinaSupplierCodes.Contains(m.ChinaSupplierCode ?? string.Empty)
                        );
                    }

                    // 步骤 6: 在数据库层面计算总数（GROUP BY 后的产品数量）
                    totalCount = await baseQuery
                        .GroupBy((o, d, m) => d.ProductCode)
                        .Select((o, d, m) => new { Count = SqlFunc.RowCount() })
                        .MergeTable()
                        .CountAsync();

                    var skip = (pageIndex - 1) * pageSize;

                    // 步骤 7: 在数据库层面执行分组、聚合、排序和分页
                    var groupedData = await baseQuery
                        .GroupBy((o, d, m) => d.ProductCode)
                        .Select(
                            (o, d, m) =>
                                new
                                {
                                    ProductCode = d.ProductCode ?? string.Empty,
                                    ProductName = SqlFunc.AggregateMax(d.ProductName),
                                    Quantity = SqlFunc.AggregateSum(d.Quantity) ?? 0,
                                    DiscountedQuantity = SqlFunc.AggregateSum(
                                        SqlFunc.IIF(d.DiscountAmount > 0, d.Quantity, 0)
                                    ) ?? 0,
                                    SalesAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0,
                                    TotalOriginalAmount = SqlFunc.AggregateSum(d.Subtotal) ?? 0,
                                    OrderCount = SqlFunc.AggregateDistinctCount(o.OrderGuid),
                                }
                        )
                        .MergeTable()
                        .OrderByDescending(x => x.SalesAmount)
                        .Skip(skip)
                        .Take(pageSize)
                        .ToListAsync();

                    // 步骤 8: 提取当前页面的产品代码列表
                    var productCodes = groupedData.Select(x => x.ProductCode).ToList();

                    // 步骤 9: 查询产品的额外信息（ItemNumber、ProductImage）
                    var products = productCodes.Any()
                        ? await _context
                            .Db.Queryable<Product>()
                            .Where(p => productCodes.Contains(p.ProductCode))
                            .Select(p => new ProductInfo
                            {
                                ProductCode = p.ProductCode ?? string.Empty,
                                ItemNumber = p.ItemNumber,
                                ProductImage = p.ProductImage,
                            })
                            .ToListAsync()
                        : new List<ProductInfo>();

                    // 步骤 10: 构建产品字典以便快速查找
                    var productDict = products.ToDictionary(p => p.ProductCode);

                    // 步骤 11: 组装最终结果数据
                    data = groupedData
                        .Select(x => new SalesProductDetailWithDiscountDto
                        {
                            ProductCode = x.ProductCode,
                            ItemNumber = productDict.TryGetValue(x.ProductCode, out var p)
                                ? (string?)p.ItemNumber
                                : null,
                            ProductImage = productDict.TryGetValue(x.ProductCode, out var p2)
                                ? (string?)p2.ProductImage
                                : null,
                            ProductName = x.ProductName,
                            Quantity = x.Quantity,
                            DiscountedQuantity = x.DiscountedQuantity,
                            SalesAmount = x.SalesAmount,
                            AverageUnitPrice = x.Quantity > 0 ? x.SalesAmount / x.Quantity : 0,
                            AverageOriginalPrice =
                                x.Quantity > 0
                                    ? x.TotalOriginalAmount / x.Quantity
                                    : (decimal?)null,
                            OrderCount = x.OrderCount,
                        })
                        .ToList();
                }
                else
                {
                    // 步骤 5.1: 构建两表连接查询（无供应商过滤）
                    var baseQuery = _posmContext
                        .Db.Queryable<SalesOrder, SalesOrderDetail>(
                            (o, d) => o.OrderGuid == d.OrderGuid
                        )
                        .Where((o, d) => o.OrderTime >= startDate && o.OrderTime < endDate);

                    // 步骤 5.2: 添加分店过滤条件
                    if (branchCodes != null && branchCodes.Any())
                    {
                        baseQuery = baseQuery.Where(
                            (o, d) => branchCodes.Contains(o.BranchCode ?? string.Empty)
                        );
                    }

                    // 步骤 6: 在数据库层面计算总数
                    totalCount = await baseQuery
                        .GroupBy((o, d) => d.ProductCode)
                        .Select((o, d) => new { Count = SqlFunc.RowCount() })
                        .MergeTable()
                        .CountAsync();

                    var skip = (pageIndex - 1) * pageSize;

                    // 步骤 7: 在数据库层面执行分组、聚合、排序和分页
                    var groupedData = await baseQuery
                        .GroupBy((o, d) => d.ProductCode)
                        .Select(
                            (o, d) =>
                                new
                                {
                                    ProductCode = d.ProductCode ?? string.Empty,
                                    ProductName = SqlFunc.AggregateMax(d.ProductName),
                                    Quantity = SqlFunc.AggregateSum(d.Quantity) ?? 0,
                                    DiscountedQuantity = SqlFunc.AggregateSum(
                                        SqlFunc.IIF(d.DiscountAmount > 0, d.Quantity, 0)
                                    ) ?? 0,
                                    SalesAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0,
                                    TotalOriginalAmount = SqlFunc.AggregateSum(d.Subtotal) ?? 0,
                                    OrderCount = SqlFunc.AggregateDistinctCount(o.OrderGuid),
                                }
                        )
                        .MergeTable()
                        .OrderByDescending(x => x.SalesAmount)
                        .Skip(skip)
                        .Take(pageSize)
                        .ToListAsync();

                    // 步骤 8: 提取当前页面的产品代码列表
                    var productCodes = groupedData.Select(x => x.ProductCode).ToList();

                    // 步骤 9: 查询产品的额外信息
                    var products = productCodes.Any()
                        ? await _context
                            .Db.Queryable<Product>()
                            .Where(p => productCodes.Contains(p.ProductCode))
                            .Select(p => new ProductInfo
                            {
                                ProductCode = p.ProductCode ?? string.Empty,
                                ItemNumber = p.ItemNumber,
                                ProductImage = p.ProductImage,
                            })
                            .ToListAsync()
                        : new List<ProductInfo>();

                    // 步骤 10: 构建产品字典
                    var productDict = products.ToDictionary(p => p.ProductCode);

                    // 步骤 11: 组装最终结果数据
                    data = groupedData
                        .Select(x => new SalesProductDetailWithDiscountDto
                        {
                            ProductCode = x.ProductCode,
                            ItemNumber = productDict.TryGetValue(x.ProductCode, out var p)
                                ? (string?)p.ItemNumber
                                : null,
                            ProductImage = productDict.TryGetValue(x.ProductCode, out var p2)
                                ? (string?)p2.ProductImage
                                : null,
                            ProductName = x.ProductName,
                            Quantity = x.Quantity,
                            DiscountedQuantity = x.DiscountedQuantity,
                            SalesAmount = x.SalesAmount,
                            AverageUnitPrice = x.Quantity > 0 ? x.SalesAmount / x.Quantity : 0,
                            AverageOriginalPrice =
                                x.Quantity > 0
                                    ? x.TotalOriginalAmount / x.Quantity
                                    : (decimal?)null,
                            OrderCount = x.OrderCount,
                        })
                        .ToList();
                }

                // 步骤 12: 构建分页结果
                var result = new PagedSalesProductDetailWithDiscountDto
                {
                    Data = data,
                    Total = totalCount,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                };

                // 步骤 13: 设置缓存选项并保存结果
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(DETAIL_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "增强产品销售明细已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(DETAIL_CACHE_DURATION)
                );

                return result;
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
        /// <returns>产品在各分店的销售数据列表</returns>
        public async Task<List<ProductBranchSalesDto>> GetProductSalesByAllBranchesAsync(
            DateRangeDto dateRange,
            string productCode
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.ProductBranch(dateRange, productCode);

                if (_cache.TryGetValue<List<ProductBranchSalesDto>>(cacheKey, out var cachedResult))
                {
                    _logger.LogInformation("从缓存获取产品各分店销售数据: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date.AddDays(1);

                var stores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => !s.IsDeleted)
                    .Select(s => new { s.StoreCode, s.StoreName })
                    .ToListAsync();

                var storeDict = stores.ToDictionary(s => s.StoreCode ?? string.Empty);

                var salesData = await _posmContext
                    .Db.Queryable<SalesOrder, SalesOrderDetail>(
                        (o, d) => o.OrderGuid == d.OrderGuid
                    )
                    .Where(
                        (o, d) =>
                            o.OrderTime >= startDate
                            && o.OrderTime < endDate
                            && d.ProductCode == productCode
                    )
                    .GroupBy((o, d) => o.BranchCode)
                    .Select(
                        (o, d) =>
                            new
                            {
                                BranchCode = o.BranchCode,
                                Quantity = SqlFunc.AggregateSum(d.Quantity) ?? 0,
                                DiscountedQuantity = SqlFunc.AggregateSum(
                                    SqlFunc.IIF(d.DiscountAmount > 0, d.Quantity, 0)
                                ) ?? 0,
                            }
                    )
                    .ToListAsync();

                var result = salesData
                    .Select(x => new ProductBranchSalesDto
                    {
                        BranchCode = x.BranchCode ?? string.Empty,
                        BranchName = storeDict.TryGetValue(
                            x.BranchCode ?? string.Empty,
                            out var store
                        )
                            ? store.StoreName ?? x.BranchCode ?? string.Empty
                            : x.BranchCode ?? string.Empty,
                        Quantity = x.Quantity,
                        DiscountedQuantity = x.DiscountedQuantity,
                    })
                    .OrderByDescending(x => x.Quantity)
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
                if (supplierCodes == null || !supplierCodes.Any())
                    throw new ArgumentException("供应商代码不能为空", nameof(supplierCodes));

                ValidateDateRange(dateRange);

                var cacheKey = SalesDashboardCacheKeys.SupplierStore(
                    dateRange,
                    supplierCodes,
                    branchCodes
                );

                if (
                    _cache.TryGetValue<List<ChinaSupplierStoreSalesDto>>(
                        cacheKey,
                        out var cachedResult
                    )
                )
                {
                    _logger.LogInformation(
                        "从缓存获取中国供应商分店销售数据: {CacheKey}",
                        cacheKey
                    );
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<ChinaSupplierStoreSalesDetail>())
                {
                    return new List<ChinaSupplierStoreSalesDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                var currentQuery = _context
                    .Db.Queryable<ChinaSupplierStoreSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (supplierCodes != null && supplierCodes.Any())
                {
                    currentQuery = currentQuery.Where(s => supplierCodes.Contains(s.SupplierCode));
                }

                if (branchCodes != null && branchCodes.Any())
                {
                    currentQuery = currentQuery.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var currentData = await currentQuery
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
                    .ToListAsync();

                var branchCodesList = currentData.Select(d => d.BranchCode).Distinct().ToList();
                var stores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => branchCodesList.Contains(s.StoreCode))
                    .ToListAsync();
                var storeDict = stores.ToDictionary(s => s.StoreCode, s => s.StoreName);

                var result = new List<ChinaSupplierStoreSalesDto>();
                foreach (var item in currentData)
                {
                    var branchName = storeDict.TryGetValue(
                        item.BranchCode ?? string.Empty,
                        out var store
                    )
                        ? store ?? item.BranchCode ?? string.Empty
                        : item.BranchCode ?? string.Empty;

                    result.Add(
                        new ChinaSupplierStoreSalesDto
                        {
                            StartDate = startDate,
                            EndDate = endDate,
                            SupplierCode = item.SupplierCode ?? string.Empty,
                            SupplierName = item.SupplierName ?? string.Empty,
                            BranchCode = item.BranchCode ?? string.Empty,
                            BranchName = branchName,
                            TotalAmount = item.TotalAmount,
                            TotalQuantity = item.TotalQuantity,
                        }
                    );
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(RANKING_CACHE_DURATION)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogInformation(
                    "中国供应商分店销售数据已缓存: {CacheKey}, 过期时间: {Expiration}",
                    cacheKey,
                    DateTime.Now.Add(RANKING_CACHE_DURATION)
                );

                return result.OrderByDescending(r => r.TotalAmount).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChinaSupplierStoreSalesAsync failed");
                return new List<ChinaSupplierStoreSalesDto>();
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
    }
}
