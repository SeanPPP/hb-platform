using System.Globalization;
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
        public string? PosmBarcode { get; set; }
        public string? PosmProductName { get; set; }
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
        /// 从 AustralianSupplierStoreSalesDetail 表查询，按供应商汇总
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

                var cacheKey = SalesDashboardCacheKeys.SupplierRank(dateRange, branchCodes, topN);

                if (
                    _cache.TryGetValue<List<SupplierSalesRankDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
                {
                    _logger.LogInformation("从缓存获取供应商销售排名: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                if (!ValidateDatabaseConnection<AustralianSupplierStoreSalesDetail>())
                {
                    return new List<SupplierSalesRankDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                _logger.LogInformation(
                    "从 AustralianSupplierStoreSalesDetail 表查询供应商销售排名: {StartDate} - {EndDate}, BranchCodes: {BranchCodes}, TopN: {TopN}, SupplierCode: {SupplierCode}",
                    startDate,
                    endDate,
                    branchCodes != null ? string.Join(",", branchCodes) : "All",
                    topN,
                    supplierCode ?? "All"
                );

                var query = _context
                    .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                if (!string.IsNullOrEmpty(supplierCode))
                {
                    query = query.Where(s => s.SupplierCode == supplierCode);
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

                _logger.LogInformation("查询到 {Count} 条澳洲供应商数据", currentData.Count);

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

                    var supplierCodes = currentData.Select(s => s.SupplierCode).ToList();
                    if (supplierCodes.Any())
                    {
                        var compareQuery = _context
                            .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                            .Where(s =>
                                s.Date >= compareStartDate
                                && s.Date <= compareEndDate
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

                foreach (var item in currentData)
                {
                    decimal totalAmount = item.TotalAmount;
                    decimal? compareTotalAmount = null;

                    if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                    {
                        if (compareDict.TryGetValue(item.SupplierCode, out var amount))
                        {
                            compareTotalAmount = amount;
                        }
                        else
                        {
                            compareTotalAmount = 0;
                        }
                    }

                    var dto = new SupplierSalesRankDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        SupplierCode = item.SupplierCode,
                        SupplierName = item.SupplierName,
                        TotalAmount = totalAmount,
                        TotalQuantity = item.TotalQuantity,
                        StoreCount = item.StoreCount,
                        CompareTotalAmount = compareTotalAmount,
                    };

                    if (compareTotalAmount.HasValue)
                    {
                        dto.TotalAmountGrowth = CalculateGrowth(
                            totalAmount,
                            compareTotalAmount.Value
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
        /// 从 ChinaSupplierStoreSalesDetail 表查询，按供应商汇总
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
                    topN
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

                if (!ValidateDatabaseConnection<ChinaSupplierStoreSalesDetail>())
                {
                    return new List<ChinaSupplierSalesRankDto>();
                }

                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date;

                _logger.LogInformation(
                    "从 ChinaSupplierStoreSalesDetail 表查询中国供应商销售排名: {StartDate} - {EndDate}, BranchCodes: {BranchCodes}, TopN: {TopN}, SupplierCode: {SupplierCode}",
                    startDate,
                    endDate,
                    branchCodes != null ? string.Join(",", branchCodes) : "All",
                    topN,
                    supplierCode ?? "All"
                );

                var query = _context
                    .Db.Queryable<ChinaSupplierStoreSalesDetail>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                if (!string.IsNullOrEmpty(supplierCode))
                {
                    query = query.Where(s => s.SupplierCode == supplierCode);
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

                _logger.LogInformation("查询到 {Count} 条中国供应商数据", currentData.Count);

                var result = new List<ChinaSupplierSalesRankDto>();

                Dictionary<string, decimal> compareDict = new Dictionary<string, decimal>();

                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    var compareStartDate = dateRange.CompareStartDate.Value.Date;
                    var compareEndDate = dateRange.CompareEndDate.Value.Date;
                    var supplierCodes = currentData.Select(s => s.SupplierCode).ToList();

                    if (supplierCodes.Any())
                    {
                        var compareQuery = _context
                            .Db.Queryable<ChinaSupplierStoreSalesDetail>()
                            .Where(s =>
                                s.Date >= compareStartDate
                                && s.Date <= compareEndDate
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
                    decimal compareTotalAmount = 0;
                    if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                    {
                        if (compareDict.TryGetValue(item.SupplierCode, out var amount))
                        {
                            compareTotalAmount = amount;
                        }
                    }

                    var dto = new ChinaSupplierSalesRankDto
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        SupplierCode = item.SupplierCode,
                        SupplierName = item.SupplierName,
                        TotalAmount = item.TotalAmount,
                        TotalQuantity = item.TotalQuantity,
                        StoreCount = item.StoreCount,
                        CompareTotalAmount = compareTotalAmount,
                    };

                    if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                    {
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

                if (
                    _cache.TryGetValue<List<SupplierStoreSalesDto>>(cacheKey, out var cachedResult)
                    && cachedResult != null
                    && cachedResult.Count != 0
                )
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

                _logger.LogInformation("[GetEnhancedSalesProductDetailsAsync] Processing request: StartDate={StartDate}, EndDate={EndDate}, CompareStartDate={CompareStartDate}, CompareEndDate={CompareEndDate}, HasSupplierFilter={HasSupplierFilter}",
                    dateRange.StartDate, dateRange.EndDate, dateRange.CompareStartDate, dateRange.CompareEndDate,
                    (localSupplierCodes != null && localSupplierCodes.Any()) || (chinaSupplierCodes != null && chinaSupplierCodes.Any()));

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
                    && cachedResult != null
                )
                {
                    _logger.LogInformation("从缓存获取增强产品销售明细: {CacheKey}", cacheKey);
                    return cachedResult;
                }

                // 步骤 3: 准备日期范围
                var startDate = dateRange.StartDate.Date;
                var endDate = dateRange.EndDate.Date.AddDays(1);

                // 步骤 3.5: 准备对比期日期范围
                DateTime? compareStartDate = null;
                DateTime? compareEndDate = null;
                if (dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue)
                {
                    compareStartDate = dateRange.CompareStartDate.Value.Date;
                    compareEndDate = dateRange.CompareEndDate.Value.Date.AddDays(1);
                }

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

                    // 步骤 10.5: 查询对比期数据
                    var compareDataDict = new Dictionary<string, (int Quantity, int DiscountedQuantity, decimal SalesAmount, decimal TotalOriginalAmount, int OrderCount)>();
                    if (compareStartDate.HasValue && compareEndDate.HasValue)
                    {
                        _logger.LogInformation("[GetEnhancedSalesProductDetailsAsync] Querying compare period data: CompareStartDate={CompareStartDate}, CompareEndDate={CompareEndDate}",
                            compareStartDate.Value, compareEndDate.Value);

                        var compareBaseQuery = _posmContext
                            .Db.Queryable<SalesOrder, SalesOrderDetail, PosmProductSupplierMapping>(
                                (o, d, m) =>
                                    o.OrderGuid == d.OrderGuid
                                    && d.ProductCode == m.ProductCode
                                    && !m.IsDeleted
                            )
                            .Where((o, d, m) => o.OrderTime >= compareStartDate.Value && o.OrderTime < compareEndDate.Value);

                        if (branchCodes != null && branchCodes.Any())
                        {
                            compareBaseQuery = compareBaseQuery.Where(
                                (o, d, m) => branchCodes.Contains(o.BranchCode ?? string.Empty)
                            );
                        }

                        if (localSupplierCodes != null && localSupplierCodes.Any())
                        {
                            compareBaseQuery = compareBaseQuery.Where(
                                (o, d, m) => localSupplierCodes.Contains(m.LocalSupplierCode)
                            );
                        }

                        if (chinaSupplierCodes != null && chinaSupplierCodes.Any())
                        {
                            compareBaseQuery = compareBaseQuery.Where(
                                (o, d, m) =>
                                    chinaSupplierCodes.Contains(m.ChinaSupplierCode ?? string.Empty)
                            );
                        }

                        var compareGroupedData = await compareBaseQuery
                            .GroupBy((o, d, m) => d.ProductCode)
                            .Select(
                                (o, d, m) =>
                                    new
                                    {
                                        ProductCode = d.ProductCode ?? string.Empty,
                                        Quantity = SqlFunc.AggregateSum(d.Quantity) ?? 0,
                                        DiscountedQuantity = SqlFunc.AggregateSum(
                                            SqlFunc.IIF(d.DiscountAmount > 0, d.Quantity, 0)
                                        ) ?? 0,
                                        SalesAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0,
                                        TotalOriginalAmount = SqlFunc.AggregateSum(d.Subtotal) ?? 0,
                                        OrderCount = SqlFunc.AggregateDistinctCount(o.OrderGuid),
                                    }
                            )
                            .ToListAsync();

                        compareDataDict = compareGroupedData
                            .ToDictionary(x => x.ProductCode, x => (x.Quantity, x.DiscountedQuantity, x.SalesAmount, x.TotalOriginalAmount, x.OrderCount));
                    }

                    // 步骤 11: 组装最终结果数据
                    data = groupedData
                        .Select(x =>
                        {
                            var result = new SalesProductDetailWithDiscountDto
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
                            };

                            if (compareDataDict.TryGetValue(x.ProductCode, out var compareData))
                            {
                                result.QuantityLY = compareData.Quantity;
                                result.DiscountedQuantityLY = compareData.DiscountedQuantity;
                                result.SalesAmountLY = compareData.SalesAmount;
                                result.AverageUnitPriceLY = compareData.Quantity > 0 ? compareData.SalesAmount / compareData.Quantity : 0;
                                result.AverageOriginalPriceLY = compareData.Quantity > 0
                                    ? compareData.TotalOriginalAmount / compareData.Quantity
                                    : null;
                                result.OrderCountLY = compareData.OrderCount;
                            }
                            else
                            {
                                _logger.LogWarning("[GetEnhancedSalesProductDetailsAsync] Product {ProductCode} not found in compare data dictionary", x.ProductCode);
                            }

                            return result;
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

                    // 步骤 10.5: 查询对比期数据
                    var compareDataDict = new Dictionary<string, (int Quantity, int DiscountedQuantity, decimal SalesAmount, decimal TotalOriginalAmount, int OrderCount)>();
                    if (compareStartDate.HasValue && compareEndDate.HasValue)
                    {
                        var compareBaseQuery = _posmContext
                            .Db.Queryable<SalesOrder, SalesOrderDetail>(
                                (o, d) => o.OrderGuid == d.OrderGuid
                            )
                            .Where((o, d) => o.OrderTime >= compareStartDate.Value && o.OrderTime < compareEndDate.Value);

                        if (branchCodes != null && branchCodes.Any())
                        {
                            compareBaseQuery = compareBaseQuery.Where(
                                (o, d) => branchCodes.Contains(o.BranchCode ?? string.Empty)
                            );
                        }

                        var compareGroupedData = await compareBaseQuery
                            .GroupBy((o, d) => d.ProductCode)
                            .Select(
                                (o, d) =>
                                    new
                                    {
                                        ProductCode = d.ProductCode ?? string.Empty,
                                        Quantity = SqlFunc.AggregateSum(d.Quantity) ?? 0,
                                        DiscountedQuantity = SqlFunc.AggregateSum(
                                            SqlFunc.IIF(d.DiscountAmount > 0, d.Quantity, 0)
                                        ) ?? 0,
                                        SalesAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0,
                                        TotalOriginalAmount = SqlFunc.AggregateSum(d.Subtotal) ?? 0,
                                        OrderCount = SqlFunc.AggregateDistinctCount(o.OrderGuid),
                                    }
                            )
                            .ToListAsync();

                        compareDataDict = compareGroupedData
                            .ToDictionary(x => x.ProductCode, x => (x.Quantity, x.DiscountedQuantity, x.SalesAmount, x.TotalOriginalAmount, x.OrderCount));
                    }

                    // 步骤 11: 组装最终结果数据
                    data = groupedData
                        .Select(x =>
                        {
                            var result = new SalesProductDetailWithDiscountDto
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
                            };

                            if (compareDataDict.TryGetValue(x.ProductCode, out var compareData))
                            {
                                result.QuantityLY = compareData.Quantity;
                                result.DiscountedQuantityLY = compareData.DiscountedQuantity;
                                result.SalesAmountLY = compareData.SalesAmount;
                                result.AverageUnitPriceLY = compareData.Quantity > 0 ? compareData.SalesAmount / compareData.Quantity : 0;
                                result.AverageOriginalPriceLY = compareData.Quantity > 0
                                    ? compareData.TotalOriginalAmount / compareData.Quantity
                                    : null;
                                result.OrderCountLY = compareData.OrderCount;
                            }

                            return result;
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
                        OrderCount = (int)s.OrderCount,
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
                            item.OrderCountLY = (int)compareItem.OrderCount;
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
            int topN = 100,
            List<string>? branchCodes = null
        )
        {
            try
            {
                ValidateDateRange(dateRange);

                var compareStartStr = dateRange.CompareStartDate?.ToString("yyyyMMdd") ?? "null";
                var compareEndStr = dateRange.CompareEndDate?.ToString("yyyyMMdd") ?? "null";
                var cacheKey =
                    $"ExecutiveBranchPerformance_{dateRange.StartDate:yyyyMMdd}_{dateRange.EndDate:yyyyMMdd}_{compareStartStr}_{compareEndStr}_{topN}_{string.Join(",", branchCodes ?? new List<string>())}";

                if (
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

                // 获取所有分店
                var stores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => !s.IsDeleted)
                    .Select(s => new { s.StoreCode, s.StoreName })
                    .ToListAsync();

                var storeDict = stores.ToDictionary(
                    s => s.StoreCode ?? string.Empty,
                    s => s.StoreName ?? s.StoreCode ?? string.Empty
                );

                // 查询当前期分店销售数据
                var currentQuery = _context
                    .Db.Queryable<DailySalesStatistic>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    // 需要从 StoreSalesStatistic 表获取分店级别的数据
                    currentQuery = null!;
                }

                // 使用 StoreSalesStatistic 表查询分店销售数据
                var branchCurrentQuery = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    branchCurrentQuery = branchCurrentQuery.Where(s =>
                        branchCodes.Contains(s.BranchCode)
                    );
                }

                var currentData = await branchCurrentQuery
                    .GroupBy(s => s.BranchCode)
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        Revenue = SqlFunc.AggregateSum(s.TotalAmount),
                        OrderCount = SqlFunc.AggregateSum(s.OrderCount),
                        Aov = SqlFunc.AggregateSum(s.TotalAmount)
                            / SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();

                var lyStartDate = dateRange.CompareStartDate;
                var lyEndDate = dateRange.CompareEndDate;

                var lyQuery = _context
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => s.Date >= lyStartDate && s.Date <= lyEndDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    lyQuery = lyQuery.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var lyData = await lyQuery
                    .GroupBy(s => s.BranchCode)
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        RevenueLY = SqlFunc.AggregateSum(s.TotalAmount),
                        OrderCountLY = SqlFunc.AggregateSum(s.OrderCount),
                        AovLY = SqlFunc.AggregateSum(s.TotalAmount)
                            / SqlFunc.AggregateSum(s.OrderCount),
                    })
                    .ToListAsync();

                var lyDict = lyData.ToDictionary(
                    s => s.BranchCode,
                    s => new
                    {
                        s.RevenueLY,
                        s.OrderCountLY,
                        s.AovLY,
                    }
                );

                // 构建结果并排序
                var result = currentData
                    .Select(
                        (item, index) =>
                            new ExecutiveBranchPerformanceDto
                            {
                                Rank = index + 1,
                                BranchCode = item.BranchCode,
                                BranchName = storeDict.TryGetValue(
                                    item.BranchCode,
                                    out var branchName
                                )
                                    ? branchName
                                    : item.BranchCode,
                                Revenue = item.Revenue,
                                RevenueLY = lyDict.TryGetValue(item.BranchCode, out var lyItem)
                                    ? lyItem.RevenueLY
                                    : 0,
                                OrderCount = item.OrderCount,
                                OrderCountLY = lyDict.TryGetValue(item.BranchCode, out var lyItem2)
                                    ? lyItem2.OrderCountLY
                                    : 0,
                                Aov = item.Aov,
                                AovLY = lyDict.TryGetValue(item.BranchCode, out var lyItem3)
                                    ? lyItem3.AovLY
                                    : 0,
                            }
                    )
                    .OrderByDescending(x => x.Revenue)
                    .Take(topN)
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

                _cache.Set(cacheKey, result, cacheOptions);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExecutiveBranchPerformanceAsync failed");
                return new List<ExecutiveBranchPerformanceDto>();
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

                var compareStartStr = dateRange.CompareStartDate?.ToString("yyyyMMdd") ?? "null";
                var compareEndStr = dateRange.CompareEndDate?.ToString("yyyyMMdd") ?? "null";
                var cacheKey =
                    $"ExecutiveHourlyTraffic_{dateRange.StartDate:yyyyMMdd}_{dateRange.EndDate:yyyyMMdd}_{compareStartStr}_{compareEndStr}_{string.Join(",", branchCodes ?? new List<string>())}";

                if (
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
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var hourlyData = await query
                    .GroupBy(s => new { s.BranchCode, s.Hour })
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        Hour = s.Hour,
                        Revenue = SqlFunc.AggregateSum(s.TotalAmount),
                    })
                    .OrderBy(s => s.BranchCode)
                    .ToListAsync()
                    .ContinueWith(t => t.Result.OrderBy(x => x.Hour).ToList());

                var branchCodeSet = hourlyData.Select(h => h.BranchCode).Distinct().ToHashSet();
                var storeMap = await GetStoreNameMapAsync(branchCodeSet);

                var lyStartDate = dateRange.CompareStartDate;
                var lyEndDate = dateRange.CompareEndDate;

                var lyQuery = _context
                    .Db.Queryable<HourlySalesStatistic>()
                    .Where(s => s.Date >= lyStartDate && s.Date <= lyEndDate);

                if (branchCodes != null && branchCodes.Any())
                {
                    lyQuery = lyQuery.Where(s => branchCodes.Contains(s.BranchCode));
                }

                var lyHourlyData = await lyQuery
                    .GroupBy(s => new { s.BranchCode, s.Hour })
                    .Select(s => new
                    {
                        BranchCode = s.BranchCode,
                        Hour = s.Hour,
                        RevenueLY = SqlFunc.AggregateSum(s.TotalAmount),
                    })
                    .ToListAsync();

                var lyDict = lyHourlyData.ToDictionary(
                    s => (s.BranchCode, s.Hour),
                    s => s.RevenueLY
                );

                var result = hourlyData
                    .GroupBy(h => h.BranchCode)
                    .Select(branchGroup =>
                    {
                        var branchCode = branchGroup.Key ?? string.Empty;
                        var branchName = storeMap.TryGetValue(branchCode, out var name)
                            ? name
                            : branchCode;
                        var branchMaxRevenue = branchGroup.Max(x => x.Revenue);
                        var peakThreshold = branchMaxRevenue * 0.8m;

                        return branchGroup.Select(item => new ExecutiveHourlyTrafficDto
                        {
                            Hour = $"{item.Hour:D2}:00",
                            BranchCode = branchCode,
                            BranchName = branchName,
                            Revenue = item.Revenue,
                            RevenueLY = lyDict.TryGetValue(
                                (branchCode, item.Hour),
                                out var lyRevenue
                            )
                                ? lyRevenue
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

                _cache.Set(cacheKey, result, cacheOptions);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExecutiveHourlyTrafficAsync failed");
                return new List<ExecutiveHourlyTrafficDto>();
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

                try
                {
                    var statisticResult = await GetBestSellersFromStatisticsAsync(
                        dateRange,
                        branchCodes,
                        pageIndex,
                        pageSize
                    );
                    if (statisticResult != null)
                    {
                        return statisticResult;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[BestSellers] Statistic query failed, falling back to POSM");
                    var fallbackResult = await GetBestSellersFromPosmFallbackAsync(
                        dateRange,
                        branchCodes,
                        pageIndex,
                        pageSize,
                        SalesStatisticRefreshStatus.Failed,
                        $"商品统计查询失败，已使用 POSM 实时回退数据: {ex.Message}"
                    );
                    return fallbackResult;
                }

                var result = await GetBestSellersFromPosmFallbackAsync(
                    dateRange,
                    branchCodes,
                    pageIndex,
                    pageSize,
                    SalesStatisticRefreshStatus.Pending,
                    "商品统计表未就绪，已使用 POSM 实时回退数据。"
                );

                _logger.LogInformation(
                    "[BestSellers] Found {Total} products, returning {Count} for page {Page}",
                    result.Total, result.Products.Count, pageIndex
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BestSellers] GetBestSellersAsync failed");
                throw;
            }
        }

        private async Task<BestSellerResponseDto?> GetBestSellersFromStatisticsAsync(
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
                    "[BestSellers] Product statistic status failed, fallback to POSM: {Message}",
                    statisticStatus.Message
                );
                return await GetBestSellersFromPosmFallbackAsync(
                    dateRange,
                    branchCodes,
                    pageIndex,
                    pageSize,
                    SalesStatisticRefreshStatus.Failed,
                    statisticStatus.Message ?? "商品统计状态失败，已使用 POSM 实时回退数据。"
                );
            }

            if (statisticStatus.Status != SalesStatisticRefreshStatus.Fresh)
            {
                // 非 Fresh 说明日期范围不完整或水位过期，不能把部分统计当完整排名展示。
                return await GetBestSellersFromPosmFallbackAsync(
                    dateRange,
                    branchCodes,
                    pageIndex,
                    pageSize,
                    statisticStatus.Status,
                    statisticStatus.Message ?? "商品统计未完整生成，已使用 POSM 实时回退数据。"
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
                return null;

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
                productCodes => GetPosmFallbackInfoFromStatisticsAsync(startDate, endDate, branchCodes, productCodes)
            );
        }

        private async Task<BestSellerResponseDto> GetBestSellersFromPosmFallbackAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            int pageIndex,
            int pageSize,
            string statisticStatus,
            string statisticMessage
        )
        {
            var startDate = dateRange.StartDate.Date;
            var endExclusive = dateRange.EndDate.Date.AddDays(1);
            var salesQuery = _posmContext.Db.Queryable<SalesOrderDetail>()
                .LeftJoin<SalesOrder>((d, o) => d.OrderGuid == o.OrderGuid)
                .Where((d, o) => o.Status == 1)
                .Where((d, o) => d.SupplierCode == "200")
                .Where((d, o) => o.OrderTime >= startDate && o.OrderTime < endExclusive);

            if (branchCodes != null && branchCodes.Any())
            {
                var allowedDirectOrderGuids = await _posmContext.Db.Queryable<SalesOrder>()
                    .Where(o =>
                        o.Status == 1
                        && o.OrderTime >= startDate
                        && o.OrderTime < endExclusive
                        && o.BranchCode != null
                        && branchCodes.Contains(o.BranchCode)
                    )
                    .Select(o => o.OrderGuid)
                    .ToListAsync();
                var deviceMappedOrderGuids = await GetDeviceMappedOrderGuidsAsync(startDate, endExclusive, branchCodes);
                var allowedOrderGuids = allowedDirectOrderGuids
                    .Concat(deviceMappedOrderGuids)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();

                if (!allowedOrderGuids.Any())
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

                // POSM 回退也按“订单分店 + 设备分店”解析权限范围，避免 BranchCode 为空时漏单。
                salesQuery = salesQuery.Where((d, o) => allowedOrderGuids.Contains(o.OrderGuid));
            }

            // POSM 回退只做最稳定的商品聚合分页，不在 SQL 内做设备分店 fallback 或字符串聚合。
            var groupedQuery = salesQuery
                .GroupBy((d, o) => d.ProductCode)
                .Select((d, o) => new BestSellerAggregateRow
                {
                    ProductCode = d.ProductCode,
                    TotalQuantity = SqlFunc.AggregateSum(d.Quantity) ?? 0,
                    TotalSalesAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                });
            var total = await groupedQuery.Clone().CountAsync();
            var pagedData = await salesQuery.Clone()
                .GroupBy((d, o) => d.ProductCode)
                .OrderBy((d, o) => SqlFunc.AggregateSum(d.Quantity), OrderByType.Desc)
                .Select((d, o) => new BestSellerAggregateRow
                {
                    ProductCode = d.ProductCode,
                    TotalQuantity = SqlFunc.AggregateSum(d.Quantity) ?? 0,
                    TotalSalesAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                })
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return await BuildBestSellerResponseAsync(
                pagedData,
                total,
                pageIndex,
                pageSize,
                statisticStatus,
                statisticMessage,
                productCodes => GetBranchSalesFromPosmAsync(dateRange, branchCodes, productCodes),
                productCodes => GetPosmFallbackInfoFromPosmAsync(dateRange, productCodes)
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
            Func<List<string>, Task<Dictionary<string, (string? Barcode, string? ProductName)>>> loadFallbackInfo
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
            var fallbackInfo = await loadFallbackInfo(productCodes);
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
                fallbackInfo.TryGetValue(item.ProductCode, out var posmInfo);
                var branchSales = branchSalesMap.TryGetValue(item.ProductCode, out var rows)
                    ? rows
                    : new List<BestSellerBranchSaleDto>();
                return new BestSellerProductDto
                {
                    ProductCode = item.ProductCode,
                    ItemNumber = productInfo?.ItemNumber,
                    Barcode = !string.IsNullOrWhiteSpace(productInfo?.Barcode)
                        ? productInfo.Barcode
                        : posmInfo.Barcode,
                    ProductImage = productInfo?.ProductImage,
                    ProductName = !string.IsNullOrWhiteSpace(productInfo?.ProductName)
                        ? productInfo.ProductName
                        : posmInfo.ProductName,
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
                .Select(s => new
                {
                    s.ProductCode,
                    s.BranchCode,
                    s.TotalQuantity,
                    s.TotalAmount,
                    s.TotalCost,
                    s.GrossProfit,
                    s.CostSource,
                })
                .ToListAsync();

            // 分店销量聚合：成本来源需要保留真实来源分布，避免 SQL 字符串聚合误导。
            return rows
                .GroupBy(s => new { s.ProductCode, s.BranchCode })
                .Select(group => new BestSellerBranchAggregateRow
                {
                    ProductCode = group.Key.ProductCode,
                    BranchCode = group.Key.BranchCode,
                    Quantity = group.Sum(x => x.TotalQuantity),
                    SalesAmount = group.Sum(x => x.TotalAmount),
                    TotalCost = group.Any(x => x.TotalCost.HasValue) ? group.Sum(x => x.TotalCost ?? 0m) : null,
                    GrossProfit = group.Any(x => x.GrossProfit.HasValue) ? group.Sum(x => x.GrossProfit ?? 0m) : null,
                    CostSource = ResolveCostSource(group.Select(x => x.CostSource)),
                })
                .ToList();
        }

        private async Task<List<string>> GetDeviceMappedOrderGuidsAsync(
            DateTime startDate,
            DateTime endExclusive,
            List<string> branchCodes
        )
        {
            var branchSet = branchCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();
            if (!branchSet.Any())
                return new List<string>();

            var deviceCodes = await _posmContext.Db.Queryable<POSM_设备注册信息表>()
                .Where(d => branchSet.Contains(d.分店代码))
                .Select(d => d.系统设备编号)
                .ToListAsync();
            deviceCodes = deviceCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();
            if (!deviceCodes.Any())
                return new List<string>();

            return await _posmContext.Db.Queryable<SalesOrder>()
                .Where(o =>
                    o.Status == 1
                    && o.OrderTime >= startDate
                    && o.OrderTime < endExclusive
                    && (o.BranchCode == null || o.BranchCode == "")
                    && o.DeviceCode != null
                    && deviceCodes.Contains(o.DeviceCode)
                )
                .Select(o => o.OrderGuid)
                .ToListAsync();
        }

        private async Task<Dictionary<string, (string? Barcode, string? ProductName)>> GetPosmFallbackInfoFromStatisticsAsync(
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

        private async Task<List<BestSellerBranchAggregateRow>> GetBranchSalesFromPosmAsync(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            List<string> productCodes
        )
        {
            if (!productCodes.Any())
                return new List<BestSellerBranchAggregateRow>();

            var startDate = dateRange.StartDate.Date;
            var endExclusive = dateRange.EndDate.Date.AddDays(1);
            var rawRows = await _posmContext.Db.Queryable<SalesOrderDetail>()
                .LeftJoin<SalesOrder>((d, o) => d.OrderGuid == o.OrderGuid)
                .Where((d, o) =>
                    o.Status == 1
                    && d.SupplierCode == "200"
                    && o.OrderTime >= startDate
                    && o.OrderTime < endExclusive
                    && productCodes.Contains(d.ProductCode)
                )
                .Select((d, o) => new
                {
                    d.ProductCode,
                    o.BranchCode,
                    o.DeviceCode,
                    Quantity = d.Quantity ?? 0,
                    SalesAmount = d.ActualAmount ?? 0m,
                })
                .ToListAsync();

            var deviceCodes = rawRows
                .Where(x => string.IsNullOrWhiteSpace(x.BranchCode) && !string.IsNullOrWhiteSpace(x.DeviceCode))
                .Select(x => x.DeviceCode!)
                .Distinct()
                .ToList();
            var deviceBranchMap = deviceCodes.Any()
                ? (await _posmContext.Db.Queryable<POSM_设备注册信息表>()
                    .Where(d => deviceCodes.Contains(d.系统设备编号))
                    .Select(d => new { d.系统设备编号, d.分店代码 })
                    .ToListAsync())
                    .Where(x => !string.IsNullOrWhiteSpace(x.系统设备编号))
                    .GroupBy(x => x.系统设备编号)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Select(row => row.分店代码).FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)) ?? string.Empty
                    )
                : new Dictionary<string, string>();

            // 分店销量聚合：POSM 回退路径在内存中解析设备分店，避免复杂 SQL 翻译失败。
            return rawRows
                .Select(x => new
                {
                    x.ProductCode,
                    BranchCode = ResolveBranchCode(x.BranchCode, x.DeviceCode, deviceBranchMap),
                    x.Quantity,
                    x.SalesAmount,
                })
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.ProductCode)
                    && !string.IsNullOrWhiteSpace(x.BranchCode)
                    && (branchCodes == null || !branchCodes.Any() || branchCodes.Contains(x.BranchCode))
                )
                .GroupBy(x => new { x.ProductCode, x.BranchCode })
                .Select(group => new BestSellerBranchAggregateRow
                {
                    ProductCode = group.Key.ProductCode,
                    BranchCode = group.Key.BranchCode,
                    Quantity = group.Sum(x => x.Quantity),
                    SalesAmount = group.Sum(x => x.SalesAmount),
                })
                .ToList();
        }

        private async Task<Dictionary<string, (string? Barcode, string? ProductName)>> GetPosmFallbackInfoFromPosmAsync(
            DateRangeDto dateRange,
            List<string> productCodes
        )
        {
            if (!productCodes.Any())
                return new Dictionary<string, (string? Barcode, string? ProductName)>();

            var startDate = dateRange.StartDate.Date;
            var endExclusive = dateRange.EndDate.Date.AddDays(1);
            var rows = await _posmContext.Db.Queryable<SalesOrderDetail>()
                .LeftJoin<SalesOrder>((d, o) => d.OrderGuid == o.OrderGuid)
                .Where((d, o) =>
                    o.Status == 1
                    && d.SupplierCode == "200"
                    && o.OrderTime >= startDate
                    && o.OrderTime < endExclusive
                    && productCodes.Contains(d.ProductCode)
                )
                .Select((d, o) => new { d.ProductCode, d.Barcode, d.ProductName })
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

            if (states.Any(s => s.Status == SalesStatisticRefreshStatus.Stale))
                return (SalesStatisticRefreshStatus.Stale, "商品统计正在等待延迟上传数据补算。");

            if (states.Any(s => s.Status == SalesStatisticRefreshStatus.Pending))
                return (SalesStatisticRefreshStatus.Pending, "商品统计正在生成中。");

            var expectedDays = (int)(endDate - startDate).TotalDays + 1;
            if (states.Select(s => s.Date.Date).Distinct().Count() < expectedDays)
                return (SalesStatisticRefreshStatus.Pending, "日期范围内仍有商品统计未生成。");

            // 上传水位检查：POSM 源数据推进后，旧统计先标记 Stale，前台走实时回退。
            var currentSourceUploadTime = await _posmContext.Db.Queryable<SalesOrder>()
                .Where(o =>
                    o.Status != null
                    && (o.Status == 1 || o.Status == 4)
                    && o.OrderTime != null
                    && o.OrderTime >= startDate
                    && o.OrderTime < endDate.AddDays(1)
                )
                .MaxAsync(o => o.LastUploadTime);
            var recordedSourceUploadTime = states
                .Where(s => s.LastSourceUploadTime.HasValue)
                .Select(s => s.LastSourceUploadTime!.Value)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            if (
                states.All(s => s.LastSourceUploadTime.HasValue)
                && currentSourceUploadTime.HasValue
                && currentSourceUploadTime.Value > recordedSourceUploadTime
            )
            {
                return (SalesStatisticRefreshStatus.Stale, "POSM 已上传新销售数据，商品统计等待补算。");
            }

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

        private static string ResolveBranchCode(
            string? branchCode,
            string? deviceCode,
            Dictionary<string, string> deviceBranchMap
        )
        {
            if (!string.IsNullOrWhiteSpace(branchCode))
                return branchCode;

            if (!string.IsNullOrWhiteSpace(deviceCode) && deviceBranchMap.TryGetValue(deviceCode, out var mappedBranch))
                return mappedBranch ?? string.Empty;

            return string.Empty;
        }
    }
}
