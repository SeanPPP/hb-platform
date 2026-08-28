using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.HBweb;
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
        /// 因已有运行租约而跳过的日期列表
        /// </summary>
        public List<string> SkippedDates { get; set; } = new();

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

    internal sealed class FullRefreshRangeExecutionResult
    {
        public int ProcessedDays { get; set; }
        public List<string> SkippedDates { get; set; } = new();
        public List<string> FailedDates { get; set; } = new();
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
        /// 2025 HBSales 批量快照中单日的不可变来源签名。
        /// </summary>
        public sealed record HBSales2025DailySnapshotSignature(
            DateTime Date,
            int RowCount,
            DateTime? MainLastModifiedAt,
            DateTime? MainCreatedAt,
            DateTime? DetailLastModifiedAt,
            DateTime? DetailCreatedAt,
            string Checksum
        );

        /// <summary>
        /// POSM 单表来源签名。四张表必须分别保留自己的行数、时间和校验值，不能压缩为跨表 MAX。
        /// </summary>
        public sealed record Posm2025DailyTableSignature(
            int RowCount,
            DateTime? LastModifiedAt,
            DateTime? CreatedAt,
            string Checksum
        );

        /// <summary>
        /// 仅供 2025 回填 Runner 使用的 POSM 日快照签名。
        /// </summary>
        public sealed record Posm2025DailySnapshotSignature(
            DateTime Date,
            Posm2025DailyTableSignature Orders,
            Posm2025DailyTableSignature Details,
            Posm2025DailyTableSignature Payments,
            Posm2025DailyTableSignature SalesReturns
        );

        /// <summary>
        /// 仅供 2025 回填 Runner 在同一进程内传递的 HBSales 批量快照。
        /// 明细不向普通业务调用方暴露，避免其误把快照当作通用查询 API。
        /// </summary>
        public sealed class HBSales2025BatchSnapshot
        {
            private readonly IReadOnlyDictionary<DateTime, List<ProductStoreDailySourceRow>> _rowsByDate;

            internal HBSales2025BatchSnapshot(
                IReadOnlyDictionary<DateTime, List<ProductStoreDailySourceRow>> rowsByDate,
                IReadOnlyDictionary<DateTime, HBSales2025DailySnapshotSignature> signatures
            )
            {
                _rowsByDate = rowsByDate;
                Signatures = signatures;
            }

            public IReadOnlyDictionary<DateTime, HBSales2025DailySnapshotSignature> Signatures { get; }

            internal IReadOnlyList<ProductStoreDailySourceRow> GetRows(DateTime date)
            {
                return _rowsByDate.TryGetValue(date.Date, out var rows)
                    ? rows
                    : throw new InvalidOperationException($"批量快照不包含日期: {date:yyyy-MM-dd}");
            }

            public HBSales2025DailySnapshotSignature GetSignature(DateTime date)
            {
                return Signatures.TryGetValue(date.Date, out var signature)
                    ? signature
                    : throw new InvalidOperationException($"批量快照不包含签名: {date:yyyy-MM-dd}");
            }
        }

        /// <summary>
        /// 2025 Runner 私有的 POSM 单日预载结果。业务行只在服务内部可见，避免普通入口误复用。
        /// </summary>
        public sealed class Posm2025DailySnapshot
        {
            internal Posm2025DailySnapshot(
                IReadOnlyList<ProductStoreDailySourceRow> detailRows,
                IReadOnlyList<ProductStoreDailySourceRow> supplementalReturnRows,
                IReadOnlyList<StoreStatisticPaymentRow> paymentRows,
                IReadOnlyList<StoreStatisticOrderRow> orderRows,
                Dictionary<string, string> deviceBranchMap,
                Posm2025DailySnapshotSignature signature
            )
            {
                DetailRows = detailRows;
                SupplementalReturnRows = supplementalReturnRows;
                PaymentRows = paymentRows;
                OrderRows = orderRows;
                DeviceBranchMap = deviceBranchMap;
                Signature = signature;
            }

            internal IReadOnlyList<ProductStoreDailySourceRow> DetailRows { get; }
            internal IReadOnlyList<ProductStoreDailySourceRow> SupplementalReturnRows { get; }
            internal IReadOnlyList<StoreStatisticPaymentRow> PaymentRows { get; }
            internal IReadOnlyList<StoreStatisticOrderRow> OrderRows { get; }
            internal Dictionary<string, string> DeviceBranchMap { get; }
            public Posm2025DailySnapshotSignature Signature { get; }
        }

        /// <summary>
        /// 商品统计提交串行锁，避免同一实例内并发请求重复提交相同日期。
        /// </summary>
        private static readonly SemaphoreSlim ProductStatisticSubmitLock = new(1, 1);

        internal sealed class StoreCostRow
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

        internal sealed class ProductStoreDailySourceRow
        {
            public bool IsHBSalesSource { get; set; }
            public DateTime Date { get; set; }
            public string? OrderGuid { get; set; }
            public string? HBSalesOrderNumber { get; set; }
            public string? DetailGuid { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
            // 2025 HBSales 预加载快照必须保留四个原始时间，不能只保留回退后的两个展示时间。
            public DateTime? HBSalesMainLastModifiedAt { get; set; }
            public DateTime? HBSalesMainCreatedAt { get; set; }
            public DateTime? HBSalesDetailLastModifiedAt { get; set; }
            public DateTime? HBSalesDetailCreatedAt { get; set; }
            public DateTime? OrderLastUploadTime { get; set; }
            public string? ProductCode { get; set; }
            public string? ItemNumber { get; set; }
            public string? SupplierCode { get; set; }
            public string? ProductName { get; set; }
            public string? Barcode { get; set; }
            public decimal Quantity { get; set; }
            public decimal ActualAmount { get; set; }
            public DateTime? DetailLastUploadTime { get; set; }
            public DateTime? SourceCreatedAt { get; set; }
            public DateTime? SourceUpdatedAt { get; set; }
            public string? DocumentType { get; set; }
        }

        internal sealed class StoreStatisticPaymentRow
        {
            public string? PaymentGuid { get; set; }
            public string? OrderGuid { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
            public decimal Amount { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public DateTime? LastUploadTime { get; set; }
        }

        private sealed class StoreStatisticQuantityRow
        {
            public string? OrderGuid { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
            public int Quantity { get; set; }
        }

        internal sealed class StoreStatisticOrderRow
        {
            public string? OrderGuid { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
            public DateTime? OrderTime { get; set; }
            public int? Status { get; set; }
            public DateTime? LastUploadTime { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }

        private sealed class HBSalesSourceWatermarkRow
        {
            public DateTime? MainLastModifiedAt { get; set; }
            public DateTime? MainCreatedAt { get; set; }
            public DateTime? DetailLastModifiedAt { get; set; }
            public DateTime? DetailCreatedAt { get; set; }
        }

        private sealed class HBSalesStoreAggregateRow
        {
            public string? BranchCode { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal TotalQuantity { get; set; }
            public int OrderCount { get; set; }
        }

        private sealed class HourlyStatisticSourceRow
        {
            public DateTime Date { get; set; }
            public int Hour { get; set; }
            public string? BranchCode { get; set; }
            public decimal TotalAmount { get; set; }
            public int TotalQuantity { get; set; }
            public int OrderCount { get; set; }
            public int CustomerCount { get; set; }
        }

        private sealed class OrderAmountRow
        {
            public string? OrderGuid { get; set; }
            public decimal Amount { get; set; }
        }

        private sealed class StoreSupplierSourceRow
        {
            public DateTime Date { get; set; }
            public string? BranchCode { get; set; }
            public string? DeviceCode { get; set; }
            public string? OrderGuid { get; set; }
            public string? DetailSupplierCode { get; set; }
            public string? LocalSupplierCode { get; set; }
            public string? ChinaSupplierCode { get; set; }
            public decimal ActualAmount { get; set; }
            public decimal Quantity { get; set; }
        }

        private sealed class StoreSupplierResolvedRow
        {
            public DateTime Date { get; set; }
            public string BranchCode { get; set; } = string.Empty;
            public string SupplierCode { get; set; } = string.Empty;
            public string SupplierName { get; set; } = string.Empty;
            public bool IsDomestic { get; set; }
            public string? OrderGuid { get; set; }
            public decimal TotalAmount { get; set; }
            public int TotalQuantity { get; set; }
        }

        /// <summary>
        /// 批量操作每批处理数量
        /// </summary>
        private const int BatchSize = 5000;

        private const string UnknownSupplierCode = "UNKNOWN";
        private const string UnknownSupplierName = "未匹配供应商";

        /// <summary>
        /// 数据库命令超时时间（秒）
        /// </summary>
        private const int CommandTimeoutSeconds = 1800;
        // 2025 runner 的单并发门禁保证相邻统计日不会并发提交，因此可用此有界窗口走主表结账日期索引，
        // 同时完整覆盖已确认的明细/主表最多三天日期不一致。
        private const int HBSalesMainCheckoutDateWindowDays = 7;
        private const int MaxProductStoreDailyBatchDays = 31;
        // 31 天在无详情日期索引的 HBSales 上一次读入；超过此上限立即失败，避免回填器耗尽内存。
        private const int MaxHBSales2025BatchSnapshotRows = 1_250_000;
        private const string ProvisionalFreshStatus = "ProvisionalFresh";
        internal const int StoreCostProductQueryBatchSize = 500;

        /// <summary>
        /// POSM数据库上下文
        /// </summary>
        private readonly POSMSqlSugarContext _posmContext;

        /// <summary>
        /// HBSalesRecord 数据库上下文；仅 2025 年商品日统计会读取该来源。
        /// </summary>
        private readonly HBSalesRecordSqlSugarContext? _hbSalesContext;

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
        /// <param name="hbSalesContext">HBSalesRecord 数据库上下文</param>
        public SalesStatisticsJobService(
            POSMSqlSugarContext posmContext,
            SqlSugarContext context,
            ILogger<SalesStatisticsJobService> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            HBSalesRecordSqlSugarContext? hbSalesContext = null
        )
        {
            _posmContext = posmContext;
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _serviceScopeFactory = serviceScopeFactory;
            _hbSalesContext = hbSalesContext;
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

                var statistic = await BuildDailySalesStatisticAsync(_posmContext, date, DateTime.Now);

                if (statistic != null)
                {
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

        private static async Task<DailySalesStatistic?> BuildDailySalesStatisticAsync(
            POSMSqlSugarContext posmContext,
            DateTime date,
            DateTime updateTime
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);

            var paymentSummary = await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                    (pd, so) => pd.OrderGuid == so.OrderGuid
                )
                .Where((pd, so) =>
                    so.Status != null
                    && (so.Status == 1 || so.Status == 4)
                    && so.OrderTime != null
                    && so.OrderTime >= targetDate
                    && so.OrderTime < nextDate
                )
                .GroupBy((pd, so) => so.OrderTime!.Value.Date)
                .Select((pd, so) => new
                {
                    TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                })
                .FirstAsync();

            var quantitySummary = await posmContext.Db.Queryable<SalesOrderDetail, SalesOrder>(
                    (d, so) => d.OrderGuid == so.OrderGuid
                )
                .Where((d, so) =>
                    so.Status != null
                    && (so.Status == 1 || so.Status == 4)
                    && so.OrderTime != null
                    && so.OrderTime >= targetDate
                    && so.OrderTime < nextDate
                )
                .GroupBy((d, so) => so.OrderTime!.Value.Date)
                .Select((d, so) => new
                {
                    TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0),
                })
                .FirstAsync();

            var orderRows = await posmContext.Db.Queryable<SalesOrder>()
                .Where(so =>
                    so.Status != null
                    && (so.Status == 1 || so.Status == 4)
                    && so.OrderTime != null
                    && so.OrderTime >= targetDate
                    && so.OrderTime < nextDate
                )
                .GroupBy(so => so.OrderGuid)
                .Select(so => new StoreStatisticOrderRow
                {
                    OrderGuid = so.OrderGuid,
                })
                .ToListAsync();

            var totalAmount = paymentSummary?.TotalAmount ?? 0m;
            var totalQuantity = quantitySummary?.TotalQuantity ?? 0;
            // 日统计金额、数量、订单数拆开在 SQL 端聚合，避免拆分支付或明细行把非金额指标放大。
            var orderCount = orderRows
                .Select(row => row.OrderGuid)
                .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
                .Count();

            if (totalAmount == 0m && totalQuantity == 0 && orderCount == 0)
            {
                return null;
            }

            return new DailySalesStatistic
            {
                Date = targetDate,
                TotalAmount = totalAmount,
                TotalQuantity = totalQuantity,
                OrderCount = orderCount,
                SkuCount = orderCount,
                CustomerCount = orderCount,
                AverageOrderValue = orderCount > 0 ? totalAmount / orderCount : 0m,
                UpdateTime = updateTime,
            };
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
                var rangeStart = hour.HasValue ? date.Date.AddHours(hour.Value) : date.Date;
                var rangeEnd = hour.HasValue ? rangeStart.AddHours(1) : date.Date.AddDays(1);

                _logger.LogInformation(
                    "开始更新分时统计数据: {Date}, 小时: {Hours}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23"
                );

                // 金额取支付明细、销量取销售明细、订单数取订单头，避免拆分支付放大非金额指标。
                var hourlyRevenueRows = await _posmContext
                    .Db.Queryable<PaymentDetail, SalesOrder>(
                        (pd, so) => pd.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (pd, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime >= rangeStart
                            && so.OrderTime < rangeEnd
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
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                            }
                    )
                    .ToListAsync();

                var hourlyQuantityRows = await _posmContext
                    .Db.Queryable<SalesOrderDetail, SalesOrder>(
                        (detail, so) => detail.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (detail, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime >= rangeStart
                            && so.OrderTime < rangeEnd
                    )
                    .GroupBy(
                        (detail, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                so.BranchCode,
                            }
                    )
                    .Select(
                        (detail, so) =>
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalQuantity = SqlFunc.AggregateSum(detail.Quantity) ?? 0,
                            }
                    )
                    .ToListAsync();

                var hourlyOrderRows = await _posmContext
                    .Db.Queryable<SalesOrder>()
                    .Where(
                        so =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime >= rangeStart
                            && so.OrderTime < rangeEnd
                    )
                    .GroupBy(
                        so =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                so.BranchCode,
                            }
                    )
                    .Select(
                        so =>
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                                CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                            }
                    )
                    .ToListAsync();

                var allHourlyData = hourlyRevenueRows
                    .Concat(hourlyQuantityRows)
                    .Concat(hourlyOrderRows)
                    .GroupBy(row => new { row.Date, row.Hour, row.BranchCode })
                    .Select(group => new HourlyStatisticSourceRow
                    {
                        Date = group.Key.Date,
                        Hour = group.Key.Hour,
                        BranchCode = group.Key.BranchCode,
                        TotalAmount = group.Sum(row => row.TotalAmount),
                        TotalQuantity = group.Sum(row => row.TotalQuantity),
                        OrderCount = group.Sum(row => row.OrderCount),
                        CustomerCount = group.Sum(row => row.CustomerCount),
                    })
                    .ToList();

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
                            OrderCount = hourlyDataForHour.Sum(d => d.OrderCount),
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
                        OrderCount = data.OrderCount,
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
            var targetDate = (date ?? DateTime.Now).Date;
            try
            {
                _logger.LogInformation("开始更新分店统计数据: {Date}", targetDate);

                if (targetDate.Year == 2025)
                {
                    // 2025 的分店与商品统计来自双来源，必须在同一事务内同时替换。
                    await Update2025StoreAndProductStatisticsAtomically(
                        _context,
                        _posmContext,
                        _hbSalesContext,
                        _logger,
                        targetDate
                    );
                    return;
                }

                var statisticsList = await BuildStoreStatisticsAsync(
                    _context,
                    _posmContext,
                    GetHBSalesContextFor2025(targetDate),
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
                _logger.LogError(ex, "更新分店统计数据失败: {Date}", targetDate);
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

                // 全量刷新统一走带数据库租约的入口，保证 8 张日级统计表口径一致且跨实例不重复跑。
                await RunLeasedFullRefreshForSingleDateAsync(previousDay, "前一天");

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

                // 当天主刷新也复用带数据库租约的完整路径，避免和手动补算抢同一天。
                var refreshed = await RunLeasedFullRefreshForSingleDateAsync(currentDay, "当天");
                if (!refreshed)
                {
                    return;
                }

                // POSM 可能延迟上传，商品统计额外滚动补算最近 7 天。
                for (var offset = 1; offset < 7; offset++)
                {
                    await RunLeasedProductStoreDailyRefreshAsync(currentDay.AddDays(-offset));
                }

                _logger.LogInformation("当天数据全量刷新完成: {Date}", currentDay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "全量刷新当天数据失败");
                throw;
            }
        }

        private async Task<bool> RunLeasedFullRefreshForSingleDateAsync(
            DateTime date,
            string label
        )
        {
            var result = await BatchFullRefreshConcurrent(date, date, 1);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }

            if (result.SkippedDates.Any())
            {
                _logger.LogInformation(
                    "{Label}数据全量刷新跳过，日期 {Date} 已有运行中统计租约",
                    label,
                    date.ToString("yyyy-MM-dd")
                );
                return false;
            }

            return true;
        }

        private async Task<bool> RunLeasedProductStoreDailyRefreshAsync(DateTime date)
        {
            var targetDate = date.Date;
            using var scope = _serviceScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
            var posmContext = scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();
            var hbSalesContext = scope.ServiceProvider.GetService<HBSalesRecordSqlSugarContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SalesStatisticsJobService>>();
            var leaseService = scope.ServiceProvider.GetRequiredService<ScheduledTaskLeaseService>();
            var dateStr = targetDate.ToString("yyyy-MM-dd");
            var leaseTaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType;
            var leaseDuration = TimeSpan.FromHours(2);
            string? leaseToken = null;
            DateTime? sourceWatermark = null;

            try
            {
                var lease = await leaseService.TryAcquireAsync(leaseTaskType, dateStr, leaseDuration);
                if (!lease.Acquired)
                {
                    _logger.LogInformation("日期 {Date} 已有统计租约运行中，商品滚动补算跳过", dateStr);
                    return false;
                }

                leaseToken = lease.Lease?.LeaseToken;
                if (string.IsNullOrWhiteSpace(leaseToken))
                {
                    throw new InvalidOperationException($"统计租约缺少 fencing token: {dateStr}");
                }

                await leaseService.EnsureActiveAsync(
                    leaseTaskType,
                    dateStr,
                    leaseToken,
                    leaseDuration,
                    "商品分店每日滚动补算"
                );
                if (targetDate.Year == 2025)
                {
                    // 滚动补算同样不能让商品表单独 Running/Failed，原子入口会成对维护状态。
                    await Update2025StoreAndProductStatisticsAtomically(
                        context,
                        posmContext,
                        hbSalesContext,
                        logger,
                        targetDate
                    );
                }
                else
                {
                    sourceWatermark = await QueryDailySourceWatermarkAsync(
                        posmContext,
                        hbSalesContext,
                        targetDate
                    );
                    await UpsertStatisticStateAsync(
                        context,
                        SalesStatisticType.ProductStoreDaily,
                        targetDate,
                        SalesStatisticRefreshStatus.Running,
                        sourceWatermark,
                        null
                    );
                    await UpdateProductStoreDailyStatisticsWithContext(
                        context,
                        posmContext,
                        hbSalesContext,
                        logger,
                        targetDate
                    );
                }
                await leaseService.EnsureActiveAsync(
                    leaseTaskType,
                    dateStr,
                    leaseToken,
                    leaseDuration,
                    "商品分店每日滚动补算完成确认"
                );
                if (!await leaseService.CompleteAsync(leaseTaskType, dateStr, leaseToken, true))
                {
                    throw new InvalidOperationException($"统计租约完成失败，token 已失效: {dateStr}");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (targetDate.Year != 2025)
                {
                    await UpsertStatisticStateAsync(
                        context,
                        SalesStatisticType.ProductStoreDaily,
                        targetDate,
                        SalesStatisticRefreshStatus.Failed,
                        sourceWatermark,
                        ex.Message
                    );
                }
                if (!string.IsNullOrWhiteSpace(leaseToken))
                {
                    await leaseService.CompleteAsync(leaseTaskType, dateStr, leaseToken, false, ex.Message);
                }
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
            var targetDate = date.Date;
            var targetBranchCodes = NormalizeBranchCodes(branchCodes);
            try
            {
                _logger.LogInformation(
                    "开始更新指定分店统计数据: {Date}, Branches: {Branches}",
                    targetDate,
                    targetBranchCodes.Any() ? string.Join(", ", targetBranchCodes) : "All"
                );

                if (targetDate.Year == 2025)
                {
                    if (targetBranchCodes.Any())
                    {
                        throw new InvalidOperationException(
                            "2025 年不能仅刷新指定分店：该操作会破坏 ProductStoreDaily 与 StoreSales 的双表一致性，请执行全分店刷新"
                        );
                    }

                    // null、空集合和空白分店代码均等同全分店，统一进入双表原子刷新。
                    await Update2025StoreAndProductStatisticsAtomically(
                        _context,
                        _posmContext,
                        _hbSalesContext,
                        _logger,
                        targetDate
                    );
                    return;
                }

                var statisticsList = await BuildStoreStatisticsAsync(
                    _context,
                    _posmContext,
                    GetHBSalesContextFor2025(targetDate),
                    targetDate,
                    branchCodes
                );

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 指定分店重算只替换对应分店，避免清掉同日其它分店统计。
                        var deleteable = _context.Db.Deleteable<StoreSalesStatistic>()
                            .Where(s => s.Date == targetDate);
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
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新指定分店统计数据失败: {Date}", targetDate);
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
                var targetEndExclusive = targetEndDate.AddDays(1);

                if (targetStartDate > targetEndDate)
                {
                    throw new ArgumentException("开始日期不能大于结束日期");
                }

                var targetSupplierCodes = NormalizeSupplierCodes(supplierCodes);

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

                // 使用半开区间过滤，避免不同数据库对 DateTime.Date 的翻译差异。
                query = query.Where(o =>
                    o.OrderTime >= targetStartDate
                    && o.OrderTime < targetEndExclusive
                );

                // 设置供应商代码过滤条件
                if (targetSupplierCodes.Any())
                {
                    var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                    query = query.Where(
                        (o, d, m) =>
                            (m.LocalSupplierCode != null
                                && targetSupplierCodes.Contains(m.LocalSupplierCode.Trim()))
                            || (m.ChinaSupplierCode != null
                                && targetSupplierCodes.Contains(m.ChinaSupplierCode.Trim()))
                            || (d.SupplierCode != null
                                && targetSupplierCodes.Contains(d.SupplierCode.Trim()))
                            || (
                                includesUnknownSupplier
                                && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                                && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                            )
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
                                DetailSupplierCode = d.SupplierCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = d.ActualAmount,
                                Quantity = d.Quantity ?? 0m,
                                OrderGuid = o.OrderGuid,
                            }
                    )
                    .ToListAsync();
                var orderAmountMaps = await LoadOrderAmountMapsAsync(
                    _posmContext,
                    targetStartDate,
                    targetEndExclusive,
                    rawData,
                    row => row.OrderGuid,
                    row => row.TotalAmount ?? 0m
                );

                var resolvedRows = rawData
                    .Select(x => new
                    {
                        x.Date,
                        BranchCode = NormalizeCode(x.BranchCode),
                        LocalSupplierCode = ResolveStatisticSupplierCode(
                            x.LocalSupplierCode,
                            x.DetailSupplierCode
                        ),
                        ChinaSupplierCode = NormalizeCode(x.ChinaSupplierCode),
                        TotalAmount = ResolveStatisticAmount(
                            x.OrderGuid,
                            x.TotalAmount ?? 0m,
                            orderAmountMaps.PaymentAmounts,
                            orderAmountMaps.DetailAmounts
                        ),
                        x.Quantity,
                        x.OrderGuid,
                    })
                    .ToList();

                var shouldRefreshAllSuppliers = !targetSupplierCodes.Any();
                var refreshesLocalMasterSupplier = targetSupplierCodes.Contains("200");
                var localRowsForStats = shouldRefreshAllSuppliers
                    ? resolvedRows
                    : resolvedRows.Where(x => targetSupplierCodes.Contains(x.LocalSupplierCode)).ToList();
                var chinaRowsForStats = resolvedRows
                    .Where(x =>
                        x.LocalSupplierCode == "200"
                        && !string.IsNullOrEmpty(x.ChinaSupplierCode)
                        && (
                            shouldRefreshAllSuppliers
                            || refreshesLocalMasterSupplier
                            || targetSupplierCodes.Contains(x.ChinaSupplierCode)
                        )
                    )
                    .ToList();

                // 1. 本地供应商聚合：局部刷新国内子供应商时不重写本地 200 总计。
                var localStats = localRowsForStats
                    .GroupBy(x => new { x.Date, x.LocalSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.LocalSupplierCode,
                        IsDomestic = false,
                        TotalAmount = g.Sum(x => x.TotalAmount),
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Distinct()
                            .Count(),
                        OrderCount = g.Select(x => x.OrderGuid)
                            .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
                            .Distinct()
                            .Count(),
                        UpdateTime = DateTime.Now,
                    })
                    .ToList();

                // 2. 国内供应商聚合
                // 仅针对 LocalSupplierCode == "200" 的记录进行二次聚合
                // 这些记录如果包含 ChinaSupplierCode，则按 ChinaSupplierCode 再统计一次
                // 这样可以得到每个具体的国内供应商的销量数据
                // 这些数据的 IsDomestic 标记为 true
                var chinaStats = chinaRowsForStats
                    .GroupBy(x => new { x.Date, x.ChinaSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.ChinaSupplierCode ?? string.Empty,
                        IsDomestic = true,
                        TotalAmount = g.Sum(x => x.TotalAmount),
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Distinct()
                            .Count(),
                        OrderCount = g.Select(x => x.OrderGuid)
                            .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
                            .Distinct()
                            .Count(),
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
                    if (stat.SupplierCode == UnknownSupplierCode)
                    {
                        stat.SupplierName = UnknownSupplierName;
                    }
                    else if (supplierNameDict.TryGetValue(stat.SupplierCode, out var name))
                    {
                        stat.SupplierName = name;
                    }
                    else
                    {
                        stat.SupplierName = stat.SupplierCode;
                    }
                }

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 供应商统计按目标范围重建，避免旧空供应商或已无销售供应商残留。
                        var deleteable = _context.Db.Deleteable<SupplierSalesStatistic>()
                            .Where(s => s.Date >= targetStartDate && s.Date <= targetEndDate);
                        if (targetSupplierCodes.Any())
                        {
                            var deleteSupplierCodes = new List<string>();
                            if (refreshesLocalMasterSupplier)
                            {
                                var existingDomesticSupplierCodes = await _context
                                    .Db.Queryable<SupplierSalesStatistic>()
                                    .Where(s =>
                                        s.Date >= targetStartDate
                                        && s.Date <= targetEndDate
                                        && s.IsDomestic == true
                                    )
                                    .Select(s => s.SupplierCode)
                                    .Distinct()
                                    .ToListAsync();
                                deleteSupplierCodes.AddRange(existingDomesticSupplierCodes);
                            }

                            deleteSupplierCodes = deleteSupplierCodes
                                .Concat(targetSupplierCodes)
                                .Concat(allStats.Select(s => s.SupplierCode))
                                .Where(code => !string.IsNullOrWhiteSpace(code))
                                .Select(code => code.Trim())
                                .Distinct()
                                .ToList();
                            var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                            deleteable = deleteable.Where(s =>
                                deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                                || (
                                    includesUnknownSupplier
                                    && (s.SupplierCode == null || s.SupplierCode.Trim() == "")
                                )
                            );
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条供应商统计旧记录", deletedCount);

                        if (!allStats.Any())
                        {
                            _logger.LogInformation(
                                "没有找到供应商统计数据: {StartDate} 至 {EndDate}",
                                targetStartDate.ToString("yyyy-MM-dd"),
                                targetEndDate.ToString("yyyy-MM-dd")
                            );
                            return;
                        }

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
            if (targetDate.Year == 2025)
            {
                // 2025 双来源统计必须同时切换两张日表，不能留下新旧口径混合的中间状态。
                await Update2025StoreAndProductStatisticsAtomically(
                    _context,
                    _posmContext,
                    GetHBSalesContextFor2025(targetDate)!,
                    _logger,
                    targetDate
                );
                return;
            }
            await UpdateProductStoreDailyStatisticsWithContext(
                _context,
                _posmContext,
                GetHBSalesContextFor2025(targetDate),
                _logger,
                targetDate
            );
        }

        /// <summary>
        /// 读取最多 31 个 2025 日期的 HBSales 明细快照，并按详情结账日期切片。
        /// 此入口只给回填 Runner 使用；普通日刷新继续走自身的单日读取和 post 复核。
        /// </summary>
        public async Task<HBSales2025BatchSnapshot> Load2025HBSalesBatchSnapshotAsync(
            IReadOnlyCollection<DateTime> dates
        )
        {
            var targetDates = dates.Select(date => date.Date).Distinct().OrderBy(date => date).ToArray();
            if (targetDates.Length == 0 || targetDates.Length > MaxProductStoreDailyBatchDays)
            {
                throw new ArgumentException(
                    $"2025 HBSales 批量快照日期必须为 1 至 {MaxProductStoreDailyBatchDays} 天",
                    nameof(dates)
                );
            }
            if (targetDates.Any(date => date.Year != 2025))
                throw new ArgumentException("2025 HBSales 批量快照只接受 2025 日期", nameof(dates));

            var rows = await LoadHBSalesProductStoreDailyRowsAsync(
                GetHBSalesContextFor2025(targetDates[0])!,
                targetDates[0],
                targetDates[^1].AddDays(1),
                MaxHBSales2025BatchSnapshotRows
            );
            var targetDateSet = targetDates.ToHashSet();
            var rowsByDate = targetDates.ToDictionary(date => date, _ => new List<ProductStoreDailySourceRow>());
            foreach (var row in rows)
            {
                if (targetDateSet.Contains(row.Date.Date))
                    rowsByDate[row.Date.Date].Add(row);
            }

            var signatures = rowsByDate.ToDictionary(
                entry => entry.Key,
                entry => CreateHBSales2025DailySnapshotSignature(entry.Key, entry.Value)
            );
            _logger.LogInformation(
                "已读取 2025 HBSales 批量快照: {StartDate} 至 {EndDate}, 日期数 {DateCount}, 明细数 {RowCount}, 内存上限 {MaxRows}",
                targetDates[0],
                targetDates[^1],
                targetDates.Length,
                rows.Count,
                MaxHBSales2025BatchSnapshotRows
            );
            return new HBSales2025BatchSnapshot(rowsByDate, signatures);
        }

        /// <summary>
        /// 仅给 2025 回填 Runner 使用：同一日一次加载 POSM 的订单、明细、支付、补充退货及设备分店映射。
        /// </summary>
        public async Task<Posm2025DailySnapshot> Load2025PosmDailySnapshotAsync(DateTime date)
        {
            var targetDate = date.Date;
            if (targetDate.Year != 2025)
                throw new ArgumentException("POSM 预载入口只接受 2025 日期", nameof(date));

            var nextDate = targetDate.AddDays(1);
            var orderRows = await _posmContext.Db.Queryable<SalesOrder>()
                .Where(order => order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= targetDate
                    && order.OrderTime < nextDate)
                .Select(order => new StoreStatisticOrderRow
                {
                    OrderGuid = order.OrderGuid,
                    BranchCode = order.BranchCode,
                    DeviceCode = order.DeviceCode,
                    OrderTime = order.OrderTime,
                    Status = order.Status,
                    LastUploadTime = order.LastUploadTime,
                    CreatedAt = order.CreatedTime,
                    UpdatedAt = order.UpdatedTime,
                })
                .ToListAsync();
            var detailRows = await _posmContext.Db.Queryable<SalesOrder>()
                .LeftJoin<SalesOrderDetail>((order, detail) => order.OrderGuid == detail.OrderGuid)
                .Where(order => order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= targetDate
                    && order.OrderTime < nextDate)
                .Select((order, detail) => new ProductStoreDailySourceRow
                {
                    Date = order.OrderTime!.Value.Date,
                    OrderGuid = order.OrderGuid,
                    DetailGuid = detail.OrderDetailGuid,
                    BranchCode = order.BranchCode,
                    DeviceCode = order.DeviceCode,
                    OrderLastUploadTime = order.LastUploadTime,
                    ProductCode = detail.ProductCode,
                    SupplierCode = detail.SupplierCode,
                    ProductName = detail.ProductName,
                    Barcode = detail.Barcode,
                    Quantity = detail.Quantity ?? 0m,
                    ActualAmount = detail.ActualAmount ?? 0m,
                    DetailLastUploadTime = detail.LastUploadTime,
                    SourceCreatedAt = detail.CreatedTime,
                    SourceUpdatedAt = detail.UpdatedTime,
                })
                .ToListAsync();
            var paymentRows = await _posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                    (payment, order) => payment.OrderGuid == order.OrderGuid
                )
                .Where((payment, order) => order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= targetDate
                    && order.OrderTime < nextDate)
                .Select((payment, order) => new StoreStatisticPaymentRow
                {
                    PaymentGuid = payment.PaymentGuid,
                    OrderGuid = payment.OrderGuid,
                    BranchCode = order.BranchCode,
                    DeviceCode = order.DeviceCode,
                    Amount = payment.Amount ?? 0m,
                    CreatedAt = payment.CreatedTime,
                    UpdatedAt = payment.UpdatedTime,
                    LastUploadTime = payment.LastUploadTime,
                })
                .ToListAsync();
            var detailGuidSet = detailRows
                .Select(row => row.DetailGuid)
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Select(guid => guid!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var supplementalReturnRows = await LoadSupplementalReturnRowsAsync(
                _posmContext,
                targetDate,
                nextDate,
                detailGuidSet
            );
            var deviceBranchMap = await LoadDeviceBranchMapAsync(
                _posmContext,
                detailRows.Select(row => row.DeviceCode)
                    .Concat(paymentRows.Select(row => row.DeviceCode))
                    .Concat(orderRows.Select(row => row.DeviceCode))
                    .Concat(supplementalReturnRows.Select(row => row.DeviceCode))
            );
            var signature = CreatePosm2025DailySnapshotSignature(
                targetDate,
                orderRows,
                detailRows,
                paymentRows,
                supplementalReturnRows
            );
            return new Posm2025DailySnapshot(
                detailRows,
                supplementalReturnRows,
                paymentRows,
                orderRows,
                deviceBranchMap,
                signature
            );
        }

        /// <summary>
        /// 使用既有批量快照刷新单个 2025 日期。提交状态先标记为 ProvisionalFresh，
        /// 必须由 Runner 的批末签名复核后再显式升级为 Fresh。
        /// </summary>
        public async Task Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(
            DateTime date,
            HBSales2025BatchSnapshot snapshot
        )
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var targetDate = date.Date;
            if (targetDate.Year != 2025)
                throw new ArgumentException("批量预载入口只接受 2025 日期", nameof(date));

            var expectedSignature = snapshot.GetSignature(targetDate);
            var snapshotStopwatch = Stopwatch.StartNew();
            var posmSnapshot = await Load2025PosmDailySnapshotAsync(targetDate);
            _logger.LogInformation(
                "2025 Runner POSM snapshot load 完成: {Date}, {ElapsedMilliseconds}ms, orders={Orders}, details={Details}, payments={Payments}, returns={Returns}",
                targetDate,
                snapshotStopwatch.ElapsedMilliseconds,
                posmSnapshot.Signature.Orders.RowCount,
                posmSnapshot.Signature.Details.RowCount,
                posmSnapshot.Signature.Payments.RowCount,
                posmSnapshot.Signature.SalesReturns.RowCount
            );
            await Update2025StoreAndProductStatisticsAtomically(
                _context,
                _posmContext,
                GetHBSalesContextFor2025(targetDate),
                _logger,
                targetDate,
                null,
                snapshot.GetRows(targetDate),
                expectedSignature,
                deferHBSalesStabilityToBatchEnd: true,
                preloadedPosmSnapshot: posmSnapshot
            );
        }

        /// <summary>
        /// 批末签名一致后，把一日双状态从 ProvisionalFresh 成对提升为 Fresh。
        /// </summary>
        public async Task Finalize2025BatchSnapshotDateAsync(DateTime date)
        {
            var targetDate = date.Date;
            await ExecuteTransactionSafelyAsync(
                beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    var states = await _context.Db.Queryable<SalesStatisticRefreshState>()
                        .Where(state =>
                            state.Date >= targetDate
                            && state.Date < targetDate.AddDays(1)
                            && (state.StatisticType == SalesStatisticType.ProductStoreDaily
                                || state.StatisticType == SalesStatisticType.StoreSales)
                        )
                        .ToListAsync();
                    var productState = states.FirstOrDefault(state =>
                        state.StatisticType == SalesStatisticType.ProductStoreDaily
                    );
                    var storeState = states.FirstOrDefault(state =>
                        state.StatisticType == SalesStatisticType.StoreSales
                    );
                    if (productState?.Status != ProvisionalFreshStatus
                        || storeState?.Status != ProvisionalFreshStatus
                        || productState.LastSourceUploadTime != storeState.LastSourceUploadTime)
                    {
                        throw new InvalidOperationException(
                            $"批末复核前双状态不是成对 ProvisionalFresh: {targetDate:yyyy-MM-dd}"
                        );
                    }

                    // 批末确认只升级状态，不重写水位；水位仍精确绑定到预载快照和 POSM pre/post 复核。
                    productState.Status = SalesStatisticRefreshStatus.Fresh;
                    productState.ErrorMessage = null;
                    productState.LastCheckedAtUtc = DateTime.UtcNow;
                    productState.CompletedAtUtc = DateTime.UtcNow;
                    storeState.Status = SalesStatisticRefreshStatus.Fresh;
                    storeState.ErrorMessage = null;
                    storeState.LastCheckedAtUtc = DateTime.UtcNow;
                    storeState.CompletedAtUtc = DateTime.UtcNow;
                    await _context.Db.Updateable(productState).ExecuteCommandAsync();
                    await _context.Db.Updateable(storeState).ExecuteCommandAsync();
                },
                commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                logger: _logger,
                operationName: "2025 批末稳定性确认"
            );
        }

        /// <summary>
        /// 批末签名不一致时使指定日期不能作为可跳过的 Fresh 断点继续使用。
        /// </summary>
        public Task Fail2025BatchSnapshotDatesAsync(
            IReadOnlyCollection<DateTime> dates,
            string errorMessage
        )
        {
            return Fail2025BatchSnapshotDatesSequentiallyAsync(dates, errorMessage);
        }

        private async Task Fail2025BatchSnapshotDatesSequentiallyAsync(
            IReadOnlyCollection<DateTime> dates,
            string errorMessage
        )
        {
            foreach (var date in dates.Select(date => date.Date).Distinct().OrderBy(date => date))
            {
                await Persist2025AtomicFailureStatesAsync(
                    _context,
                    _logger,
                    date,
                    null,
                    new InvalidOperationException(errorMessage)
                );
            }
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
            var normalizedMaxConcurrency = ResolveProductStatisticMaxConcurrency(
                targetDates,
                maxConcurrency
            );

            if (!targetDates.Any())
            {
                return new ProductStoreDailyRecalculationSubmitResult
                {
                    JobId = jobId,
                    Status = SalesStatisticRefreshStatus.Pending,
                    Message = "没有可提交的商品统计日期",
                };
            }

            if (targetDates.Count > MaxProductStoreDailyBatchDays)
            {
                // 与控制器的 400 校验保持同一条输入语义，服务层也不能被其它调用方绕过。
                throw new ArgumentException(
                    $"商品分店每日统计一次最多重算 {MaxProductStoreDailyBatchDays} 天，请分段执行",
                    nameof(dates)
                );
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

        private static int ResolveProductStatisticMaxConcurrency(
            IReadOnlyCollection<DateTime> dates,
            int maxConcurrency
        )
        {
            // 2025 会同时重建分店与商品两张日表；同一批次必须串行，避免不同日期的双来源读取争抢资源。
            return dates.Any(date => date.Year == 2025)
                ? 1
                : NormalizeProductStatisticMaxConcurrency(maxConcurrency);
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
                var normalizedMaxConcurrency = ResolveProductStatisticMaxConcurrency(
                    dates,
                    maxConcurrency
                );
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
            HBSalesRecordSqlSugarContext? hbSalesContext,
            ILogger logger,
            DateTime date,
            IReadOnlyList<StoreSalesStatistic>? atomicStoreStatistics = null,
            DateTime? sourceWatermarkOverride = null,
            Func<Task>? validateSourceWatermarkBeforeCommitAsync = null,
            IReadOnlyList<ProductStoreDailySourceRow>? preloadedHBSalesRows = null,
            string? atomicSuccessStatusOverride = null,
            Posm2025DailySnapshot? preloadedPosmSnapshot = null
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);
            var originalCommandTimeout = context.Db.Ado.CommandTimeOut;
            context.Db.Ado.CommandTimeOut = Math.Max(originalCommandTimeout, CommandTimeoutSeconds);
            try
            {
                logger.LogInformation("开始更新商品分店每日统计: {Date}", targetDate);

                // POSM 上传水位只和 POSM 自己比较，不和 HBweb UTC 时间直接比较。
                var lastSourceUploadTime = preloadedPosmSnapshot == null
                    ? await posmContext.Db.Queryable<SalesOrder>()
                        .Where(o => o.Status != null
                            && (o.Status == 1 || o.Status == 4)
                            && o.OrderTime != null
                            && o.OrderTime >= targetDate
                            && o.OrderTime < nextDate)
                        .MaxAsync(o => o.LastUploadTime)
                    : GetPosmSnapshotWatermark(preloadedPosmSnapshot);

                // 2025 年商品日统计需要将 HBSales 与 POSM 明细并列汇总；其他年份严格保持 POSM-only。
                var hbSalesRows = targetDate.Year == 2025
                    ? preloadedHBSalesRows?.ToList()
                        ?? await LoadHBSalesProductStoreDailyRowsAsync(
                            hbSalesContext
                                ?? throw new InvalidOperationException("2025 年商品统计缺少 HBSalesRecord 上下文"),
                            targetDate,
                            nextDate
                        )
                    : new List<ProductStoreDailySourceRow>();

                if (targetDate.Year == 2025)
                {
                    // 技术性零值行不参与统计；非零缺货号行仅允许由三类强键唯一解析，不能按名称或历史记录猜测。
                    hbSalesRows = hbSalesRows
                        .Where(row => row.Quantity != 0m || row.ActualAmount != 0m)
                        .ToList();
                    await ResolveMissingHBSalesProductCodesAsync(context, hbSalesRows);

                    var invalidHBSalesRows = hbSalesRows
                        .Where(row =>
                            string.IsNullOrWhiteSpace(row.BranchCode)
                            || string.IsNullOrWhiteSpace(row.ProductCode)
                        )
                        .ToList();
                    if (invalidHBSalesRows.Any())
                    {
                        var missingFields = new List<string>();
                        if (invalidHBSalesRows.Any(row => string.IsNullOrWhiteSpace(row.BranchCode)))
                            missingFields.Add("分店编码");
                        if (invalidHBSalesRows.Any(row => string.IsNullOrWhiteSpace(row.ProductCode)))
                            missingFields.Add("商品编码且无法获得唯一商品候选");
                        throw new InvalidOperationException(
                            $"2025 HBSales 存在 {invalidHBSalesRows.Count} 条非零来源行缺少{string.Join("或", missingFields)}，不能替换双表统计: {targetDate:yyyy-MM-dd}"
                        );
                    }
                }
                var hbSalesLastModifiedTime = GetLatestSourceTime(hbSalesRows);
                lastSourceUploadTime = GetLatestSourceTime(lastSourceUploadTime, hbSalesLastModifiedTime);
                var missingHBSalesBranchCount = hbSalesRows.Count(row =>
                    string.IsNullOrWhiteSpace(row.BranchCode)
                );
                if (missingHBSalesBranchCount > 0)
                {
                    // 缺分店行无法落到日分店统计，只记录诊断，不能擅自改变既有订单或取整口径。
                    logger.LogWarning(
                        "2025 HBSales 有 {Count} 条明细缺少分店编码，未写入商品分店统计: {Date}",
                        missingHBSalesBranchCount,
                        targetDate.ToString("yyyy-MM-dd")
                    );
                }

                // 商品统计以明细表为主；老系统退货也在明细表中，负数会自然冲减。
                var detailRows = preloadedPosmSnapshot?.DetailRows.ToList() ?? await posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    )
                    .Select((o, d) => new ProductStoreDailySourceRow
                    {
                        Date = o.OrderTime!.Value.Date,
                        OrderGuid = o.OrderGuid,
                        DetailGuid = d.OrderDetailGuid,
                        BranchCode = o.BranchCode,
                        DeviceCode = o.DeviceCode,
                        OrderLastUploadTime = o.LastUploadTime,
                        ProductCode = d.ProductCode,
                        SupplierCode = d.SupplierCode,
                        ProductName = d.ProductName,
                        Barcode = d.Barcode,
                        Quantity = d.Quantity ?? 0,
                        ActualAmount = d.ActualAmount ?? 0m,
                        DetailLastUploadTime = d.LastUploadTime,
                    })
                    .ToListAsync();
                var detailGuidSet = detailRows
                    .Select(x => x.DetailGuid)
                    .Where(guid => !string.IsNullOrWhiteSpace(guid))
                    .Select(guid => guid!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var supplementalReturnRows = preloadedPosmSnapshot?.SupplementalReturnRows.ToList()
                    ?? await LoadSupplementalReturnRowsAsync(posmContext, targetDate, nextDate, detailGuidSet);

                var rawRows = detailRows
                    .Concat(supplementalReturnRows)
                    .Concat(hbSalesRows)
                    .ToList();
                var supplementalReturnRowSet = supplementalReturnRows.ToHashSet();
                // 销售明细按支付金额分摊；仅落退货表的补充行继续按退货金额直接冲减。
                var orderAmountMaps = preloadedPosmSnapshot == null
                    ? await LoadOrderAmountMapsAsync(posmContext, targetDate, nextDate, detailRows, row => row.OrderGuid, row => row.ActualAmount)
                    : (
                        PaymentAmounts: BuildOrderAmountMap(preloadedPosmSnapshot.PaymentRows.Select(row => new OrderAmountRow
                        {
                            OrderGuid = row.OrderGuid,
                            Amount = row.Amount,
                        })),
                        DetailAmounts: BuildOrderAmountMap(detailRows.Select(row => new OrderAmountRow
                        {
                            OrderGuid = row.OrderGuid,
                            Amount = row.ActualAmount,
                        }))
                    );

                var deviceBranchMap = preloadedPosmSnapshot?.DeviceBranchMap
                    ?? await LoadDeviceBranchMapAsync(
                        posmContext,
                        rawRows.Where(row => string.IsNullOrWhiteSpace(row.BranchCode)).Select(row => row.DeviceCode)
                    );

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

                var storeCosts = await LoadStoreCostsInBatchesAsync(
                    context,
                    productCodes,
                    branchCodes
                );
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

                // 先统一解析分店；空供应商保留诊断，同时主表归入 UNKNOWN，保证商品统计总额不漏数。
                var resolvedRows = rawRows
                    .Select(x => new
                    {
                        Row = x,
                        ResolvedBranchCode = ResolveBranchCode(x.BranchCode, x.DeviceCode, deviceBranchMap),
                        StatisticAmount = x.IsHBSalesSource || supplementalReturnRowSet.Contains(x)
                            ? x.ActualAmount
                            : ResolveStatisticAmount(
                                x.OrderGuid,
                                x.ActualAmount,
                                orderAmountMaps.PaymentAmounts,
                                orderAmountMaps.DetailAmounts
                            ),
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
                    diagnostics.UnmatchedSupplierAmount = unmatchedSupplierRows.Sum(x => x.StatisticAmount);
                    diagnostics.UnmatchedSupplierQuantity = (int)unmatchedSupplierRows.Sum(x => x.Row.Quantity);
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
                                UnmatchedSupplierAmount = group.Sum(x => x.StatisticAmount),
                                UnmatchedSupplierQuantity = (int)group.Sum(x => x.Row.Quantity),
                                UnmatchedSupplierProductCount = group
                                    .Select(x => x.Row.ProductCode)
                                    .Where(code => !string.IsNullOrWhiteSpace(code))
                                    .Distinct()
                                    .Count(),
                            }
                        );
                }

                var statisticsList = resolvedRows
                    .GroupBy(x => new
                    {
                        x.Row.Date,
                        BranchCode = x.ResolvedBranchCode,
                        SupplierCode = ResolveStatisticSupplierCode(x.Row.SupplierCode, null),
                        ProductCode = x.Row.ProductCode!.Trim(),
                    })
                    .Select(group =>
                    {
                        // HBSales 数量是 decimal；必须先按商品汇总，再按既有年度口径转成整数。
                        var sourceQuantity = group.Sum(x => x.Row.Quantity);
                        var quantity = (int)sourceQuantity;
                        var totalAmount = group.Sum(x => x.StatisticAmount);
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

                var supplementalReturnAdjustments = resolvedRows
                    .Where(x => supplementalReturnRowSet.Contains(x.Row))
                    .GroupBy(x => x.ResolvedBranchCode, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new ProductStoreDailyBranchRollup(
                        group.Key,
                        group.Sum(x => x.StatisticAmount),
                        (int)group.Sum(x => x.Row.Quantity)
                    ))
                    .ToList();

                var status = await BuildProductStatisticStatusAsync(
                    context,
                    targetDate,
                    statisticsList,
                    diagnostics,
                    lastSourceUploadTime,
                    supplementalReturnAdjustments,
                    atomicStoreStatistics
                );
                if (!statisticsList.Any() && rawRows.Any())
                {
                    // 有来源行却没有任何有效商品分店主键时不能当作合法空销售日成功；此判断不依赖对账状态。
                    status = new ProductStatisticStatusResult(
                        SalesStatisticRefreshStatus.Failed,
                        $"商品分店每日统计存在 {rawRows.Count} 条来源记录，但没有可写入的有效分店商品: {targetDate:yyyy-MM-dd}"
                    );
                }

                if (atomicStoreStatistics != null && status.Status == SalesStatisticRefreshStatus.Failed)
                {
                    // 2025 双表原子入口不能把业务失败当作可提交结果，否则会用空/不完整结果覆盖旧统计。
                    throw new InvalidOperationException(status.ErrorMessage ?? "2025 商品分店每日统计业务校验失败");
                }

                if (atomicStoreStatistics != null
                    && status.Status == SalesStatisticRefreshStatus.Fresh
                    && !string.IsNullOrWhiteSpace(atomicSuccessStatusOverride))
                {
                    // 批量快照在批末复核前绝不能留下可跳过的 Fresh。
                    status = new ProductStatisticStatusResult(
                        atomicSuccessStatusOverride,
                        status.ErrorMessage
                    );
                }

                // 原子入口传入的水位已同时覆盖订单头、明细、支付和 HBSales；两类状态必须写同一个值。
                var effectiveSourceWatermark = sourceWatermarkOverride ?? lastSourceUploadTime;

                if (validateSourceWatermarkBeforeCommitAsync != null)
                {
                    // 构建完成后、开启主库事务前再次确认来源快照未漂移。
                    await validateSourceWatermarkBeforeCommitAsync();
                }

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        if (atomicStoreStatistics != null)
                        {
                            // 2025 先替换分店基线，再替换商品日表和两类状态；四项写入必须同一事务提交。
                            await context.Db.Deleteable<StoreSalesStatistic>()
                                .Where(s => s.Date == targetDate)
                                .ExecuteCommandAsync();
                            if (atomicStoreStatistics.Any())
                            {
                                context.Db.Fastest<StoreSalesStatistic>()
                                    .PageSize(BatchSize)
                                    .BulkCopy(atomicStoreStatistics.ToList());
                            }
                        }

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

                        await UpsertProductStatisticStateAsync(
                            context,
                            targetDate,
                            status,
                            effectiveSourceWatermark,
                            overwriteLastSourceUploadTime: atomicStoreStatistics != null
                        );
                        if (atomicStoreStatistics != null)
                        {
                            await UpsertStatisticStateAsync(
                                context,
                                SalesStatisticType.StoreSales,
                                targetDate,
                                status.Status,
                                effectiveSourceWatermark,
                                status.ErrorMessage,
                                overwriteLastSourceUploadTime: true
                            );
                        }
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
                if (atomicStoreStatistics == null)
                {
                    try
                    {
                        await UpsertProductStatisticStateAsync(
                            context,
                            targetDate,
                            new ProductStatisticStatusResult(SalesStatisticRefreshStatus.Failed, ex.Message),
                            null
                        );
                    }
                    catch (Exception stateException)
                    {
                        // 失败状态无法持久化时仅记录附加错误，绝不能覆盖原始统计异常。
                        logger.LogError(stateException, "写入商品分店每日统计失败状态失败: {Date}", targetDate);
                    }
                }
                else
                {
                    // 2025 原子入口统一在外层独立事务成对写 Failed，避免内外层重复或单边落状态。
                    logger.LogDebug(ex, "2025 双表原子统计失败，交由外层持久化成对失败状态: {Date}", targetDate);
                }
                logger.LogError(ex, "更新商品分店每日统计失败: {Date}", targetDate);
                throw;
            }
            finally
            {
                context.Db.Ado.CommandTimeOut = originalCommandTimeout;
            }
        }

        private async Task Update2025StoreAndProductStatisticsAtomically(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            ILogger logger,
            DateTime date,
            DateTime? sourceWatermarkOverride = null,
            IReadOnlyList<ProductStoreDailySourceRow>? preloadedHBSalesRows = null,
            HBSales2025DailySnapshotSignature? expectedHBSalesSignature = null,
            bool deferHBSalesStabilityToBatchEnd = false,
            Posm2025DailySnapshot? preloadedPosmSnapshot = null
        )
        {
            var targetDate = date.Date;
            DateTime? effectiveSourceWatermark = null;
            try
            {
                var requiredHBSalesContext = hbSalesContext
                    ?? throw new InvalidOperationException("2025 年统计缺少 HBSalesRecord 上下文");
                // 先读取明细快照：分店/商品统计和 pre 水位必须基于同一批 HBSales 行，避免额外聚合扫描。
                var hbSalesRows = preloadedHBSalesRows?.ToList()
                    ?? await LoadHBSalesProductStoreDailyRowsAsync(
                        requiredHBSalesContext,
                        targetDate,
                        targetDate.AddDays(1)
                    );
                if (expectedHBSalesSignature != null)
                {
                    var actualSignature = CreateHBSales2025DailySnapshotSignature(
                        targetDate,
                        hbSalesRows
                    );
                    if (actualSignature != expectedHBSalesSignature)
                    {
                        throw new InvalidOperationException(
                            $"2025 批量预载 HBSales 签名不匹配，拒绝提交: {targetDate:yyyy-MM-dd}"
                        );
                    }
                }
                // 构建前先固定 POSM 与 HBSales 水位。批量路径只在日内复核 POSM，
                // HBSales 由批末的同范围签名一次复核，避免每天再扫描千万级详情表。
                var prePosmSourceWatermark = preloadedPosmSnapshot == null
                    ? await QueryDailyPosmSourceWatermarkAsync(posmContext, targetDate)
                    : GetPosmSnapshotWatermark(preloadedPosmSnapshot);
                var preSourceWatermark = GetLatestSourceTime(
                    prePosmSourceWatermark,
                    GetHBSalesSourceWatermark(hbSalesRows)
                );
                effectiveSourceWatermark = preSourceWatermark;
                if (sourceWatermarkOverride.HasValue
                    && preSourceWatermark != sourceWatermarkOverride)
                {
                    throw new InvalidOperationException(
                        $"2025 双表统计来源水位在调用后已变化，拒绝提交: {targetDate:yyyy-MM-dd}"
                    );
                }

                // 事务开始前完成两套来源读取和两张表计算，缩短主库锁定窗口。
                var storeBuildStopwatch = Stopwatch.StartNew();
                var storeStatistics = await BuildStoreStatisticsAsync(
                    context,
                    posmContext,
                    requiredHBSalesContext,
                    targetDate,
                    null,
                    hbSalesRows,
                    preloadedPosmSnapshot
                );
                logger.LogInformation("2025 Runner store build 完成: {Date}, {ElapsedMilliseconds}ms", targetDate, storeBuildStopwatch.ElapsedMilliseconds);
                var productBuildStopwatch = Stopwatch.StartNew();
                await UpdateProductStoreDailyStatisticsWithContext(
                    context,
                    posmContext,
                    requiredHBSalesContext,
                    logger,
                    targetDate,
                    storeStatistics,
                    effectiveSourceWatermark,
                    async () =>
                    {
                        var postSignatureStopwatch = Stopwatch.StartNew();
                        if (preloadedPosmSnapshot != null)
                        {
                            // 提交前只复核同日来源；四张表逐表比较，任一张漂移都拒绝写入 HBweb。
                            var postPosmSnapshot = await Load2025PosmDailySnapshotAsync(targetDate);
                            if (postPosmSnapshot.Signature != preloadedPosmSnapshot.Signature)
                            {
                                throw new InvalidOperationException(
                                    $"2025 Runner POSM 日快照签名发生变化，拒绝提交: {targetDate:yyyy-MM-dd}"
                                );
                            }
                        }
                        var postSourceWatermark = deferHBSalesStabilityToBatchEnd
                            // 批量路径把 HBSales 放到批末一次复核；日内仍必须独立复核 POSM。
                            ? await QueryDailyPosmSourceWatermarkAsync(posmContext, targetDate)
                            : await QueryDailySourceWatermarkAsync(
                                posmContext,
                                requiredHBSalesContext,
                                targetDate
                            );
                        var sourceWatermarkToCompare = deferHBSalesStabilityToBatchEnd
                            ? prePosmSourceWatermark
                            : preSourceWatermark;
                        if (postSourceWatermark != sourceWatermarkToCompare)
                        {
                            throw new InvalidOperationException(
                                $"2025 双表统计构建期间来源水位发生变化，拒绝提交: {targetDate:yyyy-MM-dd}"
                            );
                        }
                        logger.LogInformation("2025 Runner post signature 完成: {Date}, {ElapsedMilliseconds}ms", targetDate, postSignatureStopwatch.ElapsedMilliseconds);
                    },
                    hbSalesRows,
                    deferHBSalesStabilityToBatchEnd ? ProvisionalFreshStatus : null,
                    preloadedPosmSnapshot
                );
                logger.LogInformation("2025 Runner product build 完成: {Date}, {ElapsedMilliseconds}ms", targetDate, productBuildStopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                await Persist2025AtomicFailureStatesAsync(
                    context,
                    logger,
                    targetDate,
                    effectiveSourceWatermark,
                    ex
                );
                throw;
            }
        }

        private async Task Persist2025AtomicFailureStatesAsync(
            SqlSugarContext context,
            ILogger logger,
            DateTime targetDate,
            DateTime? effectiveSourceWatermark,
            Exception originalException
        )
        {
            try
            {
                // 主统计事务已回滚或尚未开始；此处独立事务保证两类 Failed 状态成对提交。
                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        await UpsertProductStatisticStateAsync(
                            context,
                            targetDate,
                            new ProductStatisticStatusResult(
                                SalesStatisticRefreshStatus.Failed,
                                originalException.Message
                            ),
                            effectiveSourceWatermark,
                            overwriteLastSourceUploadTime: true
                        );
                        await UpsertStatisticStateAsync(
                            context,
                            SalesStatisticType.StoreSales,
                            targetDate,
                            SalesStatisticRefreshStatus.Failed,
                            effectiveSourceWatermark,
                            originalException.Message,
                            overwriteLastSourceUploadTime: true
                        );
                    },
                    commitAsync: () => context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                    logger: logger,
                    operationName: "2025 双表统计失败状态写入"
                );
            }
            catch (Exception stateException)
            {
                // 状态持久化失败只补充日志；调用方必须仍收到最初的业务或事务异常。
                logger.LogError(
                    stateException,
                    "2025 双表统计失败状态写入失败，保留原始异常: {Date}, OriginalError={OriginalError}",
                    targetDate,
                    originalException.Message
                );
            }
        }

        private static string ResolveBranchCode(
            string? branchCode,
            string? deviceCode,
            Dictionary<string, string> deviceBranchMap
        )
        {
            if (!string.IsNullOrWhiteSpace(branchCode))
                return branchCode.Trim();

            var normalizedDeviceCode = NormalizeCode(deviceCode);
            if (!string.IsNullOrWhiteSpace(normalizedDeviceCode)
                && deviceBranchMap.TryGetValue(normalizedDeviceCode, out var mappedBranch))
                return mappedBranch?.Trim() ?? string.Empty;

            return string.Empty;
        }

        private static DateTime? GetPosmSnapshotWatermark(Posm2025DailySnapshot snapshot)
        {
            var values = snapshot.OrderRows.Select(row => row.LastUploadTime)
                .Concat(snapshot.DetailRows.Select(row => GetLatestSourceTime(row.OrderLastUploadTime, row.DetailLastUploadTime)))
                .Concat(snapshot.PaymentRows.Select(row => row.LastUploadTime))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();
            return values.Count == 0 ? null : values.Max();
        }

        private HBSalesRecordSqlSugarContext? GetHBSalesContextFor2025(DateTime date)
        {
            if (date.Year != 2025)
                return null;

            // 不允许 2025 在依赖未注册时悄悄退化为 POSM-only，避免全量回填遗漏 HBSales 数据。
            return _hbSalesContext
                ?? throw new InvalidOperationException("2025 年商品统计缺少 HBSalesRecord 上下文");
        }

        internal static async Task<List<StoreCostRow>> LoadStoreCostsInBatchesAsync(
            SqlSugarContext context,
            IReadOnlyCollection<string> productCodes,
            IReadOnlyCollection<string> branchCodes
        )
        {
            if (productCodes.Count == 0 || branchCodes.Count == 0)
                return new List<StoreCostRow>();

            var normalizedBranchCodes = branchCodes.Distinct(StringComparer.Ordinal).ToList();
            var rows = new List<StoreCostRow>();
            foreach (var productCodeBatch in productCodes
                .Distinct(StringComparer.Ordinal)
                .Chunk(StoreCostProductQueryBatchSize))
            {
                // 超大 IN 条件在 460 万行分店价格表上会导致 SQL Server 优化和并发超时；
                // 小批量查询继续命中现有 ProductCode + StoreCode 索引，统计口径保持不变。
                var batch = productCodeBatch.ToList();
                var batchRows = await context.Db.Queryable<StoreRetailPrice>()
                    .Where(p =>
                        p.ProductCode != null
                        && p.StoreCode != null
                        && batch.Contains(p.ProductCode)
                        && normalizedBranchCodes.Contains(p.StoreCode)
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
                    .ToListAsync();
                rows.AddRange(batchRows);
            }

            return rows;
        }

        private static async Task<List<ProductStoreDailySourceRow>> LoadHBSalesProductStoreDailyRowsAsync(
            HBSalesRecordSqlSugarContext hbSalesContext,
            DateTime targetDate,
            DateTime nextDate,
            int? maxRows = null
        )
        {
            var originalCommandTimeout = hbSalesContext.Db.Ado.CommandTimeOut;
            var mainCheckoutDateWindowStart = targetDate.AddDays(
                -HBSalesMainCheckoutDateWindowDays
            );
            var mainCheckoutDateWindowEnd = nextDate.AddDays(HBSalesMainCheckoutDateWindowDays);
            hbSalesContext.Db.Ado.CommandTimeOut = Math.Max(
                originalCommandTimeout,
                CommandTimeoutSeconds
            );
            List<ProductStoreDailySourceRow> rows;
            try
            {
                var query = hbSalesContext.Db.Queryable<SalesOrderMain>()
                    .LeftJoin<SalesOrderDetailRecord>((main, detail) =>
                        main.B销售单号 == detail.B销售单号
                    )
                    .Where((main, detail) =>
                        detail.B结账日期.HasValue
                        && detail.B结账日期.Value >= targetDate
                        && detail.B结账日期.Value < nextDate
                        && main.B结账日期.HasValue
                        && main.B结账日期.Value >= mainCheckoutDateWindowStart
                        && main.B结账日期.Value < mainCheckoutDateWindowEnd
                        &&
                        // SQL 的 != 会丢掉 NULL；年度口径是只排除类型 2。
                        (main.B单据类型 == null || main.B单据类型.Trim() != "2")
                    )
                    .Select((main, detail) => new ProductStoreDailySourceRow
                    {
                        IsHBSalesSource = true,
                        Date = detail.B结账日期!.Value.Date,
                        // 前缀避免与 POSM 的订单号偶然相同而误合并订单数。
                        OrderGuid = "HBSALES:" + main.B销售单号,
                        // 分店统计的订单数必须保留源单号的 null 语义，不能从带前缀的显示键反推。
                        HBSalesOrderNumber = main.B销售单号,
                        DetailGuid = $"HBSALES:{detail.ID}",
                        // 与既有 2025 年度统计保持一致：分店以明细记录为准，不从主表补写。
                        BranchCode = detail.B分店代码,
                        ProductCode = detail.B产品编号,
                        ItemNumber = detail.B货号,
                        SupplierCode = detail.B供应商ID,
                        ProductName = detail.B商品名,
                        Barcode = detail.B条形码,
                        Quantity = detail.B数量 ?? 0m,
                        ActualAmount = detail.B合计金额 ?? 0m,
                        // HBSales 使用明细/主表的最后修改时间；创建时间仅作为旧记录的可靠回退。
                        // 同时保留四个原始时间，供 pre 水位按字段独立 MAX，不能由回退值反推。
                        HBSalesMainLastModifiedAt = main.FGC_LastModifyDate,
                        HBSalesMainCreatedAt = main.FGC_CreateDate,
                        HBSalesDetailLastModifiedAt = detail.FGC_LastModifyDate,
                        HBSalesDetailCreatedAt = detail.FGC_CreateDate,
                        OrderLastUploadTime = main.FGC_LastModifyDate ?? main.FGC_CreateDate,
                        DetailLastUploadTime = detail.FGC_LastModifyDate ?? detail.FGC_CreateDate,
                        DocumentType = main.B单据类型,
                    });
                rows = maxRows.HasValue
                    ? await query.Take(maxRows.Value + 1).ToListAsync()
                    : await query.ToListAsync();
            }
            finally
            {
                // 共享上下文可能被后续查询复用，必须还原调用方原有超时。
                hbSalesContext.Db.Ado.CommandTimeOut = originalCommandTimeout;
            }

            if (maxRows.HasValue && rows.Count > maxRows.Value)
            {
                throw new InvalidOperationException(
                    $"2025 HBSales 批量快照超过 {maxRows.Value:N0} 行内存保护上限，请缩小日期范围"
                );
            }

            foreach (var row in rows.Where(row =>
                NormalizeCode(row.DocumentType) == "3" || NormalizeCode(row.DocumentType) == "4"
            ))
            {
                // HBSales 年度统计口径：类型 3/4 为退货/退款，数量和金额统一取反。
                row.Quantity = -row.Quantity;
                row.ActualAmount = -row.ActualAmount;
            }

            return rows;
        }

        private static HBSales2025DailySnapshotSignature CreateHBSales2025DailySnapshotSignature(
            DateTime date,
            IEnumerable<ProductStoreDailySourceRow> rows
        )
        {
            var dayRows = rows.Where(row => row.Date.Date == date.Date).ToList();
            var mainLastModifiedAt = dayRows.Max(row => row.HBSalesMainLastModifiedAt);
            var mainCreatedAt = dayRows.Max(row => row.HBSalesMainCreatedAt);
            var detailLastModifiedAt = dayRows.Max(row => row.HBSalesDetailLastModifiedAt);
            var detailCreatedAt = dayRows.Max(row => row.HBSalesDetailCreatedAt);
            using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var row in dayRows.OrderBy(row => row.HBSalesOrderNumber, StringComparer.Ordinal)
                         .ThenBy(row => row.DetailGuid, StringComparer.Ordinal)
                         .ThenBy(row => row.BranchCode, StringComparer.Ordinal)
                         .ThenBy(row => row.ProductCode, StringComparer.Ordinal))
            {
                // 长度前缀和固定字段顺序让 checksum 与 SQL 返回顺序无关，并区分 null、空串及边界拼接。
                AppendHBSalesSignatureValue(checksum, row.HBSalesOrderNumber);
                AppendHBSalesSignatureValue(checksum, row.DetailGuid);
                AppendHBSalesSignatureValue(checksum, row.Date);
                AppendHBSalesSignatureValue(checksum, row.BranchCode);
                AppendHBSalesSignatureValue(checksum, row.ProductCode);
                AppendHBSalesSignatureValue(checksum, row.ItemNumber);
                AppendHBSalesSignatureValue(checksum, row.Barcode);
                AppendHBSalesSignatureValue(checksum, row.SupplierCode);
                AppendHBSalesSignatureValue(checksum, row.ProductName);
                AppendHBSalesSignatureValue(checksum, row.Quantity);
                AppendHBSalesSignatureValue(checksum, row.ActualAmount);
                AppendHBSalesSignatureValue(checksum, row.DocumentType);
                AppendHBSalesSignatureValue(checksum, row.HBSalesMainLastModifiedAt);
                AppendHBSalesSignatureValue(checksum, row.HBSalesMainCreatedAt);
                AppendHBSalesSignatureValue(checksum, row.HBSalesDetailLastModifiedAt);
                AppendHBSalesSignatureValue(checksum, row.HBSalesDetailCreatedAt);
            }
            return new HBSales2025DailySnapshotSignature(
                date.Date,
                dayRows.Count,
                mainLastModifiedAt,
                mainCreatedAt,
                detailLastModifiedAt,
                detailCreatedAt,
                Convert.ToHexString(checksum.GetHashAndReset())
            );
        }

        private static void AppendHBSalesSignatureValue(IncrementalHash checksum, object? value)
        {
            var text = value switch
            {
                null => "<null>",
                DateTime dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                decimal decimalValue => decimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            };
            var bytes = Encoding.UTF8.GetBytes(text);
            checksum.AppendData(BitConverter.GetBytes(bytes.Length));
            checksum.AppendData(bytes);
        }

        private static Posm2025DailySnapshotSignature CreatePosm2025DailySnapshotSignature(
            DateTime date,
            IEnumerable<StoreStatisticOrderRow> orders,
            IEnumerable<ProductStoreDailySourceRow> details,
            IEnumerable<StoreStatisticPaymentRow> payments,
            IEnumerable<ProductStoreDailySourceRow> salesReturns
        )
        {
            // 每张表独立签名，才能识别某张表单独新增、删除或只改金额/时间的情况。
            var orderRows = orders.OrderBy(row => row.OrderGuid, StringComparer.Ordinal).ToList();
            var detailRows = details.OrderBy(row => row.DetailGuid, StringComparer.Ordinal).ToList();
            var paymentRows = payments.OrderBy(row => row.PaymentGuid, StringComparer.Ordinal).ToList();
            var returnRows = salesReturns.OrderBy(row => row.DetailGuid, StringComparer.Ordinal).ToList();
            return new Posm2025DailySnapshotSignature(
                date.Date,
                CreatePosmTableSignature(orderRows, row => row.UpdatedAt, row => row.CreatedAt, row =>
                    [row.OrderGuid, row.OrderTime, row.BranchCode, row.DeviceCode, row.Status, row.LastUploadTime, row.CreatedAt, row.UpdatedAt]),
                CreatePosmTableSignature(detailRows, row => row.SourceUpdatedAt, row => row.SourceCreatedAt, row =>
                    [row.OrderGuid, row.DetailGuid, row.ProductCode, row.SupplierCode, row.ProductName, row.Barcode, row.Quantity, row.ActualAmount, row.DetailLastUploadTime, row.SourceCreatedAt, row.SourceUpdatedAt]),
                CreatePosmTableSignature(paymentRows, row => row.UpdatedAt, row => row.CreatedAt, row =>
                    [row.PaymentGuid, row.OrderGuid, row.Amount, row.LastUploadTime, row.CreatedAt, row.UpdatedAt]),
                CreatePosmTableSignature(returnRows, row => row.SourceUpdatedAt, row => row.SourceCreatedAt, row =>
                    [row.OrderGuid, row.DetailGuid, row.ProductCode, row.Quantity, row.ActualAmount, row.SourceCreatedAt, row.SourceUpdatedAt])
            );
        }

        private static Posm2025DailyTableSignature CreatePosmTableSignature<T>(
            IReadOnlyList<T> rows,
            Func<T, DateTime?> lastModifiedSelector,
            Func<T, DateTime?> createdSelector,
            Func<T, object?[]> valuesSelector
        )
        {
            using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var row in rows)
            {
                foreach (var value in valuesSelector(row))
                    AppendHBSalesSignatureValue(checksum, value);
            }
            return new Posm2025DailyTableSignature(
                rows.Count,
                rows.Max(lastModifiedSelector),
                rows.Max(createdSelector),
                Convert.ToHexString(checksum.GetHashAndReset())
            );
        }

        private static async Task ResolveMissingHBSalesProductCodesAsync(
            SqlSugarContext context,
            IReadOnlyList<ProductStoreDailySourceRow> hbSalesRows
        )
        {
            var rowsToResolve = hbSalesRows
                .Where(row => string.IsNullOrWhiteSpace(row.ProductCode))
                .Where(row =>
                    !string.IsNullOrWhiteSpace(NormalizeCode(row.ItemNumber))
                    || !string.IsNullOrWhiteSpace(NormalizeCode(row.Barcode))
                )
                .ToList();
            if (!rowsToResolve.Any())
            {
                return;
            }

            var globalLookupCodes = rowsToResolve
                .SelectMany(row => new[] { NormalizeCode(row.ItemNumber), NormalizeCode(row.Barcode) })
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var barcodeLookupCodes = rowsToResolve
                .Select(row => NormalizeCode(row.Barcode))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var globalCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var branchCandidates = new Dictionary<(string StoreCode, string Barcode), HashSet<string>>();
            var crossStoreCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void AddGlobalCandidate(string? lookupCode, string? productCode)
            {
                var normalizedLookupCode = NormalizeCode(lookupCode);
                var normalizedProductCode = NormalizeCode(productCode);
                if (string.IsNullOrWhiteSpace(normalizedLookupCode)
                    || string.IsNullOrWhiteSpace(normalizedProductCode))
                {
                    return;
                }

                if (!globalCandidates.TryGetValue(normalizedLookupCode, out var candidates))
                {
                    candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    globalCandidates[normalizedLookupCode] = candidates;
                }
                candidates.Add(normalizedProductCode);
            }

            void AddStoreCandidate(string? storeCode, string? lookupCode, string? productCode)
            {
                var normalizedStoreCode = NormalizeCode(storeCode);
                var normalizedLookupCode = NormalizeCode(lookupCode);
                var normalizedProductCode = NormalizeCode(productCode);
                if (string.IsNullOrWhiteSpace(normalizedStoreCode)
                    || string.IsNullOrWhiteSpace(normalizedLookupCode)
                    || string.IsNullOrWhiteSpace(normalizedProductCode))
                {
                    return;
                }

                var branchKey = (
                    normalizedStoreCode.ToUpperInvariant(),
                    normalizedLookupCode.ToUpperInvariant()
                );
                if (!branchCandidates.TryGetValue(branchKey, out var candidates))
                {
                    candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    branchCandidates[branchKey] = candidates;
                }
                candidates.Add(normalizedProductCode);

                if (!crossStoreCandidates.TryGetValue(normalizedLookupCode, out var crossStore))
                {
                    crossStore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    crossStoreCandidates[normalizedLookupCode] = crossStore;
                }
                crossStore.Add(normalizedProductCode);
            }

            foreach (var lookupCodeBatch in globalLookupCodes.Chunk(StoreCostProductQueryBatchSize))
            {
                // 只查询当前缺码行携带的货号/条码，分批限制 IN 条件大小，避免不受控参数量。
                var batch = lookupCodeBatch.ToList();
                var productRows = await context.Db.Queryable<Product>()
                    .Where(product =>
                        product.IsDeleted == false
                        && product.IsActive
                        && product.ProductCode != null
                        && (
                            (product.ItemNumber != null && batch.Contains(product.ItemNumber))
                            || (product.Barcode != null && batch.Contains(product.Barcode))
                        )
                    )
                    .Select(product => new { product.ItemNumber, product.Barcode, product.ProductCode })
                    .ToListAsync();
                foreach (var product in productRows)
                {
                    AddGlobalCandidate(product.ItemNumber, product.ProductCode);
                    AddGlobalCandidate(product.Barcode, product.ProductCode);
                }
            }

            foreach (var barcodeBatch in barcodeLookupCodes.Chunk(StoreCostProductQueryBatchSize))
            {
                // ProductSetCode 和 StoreMultiCodeProduct 都以条码为强键，后者还必须匹配分店。
                var batch = barcodeBatch.ToList();
                var productSetCodeRows = await context.Db.Queryable<ProductSetCode>()
                    .Where(setCode =>
                        setCode.IsDeleted == false
                        && setCode.IsActive
                        && setCode.ProductCode != null
                        && setCode.SetBarcode != null
                        && batch.Contains(setCode.SetBarcode)
                    )
                    .Select(setCode => new { setCode.SetBarcode, setCode.ProductCode })
                    .ToListAsync();
                foreach (var setCode in productSetCodeRows)
                {
                    AddGlobalCandidate(setCode.SetBarcode, setCode.ProductCode);
                }

                var storeMultiCodeRows = await context.Db.Queryable<StoreMultiCodeProduct>()
                    .Where(multiCode =>
                        multiCode.IsDeleted == false
                        && multiCode.IsActive
                        && multiCode.StoreCode != null
                        && multiCode.ProductCode != null
                        && multiCode.MultiBarcode != null
                        && batch.Contains(multiCode.MultiBarcode)
                    )
                    .Select(multiCode => new
                    {
                        multiCode.StoreCode,
                        multiCode.MultiBarcode,
                        multiCode.ProductCode,
                    })
                    .ToListAsync();
                foreach (var multiCode in storeMultiCodeRows)
                {
                    AddStoreCandidate(multiCode.StoreCode, multiCode.MultiBarcode, multiCode.ProductCode);
                }
            }

            foreach (var row in rowsToResolve)
            {
                var barcode = NormalizeCode(row.Barcode);
                var branchKey = (
                    NormalizeCode(row.BranchCode).ToUpperInvariant(),
                    barcode.ToUpperInvariant()
                );
                if (!string.IsNullOrWhiteSpace(barcode)
                    && branchCandidates.TryGetValue(branchKey, out var branch))
                {
                    // 第一层命中后不可再被全局候选推翻；多候选直接保留为空，不能降级。
                    if (branch.Count == 1)
                    {
                        row.ProductCode = SelectDeterministicProductCode(branch);
                    }
                    continue;
                }

                var global = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var lookupCode in new[]
                    { NormalizeCode(row.ItemNumber), NormalizeCode(row.Barcode) }
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (globalCandidates.TryGetValue(lookupCode, out var candidates))
                    {
                        global.UnionWith(candidates);
                    }
                }

                // 第二层将 Product 的货号/条码候选与 ProductSetCode 条码候选合并；冲突同样不能降级。
                if (global.Count == 1)
                {
                    row.ProductCode = SelectDeterministicProductCode(global);
                    continue;
                }
                if (global.Count > 1 || string.IsNullOrWhiteSpace(barcode))
                {
                    continue;
                }

                // 仅前两层均无候选时，才允许跨分店多码条码回退；多候选保持空并由原子路径失败。
                if (crossStoreCandidates.TryGetValue(barcode, out var crossStore)
                    && crossStore.Count == 1)
                {
                    row.ProductCode = SelectDeterministicProductCode(crossStore);
                }
            }
        }

        private static string SelectDeterministicProductCode(IEnumerable<string> candidates)
        {
            return candidates
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(code => code, StringComparer.Ordinal)
                .First();
        }

        private static DateTime? GetLatestSourceTime(params DateTime?[] timestamps)
        {
            var values = timestamps.Where(timestamp => timestamp.HasValue)
                .Select(timestamp => timestamp!.Value)
                .ToList();
            return values.Count == 0 ? null : values.Max();
        }

        private static DateTime? GetLatestSourceTime(IEnumerable<ProductStoreDailySourceRow> rows)
        {
            return GetLatestSourceTime(rows
                .SelectMany(row => new[] { row.OrderLastUploadTime, row.DetailLastUploadTime })
                .ToArray());
        }

        private static DateTime? GetHBSalesSourceWatermark(
            IEnumerable<ProductStoreDailySourceRow> hbSalesRows
        )
        {
            // 必须分别计算四列的 MAX：LastModify 有值时会遮蔽 Create，不能使用已 coalesce 的统计行时间。
            return GetLatestSourceTime(
                GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesMainLastModifiedAt).ToArray()),
                GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesMainCreatedAt).ToArray()),
                GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesDetailLastModifiedAt).ToArray()),
                GetLatestSourceTime(hbSalesRows.Select(row => row.HBSalesDetailCreatedAt).ToArray())
            );
        }

        private static async Task<List<HBSalesStoreAggregateRow>> LoadHBSalesStoreAggregatesAsync(
            HBSalesRecordSqlSugarContext hbSalesContext,
            DateTime targetDate,
            DateTime nextDate
        )
        {
            var originalCommandTimeout = hbSalesContext.Db.Ado.CommandTimeOut;
            var mainCheckoutDateWindowStart = targetDate.AddDays(
                -HBSalesMainCheckoutDateWindowDays
            );
            var mainCheckoutDateWindowEnd = nextDate.AddDays(HBSalesMainCheckoutDateWindowDays);
            hbSalesContext.Db.Ado.CommandTimeOut = Math.Max(
                originalCommandTimeout,
                CommandTimeoutSeconds
            );
            try
            {
                return await hbSalesContext.Db.Queryable<SalesOrderMain>()
                    .LeftJoin<SalesOrderDetailRecord>((main, detail) =>
                        main.B销售单号 == detail.B销售单号
                    )
                    .Where((main, detail) =>
                        detail.B结账日期.HasValue
                        && detail.B结账日期.Value >= targetDate
                        && detail.B结账日期.Value < nextDate
                        && main.B结账日期.HasValue
                        && main.B结账日期.Value >= mainCheckoutDateWindowStart
                        && main.B结账日期.Value < mainCheckoutDateWindowEnd
                        && (main.B单据类型 == null || main.B单据类型.Trim() != "2")
                        && detail.B分店代码 != null
                        && detail.B分店代码.Trim() != ""
                    )
                    .GroupBy((main, detail) => detail.B分店代码!.Trim())
                    .Select((main, detail) => new HBSalesStoreAggregateRow
                    {
                        BranchCode = detail.B分店代码!.Trim(),
                        TotalAmount = SqlFunc.AggregateSum(
                            (detail.B合计金额 ?? 0m) * SqlFunc.IIF(
                                main.B单据类型 != null
                                    && (main.B单据类型.Trim() == "3" || main.B单据类型.Trim() == "4"),
                                -1m,
                                1m
                            )
                        ),
                        TotalQuantity = SqlFunc.AggregateSum(
                            (detail.B数量 ?? 0m) * SqlFunc.IIF(
                                main.B单据类型 != null
                                    && (main.B单据类型.Trim() == "3" || main.B单据类型.Trim() == "4"),
                                -1m,
                                1m
                            )
                        ),
                        OrderCount = SqlFunc.AggregateDistinctCount(main.B销售单号),
                    })
                    .ToListAsync();
            }
            finally
            {
                hbSalesContext.Db.Ado.CommandTimeOut = originalCommandTimeout;
            }
        }

        private static List<HBSalesStoreAggregateRow> BuildHBSalesStoreAggregates(
            IReadOnlyList<ProductStoreDailySourceRow> hbSalesRows
        )
        {
            // 原子刷新从同一份已过滤且已反向的明细派生分店汇总，避免重复扫描 HBSales。
            return hbSalesRows
                .Where(row => row.IsHBSalesSource)
                .Select(row => new
                {
                    Row = row,
                    BranchCode = NormalizeCode(row.BranchCode),
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.BranchCode))
                .GroupBy(row => row.BranchCode)
                .Select(group => new HBSalesStoreAggregateRow
                {
                    BranchCode = group.Key,
                    TotalAmount = group.Sum(row => row.Row.ActualAmount),
                    TotalQuantity = group.Sum(row => row.Row.Quantity),
                    // SQL DISTINCT COUNT 不计 NULL；空字符串仍是一个有效销售单号。
                    OrderCount = group
                        .Select(row => row.Row.HBSalesOrderNumber)
                        .Where(orderNumber => orderNumber != null)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                })
                .ToList();
        }

        internal async Task<IReadOnlyList<ProductStoreDailyBranchRollup>> GetProductStoreDailyReturnAdjustmentsAsync(
            DateTime date
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);
            var detailRows = await _posmContext.Db.Queryable<SalesOrder>()
                .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                .Where((o, d) =>
                    o.Status != null
                    && (o.Status == 1 || o.Status == 4)
                    && o.OrderTime != null
                    && o.OrderTime >= targetDate
                    && o.OrderTime < nextDate
                )
                .Select((o, d) => new { d.OrderDetailGuid })
                .ToListAsync();
            var detailGuidSet = detailRows
                .Select(x => x.OrderDetailGuid)
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Select(guid => guid!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var supplementalReturnRows = await LoadSupplementalReturnRowsAsync(
                _posmContext,
                targetDate,
                nextDate,
                detailGuidSet
            );
            var deviceBranchMap = await LoadDeviceBranchMapAsync(
                _posmContext,
                supplementalReturnRows.Select(row => row.DeviceCode)
            );

            return supplementalReturnRows
                .Select(row => new
                {
                    Row = row,
                    BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                })
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.BranchCode)
                    && !string.IsNullOrWhiteSpace(x.Row.ProductCode)
                )
                .GroupBy(x => x.BranchCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ProductStoreDailyBranchRollup(
                    group.Key,
                    group.Sum(x => x.Row.ActualAmount),
                    (int)group.Sum(x => x.Row.Quantity)
                ))
                .ToList();
        }

        private static async Task<List<ProductStoreDailySourceRow>> LoadSupplementalReturnRowsAsync(
            POSMSqlSugarContext posmContext,
            DateTime targetDate,
            DateTime nextDate,
            HashSet<string> detailGuidSet
        )
        {
            var returnTableName = posmContext.Db.EntityMaintenance.GetTableName(typeof(SalesReturnRecord));
            var hasReturnTable = posmContext.Db.DbMaintenance.GetTableInfoList(false)
                .Any(table => string.Equals(table.Name, returnTableName, StringComparison.OrdinalIgnoreCase));
            // 旧 POSM 库可能没有新退货表；缺表时只按明细表里的负数退货统计。
            if (!hasReturnTable)
                return new List<ProductStoreDailySourceRow>();

            var returnRows = await posmContext.Db.Queryable<SalesReturnRecord>()
                .LeftJoin<SalesOrder>((r, o) => r.ReturnOrderGuid == o.OrderGuid)
                .LeftJoin<SalesOrderDetail>((r, o, d) => r.OriginalOrderDetailGuid == d.OrderDetailGuid)
                .Where((r, o, d) =>
                    o.Status != null
                    && (o.Status == 1 || o.Status == 4)
                    && o.OrderTime != null
                    && o.OrderTime >= targetDate
                    && o.OrderTime < nextDate
                )
                .Select((r, o, d) => new
                {
                    r.ReturnDetailGuid,
                    ReturnProductCode = r.ProductCode,
                    ReturnQuantity = r.ReturnQuantity,
                    ReturnAmount = r.ReturnAmount,
                    ReturnCreatedTime = r.CreatedTime,
                    ReturnUpdatedTime = r.UpdatedTime,
                    o.OrderGuid,
                    o.BranchCode,
                    o.DeviceCode,
                    o.OrderTime,
                    OrderLastUploadTime = o.LastUploadTime,
                    DetailProductCode = d.ProductCode,
                    d.SupplierCode,
                    d.ProductName,
                    d.Barcode,
                })
                .ToListAsync();

            // 新系统退货只落 sales_return_record；若同一明细已在明细表中，跳过避免重复冲减。
            return returnRows
                .Where(x =>
                    string.IsNullOrWhiteSpace(x.ReturnDetailGuid)
                    || !detailGuidSet.Contains(x.ReturnDetailGuid)
                )
                .Select(x => new ProductStoreDailySourceRow
                {
                    Date = x.OrderTime!.Value.Date,
                    OrderGuid = x.OrderGuid,
                    DetailGuid = x.ReturnDetailGuid,
                    BranchCode = x.BranchCode,
                    DeviceCode = x.DeviceCode,
                    OrderLastUploadTime = x.OrderLastUploadTime,
                    ProductCode = string.IsNullOrWhiteSpace(x.ReturnProductCode)
                        ? x.DetailProductCode
                        : x.ReturnProductCode,
                    SupplierCode = x.SupplierCode,
                    ProductName = x.ProductName,
                    Barcode = x.Barcode,
                    Quantity = -Math.Abs(x.ReturnQuantity ?? 0m),
                    ActualAmount = -Math.Abs(x.ReturnAmount ?? 0m),
                    DetailLastUploadTime = x.ReturnUpdatedTime ?? x.ReturnCreatedTime,
                    SourceCreatedAt = x.ReturnCreatedTime,
                    SourceUpdatedAt = x.ReturnUpdatedTime,
                })
                .ToList();
        }

        private static string NormalizeCode(string? code)
        {
            return code?.Trim() ?? string.Empty;
        }

        private static string ResolveStatisticSupplierCode(string? mappedSupplierCode, string? detailSupplierCode)
        {
            // 统计主键不允许空供应商：先用映射供应商，再回退 POSM 明细供应商，最后统一归入 UNKNOWN。
            var supplierCode = NormalizeCode(mappedSupplierCode);
            if (!string.IsNullOrWhiteSpace(supplierCode))
                return supplierCode;

            supplierCode = NormalizeCode(detailSupplierCode);
            return !string.IsNullOrWhiteSpace(supplierCode) ? supplierCode : UnknownSupplierCode;
        }

        private static async Task<Dictionary<string, string>> LoadDeviceBranchMapAsync(
            POSMSqlSugarContext posmContext,
            IEnumerable<string?> deviceCodes
        )
        {
            var targetDeviceCodes = deviceCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (!targetDeviceCodes.Any())
                return new Dictionary<string, string>();

            return (await posmContext.Db.Queryable<POSM_设备注册信息表>()
                    .Where(device => targetDeviceCodes.Contains(device.系统设备编号))
                    .Select(device => new { device.系统设备编号, device.分店代码 })
                    .ToListAsync())
                .Where(device => !string.IsNullOrWhiteSpace(device.系统设备编号))
                .GroupBy(device => device.系统设备编号.Trim())
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(device => device.分店代码)
                        .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code))?.Trim() ?? string.Empty
                );
        }

        private static async Task<(
            Dictionary<string, decimal> PaymentAmounts,
            Dictionary<string, decimal> DetailAmounts
        )> LoadOrderAmountMapsAsync<T>(
            POSMSqlSugarContext posmContext,
            DateTime startDate,
            DateTime endExclusive,
            IEnumerable<T> detailRows,
            Func<T, string?> orderGuidSelector,
            Func<T, decimal> detailAmountSelector
        )
        {
            var paymentRows = await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                    (payment, order) => payment.OrderGuid == order.OrderGuid
                )
                .Where((payment, order) =>
                    order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= startDate
                    && order.OrderTime < endExclusive
                )
                .GroupBy((payment, order) => payment.OrderGuid)
                .Select((payment, order) => new OrderAmountRow
                {
                    OrderGuid = payment.OrderGuid,
                    Amount = SqlFunc.AggregateSum(payment.Amount) ?? 0m,
                })
                .ToListAsync();

            // 明细合计直接复用当前统计已读取的 POSM 明细，避免对 sales_order_detail 再做一次大范围 group by。
            var detailAmountRows = detailRows.Select(row => new OrderAmountRow
            {
                OrderGuid = orderGuidSelector(row),
                Amount = detailAmountSelector(row),
            });

            return (BuildOrderAmountMap(paymentRows), BuildOrderAmountMap(detailAmountRows));
        }

        private static Dictionary<string, decimal> BuildOrderAmountMap(IEnumerable<OrderAmountRow> rows)
        {
            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.OrderGuid))
                .GroupBy(row => row.OrderGuid!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(row => row.Amount),
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private static decimal ResolveStatisticAmount(
            string? orderGuid,
            decimal detailAmount,
            Dictionary<string, decimal> paymentAmounts,
            Dictionary<string, decimal> detailAmounts
        )
        {
            var key = NormalizeCode(orderGuid);
            if (string.IsNullOrWhiteSpace(key)
                || !paymentAmounts.TryGetValue(key, out var paymentAmount)
                || !detailAmounts.TryGetValue(key, out var detailTotal)
                || detailTotal == 0m)
                // 无支付记录时必须按支付口径计 0，不能静默回退明细金额掩盖对账异常。
                return 0m;

            // 统计金额按订单支付金额分摊，保证供应商/商品类汇总能回到分店支付总账。
            return paymentAmount * detailAmount / detailTotal;
        }

        private async Task<List<StoreSalesStatistic>> BuildStoreStatisticsAsync(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            DateTime date,
            List<string>? branchCodes,
            IReadOnlyList<ProductStoreDailySourceRow>? preloadedHBSalesRows = null,
            Posm2025DailySnapshot? preloadedPosmSnapshot = null
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);
            var targetBranchCodes = NormalizeBranchCodes(branchCodes);

            var paymentRows = preloadedPosmSnapshot?.PaymentRows.ToList() ?? await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
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

            var quantityRows = preloadedPosmSnapshot?.DetailRows
                .Select(row => new StoreStatisticQuantityRow
                {
                    OrderGuid = row.OrderGuid,
                    BranchCode = row.BranchCode,
                    DeviceCode = row.DeviceCode,
                    Quantity = (int)row.Quantity,
                })
                .ToList()
                ?? await posmContext.Db.Queryable<SalesOrderDetail, SalesOrder>(
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

            var orderRows = preloadedPosmSnapshot?.OrderRows.ToList() ?? await posmContext.Db.Queryable<SalesOrder>()
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
            var deviceBranchMap = preloadedPosmSnapshot?.DeviceBranchMap ?? (deviceCodes.Any()
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
                : new Dictionary<string, string>());

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
                .ToDictionary(group => group.Key, group => (decimal)group.Sum(row => row.Quantity));

            var orderCountByBranch = resolvedOrderRows
                .Where(row => IsTargetBranch(row.BranchCode) && !string.IsNullOrWhiteSpace(row.OrderGuid))
                .GroupBy(row => row.BranchCode)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.OrderGuid).Distinct().Count()
                );

            if (targetDate.Year == 2025)
            {
                var hbSalesAggregates = preloadedHBSalesRows != null
                    ? BuildHBSalesStoreAggregates(preloadedHBSalesRows)
                    : await LoadHBSalesStoreAggregatesAsync(
                        hbSalesContext
                            ?? throw new InvalidOperationException("2025 年分店统计缺少 HBSalesRecord 上下文"),
                        targetDate,
                        nextDate
                    );
                var resolvedHBSalesRows = hbSalesAggregates
                    .Select(row => new
                    {
                        BranchCode = NormalizeCode(row.BranchCode),
                        row.TotalQuantity,
                        row.TotalAmount,
                        row.OrderCount,
                    })
                    .Where(row => IsTargetBranch(row.BranchCode))
                    .ToList();

                foreach (var group in resolvedHBSalesRows.GroupBy(row => row.BranchCode))
                {
                    amountByBranch[group.Key] = amountByBranch.GetValueOrDefault(group.Key)
                        + group.Sum(row => row.TotalAmount);
                    quantityByBranch[group.Key] = quantityByBranch.GetValueOrDefault(group.Key)
                        + group.Sum(row => row.TotalQuantity);
                    orderCountByBranch[group.Key] = orderCountByBranch.GetValueOrDefault(group.Key)
                        + group.Sum(row => row.OrderCount);
                }
            }

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
                        TotalQuantity = (int)quantityByBranch.GetValueOrDefault(branchCode),
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

        private static List<string> NormalizeSupplierCodes(List<string>? supplierCodes)
        {
            return supplierCodes?
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
            DateTime? lastSourceUploadTime,
            IReadOnlyList<ProductStoreDailyBranchRollup> supplementalReturnAdjustments,
            IReadOnlyList<StoreSalesStatistic>? atomicStoreStatistics = null
        )
        {
            var existingStoreSales = atomicStoreStatistics == null
                ? await context.Db.Queryable<StoreSalesStatistic>()
                    .Where(s => s.Date == targetDate)
                    .Select(s => new { s.BranchCode, s.TotalAmount, s.TotalQuantity })
                    .ToListAsync()
                : atomicStoreStatistics.Select(s => new
                {
                    s.BranchCode,
                    s.TotalAmount,
                    s.TotalQuantity,
                }).ToList();

            var storeRollups = existingStoreSales
                .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
                .Select(x => new ProductStoreDailyBranchRollup(
                    x.BranchCode!,
                    x.TotalAmount,
                    x.TotalQuantity
                ))
                .ToList();
            var effectiveStoreRollups = ProductStoreDailyReconciliationCalculator
                .ApplyExistingStoreAdjustments(storeRollups, supplementalReturnAdjustments);

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
                effectiveStoreRollups,
                branchDiagnostics
            );

            return new ProductStatisticStatusResult(reconciliation.Status, reconciliation.ErrorMessage);
        }

        private async Task UpsertProductStatisticStateAsync(
            SqlSugarContext context,
            DateTime targetDate,
            ProductStatisticStatusResult status,
            DateTime? lastSourceUploadTime,
            bool overwriteLastSourceUploadTime = false
        )
        {
            var nextDate = targetDate.Date.AddDays(1);
            var existing = await context.Db.Queryable<SalesStatisticRefreshState>()
                // SQLite/SQL Server 的 DateTime 精度不同，按规范化日期范围读取已有状态。
                .Where(s =>
                    s.StatisticType == SalesStatisticType.ProductStoreDaily
                    && s.Date >= targetDate.Date
                    && s.Date < nextDate
                )
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
                    || status.Status == ProvisionalFreshStatus
                    || status.Status == SalesStatisticRefreshStatus.Failed
                )
                {
                    existing.CompletedAtUtc = DateTime.UtcNow;
                }
                await context.Db.Insertable(existing).ExecuteCommandAsync();
                return;
            }

            existing.Status = status.Status;
            existing.LastSourceUploadTime = overwriteLastSourceUploadTime
                ? lastSourceUploadTime
                : lastSourceUploadTime ?? existing.LastSourceUploadTime;
            existing.SourceTimeZone = "POSM_LOCAL";
            existing.LastAggregatedAtUtc = DateTime.UtcNow;
            existing.LastCheckedAtUtc = DateTime.UtcNow;
            existing.ErrorMessage = status.ErrorMessage;
            if (
                status.Status == SalesStatisticRefreshStatus.Fresh
                || status.Status == ProvisionalFreshStatus
                || status.Status == SalesStatisticRefreshStatus.Failed
            )
            {
                existing.CompletedAtUtc = DateTime.UtcNow;
            }
            await context.Db.Updateable(existing).ExecuteCommandAsync();
        }

        private static async Task UpsertStatisticStateAsync(
            SqlSugarContext context,
            string statisticType,
            DateTime targetDate,
            string status,
            DateTime? lastSourceUploadTime,
            string? errorMessage,
            bool overwriteLastSourceUploadTime = false
        )
        {
            var now = DateTime.UtcNow;
            var nextDate = targetDate.Date.AddDays(1);
            var existing = await context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s =>
                    s.StatisticType == statisticType
                    && s.Date >= targetDate.Date
                    && s.Date < nextDate
                )
                .FirstAsync();
            var isNew = existing == null;

            if (isNew)
            {
                existing = new SalesStatisticRefreshState
                {
                    StatisticType = statisticType,
                    Date = targetDate.Date,
                    SourceTimeZone = "POSM_LOCAL",
                };
            }
            var state = existing!;

            state.Status = status;
            state.LastSourceUploadTime = overwriteLastSourceUploadTime
                ? lastSourceUploadTime
                : lastSourceUploadTime ?? state.LastSourceUploadTime;
            state.LastCheckedAtUtc = now;
            state.ErrorMessage = errorMessage;
            if (status == SalesStatisticRefreshStatus.Running)
            {
                state.StartedAtUtc = now;
                state.CompletedAtUtc = null;
            }
            else
            {
                state.LastAggregatedAtUtc = now;
                state.CompletedAtUtc = now;
            }

            if (isNew)
            {
                await context.Db.Insertable(state).ExecuteCommandAsync();
            }
            else
            {
                await context.Db.Updateable(state).ExecuteCommandAsync();
            }
        }

        private static async Task<DateTime?> QueryDailyPosmSourceWatermarkAsync(
            POSMSqlSugarContext posmContext,
            DateTime date
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);
            var watermarks = new List<DateTime?>
            {
                await posmContext.Db.Queryable<SalesOrder>()
                    .Where(order =>
                        order.Status != null
                        && (order.Status == 1 || order.Status == 4)
                        && order.OrderTime != null
                        && order.OrderTime >= targetDate
                        && order.OrderTime < nextDate
                    )
                    .MaxAsync(order => order.LastUploadTime),
                await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                        (payment, order) => payment.OrderGuid == order.OrderGuid
                    )
                    .Where((payment, order) =>
                        order.Status != null
                        && (order.Status == 1 || order.Status == 4)
                        && order.OrderTime != null
                        && order.OrderTime >= targetDate
                        && order.OrderTime < nextDate
                    )
                    .MaxAsync((payment, order) => payment.LastUploadTime),
                await posmContext.Db.Queryable<SalesOrderDetail, SalesOrder>(
                        (detail, order) => detail.OrderGuid == order.OrderGuid
                    )
                    .Where((detail, order) =>
                        order.Status != null
                        && (order.Status == 1 || order.Status == 4)
                        && order.OrderTime != null
                        && order.OrderTime >= targetDate
                        && order.OrderTime < nextDate
                    )
                    .MaxAsync((detail, order) => detail.LastUploadTime),
            };
            var values = watermarks.Where(value => value.HasValue).Select(value => value!.Value).ToList();
            return values.Count == 0 ? null : values.Max();
        }

        private static async Task<DateTime?> QueryDailySourceWatermarkAsync(
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            DateTime date,
            IReadOnlyCollection<ProductStoreDailySourceRow>? preloadedHBSalesRows = null
        )
        {
            var targetDate = date.Date;
            var nextDate = targetDate.AddDays(1);
            var mainCheckoutDateWindowStart = targetDate.AddDays(
                -HBSalesMainCheckoutDateWindowDays
            );
            var mainCheckoutDateWindowEnd = nextDate.AddDays(HBSalesMainCheckoutDateWindowDays);
            var watermarks = new List<DateTime?>
            {
                await QueryDailyPosmSourceWatermarkAsync(posmContext, targetDate),
            };

            if (targetDate.Year == 2025)
            {
                var requiredHBSalesContext = hbSalesContext
                    ?? throw new InvalidOperationException("2025 年统计水位查询缺少 HBSalesRecord 上下文");
                if (preloadedHBSalesRows != null)
                {
                    // 仅 pre 阶段复用已加载明细；post 阶段不传此参数，必须重新查库检测漂移。
                    watermarks.Add(GetHBSalesSourceWatermark(preloadedHBSalesRows));
                }
                else
                {
                    var originalCommandTimeout = requiredHBSalesContext.Db.Ado.CommandTimeOut;
                    requiredHBSalesContext.Db.Ado.CommandTimeOut = Math.Max(
                        originalCommandTimeout,
                        CommandTimeoutSeconds
                    );
                    HBSalesSourceWatermarkRow? hbSalesWatermark;
                    try
                    {
                        hbSalesWatermark = await requiredHBSalesContext.Db.Queryable<SalesOrderMain>()
                            .LeftJoin<SalesOrderDetailRecord>((main, detail) =>
                                main.B销售单号 == detail.B销售单号
                            )
                            .Where((main, detail) =>
                                detail.B结账日期.HasValue
                                && detail.B结账日期.Value >= targetDate
                                && detail.B结账日期.Value < nextDate
                                && main.B结账日期.HasValue
                                && main.B结账日期.Value >= mainCheckoutDateWindowStart
                                && main.B结账日期.Value < mainCheckoutDateWindowEnd
                                && (main.B单据类型 == null || main.B单据类型.Trim() != "2")
                            )
                            .Select((main, detail) => new HBSalesSourceWatermarkRow
                            {
                                MainLastModifiedAt = SqlFunc.AggregateMax(main.FGC_LastModifyDate),
                                MainCreatedAt = SqlFunc.AggregateMax(main.FGC_CreateDate),
                                DetailLastModifiedAt = SqlFunc.AggregateMax(detail.FGC_LastModifyDate),
                                DetailCreatedAt = SqlFunc.AggregateMax(detail.FGC_CreateDate),
                            })
                            .FirstAsync();
                    }
                    finally
                    {
                        requiredHBSalesContext.Db.Ado.CommandTimeOut = originalCommandTimeout;
                    }
                    if (hbSalesWatermark != null)
                    {
                        watermarks.Add(GetLatestSourceTime(
                            hbSalesWatermark.MainLastModifiedAt,
                            hbSalesWatermark.MainCreatedAt,
                            hbSalesWatermark.DetailLastModifiedAt,
                            hbSalesWatermark.DetailCreatedAt
                        ));
                    }
                }
            }

            var values = watermarks
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();
            return values.Count == 0 ? null : values.Max();
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
            var skippedDates = new List<string>();
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
                var endExclusive = endDate.AddDays(1);
                var targetSupplierCodes = NormalizeSupplierCodes(supplierCodes);

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

                // 使用半开区间过滤，避免不同数据库对 DateTime.Date 的翻译差异。
                query = query.Where(o =>
                    o.OrderTime >= startDate
                    && o.OrderTime < endExclusive
                );

                // 设置供应商代码过滤条件
                if (targetSupplierCodes.Any())
                {
                    var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                    query = query.Where(
                        (o, d, m) =>
                            (m.LocalSupplierCode != null
                                && targetSupplierCodes.Contains(m.LocalSupplierCode.Trim()))
                            || (m.ChinaSupplierCode != null
                                && targetSupplierCodes.Contains(m.ChinaSupplierCode.Trim()))
                            || (d.SupplierCode != null
                                && targetSupplierCodes.Contains(d.SupplierCode.Trim()))
                            || (
                                includesUnknownSupplier
                                && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                                && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                            )
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
                                DetailSupplierCode = d.SupplierCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                TotalAmount = d.ActualAmount,
                                Quantity = d.Quantity ?? 0m,
                                OrderGuid = o.OrderGuid,
                            }
                    )
                    .ToListAsync();
                var orderAmountMaps = await LoadOrderAmountMapsAsync(
                    posmContext,
                    startDate,
                    endExclusive,
                    rawData,
                    row => row.OrderGuid,
                    row => row.TotalAmount ?? 0m
                );

                var resolvedRows = rawData
                    .Select(x => new
                    {
                        x.Date,
                        BranchCode = NormalizeCode(x.BranchCode),
                        LocalSupplierCode = ResolveStatisticSupplierCode(
                            x.LocalSupplierCode,
                            x.DetailSupplierCode
                        ),
                        ChinaSupplierCode = NormalizeCode(x.ChinaSupplierCode),
                        TotalAmount = ResolveStatisticAmount(
                            x.OrderGuid,
                            x.TotalAmount ?? 0m,
                            orderAmountMaps.PaymentAmounts,
                            orderAmountMaps.DetailAmounts
                        ),
                        x.Quantity,
                        x.OrderGuid,
                    })
                    .ToList();

                var shouldRefreshAllSuppliers = !targetSupplierCodes.Any();
                var refreshesLocalMasterSupplier = targetSupplierCodes.Contains("200");
                var localRowsForStats = shouldRefreshAllSuppliers
                    ? resolvedRows
                    : resolvedRows.Where(x => targetSupplierCodes.Contains(x.LocalSupplierCode)).ToList();
                var chinaRowsForStats = resolvedRows
                    .Where(x =>
                        x.LocalSupplierCode == "200"
                        && !string.IsNullOrEmpty(x.ChinaSupplierCode)
                        && (
                            shouldRefreshAllSuppliers
                            || refreshesLocalMasterSupplier
                            || targetSupplierCodes.Contains(x.ChinaSupplierCode)
                        )
                    )
                    .ToList();

                // 本地供应商聚合：局部刷新国内子供应商时不重写本地 200 总计。
                var localStats = localRowsForStats
                    .GroupBy(x => new { x.Date, x.LocalSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.LocalSupplierCode,
                        IsDomestic = false,
                        TotalAmount = g.Sum(x => x.TotalAmount),
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Distinct()
                            .Count(),
                        OrderCount = g.Select(x => x.OrderGuid)
                            .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
                            .Distinct()
                            .Count(),
                        UpdateTime = DateTime.Now,
                    })
                    .ToList();

                // 国内供应商聚合
                var chinaStats = chinaRowsForStats
                    .GroupBy(x => new { x.Date, x.ChinaSupplierCode })
                    .Select(g => new SupplierSalesStatistic
                    {
                        Date = g.Key.Date,
                        SupplierCode = g.Key.ChinaSupplierCode!,
                        IsDomestic = true,
                        TotalAmount = g.Sum(x => x.TotalAmount),
                        TotalQuantity = (int)g.Sum(x => x.Quantity),
                        StoreCount = g.Select(x => x.BranchCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Distinct()
                            .Count(),
                        OrderCount = g.Select(x => x.OrderGuid)
                            .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
                            .Distinct()
                            .Count(),
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
                    if (stat.SupplierCode == UnknownSupplierCode)
                    {
                        stat.SupplierName = UnknownSupplierName;
                    }
                    else if (supplierNameDict.TryGetValue(stat.SupplierCode, out var name))
                    {
                        stat.SupplierName = name;
                    }
                    else
                    {
                        stat.SupplierName = stat.SupplierCode;
                    }
                }

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 供应商统计按目标范围重建，避免旧空供应商或已无销售供应商残留。
                        var deleteable = context.Db.Deleteable<SupplierSalesStatistic>()
                            .Where(s => s.Date >= startDate && s.Date <= endDate);
                        if (targetSupplierCodes.Any())
                        {
                            var deleteSupplierCodes = new List<string>();
                            if (refreshesLocalMasterSupplier)
                            {
                                var existingDomesticSupplierCodes = await context
                                    .Db.Queryable<SupplierSalesStatistic>()
                                    .Where(s =>
                                        s.Date >= startDate
                                        && s.Date <= endDate
                                        && s.IsDomestic == true
                                    )
                                    .Select(s => s.SupplierCode)
                                    .Distinct()
                                    .ToListAsync();
                                deleteSupplierCodes.AddRange(existingDomesticSupplierCodes);
                            }

                            deleteSupplierCodes = deleteSupplierCodes
                                .Concat(targetSupplierCodes)
                                .Concat(allStats.Select(s => s.SupplierCode))
                                .Where(code => !string.IsNullOrWhiteSpace(code))
                                .Select(code => code.Trim())
                                .Distinct()
                                .ToList();
                            var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                            deleteable = deleteable.Where(s =>
                                deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                                || (
                                    includesUnknownSupplier
                                    && (s.SupplierCode == null || s.SupplierCode.Trim() == "")
                                )
                            );
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        logger.LogInformation("删除 {Count} 条供应商统计旧记录", deletedCount);

                        if (!allStats.Any())
                        {
                            logger.LogInformation(
                                "没有找到供应商统计数据: {StartDate} 至 {EndDate}",
                                startDate.ToString("yyyy-MM-dd"),
                                endDate.ToString("yyyy-MM-dd")
                            );
                            return;
                        }

                        context
                            .Db.Fastest<SupplierSalesStatistic>()
                            .PageSize(BatchSize)
                            .BulkCopy(allStats);
                        logger.LogInformation("批量插入 {Count} 条供应商统计记录", allStats.Count);
                    },
                    commitAsync: () => context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                    logger: logger,
                    operationName: "供应商统计数据更新"
                );

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

        private static List<StoreSupplierSalesDetail> BuildStoreSupplierSalesDetails(
            IEnumerable<StoreSupplierSourceRow> storeSupplierData,
            IReadOnlyDictionary<string, HBLocalSupplier> localSupplierDict,
            IReadOnlyDictionary<string, ChinaSupplier> chinaSupplierDict,
            DateTime updateTime
        )
        {
            var resolvedRows = new List<StoreSupplierResolvedRow>();

            foreach (var data in storeSupplierData)
            {
                // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                if (string.IsNullOrWhiteSpace(data.BranchCode))
                    continue;

                var branchCode = data.BranchCode.Trim();
                var mappedLocalSupplierCode = data.LocalSupplierCode?.Trim() ?? string.Empty;
                var detailSupplierCode = data.DetailSupplierCode?.Trim() ?? string.Empty;
                var localSupplierCode = !string.IsNullOrWhiteSpace(mappedLocalSupplierCode)
                    ? mappedLocalSupplierCode
                    : detailSupplierCode;
                var chinaSupplierCode = data.ChinaSupplierCode?.Trim();

                var supplierCode = localSupplierCode;
                var supplierName = localSupplierCode;
                var isDomestic = false;

                // 映射缺失或本地供应商为空时，优先回退 POSM 明细供应商，仍为空才归入 UNKNOWN，避免空供应商主键冲突。
                if (string.IsNullOrWhiteSpace(supplierCode))
                {
                    supplierCode = UnknownSupplierCode;
                    supplierName = UnknownSupplierName;
                }
                else if (localSupplierCode == "200" && !string.IsNullOrWhiteSpace(chinaSupplierCode))
                {
                    supplierCode = chinaSupplierCode;
                    isDomestic = true;
                    if (chinaSupplierDict.TryGetValue(chinaSupplierCode, out var cs))
                    {
                        supplierName = cs.SupplierName ?? supplierCode;
                    }
                    else
                    {
                        supplierName = supplierCode;
                    }
                }
                else if (localSupplierDict.TryGetValue(localSupplierCode, out var ls))
                {
                    supplierName = ls.Name ?? localSupplierCode;
                }

                resolvedRows.Add(new StoreSupplierResolvedRow
                {
                    Date = data.Date.Date,
                    BranchCode = branchCode,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    IsDomestic = isDomestic,
                    OrderGuid = data.OrderGuid,
                    TotalAmount = data.ActualAmount,
                    TotalQuantity = (int)data.Quantity,
                });
            }

            // 最终供应商编码可能来自不同路径（映射、明细、UNKNOWN），写表前必须按真实主键再次合并。
            return resolvedRows
                .GroupBy(stat => new { stat.Date, stat.BranchCode, stat.SupplierCode })
                .Select(group =>
                {
                    var orderCount = group
                        .Select(x => x.OrderGuid)
                        .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
                        .Distinct()
                        .Count();
                    return new StoreSupplierSalesDetail
                    {
                        Date = group.Key.Date,
                        BranchCode = group.Key.BranchCode,
                        SupplierCode = group.Key.SupplierCode,
                        SupplierName = group.Select(x => x.SupplierName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key.SupplierCode,
                        IsDomestic = group.Any(x => x.IsDomestic == true),
                        TotalAmount = group.Sum(x => x.TotalAmount),
                        TotalQuantity = group.Sum(x => x.TotalQuantity),
                        OrderCount = orderCount,
                        UpdateTime = updateTime,
                    };
                })
                .ToList();
        }

        private static List<AustralianSupplierStoreSalesDetail> BuildAustralianSupplierStoreSalesDetails(
            IEnumerable<StoreSupplierSourceRow> storeSupplierData,
            IReadOnlyDictionary<string, HBLocalSupplier> localSupplierDict,
            DateTime updateTime
        )
        {
            var resolvedRows = new List<StoreSupplierResolvedRow>();

            foreach (var data in storeSupplierData)
            {
                // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                if (string.IsNullOrWhiteSpace(data.BranchCode))
                    continue;

                var branchCode = data.BranchCode.Trim();
                var mappedLocalSupplierCode = data.LocalSupplierCode?.Trim() ?? string.Empty;
                var detailSupplierCode = data.DetailSupplierCode?.Trim() ?? string.Empty;
                var supplierCode = !string.IsNullOrWhiteSpace(mappedLocalSupplierCode)
                    ? mappedLocalSupplierCode
                    : detailSupplierCode;
                var supplierName = supplierCode;

                // 澳洲供应商统计只认本地供应商编码；映射为空时回退明细供应商，仍为空才归入 UNKNOWN。
                if (string.IsNullOrWhiteSpace(supplierCode))
                {
                    supplierCode = UnknownSupplierCode;
                    supplierName = UnknownSupplierName;
                }
                else if (localSupplierDict.TryGetValue(supplierCode, out var localSupplier))
                {
                    supplierName = localSupplier.Name ?? supplierCode;
                }

                resolvedRows.Add(new StoreSupplierResolvedRow
                {
                    Date = data.Date.Date,
                    BranchCode = branchCode,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    OrderGuid = data.OrderGuid,
                    TotalAmount = data.ActualAmount,
                    TotalQuantity = (int)data.Quantity,
                });
            }

            // 最终供应商编码可能由映射、明细或 UNKNOWN 得到，写入前必须按真实主键二次聚合。
            return resolvedRows
                .GroupBy(stat => new { stat.Date, stat.BranchCode, stat.SupplierCode })
                .Select(group =>
                {
                    var orderCount = group
                        .Select(x => x.OrderGuid)
                        .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
                        .Distinct()
                        .Count();
                    return new AustralianSupplierStoreSalesDetail
                    {
                        Date = group.Key.Date,
                        BranchCode = group.Key.BranchCode,
                        SupplierCode = group.Key.SupplierCode,
                        SupplierName = group.Select(x => x.SupplierName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key.SupplierCode,
                        TotalAmount = group.Sum(x => x.TotalAmount),
                        TotalQuantity = group.Sum(x => x.TotalQuantity),
                        OrderCount = orderCount,
                        UpdateTime = updateTime,
                    };
                })
                .ToList();
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
                var targetDate = (date ?? DateTime.Now.Date).Date;
                var nextDate = targetDate.AddDays(1);
                var targetBranchCodes = NormalizeBranchCodes(branchCodes);
                var targetSupplierCodes = NormalizeSupplierCodes(supplierCodes);

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
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    );

                // 设置分店过滤条件
                if (targetBranchCodes.Any())
                {
                    query = query.Where(o =>
                        (o.BranchCode != null && targetBranchCodes.Contains(o.BranchCode.Trim()))
                        || o.BranchCode == null
                        || o.BranchCode.Trim() == ""
                    );
                }

                // 设置供应商过滤条件
                if (targetSupplierCodes.Any())
                {
                    var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                    query = query.Where(
                        (o, d, m) =>
                            (
                                m.LocalSupplierCode != null
                                && targetSupplierCodes.Contains(m.LocalSupplierCode.Trim())
                            )
                            || (
                                m.ChinaSupplierCode != null
                                && targetSupplierCodes.Contains(m.ChinaSupplierCode.Trim())
                            )
                            || (d.SupplierCode != null && targetSupplierCodes.Contains(d.SupplierCode.Trim()))
                            || (
                                includesUnknownSupplier
                                && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                                && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                            )
                    );
                }

                // 查询销售明细后按最终供应商编码聚合，确保订单数按订单去重。
                var rawStoreSupplierData = await query
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode,
                                DeviceCode = o.DeviceCode,
                                OrderGuid = o.OrderGuid,
                                DetailSupplierCode = d.SupplierCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                ActualAmount = d.ActualAmount ?? 0m,
                                Quantity = d.Quantity ?? 0m,
                            }
                    )
                    .ToListAsync();
                var orderAmountMaps = await LoadOrderAmountMapsAsync(
                    _posmContext,
                    targetDate,
                    nextDate,
                    rawStoreSupplierData,
                    row => row.OrderGuid,
                    row => row.ActualAmount
                );
                var deviceBranchMap = await LoadDeviceBranchMapAsync(
                    _posmContext,
                    rawStoreSupplierData
                        .Where(row => string.IsNullOrWhiteSpace(row.BranchCode))
                        .Select(row => row.DeviceCode)
                );
                var storeSupplierData = rawStoreSupplierData
                    .Select(row => new StoreSupplierSourceRow
                    {
                        Date = row.Date,
                        BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                        DeviceCode = row.DeviceCode,
                        OrderGuid = row.OrderGuid,
                        DetailSupplierCode = row.DetailSupplierCode,
                        LocalSupplierCode = row.LocalSupplierCode,
                        ChinaSupplierCode = row.ChinaSupplierCode,
                        ActualAmount = ResolveStatisticAmount(
                            row.OrderGuid,
                            row.ActualAmount,
                            orderAmountMaps.PaymentAmounts,
                            orderAmountMaps.DetailAmounts
                        ),
                        Quantity = row.Quantity,
                    })
                    .Where(row =>
                        !targetBranchCodes.Any()
                        || targetBranchCodes.Contains(row.BranchCode ?? string.Empty)
                    )
                    .ToList();

                // 获取所有本地供应商代码
                var allLocalSupplierCodes = storeSupplierData
                    .Select(d =>
                        !string.IsNullOrWhiteSpace(d.LocalSupplierCode)
                            ? d.LocalSupplierCode!.Trim()
                            : d.DetailSupplierCode?.Trim()
                    )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
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

                LogSkippedBranchCodeRows(
                    "分店供应商销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.ActualAmount,
                    data => data.Quantity
                );

                var statisticsList = BuildStoreSupplierSalesDetails(
                    storeSupplierData,
                    localSupplierDict,
                    chinaSupplierDict,
                    DateTime.Now
                );

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 局部重算只删除本次范围，避免清掉同一天其它门店或供应商的统计。
                        var deleteable = _context
                            .Db.Deleteable<StoreSupplierSalesDetail>()
                            .Where(s => s.Date == targetDate);
                        if (targetBranchCodes.Any())
                        {
                            deleteable = deleteable.Where(s => targetBranchCodes.Contains(s.BranchCode));
                        }
                        if (targetSupplierCodes.Any())
                        {
                            // 国内供应商和 UNKNOWN 会在构建阶段改写成最终编码，删除时必须覆盖最终编码。
                            var deleteSupplierCodes = new List<string>();
                            if (targetSupplierCodes.Contains("200"))
                            {
                                var existingDomesticQuery = _context
                                    .Db.Queryable<StoreSupplierSalesDetail>()
                                    .Where(s => s.Date == targetDate && s.IsDomestic == true);
                                if (targetBranchCodes.Any())
                                {
                                    existingDomesticQuery = existingDomesticQuery.Where(s =>
                                        targetBranchCodes.Contains(s.BranchCode)
                                    );
                                }

                                var existingDomesticSupplierCodes = await existingDomesticQuery
                                    .Select(s => s.SupplierCode)
                                    .Distinct()
                                    .ToListAsync();
                                deleteSupplierCodes.AddRange(existingDomesticSupplierCodes);
                            }

                            deleteSupplierCodes = deleteSupplierCodes
                                .Concat(targetSupplierCodes)
                                .Concat(statisticsList.Select(s => s.SupplierCode))
                                .Where(code => !string.IsNullOrWhiteSpace(code))
                                .Select(code => code.Trim())
                                .Distinct()
                                .ToList();
                            var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                            deleteable = deleteable.Where(s =>
                                deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                                || (
                                    includesUnknownSupplier
                                    && (s.SupplierCode == null || s.SupplierCode.Trim() == "")
                                )
                            );
                        }

                        var deletedCount = await deleteable
                            .ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条门店供应商统计旧记录", deletedCount);

                        if (!statisticsList.Any())
                        {
                            _logger.LogInformation("没有找到门店供应商统计数据: {Date}", targetDate);
                            return;
                        }

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
                    await UpdateStoreSupplierStatistics(date, branchCodes, supplierCodes);
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

            var fullRefreshMaxConcurrency = ResolveFullRefreshMaxConcurrency(
                startDate,
                endMonthLastDay,
                _maxConcurrentUpdates
            );

            for (var rangeStart = startDate; rangeStart <= endMonthLastDay;)
            {
                var rangeEnd = rangeStart.AddDays(_maxDaysForConcurrentUpdate - 1);
                if (rangeEnd > endMonthLastDay)
                {
                    rangeEnd = endMonthLastDay;
                }

                // 月/季度复查复用同一条带数据库租约的日级全量刷新路径，避免旧批量入口漏表或重复跑。
                var rangeResult = await BatchFullRefreshConcurrent(
                    rangeStart,
                    rangeEnd,
                    fullRefreshMaxConcurrency
                );
                result.ProcessedDays += rangeResult.ProcessedDays;
                result.FailedDates.AddRange(rangeResult.FailedDates);
                result.SkippedDates.AddRange(rangeResult.SkippedDates);
                rangeStart = rangeEnd.AddDays(1);
            }

            for (var month = startDate; month <= endMonthLastDay; month = month.AddMonths(1))
            {
                var monthKey = month.ToString("yyyy-MM");
                result.ProcessedMonths++;
                if (result.FailedDates.Any(date => date.StartsWith(monthKey, StringComparison.Ordinal)))
                {
                    result.FailedMonths.Add(monthKey);
                }
            }

            result.Success = result.FailedDates.Count == 0;
            result.Message = result.Success
                ? $"批量按月份刷新完成: {result.ProcessedDays}/{result.TotalDays} 天, 跳过: {result.SkippedDates.Count} 天, {result.ProcessedMonths}/{result.TotalMonths} 个月"
                : $"批量按月份刷新部分完成: {result.ProcessedDays}/{result.TotalDays} 天, 跳过: {result.SkippedDates.Count} 天, 失败 {result.FailedDates.Count} 天, 失败月份: {string.Join(", ", result.FailedMonths)}";

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
            var concurrency = ResolveFullRefreshMaxConcurrency(
                startDate,
                endDate,
                maxConcurrency ?? _maxConcurrentUpdates
            );

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
            var skippedDates = new List<string>();
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
                            var hbSalesContext =
                                scope.ServiceProvider.GetService<HBSalesRecordSqlSugarContext>();
                            var logger = scope.ServiceProvider.GetRequiredService<
                                ILogger<SalesStatisticsJobService>
                            >();
                            var leaseService =
                                scope.ServiceProvider.GetRequiredService<ScheduledTaskLeaseService>();

                            // 调用带上下文的全量刷新方法
                            var rangeResult = await FullRefreshDateRangeWithContext(
                                context,
                                posmContext,
                                hbSalesContext,
                                logger,
                                leaseService,
                                dateRange.StartDate,
                                dateRange.EndDate
                            );

                            // 使用锁更新进度
                            lock (syncLock)
                            {
                                processedDays += rangeResult.ProcessedDays;
                                skippedDates.AddRange(rangeResult.SkippedDates);
                                failedDates.AddRange(rangeResult.FailedDates);
                            }

                            logger.LogInformation(
                                "并发块处理完成 {StartDate} 至 {EndDate} ({Days} 天), 累计进度: {Progress}/{Total}, 跳过: {Skipped}, 失败: {Failed}",
                                dateRange.StartDate.ToString("yyyy-MM-dd"),
                                dateRange.EndDate.ToString("yyyy-MM-dd"),
                                dateRange.DayCount,
                                processedDays,
                                totalDays,
                                rangeResult.SkippedDates.Count,
                                rangeResult.FailedDates.Count
                            );
                        }
                        catch (Exception ex)
                        {
                            var rangeKey =
                                $"{dateRange.StartDate:yyyy-MM-dd} 至 {dateRange.EndDate:yyyy-MM-dd}";
                            lock (syncLock)
                            {
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
                result.SkippedDates = skippedDates;
                result.Success = failedDates.Count == 0;

                var avgTimePerDay = stopwatch.Elapsed.TotalSeconds / totalDays;
                result.Message = result.Success
                    ? $"并发完整刷新完成: {processedDays}/{totalDays} 天, 跳过: {skippedDates.Count} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}, 平均每天: {avgTimePerDay:F2}秒"
                    : $"并发完整刷新部分完成: {processedDays}/{totalDays} 天, 跳过: {skippedDates.Count} 天, 失败: {failedDates.Count} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}";

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

        private static int ResolveFullRefreshMaxConcurrency(
            DateTime startDate,
            DateTime endDate,
            int configuredConcurrency
        )
        {
            var includes2025 = startDate.Date <= new DateTime(2025, 12, 31)
                && endDate.Date >= new DateTime(2025, 1, 1);
            return includes2025 ? 1 : configuredConcurrency;
        }

        /// <summary>
        /// 带上下文全量刷新日期范围（用于并发处理）
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="posmContext">POSM数据库上下文</param>
        /// <param name="hbSalesContext">HBSalesRecord 数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="leaseService">统计任务数据库租约服务</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        private async Task<FullRefreshRangeExecutionResult> FullRefreshDateRangeWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            ILogger logger,
            ScheduledTaskLeaseService leaseService,
            DateTime startDate,
            DateTime endDate
        )
        {
            var result = new FullRefreshRangeExecutionResult();
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
                    var dateStr = date.ToString("yyyy-MM-dd");
                    var leaseTaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType;
                    var leaseDuration = TimeSpan.FromHours(2);
                    string? leaseToken = null;
                    try
                    {
                        var lease = await leaseService.TryAcquireAsync(
                            leaseTaskType,
                            dateStr,
                            leaseDuration
                        );
                        if (!lease.Acquired)
                        {
                            result.SkippedDates.Add(dateStr);
                            logger.LogInformation("日期 {Date} 已有统计租约运行中，本次完整刷新跳过", dateStr);
                            continue;
                        }

                        leaseToken = lease.Lease?.LeaseToken;
                        if (string.IsNullOrWhiteSpace(leaseToken))
                        {
                            throw new InvalidOperationException($"统计租约缺少 fencing token: {dateStr}");
                        }

                        var sourceWatermark = await QueryDailySourceWatermarkAsync(
                            posmContext,
                            hbSalesContext,
                            date
                        );

                        async Task RunStep(
                            string statisticType,
                            string stepName,
                            Func<Task> action,
                            bool markFreshOnSuccess = true
                        )
                        {
                            await leaseService.EnsureActiveAsync(
                                leaseTaskType,
                                dateStr,
                                leaseToken,
                                leaseDuration,
                                stepName
                            );
                            await UpsertStatisticStateAsync(
                                context,
                                statisticType,
                                date,
                                SalesStatisticRefreshStatus.Running,
                                sourceWatermark,
                                null
                            );

                            try
                            {
                                await action();
                                await leaseService.EnsureActiveAsync(
                                    leaseTaskType,
                                    dateStr,
                                    leaseToken,
                                    leaseDuration,
                                    $"{stepName}完成确认"
                                );
                                if (markFreshOnSuccess)
                                {
                                    await UpsertStatisticStateAsync(
                                        context,
                                        statisticType,
                                        date,
                                        SalesStatisticRefreshStatus.Fresh,
                                        sourceWatermark,
                                        null
                                    );
                                }
                            }
                            catch (Exception stepEx)
                            {
                                await UpsertStatisticStateAsync(
                                    context,
                                    statisticType,
                                    date,
                                    SalesStatisticRefreshStatus.Failed,
                                    sourceWatermark,
                                    stepEx.Message
                                );
                                throw;
                            }
                        }

                        await RunStep(
                            SalesStatisticType.DailySales,
                            "每日统计",
                            () => UpdateDailyStatisticsWithContext(context, posmContext, logger, dateStr)
                        );
                        await RunStep(
                            SalesStatisticType.HourlySales,
                            "分时统计",
                            () => UpdateHourlyStatisticsWithContext(context, posmContext, logger, date, null)
                        );
                        if (date.Year != 2025)
                        {
                            await RunStep(
                                SalesStatisticType.StoreSales,
                                "分店统计",
                                () => UpdateStoreStatisticsWithContext(
                                    context,
                                    posmContext,
                                    hbSalesContext,
                                    logger,
                                    date,
                                    null
                                )
                            );
                        }
                        await RunStep(
                            SalesStatisticType.SupplierSales,
                            "供应商统计",
                            () => UpdateSupplierStatisticsWithContext(context, posmContext, logger, date, date, null)
                        );
                        await RunStep(
                            SalesStatisticType.StoreSupplierSales,
                            "门店供应商统计",
                            () => UpdateStoreSupplierStatisticsWithContext(context, posmContext, logger, date, null, null)
                        );
                        await RunStep(
                            SalesStatisticType.ProductStoreDaily,
                            "商品分店每日统计",
                            async () =>
                            {
                                if (date.Year == 2025)
                                {
                                    // 完整刷新也必须复用 2025 原子入口，不能先独立提交分店统计。
                                    await Update2025StoreAndProductStatisticsAtomically(
                                        context,
                                        posmContext,
                                        hbSalesContext
                                            ?? throw new InvalidOperationException(
                                                "2025 年完整刷新缺少 HBSalesRecord 上下文"
                                            ),
                                        logger,
                                        date,
                                        sourceWatermark
                                    );
                                }
                                else
                                {
                                    await UpdateProductStoreDailyStatisticsWithContext(
                                        context,
                                        posmContext,
                                        hbSalesContext,
                                        logger,
                                        date
                                    );
                                }

                                var productState = await context.Db
                                    .Queryable<SalesStatisticRefreshState>()
                                    .Where(state =>
                                        state.StatisticType == SalesStatisticType.ProductStoreDaily
                                        && state.Date == date.Date
                                    )
                                    .FirstAsync();
                                if (productState == null
                                    || productState.Status == SalesStatisticRefreshStatus.Failed)
                                {
                                    throw new InvalidOperationException(
                                        productState?.ErrorMessage
                                            ?? $"商品分店每日统计状态缺失: {date:yyyy-MM-dd}"
                                    );
                                }
                            },
                            false
                        );
                        await RunStep(
                            SalesStatisticType.AustralianSupplierStoreSales,
                            "澳洲供应商门店统计",
                            () => UpdateAustralianSupplierStoreStatisticsWithContext(context, posmContext, logger, date, null, null)
                        );
                        await RunStep(
                            SalesStatisticType.ChinaSupplierStoreSales,
                            "中国供应商门店统计",
                            () => UpdateChinaSupplierStoreStatisticsWithContext(context, posmContext, logger, date, null, null)
                        );

                        logger.LogInformation(
                            "日期 {Date} 完整刷新完成",
                            date.ToString("yyyy-MM-dd")
                        );
                        if (!await leaseService.CompleteAsync(leaseTaskType, dateStr, leaseToken, true))
                        {
                            throw new InvalidOperationException($"统计租约完成失败，token 已失效: {dateStr}");
                        }
                        result.ProcessedDays += 1;
                    }
                    catch (Exception ex)
                    {
                        if (!string.IsNullOrWhiteSpace(leaseToken))
                        {
                            await leaseService.CompleteAsync(
                                leaseTaskType,
                                dateStr,
                                leaseToken,
                                false,
                                ex.Message
                            );
                        }
                        logger.LogError(
                            ex,
                            "日期 {Date} 完整刷新失败",
                            date.ToString("yyyy-MM-dd")
                        );
                        result.FailedDates.Add(dateStr);
                    }
                }

                logger.LogInformation(
                    "日期范围完整刷新完成: {StartDate} 至 {EndDate}",
                    startDate.ToString("yyyy-MM-dd"),
                    endDate.ToString("yyyy-MM-dd")
                );
                return result;
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

                var statistic = await BuildDailySalesStatisticAsync(posmContext, date, DateTime.Now);

                if (statistic != null)
                {
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
                var rangeStart = hour.HasValue ? date.Date.AddHours(hour.Value) : date.Date;
                var rangeEnd = hour.HasValue ? rangeStart.AddHours(1) : date.Date.AddDays(1);

                logger.LogInformation(
                    "开始更新分时统计数据: {Date}, 小时: {Hours}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23"
                );

                // 金额取支付明细、销量取销售明细、订单数取订单头，避免拆分支付放大非金额指标。
                var hourlyRevenueRows = await posmContext
                    .Db.Queryable<PaymentDetail, SalesOrder>(
                        (pd, so) => pd.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (pd, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime >= rangeStart
                            && so.OrderTime < rangeEnd
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
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                            }
                    )
                    .ToListAsync();

                var hourlyQuantityRows = await posmContext
                    .Db.Queryable<SalesOrderDetail, SalesOrder>(
                        (detail, so) => detail.OrderGuid == so.OrderGuid
                    )
                    .Where(
                        (detail, so) =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime >= rangeStart
                            && so.OrderTime < rangeEnd
                    )
                    .GroupBy(
                        (detail, so) =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                so.BranchCode,
                            }
                    )
                    .Select(
                        (detail, so) =>
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalQuantity = SqlFunc.AggregateSum(detail.Quantity) ?? 0,
                            }
                    )
                    .ToListAsync();

                var hourlyOrderRows = await posmContext
                    .Db.Queryable<SalesOrder>()
                    .Where(
                        so =>
                            so.Status != null
                            && (so.Status == 1 || so.Status == 4)
                            && so.OrderTime != null
                            && so.OrderTime >= rangeStart
                            && so.OrderTime < rangeEnd
                    )
                    .GroupBy(
                        so =>
                            new
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                so.BranchCode,
                            }
                    )
                    .Select(
                        so =>
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                                CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                            }
                    )
                    .ToListAsync();

                var allHourlyData = hourlyRevenueRows
                    .Concat(hourlyQuantityRows)
                    .Concat(hourlyOrderRows)
                    .GroupBy(row => new { row.Date, row.Hour, row.BranchCode })
                    .Select(group => new HourlyStatisticSourceRow
                    {
                        Date = group.Key.Date,
                        Hour = group.Key.Hour,
                        BranchCode = group.Key.BranchCode,
                        TotalAmount = group.Sum(row => row.TotalAmount),
                        TotalQuantity = group.Sum(row => row.TotalQuantity),
                        OrderCount = group.Sum(row => row.OrderCount),
                        CustomerCount = group.Sum(row => row.CustomerCount),
                    })
                    .ToList();

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
                            OrderCount = hourlyDataForHour.Sum(d => d.OrderCount),
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
                        OrderCount = data.OrderCount,
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
        /// <param name="hbSalesContext">HBSalesRecord 数据库上下文；仅 2025 年使用</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="date">目标日期</param>
        /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
        private async Task UpdateStoreStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            ILogger logger,
            DateTime date,
            List<string>? branchCodes
        )
        {
            try
            {
                var targetDate = date.Date;
                logger.LogInformation(
                    "开始更新指定分店统计数据: {Date}, Branches: {Branches}",
                    date,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All"
                );

                var statisticsList = await BuildStoreStatisticsAsync(
                    context,
                    posmContext,
                    targetDate.Year == 2025
                        ? hbSalesContext
                            ?? throw new InvalidOperationException("2025 年分店统计缺少 HBSalesRecord 上下文")
                        : null,
                    targetDate,
                    branchCodes
                );
                var targetBranchCodes = NormalizeBranchCodes(branchCodes);
                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        var deleteable = context.Db.Deleteable<StoreSalesStatistic>()
                            .Where(row => row.Date == targetDate);
                        if (targetBranchCodes.Any())
                        {
                            deleteable = deleteable.Where(row =>
                                targetBranchCodes.Contains(row.BranchCode)
                            );
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        logger.LogInformation("删除 {Count} 条分店统计旧记录", deletedCount);
                        if (statisticsList.Any())
                        {
                            context.Db.Fastest<StoreSalesStatistic>()
                                .PageSize(BatchSize)
                                .BulkCopy(statisticsList);
                        }
                    },
                    commitAsync: () => context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                    logger: logger,
                    operationName: "并发分店统计数据更新"
                );

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
                var targetDate = (date ?? DateTime.Now.Date).Date;
                var nextDate = targetDate.AddDays(1);
                var targetBranchCodes = NormalizeBranchCodes(branchCodes);
                var targetSupplierCodes = NormalizeSupplierCodes(supplierCodes);

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
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    );

                // 设置分店过滤条件
                if (targetBranchCodes.Any())
                {
                    query = query.Where(o =>
                        (o.BranchCode != null && targetBranchCodes.Contains(o.BranchCode.Trim()))
                        || o.BranchCode == null
                        || o.BranchCode.Trim() == ""
                    );
                }

                // 设置供应商过滤条件
                if (targetSupplierCodes.Any())
                {
                    var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                    query = query.Where(
                        (o, d, m) =>
                            (
                                m.LocalSupplierCode != null
                                && targetSupplierCodes.Contains(m.LocalSupplierCode.Trim())
                            )
                            || (
                                m.ChinaSupplierCode != null
                                && targetSupplierCodes.Contains(m.ChinaSupplierCode.Trim())
                            )
                            || (d.SupplierCode != null && targetSupplierCodes.Contains(d.SupplierCode.Trim()))
                            || (
                                includesUnknownSupplier
                                && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                                && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                            )
                    );
                }

                // 查询销售明细后按最终供应商编码聚合，确保订单数按订单去重。
                var rawStoreSupplierData = await query
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode,
                                DeviceCode = o.DeviceCode,
                                OrderGuid = o.OrderGuid,
                                DetailSupplierCode = d.SupplierCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                ActualAmount = d.ActualAmount ?? 0m,
                                Quantity = d.Quantity ?? 0m,
                            }
                    )
                    .ToListAsync();
                var orderAmountMaps = await LoadOrderAmountMapsAsync(
                    posmContext,
                    targetDate,
                    nextDate,
                    rawStoreSupplierData,
                    row => row.OrderGuid,
                    row => row.ActualAmount
                );
                var deviceBranchMap = await LoadDeviceBranchMapAsync(
                    posmContext,
                    rawStoreSupplierData
                        .Where(row => string.IsNullOrWhiteSpace(row.BranchCode))
                        .Select(row => row.DeviceCode)
                );
                var storeSupplierData = rawStoreSupplierData
                    .Select(row => new StoreSupplierSourceRow
                    {
                        Date = row.Date,
                        BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                        DeviceCode = row.DeviceCode,
                        OrderGuid = row.OrderGuid,
                        DetailSupplierCode = row.DetailSupplierCode,
                        LocalSupplierCode = row.LocalSupplierCode,
                        ChinaSupplierCode = row.ChinaSupplierCode,
                        ActualAmount = ResolveStatisticAmount(
                            row.OrderGuid,
                            row.ActualAmount,
                            orderAmountMaps.PaymentAmounts,
                            orderAmountMaps.DetailAmounts
                        ),
                        Quantity = row.Quantity,
                    })
                    .Where(row =>
                        !targetBranchCodes.Any()
                        || targetBranchCodes.Contains(row.BranchCode ?? string.Empty)
                    )
                    .ToList();

                // 获取所有本地供应商代码
                var allLocalSupplierCodes = storeSupplierData
                    .Select(d =>
                        !string.IsNullOrWhiteSpace(d.LocalSupplierCode)
                            ? d.LocalSupplierCode!.Trim()
                            : d.DetailSupplierCode?.Trim()
                    )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
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

                LogSkippedBranchCodeRows(
                    "分店供应商销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.ActualAmount,
                    data => data.Quantity
                );

                var statisticsList = BuildStoreSupplierSalesDetails(
                    storeSupplierData,
                    localSupplierDict,
                    chinaSupplierDict,
                    DateTime.Now
                );

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 并发路径也按本次影响范围重建，避免旧供应商统计残留。
                        var deleteable = context.Db.Deleteable<StoreSupplierSalesDetail>()
                            .Where(s => s.Date == targetDate);
                        if (targetBranchCodes.Any())
                        {
                            deleteable = deleteable.Where(s => targetBranchCodes.Contains(s.BranchCode));
                        }
                        if (targetSupplierCodes.Any())
                        {
                            var deleteSupplierCodes = new List<string>();
                            if (targetSupplierCodes.Contains("200"))
                            {
                                var existingDomesticQuery = context.Db.Queryable<StoreSupplierSalesDetail>()
                                    .Where(s => s.Date == targetDate && s.IsDomestic == true);
                                if (targetBranchCodes.Any())
                                {
                                    existingDomesticQuery = existingDomesticQuery.Where(s =>
                                        targetBranchCodes.Contains(s.BranchCode)
                                    );
                                }

                                var existingDomesticSupplierCodes = await existingDomesticQuery
                                    .Select(s => s.SupplierCode)
                                    .Distinct()
                                    .ToListAsync();
                                deleteSupplierCodes.AddRange(existingDomesticSupplierCodes);
                            }

                            deleteSupplierCodes = deleteSupplierCodes
                                .Concat(targetSupplierCodes)
                                .Concat(statisticsList.Select(s => s.SupplierCode))
                                .Where(code => !string.IsNullOrWhiteSpace(code))
                                .Select(code => code.Trim())
                                .Distinct()
                                .ToList();
                            var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                            deleteable = deleteable.Where(s =>
                                deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                                || (
                                    includesUnknownSupplier
                                    && (s.SupplierCode == null || s.SupplierCode.Trim() == "")
                                )
                            );
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        logger.LogInformation("删除 {Count} 条门店供应商统计旧记录", deletedCount);

                        if (!statisticsList.Any())
                        {
                            logger.LogInformation("没有找到门店供应商统计数据: {Date}", targetDate);
                            return;
                        }

                        context
                            .Db.Fastest<StoreSupplierSalesDetail>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                        logger.LogInformation("批量插入 {Count} 条门店供应商统计记录", statisticsList.Count);
                    },
                    commitAsync: () => context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                    logger: logger,
                    operationName: "门店供应商统计数据更新"
                );

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
                var targetDate = (date ?? DateTime.Now.Date).Date;
                var nextDate = targetDate.AddDays(1);
                var targetBranchCodes = NormalizeBranchCodes(branchCodes);
                var targetSupplierCodes = NormalizeSupplierCodes(supplierCodes);

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
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    );

                // 设置分店过滤条件
                if (targetBranchCodes.Any())
                {
                    query = query.Where(o =>
                        (o.BranchCode != null && targetBranchCodes.Contains(o.BranchCode.Trim()))
                        || o.BranchCode == null
                        || o.BranchCode.Trim() == ""
                    );
                }

                // 设置供应商过滤条件
                if (targetSupplierCodes.Any())
                {
                    var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                    query = query.Where(
                        (o, d, m) =>
                            (
                                m.LocalSupplierCode != null
                                && targetSupplierCodes.Contains(m.LocalSupplierCode.Trim())
                            )
                            || (
                                d.SupplierCode != null
                                && targetSupplierCodes.Contains(d.SupplierCode.Trim())
                            )
                            || (
                                includesUnknownSupplier
                                && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                                && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                            )
                    );
                }

                // 查询销售明细后按最终供应商编码聚合，避免空映射在写表时形成重复主键。
                var rawStoreSupplierData = await query
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode,
                                DeviceCode = o.DeviceCode,
                                OrderGuid = o.OrderGuid,
                                DetailSupplierCode = d.SupplierCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ActualAmount = d.ActualAmount ?? 0m,
                                Quantity = d.Quantity ?? 0m,
                            }
                    )
                    .ToListAsync();
                var (paymentAmounts, detailAmounts) = await LoadOrderAmountMapsAsync(
                    _posmContext,
                    targetDate,
                    nextDate,
                    rawStoreSupplierData,
                    row => row.OrderGuid,
                    row => row.ActualAmount
                );
                var deviceBranchMap = await LoadDeviceBranchMapAsync(
                    _posmContext,
                    rawStoreSupplierData
                        .Where(row => string.IsNullOrWhiteSpace(row.BranchCode))
                        .Select(row => row.DeviceCode)
                );
                var storeSupplierData = rawStoreSupplierData
                    .Select(row => new StoreSupplierSourceRow
                    {
                        Date = row.Date,
                        BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                        DeviceCode = row.DeviceCode,
                        OrderGuid = row.OrderGuid,
                        DetailSupplierCode = row.DetailSupplierCode,
                        LocalSupplierCode = row.LocalSupplierCode,
                        ActualAmount = ResolveStatisticAmount(
                            row.OrderGuid,
                            row.ActualAmount,
                            paymentAmounts,
                            detailAmounts
                        ),
                        Quantity = row.Quantity,
                    })
                    .Where(row =>
                        !targetBranchCodes.Any()
                        || targetBranchCodes.Contains(row.BranchCode ?? string.Empty)
                    )
                    .ToList();

                // 获取所有本地供应商代码
                var allLocalSupplierCodes = storeSupplierData
                    .Select(d =>
                        !string.IsNullOrWhiteSpace(d.LocalSupplierCode)
                            ? d.LocalSupplierCode!.Trim()
                            : d.DetailSupplierCode?.Trim()
                    )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
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

                LogSkippedBranchCodeRows(
                    "澳洲供应商分店销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.ActualAmount,
                    data => data.Quantity
                );

                var statisticsList = BuildAustralianSupplierStoreSalesDetails(
                    storeSupplierData,
                    localSupplierDict,
                    DateTime.Now
                );

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 普通路径也按本次影响范围清理，避免局部供应商刷新误删同日其它供应商。
                        var deleteable = _context
                            .Db.Deleteable<AustralianSupplierStoreSalesDetail>()
                            .Where(s => s.Date == targetDate);

                        if (targetBranchCodes.Any())
                        {
                            deleteable = deleteable.Where(s =>
                                targetBranchCodes.Contains(s.BranchCode.Trim())
                            );
                        }

                        if (targetSupplierCodes.Any())
                        {
                            var deleteSupplierCodes = targetSupplierCodes
                                .Concat(statisticsList.Select(s => s.SupplierCode))
                                .Where(code => !string.IsNullOrWhiteSpace(code))
                                .Select(code => code.Trim())
                                .Distinct()
                                .ToList();
                            deleteable = deleteable.Where(s =>
                                // 局部补算也要清理历史空白供应商，避免新回退编码旁边残留旧空主键数据。
                                s.SupplierCode == null
                                || s.SupplierCode.Trim() == ""
                                || deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                            );
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        _logger.LogInformation("删除 {Count} 条澳洲供应商门店统计旧记录", deletedCount);

                        if (!statisticsList.Any())
                        {
                            _logger.LogInformation("没有找到澳洲供应商门店数据: {Date}", targetDate);
                            return;
                        }

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
                var targetDate = (date ?? DateTime.Now.Date).Date;
                var nextDate = targetDate.AddDays(1);
                var targetBranchCodes = NormalizeBranchCodes(branchCodes);
                var targetSupplierCodes = NormalizeSupplierCodes(supplierCodes);

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
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    );

                if (targetBranchCodes.Any())
                {
                    query = query.Where(o =>
                        (o.BranchCode != null && targetBranchCodes.Contains(o.BranchCode.Trim()))
                        || o.BranchCode == null
                        || o.BranchCode.Trim() == ""
                    );
                }

                if (targetSupplierCodes.Any())
                {
                    var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                    query = query.Where(
                        (o, d, m) =>
                            (
                                m.LocalSupplierCode != null
                                && targetSupplierCodes.Contains(m.LocalSupplierCode.Trim())
                            )
                            || (
                                d.SupplierCode != null
                                && targetSupplierCodes.Contains(d.SupplierCode.Trim())
                            )
                            || (
                                includesUnknownSupplier
                                && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                                && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                            )
                    );
                }

                // 查询销售明细后按最终供应商编码聚合，避免空映射在写表时形成重复主键。
                var rawStoreSupplierData = await query
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode,
                                DeviceCode = o.DeviceCode,
                                OrderGuid = o.OrderGuid,
                                DetailSupplierCode = d.SupplierCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ActualAmount = d.ActualAmount ?? 0m,
                                Quantity = d.Quantity ?? 0m,
                            }
                    )
                    .ToListAsync();
                var (paymentAmounts, detailAmounts) = await LoadOrderAmountMapsAsync(
                    posmContext,
                    targetDate,
                    nextDate,
                    rawStoreSupplierData,
                    row => row.OrderGuid,
                    row => row.ActualAmount
                );
                var deviceBranchMap = await LoadDeviceBranchMapAsync(
                    posmContext,
                    rawStoreSupplierData
                        .Where(row => string.IsNullOrWhiteSpace(row.BranchCode))
                        .Select(row => row.DeviceCode)
                );
                var storeSupplierData = rawStoreSupplierData
                    .Select(row => new StoreSupplierSourceRow
                    {
                        Date = row.Date,
                        BranchCode = ResolveBranchCode(row.BranchCode, row.DeviceCode, deviceBranchMap),
                        DeviceCode = row.DeviceCode,
                        OrderGuid = row.OrderGuid,
                        DetailSupplierCode = row.DetailSupplierCode,
                        LocalSupplierCode = row.LocalSupplierCode,
                        ActualAmount = ResolveStatisticAmount(
                            row.OrderGuid,
                            row.ActualAmount,
                            paymentAmounts,
                            detailAmounts
                        ),
                        Quantity = row.Quantity,
                    })
                    .Where(row =>
                        !targetBranchCodes.Any()
                        || targetBranchCodes.Contains(row.BranchCode ?? string.Empty)
                    )
                    .ToList();

                var allLocalSupplierCodes = storeSupplierData
                    .Select(d =>
                        !string.IsNullOrWhiteSpace(d.LocalSupplierCode)
                            ? d.LocalSupplierCode!.Trim()
                            : d.DetailSupplierCode?.Trim()
                    )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
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

                LogSkippedBranchCodeRows(
                    "澳洲供应商分店销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.ActualAmount,
                    data => data.Quantity
                );

                var statisticsList = BuildAustralianSupplierStoreSalesDetails(
                    storeSupplierData,
                    localSupplierDict,
                    DateTime.Now
                );

                await ExecuteTransactionSafelyAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 并发补算路径也先清理本次影响范围，避免旧空供应商主键在新 UNKNOWN 旁边残留。
                        var deleteable = context
                            .Db.Deleteable<AustralianSupplierStoreSalesDetail>()
                            .Where(s => s.Date == targetDate);

                        if (targetBranchCodes.Any())
                        {
                            deleteable = deleteable.Where(s =>
                                targetBranchCodes.Contains(s.BranchCode.Trim())
                            );
                        }

                        if (targetSupplierCodes.Any())
                        {
                            var deleteSupplierCodes = targetSupplierCodes
                                .Concat(statisticsList.Select(s => s.SupplierCode))
                                .Where(code => !string.IsNullOrWhiteSpace(code))
                                .Select(code => code.Trim())
                                .Distinct()
                                .ToList();
                            deleteable = deleteable.Where(s =>
                                // 局部补算也要清理历史空白供应商，避免新回退编码旁边残留旧空主键数据。
                                s.SupplierCode == null
                                || s.SupplierCode.Trim() == ""
                                || deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                            );
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        logger.LogInformation("删除 {Count} 条澳洲供应商门店统计旧记录", deletedCount);

                        if (!statisticsList.Any())
                        {
                            logger.LogInformation("没有找到澳洲供应商门店数据: {Date}", targetDate);
                            return;
                        }

                        context
                            .Db.Fastest<AustralianSupplierStoreSalesDetail>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                        logger.LogInformation(
                            "批量插入 {Count} 条澳洲供应商门店统计记录",
                            statisticsList.Count
                        );
                    },
                    commitAsync: () => context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                    logger: logger,
                    operationName: "澳洲供应商门店统计数据更新"
                );

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
