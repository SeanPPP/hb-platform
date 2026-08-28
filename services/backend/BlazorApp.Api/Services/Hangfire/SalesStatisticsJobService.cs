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
    /// 销售统计作业服务
    /// 负责从POSM系统获取销售数据并生成各种维度的统计报表
    /// </summary>
    public class SalesStatisticsJobService : IProductStoreDailyStatisticExecutor
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
            private readonly SalesStatisticsCompatibilityAdapter.HBSalesSnapshotView<
                ProductStoreDailySourceRow,
                HBSales2025DailySnapshotSignature> _view;

            internal HBSales2025BatchSnapshot(
                IReadOnlyDictionary<DateTime, List<ProductStoreDailySourceRow>> rowsByDate,
                IReadOnlyDictionary<DateTime, HBSales2025DailySnapshotSignature> signatures
            )
            {
                Canonical = SalesStatisticsCompatibilityAdapter.ToCanonicalHBSalesSnapshot(
                    rowsByDate,
                    signatures,
                    ToCanonicalProductStoreDailySourceRow,
                    ToCanonicalHBSalesSignature
                );
                _view = SalesStatisticsCompatibilityAdapter.CreateHBSalesView(
                    Canonical,
                    ToLegacyProductStoreDailySourceRow,
                    ToLegacyHBSalesSignature
                );
                Signatures = _view.Signatures;
            }

            internal HBSales2025BatchSnapshot(
                global::BlazorApp.Api.Services.HBSales2025BatchSnapshot canonical
            )
            {
                Canonical = canonical;
                _view = SalesStatisticsCompatibilityAdapter.CreateHBSalesView(
                    canonical,
                    ToLegacyProductStoreDailySourceRow,
                    ToLegacyHBSalesSignature
                );
                Signatures = _view.Signatures;
            }

            internal global::BlazorApp.Api.Services.HBSales2025BatchSnapshot Canonical { get; }

            public IReadOnlyDictionary<DateTime, HBSales2025DailySnapshotSignature> Signatures { get; }

            internal IReadOnlyList<ProductStoreDailySourceRow> GetRows(DateTime date)
            {
                return _view.GetRows(date);
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
            private readonly SalesStatisticsCompatibilityAdapter.PosmSnapshotView<
                ProductStoreDailySourceRow,
                StoreStatisticPaymentRow,
                StoreStatisticOrderRow,
                Posm2025DailySnapshotSignature> _view;

            internal Posm2025DailySnapshot(
                IReadOnlyList<ProductStoreDailySourceRow> detailRows,
                IReadOnlyList<ProductStoreDailySourceRow> supplementalReturnRows,
                IReadOnlyList<StoreStatisticPaymentRow> paymentRows,
                IReadOnlyList<StoreStatisticOrderRow> orderRows,
                Dictionary<string, string> deviceBranchMap,
                Posm2025DailySnapshotSignature signature
            )
            {
                Canonical = SalesStatisticsCompatibilityAdapter.ToCanonicalPosmSnapshot(
                    detailRows,
                    supplementalReturnRows,
                    paymentRows,
                    orderRows,
                    deviceBranchMap,
                    signature,
                    ToCanonicalProductStoreDailySourceRow,
                    ToCanonicalStoreStatisticPaymentRow,
                    ToCanonicalStoreStatisticOrderRow,
                    ToCanonicalPosmSnapshotSignature
                );
                _view = SalesStatisticsCompatibilityAdapter.CreatePosmView(
                    Canonical,
                    ToLegacyProductStoreDailySourceRow,
                    ToLegacyStoreStatisticPaymentRow,
                    ToLegacyStoreStatisticOrderRow,
                    ToLegacyPosmSnapshotSignature
                );
                DetailRows = _view.DetailRows;
                SupplementalReturnRows = _view.SupplementalReturnRows;
                PaymentRows = _view.PaymentRows;
                OrderRows = _view.OrderRows;
                DeviceBranchMap = _view.DeviceBranchMap;
                Signature = _view.Signature;
            }

            internal Posm2025DailySnapshot(
                global::BlazorApp.Api.Services.Posm2025DailySnapshot canonical
            )
            {
                Canonical = canonical;
                _view = SalesStatisticsCompatibilityAdapter.CreatePosmView(
                    canonical,
                    ToLegacyProductStoreDailySourceRow,
                    ToLegacyStoreStatisticPaymentRow,
                    ToLegacyStoreStatisticOrderRow,
                    ToLegacyPosmSnapshotSignature
                );
                DetailRows = _view.DetailRows;
                SupplementalReturnRows = _view.SupplementalReturnRows;
                PaymentRows = _view.PaymentRows;
                OrderRows = _view.OrderRows;
                DeviceBranchMap = _view.DeviceBranchMap;
                Signature = _view.Signature;
            }

            internal IReadOnlyList<ProductStoreDailySourceRow> DetailRows { get; }
            internal IReadOnlyList<ProductStoreDailySourceRow> SupplementalReturnRows { get; }
            internal IReadOnlyList<StoreStatisticPaymentRow> PaymentRows { get; }
            internal IReadOnlyList<StoreStatisticOrderRow> OrderRows { get; }
            internal Dictionary<string, string> DeviceBranchMap { get; }
            public Posm2025DailySnapshotSignature Signature { get; }
            internal global::BlazorApp.Api.Services.Posm2025DailySnapshot Canonical { get; }
        }

        /// <summary>
        /// 商品统计提交串行锁，避免同一实例内并发请求重复提交相同日期。
        /// </summary>

        internal sealed class StoreCostRow
        {
            public string? StoreCode { get; set; }
            public string? SupplierCode { get; set; }
            public string? ProductCode { get; set; }
            public decimal? PurchasePrice { get; set; }
        }

        internal sealed class ProductCostRow
        {
            public string? ProductCode { get; set; }
            public decimal? PurchasePrice { get; set; }
        }

        internal sealed class WarehouseCostRow
        {
            public string ProductCode { get; set; } = string.Empty;
            public decimal? ImportPrice { get; set; }
        }

        internal sealed class ProductStatisticDiagnosticRow
        {
            public string BranchCode { get; set; } = string.Empty;
            public decimal UnmatchedSupplierAmount { get; set; }
            public int UnmatchedSupplierQuantity { get; set; }
            public int UnmatchedSupplierProductCount { get; set; }
        }

        // 切片间传递的诊断 DTO 仅在本程序集可见，避免扩展公开服务契约。
        internal sealed class ProductStatisticDiagnostics
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

        internal sealed class StoreStatisticQuantityRow
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

        internal sealed class HBSalesSourceWatermarkRow
        {
            public DateTime? MainLastModifiedAt { get; set; }
            public DateTime? MainCreatedAt { get; set; }
            public DateTime? DetailLastModifiedAt { get; set; }
            public DateTime? DetailCreatedAt { get; set; }
        }

        // HBSales 聚合行由商品日统计基础设施共享，保持内部可见即可。
        internal sealed class HBSalesStoreAggregateRow
        {
            public string? BranchCode { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal TotalQuantity { get; set; }
            public int OrderCount { get; set; }
        }

        internal sealed class HourlyStatisticSourceRow
        {
            public DateTime Date { get; set; }
            public int Hour { get; set; }
            public string? BranchCode { get; set; }
            public decimal TotalAmount { get; set; }
            public int TotalQuantity { get; set; }
            public int OrderCount { get; set; }
            public int CustomerCount { get; set; }
        }

        // 订单金额投影供商品日统计切片复用，不对程序集外暴露。
        internal sealed class OrderAmountRow
        {
            public string? OrderGuid { get; set; }
            public decimal Amount { get; set; }
        }

        // 门店供应商来源投影由供应商切片共享，避免重复定义口径。
        internal sealed class StoreSupplierSourceRow
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

        internal sealed class StoreSupplierResolvedRow
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

        // Legacy nested DTO 的字段映射只存在于 façade 外沿；通用适配器不认识 owner 类型。
        private static global::BlazorApp.Api.Services.HBSales2025DailySnapshotSignature
            ToCanonicalHBSalesSignature(HBSales2025DailySnapshotSignature value) =>
            new(
                value.Date,
                value.RowCount,
                value.MainLastModifiedAt,
                value.MainCreatedAt,
                value.DetailLastModifiedAt,
                value.DetailCreatedAt,
                value.Checksum
            );

        private static HBSales2025DailySnapshotSignature ToLegacyHBSalesSignature(
            global::BlazorApp.Api.Services.HBSales2025DailySnapshotSignature value) =>
            new(
                value.Date,
                value.RowCount,
                value.MainLastModifiedAt,
                value.MainCreatedAt,
                value.DetailLastModifiedAt,
                value.DetailCreatedAt,
                value.Checksum
            );

        private static global::BlazorApp.Api.Services.Posm2025DailyTableSignature
            ToCanonicalPosmTableSignature(Posm2025DailyTableSignature value) =>
            new(value.RowCount, value.LastModifiedAt, value.CreatedAt, value.Checksum);

        private static Posm2025DailyTableSignature ToLegacyPosmTableSignature(
            global::BlazorApp.Api.Services.Posm2025DailyTableSignature value) =>
            new(value.RowCount, value.LastModifiedAt, value.CreatedAt, value.Checksum);

        private static global::BlazorApp.Api.Services.Posm2025DailySnapshotSignature
            ToCanonicalPosmSnapshotSignature(Posm2025DailySnapshotSignature value) =>
            new(
                value.Date,
                ToCanonicalPosmTableSignature(value.Orders),
                ToCanonicalPosmTableSignature(value.Details),
                ToCanonicalPosmTableSignature(value.Payments),
                ToCanonicalPosmTableSignature(value.SalesReturns)
            );

        private static Posm2025DailySnapshotSignature ToLegacyPosmSnapshotSignature(
            global::BlazorApp.Api.Services.Posm2025DailySnapshotSignature value) =>
            new(
                value.Date,
                ToLegacyPosmTableSignature(value.Orders),
                ToLegacyPosmTableSignature(value.Details),
                ToLegacyPosmTableSignature(value.Payments),
                ToLegacyPosmTableSignature(value.SalesReturns)
            );

        private static global::BlazorApp.Api.Services.ProductStoreDailySourceRow
            ToCanonicalProductStoreDailySourceRow(ProductStoreDailySourceRow value) =>
            new()
            {
                IsHBSalesSource = value.IsHBSalesSource,
                Date = value.Date,
                OrderGuid = value.OrderGuid,
                HBSalesOrderNumber = value.HBSalesOrderNumber,
                DetailGuid = value.DetailGuid,
                BranchCode = value.BranchCode,
                DeviceCode = value.DeviceCode,
                HBSalesMainLastModifiedAt = value.HBSalesMainLastModifiedAt,
                HBSalesMainCreatedAt = value.HBSalesMainCreatedAt,
                HBSalesDetailLastModifiedAt = value.HBSalesDetailLastModifiedAt,
                HBSalesDetailCreatedAt = value.HBSalesDetailCreatedAt,
                OrderLastUploadTime = value.OrderLastUploadTime,
                ProductCode = value.ProductCode,
                ItemNumber = value.ItemNumber,
                SupplierCode = value.SupplierCode,
                ProductName = value.ProductName,
                Barcode = value.Barcode,
                Quantity = value.Quantity,
                ActualAmount = value.ActualAmount,
                DetailLastUploadTime = value.DetailLastUploadTime,
                SourceCreatedAt = value.SourceCreatedAt,
                SourceUpdatedAt = value.SourceUpdatedAt,
                DocumentType = value.DocumentType,
            };

        private static ProductStoreDailySourceRow ToLegacyProductStoreDailySourceRow(
            global::BlazorApp.Api.Services.ProductStoreDailySourceRow value) =>
            new()
            {
                IsHBSalesSource = value.IsHBSalesSource,
                Date = value.Date,
                OrderGuid = value.OrderGuid,
                HBSalesOrderNumber = value.HBSalesOrderNumber,
                DetailGuid = value.DetailGuid,
                BranchCode = value.BranchCode,
                DeviceCode = value.DeviceCode,
                HBSalesMainLastModifiedAt = value.HBSalesMainLastModifiedAt,
                HBSalesMainCreatedAt = value.HBSalesMainCreatedAt,
                HBSalesDetailLastModifiedAt = value.HBSalesDetailLastModifiedAt,
                HBSalesDetailCreatedAt = value.HBSalesDetailCreatedAt,
                OrderLastUploadTime = value.OrderLastUploadTime,
                ProductCode = value.ProductCode,
                ItemNumber = value.ItemNumber,
                SupplierCode = value.SupplierCode,
                ProductName = value.ProductName,
                Barcode = value.Barcode,
                Quantity = value.Quantity,
                ActualAmount = value.ActualAmount,
                DetailLastUploadTime = value.DetailLastUploadTime,
                SourceCreatedAt = value.SourceCreatedAt,
                SourceUpdatedAt = value.SourceUpdatedAt,
                DocumentType = value.DocumentType,
            };

        private static global::BlazorApp.Api.Services.StoreStatisticPaymentRow
            ToCanonicalStoreStatisticPaymentRow(StoreStatisticPaymentRow value) =>
            new()
            {
                PaymentGuid = value.PaymentGuid,
                OrderGuid = value.OrderGuid,
                BranchCode = value.BranchCode,
                DeviceCode = value.DeviceCode,
                Amount = value.Amount,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt,
                LastUploadTime = value.LastUploadTime,
            };

        private static StoreStatisticPaymentRow ToLegacyStoreStatisticPaymentRow(
            global::BlazorApp.Api.Services.StoreStatisticPaymentRow value) =>
            new()
            {
                PaymentGuid = value.PaymentGuid,
                OrderGuid = value.OrderGuid,
                BranchCode = value.BranchCode,
                DeviceCode = value.DeviceCode,
                Amount = value.Amount,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt,
                LastUploadTime = value.LastUploadTime,
            };

        private static global::BlazorApp.Api.Services.StoreStatisticOrderRow
            ToCanonicalStoreStatisticOrderRow(StoreStatisticOrderRow value) =>
            new()
            {
                OrderGuid = value.OrderGuid,
                BranchCode = value.BranchCode,
                DeviceCode = value.DeviceCode,
                OrderTime = value.OrderTime,
                Status = value.Status,
                LastUploadTime = value.LastUploadTime,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt,
            };

        private static StoreStatisticOrderRow ToLegacyStoreStatisticOrderRow(
            global::BlazorApp.Api.Services.StoreStatisticOrderRow value) =>
            new()
            {
                OrderGuid = value.OrderGuid,
                BranchCode = value.BranchCode,
                DeviceCode = value.DeviceCode,
                OrderTime = value.OrderTime,
                Status = value.Status,
                LastUploadTime = value.LastUploadTime,
                CreatedAt = value.CreatedAt,
                UpdatedAt = value.UpdatedAt,
            };

        private static List<StoreCostRow> ToLegacyStoreCostRows(
            IReadOnlyList<global::BlazorApp.Api.Services.StoreCostRow> rows) =>
            SalesStatisticsCompatibilityAdapter.MapToList(
                rows,
                value => new StoreCostRow
                {
                    StoreCode = value.StoreCode,
                    SupplierCode = value.SupplierCode,
                    ProductCode = value.ProductCode,
                    PurchasePrice = value.PurchasePrice,
                }
            );

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

        private readonly SalesStatisticsApplicationCoordinator _application;
        private readonly IServiceScopeFactory? _serviceScopeFactory;

        /// <summary>保留既有公开构造签名；内部仅创建独立应用协调器。</summary>
        public SalesStatisticsJobService(
            POSMSqlSugarContext posmContext,
            SqlSugarContext context,
            ILogger<SalesStatisticsJobService> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            HBSalesRecordSqlSugarContext? hbSalesContext = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _application = new SalesStatisticsApplicationCoordinator(
                posmContext,
                context,
                logger,
                configuration,
                serviceScopeFactory,
                serviceProvider => serviceProvider.GetRequiredService<ISalesStatisticsRecalculationExecutor>(),
                hbSalesContext
            );
        }

        internal SalesStatisticsJobService(SalesStatisticsApplicationCoordinator application)
        {
            _application = application;
        }

        /// <summary>
        /// DI 由协调器构造门面时也必须保留持久队列作用域工厂；否则兼容重算入口会退化为不可用。
        /// </summary>
        internal SalesStatisticsJobService(
            SalesStatisticsApplicationCoordinator application,
            IServiceScopeFactory serviceScopeFactory)
        {
            _application = application;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public Task UpdateCurrentHourStatistics() => _application.UpdateCurrentHourStatistics();
        public Task UpdateDailyStatistics(string? dateStr = null) => _application.UpdateDailyStatistics(dateStr);
        public Task UpdateHourlyStatistics(DateTime date, int? hour = null) => _application.UpdateHourlyStatistics(date, hour);
        public Task UpdateStoreStatistics(DateTime? date = null) => _application.UpdateStoreStatistics(date);
        public Task FullRefreshPreviousDay() => _application.FullRefreshPreviousDay();
        public Task FullRefreshCurrentDay() => _application.FullRefreshCurrentDay();
        public Task UpdateStoreStatistics(DateTime date, List<string>? branchCodes = null) => _application.UpdateStoreStatistics(date, branchCodes);
        public Task UpdateSupplierStatistics(DateTime? startDate = null, DateTime? endDate = null, List<string>? supplierCodes = null) => _application.UpdateSupplierStatistics(startDate, endDate, supplierCodes);
        public Task UpdateProductStoreDailyStatistics(DateTime? date = null) => _application.UpdateProductStoreDailyStatistics(date);
        public Task ExecuteQueuedDateAsync(
            DateTime date,
            Guid expectedJobId,
            Func<Task> validateExecutionOwnershipAsync,
            CancellationToken cancellationToken) => _application.ExecuteQueuedDateAsync(
            date,
            expectedJobId,
            validateExecutionOwnershipAsync,
            cancellationToken);
        public async Task<HBSales2025BatchSnapshot> Load2025HBSalesBatchSnapshotAsync(IReadOnlyCollection<DateTime> dates) =>
            new(await _application.Load2025HBSalesBatchSnapshotAsync(dates));
        public async Task<Posm2025DailySnapshot> Load2025PosmDailySnapshotAsync(DateTime date) =>
            new(await _application.Load2025PosmDailySnapshotAsync(date));
        internal static async Task<List<StoreCostRow>> LoadStoreCostsInBatchesAsync(SqlSugarContext context, IReadOnlyCollection<string> productCodes, IReadOnlyCollection<string> branchCodes) =>
            ToLegacyStoreCostRows(
                await SalesStatisticsApplicationCoordinator.LoadStoreCostsInBatchesAsync(
                    context,
                    productCodes,
                    branchCodes
                )
            );
        internal static Task<Dictionary<string, string>> LoadPosmSupplierMappingInBatchesAsync(
            POSMSqlSugarContext posmContext,
            IEnumerable<string?> productCodes) =>
            SalesStatisticsProductStoreDailySourceReader.LoadPosmSupplierMappingInBatchesAsync(
                posmContext,
                productCodes);
        public Task Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(DateTime date, HBSales2025BatchSnapshot snapshot) =>
            _application.Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(date, snapshot.Canonical);
        public Task Finalize2025BatchSnapshotDateAsync(DateTime date) => _application.Finalize2025BatchSnapshotDateAsync(date);
        public Task Fail2025BatchSnapshotDatesAsync(IReadOnlyCollection<DateTime> dates, string errorMessage) => _application.Fail2025BatchSnapshotDatesAsync(dates, errorMessage);
        public async Task<ProductStoreDailyRecalculationSubmitResult> SubmitProductStoreDailyRecalculationAsync(IEnumerable<DateTime> dates, string? requestedBy, int maxConcurrency = 3)
        {
            using var scope = (_serviceScopeFactory ?? throw new InvalidOperationException("此兼容门面未配置持久任务队列作用域工厂")).CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IProductStoreDailyStatisticQueueService>()
                .EnqueueAsync(dates, requestedBy, maxConcurrency);
        }
        public async Task<int> RecoverTimedOutProductStoreDailyRecalculationJobsAsync(TimeSpan timeout, DateTime? nowUtc = null)
        {
            using var scope = (_serviceScopeFactory ?? throw new InvalidOperationException("此兼容门面未配置持久任务队列作用域工厂")).CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IProductStoreDailyStatisticQueueService>()
                .RecoverExpiredRunningClaimsAsync();
        }
        public Task RefreshRecentProductStoreDailyStatistics(int days = 7) => _application.RefreshRecentProductStoreDailyStatistics(days);
        public Task<BatchStatisticsUpdateResult> BatchUpdateStoreStatistics(DateTime startDate, DateTime endDate, List<string>? branchCodes = null) => _application.BatchUpdateStoreStatistics(startDate, endDate, branchCodes);
        public Task<BatchStatisticsUpdateResult> BatchUpdateSupplierStatistics(DateTime startDate, DateTime endDate, List<string>? supplierCodes = null) => _application.BatchUpdateSupplierStatistics(startDate, endDate, supplierCodes);
        public Task<BatchStatisticsUpdateResult> BatchUpdateSupplierStatisticsConcurrent(DateTime startDate, DateTime endDate, List<string>? supplierCodes = null, int? maxConcurrency = null) => _application.BatchUpdateSupplierStatisticsConcurrent(startDate, endDate, supplierCodes, maxConcurrency);
        public Task<BatchStatisticsUpdateResult> BatchUpdateDailyStatistics(DateTime startDate, DateTime endDate) => _application.BatchUpdateDailyStatistics(startDate, endDate);
        public Task<BatchStatisticsUpdateResult> BatchUpdateHourlyStatistics(DateTime startDate, DateTime endDate, int? hour = null) => _application.BatchUpdateHourlyStatistics(startDate, endDate, hour);
        public Task UpdateStoreSupplierStatistics(DateTime? date = null, List<string>? branchCodes = null, List<string>? supplierCodes = null) => _application.UpdateStoreSupplierStatistics(date, branchCodes, supplierCodes);
        public Task<BatchStatisticsUpdateResult> BatchUpdateStoreSupplierStatistics(DateTime startDate, DateTime endDate, List<string>? branchCodes = null, List<string>? supplierCodes = null) => _application.BatchUpdateStoreSupplierStatistics(startDate, endDate, branchCodes, supplierCodes);
        public Task<BatchStatisticsUpdateResult> BatchFullRefreshByMonths(string startYearMonth, string endYearMonth, int maxMonths = 12) => _application.BatchFullRefreshByMonths(startYearMonth, endYearMonth, maxMonths);
        public Task<BatchStatisticsUpdateResult> BatchFullRefreshConcurrent(DateTime startDate, DateTime endDate, int? maxConcurrency = null) => _application.BatchFullRefreshConcurrent(startDate, endDate, maxConcurrency);
        public Task UpdateAustralianSupplierStoreStatistics(DateTime? date = null, List<string>? branchCodes = null, List<string>? supplierCodes = null) => _application.UpdateAustralianSupplierStoreStatistics(date, branchCodes, supplierCodes);
        public Task UpdateChinaSupplierStoreStatistics(DateTime? date = null, List<string>? branchCodes = null, List<string>? supplierCodes = null) => _application.UpdateChinaSupplierStoreStatistics(date, branchCodes, supplierCodes);
        internal Task<IReadOnlyList<ProductStoreDailyBranchRollup>> GetProductStoreDailyReturnAdjustmentsAsync(DateTime date) => _application.GetProductStoreDailyReturnAdjustmentsAsync(date);
        internal Task MarkProductStatisticJobRunningAsync(Guid jobId, DateTime date) => Task.CompletedTask;

        // 保留既有非公开反射入口，但只委派至垂直切片，避免 façade 重新承载 SQL 或事务实现。
        private static Task ExecuteTransactionSafelyAsync(
            Func<Task> beginAsync,
            Func<Task> workAsync,
            Func<Task> commitAsync,
            Func<Task> rollbackAsync,
            ILogger logger,
            string operationName) => SalesStatisticsDailyHourlySlice.ExecuteTransactionSafelyAsync(
                beginAsync, workAsync, commitAsync, rollbackAsync, logger, operationName);

        private static int NormalizeProductStatisticMaxConcurrency(int maxConcurrency) =>
            SalesStatisticsProductStoreDailyEntrySlice.NormalizeProductStatisticMaxConcurrency(maxConcurrency);

        private static int ResolveProductStatisticMaxConcurrency(
            IReadOnlyCollection<DateTime> dates,
            int maxConcurrency) => SalesStatisticsProductStoreDailyEntrySlice.ResolveProductStatisticMaxConcurrency(
                dates, maxConcurrency);

        private static int ResolveFullRefreshMaxConcurrency(
            DateTime startDate,
            DateTime endDate,
            int configuredConcurrency) => SalesStatisticsOrchestrationSlice.ResolveFullRefreshMaxConcurrency(
                startDate, endDate, configuredConcurrency);

        private static Task<DateTime?> QueryDailySourceWatermarkAsync(
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            DateTime date,
            IReadOnlyCollection<ProductStoreDailySourceRow>? preloadedHBSalesRows = null) =>
            SalesStatisticsProductStoreDailyStateSlice.QueryDailySourceWatermarkAsync(
                posmContext,
                hbSalesContext,
                date,
                SalesStatisticsCompatibilityAdapter.ToCanonicalRows(
                    preloadedHBSalesRows,
                    ToCanonicalProductStoreDailySourceRow
                ));

        private Task Update2025StoreAndProductStatisticsAtomically(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            ILogger logger,
            DateTime date,
            DateTime? sourceWatermarkOverride = null,
            IReadOnlyList<ProductStoreDailySourceRow>? preloadedHBSalesRows = null,
            HBSales2025DailySnapshotSignature? expectedHBSalesSignature = null,
            bool deferHBSalesStabilityToBatchEnd = false,
            Posm2025DailySnapshot? preloadedPosmSnapshot = null,
            Guid? expectedJobId = null,
            Func<Task>? validateExecutionOwnershipAsync = null) =>
            _application.Update2025StoreAndProductStatisticsAtomicallyAsync(
                context, posmContext, hbSalesContext, logger, date, sourceWatermarkOverride,
                SalesStatisticsCompatibilityAdapter.ToCanonicalRows(
                    preloadedHBSalesRows,
                    ToCanonicalProductStoreDailySourceRow
                ),
                expectedHBSalesSignature == null
                    ? null
                    : ToCanonicalHBSalesSignature(expectedHBSalesSignature),
                deferHBSalesStabilityToBatchEnd,
                preloadedPosmSnapshot?.Canonical,
                expectedJobId,
                validateExecutionOwnershipAsync);

        private Task<bool> RunLeasedProductStoreDailyRefreshAsync(DateTime date) =>
            _application.RunLeasedProductStoreDailyRefreshAsync(date);

        private Task UpdateStoreStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            HBSalesRecordSqlSugarContext? hbSalesContext,
            ILogger logger,
            DateTime date,
            List<string>? branchCodes) => _application.UpdateStoreStatisticsWithContext(
                context, posmContext, hbSalesContext, logger, date, branchCodes);

        private Task UpdateAustralianSupplierStoreStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime? date,
            List<string>? branchCodes,
            List<string>? supplierCodes) =>
            _application.UpdateAustralianSupplierStoreStatisticsWithContext(
                context, posmContext, logger, date, branchCodes, supplierCodes);

    }
}
