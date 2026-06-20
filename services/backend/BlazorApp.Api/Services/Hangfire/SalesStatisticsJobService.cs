using System.Runtime.ExceptionServices;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 批量统计更新结果
    /// </summary>
    public class BatchStatisticsUpdateResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 总天数
        /// </summary>
        public int TotalDays { get; set; }

        /// <summary>
        /// 已处理天数
        /// </summary>
        public int ProcessedDays { get; set; }

        /// <summary>
        /// 失败日期列表
        /// </summary>
        public List<string> FailedDates { get; set; } = new();

        /// <summary>
        /// 结果消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 总月数
        /// </summary>
        public int TotalMonths { get; set; }

        /// <summary>
        /// 已处理月数
        /// </summary>
        public int ProcessedMonths { get; set; }

        /// <summary>
        /// 失败月份列表
        /// </summary>
        public List<string> FailedMonths { get; set; } = new();

        /// <summary>
        /// 任务ID
        /// </summary>
        public Guid TaskId { get; set; }
    }

    public class ProductStoreDailyRecalculationSubmitResult
    {
        public Guid JobId { get; set; }
        public List<DateTime> SubmittedDates { get; set; } = new();
        public List<DateTime> SkippedDates { get; set; } = new();
        public string Status { get; set; } = SalesStatisticRefreshStatus.Queued;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 日期范围
    /// </summary>
    public class DateRange
    {
        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// 天数
        /// </summary>
        public int DayCount => (int)(EndDate - StartDate).TotalDays + 1;
    }

    /// <summary>
    /// 销售统计作业服务
    /// 负责从POSM系统获取销售数据并生成各种维度的统计报表
    /// </summary>
    public class SalesStatisticsJobService
    {
        /// <summary>
        /// 商品统计提交串行锁，避免同一实例内并发请求重复提交相同日期。
        /// </summary>
        private static readonly SemaphoreSlim ProductStatisticSubmitLock = new(1, 1);

        private sealed class StoreCostRow
        {
            public string? StoreCode { get; set; }
            public string? SupplierCode { get; set; }
            public string? ProductCode { get; set; }
            public decimal? PurchasePrice { get; set; }
        }

        private sealed class ProductCostRow
        {
            public string? ProductCode { get; set; }
            public decimal? PurchasePrice { get; set; }
        }

        private sealed class WarehouseCostRow
        {
            public string ProductCode { get; set; } = string.Empty;
            public decimal? ImportPrice { get; set; }
        }

        private sealed class ProductStatisticDiagnosticRow
        {
            public string BranchCode { get; set; } = string.Empty;
            public decimal UnmatchedSupplierAmount { get; set; }
            public int UnmatchedSupplierQuantity { get; set; }
            public int UnmatchedSupplierProductCount { get; set; }
        }

        private sealed class ProductStatisticDiagnostics
        {
            public decimal UnmatchedSupplierAmount { get; set; }
            public int UnmatchedSupplierQuantity { get; set; }
            public int UnmatchedSupplierProductCount { get; set; }
            public Dictionary<string, ProductStatisticDiagnosticRow> BranchDiagnostics { get; set; } =
                new();
        }

        private sealed class StoreStatisticPaymentRow
        {
            public string? OrderGuid { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
            public decimal Amount { get; set; }
        }

        private sealed class StoreStatisticQuantityRow
        {
            public string? OrderGuid { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
            public int Quantity { get; set; }
        }

        private sealed class StoreStatisticOrderRow
        {
            public string? OrderGuid { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
        }

        /// <summary>
        /// 批量操作每批处理数量
        /// </summary>
        private const int BatchSize = 5000;

        /// <summary>
        /// 数据库命令超时时间（秒）
        /// </summary>
        private const int CommandTimeoutSeconds = 1800;

        /// <summary>
        /// POSM数据库上下文
        /// </summary>
        private readonly POSMSqlSugarContext _posmContext;

        /// <summary>
        /// 主数据库上下文
        /// </summary>
        private readonly SqlSugarContext _context;

        /// <summary>
        /// 日志记录器
        /// </summary>
        private readonly ILogger<SalesStatisticsJobService> _logger;

        /// <summary>
        /// 配置服务
        /// </summary>
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 服务作用域工厂（用于并发时创建独立的数据库上下文）
        /// </summary>
        private readonly IServiceScopeFactory _serviceScopeFactory;

        /// <summary>
        /// 最大并发更新数
        /// </summary>
        private readonly int _maxConcurrentUpdates;

        /// <summary>
        /// 并发更新支持的最大天数
        /// </summary>
        private readonly int _maxDaysForConcurrentUpdate;

        /// <summary>
        /// 每个并发块包含的最大天数
        /// </summary>
        private readonly int _maxDaysPerChunk;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="context">主数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="configuration">配置服务</param>
        /// <param name="serviceScopeFactory">服务作用域工厂</param>
        public SalesStatisticsJobService(
            POSMSqlSugarContext posmContext,
            SqlSugarContext context,
            ILogger<SalesStatisticsJobService> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory
        )
        {
            _posmContext = posmContext;
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _serviceScopeFactory = serviceScopeFactory;
            // 从配置中读取最大并发更新数，默认值为5
            _maxConcurrentUpdates = _configuration.GetValue<int>(
                "ScheduledTasks:MaxConcurrentUpdates",
                5
            );
            // 从配置中读取并发更新支持的最大天数，默认值为365
            _maxDaysForConcurrentUpdate = _configuration.GetValue<int>(
                "ScheduledTasks:MaxDaysForConcurrentUpdate",
                365
            );
            // 从配置中读取每个并发块包含的最大天数，默认值为7
            _maxDaysPerChunk = _configuration.GetValue<int>("ScheduledTasks:MaxDaysPerChunk", 7);
        }

        /// <summary>
        /// 统一执行统计事务，并在提交/回滚失败时保留最原始的业务异常
        /// </summary>
        private static async Task ExecuteTransactionSafelyAsync(
            Func<Task> beginAsync,
            Func<Task> workAsync,
            Func<Task> commitAsync,
            Func<Task> rollbackAsync,
            ILogger logger,
            string operationName
        )
        {
            await beginAsync();

            Exception? originalException = null;

            try
            {
                await workAsync();

                try
                {
                    await commitAsync();
                }
                catch (Exception commitException)
                {
                    // 提交失败时，提交异常本身就是最重要的原始异常，后续回滚失败不能覆盖它。
                    originalException = commitException;
                    logger.LogError(commitException, "{OperationName} 提交事务失败，准备尝试回滚", operationName);
                    throw;
                }
            }
            catch (Exception ex)
            {
                originalException ??= ex;

                try
                {
                    await rollbackAsync();
                }
                catch (Exception rollbackException)
                {
                    logger.LogError(
                        rollbackException,
                        "{OperationName} 回滚事务失败，将保留原始异常继续抛出",
                        operationName
                    );
                }

                ExceptionDispatchInfo.Capture(originalException).Throw();
                throw;
            }
        }

        /// <summary>
        /// 更新当前小时统计数据
        /// 包括分时统计、每日统计、分店统计、澳洲供应商门店统计、中国供应商门店统计
        /// </summary>
        public async Task UpdateCurrentHourStatistics()
        {
            try
            {
                // 获取当前时间
                var now = DateTime.Now;
                var currentHour = now.Hour;
                var currentDate = now.Date;

                _logger.LogInformation(
                    "开始更新当前小时统计数据: {Date} {Hour}",
                    currentDate,
                    currentHour
                );

                // 更新分时统计数据
                await UpdateHourlyStatistics(currentDate, currentHour);
                // 更新每日统计数据
                await UpdateDailyStatistics(currentDate.ToString("yyyy-MM-dd"));
                // 更新分店统计数据
                await UpdateStoreStatistics(currentDate);
                // await UpdateSupplierStatistics(currentDate);
                // await UpdateStoreSupplierStatistics(currentDate);
                // 更新澳洲供应商门店统计数据
                await UpdateAustralianSupplierStoreStatistics(currentDate);
                // 更新中国供应商门店统计数据
                await UpdateChinaSupplierStoreStatistics(currentDate);

                _logger.LogInformation(
                    "当前小时统计数据更新完成: {Date} {Hour}",
                    currentDate,
                    currentHour
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新当前小时统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 更新每日统计数据
        /// 从POSM系统聚合当日销售订单的汇总数据
        /// </summary>
        /// <param name="dateStr">日期字符串（格式yyyy-MM-dd），为空则更新当天</param>
        public async Task UpdateDailyStatistics(string? dateStr = null)
        {
            try
            {
                // 确定目标日期
                var date = string.IsNullOrEmpty(dateStr)
                    ? DateTime.Now.Date
                    : DateTime.Parse(dateStr).Date;

                _logger.LogInformation("开始更新每日统计数据: {Date}", date);

                // 从POSM数据库查询并聚合当日销售数据，按支付明细统计营业额
                var summary = await _posmContext
                    .Db.Queryable<PaymentDetail, SalesOrder>(
                        (pd, so) => pd.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (pd, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime.Value.Date == date
                    )
                    .GroupBy((pd, so) => new { Date = so.OrderTime!.Value.Date })
                    .Select(
                        (pd, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                // 按支付明细统计总金额
                                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                                // 计算总数量
                                TotalQuantity = SqlFunc.AggregateSum(so.ItemCount) ?? 0,
                                // 计算订单数
                                OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                                // 计算SKU数（这里暂时用订单数代替）
                                SkuCount = SqlFunc.AggregateCount(so.OrderGuid),
                                // 计算客户数（这里暂时用订单数代替）
                                CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                            }
                    )
                    .FirstAsync();

                if (summary != null)
                {
                    // 构建统计数据对象
                    var statistic = new DailySalesStatistic
                    {
                        Date = summary.Date,
                        TotalAmount = summary.TotalAmount,
                        TotalQuantity = summary.TotalQuantity,
                        OrderCount = summary.OrderCount,
                        SkuCount = summary.SkuCount,
                        CustomerCount = summary.CustomerCount,
                        // 计算平均订单价值
                        AverageOrderValue =
                            summary.OrderCount > 0 ? summary.TotalAmount / summary.OrderCount : 0m,
                        UpdateTime = DateTime.Now,
                    };

                    // 查询是否已存在该日期的统计数据
                    var existing = await _context
                        .Db.Queryable<DailySalesStatistic>()
                        .Where(s => s.Date == date)
                        .FirstAsync();

                    if (existing != null)
                    {
                        // 存在则更新
                        await _context.Db.Updateable(statistic).ExecuteCommandAsync();
                    }
                    else
                    {
                        // 不存在则插入
                        await _context.Db.Insertable(statistic).ExecuteCommandAsync();
                    }
                }

                _logger.LogInformation("每日统计数据更新完成: {Date}", date);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新每日统计数据失败: {Date}", dateStr);
                throw;
            }
        }

        /// <summary>
        /// 更新分时统计数据
        /// 按小时和分店维度聚合销售数据，包含全店汇总记录，按支付明细统计营业额
        /// </summary>
        /// <param name="date">目标日期</param>
        /// <param name="hour">指定小时，为空则更新全天24小时</param>
        public async Task UpdateHourlyStatistics(DateTime date, int? hour = null)
        {
            try
            {
                // 确定要更新的小时列表
                var targetHours = hour.HasValue
                    ? new[] { hour.Value }
                    : Enumerable.Range(0, 24).ToArray();

                _logger.LogInformation(
                    "开始更新分时统计数据: {Date}, 小时: {Hours}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23"
                );

                // 从POSM数据库查询分时销售数据，按支付明细统计营业额
                var allHourlyData = await _posmContext
                    .Db.Queryable<PaymentDetail, SalesOrder>(
                        (pd, so) => pd.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (pd, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime.Value.Date == date
                            && targetHours.Contains(so.OrderTime.Value.Hour)
                    )
                    .GroupBy(
                        (pd, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                so.BranchCode,
                            }
                    )
                    .Select(
                        (pd, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(so.ItemCount) ?? 0,
                                OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                                CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                            }
                    )
                    .ToListAsync();

                if (!allHourlyData.Any())
                {
                    _logger.LogInformation("没有找到销售数据: {Date}", date);
                    return;
                }

                // 获取所有分店代码
                var branchCodes = allHourlyData
                    .Select(d => d.BranchCode)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .ToList();

                // 查询分店信息
                var stores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => branchCodes.Contains(s.StoreCode))
                    .ToListAsync();

                var storeDict = stores.ToDictionary(s => s.StoreCode, s => s);

                var statisticsList = new List<HourlySalesStatistic>();

                // 为每个小时创建全店汇总记录
                foreach (var h in targetHours)
                {
                    var hourlyDataForHour = allHourlyData.Where(d => d.Hour == h).ToList();

                    if (hourlyDataForHour.Any())
                    {
                        var allStoreData = new HourlySalesStatistic
                        {
                            Date = date,
                            Hour = h,
                            BranchCode = "ALL",
                            BranchName = "All Stores",
                            TotalAmount = hourlyDataForHour.Sum(d => d.TotalAmount),
                            TotalQuantity = (int)hourlyDataForHour.Sum(d => d.TotalQuantity),
                            CustomerCount = hourlyDataForHour.Sum(d => d.CustomerCount),
                            AverageOrderValue =
                                hourlyDataForHour.Sum(d => d.OrderCount) > 0
                                    ? hourlyDataForHour.Sum(d => d.TotalAmount)
                                        / hourlyDataForHour.Sum(d => d.OrderCount)
                                    : 0m,
                            UpdateTime = DateTime.Now,
                        };
                        statisticsList.Add(allStoreData);
                    }
                }

                LogSkippedBranchCodeRows(
                    "分时分店销售统计",
                    allHourlyData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 为每个分店创建分时统计记录
                foreach (var data in allHourlyData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var store = storeDict.GetValueOrDefault(branchCode);

                    var storeStatistic = new HourlySalesStatistic
                    {
                        Date = data.Date,
                        Hour = data.Hour,
                        BranchCode = branchCode,
                        BranchName = store?.StoreName ?? branchCode,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        CustomerCount = data.CustomerCount,
                        AverageOrderValue =
                            data.OrderCount > 0 ? data.TotalAmount / data.OrderCount : 0m,
                        UpdateTime = DateTime.Now,
                    };
                    statisticsList.Add(storeStatistic);
                }

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 删除指定日期和小时的旧记录
                        var deletedCount = await _context
                            .Db.Deleteable<HourlySalesStatistic>()
                            .Where(s => s.Date == date && targetHours.Contains(s.Hour))
                            .ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条分时统计旧记录", deletedCount);

                        // 批量插入新记录
                        _context
                            .Db.Fastest<HourlySalesStatistic>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                    },
                    commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                    logger: _logger,
                    operationName: "分时统计数据更新"
                );

                _logger.LogInformation(
                    "分时统计数据更新完成: {Date}, 小时: {Hours}, 总记录: {Total}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23",
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分时统计数据失败: {Date} {Hour}", date, hour);
                throw;
            }
        }

        /// <summary>
        /// 更新分店统计数据（所有分店）
        /// 按分店维度聚合销售数据，按支付明细统计营业额
        /// </summary>
        /// <param name="date">目标日期，为空则更新当天</param>
        public async Task UpdateStoreStatistics(DateTime? date = null)
        {
            try
            {
                var targetDate = date ?? DateTime.Now.Date;

                _logger.LogInformation("开始更新分店统计数据: {Date}", targetDate);

                var statisticsList = await BuildStoreStatisticsAsync(
                    _context,
                    _posmContext,
                    targetDate,
                    null
                );

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 删除该日期的所有旧记录
                        var deletedCount = await _context
                            .Db.Deleteable<StoreSalesStatistic>()
                            .Where(s => s.Date == targetDate)
                            .ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条分店统计旧记录", deletedCount);

                        // 批量插入新记录
                        if (statisticsList.Any())
                        {
                            _context
                                .Db.Fastest<StoreSalesStatistic>()
                                .PageSize(BatchSize)
                                .BulkCopy(statisticsList);
                        }
                    },
                    commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                    logger: _logger,
                    operationName: "分店统计数据更新"
                );

                _logger.LogInformation(
                    "分店统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 全量刷新前一天数据
        /// 刷新前一天的每日统计、分时统计、分店统计和供应商统计
        /// </summary>
        public async Task FullRefreshPreviousDay()
        {
            try
            {
                var previousDay = DateTime.Now.AddDays(-1).Date;

                _logger.LogInformation("开始全量刷新前一天数据: {Date}", previousDay);

                // 更新每日统计
                await UpdateDailyStatistics(previousDay.ToString("yyyy-MM-dd"));

                // 更新全天24小时的分时统计
                for (int hour = 0; hour < 24; hour++)
                {
                    await UpdateHourlyStatistics(previousDay, hour);
                }

                // 更新分店统计
                await UpdateStoreStatistics(previousDay);
                // 更新商品分店每日统计，热销页优先读取该统计表。
                await UpdateProductStoreDailyStatistics(previousDay);
                // await UpdateSupplierStatistics(previousDay);
                // await UpdateStoreSupplierStatistics(previousDay);
                // 更新澳洲供应商门店统计
                await UpdateAustralianSupplierStoreStatistics(previousDay);
                // 更新中国供应商门店统计
                await UpdateChinaSupplierStoreStatistics(previousDay);

                _logger.LogInformation("前一天数据全量刷新完成: {Date}", previousDay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "全量刷新前一天数据失败");
                throw;
            }
        }

        /// <summary>
        /// 全量刷新当天数据
        /// 刷新当天的每日统计、分时统计、分店统计和供应商统计
        /// </summary>
        public async Task FullRefreshCurrentDay()
        {
            try
            {
                var currentDay = DateTime.Now.Date;

                _logger.LogInformation("开始全量刷新当天数据: {Date}", currentDay);

                // 更新每日统计
                await UpdateDailyStatistics(currentDay.ToString("yyyy-MM-dd"));

                // 更新全天24小时的分时统计
                await UpdateHourlyStatistics(currentDay, null);

                // 更新分店统计
                await UpdateStoreStatistics(currentDay);
                // 更新商品分店每日统计，热销页优先读取该统计表。
                await UpdateProductStoreDailyStatistics(currentDay);
                // POSM 可能延迟上传，商品统计额外滚动补算最近 7 天。
                for (var offset = 1; offset < 7; offset++)
                {
                    await UpdateProductStoreDailyStatistics(currentDay.AddDays(-offset));
                }
                // await UpdateSupplierStatistics(currentDay);
                //  await UpdateStoreSupplierStatistics(currentDay);
                // 更新澳洲供应商门店统计
                await UpdateAustralianSupplierStoreStatistics(currentDay);
                // 更新中国供应商门店统计
                await UpdateChinaSupplierStoreStatistics(currentDay);

                _logger.LogInformation("当天数据全量刷新完成: {Date}", currentDay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "全量刷新当天数据失败");
                throw;
            }
        }

        /// <summary>
        /// 检查是否为国内供应商
        /// </summary>
        /// <param name="supplierCode">供应商代码</param>
        /// <returns>是否为国内供应商</returns>
        private async Task<bool> CheckIsDomesticSupplierAsync(string supplierCode)
        {
            try
            {
                // 查询中国供应商表
                var chinaSupplier = await _context.ChinaSupplierDb.GetFirstAsync(s =>
                    s.SupplierCode == supplierCode && !s.IsDeleted
                );

                if (chinaSupplier != null)
                {
                    return true;
                }

                // 查询国内产品表
                var domesticProduct = await _context
                    .Db.Queryable<DomesticProduct>()
                    .Where(dp => dp.SupplierCode == supplierCode && !dp.IsDeleted)
                    .FirstAsync();

                return domesticProduct != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 更新指定分店统计数据
        /// 可以指定分店代码列表，只更新这些分店的统计数据
        /// </summary>
        /// <param name="date">目标日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        public async Task UpdateStoreStatistics(DateTime date, List<string>? branchCodes = null)
        {
            try
            {
                _logger.LogInformation(
                    "开始更新指定分店统计数据: {Date}, Branches: {Branches}",
                    date,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All"
                );

                var statisticsList = await BuildStoreStatisticsAsync(
                    _context,
                    _posmContext,
                    date,
                    branchCodes
                );
                var targetBranchCodes = NormalizeBranchCodes(branchCodes);

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 指定分店重算只替换对应分店，避免清掉同日其它分店统计。
                        var deleteable = _context.Db.Deleteable<StoreSalesStatistic>()
                            .Where(s => s.Date == date);
                        if (targetBranchCodes.Any())
                        {
                            deleteable = deleteable.Where(s => targetBranchCodes.Contains(s.BranchCode));
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条分店统计旧记录", deletedCount);

                        // 批量插入新记录
                        if (statisticsList.Any())
                        {
                            _context
                                .Db.Fastest<StoreSalesStatistic>()
                                .PageSize(BatchSize)
                                .BulkCopy(statisticsList);
                        }
                    },
                    commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                    logger: _logger,
                    operationName: "指定分店统计数据更新"
                );

                _logger.LogInformation(
                    "指定分店统计数据更新完成: {Date}, 总记录: {Total}",
                    date,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新指定分店统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 更新供应商统计数据
        /// 支持本地供应商和国内供应商两种类型
        /// - 本地供应商：按LocalSupplierCode聚合
        /// - 国内供应商：LocalSupplierCode为"200"时，按ChinaSupplierCode聚合
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        public async Task UpdateSupplierStatistics(
            DateTime? startDate = null,
            DateTime? endDate = null,
            List<string>? supplierCodes = null
        )
        {
            try
            {
                var targetStartDate = startDate ?? DateTime.Now.Date;
                var targetEndDate = endDate ?? targetStartDate;

                if (targetStartDate > targetEndDate)
                {
                    throw new ArgumentException("开始日期不能大于结束日期");
                }

                var isSingleDate = targetStartDate == targetEndDate;

                _logger.LogInformation(
                    "开始更新指定供应商统计数据: {StartDate} 至 {EndDate}, Suppliers: {Suppliers}",
                    targetStartDate.ToString("yyyy-MM-dd"),
                    targetEndDate.ToString("yyyy-MM-dd"),
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                // 构建查询，关联销售订单、订单明细和产品供应商映射表
                var query = _posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(o =>
                        o.Status != null && (o.Status == 1 || o.Status == 4) && o.OrderTime != null
                    );

                // 设置日期过滤条件
                if (isSingleDate)
                {
                    query = query.Where(o => o.OrderTime!.Value.Date == targetStartDate);
                }
                else
                {
                    query = query.Where(o =>
                        o.OrderTime!.Value.Date >= targetStartDate
                        && o.OrderTime!.Value.Date <= targetEndDate
                    );
                }

                // 设置供应商代码过滤条件
                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            (m.LocalSupplierCode != null
                                && supplierCodes.Contains(m.LocalSupplierCode))
                            || (m.ChinaSupplierCode != null
                                && supplierCodes.Contains(m.ChinaSupplierCode))
                    );
                }

                // 获取原始销售数据
                var rawData = await query
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = d.ActualAmount,
                                Quantity = d.Quantity ?? 0m,
                                OrderGuid = o.OrderGuid,
                            }
                    )
                    .ToListAsync();

                // 1. 本地供应商聚合
                // 所有的销售记录都按 LocalSupplierCode 进行一次聚合
                // 即使 LocalSupplierCode 是 "200"（总代），也会作为一个普通的本地供应商参与统计
                // 这样可以得到每个本地供应商（包括总代）的总销量和总金额
                var localStats = rawData
                    .Where(x => !string.IsNullOrEmpty(x.LocalSupplierCode))
                    .GroupBy(x => new { x.Date, x.LocalSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.LocalSupplierCode,
                        IsDomestic = false,
                        TotalAmount = g.Sum(x => x.TotalAmount) ?? 0m,
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode).Distinct().Count(),
                        OrderCount = g.Select(x => x.OrderGuid).Distinct().Count(),
                        UpdateTime = DateTime.Now,
                    })
                    .ToList();

                // 2. 国内供应商聚合
                // 仅针对 LocalSupplierCode == "200" 的记录进行二次聚合
                // 这些记录如果包含 ChinaSupplierCode，则按 ChinaSupplierCode 再统计一次
                // 这样可以得到每个具体的国内供应商的销量数据
                // 这些数据的 IsDomestic 标记为 true
                var chinaStats = rawData
                    .Where(x =>
                        x.LocalSupplierCode == "200" && !string.IsNullOrEmpty(x.ChinaSupplierCode)
                    )
                    .GroupBy(x => new { x.Date, x.ChinaSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.ChinaSupplierCode ?? string.Empty,
                        IsDomestic = true,
                        TotalAmount = g.Sum(x => x.TotalAmount) ?? 0m,
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode).Distinct().Count(),
                        OrderCount = g.Select(x => x.OrderGuid).Distinct().Count(),
                        UpdateTime = DateTime.Now,
                    })
                    .ToList();

                // 合并本地供应商和国内供应商统计
                var allStats = localStats.Concat(chinaStats).ToList();

                // 获取供应商名称
                var allLocalCodes = localStats.Select(x => x.SupplierCode).Distinct().ToList();
                var allChinaCodes = chinaStats.Select(x => x.SupplierCode).Distinct().ToList();

                var supplierNameDict = new Dictionary<string, string>();

                // 查询本地供应商名称
                if (allLocalCodes.Any())
                {
                    var localSuppliers = await _context.HBLocalSupplierDb.GetListAsync(s =>
                        s.LocalSupplierCode != null
                        && allLocalCodes.Contains(s.LocalSupplierCode)
                        && !s.IsDeleted
                    );
                    foreach (var s in localSuppliers)
                    {
                        if (!string.IsNullOrEmpty(s.LocalSupplierCode))
                        {
                            supplierNameDict[s.LocalSupplierCode] = s.Name ?? s.LocalSupplierCode;
                        }
                    }
                }

                // 查询国内供应商名称
                if (allChinaCodes.Any())
                {
                    var chinaSuppliers = await _context.ChinaSupplierDb.GetListAsync(s =>
                        s.SupplierCode != null
                        && allChinaCodes.Contains(s.SupplierCode)
                        && !s.IsDeleted
                    );
                    foreach (var s in chinaSuppliers)
                    {
                        if (!string.IsNullOrEmpty(s.SupplierCode))
                        {
                            supplierNameDict[s.SupplierCode] = s.SupplierName ?? s.SupplierCode;
                        }
                    }
                }

                // 为统计记录填充供应商名称
                foreach (var stat in allStats)
                {
                    if (supplierNameDict.TryGetValue(stat.SupplierCode, out var name))
                    {
                        stat.SupplierName = name;
                    }
                    else
                    {
                        stat.SupplierName = stat.SupplierCode;
                    }
                }

                var allSupplierCodes = allStats.Select(s => s.SupplierCode).Distinct().ToList();

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 删除日期范围内的旧记录
                        var deletedCount = await _context
                            .Db.Deleteable<SupplierSalesStatistic>()
                            .Where(s => s.Date >= targetStartDate && s.Date <= targetEndDate)
                            .ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条供应商统计旧记录", deletedCount);

                        // 批量插入新记录
                        _context
                            .Db.Fastest<SupplierSalesStatistic>()
                            .PageSize(BatchSize)
                            .BulkCopy(allStats);
                    },
                    commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                    logger: _logger,
                    operationName: "指定供应商统计数据更新"
                );

                _logger.LogInformation(
                    "指定供应商统计数据更新完成: {StartDate} 至 {EndDate}, 总记录: {Total}",
                    targetStartDate.ToString("yyyy-MM-dd"),
                    targetEndDate.ToString("yyyy-MM-dd"),
                    allStats.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新指定供应商统计数据失败: {StartDate} 至 {EndDate}",
                    startDate?.ToString("yyyy-MM-dd"),
                    endDate?.ToString("yyyy-MM-dd")
                );
                throw;
            }
        }

        /// <summary>
        /// 更新商品分店每日统计数据，用于热销商品和毛利率查询。
        /// </summary>
        public async Task UpdateProductStoreDailyStatistics(DateTime? date = null)
        {
            var targetDate = (date ?? DateTime.Now.Date).Date;
            await UpdateProductStoreDailyStatisticsWithContext(
                _context,
                _posmContext,
                _logger,
                targetDate
            );
        }

        public async Task<ProductStoreDailyRecalculationSubmitResult> SubmitProductStoreDailyRecalculationAsync(
            IEnumerable<DateTime> dates,
            string? requestedBy,
            int maxConcurrency = 3
        )
        {
            var targetDates = dates
                .Select(date => date.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();
            var jobId = Guid.NewGuid();
            var normalizedMaxConcurrency = NormalizeProductStatisticMaxConcurrency(maxConcurrency);

            if (!targetDates.Any())
            {
                return new ProductStoreDailyRecalculationSubmitResult
                {
                    JobId = jobId,
                    Status = SalesStatisticRefreshStatus.Pending,
                    Message = "没有可提交的商品统计日期",
                };
            }

            List<DateTime> submittedDates;
            List<DateTime> skippedDates;
            await ProductStatisticSubmitLock.WaitAsync();
            try
            {
                var minDate = targetDates.Min();
                var maxDate = targetDates.Max();

                // 避免 DateTime 列表 Contains 在不同数据库方言下漏匹配，先按范围取回再按日期精确过滤。
                var existingStates = await _context.Db.Queryable<SalesStatisticRefreshState>()
                    .Where(s =>
                        s.StatisticType == SalesStatisticType.ProductStoreDaily
                        && s.Date >= minDate
                        && s.Date <= maxDate
                    )
                    .ToListAsync();
                existingStates = existingStates
                    .Where(s => targetDates.Contains(s.Date.Date))
                    .ToList();
                var runningDates = existingStates
                    .Where(s =>
                        s.Status == SalesStatisticRefreshStatus.Queued
                        || s.Status == SalesStatisticRefreshStatus.Running
                    )
                    .Select(s => s.Date.Date)
                    .ToHashSet();
                submittedDates = targetDates
                    .Where(date => !runningDates.Contains(date))
                    .ToList();
                skippedDates = targetDates
                    .Where(date => runningDates.Contains(date))
                    .ToList();

                foreach (var date in submittedDates)
                {
                    await UpsertProductStatisticQueuedStateAsync(
                        _context,
                        date,
                        jobId,
                        requestedBy
                    );
                }
            }
            finally
            {
                ProductStatisticSubmitLock.Release();
            }

            if (submittedDates.Any())
            {
                _ = Task.Run(() =>
                    RunProductStoreDailyRecalculationJobAsync(
                        jobId,
                        submittedDates,
                        normalizedMaxConcurrency
                    )
                );
            }

            return new ProductStoreDailyRecalculationSubmitResult
            {
                JobId = jobId,
                SubmittedDates = submittedDates,
                SkippedDates = skippedDates,
                Status = submittedDates.Any()
                    ? SalesStatisticRefreshStatus.Queued
                    : SalesStatisticRefreshStatus.Running,
                Message = BuildProductStoreDailySubmitMessage(submittedDates.Count, skippedDates.Count),
            };
        }

        private static int NormalizeProductStatisticMaxConcurrency(int maxConcurrency)
        {
            return maxConcurrency < 1 ? 3 : Math.Min(maxConcurrency, 10);
        }

        public async Task<int> RecoverTimedOutProductStoreDailyRecalculationJobsAsync(
            TimeSpan timeout,
            DateTime? nowUtc = null
        )
        {
            var currentUtc = nowUtc ?? DateTime.UtcNow;
            var activeStates = await _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s =>
                    s.StatisticType == SalesStatisticType.ProductStoreDaily
                    && (
                        s.Status == SalesStatisticRefreshStatus.Queued
                        || s.Status == SalesStatisticRefreshStatus.Running
                    )
                )
                .ToListAsync();

            var timedOutStates = activeStates
                .Where(state => IsProductStatisticRecoveryTimedOut(state, currentUtc, timeout))
                .ToList();

            foreach (var state in timedOutStates)
            {
                state.Status = SalesStatisticRefreshStatus.Pending;
                state.JobId = null;
                state.StartedAtUtc = null;
                state.CompletedAtUtc = null;
                state.ErrorMessage = null;
                state.LastCheckedAtUtc = currentUtc;
                await _context.Db.Updateable(state).ExecuteCommandAsync();
            }

            return timedOutStates.Count;
        }

        private static bool IsProductStatisticRecoveryTimedOut(
            SalesStatisticRefreshState state,
            DateTime nowUtc,
            TimeSpan timeout
        )
        {
            var referenceTime = state.Status == SalesStatisticRefreshStatus.Running
                ? state.StartedAtUtc ?? state.LastCheckedAtUtc ?? state.RequestedAtUtc
                : state.RequestedAtUtc ?? state.LastCheckedAtUtc;

            // 缺少时间水位的执行中状态无法证明仍有后台任务，启动时优先解锁避免永久卡住。
            return referenceTime == null || nowUtc - referenceTime.Value >= timeout;
        }

        /// <summary>
        /// 滚动刷新最近几天的商品分店每日统计，处理 POSM 延迟上传。
        /// </summary>
        public async Task RefreshRecentProductStoreDailyStatistics(int days = 7)
        {
            var safeDays = Math.Max(1, days);
            var endDate = DateTime.Now.Date;
            var startDate = endDate.AddDays(-(safeDays - 1));

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                await UpdateProductStoreDailyStatistics(date);
            }
        }

        private async Task RunProductStoreDailyRecalculationJobAsync(
            Guid jobId,
            List<DateTime> dates,
            int maxConcurrency
        )
        {
            try
            {
                var normalizedMaxConcurrency = NormalizeProductStatisticMaxConcurrency(maxConcurrency);
                using var semaphore = new SemaphoreSlim(normalizedMaxConcurrency, normalizedMaxConcurrency);
                var tasks = dates.Select(async date =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // 每个日期独立创建作用域和服务，避免共享 DbContext/SqlSugarClient 造成并发污染。
                        using var scope = _serviceScopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<SalesStatisticsJobService>();
                        await service.MarkProductStatisticJobRunningAsync(jobId, date);
                        await service.UpdateProductStoreDailyStatistics(date);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "商品分店每日统计异步重算失败: JobId={JobId}, Date={Date}",
                            jobId,
                            date
                        );
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商品分店每日统计异步任务启动失败: JobId={JobId}", jobId);
            }
        }

        private async Task MarkProductStatisticJobRunningAsync(Guid jobId, DateTime date)
        {
            var targetDate = date.Date;
            var existing = await _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date == targetDate)
                .FirstAsync();

            if (existing == null || existing.JobId != jobId)
            {
                return;
            }

            existing.Status = SalesStatisticRefreshStatus.Running;
            existing.StartedAtUtc = DateTime.UtcNow;
            existing.CompletedAtUtc = null;
            existing.LastCheckedAtUtc = DateTime.UtcNow;
            existing.ErrorMessage = null;
            await _context.Db.Updateable(existing).ExecuteCommandAsync();
        }

        /// <summary>
        /// 带上下文更新商品分店每日统计数据（用于并发处理）。
        /// </summary>
        private async Task UpdateProductStoreDailyStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime date
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);
            try
            {
                logger.LogInformation("开始更新商品分店每日统计: {Date}", targetDate);

                // POSM 上传水位只和 POSM 自己比较，不和 HBweb UTC 时间直接比较。
                var lastSourceUploadTime = await posmContext
                    .Db.Queryable<SalesOrder>()
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    )
                    .MaxAsync(o => o.LastUploadTime);

                // 商品统计以稳定的单日明细读取为基础，分店 fallback 和成本快照在内存中处理。
                var rawRows = await posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    )
                    .Select((o, d) => new
                    {
                        Date = o.OrderTime!.Value.Date,
                        o.OrderGuid,
                        o.BranchCode,
                        o.DeviceCode,
                        OrderLastUploadTime = o.LastUploadTime,
                        d.ProductCode,
                        d.SupplierCode,
                        d.ProductName,
                        d.Barcode,
                        Quantity = d.Quantity ?? 0,
                        ActualAmount = d.ActualAmount ?? 0m,
                        DetailLastUploadTime = d.LastUploadTime,
                    })
                    .ToListAsync();

                var deviceCodes = rawRows
                    .Where(x => string.IsNullOrWhiteSpace(x.BranchCode) && !string.IsNullOrWhiteSpace(x.DeviceCode))
                    .Select(x => x.DeviceCode!)
                    .Distinct()
                    .ToList();
                var deviceBranchMap = deviceCodes.Any()
                    ? (await posmContext.Db.Queryable<POSM_设备注册信息表>()
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

                var productCodes = rawRows
                    .Select(x => x.ProductCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .Distinct()
                    .ToList();
                var branchCodes = rawRows
                    .Select(x => ResolveBranchCode(x.BranchCode, x.DeviceCode, deviceBranchMap))
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct()
                    .ToList();

                var storeCosts = productCodes.Any() && branchCodes.Any()
                    ? await context.Db.Queryable<StoreRetailPrice>()
                        .Where(p =>
                            p.ProductCode != null
                            && p.StoreCode != null
                            && productCodes.Contains(p.ProductCode)
                            && branchCodes.Contains(p.StoreCode)
                            && p.SupplierCode != null
                            && p.IsDeleted == false
                            && p.IsActive == true
                        )
                        .Select(p => new StoreCostRow
                        {
                            StoreCode = p.StoreCode,
                            SupplierCode = p.SupplierCode,
                            ProductCode = p.ProductCode,
                            PurchasePrice = p.PurchasePrice,
                        })
                        .ToListAsync()
                    : new List<StoreCostRow>();
                var storeCostMap = storeCosts
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.StoreCode)
                        && !string.IsNullOrWhiteSpace(x.SupplierCode)
                        && !string.IsNullOrWhiteSpace(x.ProductCode)
                    )
                    .GroupBy(x => $"{x.StoreCode}|{x.SupplierCode}|{x.ProductCode}")
                    .ToDictionary(
                        x => x.Key,
                        x => x.Select(row => (decimal?)row.PurchasePrice).FirstOrDefault(price => price.HasValue && price.Value > 0)
                    );

                var productCosts = productCodes.Any()
                    ? await context.Db.Queryable<Product>()
                        .Where(p => p.ProductCode != null && productCodes.Contains(p.ProductCode) && p.IsDeleted == false)
                        .Select(p => new ProductCostRow
                        {
                            ProductCode = p.ProductCode,
                            PurchasePrice = p.PurchasePrice,
                        })
                        .ToListAsync()
                    : new List<ProductCostRow>();
                var productCostMap = productCosts
                    .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                    .GroupBy(x => x.ProductCode!)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Select(row => (decimal?)row.PurchasePrice).FirstOrDefault(price => price.HasValue && price.Value > 0)
                    );

                var warehouseCosts = productCodes.Any()
                    ? await context.Db.Queryable<WarehouseProduct>()
                        .Where(p => productCodes.Contains(p.ProductCode) && p.IsDeleted == false)
                        .Select(p => new WarehouseCostRow
                        {
                            ProductCode = p.ProductCode,
                            ImportPrice = p.ImportPrice,
                        })
                        .ToListAsync()
                    : new List<WarehouseCostRow>();
                var warehouseCostMap = warehouseCosts
                    .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                    .GroupBy(x => x.ProductCode)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Select(row => (decimal?)row.ImportPrice).FirstOrDefault(price => price.HasValue && price.Value > 0)
                    );

                // 先统一解析分店，再把空供应商留在诊断里，避免污染商品统计主表。
                var resolvedRows = rawRows
                    .Select(x => new
                    {
                        Row = x,
                        ResolvedBranchCode = ResolveBranchCode(x.BranchCode, x.DeviceCode, deviceBranchMap),
                    })
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.ResolvedBranchCode)
                        && !string.IsNullOrWhiteSpace(x.Row.ProductCode)
                    )
                    .ToList();

                var diagnostics = new ProductStatisticDiagnostics();
                var unmatchedSupplierRows = resolvedRows
                    .Where(x => string.IsNullOrWhiteSpace(x.Row.SupplierCode))
                    .ToList();
                if (unmatchedSupplierRows.Any())
                {
                    diagnostics.UnmatchedSupplierAmount = unmatchedSupplierRows.Sum(x => x.Row.ActualAmount);
                    diagnostics.UnmatchedSupplierQuantity = unmatchedSupplierRows.Sum(x => x.Row.Quantity);
                    diagnostics.UnmatchedSupplierProductCount = unmatchedSupplierRows
                        .Select(x => x.Row.ProductCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct()
                        .Count();
                    diagnostics.BranchDiagnostics = unmatchedSupplierRows
                        .GroupBy(x => x.ResolvedBranchCode)
                        .ToDictionary(
                            group => group.Key,
                            group => new ProductStatisticDiagnosticRow
                            {
                                BranchCode = group.Key,
                                UnmatchedSupplierAmount = group.Sum(x => x.Row.ActualAmount),
                                UnmatchedSupplierQuantity = group.Sum(x => x.Row.Quantity),
                                UnmatchedSupplierProductCount = group
                                    .Select(x => x.Row.ProductCode)
                                    .Where(code => !string.IsNullOrWhiteSpace(code))
                                    .Distinct()
                                    .Count(),
                            }
                        );
                }

                var statisticsList = resolvedRows
                    .Where(x => !string.IsNullOrWhiteSpace(x.Row.SupplierCode))
                    .GroupBy(x => new
                    {
                        x.Row.Date,
                        BranchCode = x.ResolvedBranchCode,
                        SupplierCode = x.Row.SupplierCode!,
                        ProductCode = x.Row.ProductCode!,
                    })
                    .Select(group =>
                    {
                        var quantity = group.Sum(x => x.Row.Quantity);
                        var totalAmount = group.Sum(x => x.Row.ActualAmount);
                        var unitCost = ResolveUnitCost(
                            group.Key.BranchCode,
                            group.Key.SupplierCode,
                            group.Key.ProductCode,
                            storeCostMap,
                            productCostMap,
                            warehouseCostMap,
                            out var costSource
                        );
                        var totalCost = unitCost.HasValue ? unitCost.Value * quantity : (decimal?)null;
                        var grossProfit = totalCost.HasValue ? totalAmount - totalCost.Value : (decimal?)null;

                        return new ProductStoreDailySalesStatistic
                        {
                            Date = group.Key.Date,
                            BranchCode = group.Key.BranchCode,
                            SupplierCode = group.Key.SupplierCode,
                            ProductCode = group.Key.ProductCode,
                            ProductName = group.Select(x => x.Row.ProductName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                            Barcode = group.Select(x => x.Row.Barcode).FirstOrDefault(barcode => !string.IsNullOrWhiteSpace(barcode)),
                            TotalQuantity = quantity,
                            TotalAmount = totalAmount,
                            OrderCount = group.Select(x => x.Row.OrderGuid).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count(),
                            UnitCostSnapshot = unitCost,
                            TotalCost = totalCost,
                            GrossProfit = grossProfit,
                            GrossMarginRate = totalAmount > 0m && grossProfit.HasValue
                                ? grossProfit.Value / totalAmount
                                : null,
                            CostSource = costSource,
                            LastSourceUploadTime = group
                                .SelectMany(x => new[] { x.Row.OrderLastUploadTime, x.Row.DetailLastUploadTime })
                                .Where(value => value.HasValue)
                                .Select(value => value!.Value)
                                .DefaultIfEmpty(lastSourceUploadTime ?? DateTime.MinValue)
                                .Max(),
                            UpdateTime = DateTime.Now,
                        };
                    })
                    .ToList();

                var status = await BuildProductStatisticStatusAsync(
                    context,
                    targetDate,
                    statisticsList,
                    diagnostics,
                    lastSourceUploadTime
                );

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        var deletedCount = await context.Db.Deleteable<ProductStoreDailySalesStatistic>()
                            .Where(s => s.Date == targetDate)
                            .ExecuteCommandAsync();
                        logger.LogInformation("删除 {Count} 条商品分店每日统计旧记录", deletedCount);

                        if (statisticsList.Any())
                        {
                            context.Db.Fastest<ProductStoreDailySalesStatistic>()
                                .PageSize(BatchSize)
                                .BulkCopy(statisticsList);
                        }

                        await UpsertProductStatisticStateAsync(context, targetDate, status, lastSourceUploadTime);
                    },
                    commitAsync: () => context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                    logger: logger,
                    operationName: "商品分店每日统计更新"
                );

                logger.LogInformation(
                    "商品分店每日统计更新完成: {Date}, 总记录: {Total}, 状态: {Status}",
                    targetDate,
                    statisticsList.Count,
                    status.Status
                );
            }
            catch (Exception ex)
            {
                await UpsertProductStatisticStateAsync(
                    context,
                    targetDate,
                    new ProductStatisticStatusResult(SalesStatisticRefreshStatus.Failed, ex.Message),
                    null
                );
                logger.LogError(ex, "更新商品分店每日统计失败: {Date}", targetDate);
                throw;
            }
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

        private async Task<List<StoreSalesStatistic>> BuildStoreStatisticsAsync(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            DateTime date,
            List<string>? branchCodes
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);
            var targetBranchCodes = NormalizeBranchCodes(branchCodes);

            var paymentRows = await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                    (pd, so) => pd.OrderGuid == so.OrderGuid
                )
                .Where((pd, so) =>
                    so.Status != null
                    && (so.Status == 1 || so.Status == 4)
                    && so.OrderTime != null
                    && so.OrderTime >= targetDate
                    && so.OrderTime < nextDate
                )
                .Select((pd, so) => new StoreStatisticPaymentRow
                {
                    OrderGuid = so.OrderGuid,
                    BranchCode = so.BranchCode,
                    DeviceCode = so.DeviceCode,
                    Amount = pd.Amount ?? 0m,
                })
                .ToListAsync();

            var quantityRows = await posmContext.Db.Queryable<SalesOrderDetail, SalesOrder>(
                    (d, so) => d.OrderGuid == so.OrderGuid
                )
                .Where((d, so) =>
                    so.Status != null
                    && (so.Status == 1 || so.Status == 4)
                    && so.OrderTime != null
                    && so.OrderTime >= targetDate
                    && so.OrderTime < nextDate
                )
                .Select((d, so) => new StoreStatisticQuantityRow
                {
                    OrderGuid = so.OrderGuid,
                    BranchCode = so.BranchCode,
                    DeviceCode = so.DeviceCode,
                    Quantity = d.Quantity ?? 0,
                })
                .ToListAsync();

            var orderRows = await posmContext.Db.Queryable<SalesOrder>()
                .Where(so =>
                    so.Status != null
                    && (so.Status == 1 || so.Status == 4)
                    && so.OrderTime != null
                    && so.OrderTime >= targetDate
                    && so.OrderTime < nextDate
                )
                .Select(so => new StoreStatisticOrderRow
                {
                    OrderGuid = so.OrderGuid,
                    BranchCode = so.BranchCode,
                    DeviceCode = so.DeviceCode,
                })
                .ToListAsync();

            var deviceCodes = paymentRows
                .Select(row => row.BranchCode is null or "" ? row.DeviceCode : null)
                .Concat(quantityRows.Select(row => row.BranchCode is null or "" ? row.DeviceCode : null))
                .Concat(orderRows.Select(row => row.BranchCode is null or "" ? row.DeviceCode : null))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct()
                .ToList();
            var deviceBranchMap = deviceCodes.Any()
                ? (await posmContext.Db.Queryable<POSM_设备注册信息表>()
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

            bool IsTargetBranch(string branchCode) =>
                !string.IsNullOrWhiteSpace(branchCode)
                && (!targetBranchCodes.Any() || targetBranchCodes.Contains(branchCode));

            var resolvedPaymentRows = paymentRows
                .Select(row => new
                {
                    BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                    row.Amount,
                })
                .ToList();
            LogSkippedBranchCodeRows(
                "分店每日销售统计金额",
                resolvedPaymentRows,
                row => row.BranchCode,
                row => row.Amount,
                _ => 0m
            );

            var resolvedQuantityRows = quantityRows
                .Select(row => new
                {
                    BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                    row.Quantity,
                })
                .ToList();
            LogSkippedBranchCodeRows(
                "分店每日销售统计销量",
                resolvedQuantityRows,
                row => row.BranchCode,
                _ => 0m,
                row => row.Quantity
            );

            var resolvedOrderRows = orderRows
                .Select(row => new
                {
                    row.OrderGuid,
                    BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                })
                .ToList();
            LogSkippedBranchCodeRows(
                "分店每日销售统计订单数",
                resolvedOrderRows,
                row => row.BranchCode,
                _ => 0m,
                _ => 0m
            );

            // 金额仍以支付明细为准；只在内存中解析分店，避免订单分店为空时漏入统计。
            var amountByBranch = resolvedPaymentRows
                .Where(row => IsTargetBranch(row.BranchCode))
                .GroupBy(row => row.BranchCode)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));

            // 销量使用销售明细 Quantity，不能使用订单头 ItemCount。
            var quantityByBranch = resolvedQuantityRows
                .Where(row => IsTargetBranch(row.BranchCode))
                .GroupBy(row => row.BranchCode)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Quantity));

            var orderCountByBranch = resolvedOrderRows
                .Where(row => IsTargetBranch(row.BranchCode) && !string.IsNullOrWhiteSpace(row.OrderGuid))
                .GroupBy(row => row.BranchCode)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.OrderGuid).Distinct().Count()
                );

            var statisticBranchCodes = amountByBranch.Keys
                .Union(quantityByBranch.Keys)
                .Union(orderCountByBranch.Keys)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToList();

            var stores = statisticBranchCodes.Any()
                ? await context.Db.Queryable<Store>()
                    .Where(s => statisticBranchCodes.Contains(s.StoreCode))
                    .ToListAsync()
                : new List<Store>();
            var storeDict = stores.ToDictionary(s => s.StoreCode, s => s);

            return statisticBranchCodes
                .Select(branchCode =>
                {
                    var totalAmount = amountByBranch.GetValueOrDefault(branchCode);
                    var orderCount = orderCountByBranch.GetValueOrDefault(branchCode);
                    var store = storeDict.GetValueOrDefault(branchCode);

                    return new StoreSalesStatistic
                    {
                        Date = targetDate,
                        BranchCode = branchCode,
                        BranchName = store?.StoreName ?? branchCode,
                        TotalAmount = totalAmount,
                        TotalQuantity = quantityByBranch.GetValueOrDefault(branchCode),
                        OrderCount = orderCount,
                        CustomerCount = orderCount,
                        AverageOrderValue = orderCount > 0 ? totalAmount / orderCount : 0m,
                        UpdateTime = DateTime.Now,
                    };
                })
                .ToList();
        }

        private static List<string> NormalizeBranchCodes(List<string>? branchCodes)
        {
            return branchCodes?
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? new List<string>();
        }

        private static decimal? ResolveUnitCost(
            string branchCode,
            string supplierCode,
            string productCode,
            Dictionary<string, decimal?> storeCostMap,
            Dictionary<string, decimal?> productCostMap,
            Dictionary<string, decimal?> warehouseCostMap,
            out string costSource
        )
        {
            if (storeCostMap.TryGetValue($"{branchCode}|{supplierCode}|{productCode}", out var storeCost) && storeCost.HasValue && storeCost.Value > 0)
            {
                costSource = "StoreRetailPrice";
                return storeCost;
            }

            if (productCostMap.TryGetValue(productCode, out var productCost) && productCost.HasValue && productCost.Value > 0)
            {
                costSource = "ProductPurchasePrice";
                return productCost;
            }

            if (warehouseCostMap.TryGetValue(productCode, out var warehouseCost) && warehouseCost.HasValue && warehouseCost.Value > 0)
            {
                costSource = "WarehouseImportPrice";
                return warehouseCost;
            }

            costSource = "Missing";
            return null;
        }

        private sealed record ProductStatisticStatusResult(string Status, string? ErrorMessage);

        private async Task<ProductStatisticStatusResult> BuildProductStatisticStatusAsync(
            SqlSugarContext context,
            DateTime targetDate,
            List<ProductStoreDailySalesStatistic> statisticsList,
            ProductStatisticDiagnostics diagnostics,
            DateTime? lastSourceUploadTime
        )
        {
            var existingStoreSales = await context.Db.Queryable<StoreSalesStatistic>()
                .Where(s => s.Date == targetDate)
                .Select(s => new { s.BranchCode, s.TotalAmount, s.TotalQuantity })
                .ToListAsync();

            var storeRollups = existingStoreSales
                .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
                .Select(x => new ProductStoreDailyBranchRollup(
                    x.BranchCode!,
                    x.TotalAmount,
                    x.TotalQuantity
                ));

            var productRollups = statisticsList
                .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
                .Select(x => new ProductStoreDailyBranchRollup(
                    x.BranchCode,
                    x.TotalAmount,
                    x.TotalQuantity
                ));

            var branchDiagnostics = diagnostics.BranchDiagnostics
                .ToDictionary(
                    item => item.Key,
                    item => new ProductStoreDailyBranchDiagnostic(
                        item.Value.UnmatchedSupplierAmount,
                        item.Value.UnmatchedSupplierQuantity,
                        item.Value.UnmatchedSupplierProductCount
                    )
                );

            var reconciliation = ProductStoreDailyReconciliationCalculator.Calculate(
                targetDate,
                productRollups,
                storeRollups,
                branchDiagnostics
            );

            return new ProductStatisticStatusResult(reconciliation.Status, reconciliation.ErrorMessage);
        }

        private async Task UpsertProductStatisticStateAsync(
            SqlSugarContext context,
            DateTime targetDate,
            ProductStatisticStatusResult status,
            DateTime? lastSourceUploadTime
        )
        {
            var existing = await context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date == targetDate)
                .FirstAsync();

            if (existing == null)
            {
                existing = new SalesStatisticRefreshState
                {
                    StatisticType = SalesStatisticType.ProductStoreDaily,
                    Date = targetDate,
                };
                existing.Status = status.Status;
                existing.LastSourceUploadTime = lastSourceUploadTime;
                existing.SourceTimeZone = "POSM_LOCAL";
                existing.LastAggregatedAtUtc = DateTime.UtcNow;
                existing.LastCheckedAtUtc = DateTime.UtcNow;
                existing.ErrorMessage = status.ErrorMessage;
                if (
                    status.Status == SalesStatisticRefreshStatus.Fresh
                    || status.Status == SalesStatisticRefreshStatus.Failed
                )
                {
                    existing.CompletedAtUtc = DateTime.UtcNow;
                }
                await context.Db.Insertable(existing).ExecuteCommandAsync();
                return;
            }

            existing.Status = status.Status;
            existing.LastSourceUploadTime = lastSourceUploadTime ?? existing.LastSourceUploadTime;
            existing.SourceTimeZone = "POSM_LOCAL";
            existing.LastAggregatedAtUtc = DateTime.UtcNow;
            existing.LastCheckedAtUtc = DateTime.UtcNow;
            existing.ErrorMessage = status.ErrorMessage;
            if (
                status.Status == SalesStatisticRefreshStatus.Fresh
                || status.Status == SalesStatisticRefreshStatus.Failed
            )
            {
                existing.CompletedAtUtc = DateTime.UtcNow;
            }
            await context.Db.Updateable(existing).ExecuteCommandAsync();
        }

        private static string BuildProductStoreDailySubmitMessage(int submittedCount, int skippedCount)
        {
            if (submittedCount == 0 && skippedCount > 0)
            {
                return $"所选 {skippedCount} 天商品统计已有任务执行中，未重复提交";
            }

            if (skippedCount > 0)
            {
                return $"已提交 {submittedCount} 天商品统计重算，跳过 {skippedCount} 天执行中的任务";
            }

            return $"已提交 {submittedCount} 天商品统计重算任务";
        }

        private static async Task UpsertProductStatisticQueuedStateAsync(
            SqlSugarContext context,
            DateTime targetDate,
            Guid jobId,
            string? requestedBy
        )
        {
            var now = DateTime.UtcNow;
            var existing = await context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date == targetDate)
                .FirstAsync();

            if (existing == null)
            {
                await context.Db.Insertable(new SalesStatisticRefreshState
                {
                    StatisticType = SalesStatisticType.ProductStoreDaily,
                    Date = targetDate,
                    Status = SalesStatisticRefreshStatus.Queued,
                    SourceTimeZone = "POSM_LOCAL",
                    JobId = jobId,
                    RequestedBy = requestedBy,
                    RequestedAtUtc = now,
                    LastCheckedAtUtc = now,
                }).ExecuteCommandAsync();
                return;
            }

            existing.Status = SalesStatisticRefreshStatus.Queued;
            existing.JobId = jobId;
            existing.RequestedBy = requestedBy;
            existing.RequestedAtUtc = now;
            existing.StartedAtUtc = null;
            existing.CompletedAtUtc = null;
            existing.LastCheckedAtUtc = now;
            existing.ErrorMessage = null;
            await context.Db.Updateable(existing).ExecuteCommandAsync();
        }

        /// <summary>
        /// 批量更新分店统计数据
        /// 逐日更新指定日期范围内各分店的统计数据
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchUpdateStoreStatistics(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes = null
        )
        {
            var result = new BatchStatisticsUpdateResult();
            // 验证日期范围
            var validation = ValidateDateRange(startDate, endDate);
            if (!validation.Success)
            {
                return validation;
            }

            result.TotalDays = (int)(endDate - startDate).TotalDays + 1;
            _logger.LogInformation(
                "开始批量更新分店统计数据: {StartDate} 至 {EndDate}, 分店: {Branches}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                branchCodes != null ? string.Join(", ", branchCodes) : "All"
            );

            // 逐日更新统计数据
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                try
                {
                    await UpdateStoreStatistics(date, branchCodes);
                    result.ProcessedDays++;
                }
                catch (Exception ex)
                {
                    result.FailedDates.Add(date.ToString("yyyy-MM-dd"));
                    _logger.LogError(ex, "批量更新分店统计失败: {Date}", date);
                }
            }

            result.Success = result.FailedDates.Count == 0;
            result.Message = result.Success
                ? $"批量更新分店统计完成: {result.ProcessedDays}/{result.TotalDays} 天"
                : $"批量更新分店统计部分完成: {result.ProcessedDays}/{result.TotalDays} 天, 失败 {result.FailedDates.Count} 天";

            _logger.LogInformation(result.Message);
            return result;
        }

        /// <summary>
        /// 批量更新供应商统计数据
        /// 指定日期范围内更新供应商统计数据
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchUpdateSupplierStatistics(
            DateTime startDate,
            DateTime endDate,
            List<string>? supplierCodes = null
        )
        {
            var result = new BatchStatisticsUpdateResult();
            // 验证日期范围
            var validation = ValidateDateRange(startDate, endDate);
            if (!validation.Success)
            {
                return validation;
            }

            result.TotalDays = (int)(endDate - startDate).TotalDays + 1;
            _logger.LogInformation(
                "开始批量更新供应商统计数据: {StartDate} 至 {EndDate}, 供应商: {Suppliers}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
            );

            try
            {
                // 执行更新
                await UpdateSupplierStatistics(startDate, endDate, supplierCodes);
                result.ProcessedDays = result.TotalDays;
                result.Success = true;
                result.Message =
                    $"批量更新供应商统计完成: {result.ProcessedDays}/{result.TotalDays} 天";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message =
                    $"批量更新供应商统计失败: {result.TotalDays} 天, 错误: {ex.Message}";
                _logger.LogError(
                    ex,
                    "批量更新供应商统计失败: {StartDate} 至 {EndDate}",
                    startDate,
                    endDate
                );
            }

            return result;
        }

        /// <summary>
        /// 并发批量更新供应商统计数据
        /// 将日期范围拆分为多个块，并发处理以提高效率
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        /// <param name="maxConcurrency">最大并发数，为空则使用配置值</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchUpdateSupplierStatisticsConcurrent(
            DateTime startDate,
            DateTime endDate,
            List<string>? supplierCodes = null,
            int? maxConcurrency = null
        )
        {
            var result = new BatchStatisticsUpdateResult();

            // 验证日期范围
            if (startDate > endDate)
            {
                result.Message = "开始日期不能大于结束日期";
                _logger.LogWarning(result.Message);
                return result;
            }

            var totalDays = (int)(endDate - startDate).TotalDays + 1;

            // 检查日期范围是否超出并发更新支持的最大天数
            if (totalDays > _maxDaysForConcurrentUpdate)
            {
                result.Message =
                    $"日期范围过大，并发更新最多支持 {_maxDaysForConcurrentUpdate} 天（当前: {totalDays} 天）";
                _logger.LogWarning(result.Message);
                return result;
            }

            // 确定并发度
            var concurrency = maxConcurrency ?? _maxConcurrentUpdates;

            _logger.LogInformation(
                "开始并发批量更新供应商统计数据: {StartDate} 至 {EndDate}, 供应商: {Suppliers}, 并发度: {Concurrency}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                supplierCodes != null ? string.Join(", ", supplierCodes) : "All",
                concurrency
            );

            // 拆分日期范围为多个并发块
            var dateRanges = SplitDateRange(startDate, endDate, concurrency);
            _logger.LogInformation(
                "将 {TotalDays} 天拆分为 {ChunkCount} 个并发块",
                totalDays,
                dateRanges.Count
            );

            var processedDays = 0;
            var failedDates = new List<string>();
            var failedRanges = new List<string>();
            var syncLock = new object();

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = concurrency };

            // 启动计时器
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 并发处理每个日期块
                await Parallel.ForEachAsync(
                    dateRanges,
                    parallelOptions,
                    async (dateRange, cancellationToken) =>
                    {
                        try
                        {
                            _logger.LogInformation(
                                "开始处理并发块 {StartDate} 至 {EndDate} ({Days} 天)",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd"),
                                dateRange.DayCount
                            );

                            // 为每个并发任务创建独立的作用域和数据库上下文
                            using var scope = _serviceScopeFactory.CreateScope();
                            var context =
                                scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
                            var posmContext =
                                scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();
                            var logger = scope.ServiceProvider.GetRequiredService<
                                ILogger<SalesStatisticsJobService>
                            >();

                            // 调用带上下文的更新方法
                            await UpdateSupplierStatisticsWithContext(
                                context,
                                posmContext,
                                logger,
                                dateRange.StartDate,
                                dateRange.EndDate,
                                supplierCodes
                            );

                            // 使用锁更新进度
                            lock (syncLock)
                            {
                                processedDays += dateRange.DayCount;
                            }

                            logger.LogInformation(
                                "并发块处理完成 {StartDate} 至 {EndDate} ({Days} 天), 累计进度: {Progress}/{Total}",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd"),
                                dateRange.DayCount,
                                processedDays,
                                totalDays
                            );
                        }
                        catch (Exception ex)
                        {
                            var rangeKey =
                                $"{dateRange.StartDate:yyyy-MM-dd} 至 {dateRange.EndDate:yyyy-MM-dd}";
                            failedRanges.Add(rangeKey);

                            // 记录失败的日期
                            for (
                                var d = dateRange.StartDate;
                                d <= dateRange.EndDate;
                                d = d.AddDays(1)
                            )
                            {
                                failedDates.Add(d.ToString("yyyy-MM-dd"));
                            }

                            _logger.LogError(
                                ex,
                                "并发块处理失败 {StartDate} 至 {EndDate}",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd")
                            );
                        }
                    }
                );

                stopwatch.Stop();

                result.TotalDays = totalDays;
                result.ProcessedDays = processedDays;
                result.FailedDates = failedDates;
                result.Success = failedDates.Count == 0;

                var avgTimePerDay = stopwatch.Elapsed.TotalSeconds / totalDays;
                result.Message = result.Success
                    ? $"并发批量更新供应商统计完成: {processedDays}/{totalDays} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}, 平均每天: {avgTimePerDay:F2}秒"
                    : $"并发批量更新供应商统计部分完成: {processedDays}/{totalDays} 天, 失败: {failedDates.Count} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}";

                _logger.LogInformation(
                    "{ResultMessage}, 失败的日期块: {FailedRanges}",
                    result.Message,
                    failedRanges.Count > 0 ? string.Join("; ", failedRanges) : "无"
                );
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Success = false;
                result.Message =
                    $"并发批量更新供应商统计失败: 处理 {processedDays}/{totalDays} 天, 错误: {ex.Message}";
                _logger.LogError(
                    ex,
                    "并发批量更新供应商统计失败: {StartDate} 至 {EndDate}",
                    startDate,
                    endDate
                );
            }

            return result;
        }

        /// <summary>
        /// 带上下文更新供应商统计数据（用于并发处理）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="supplierCodes">供应商代码列表</param>
        private async Task UpdateSupplierStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime startDate,
            DateTime endDate,
            List<string>? supplierCodes
        )
        {
            try
            {
                var isSingleDate = startDate == endDate;

                logger.LogInformation(
                    "更新供应商统计数据: {StartDate} 至 {EndDate}, Suppliers: {Suppliers}",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd"),
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                // 构建查询
                var query = posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(o =>
                        o.Status != null && (o.Status == 1 || o.Status == 4) && o.OrderTime != null
                    );

                // 设置日期过滤条件
                if (isSingleDate)
                {
                    query = query.Where(o => o.OrderTime!.Value.Date == startDate);
                }
                else
                {
                    query = query.Where(o =>
                        o.OrderTime!.Value.Date >= startDate && o.OrderTime!.Value.Date <= endDate
                    );
                }

                // 设置供应商代码过滤条件
                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            (m.LocalSupplierCode != null
                                && supplierCodes.Contains(m.LocalSupplierCode))
                            || (m.ChinaSupplierCode != null
                                && supplierCodes.Contains(m.ChinaSupplierCode))
                    );
                }

                // 获取原始销售数据
                var rawData = await query
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = d.ActualAmount,
                                Quantity = d.Quantity ?? 0m,
                                OrderGuid = o.OrderGuid,
                            }
                    )
                    .ToListAsync();

                // 本地供应商聚合
                var localStats = rawData
                    .Where(x => !string.IsNullOrEmpty(x.LocalSupplierCode))
                    .GroupBy(x => new { x.Date, x.LocalSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.LocalSupplierCode,
                        IsDomestic = false,
                        TotalAmount = g.Sum(x => x.TotalAmount) ?? 0m,
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode).Distinct().Count(),
                        OrderCount = g.Select(x => x.OrderGuid).Distinct().Count(),
                        UpdateTime = DateTime.Now,
                    })
                    .ToList();

                // 国内供应商聚合
                var chinaStats = rawData
                    .Where(x =>
                        x.LocalSupplierCode == "200" && !string.IsNullOrEmpty(x.ChinaSupplierCode)
                    )
                    .GroupBy(x => new { x.Date, x.ChinaSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.ChinaSupplierCode!,
                        IsDomestic = true,
                        TotalAmount = g.Sum(x => x.TotalAmount) ?? 0m,
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode).Distinct().Count(),
                        OrderCount = g.Select(x => x.OrderGuid).Distinct().Count(),
                        UpdateTime = DateTime.Now,
                    })
                    .ToList();

                // 合并本地供应商和国内供应商统计
                var allStats = localStats.Concat(chinaStats).ToList();

                var allLocalCodes = localStats.Select(x => x.SupplierCode).Distinct().ToList();
                var allChinaCodes = chinaStats.Select(x => x.SupplierCode).Distinct().ToList();

                var supplierNameDict = new Dictionary<string, string>();

                // 查询本地供应商名称
                if (allLocalCodes.Any())
                {
                    var localSuppliers = await context.HBLocalSupplierDb.GetListAsync(s =>
                        s.LocalSupplierCode != null
                        && allLocalCodes.Contains(s.LocalSupplierCode)
                        && !s.IsDeleted
                    );
                    foreach (var s in localSuppliers)
                    {
                        if (!string.IsNullOrEmpty(s.LocalSupplierCode))
                        {
                            supplierNameDict[s.LocalSupplierCode] = s.Name ?? s.LocalSupplierCode;
                        }
                    }
                }

                // 查询国内供应商名称
                if (allChinaCodes.Any())
                {
                    var chinaSuppliers = await context.ChinaSupplierDb.GetListAsync(s =>
                        s.SupplierCode != null
                        && allChinaCodes.Contains(s.SupplierCode)
                        && !s.IsDeleted
                    );
                    foreach (var s in chinaSuppliers)
                    {
                        if (!string.IsNullOrEmpty(s.SupplierCode))
                        {
                            supplierNameDict[s.SupplierCode] = s.SupplierName ?? s.SupplierCode;
                        }
                    }
                }

                // 为统计记录填充供应商名称
                foreach (var stat in allStats)
                {
                    if (supplierNameDict.TryGetValue(stat.SupplierCode, out var name))
                    {
                        stat.SupplierName = name;
                    }
                    else
                    {
                        stat.SupplierName = stat.SupplierCode;
                    }
                }

                var allSupplierCodes = allStats.Select(s => s.SupplierCode).Distinct().ToList();

                // 查询数据库中已存在的记录
                var existingRecords = await context
                    .Db.Queryable<SupplierSalesStatistic>()
                    .Where(s =>
                        s.Date >= startDate
                        && s.Date <= endDate
                        && allSupplierCodes.Contains(s.SupplierCode)
                    )
                    .ToListAsync();

                // 构建已存在记录的字典，用于快速查找
                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.SupplierCode}",
                    s => s
                );

                var toInsert = new List<SupplierSalesStatistic>();
                var toUpdate = new List<SupplierSalesStatistic>();

                // 遍历统计数据，区分插入和更新操作
                foreach (var stat in allStats)
                {
                    var key = $"{stat.Date}_{stat.SupplierCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 记录已存在，更新字段值
                        existing.SupplierName = stat.SupplierName;
                        existing.IsDomestic = stat.IsDomestic;
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.StoreCount = stat.StoreCount;
                        existing.OrderCount = stat.OrderCount;
                        existing.UpdateTime = stat.UpdateTime;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新记录，加入插入列表
                        toInsert.Add(stat);
                    }
                }

                // 批量插入新记录
                if (toInsert.Any())
                {
                    context
                        .Db.Fastest<SupplierSalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkCopy(toInsert);
                    logger.LogInformation("批量插入 {Count} 条供应商统计记录", toInsert.Count);
                }

                // 批量更新已存在记录
                if (toUpdate.Any())
                {
                    context
                        .Db.Fastest<SupplierSalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkUpdate(toUpdate);
                    logger.LogInformation("批量更新 {Count} 条供应商统计记录", toUpdate.Count);
                }

                logger.LogInformation(
                    "供应商统计数据更新完成: {StartDate} 至 {EndDate}, 总记录: {Total}",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd"),
                    allStats.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "更新供应商统计数据失败: {StartDate} 至 {EndDate}",
                    startDate,
                    endDate
                );
                throw;
            }
        }

        private List<DateRange> SplitDateRange(DateTime startDate, DateTime endDate, int maxChunks)
        {
            var totalDays = (int)(endDate - startDate).TotalDays + 1;

            var chunkCount = Math.Min(
                maxChunks,
                (int)Math.Ceiling((double)totalDays / _maxDaysPerChunk)
            );

            var ranges = new List<DateRange>();
            var currentStart = startDate;

            while (currentStart <= endDate)
            {
                var currentEnd = currentStart.AddDays(_maxDaysPerChunk - 1);
                if (currentEnd > endDate)
                {
                    currentEnd = endDate;
                }

                ranges.Add(new DateRange { StartDate = currentStart, EndDate = currentEnd });
                currentStart = currentEnd.AddDays(1);
            }

            return ranges;
        }

        /// <summary>
        /// 批量更新每日统计数据
        /// 逐日更新指定日期范围内的每日销售汇总数据
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchUpdateDailyStatistics(
            DateTime startDate,
            DateTime endDate
        )
        {
            var result = new BatchStatisticsUpdateResult();
            // 验证日期范围
            var validation = ValidateDateRange(startDate, endDate);
            if (!validation.Success)
            {
                return validation;
            }

            result.TotalDays = (int)(endDate - startDate).TotalDays + 1;
            _logger.LogInformation(
                "开始批量更新每日统计数据: {StartDate} 至 {EndDate}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd")
            );

            // 逐日更新统计数据
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                try
                {
                    await UpdateDailyStatistics(date.ToString("yyyy-MM-dd"));
                    result.ProcessedDays++;
                }
                catch (Exception ex)
                {
                    result.FailedDates.Add(date.ToString("yyyy-MM-dd"));
                    _logger.LogError(ex, "批量更新每日统计失败: {Date}", date);
                }
            }

            result.Success = result.FailedDates.Count == 0;
            result.Message = result.Success
                ? $"批量更新每日统计完成: {result.ProcessedDays}/{result.TotalDays} 天"
                : $"批量更新每日统计部分完成: {result.ProcessedDays}/{result.TotalDays} 天, 失败 {result.FailedDates.Count} 天";

            _logger.LogInformation(result.Message);
            return result;
        }

        /// <summary>
        /// 批量更新分时统计数据
        /// 逐日更新指定日期范围内的分时统计数据（可指定小时）
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="hour">指定小时，为空则更新所有小时</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchUpdateHourlyStatistics(
            DateTime startDate,
            DateTime endDate,
            int? hour = null
        )
        {
            var result = new BatchStatisticsUpdateResult();
            // 验证日期范围
            var validation = ValidateDateRange(startDate, endDate);
            if (!validation.Success)
            {
                return validation;
            }

            result.TotalDays = (int)(endDate - startDate).TotalDays + 1;
            var hourStr = hour.HasValue ? $" hour {hour.Value}" : " all hours";
            _logger.LogInformation(
                "开始批量更新分时统计数据: {StartDate} 至 {EndDate}{Hour}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                hourStr
            );

            // 逐日更新统计数据
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                try
                {
                    if (hour.HasValue)
                    {
                        await UpdateHourlyStatistics(date, hour.Value);
                    }
                    else
                    {
                        await UpdateHourlyStatistics(date, null);
                    }
                    result.ProcessedDays++;
                }
                catch (Exception ex)
                {
                    result.FailedDates.Add(date.ToString("yyyy-MM-dd"));
                    _logger.LogError(ex, "批量更新分时统计失败: {Date}", date);
                }
            }

            result.Success = result.FailedDates.Count == 0;
            result.Message = result.Success
                ? $"批量更新分时统计完成: {result.ProcessedDays}/{result.TotalDays} 天"
                : $"批量更新分时统计部分完成: {result.ProcessedDays}/{result.TotalDays} 天, 失败 {result.FailedDates.Count} 天";

            _logger.LogInformation(result.Message);
            return result;
        }

        /// <summary>
        /// 更新门店供应商统计数据
        /// 按门店和供应商维度聚合销售数据
        /// </summary>
        /// <param name="date">目标日期，为空则更新当天</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        public async Task UpdateStoreSupplierStatistics(
            DateTime? date = null,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        )
        {
            try
            {
                var targetDate = date ?? DateTime.Now.Date;

                _logger.LogInformation(
                    "开始更新门店供应商统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    targetDate,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All",
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                // 构建查询
                var query = _posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime.Value.Date == targetDate
                    );

                // 设置分店过滤条件
                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(o =>
                        o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                    );
                }

                // 设置供应商过滤条件
                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            m.LocalSupplierCode != null
                            && supplierCodes.Contains(m.LocalSupplierCode)
                    );
                }

                // 查询并聚合门店供应商销售数据
                var storeSupplierData = await query
                    .GroupBy(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                            }
                    )
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                                OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                            }
                    )
                    .ToListAsync();

                // 获取所有本地供应商代码
                var allLocalSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.LocalSupplierCode))
                    .Select(d => d.LocalSupplierCode!)
                    .Distinct()
                    .ToList();

                // 查询本地供应商信息
                var localSupplierDict = new Dictionary<string, HBLocalSupplier>();
                if (allLocalSupplierCodes.Any())
                {
                    var localSuppliers = await _context.HBLocalSupplierDb.GetListAsync(s =>
                        s.LocalSupplierCode != null
                        && allLocalSupplierCodes.Contains(s.LocalSupplierCode)
                        && !s.IsDeleted
                    );
                    localSupplierDict = localSuppliers.ToDictionary(
                        s => s.LocalSupplierCode!,
                        s => s
                    );
                }

                // 获取所有国内供应商代码
                var allChinaSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.ChinaSupplierCode))
                    .Select(d => d.ChinaSupplierCode!)
                    .Distinct()
                    .ToList();

                // 查询国内供应商信息
                var chinaSupplierDict = new Dictionary<string, ChinaSupplier>();
                if (allChinaSupplierCodes.Any())
                {
                    var chinaSuppliers = await _context.ChinaSupplierDb.GetListAsync(cs =>
                        cs.SupplierCode != null
                        && allChinaSupplierCodes.Contains(cs.SupplierCode)
                        && !cs.IsDeleted
                    );
                    chinaSupplierDict = chinaSuppliers
                        .Where(cs => !string.IsNullOrEmpty(cs.SupplierCode))
                        .ToDictionary(cs => cs.SupplierCode!, cs => cs);
                }

                var statisticsList = new List<StoreSupplierSalesDetail>();

                LogSkippedBranchCodeRows(
                    "分店供应商销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 构建每个门店供应商的统计记录
                foreach (var data in storeSupplierData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var localSupplierCode = data.LocalSupplierCode ?? string.Empty;
                    var chinaSupplierCode = data.ChinaSupplierCode;

                    var supplierCode = localSupplierCode;
                    var supplierName = localSupplierCode;
                    var isDomestic = false;

                    // 判断供应商类型并获取供应商名称
                    if (localSupplierCode == "200" && !string.IsNullOrEmpty(chinaSupplierCode))
                    {
                        // 国内供应商
                        supplierCode = chinaSupplierCode;
                        isDomestic = true;
                        if (chinaSupplierDict.TryGetValue(chinaSupplierCode, out var cs))
                        {
                            supplierName = cs.SupplierName ?? supplierCode;
                        }
                    }
                    else
                    {
                        // 本地供应商
                        if (localSupplierDict.TryGetValue(localSupplierCode, out var ls))
                        {
                            supplierName = ls.Name ?? localSupplierCode;
                            isDomestic = false;
                        }
                    }

                    var statistic = new StoreSupplierSalesDetail
                    {
                        Date = data.Date,
                        BranchCode = branchCode,
                        SupplierCode = supplierCode,
                        SupplierName = supplierName,
                        IsDomestic = isDomestic,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        OrderCount = data.OrderCount,
                        UpdateTime = DateTime.Now,
                    };

                    statisticsList.Add(statistic);
                }

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 删除该日期的所有旧记录
                        var deletedCount = await _context
                            .Db.Deleteable<StoreSupplierSalesDetail>()
                            .Where(s => s.Date == targetDate)
                            .ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条门店供应商统计旧记录", deletedCount);

                        // 批量插入新记录
                        _context
                            .Db.Fastest<StoreSupplierSalesDetail>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                    },
                    commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                    logger: _logger,
                    operationName: "门店供应商统计数据更新"
                );

                _logger.LogInformation(
                    "门店供应商统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新门店供应商统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 批量更新门店供应商统计数据
        /// 逐日更新指定日期范围内的门店供应商统计
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchUpdateStoreSupplierStatistics(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        )
        {
            var result = new BatchStatisticsUpdateResult();
            // 验证日期范围
            var validation = ValidateDateRange(startDate, endDate);
            if (!validation.Success)
            {
                return validation;
            }

            result.TotalDays = (int)(endDate - startDate).TotalDays + 1;
            _logger.LogInformation(
                "开始批量更新门店供应商统计数据: {StartDate} 至 {EndDate}, 分店: {Branches}, 供应商: {Suppliers}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                branchCodes != null ? string.Join(", ", branchCodes) : "All",
                supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
            );

            // 逐日更新统计数据
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                try
                {
                    await UpdateStoreSupplierStatistics(date);
                    result.ProcessedDays++;
                }
                catch (Exception ex)
                {
                    result.FailedDates.Add(date.ToString("yyyy-MM-dd"));
                    _logger.LogError(ex, "批量更新门店供应商统计失败: {Date}", date);
                }
            }

            result.Success = result.FailedDates.Count == 0;
            result.Message = result.Success
                ? $"批量更新门店供应商统计完成: {result.ProcessedDays}/{result.TotalDays} 天"
                : $"批量更新门店供应商统计部分完成: {result.ProcessedDays}/{result.TotalDays} 天, 失败 {result.FailedDates.Count} 天";

            _logger.LogInformation(result.Message);
            return result;
        }

        /// <summary>
        /// 验证日期范围
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>验证结果</returns>
        private BatchStatisticsUpdateResult ValidateDateRange(DateTime startDate, DateTime endDate)
        {
            var result = new BatchStatisticsUpdateResult { Success = false };

            // 验证开始日期是否小于等于结束日期
            if (startDate > endDate)
            {
                result.Message = "开始日期不能大于结束日期";
                _logger.LogWarning(result.Message);
                return result;
            }

            // 验证日期范围是否超过最大限制
            var totalDays = (int)(endDate - startDate).TotalDays + 1;
            if (totalDays > 30)
            {
                result.Message = $"日期范围过大，最多支持 30 天（当前: {totalDays} 天）";
                _logger.LogWarning(result.Message);
                return result;
            }

            result.Success = true;
            return result;
        }

        /// <summary>
        /// 验证月份范围
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="maxMonths">最大月数</param>
        /// <returns>验证结果</returns>
        private BatchStatisticsUpdateResult ValidateMonthRange(
            DateTime startDate,
            DateTime endDate,
            int maxMonths = 12
        )
        {
            var result = new BatchStatisticsUpdateResult { Success = false };

            // 验证开始日期是否小于等于结束日期
            if (startDate > endDate)
            {
                result.Message = "开始日期不能大于结束日期";
                _logger.LogWarning(result.Message);
                return result;
            }

            // 计算月数
            var totalMonths =
                ((endDate.Year - startDate.Year) * 12) + endDate.Month - startDate.Month + 1;
            if (totalMonths > maxMonths)
            {
                result.Message =
                    $"月份范围过大，最多支持 {maxMonths} 个月（当前: {totalMonths} 个月）";
                _logger.LogWarning(result.Message);
                return result;
            }

            result.Success = true;
            return result;
        }

        /// <summary>
        /// 按月份批量全量刷新数据
        /// 刷新指定月份范围内的所有统计数据
        /// </summary>
        /// <param name="startYearMonth">开始年月（格式yyyy-MM）</param>
        /// <param name="endYearMonth">结束年月（格式yyyy-MM）</param>
        /// <param name="maxMonths">最大月数</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchFullRefreshByMonths(
            string startYearMonth,
            string endYearMonth,
            int maxMonths = 12
        )
        {
            var result = new BatchStatisticsUpdateResult();

            // 解析开始年月
            if (
                !DateTime.TryParseExact(
                    startYearMonth,
                    "yyyy-MM",
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out var startDate
                )
            )
            {
                result.Message = $"开始年月格式错误，应为 yyyy-MM 格式（如 2024-01）";
                _logger.LogWarning(result.Message);
                return result;
            }

            // 解析结束年月
            if (
                !DateTime.TryParseExact(
                    endYearMonth,
                    "yyyy-MM",
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out var endDate
                )
            )
            {
                result.Message = $"结束年月格式错误，应为 yyyy-MM 格式（如 2024-06）";
                _logger.LogWarning(result.Message);
                return result;
            }

            // 设置开始日期为该月第一天
            startDate = new DateTime(startDate.Year, startDate.Month, 1);
            // 设置结束日期为该月最后一天
            var endMonthLastDay = new DateTime(
                endDate.Year,
                endDate.Month,
                DateTime.DaysInMonth(endDate.Year, endDate.Month)
            );

            // 验证月份范围
            var validation = ValidateMonthRange(startDate, endMonthLastDay, maxMonths);
            if (!validation.Success)
            {
                return validation;
            }

            result.TotalMonths =
                ((endMonthLastDay.Year - startDate.Year) * 12)
                + endMonthLastDay.Month
                - startDate.Month
                + 1;
            result.TotalDays = (int)(endMonthLastDay - startDate).TotalDays + 1;

            _logger.LogInformation(
                "开始批量按月份刷新完整数据: {StartYearMonth} 至 {EndYearMonth}, 共 {Months} 个月, {Days} 天",
                startYearMonth,
                endYearMonth,
                result.TotalMonths,
                result.TotalDays
            );

            // 用于统计每个月的失败情况
            var monthStats = new Dictionary<string, (int success, int failed)>();

            // 逐日更新统计数据
            for (var date = startDate; date <= endMonthLastDay; date = date.AddDays(1))
            {
                try
                {
                    var monthKey = date.ToString("yyyy-MM");
                    if (!monthStats.ContainsKey(monthKey))
                    {
                        monthStats[monthKey] = (0, 0);
                    }

                    // 更新每日统计
                    await UpdateDailyStatistics(date.ToString("yyyy-MM-dd"));
                    // 更新分时统计
                    await UpdateHourlyStatistics(date, null);
                    // 更新分店统计
                    await UpdateStoreStatistics(date);
                    //  await UpdateSupplierStatistics(date);
                    //  await UpdateStoreSupplierStatistics(date);
                    // 更新澳洲供应商门店统计
                    await UpdateAustralianSupplierStoreStatistics(date);
                    // 更新中国供应商门店统计
                    await UpdateChinaSupplierStoreStatistics(date);

                    result.ProcessedDays++;
                    monthStats[monthKey] = (
                        monthStats[monthKey].success + 1,
                        monthStats[monthKey].failed
                    );
                }
                catch (Exception ex)
                {
                    result.FailedDates.Add(date.ToString("yyyy-MM-dd"));
                    _logger.LogError(ex, "批量按月份刷新失败: {Date}", date);

                    var monthKey = date.ToString("yyyy-MM");
                    if (!monthStats.ContainsKey(monthKey))
                    {
                        monthStats[monthKey] = (0, 0);
                    }
                    monthStats[monthKey] = (
                        monthStats[monthKey].success,
                        monthStats[monthKey].failed + 1
                    );
                }
            }

            // 统计每个月的处理情况
            foreach (var (month, stats) in monthStats)
            {
                if (stats.failed > 0)
                {
                    result.FailedMonths.Add(month);
                }
                result.ProcessedMonths++;
            }

            result.Success = result.FailedDates.Count == 0;
            result.Message = result.Success
                ? $"批量按月份刷新完成: {result.ProcessedDays}/{result.TotalDays} 天, {result.ProcessedMonths}/{result.TotalMonths} 个月"
                : $"批量按月份刷新部分完成: {result.ProcessedDays}/{result.TotalDays} 天, {result.ProcessedMonths}/{result.TotalMonths} 个月, 失败 {result.FailedDates.Count} 天, 失败月份: {string.Join(", ", result.FailedMonths)}";

            _logger.LogInformation(result.Message);
            return result;
        }

        /// <summary>
        /// 并发全量刷新数据
        /// 将日期范围拆分为多个块，并发处理以提高效率
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="maxConcurrency">最大并发数，为空则使用配置值</param>
        /// <returns>批量更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> BatchFullRefreshConcurrent(
            DateTime startDate,
            DateTime endDate,
            int? maxConcurrency = null
        )
        {
            var result = new BatchStatisticsUpdateResult();

            // 验证日期范围
            if (startDate > endDate)
            {
                result.Message = "开始日期不能大于结束日期";
                _logger.LogWarning(result.Message);
                return result;
            }

            var totalDays = (int)(endDate - startDate).TotalDays + 1;

            // 检查日期范围是否超出并发更新支持的最大天数
            if (totalDays > _maxDaysForConcurrentUpdate)
            {
                result.Message =
                    $"日期范围过大，并发更新最多支持 {_maxDaysForConcurrentUpdate} 天（当前: {totalDays} 天）";
                _logger.LogWarning(result.Message);
                return result;
            }

            // 确定并发度
            var concurrency = maxConcurrency ?? _maxConcurrentUpdates;

            _logger.LogInformation(
                "开始并发完整刷新统计数据: {StartDate} 至 {EndDate}, 并发度: {Concurrency}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                concurrency
            );

            // 拆分日期范围为多个并发块
            var dateRanges = SplitDateRange(startDate, endDate, concurrency);
            _logger.LogInformation(
                "将 {TotalDays} 天拆分为 {ChunkCount} 个并发块",
                totalDays,
                dateRanges.Count
            );

            var processedDays = 0;
            var failedDates = new List<string>();
            var failedRanges = new List<string>();
            var syncLock = new object();

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = concurrency };

            // 启动计时器
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 并发处理每个日期块
                await Parallel.ForEachAsync(
                    dateRanges,
                    parallelOptions,
                    async (dateRange, cancellationToken) =>
                    {
                        try
                        {
                            _logger.LogInformation(
                                "开始处理并发块 {StartDate} 至 {EndDate} ({Days} 天)",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd"),
                                dateRange.DayCount
                            );

                            // 为每个并发任务创建独立的作用域和数据库上下文
                            using var scope = _serviceScopeFactory.CreateScope();
                            var context =
                                scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
                            var posmContext =
                                scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();
                            var logger = scope.ServiceProvider.GetRequiredService<
                                ILogger<SalesStatisticsJobService>
                            >();

                            // 调用带上下文的全量刷新方法
                            await FullRefreshDateRangeWithContext(
                                context,
                                posmContext,
                                logger,
                                dateRange.StartDate,
                                dateRange.EndDate
                            );

                            // 使用锁更新进度
                            lock (syncLock)
                            {
                                processedDays += dateRange.DayCount;
                            }

                            logger.LogInformation(
                                "并发块处理完成 {StartDate} 至 {EndDate} ({Days} 天), 累计进度: {Progress}/{Total}",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd"),
                                dateRange.DayCount,
                                processedDays,
                                totalDays
                            );
                        }
                        catch (Exception ex)
                        {
                            var rangeKey =
                                $"{dateRange.StartDate:yyyy-MM-dd} 至 {dateRange.EndDate:yyyy-MM-dd}";
                            failedRanges.Add(rangeKey);

                            // 记录失败的日期
                            for (
                                var d = dateRange.StartDate;
                                d <= dateRange.EndDate;
                                d = d.AddDays(1)
                            )
                            {
                                failedDates.Add(d.ToString("yyyy-MM-dd"));
                            }

                            _logger.LogError(
                                ex,
                                "并发块处理失败 {StartDate} 至 {EndDate}",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd")
                            );
                        }
                    }
                );

                stopwatch.Stop();

                result.TotalDays = totalDays;
                result.ProcessedDays = processedDays;
                result.FailedDates = failedDates;
                result.Success = failedDates.Count == 0;

                var avgTimePerDay = stopwatch.Elapsed.TotalSeconds / totalDays;
                result.Message = result.Success
                    ? $"并发完整刷新完成: {processedDays}/{totalDays} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}, 平均每天: {avgTimePerDay:F2}秒"
                    : $"并发完整刷新部分完成: {processedDays}/{totalDays} 天, 失败: {failedDates.Count} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}";

                _logger.LogInformation(
                    "{ResultMessage}, 失败的日期块: {FailedRanges}",
                    result.Message,
                    failedRanges.Count > 0 ? string.Join("; ", failedRanges) : "无"
                );
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Success = false;
                result.Message =
                    $"并发完整刷新失败: 处理 {processedDays}/{totalDays} 天, 错误: {ex.Message}";
                _logger.LogError(
                    ex,
                    "并发完整刷新失败: {StartDate} 至 {EndDate}",
                    startDate,
                    endDate
                );
            }

            return result;
        }

        /// <summary>
        /// 带上下文全量刷新日期范围（用于并发处理）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        private async Task FullRefreshDateRangeWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime startDate,
            DateTime endDate
        )
        {
            try
            {
                logger.LogInformation(
                    "开始完整刷新日期范围: {StartDate} 至 {EndDate} ({Days} 天)",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd"),
                    (int)(endDate - startDate).TotalDays + 1
                );

                // 逐日刷新所有统计数据
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    try
                    {
                        var dateStr = date.ToString("yyyy-MM-dd");

                        // 更新每日统计
                        await UpdateDailyStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            dateStr
                        );

                        // 更新分时统计
                        await UpdateHourlyStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date,
                            null
                        );

                        // 更新分店统计
                        await UpdateStoreStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date,
                            null
                        );

                        // 更新供应商统计
                        await UpdateSupplierStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date,
                            date,
                            null
                        );

                        // 更新门店供应商统计
                        await UpdateStoreSupplierStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date,
                            null,
                            null
                        );

                        // 更新商品分店每日统计，供热销商品和毛利率查询使用。
                        await UpdateProductStoreDailyStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date
                        );

                        // 更新澳洲供应商门店统计
                        await UpdateAustralianSupplierStoreStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date,
                            null,
                            null
                        );

                        // 更新中国供应商门店统计
                        await UpdateChinaSupplierStoreStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date,
                            null,
                            null
                        );

                        logger.LogInformation(
                            "日期 {Date} 完整刷新完成",
                            date.ToString("yyyy-MM-dd")
                        );
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "日期 {Date} 完整刷新失败",
                            date.ToString("yyyy-MM-dd")
                        );
                        throw;
                    }
                }

                logger.LogInformation(
                    "日期范围完整刷新完成: {StartDate} 至 {EndDate}",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd")
                );
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "日期范围完整刷新失败: {StartDate} 至 {EndDate}",
                    startDate,
                    endDate
                );
                throw;
            }
        }

        /// <summary>
        /// 带上下文更新每日统计数据（用于并发处理）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="dateStr">日期字符串</param>
        private async Task UpdateDailyStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            string dateStr
        )
        {
            try
            {
                var date = string.IsNullOrEmpty(dateStr)
                    ? DateTime.Now.Date
                    : DateTime.Parse(dateStr).Date;

                logger.LogInformation("开始更新每日统计数据: {Date}", date);

                // 从POSM数据库查询并聚合当日销售数据，按支付明细统计营业额
                var summary = await posmContext
                    .Db.Queryable<PaymentDetail, SalesOrder>(
                        (pd, so) => pd.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (pd, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime.Value.Date == date
                    )
                    .GroupBy((pd, so) => new { Date = so.OrderTime!.Value.Date })
                    .Select(
                        (pd, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(so.ItemCount) ?? 0,
                                OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                                SkuCount = SqlFunc.AggregateCount(so.OrderGuid),
                                CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                            }
                    )
                    .FirstAsync();

                if (summary != null)
                {
                    // 构建统计数据对象
                    var statistic = new DailySalesStatistic
                    {
                        Date = summary.Date,
                        TotalAmount = summary.TotalAmount,
                        TotalQuantity = summary.TotalQuantity,
                        OrderCount = summary.OrderCount,
                        SkuCount = summary.SkuCount,
                        CustomerCount = summary.CustomerCount,
                        AverageOrderValue =
                            summary.OrderCount > 0 ? summary.TotalAmount / summary.OrderCount : 0m,
                        UpdateTime = DateTime.Now,
                    };

                    // 查询是否已存在该日期的统计数据
                    var existing = await context
                        .Db.Queryable<DailySalesStatistic>()
                        .Where(s => s.Date == date)
                        .FirstAsync();

                    if (existing != null)
                    {
                        // 存在则更新
                        await context.Db.Updateable(statistic).ExecuteCommandAsync();
                    }
                    else
                    {
                        // 不存在则插入
                        await context.Db.Insertable(statistic).ExecuteCommandAsync();
                    }
                }

                logger.LogInformation("每日统计数据更新完成: {Date}", date);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新每日统计数据失败: {Date}", dateStr);
                throw;
            }
        }

        /// <summary>
        /// 带上下文更新分时统计数据（用于并发处理）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="date">目标日期</param>
        /// <param name="hour">指定小时，为空则更新所有小时</param>
        private async Task UpdateHourlyStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime date,
            int? hour
        )
        {
            try
            {
                // 确定要更新的小时列表
                var targetHours = hour.HasValue
                    ? new[] { hour.Value }
                    : Enumerable.Range(0, 24).ToArray();

                logger.LogInformation(
                    "开始更新分时统计数据: {Date}, 小时: {Hours}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23"
                );

                // 从POSM数据库查询分时销售数据，按支付明细统计营业额
                var allHourlyData = await posmContext
                    .Db.Queryable<PaymentDetail, SalesOrder>(
                        (pd, so) => pd.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (pd, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime.Value.Date == date
                            && targetHours.Contains(so.OrderTime.Value.Hour)
                    )
                    .GroupBy(
                        (pd, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                so.BranchCode,
                            }
                    )
                    .Select(
                        (pd, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(so.ItemCount) ?? 0,
                                OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                                CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                            }
                    )
                    .ToListAsync();

                if (!allHourlyData.Any())
                {
                    logger.LogInformation("没有找到销售数据: {Date}", date);
                    return;
                }

                // 获取所有分店代码
                var branchCodes = allHourlyData
                    .Select(d => d.BranchCode)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .ToList();

                // 查询分店信息
                var stores = await context
                    .Db.Queryable<Store>()
                    .Where(s => branchCodes.Contains(s.StoreCode))
                    .ToListAsync();

                var storeDict = stores.ToDictionary(s => s.StoreCode, s => s);

                var statisticsList = new List<HourlySalesStatistic>();

                // 为每个小时创建全店汇总记录
                foreach (var h in targetHours)
                {
                    var hourlyDataForHour = allHourlyData.Where(d => d.Hour == h).ToList();

                    if (hourlyDataForHour.Any())
                    {
                        var allStoreData = new HourlySalesStatistic
                        {
                            Date = date,
                            Hour = h,
                            BranchCode = "ALL",
                            BranchName = "All Stores",
                            TotalAmount = hourlyDataForHour.Sum(d => d.TotalAmount),
                            TotalQuantity = (int)hourlyDataForHour.Sum(d => d.TotalQuantity),
                            CustomerCount = hourlyDataForHour.Sum(d => d.CustomerCount),
                            AverageOrderValue =
                                hourlyDataForHour.Sum(d => d.OrderCount) > 0
                                    ? hourlyDataForHour.Sum(d => d.TotalAmount)
                                        / hourlyDataForHour.Sum(d => d.OrderCount)
                                    : 0m,
                            UpdateTime = DateTime.Now,
                        };
                        statisticsList.Add(allStoreData);
                    }
                }

                LogSkippedBranchCodeRows(
                    "分时分店销售统计",
                    allHourlyData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 为每个分店创建分时统计记录
                foreach (var data in allHourlyData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var store = storeDict.GetValueOrDefault(branchCode);

                    var storeStatistic = new HourlySalesStatistic
                    {
                        Date = data.Date,
                        Hour = data.Hour,
                        BranchCode = branchCode,
                        BranchName = store?.StoreName ?? branchCode,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        CustomerCount = data.CustomerCount,
                        AverageOrderValue =
                            data.OrderCount > 0 ? data.TotalAmount / data.OrderCount : 0m,
                        UpdateTime = DateTime.Now,
                    };
                    statisticsList.Add(storeStatistic);
                }

                // 查询数据库中已存在的记录
                var existingRecords = await context
                    .Db.Queryable<HourlySalesStatistic>()
                    .Where(s => s.Date == date && targetHours.Contains(s.Hour))
                    .ToListAsync();

                // 构建已存在记录的字典，用于快速查找
                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.Hour}_{s.BranchCode}",
                    s => s
                );

                var toInsert = new List<HourlySalesStatistic>();
                var toUpdate = new List<HourlySalesStatistic>();

                // 遍历统计数据，区分插入和更新操作
                foreach (var stat in statisticsList)
                {
                    var key = $"{stat.Date}_{stat.Hour}_{stat.BranchCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        stat.Date = existing.Date;
                        stat.Hour = existing.Hour;
                        stat.BranchCode = existing.BranchCode;
                        toUpdate.Add(stat);
                    }
                    else
                    {
                        toInsert.Add(stat);
                    }
                }

                // 批量插入新记录
                if (toInsert.Any())
                {
                    context
                        .Db.Fastest<HourlySalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkCopy(toInsert);
                    logger.LogInformation("批量插入 {Count} 条分时统计记录", toInsert.Count);
                }

                // 批量更新已存在记录
                if (toUpdate.Any())
                {
                    context
                        .Db.Fastest<HourlySalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkUpdate(toUpdate);
                    logger.LogInformation("批量更新 {Count} 条分时统计记录", toUpdate.Count);
                }

                logger.LogInformation(
                    "分时统计数据更新完成: {Date}, 小时: {Hours}, 总记录: {Total}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23",
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新分时统计数据失败: {Date} {Hour}", date, hour);
                throw;
            }
        }

        /// <summary>
        /// 带上下文更新分店统计数据（用于并发处理）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="date">目标日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        private async Task UpdateStoreStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime date,
            List<string>? branchCodes
        )
        {
            try
            {
                logger.LogInformation(
                    "开始更新指定分店统计数据: {Date}, Branches: {Branches}",
                    date,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All"
                );

                var statisticsList = await BuildStoreStatisticsAsync(
                    context,
                    posmContext,
                    date,
                    branchCodes
                );

                // 查询数据库中已存在的记录
                var allBranchCodes = statisticsList.Select(s => s.BranchCode).Distinct().ToList();
                var existingRecords = allBranchCodes.Any()
                    ? await context
                        .Db.Queryable<StoreSalesStatistic>()
                        .Where(s => s.Date == date && allBranchCodes.Contains(s.BranchCode))
                        .ToListAsync()
                    : new List<StoreSalesStatistic>();

                // 构建已存在记录的字典，用于快速查找
                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.BranchCode}",
                    s => s
                );

                var toInsert = new List<StoreSalesStatistic>();
                var toUpdate = new List<StoreSalesStatistic>();

                // 遍历统计数据，区分插入和更新操作
                foreach (var stat in statisticsList)
                {
                    var key = $"{stat.Date}_{stat.BranchCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 更新已存在记录的字段值
                        existing.BranchName = stat.BranchName;
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.OrderCount = stat.OrderCount;
                        existing.CustomerCount = stat.CustomerCount;
                        existing.AverageOrderValue = stat.AverageOrderValue;
                        existing.UpdateTime = stat.UpdateTime;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新记录，加入插入列表
                        toInsert.Add(stat);
                    }
                }

                // 批量插入新记录
                if (toInsert.Any())
                {
                    context
                        .Db.Fastest<StoreSalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkCopy(toInsert);
                    logger.LogInformation("批量插入 {Count} 条分店统计记录", toInsert.Count);
                }

                // 批量更新已存在记录
                if (toUpdate.Any())
                {
                    context
                        .Db.Fastest<StoreSalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkUpdate(toUpdate);
                    logger.LogInformation("批量更新 {Count} 条分店统计记录", toUpdate.Count);
                }

                logger.LogInformation(
                    "指定分店统计数据更新完成: {Date}, 总记录: {Total}",
                    date,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新指定分店统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 带上下文更新门店供应商统计数据（用于并发处理）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="date">目标日期，为空则更新当天</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        private async Task UpdateStoreSupplierStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime? date,
            List<string>? branchCodes,
            List<string>? supplierCodes
        )
        {
            try
            {
                var targetDate = date ?? DateTime.Now.Date;

                logger.LogInformation(
                    "开始更新门店供应商统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    targetDate,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All",
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                // 构建查询
                var query = posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime.Value.Date == targetDate
                    );

                // 设置分店过滤条件
                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(o =>
                        o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                    );
                }

                // 设置供应商过滤条件
                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            m.LocalSupplierCode != null
                            && supplierCodes.Contains(m.LocalSupplierCode)
                    );
                }

                // 查询并聚合门店供应商销售数据
                var storeSupplierData = await query
                    .GroupBy(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                            }
                    )
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                                OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                            }
                    )
                    .ToListAsync();

                // 获取所有本地供应商代码
                var allLocalSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.LocalSupplierCode))
                    .Select(d => d.LocalSupplierCode!)
                    .Distinct()
                    .ToList();

                // 查询本地供应商信息
                var localSupplierDict = new Dictionary<string, HBLocalSupplier>();
                if (allLocalSupplierCodes.Any())
                {
                    var localSuppliers = await context.HBLocalSupplierDb.GetListAsync(s =>
                        s.LocalSupplierCode != null
                        && allLocalSupplierCodes.Contains(s.LocalSupplierCode)
                        && !s.IsDeleted
                    );
                    localSupplierDict = localSuppliers.ToDictionary(
                        s => s.LocalSupplierCode!,
                        s => s
                    );
                }

                // 获取所有国内供应商代码
                var allChinaSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.ChinaSupplierCode))
                    .Select(d => d.ChinaSupplierCode!)
                    .Distinct()
                    .ToList();

                // 查询国内供应商信息
                var chinaSupplierDict = new Dictionary<string, ChinaSupplier>();
                if (allChinaSupplierCodes.Any())
                {
                    var chinaSuppliers = await context.ChinaSupplierDb.GetListAsync(cs =>
                        cs.SupplierCode != null
                        && allChinaSupplierCodes.Contains(cs.SupplierCode)
                        && !cs.IsDeleted
                    );
                    chinaSupplierDict = chinaSuppliers
                        .Where(cs => !string.IsNullOrEmpty(cs.SupplierCode))
                        .ToDictionary(cs => cs.SupplierCode!, cs => cs);
                }

                var statisticsList = new List<StoreSupplierSalesDetail>();

                LogSkippedBranchCodeRows(
                    "分店供应商销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 构建每个门店供应商的统计记录
                foreach (var data in storeSupplierData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var localSupplierCode = data.LocalSupplierCode ?? string.Empty;
                    var chinaSupplierCode = data.ChinaSupplierCode;

                    var supplierCode = localSupplierCode;
                    var supplierName = localSupplierCode;
                    var isDomestic = false;

                    // 判断供应商类型并获取供应商名称
                    if (localSupplierCode == "200" && !string.IsNullOrEmpty(chinaSupplierCode))
                    {
                        // 国内供应商
                        supplierCode = chinaSupplierCode;
                        isDomestic = true;
                        if (chinaSupplierDict.TryGetValue(chinaSupplierCode, out var cs))
                        {
                            supplierName = cs.SupplierName ?? supplierCode;
                        }
                    }
                    else
                    {
                        // 本地供应商
                        if (localSupplierDict.TryGetValue(localSupplierCode, out var ls))
                        {
                            supplierName = ls.Name ?? localSupplierCode;
                            isDomestic = false;
                        }
                    }

                    var statistic = new StoreSupplierSalesDetail
                    {
                        Date = data.Date,
                        BranchCode = branchCode,
                        SupplierCode = supplierCode,
                        SupplierName = supplierName,
                        IsDomestic = isDomestic,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        OrderCount = data.OrderCount,
                        UpdateTime = DateTime.Now,
                    };

                    statisticsList.Add(statistic);
                }

                var allBranchCodes = statisticsList.Select(s => s.BranchCode).Distinct().ToList();
                var allSupplierCodes = statisticsList
                    .Select(s => s.SupplierCode)
                    .Distinct()
                    .ToList();

                // 查询数据库中已存在的记录
                var existingRecords = await context
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s =>
                        s.Date == targetDate
                        && allBranchCodes.Contains(s.BranchCode)
                        && allSupplierCodes.Contains(s.SupplierCode)
                    )
                    .ToListAsync();

                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.BranchCode}_{s.SupplierCode}",
                    s => s
                );

                var toInsert = new List<StoreSupplierSalesDetail>();
                var toUpdate = new List<StoreSupplierSalesDetail>();

                // 遍历统计数据，区分插入和更新操作
                foreach (var stat in statisticsList)
                {
                    var key = $"{stat.Date}_{stat.BranchCode}_{stat.SupplierCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 更新已存在记录的字段值
                        existing.SupplierName = stat.SupplierName;
                        existing.IsDomestic = stat.IsDomestic;
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.OrderCount = stat.OrderCount;
                        existing.UpdateTime = stat.UpdateTime;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新记录，加入插入列表
                        toInsert.Add(stat);
                    }
                }

                // 批量插入新记录
                if (toInsert.Any())
                {
                    context
                        .Db.Fastest<StoreSupplierSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkCopy(toInsert);
                    logger.LogInformation("批量插入 {Count} 条门店供应商统计记录", toInsert.Count);
                }

                // 批量更新已存在记录
                if (toUpdate.Any())
                {
                    context
                        .Db.Fastest<StoreSupplierSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkUpdate(toUpdate);
                    logger.LogInformation("批量更新 {Count} 条门店供应商统计记录", toUpdate.Count);
                }

                logger.LogInformation(
                    "门店供应商统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新门店供应商统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 更新澳洲供应商门店统计数据
        /// </summary>
        /// <param name="date">目标日期，默认为当前日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        public async Task UpdateAustralianSupplierStoreStatistics(
            DateTime? date = null,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        )
        {
            try
            {
                var targetDate = date ?? DateTime.Now.Date;

                _logger.LogInformation(
                    "开始更新澳洲供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    targetDate,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All",
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                // 构建查询
                var query = _posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime.Value.Date == targetDate
                    );

                // 设置分店过滤条件
                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(o =>
                        o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                    );
                }

                // 设置供应商过滤条件
                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            m.LocalSupplierCode != null
                            && supplierCodes.Contains(m.LocalSupplierCode)
                    );
                }

                // 查询并聚合澳洲供应商门店销售数据
                var storeSupplierData = await query
                    .GroupBy(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                            }
                    )
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                                TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                                OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                            }
                    )
                    .ToListAsync();

                // 获取所有本地供应商代码
                var allLocalSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.LocalSupplierCode))
                    .Select(d => d.LocalSupplierCode!)
                    .Distinct()
                    .ToList();

                // 查询本地供应商信息
                var localSupplierDict = new Dictionary<string, HBLocalSupplier>();
                if (allLocalSupplierCodes.Any())
                {
                    var localSuppliers = await _context.HBLocalSupplierDb.GetListAsync(s =>
                        s.LocalSupplierCode != null
                        && allLocalSupplierCodes.Contains(s.LocalSupplierCode)
                        && !s.IsDeleted
                    );
                    localSupplierDict = localSuppliers.ToDictionary(
                        s => s.LocalSupplierCode!,
                        s => s
                    );
                }

                var statisticsList = new List<AustralianSupplierStoreSalesDetail>();

                LogSkippedBranchCodeRows(
                    "澳洲供应商分店销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 构建每个澳洲供应商门店的统计记录
                foreach (var data in storeSupplierData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var localSupplierCode = data.LocalSupplierCode ?? string.Empty;

                    var supplierCode = localSupplierCode;
                    var supplierName = localSupplierCode;

                    // 获取供应商名称
                    if (localSupplierDict.TryGetValue(localSupplierCode, out var ls))
                    {
                        supplierName = ls.Name ?? localSupplierCode;
                    }

                    var statistic = new AustralianSupplierStoreSalesDetail
                    {
                        Date = data.Date,
                        BranchCode = branchCode,
                        SupplierCode = supplierCode,
                        SupplierName = supplierName,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        OrderCount = data.OrderCount,
                        UpdateTime = DateTime.Now,
                    };

                    statisticsList.Add(statistic);
                }

                // 如果没有数据则返回
                if (!statisticsList.Any())
                {
                    _logger.LogInformation("没有找到澳洲供应商门店数据: {Date}", targetDate);
                    return;
                }

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 按日期删除该日期的所有旧数据
                        var deletedCount = await _context
                            .Db.Deleteable<AustralianSupplierStoreSalesDetail>()
                            .Where(s => s.Date == targetDate)
                            .ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条澳洲供应商门店统计旧记录", deletedCount);

                        // 批量插入新记录
                        _context
                            .Db.Fastest<AustralianSupplierStoreSalesDetail>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                    },
                    commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                    logger: _logger,
                    operationName: "澳洲供应商门店统计数据更新"
                );

                _logger.LogInformation(
                    "澳洲供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新澳洲供应商门店统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 更新中国供应商门店统计数据
        /// </summary>
        /// <param name="date">目标日期，默认为当前日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        public async Task UpdateChinaSupplierStoreStatistics(
            DateTime? date = null,
            List<string>? branchCodes = null,
            List<string>? supplierCodes = null
        )
        {
            try
            {
                var targetDate = date ?? DateTime.Now.Date;

                _logger.LogInformation(
                    "开始更新中国供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    targetDate,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All",
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                // 构建查询（只查询LocalSupplierCode为"200"的记录，即国内供应商记录）
                var query = _posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(
                        (o, d, m) =>
                            o.Status != null
                            && (o.Status == 1 || o.Status == 4)
                            && o.OrderTime != null
                            && o.OrderTime.Value.Date == targetDate
                            && m.LocalSupplierCode == "200"
                    );

                // 设置分店过滤条件
                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(o =>
                        o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                    );
                }

                // 设置供应商过滤条件（按国内供应商代码）
                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            m.ChinaSupplierCode != null
                            && supplierCodes.Contains(m.ChinaSupplierCode)
                    );
                }

                // 查询并聚合中国供应商门店销售数据
                var storeSupplierData = await query
                    .GroupBy(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                            }
                    )
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                                OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                            }
                    )
                    .ToListAsync();

                // 获取所有国内供应商代码
                var allChinaSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.ChinaSupplierCode))
                    .Select(d => d.ChinaSupplierCode!)
                    .Distinct()
                    .ToList();

                // 查询国内供应商信息
                var chinaSupplierDict = new Dictionary<string, ChinaSupplier>();
                if (allChinaSupplierCodes.Any())
                {
                    var chinaSuppliers = await _context.ChinaSupplierDb.GetListAsync(cs =>
                        cs.SupplierCode != null
                        && allChinaSupplierCodes.Contains(cs.SupplierCode)
                        && !cs.IsDeleted
                    );
                    chinaSupplierDict = chinaSuppliers
                        .Where(cs => !string.IsNullOrEmpty(cs.SupplierCode))
                        .ToDictionary(cs => cs.SupplierCode!, cs => cs);
                }

                var statisticsList = new List<ChinaSupplierStoreSalesDetail>();

                LogSkippedBranchCodeRows(
                    "中国供应商分店销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 构建每个中国供应商门店的统计记录
                foreach (var data in storeSupplierData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var chinaSupplierCode = data.ChinaSupplierCode ?? string.Empty;

                    if (string.IsNullOrEmpty(chinaSupplierCode))
                        continue;

                    var supplierCode = chinaSupplierCode;
                    var supplierName = chinaSupplierCode;

                    // 获取供应商名称
                    if (chinaSupplierDict.TryGetValue(chinaSupplierCode, out var cs))
                    {
                        supplierName = cs.SupplierName ?? supplierCode;
                    }

                    var statistic = new ChinaSupplierStoreSalesDetail
                    {
                        Date = data.Date,
                        BranchCode = branchCode,
                        SupplierCode = supplierCode,
                        SupplierName = supplierName,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        OrderCount = data.OrderCount,
                        UpdateTime = DateTime.Now,
                    };

                    statisticsList.Add(statistic);
                }

                // 如果没有数据则返回
                if (!statisticsList.Any())
                {
                    _logger.LogInformation("没有找到中国供应商门店数据: {Date}", targetDate);
                    return;
                }

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 按日期删除该日期的所有旧数据
                        var deletedCount = await _context
                            .Db.Deleteable<ChinaSupplierStoreSalesDetail>()
                            .Where(s => s.Date == targetDate)
                            .ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条中国供应商门店统计旧记录", deletedCount);

                        // 批量插入新记录
                        _context
                            .Db.Fastest<ChinaSupplierStoreSalesDetail>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                    },
                    commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                    logger: _logger,
                    operationName: "中国供应商门店统计数据更新"
                );

                _logger.LogInformation(
                    "中国供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新中国供应商门店统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 更新澳洲供应商门店统计数据（并发上下文版本）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="date">目标日期，默认为当前日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        private async Task UpdateAustralianSupplierStoreStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime? date,
            List<string>? branchCodes,
            List<string>? supplierCodes
        )
        {
            try
            {
                var targetDate = date ?? DateTime.Now.Date;

                logger.LogInformation(
                    "开始更新澳洲供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    targetDate,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All",
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                var query = posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime.Value.Date == targetDate
                    );

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(o =>
                        o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                    );
                }

                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            m.LocalSupplierCode != null
                            && supplierCodes.Contains(m.LocalSupplierCode)
                    );
                }

                var storeSupplierData = await query
                    .GroupBy(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                            }
                    )
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                LocalSupplierCode = m.LocalSupplierCode,
                                TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                                OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                            }
                    )
                    .ToListAsync();

                var allLocalSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.LocalSupplierCode))
                    .Select(d => d.LocalSupplierCode!)
                    .Distinct()
                    .ToList();

                var localSupplierDict = new Dictionary<string, HBLocalSupplier>();
                if (allLocalSupplierCodes.Any())
                {
                    var localSuppliers = await context.HBLocalSupplierDb.GetListAsync(s =>
                        s.LocalSupplierCode != null
                        && allLocalSupplierCodes.Contains(s.LocalSupplierCode)
                        && !s.IsDeleted
                    );
                    localSupplierDict = localSuppliers.ToDictionary(
                        s => s.LocalSupplierCode!,
                        s => s
                    );
                }

                var statisticsList = new List<AustralianSupplierStoreSalesDetail>();

                LogSkippedBranchCodeRows(
                    "澳洲供应商分店销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 构建每个澳洲供应商门店的统计记录
                foreach (var data in storeSupplierData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var localSupplierCode = data.LocalSupplierCode ?? string.Empty;

                    var supplierCode = localSupplierCode;
                    var supplierName = localSupplierCode;

                    // 获取供应商名称
                    if (localSupplierDict.TryGetValue(localSupplierCode, out var ls))
                    {
                        supplierName = ls.Name ?? localSupplierCode;
                    }

                    var statistic = new AustralianSupplierStoreSalesDetail
                    {
                        Date = data.Date,
                        BranchCode = branchCode,
                        SupplierCode = supplierCode,
                        SupplierName = supplierName,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        OrderCount = data.OrderCount,
                        UpdateTime = DateTime.Now,
                    };

                    statisticsList.Add(statistic);
                }

                // 如果没有数据则返回
                if (!statisticsList.Any())
                {
                    logger.LogInformation("没有找到澳洲供应商门店数据: {Date}", targetDate);
                    return;
                }

                // 获取所有分店和供应商代码
                var allBranchCodes = statisticsList.Select(s => s.BranchCode).Distinct().ToList();
                var allSupplierCodes = statisticsList
                    .Select(s => s.SupplierCode)
                    .Distinct()
                    .ToList();

                // 查询数据库中已存在的记录
                var existingRecords = await context
                    .Db.Queryable<AustralianSupplierStoreSalesDetail>()
                    .Where(s =>
                        s.Date == targetDate
                        && allBranchCodes.Contains(s.BranchCode)
                        && allSupplierCodes.Contains(s.SupplierCode)
                    )
                    .ToListAsync();

                // 构建已存在记录的字典，用于快速查找
                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.BranchCode}_{s.SupplierCode}",
                    s => s
                );

                var toInsert = new List<AustralianSupplierStoreSalesDetail>();
                var toUpdate = new List<AustralianSupplierStoreSalesDetail>();

                // 遍历统计数据，区分插入和更新操作
                foreach (var stat in statisticsList)
                {
                    var key = $"{stat.Date}_{stat.BranchCode}_{stat.SupplierCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 记录已存在，更新字段值
                        existing.SupplierName = stat.SupplierName;
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.OrderCount = stat.OrderCount;
                        existing.UpdateTime = stat.UpdateTime;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新记录，加入插入列表
                        toInsert.Add(stat);
                    }
                }

                // 批量插入新记录
                if (toInsert.Any())
                {
                    context
                        .Db.Fastest<AustralianSupplierStoreSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkCopy(toInsert);
                    logger.LogInformation(
                        "批量插入 {Count} 条澳洲供应商门店统计记录",
                        toInsert.Count
                    );
                }

                // 批量更新已存在记录
                if (toUpdate.Any())
                {
                    context
                        .Db.Fastest<AustralianSupplierStoreSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkUpdate(toUpdate);
                    logger.LogInformation(
                        "批量更新 {Count} 条澳洲供应商门店统计记录",
                        toUpdate.Count
                    );
                }

                logger.LogInformation(
                    "澳洲供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新澳洲供应商门店统计数据失败: {Date}", date);
                throw;
            }
        }

        /// <summary>
        /// 更新中国供应商门店统计数据（并发上下文版本）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="date">目标日期，默认为当前日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
        private async Task UpdateChinaSupplierStoreStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime? date,
            List<string>? branchCodes,
            List<string>? supplierCodes
        )
        {
            try
            {
                var targetDate = date ?? DateTime.Now.Date;

                logger.LogInformation(
                    "开始更新中国供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    targetDate,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All",
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                var query = posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(
                        (o, d, m) =>
                            o.Status != null
                            && (o.Status == 1 || o.Status == 4)
                            && o.OrderTime != null
                            && o.OrderTime.Value.Date == targetDate
                            && m.LocalSupplierCode == "200"
                    );

                if (branchCodes != null && branchCodes.Any())
                {
                    query = query.Where(o =>
                        o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                    );
                }

                if (supplierCodes != null && supplierCodes.Any())
                {
                    query = query.Where(
                        (o, d, m) =>
                            m.ChinaSupplierCode != null
                            && supplierCodes.Contains(m.ChinaSupplierCode)
                    );
                }

                var storeSupplierData = await query
                    .GroupBy(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                            }
                    )
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode!,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                                TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                                OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                            }
                    )
                    .ToListAsync();

                var allChinaSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.ChinaSupplierCode))
                    .Select(d => d.ChinaSupplierCode!)
                    .Distinct()
                    .ToList();

                var chinaSupplierDict = new Dictionary<string, ChinaSupplier>();
                if (allChinaSupplierCodes.Any())
                {
                    var chinaSuppliers = await context.ChinaSupplierDb.GetListAsync(cs =>
                        cs.SupplierCode != null
                        && allChinaSupplierCodes.Contains(cs.SupplierCode)
                        && !cs.IsDeleted
                    );
                    chinaSupplierDict = chinaSuppliers
                        .Where(cs => !string.IsNullOrEmpty(cs.SupplierCode))
                        .ToDictionary(cs => cs.SupplierCode!, cs => cs);
                }

                var statisticsList = new List<ChinaSupplierStoreSalesDetail>();

                LogSkippedBranchCodeRows(
                    "中国供应商分店销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.TotalAmount,
                    data => data.TotalQuantity
                );

                // 构建每个中国供应商门店的统计记录
                foreach (var data in storeSupplierData)
                {
                    // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                    if (string.IsNullOrWhiteSpace(data.BranchCode))
                        continue;
                    var branchCode = data.BranchCode;
                    var chinaSupplierCode = data.ChinaSupplierCode ?? string.Empty;

                    if (string.IsNullOrEmpty(chinaSupplierCode))
                        continue;

                    var supplierCode = chinaSupplierCode;
                    var supplierName = chinaSupplierCode;

                    // 获取供应商名称
                    if (chinaSupplierDict.TryGetValue(chinaSupplierCode, out var cs))
                    {
                        supplierName = cs.SupplierName ?? supplierCode;
                    }

                    var statistic = new ChinaSupplierStoreSalesDetail
                    {
                        Date = data.Date,
                        BranchCode = branchCode,
                        SupplierCode = supplierCode,
                        SupplierName = supplierName,
                        TotalAmount = data.TotalAmount,
                        TotalQuantity = (int)data.TotalQuantity,
                        OrderCount = data.OrderCount,
                        UpdateTime = DateTime.Now,
                    };

                    statisticsList.Add(statistic);
                }

                // 如果没有数据则返回
                if (!statisticsList.Any())
                {
                    logger.LogInformation("没有找到中国供应商门店数据: {Date}", targetDate);
                    return;
                }

                // 获取所有分店和供应商代码
                var allBranchCodes = statisticsList.Select(s => s.BranchCode).Distinct().ToList();
                var allSupplierCodes = statisticsList
                    .Select(s => s.SupplierCode)
                    .Distinct()
                    .ToList();

                // 查询数据库中已存在的记录
                var existingRecords = await context
                    .Db.Queryable<ChinaSupplierStoreSalesDetail>()
                    .Where(s =>
                        s.Date == targetDate
                        && allBranchCodes.Contains(s.BranchCode)
                        && allSupplierCodes.Contains(s.SupplierCode)
                    )
                    .ToListAsync();

                // 构建已存在记录的字典，用于快速查找
                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.BranchCode}_{s.SupplierCode}",
                    s => s
                );

                var toInsert = new List<ChinaSupplierStoreSalesDetail>();
                var toUpdate = new List<ChinaSupplierStoreSalesDetail>();

                // 遍历统计数据，区分插入和更新操作
                foreach (var stat in statisticsList)
                {
                    var key = $"{stat.Date}_{stat.BranchCode}_{stat.SupplierCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 记录已存在，更新字段值
                        existing.SupplierName = stat.SupplierName;
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.OrderCount = stat.OrderCount;
                        existing.UpdateTime = stat.UpdateTime;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新记录，加入插入列表
                        toInsert.Add(stat);
                    }
                }

                // 批量插入新记录
                if (toInsert.Any())
                {
                    context
                        .Db.Fastest<ChinaSupplierStoreSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkCopy(toInsert);
                    logger.LogInformation(
                        "批量插入 {Count} 条中国供应商门店统计记录",
                        toInsert.Count
                    );
                }

                // 批量更新已存在记录
                if (toUpdate.Any())
                {
                    context
                        .Db.Fastest<ChinaSupplierStoreSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkUpdate(toUpdate);
                    logger.LogInformation(
                        "批量更新 {Count} 条中国供应商门店统计记录",
                        toUpdate.Count
                    );
                }

                logger.LogInformation(
                    "中国供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新中国供应商门店统计数据失败: {Date}", date);
                throw;
            }
        }

        private void LogSkippedBranchCodeRows<T>(
            string statisticName,
            IEnumerable<T> rows,
            Func<T, string?> branchCodeSelector,
            Func<T, decimal> amountSelector,
            Func<T, decimal> quantitySelector
        )
        {
            var skippedRows = rows
                .Where(row => string.IsNullOrWhiteSpace(branchCodeSelector(row)))
                .ToList();
            if (skippedRows.Count == 0)
            {
                return;
            }

            // 分店维度不写空编码统计行，但必须把跳过口径写入日志，便于排查源数据异常。
            _logger.LogWarning(
                "{StatisticName} 跳过 {Count} 条缺少分店编码的销售记录，金额合计 {Amount}，数量合计 {Quantity}",
                statisticName,
                skippedRows.Count,
                skippedRows.Sum(amountSelector),
                skippedRows.Sum(quantitySelector)
            );
        }
    }
}
