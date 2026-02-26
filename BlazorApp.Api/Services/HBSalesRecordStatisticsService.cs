using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 销售统计结果集合
    /// </summary>
    public class HBSalesStatisticsResult
    {
        public List<DailySalesStatistic> DailyStatistics { get; set; } = new();
        public List<HourlySalesStatistic> HourlyStatistics { get; set; } = new();
        public List<StoreSalesStatistic> StoreStatistics { get; set; } = new();
        public List<SupplierSalesStatistic> SupplierStatistics { get; set; } = new();
        public List<StoreSupplierSalesDetail> StoreSupplierStatistics { get; set; } = new();
        public List<AustralianSupplierStoreSalesDetail> AustralianSupplierStoreSalesDetails { get; set; } =
            new();
        public List<ChinaSupplierStoreSalesDetail> ChinaSupplierStoreSalesDetails { get; set; } =
            new();
    }

    /// <summary>
    /// 销售数据聚合实体
    /// </summary>
    public class SalesDataAggregate
    {
        public DateTime Date { get; set; }
        public int Hour { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal TotalQuantity { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public int CustomerCount { get; set; }
    }

    /// <summary>
    /// 供应商名称集合
    /// </summary>
    public class SupplierNames
    {
        public Dictionary<string, string> LocalSuppliers { get; set; } = new();
        public Dictionary<string, string> ChinaSuppliers { get; set; } = new();
    }

    /// <summary>
    /// HB销售记录统计服务
    /// 负责从HBSales和POSM数据库导入销售数据并生成各种统计报表
    /// </summary>
    public class HBSalesRecordStatisticsService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<HBSalesRecordStatisticsService> _logger;
        private readonly int _maxConcurrentTasks = 10;
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly SqlSugarContext _mainContext;
        private const int BatchSize = 5000;
        private const int CommandTimeoutSeconds = 1800;

        /// <summary>
        /// 构造函数
        /// </summary>
        public HBSalesRecordStatisticsService(
            IServiceScopeFactory scopeFactory,
            ILogger<HBSalesRecordStatisticsService> logger,
            ScheduledTaskLogService taskLogService,
            SqlSugarContext mainContext
        )
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _taskLogService = taskLogService;
            _mainContext = mainContext;
        }

        /// <summary>
        /// 并发导入并统计2025年HBSalesRecord销售数据
        /// 使用并行处理提高性能，将日期范围拆分为多个批次并发处理
        /// </summary>
        /// <returns>批量统计更新结果</returns>
        public async Task<BatchStatisticsUpdateResult> ImportAndStatistics2025Concurrent()
        {
            var result = new BatchStatisticsUpdateResult();
            var startDate = new DateTime(2025, 1, 1);
            var endDate = new DateTime(2025, 12, 31);

            var taskLog = await _taskLogService.LogTaskStartAsync(
                "HBSalesRecordImport2025",
                new TaskParameters
                {
                    CustomParameters = new Dictionary<string, object> { ["Year"] = 2025 },
                },
                TaskTrigger.Manual
            );

            _logger.LogInformation(
                "开始导入并统计2025年HBSalesRecord销售数据: {StartDate} 至 {EndDate}, TaskId: {TaskId}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                taskLog.Id
            );

            var dateRanges = SplitDateRangeByDays(startDate, endDate, 15);

            _logger.LogInformation(
                "将 {TotalDays} 天拆分为 {BatchCount} 个批次，每批最多 {DaysPerBatch} 天",
                (int)(endDate - startDate).TotalDays + 1,
                dateRanges.Count,
                10
            );

            var allStatistics = new HBSalesStatisticsResult();
            var failedDates = new List<string>();
            var syncLock = new object();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrentTasks,
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("开始加载供应商名称和映射...");
                var supplierNames = await LoadSupplierNamesToMemoryAsync(_mainContext);
                _logger.LogInformation(
                    "已加载 {LocalCount} 条本地供应商和 {ChinaCount} 条国内供应商名称",
                    supplierNames.LocalSuppliers.Count,
                    supplierNames.ChinaSuppliers.Count
                );

                Dictionary<string, PosmProductSupplierMapping> supplierMapping;
                using (var preloadScope = _scopeFactory.CreateScope())
                {
                    var preloadPosmContext =
                        preloadScope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();
                    supplierMapping = await LoadSupplierMappingToMemoryAsync(preloadPosmContext);
                }
                _logger.LogInformation("已加载 {Count} 条供应商映射到内存", supplierMapping.Count);

                await Parallel.ForEachAsync(
                    dateRanges,
                    parallelOptions,
                    async (dateRange, cancellationToken) =>
                    {
                        try
                        {
                            _logger.LogInformation(
                                "开始处理批次: {StartDate} 至 {EndDate} ({Days} 天)",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd"),
                                dateRange.DayCount
                            );

                            using var scope = _scopeFactory.CreateScope();
                            var hbSalesContext =
                                scope.ServiceProvider.GetRequiredService<HBSalesRecordSqlSugarContext>();
                            var posmContext =
                                scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();

                            var statistics = await ProcessDateRange(
                                dateRange.StartDate,
                                dateRange.EndDate,
                                hbSalesContext,
                                posmContext,
                                supplierMapping,
                                supplierNames
                            );

                            lock (syncLock)
                            {
                                allStatistics.DailyStatistics.AddRange(statistics.DailyStatistics);
                                allStatistics.HourlyStatistics.AddRange(
                                    statistics.HourlyStatistics
                                );
                                allStatistics.StoreStatistics.AddRange(statistics.StoreStatistics);
                                // allStatistics.SupplierStatistics.AddRange(
                                //     statistics.SupplierStatistics
                                // );
                                // allStatistics.StoreSupplierStatistics.AddRange(
                                //     statistics.StoreSupplierStatistics
                                // );
                                allStatistics.AustralianSupplierStoreSalesDetails.AddRange(
                                    statistics.AustralianSupplierStoreSalesDetails
                                );
                                allStatistics.ChinaSupplierStoreSalesDetails.AddRange(
                                    statistics.ChinaSupplierStoreSalesDetails
                                );
                            }

                            _logger.LogInformation(
                                "批次处理完成: {StartDate} 至 {EndDate}, 生成统计记录: 每日{DailyCount}条, 分时{HourlyCount}条, 分店{StoreCount}条, 供应商{SupplierCount}条, 供应商分店{StoreSupplierCount}条, 澳洲供应商{AusCount}条, 中国供应商{ChinaCount}条",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd"),
                                statistics.DailyStatistics.Count,
                                statistics.HourlyStatistics.Count,
                                statistics.StoreStatistics.Count,
                                statistics.SupplierStatistics.Count,
                                statistics.StoreSupplierStatistics.Count,
                                statistics.AustralianSupplierStoreSalesDetails.Count,
                                statistics.ChinaSupplierStoreSalesDetails.Count
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "批次处理失败: {StartDate} 至 {EndDate}",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd")
                            );

                            for (
                                var d = dateRange.StartDate;
                                d <= dateRange.EndDate;
                                d = d.AddDays(1)
                            )
                            {
                                lock (syncLock)
                                {
                                    failedDates.Add(d.ToString("yyyy-MM-dd"));
                                }
                            }
                        }
                    }
                );

                stopwatch.Stop();

                var totalRecords =
                    allStatistics.DailyStatistics.Count
                    + allStatistics.HourlyStatistics.Count
                    + allStatistics.StoreStatistics.Count
                    // + allStatistics.SupplierStatistics.Count
                    // + allStatistics.StoreSupplierStatistics.Count
                    + allStatistics.AustralianSupplierStoreSalesDetails.Count
                    + allStatistics.ChinaSupplierStoreSalesDetails.Count;

                if (totalRecords > 0)
                {
                    _logger.LogInformation(
                        "开始批量写入统计记录到数据库: 每日{DailyCount}条, 分时{HourlyCount}条, 分店{StoreCount}条, 供应商{SupplierCount}条, 供应商分店{StoreSupplierCount}条, 澳洲供应商{AusCount}条, 中国供应商{ChinaCount}条, 共计{Total}条",
                        allStatistics.DailyStatistics.Count,
                        allStatistics.HourlyStatistics.Count,
                        allStatistics.StoreStatistics.Count,
                        allStatistics.SupplierStatistics.Count,
                        allStatistics.StoreSupplierStatistics.Count,
                        allStatistics.AustralianSupplierStoreSalesDetails.Count,
                        allStatistics.ChinaSupplierStoreSalesDetails.Count,
                        totalRecords
                    );

                    using var scope = _scopeFactory.CreateScope();
                    var mainContext = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();

                    await UpdateDailyStatisticsBatch(allStatistics.DailyStatistics, mainContext);
                    await UpdateHourlyStoreStatisticsBatch(
                        allStatistics.HourlyStatistics,
                        mainContext
                    );
                    await UpdateStoreDateStatisticsBatch(
                        allStatistics.StoreStatistics,
                        mainContext
                    );
                    // await UpdateStatisticsBatch(allStatistics.SupplierStatistics, mainContext);
                    // await UpdateStoreSupplierStatisticsBatch(
                    //     allStatistics.StoreSupplierStatistics,
                    //     mainContext
                    // );
                    await UpdateAustralianSupplierStoreSalesDetailBatch(
                        allStatistics.AustralianSupplierStoreSalesDetails,
                        mainContext
                    );
                    await UpdateChinaSupplierStoreSalesDetailBatch(
                        allStatistics.ChinaSupplierStoreSalesDetails,
                        mainContext
                    );
                }

                result.TaskId = taskLog.Id;
                result.TotalDays = (int)(endDate - startDate).TotalDays + 1;
                result.ProcessedDays = result.TotalDays - failedDates.Count;
                result.FailedDates = failedDates;
                result.Success = failedDates.Count == 0;
                result.Message = result.Success
                    ? $"导入并统计2025年数据完成: {result.ProcessedDays}/{result.TotalDays} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}, 平均每天: {stopwatch.Elapsed.TotalMilliseconds / result.ProcessedDays:F2}毫秒"
                    : $"导入并统计2025年数据部分完成: {result.ProcessedDays}/{result.TotalDays} 天, 失败: {failedDates.Count} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}";
                _logger.LogInformation(result.Message);

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(
                        taskLog.Id,
                        $"部分失败: {failedDates.Count} 天"
                    );
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"导入并统计2025年数据失败: 错误: {ex.Message}";
                _logger.LogError(ex, "导入并统计2025年数据失败");

                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 加载供应商映射到内存
        /// </summary>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <returns>产品编号到供应商映射的字典</returns>
        private async Task<
            Dictionary<string, PosmProductSupplierMapping>
        > LoadSupplierMappingToMemoryAsync(POSMSqlSugarContext posmContext)
        {
            try
            {
                _logger.LogInformation("开始从POSM数据库加载供应商映射...");
                // 查询所有未删除的供应商映射记录
                var mappings = await posmContext
                    .Db.Queryable<PosmProductSupplierMapping>()
                    .Where(m => !m.IsDeleted)
                    .ToListAsync();

                var mappingDict = new Dictionary<string, PosmProductSupplierMapping>();
                foreach (var mapping in mappings)
                {
                    // 将映射记录存入字典，Key为产品编号
                    if (!string.IsNullOrEmpty(mapping.ProductCode))
                    {
                        mappingDict[mapping.ProductCode] = mapping;
                    }
                }

                _logger.LogInformation("供应商映射加载完成: {Count} 条记录", mappingDict.Count);

                return mappingDict;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载供应商映射失败");
                return new Dictionary<string, PosmProductSupplierMapping>();
            }
        }

        /// <summary>
        /// 加载供应商名称到内存
        /// </summary>
        /// <param name="mainContext">主数据库上下文</param>
        /// <returns>供应商名称集合</returns>
        private async Task<SupplierNames> LoadSupplierNamesToMemoryAsync(
            SqlSugarContext mainContext
        )
        {
            try
            {
                _logger.LogInformation("开始加载供应商名称...");
                var supplierNames = new SupplierNames();

                // 加载本地供应商信息
                var localSuppliers = await mainContext
                    .Db.Queryable<HBLocalSupplier>()
                    .ToListAsync();

                foreach (var supplier in localSuppliers)
                {
                    if (!string.IsNullOrEmpty(supplier.LocalSupplierCode))
                    {
                        supplierNames.LocalSuppliers[supplier.LocalSupplierCode] = supplier.Name;
                    }
                }

                // 加载国内供应商信息
                var chinaSuppliers = await mainContext.Db.Queryable<ChinaSupplier>().ToListAsync();

                foreach (var supplier in chinaSuppliers)
                {
                    if (!string.IsNullOrEmpty(supplier.SupplierCode))
                    {
                        // 优先使用SupplierName，如果为空则使用SupplierCode
                        supplierNames.ChinaSuppliers[supplier.SupplierCode] =
                            supplier.SupplierName ?? supplier.SupplierCode;
                    }
                }

                _logger.LogInformation(
                    "供应商名称加载完成: 本地供应商 {LocalCount} 条, 国内供应商 {ChinaCount} 条",
                    supplierNames.LocalSuppliers.Count,
                    supplierNames.ChinaSuppliers.Count
                );

                return supplierNames;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载供应商名称失败");
                return new SupplierNames();
            }
        }

        /// <summary>
        /// 处理指定日期范围内的销售数据
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="hbSalesContext">HBSales数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="supplierMapping">供应商映射字典</param>
        /// <param name="supplierNames">供应商名称集合</param>
        /// <returns>该日期范围内的统计结果</returns>
        private async Task<HBSalesStatisticsResult> ProcessDateRange(
            DateTime startDate,
            DateTime endDate,
            HBSalesRecordSqlSugarContext hbSalesContext,
            POSMSqlSugarContext posmContext,
            Dictionary<string, PosmProductSupplierMapping> supplierMapping,
            SupplierNames supplierNames
        )
        {
            var result = new HBSalesStatisticsResult();

            try
            {
                // 设置查询超时时间为30分钟
                hbSalesContext.Db.Ado.CommandTimeOut = CommandTimeoutSeconds;
                posmContext.Db.Ado.CommandTimeOut = CommandTimeoutSeconds;

                // 1. 从 HBSales 数据库查询销售数据（排除单据类型为2的记录）
                var hbSalesDataRaw = await hbSalesContext
                    .Db.Queryable<SalesOrderMain>()
                    .LeftJoin<SalesOrderDetailRecord>((m, d) => m.B销售单号 == d.B销售单号)
                    .Where(
                        (m, d) =>
                            d.B结账日期.HasValue
                            && d.B结账日期.Value >= startDate
                            && d.B结账日期.Value <= endDate
                            && m.B单据类型 != "2"
                    )
                    .Select(
                        (m, d) =>
                            new
                            {
                                Date = d.B结账日期.Value.Date,
                                CheckoutTime = d.B结账时间,
                                BranchCode = d.B分店代码 ?? string.Empty,
                                ProductCode = d.B产品编号,
                                SalesOrderNo = m.B销售单号,
                                DocumentType = m.B单据类型,
                                Amount = d.B合计金额 ?? 0m,
                                Quantity = d.B数量 ?? 0m,
                            }
                    )
                    .ToListAsync();

                _logger.LogInformation(
                    "从 HBSales {StartDate} 至 {EndDate} 读取到 {Count} 条销售详情记录",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd"),
                    hbSalesDataRaw.Count
                );

                // 转换 HBSales 数据格式
                var hbSalesData = hbSalesDataRaw
                    .Select(r => new
                    {
                        r.Date,
                        Hour = r.CheckoutTime.HasValue ? r.CheckoutTime.Value.Hours : 0,
                        r.BranchCode,
                        r.ProductCode,
                        r.SalesOrderNo,
                        r.DocumentType,
                        r.Amount,
                        r.Quantity,
                    })
                    .ToList();

                // 2. 从 POSM 数据库查询销售数据（状态为1或4的订单）
                var posmQuery = posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime.Value.Date >= startDate
                        && o.OrderTime.Value.Date <= endDate
                    );

                var posmData = await posmQuery
                    .Select(
                        (o, d) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                Hour = o.OrderTime.Value.Hour,
                                BranchCode = o.BranchCode,
                                ProductCode = d.ProductCode,
                                TotalAmount = d.ActualAmount,
                                Quantity = d.Quantity ?? 0m,
                                OrderGuid = o.OrderGuid,
                            }
                    )
                    .ToListAsync();

                _logger.LogInformation(
                    "从 POSM {StartDate} 至 {EndDate} 读取到 {Count} 条销售详情记录",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd"),
                    posmData.Count
                );

                // 3. 合并两个来源的数据
                var salesData = new List<SalesDataAggregate>();

                foreach (var hbRecord in hbSalesData)
                {
                    if (!string.IsNullOrEmpty(hbRecord.ProductCode))
                    {
                        decimal totalAmount = hbRecord.Amount;
                        decimal totalQuantity = hbRecord.Quantity;

                        // 处理退货/退款单据（类型3或4），金额和数量取反
                        if (hbRecord.DocumentType == "3" || hbRecord.DocumentType == "4")
                        {
                            totalAmount = -totalAmount;
                            totalQuantity = -totalQuantity;
                        }

                        salesData.Add(
                            new SalesDataAggregate
                            {
                                Date = hbRecord.Date,
                                Hour = hbRecord.Hour,
                                BranchCode = hbRecord.BranchCode,
                                ProductCode = hbRecord.ProductCode,
                                TotalAmount = totalAmount,
                                TotalQuantity = totalQuantity,
                                OrderNo = hbRecord.SalesOrderNo ?? string.Empty,
                                CustomerCount = 1,
                            }
                        );
                    }
                }

                foreach (var posmRecord in posmData)
                {
                    salesData.Add(
                        new SalesDataAggregate
                        {
                            Date = posmRecord.Date,
                            Hour = posmRecord.Hour,
                            BranchCode = posmRecord.BranchCode ?? string.Empty,
                            ProductCode = posmRecord.ProductCode ?? string.Empty,
                            TotalAmount = posmRecord.TotalAmount ?? 0m,
                            TotalQuantity = posmRecord.Quantity,
                            OrderNo = posmRecord.OrderGuid?.ToString() ?? string.Empty,
                            CustomerCount = 1,
                        }
                    );
                }

                _logger.LogInformation("合并后共 {Count} 条销售记录", salesData.Count);

                _logger.LogInformation(
                    "从 {StartDate} 至 {EndDate} 读取到 {Count} 条销售详情记录",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd"),
                    salesData.Count
                );

                // 4. 过滤无效数据并加载相关分店信息
                var validSalesData = salesData
                    .Where(d => !string.IsNullOrEmpty(d.ProductCode))
                    .ToList();

                var allBranchCodes = validSalesData.Select(d => d.BranchCode).Distinct().ToList();
                var stores = await _mainContext
                    .Db.Queryable<Store>()
                    .Where(s => allBranchCodes.Contains(s.StoreCode))
                    .ToListAsync();
                var storeDict = stores.ToDictionary(s => s.StoreCode, s => s);

                _logger.LogInformation("加载了 {StoreCount} 个分店信息", storeDict.Count);

                // 5. 生成各维度统计报表
                result.DailyStatistics = await ProcessDailyStatistics(
                    validSalesData,
                    startDate,
                    endDate
                );
                result.HourlyStatistics = await ProcessHourlyStoreStatistics(
                    validSalesData,
                    storeDict,
                    startDate,
                    endDate
                );
                result.StoreStatistics = await ProcessStoreDateStatistics(
                    validSalesData,
                    storeDict,
                    startDate,
                    endDate
                );
                // 暂时注释掉供应商相关统计
                // result.SupplierStatistics = await ProcessSupplierDateStatistics(
                //     validSalesData,
                //     supplierMapping,
                //     supplierNames,
                //     startDate,
                //     endDate
                // );
                // result.StoreSupplierStatistics = await ProcessStoreSupplierStatistics(
                //     validSalesData,
                //     supplierMapping,
                //     supplierNames,
                //     startDate,
                //     endDate
                // );
                result.AustralianSupplierStoreSalesDetails =
                    ProcessAustralianSupplierStoreStatistics(
                        validSalesData,
                        supplierMapping,
                        supplierNames,
                        startDate,
                        endDate
                    );
                result.ChinaSupplierStoreSalesDetails = ProcessChinaSupplierStoreStatistics(
                    validSalesData,
                    supplierMapping,
                    supplierNames,
                    startDate,
                    endDate
                );

                var totalRecords =
                    result.DailyStatistics.Count
                    + result.HourlyStatistics.Count
                    + result.StoreStatistics.Count
                    + result.SupplierStatistics.Count
                    + result.StoreSupplierStatistics.Count
                    + result.AustralianSupplierStoreSalesDetails.Count
                    + result.ChinaSupplierStoreSalesDetails.Count;

                _logger.LogInformation(
                    "日期范围 {StartDate} 至 {EndDate} 生成统计记录: 每日{DailyCount}条, 分时{HourlyCount}条, 分店{StoreCount}条, 供应商{SupplierCount}条, 供应商分店{StoreSupplierCount}条, 澳洲供应商{AusCount}条, 中国供应商{ChinaCount}条, 共计{Total}条",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd"),
                    result.DailyStatistics.Count,
                    result.HourlyStatistics.Count,
                    result.StoreStatistics.Count,
                    result.SupplierStatistics.Count,
                    result.StoreSupplierStatistics.Count,
                    result.AustralianSupplierStoreSalesDetails.Count,
                    result.ChinaSupplierStoreSalesDetails.Count,
                    totalRecords
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "处理日期范围失败: {StartDate} 至 {EndDate}",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd")
                );
                throw;
            }

            return result;
        }

        /// <summary>
        /// 生成每日销售统计
        /// </summary>
        /// <param name="salesData">销售数据聚合列表</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>每日销售统计列表</returns>
        private async Task<List<DailySalesStatistic>> ProcessDailyStatistics(
            List<SalesDataAggregate> salesData,
            DateTime startDate,
            DateTime endDate
        )
        {
            var statisticsList = new List<DailySalesStatistic>();

            // 按日期分组统计
            var groupedData = salesData
                .GroupBy(d => d.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalAmount = g.Sum(d => d.TotalAmount),
                    TotalQuantity = g.Sum(d => d.TotalQuantity),
                    OrderCount = g.Select(d => d.OrderNo).Distinct().Count(),
                    CustomerCount = g.Sum(d => d.CustomerCount),
                })
                .ToList();

            foreach (var stat in groupedData)
            {
                var averageOrderValue =
                    stat.OrderCount > 0 ? stat.TotalAmount / stat.OrderCount : 0m;

                statisticsList.Add(
                    new DailySalesStatistic
                    {
                        Date = stat.Date,
                        TotalAmount = stat.TotalAmount,
                        TotalQuantity = (int)stat.TotalQuantity,
                        OrderCount = stat.OrderCount,
                        SkuCount = salesData.Count(d => d.Date == stat.Date),
                        CustomerCount = stat.CustomerCount,
                        AverageOrderValue = averageOrderValue,
                        UpdateTime = DateTime.Now,
                    }
                );
            }

            return statisticsList;
        }

        /// <summary>
        /// 生成分时分店销售统计
        /// </summary>
        /// <param name="salesData">销售数据聚合列表</param>
        /// <param name="storeDict">分店字典</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>分时销售统计列表</returns>
        private async Task<List<HourlySalesStatistic>> ProcessHourlyStoreStatistics(
            List<SalesDataAggregate> salesData,
            Dictionary<string, Store> storeDict,
            DateTime startDate,
            DateTime endDate
        )
        {
            var statisticsList = new List<HourlySalesStatistic>();

            // 按日期、小时、分店分组统计
            var groupedData = salesData
                .GroupBy(d => new
                {
                    d.Date,
                    d.Hour,
                    d.BranchCode,
                })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Hour,
                    g.Key.BranchCode,
                    TotalAmount = g.Sum(d => d.TotalAmount),
                    TotalQuantity = g.Sum(d => d.TotalQuantity),
                    OrderCount = g.Select(d => d.OrderNo).Distinct().Count(),
                    CustomerCount = g.Select(d => d.OrderNo).Distinct().Count(),
                })
                .ToList();

            foreach (var stat in groupedData)
            {
                var averageOrderValue =
                    stat.OrderCount > 0 ? stat.TotalAmount / stat.OrderCount : 0m;

                var store = storeDict.GetValueOrDefault(stat.BranchCode);

                statisticsList.Add(
                    new HourlySalesStatistic
                    {
                        Date = stat.Date,
                        Hour = stat.Hour,
                        BranchCode = stat.BranchCode,
                        BranchName = store?.StoreName ?? stat.BranchCode ?? string.Empty,
                        TotalAmount = stat.TotalAmount,
                        TotalQuantity = (int)stat.TotalQuantity,
                        OrderCount = stat.OrderCount,
                        CustomerCount = stat.CustomerCount,
                        AverageOrderValue = averageOrderValue,
                        UpdateTime = DateTime.Now,
                    }
                );
            }

            return statisticsList;
        }

        /// <summary>
        /// 生成分店每日销售统计
        /// </summary>
        /// <param name="salesData">销售数据聚合列表</param>
        /// <param name="storeDict">分店字典</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>分店销售统计列表</returns>
        private async Task<List<StoreSalesStatistic>> ProcessStoreDateStatistics(
            List<SalesDataAggregate> salesData,
            Dictionary<string, Store> storeDict,
            DateTime startDate,
            DateTime endDate
        )
        {
            var statisticsList = new List<StoreSalesStatistic>();

            // 按日期、分店分组统计
            var groupedData = salesData
                .GroupBy(d => new { d.Date, d.BranchCode })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.BranchCode,
                    TotalAmount = g.Sum(d => d.TotalAmount),
                    TotalQuantity = g.Sum(d => d.TotalQuantity),
                    OrderCount = g.Select(d => d.OrderNo).Distinct().Count(),
                    CustomerCount = g.Select(d => d.OrderNo).Distinct().Count(),
                })
                .ToList();

            foreach (var stat in groupedData)
            {
                var averageOrderValue =
                    stat.OrderCount > 0 ? stat.TotalAmount / stat.OrderCount : 0m;

                var store = storeDict.GetValueOrDefault(stat.BranchCode);

                statisticsList.Add(
                    new StoreSalesStatistic
                    {
                        Date = stat.Date,
                        BranchCode = stat.BranchCode,
                        BranchName = store?.StoreName ?? stat.BranchCode ?? string.Empty,
                        TotalAmount = stat.TotalAmount,
                        TotalQuantity = (int)stat.TotalQuantity,
                        OrderCount = stat.OrderCount,
                        CustomerCount = stat.CustomerCount,
                        AverageOrderValue = averageOrderValue,
                        UpdateTime = DateTime.Now,
                    }
                );
            }

            return statisticsList;
        }

        /// <summary>
        /// 生成供应商每日销售统计
        /// </summary>
        /// <param name="salesData">销售数据聚合列表</param>
        /// <param name="supplierMapping">供应商映射字典</param>
        /// <param name="supplierNames">供应商名称集合</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>供应商销售统计列表</returns>
        private async Task<List<SupplierSalesStatistic>> ProcessSupplierDateStatistics(
            List<SalesDataAggregate> salesData,
            Dictionary<string, PosmProductSupplierMapping> supplierMapping,
            SupplierNames supplierNames,
            DateTime startDate,
            DateTime endDate
        )
        {
            var statisticsList = new List<SupplierSalesStatistic>();

            // 按日期、供应商分组统计
            var groupedData = salesData
                .GroupBy(d =>
                {
                    var productCode = d.ProductCode!;
                    PosmProductSupplierMapping? mapping = null;
                    if (supplierMapping.TryGetValue(productCode, out mapping))
                    {
                        var supplierCode = mapping.LocalSupplierCode;
                        // 特殊处理：如果本地供应商代码为200且有国内供应商代码，则使用国内供应商代码
                        if (
                            !string.IsNullOrEmpty(mapping.ChinaSupplierCode)
                            && mapping.LocalSupplierCode == "200"
                        )
                        {
                            supplierCode = mapping.ChinaSupplierCode;
                        }

                        return new { Date = d.Date, SupplierCode = supplierCode ?? string.Empty };
                    }

                    return new { Date = d.Date, SupplierCode = string.Empty };
                })
                .Where(g => !string.IsNullOrEmpty(g.Key.SupplierCode))
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.SupplierCode,
                    TotalAmount = g.Sum(d => d.TotalAmount),
                    TotalQuantity = g.Sum(d => d.TotalQuantity),
                    StoreCount = g.Select(d => d.BranchCode).Distinct().Count(),
                    OrderCount = g.Select(d => d.OrderNo).Distinct().Count(),
                })
                .ToList();

            foreach (var stat in groupedData)
            {
                var isDomestic = IsDomesticSupplier(stat.SupplierCode, supplierMapping);
                var supplierName = GetSupplierName(stat.SupplierCode, isDomestic, supplierNames);

                statisticsList.Add(
                    new SupplierSalesStatistic
                    {
                        Date = stat.Date,
                        SupplierCode = stat.SupplierCode,
                        SupplierName = supplierName,
                        IsDomestic = isDomestic,
                        TotalAmount = stat.TotalAmount,
                        TotalQuantity = (int)stat.TotalQuantity,
                        StoreCount = stat.StoreCount,
                        OrderCount = stat.OrderCount,
                        UpdateTime = DateTime.Now,
                    }
                );
            }

            return statisticsList;
        }

        /// <summary>
        /// 生成分店供应商销售统计
        /// </summary>
        /// <param name="salesData">销售数据聚合列表</param>
        /// <param name="supplierMapping">供应商映射字典</param>
        /// <param name="supplierNames">供应商名称集合</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>分店供应商销售统计列表</returns>
        private async Task<List<StoreSupplierSalesDetail>> ProcessStoreSupplierStatistics(
            List<SalesDataAggregate> salesData,
            Dictionary<string, PosmProductSupplierMapping> supplierMapping,
            SupplierNames supplierNames,
            DateTime startDate,
            DateTime endDate
        )
        {
            var statisticsList = new List<StoreSupplierSalesDetail>();

            // 按日期、分店、供应商分组统计
            var groupedData = salesData
                .GroupBy(d =>
                {
                    var productCode = d.ProductCode!;
                    PosmProductSupplierMapping? mapping = null;
                    if (supplierMapping.TryGetValue(productCode, out mapping))
                    {
                        var supplierCode = mapping.LocalSupplierCode;
                        // 特殊处理：如果本地供应商代码为200且有国内供应商代码，则使用国内供应商代码
                        if (
                            !string.IsNullOrEmpty(mapping.ChinaSupplierCode)
                            && mapping.LocalSupplierCode == "200"
                        )
                        {
                            supplierCode = mapping.ChinaSupplierCode;
                        }

                        return new
                        {
                            Date = d.Date,
                            BranchCode = d.BranchCode ?? string.Empty,
                            SupplierCode = supplierCode ?? string.Empty,
                        };
                    }

                    return new
                    {
                        Date = d.Date,
                        BranchCode = d.BranchCode ?? string.Empty,
                        SupplierCode = string.Empty,
                    };
                })
                .Where(g => !string.IsNullOrEmpty(g.Key.SupplierCode))
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.BranchCode,
                    g.Key.SupplierCode,
                    TotalAmount = g.Sum(d => d.TotalAmount),
                    TotalQuantity = g.Sum(d => d.TotalQuantity),
                    OrderCount = g.Select(d => d.OrderNo).Distinct().Count(),
                })
                .ToList();

            foreach (var stat in groupedData)
            {
                var isDomestic = IsDomesticSupplier(stat.SupplierCode, supplierMapping);
                var supplierName = GetSupplierName(stat.SupplierCode, isDomestic, supplierNames);

                statisticsList.Add(
                    new StoreSupplierSalesDetail
                    {
                        Date = stat.Date,
                        BranchCode = stat.BranchCode,
                        SupplierCode = stat.SupplierCode,
                        SupplierName = supplierName,
                        IsDomestic = isDomestic,
                        TotalAmount = stat.TotalAmount,
                        TotalQuantity = (int)stat.TotalQuantity,
                        OrderCount = stat.OrderCount,
                        UpdateTime = DateTime.Now,
                    }
                );
            }

            return statisticsList;
        }

        /// <summary>
        /// 判断是否为国内供应商
        /// </summary>
        /// <param name="supplierCode">供应商代码</param>
        /// <param name="supplierMapping">供应商映射字典</param>
        /// <returns>如果是国内供应商返回true，否则返回false</returns>
        private bool IsDomesticSupplier(
            string supplierCode,
            Dictionary<string, PosmProductSupplierMapping> supplierMapping
        )
        {
            if (string.IsNullOrEmpty(supplierCode))
            {
                return false;
            }

            return supplierMapping.Values.Any(m =>
                m.ChinaSupplierCode == supplierCode || m.LocalSupplierCode == "200"
            );
        }

        /// <summary>
        /// 获取供应商名称
        /// </summary>
        /// <param name="supplierCode">供应商代码</param>
        /// <param name="isDomestic">是否为国内供应商</param>
        /// <param name="supplierNames">供应商名称集合</param>
        /// <returns>供应商名称</returns>
        private string GetSupplierName(
            string supplierCode,
            bool isDomestic,
            SupplierNames supplierNames
        )
        {
            if (string.IsNullOrEmpty(supplierCode))
            {
                return string.Empty;
            }

            if (isDomestic)
            {
                return supplierNames.ChinaSuppliers.TryGetValue(supplierCode, out var name)
                    ? name
                    : supplierCode;
            }

            return supplierNames.LocalSuppliers.TryGetValue(supplierCode, out var localName)
                ? localName
                : supplierCode;
        }

        /// <summary>
        /// 生成澳洲供应商门店销售统计
        /// </summary>
        /// <param name="salesData">销售数据聚合列表</param>
        /// <param name="supplierMapping">供应商映射字典</param>
        /// <param name="supplierNames">供应商名称集合</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>澳洲供应商门店销售统计列表</returns>
        private List<AustralianSupplierStoreSalesDetail> ProcessAustralianSupplierStoreStatistics(
            List<SalesDataAggregate> salesData,
            Dictionary<string, PosmProductSupplierMapping> supplierMapping,
            SupplierNames supplierNames,
            DateTime startDate,
            DateTime endDate
        )
        {
            var statisticsList = new List<AustralianSupplierStoreSalesDetail>();

            // 按日期、分店、供应商分组统计（仅限澳洲本地供应商）
            var groupedData = salesData
                .GroupBy(d =>
                {
                    var productCode = d.ProductCode!;
                    PosmProductSupplierMapping? mapping = null;
                    if (supplierMapping.TryGetValue(productCode, out mapping))
                    {
                        return new
                        {
                            Date = d.Date,
                            BranchCode = d.BranchCode ?? string.Empty,
                            SupplierCode = mapping.LocalSupplierCode ?? string.Empty,
                        };
                    }

                    return new
                    {
                        Date = d.Date,
                        BranchCode = d.BranchCode ?? string.Empty,
                        SupplierCode = string.Empty,
                    };
                })
                .Where(g => !string.IsNullOrEmpty(g.Key.SupplierCode))
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.BranchCode,
                    g.Key.SupplierCode,
                    TotalAmount = g.Sum(d => d.TotalAmount),
                    TotalQuantity = g.Sum(d => d.TotalQuantity),
                    OrderCount = g.Select(d => d.OrderNo).Distinct().Count(),
                })
                .ToList();

            foreach (var stat in groupedData)
            {
                var supplierName = supplierNames.LocalSuppliers.TryGetValue(
                    stat.SupplierCode,
                    out var name
                )
                    ? name
                    : stat.SupplierCode;

                statisticsList.Add(
                    new AustralianSupplierStoreSalesDetail
                    {
                        Date = stat.Date,
                        BranchCode = stat.BranchCode,
                        SupplierCode = stat.SupplierCode,
                        SupplierName = supplierName,
                        TotalAmount = stat.TotalAmount,
                        TotalQuantity = (int)stat.TotalQuantity,
                        OrderCount = stat.OrderCount,
                        UpdateTime = DateTime.Now,
                    }
                );
            }

            return statisticsList;
        }

        /// <summary>
        /// 生成中国供应商门店销售统计
        /// </summary>
        /// <param name="salesData">销售数据聚合列表</param>
        /// <param name="supplierMapping">供应商映射字典</param>
        /// <param name="supplierNames">供应商名称集合</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>中国供应商门店销售统计列表</returns>
        private List<ChinaSupplierStoreSalesDetail> ProcessChinaSupplierStoreStatistics(
            List<SalesDataAggregate> salesData,
            Dictionary<string, PosmProductSupplierMapping> supplierMapping,
            SupplierNames supplierNames,
            DateTime startDate,
            DateTime endDate
        )
        {
            var statisticsList = new List<ChinaSupplierStoreSalesDetail>();

            // 按日期、分店、供应商分组统计（仅限中国供应商）
            var groupedData = salesData
                .GroupBy(d =>
                {
                    var productCode = d.ProductCode!;
                    PosmProductSupplierMapping? mapping = null;
                    if (
                        supplierMapping.TryGetValue(productCode, out mapping)
                        && mapping != null
                        && mapping.LocalSupplierCode == "200"
                    )
                    {
                        return new
                        {
                            Date = d.Date,
                            BranchCode = d.BranchCode ?? string.Empty,
                            SupplierCode = mapping.ChinaSupplierCode ?? string.Empty,
                        };
                    }

                    return null;
                })
                .Where(g => g != null)
                .Where(g => g!.Key != null && !string.IsNullOrEmpty(g.Key.SupplierCode))
                .Select(g => new
                {
                    g!.Key.Date,
                    g.Key.BranchCode,
                    g.Key.SupplierCode,
                    TotalAmount = g.Sum(d => d.TotalAmount),
                    TotalQuantity = g.Sum(d => d.TotalQuantity),
                    OrderCount = g.Select(d => d.OrderNo).Distinct().Count(),
                })
                .ToList();

            foreach (var stat in groupedData)
            {
                var supplierName = supplierNames.ChinaSuppliers.TryGetValue(
                    stat.SupplierCode,
                    out var name
                )
                    ? name
                    : stat.SupplierCode;

                statisticsList.Add(
                    new ChinaSupplierStoreSalesDetail
                    {
                        Date = stat.Date,
                        BranchCode = stat.BranchCode,
                        SupplierCode = stat.SupplierCode,
                        SupplierName = supplierName,
                        TotalAmount = stat.TotalAmount,
                        TotalQuantity = (int)stat.TotalQuantity,
                        OrderCount = stat.OrderCount,
                        UpdateTime = DateTime.Now,
                    }
                );
            }

            return statisticsList;
        }

        /// <summary>
        /// 批量更新供应商统计数据
        /// </summary>
        /// <param name="statistics">供应商统计数据列表</param>
        /// <param name="mainContext">数据库上下文</param>
        private async Task UpdateStatisticsBatch(
            List<SupplierSalesStatistic> statistics,
            SqlSugarContext mainContext
        )
        {
            try
            {
                var allSupplierCodes = statistics.Select(s => s.SupplierCode).Distinct().ToList();
                var allDates = statistics.Select(s => s.Date).Distinct().ToList();

                // 查询已存在的记录
                var existingRecords = await mainContext
                    .Db.Queryable<SupplierSalesStatistic>()
                    .Where(s =>
                        allDates.Contains(s.Date) && allSupplierCodes.Contains(s.SupplierCode)
                    )
                    .ToListAsync();

                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.SupplierCode}",
                    s => s
                );

                var toInsert = new List<SupplierSalesStatistic>();
                var toUpdate = new List<SupplierSalesStatistic>();

                foreach (var stat in statistics)
                {
                    var key = $"{stat.Date}_{stat.SupplierCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 累加更新
                        existing.TotalAmount += stat.TotalAmount;
                        existing.TotalQuantity += stat.TotalQuantity;
                        existing.StoreCount = Math.Max(existing.StoreCount, stat.StoreCount);
                        existing.OrderCount = stat.OrderCount;
                        existing.UpdateTime = DateTime.Now;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新增
                        toInsert.Add(stat);
                    }
                }

                if (toInsert.Any())
                {
                    await InsertInBatches(toInsert, mainContext);
                    _logger.LogInformation("批量插入 {Count} 条供应商统计记录", toInsert.Count);
                }

                if (toUpdate.Any())
                {
                    await UpdateInBatches(toUpdate, mainContext);
                    _logger.LogInformation(
                        "批量更新 {Count} 条供应商统计记录（累加）",
                        toUpdate.Count
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新供应商统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 批量更新每日销售统计数据
        /// </summary>
        /// <param name="statistics">每日销售统计列表</param>
        /// <param name="mainContext">数据库上下文</param>
        private async Task UpdateDailyStatisticsBatch(
            List<DailySalesStatistic> statistics,
            SqlSugarContext mainContext
        )
        {
            try
            {
                var allDates = statistics.Select(s => s.Date).Distinct().ToList();

                // 查询已存在的记录
                var existingRecords = await mainContext
                    .Db.Queryable<DailySalesStatistic>()
                    .Where(s => allDates.Contains(s.Date))
                    .ToListAsync();

                var existingDict = existingRecords.ToDictionary(
                    s => s.Date.ToString("yyyy-MM-dd"),
                    s => s
                );

                var toInsert = new List<DailySalesStatistic>();
                var toUpdate = new List<DailySalesStatistic>();

                foreach (var stat in statistics)
                {
                    var key = stat.Date.ToString("yyyy-MM-dd");

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 覆盖更新
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.OrderCount = stat.OrderCount;
                        existing.SkuCount = stat.SkuCount;
                        existing.CustomerCount = stat.CustomerCount;
                        existing.AverageOrderValue = stat.AverageOrderValue;
                        existing.UpdateTime = DateTime.Now;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新增
                        toInsert.Add(stat);
                    }
                }

                if (toInsert.Any())
                {
                    await InsertInBatches(toInsert, mainContext);
                    _logger.LogInformation("批量插入 {Count} 条每日统计记录", toInsert.Count);
                }

                if (toUpdate.Any())
                {
                    await UpdateInBatches(toUpdate, mainContext);
                    _logger.LogInformation("批量更新 {Count} 条每日统计记录", toUpdate.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新每日统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 批量更新分时销售统计数据
        /// </summary>
        /// <param name="statistics">分时销售统计列表</param>
        /// <param name="mainContext">数据库上下文</param>
        private async Task UpdateHourlyStoreStatisticsBatch(
            List<HourlySalesStatistic> statistics,
            SqlSugarContext mainContext
        )
        {
            try
            {
                var allDates = statistics.Select(s => s.Date).Distinct().ToList();
                var allHours = statistics.Select(s => s.Hour).Distinct().ToList();
                var allBranchCodes = statistics.Select(s => s.BranchCode).Distinct().ToList();

                // 查询已存在的记录
                var existingRecords = await mainContext
                    .Db.Queryable<HourlySalesStatistic>()
                    .Where(s =>
                        allDates.Contains(s.Date)
                        && allHours.Contains(s.Hour)
                        && allBranchCodes.Contains(s.BranchCode)
                    )
                    .ToListAsync();

                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.Hour}_{s.BranchCode}",
                    s => s
                );

                var toInsert = new List<HourlySalesStatistic>();
                var toUpdate = new List<HourlySalesStatistic>();

                foreach (var stat in statistics)
                {
                    var key = $"{stat.Date}_{stat.Hour}_{stat.BranchCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 覆盖更新
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.CustomerCount = stat.CustomerCount;
                        existing.AverageOrderValue = stat.AverageOrderValue;
                        existing.UpdateTime = DateTime.Now;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新增
                        toInsert.Add(stat);
                    }
                }

                if (toInsert.Any())
                {
                    await InsertInBatches(toInsert, mainContext);
                    _logger.LogInformation("批量插入 {Count} 条分时统计记录", toInsert.Count);
                }

                if (toUpdate.Any())
                {
                    await UpdateInBatches(toUpdate, mainContext);
                    _logger.LogInformation("批量更新 {Count} 条分时统计记录", toUpdate.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新分时统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 批量更新分店销售统计数据
        /// </summary>
        /// <param name="statistics">分店销售统计列表</param>
        /// <param name="mainContext">数据库上下文</param>
        private async Task UpdateStoreDateStatisticsBatch(
            List<StoreSalesStatistic> statistics,
            SqlSugarContext mainContext
        )
        {
            try
            {
                var allDates = statistics.Select(s => s.Date).Distinct().ToList();
                var allBranchCodes = statistics.Select(s => s.BranchCode).Distinct().ToList();

                // 查询已存在的记录
                var existingRecords = await mainContext
                    .Db.Queryable<StoreSalesStatistic>()
                    .Where(s => allDates.Contains(s.Date) && allBranchCodes.Contains(s.BranchCode))
                    .ToListAsync();

                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.BranchCode}",
                    s => s
                );

                var toInsert = new List<StoreSalesStatistic>();
                var toUpdate = new List<StoreSalesStatistic>();

                foreach (var stat in statistics)
                {
                    var key = $"{stat.Date}_{stat.BranchCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 覆盖更新
                        existing.BranchName = stat.BranchName;
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.OrderCount = stat.OrderCount;
                        existing.CustomerCount = stat.CustomerCount;
                        existing.AverageOrderValue = stat.AverageOrderValue;
                        existing.UpdateTime = DateTime.Now;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新增
                        toInsert.Add(stat);
                    }
                }

                if (toInsert.Any())
                {
                    await InsertInBatches(toInsert, mainContext);
                    _logger.LogInformation("批量插入 {Count} 条分店统计记录", toInsert.Count);
                }

                if (toUpdate.Any())
                {
                    await UpdateInBatches(toUpdate, mainContext);
                    _logger.LogInformation("批量更新 {Count} 条分店统计记录", toUpdate.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新分店统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 批量更新分店供应商销售统计数据
        /// </summary>
        /// <param name="statistics">分店供应商销售统计列表</param>
        /// <param name="mainContext">数据库上下文</param>
        private async Task UpdateStoreSupplierStatisticsBatch(
            List<StoreSupplierSalesDetail> statistics,
            SqlSugarContext mainContext
        )
        {
            try
            {
                var allDates = statistics.Select(s => s.Date).Distinct().ToList();
                var allBranchCodes = statistics.Select(s => s.BranchCode).Distinct().ToList();
                var allSupplierCodes = statistics.Select(s => s.SupplierCode).Distinct().ToList();

                // 查询已存在的记录
                var existingRecords = await mainContext
                    .Db.Queryable<StoreSupplierSalesDetail>()
                    .Where(s =>
                        allDates.Contains(s.Date)
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

                foreach (var stat in statistics)
                {
                    var key = $"{stat.Date}_{stat.BranchCode}_{stat.SupplierCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        // 覆盖更新
                        existing.SupplierName = stat.SupplierName;
                        existing.IsDomestic = stat.IsDomestic;
                        existing.TotalAmount = stat.TotalAmount;
                        existing.TotalQuantity = stat.TotalQuantity;
                        existing.OrderCount = stat.OrderCount;
                        existing.UpdateTime = DateTime.Now;
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        // 新增
                        toInsert.Add(stat);
                    }
                }

                if (toInsert.Any())
                {
                    await InsertInBatches(toInsert, mainContext);
                    _logger.LogInformation("批量插入 {Count} 条供应商分店统计记录", toInsert.Count);
                }

                if (toUpdate.Any())
                {
                    await UpdateInBatches(toUpdate, mainContext);
                    _logger.LogInformation("批量更新 {Count} 条供应商分店统计记录", toUpdate.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新供应商分店统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 批量更新澳洲供应商门店销售统计详情
        /// 采用先删除后插入的策略
        /// </summary>
        /// <param name="statistics">澳洲供应商门店销售统计列表</param>
        /// <param name="mainContext">数据库上下文</param>
        private async Task UpdateAustralianSupplierStoreSalesDetailBatch(
            List<AustralianSupplierStoreSalesDetail> statistics,
            SqlSugarContext mainContext
        )
        {
            try
            {
                var allDates = statistics.Select(s => s.Date).Distinct().ToList();

                await mainContext.Db.Ado.BeginTranAsync();
                try
                {
                    // 删除旧数据
                    var deletedCount = await mainContext
                        .Db.Deleteable<AustralianSupplierStoreSalesDetail>()
                        .Where(s => allDates.Contains(s.Date))
                        .ExecuteCommandAsync();
                    _logger.LogInformation("删除 {Count} 条澳洲供应商门店统计旧记录", deletedCount);

                    if (statistics.Any())
                    {
                        // 插入新数据
                        await InsertInBatches(statistics, mainContext);
                        _logger.LogInformation(
                            "批量插入 {Count} 条澳洲供应商门店统计记录",
                            statistics.Count
                        );
                    }

                    await mainContext.Db.Ado.CommitTranAsync();
                }
                catch
                {
                    await mainContext.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新澳洲供应商门店统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 批量更新中国供应商门店销售统计详情
        /// 采用先删除后插入的策略
        /// </summary>
        /// <param name="statistics">中国供应商门店销售统计列表</param>
        /// <param name="mainContext">数据库上下文</param>
        private async Task UpdateChinaSupplierStoreSalesDetailBatch(
            List<ChinaSupplierStoreSalesDetail> statistics,
            SqlSugarContext mainContext
        )
        {
            try
            {
                var allDates = statistics.Select(s => s.Date).Distinct().ToList();

                await mainContext.Db.Ado.BeginTranAsync();
                try
                {
                    // 删除旧数据
                    var deletedCount = await mainContext
                        .Db.Deleteable<ChinaSupplierStoreSalesDetail>()
                        .Where(s => allDates.Contains(s.Date))
                        .ExecuteCommandAsync();
                    _logger.LogInformation("删除 {Count} 条中国供应商门店统计旧记录", deletedCount);

                    if (statistics.Any())
                    {
                        // 插入新数据
                        await InsertInBatches(statistics, mainContext);
                        _logger.LogInformation(
                            "批量插入 {Count} 条中国供应商门店统计记录",
                            statistics.Count
                        );
                    }

                    await mainContext.Db.Ado.CommitTranAsync();
                }
                catch
                {
                    await mainContext.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新中国供应商门店统计数据失败");
                throw;
            }
        }

        /// <summary>
        /// 批量插入数据
        /// 使用 Fastest.BulkCopy 提高插入性能
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="entities">实体列表</param>
        /// <param name="context">数据库上下文</param>
        /// <param name="batchSize">批次大小</param>
        /// <param name="commandTimeoutSeconds">命令超时时间</param>
        private async Task InsertInBatches<TEntity>(
            List<TEntity> entities,
            SqlSugarContext context,
            int batchSize = BatchSize,
            int commandTimeoutSeconds = CommandTimeoutSeconds
        )
            where TEntity : class, new()
        {
            if (entities == null || entities.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "开始批量插入 {TotalCount} 条记录，使用 Fastest.BulkCopy",
                entities.Count
            );

            var originalTimeout = context.Db.Ado.CommandTimeOut;
            context.Db.Ado.CommandTimeOut = commandTimeoutSeconds;

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 使用 BulkCopy 进行高速批量插入
                context.Db.Fastest<TEntity>().BulkCopy(entities);

                stopwatch.Stop();

                _logger.LogInformation(
                    "批量插入完成，共 {Count} 条记录，耗时 {Elapsed}",
                    entities.Count,
                    stopwatch.Elapsed
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量插入失败");
                throw;
            }
            finally
            {
                context.Db.Ado.CommandTimeOut = originalTimeout;
            }
        }

        /// <summary>
        /// 批量更新数据
        /// 使用 Fastest.BulkUpdate 提高更新性能
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="entities">实体列表</param>
        /// <param name="context">数据库上下文</param>
        /// <param name="batchSize">批次大小</param>
        /// <param name="commandTimeoutSeconds">命令超时时间</param>
        private async Task UpdateInBatches<TEntity>(
            List<TEntity> entities,
            SqlSugarContext context,
            int batchSize = BatchSize,
            int commandTimeoutSeconds = CommandTimeoutSeconds
        )
            where TEntity : class, new()
        {
            if (entities == null || entities.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "开始批量更新 {TotalCount} 条记录，使用 Fastest.BulkUpdate",
                entities.Count
            );

            var originalTimeout = context.Db.Ado.CommandTimeOut;
            context.Db.Ado.CommandTimeOut = commandTimeoutSeconds;

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 使用 BulkUpdate 进行高速批量更新
                context.Db.Fastest<TEntity>().BulkUpdate(entities);

                stopwatch.Stop();

                _logger.LogInformation(
                    "批量更新完成，共 {Count} 条记录，耗时 {Elapsed}",
                    entities.Count,
                    stopwatch.Elapsed
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新失败");
                throw;
            }
            finally
            {
                context.Db.Ado.CommandTimeOut = originalTimeout;
            }
        }

        /// <summary>
        /// 将日期范围按天数拆分
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="daysPerBatch">每批天数</param>
        /// <returns>日期范围列表</returns>
        private List<DateRange> SplitDateRangeByDays(
            DateTime startDate,
            DateTime endDate,
            int daysPerBatch
        )
        {
            var ranges = new List<DateRange>();
            var currentStart = startDate;

            while (currentStart <= endDate)
            {
                var currentEnd = currentStart.AddDays(daysPerBatch - 1);
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
        /// 日期范围实体
        /// </summary>
        public class DateRange
        {
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public int DayCount => (int)(EndDate - StartDate).TotalDays + 1;
        }
    }
}
