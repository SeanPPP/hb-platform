using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services;

/// <summary>
/// 销售统计应用层组合根。它按单向依赖顺序组装切片，并为兼容门面与窄刷新端口提供委派目标。
/// </summary>
internal sealed class SalesStatisticsApplicationCoordinator :
    ISalesStatisticsRefreshOperations,
    ISalesStatisticsRecalculationExecutor
{
    private readonly POSMSqlSugarContext _posmContext;
    private readonly SalesStatisticsProductStoreDailySupportSlice _productSupport;
    private readonly SalesStatisticsProductStoreDailyRefreshSlice _productRefresh;
    private readonly SalesStatisticsProductStoreDailyEntrySlice _productEntry;
    private readonly SalesStatisticsOrchestrationSlice _orchestration;
    private readonly SalesStatisticsStoreDailySlice _storeDaily;
    private readonly SalesStatisticsSupplierStoreSlice _supplierStore;
    private readonly SalesStatisticsDailyHourlySlice _dailyHourly;
    private readonly SalesStatisticsSupplierSlice _supplier;
    private readonly SalesStatisticsSupplierBatchSlice _supplierBatch;
    private readonly SalesStatisticsStoreSupplierSlice _storeSupplier;

    public SalesStatisticsApplicationCoordinator(
        POSMSqlSugarContext posmContext,
        SqlSugarContext context,
        ILogger<SalesStatisticsApplicationCoordinator> logger,
        IConfiguration configuration,
        IServiceScopeFactory serviceScopeFactory,
        HBSalesRecordSqlSugarContext? hbSalesContext = null)
        : this(
            posmContext,
            context,
            logger,
            configuration,
            serviceScopeFactory,
            serviceProvider => serviceProvider.GetRequiredService<ISalesStatisticsRecalculationExecutor>(),
            hbSalesContext)
    {
    }

    internal SalesStatisticsApplicationCoordinator(
        POSMSqlSugarContext posmContext,
        SqlSugarContext context,
        ILogger logger,
        IConfiguration configuration,
        IServiceScopeFactory serviceScopeFactory,
        Func<IServiceProvider, ISalesStatisticsRecalculationExecutor> resolveRecalculationExecutor,
        HBSalesRecordSqlSugarContext? hbSalesContext = null)
    {
        _posmContext = posmContext;
        var shared = new SalesStatisticsSliceContext(
            posmContext,
            context,
            logger,
            configuration,
            serviceScopeFactory,
            resolveRecalculationExecutor,
            hbSalesContext,
            configuration.GetValue<int>("ScheduledTasks:MaxConcurrentUpdates", 5),
            configuration.GetValue<int>("ScheduledTasks:MaxDaysForConcurrentUpdate", 365),
            configuration.GetValue<int>("ScheduledTasks:MaxDaysPerChunk", 7)
        );

        // 先组装叶子切片，再逐层组装调用方；任何切片都不会反向持有协调器或兼容门面。
        _productSupport = new SalesStatisticsProductStoreDailySupportSlice(shared);
        _productRefresh = new SalesStatisticsProductStoreDailyRefreshSlice(shared, _productSupport);
        _productEntry = new SalesStatisticsProductStoreDailyEntrySlice(shared, _productRefresh);
        _orchestration = new SalesStatisticsOrchestrationSlice(
            shared,
            _productRefresh,
            _productSupport
        );
        _storeDaily = new SalesStatisticsStoreDailySlice(
            shared,
            _productRefresh,
            _productSupport,
            _orchestration
        );
        _supplierStore = new SalesStatisticsSupplierStoreSlice(shared);
        _dailyHourly = new SalesStatisticsDailyHourlySlice(shared, _storeDaily, _supplierStore);
        _supplier = new SalesStatisticsSupplierSlice(shared);
        _supplierBatch = new SalesStatisticsSupplierBatchSlice(
            shared,
            _storeDaily,
            _supplier,
            _dailyHourly
        );
        _storeSupplier = new SalesStatisticsStoreSupplierSlice(shared);
    }

    internal Task UpdateCurrentHourStatistics() => _dailyHourly.UpdateCurrentHourStatistics();

    internal Task UpdateDailyStatistics(string? dateStr = null) =>
        _dailyHourly.UpdateDailyStatistics(dateStr);

    internal Task UpdateHourlyStatistics(DateTime date, int? hour = null) =>
        _dailyHourly.UpdateHourlyStatistics(date, hour);

    internal Task UpdateStoreStatistics(DateTime? date = null) =>
        _storeDaily.UpdateStoreStatistics(date);

    internal Task FullRefreshPreviousDay() => _storeDaily.FullRefreshPreviousDay();

    internal Task FullRefreshCurrentDay() => _storeDaily.FullRefreshCurrentDay();

    internal Task UpdateStoreStatistics(DateTime date, List<string>? branchCodes = null) =>
        _storeDaily.UpdateStoreStatistics(date, branchCodes);

    internal Task UpdateSupplierStatistics(
        DateTime? startDate = null,
        DateTime? endDate = null,
        List<string>? supplierCodes = null) =>
        _supplier.UpdateSupplierStatistics(startDate, endDate, supplierCodes);

    internal Task UpdateProductStoreDailyStatistics(DateTime? date = null) =>
        _productEntry.UpdateProductStoreDailyStatistics(date);

    internal Task ExecuteQueuedDateAsync(
        DateTime date,
        Guid expectedJobId,
        Func<Task> validateExecutionOwnershipAsync,
        CancellationToken cancellationToken) =>
        _productEntry.ExecuteQueuedDateAsync(
            date,
            expectedJobId,
            validateExecutionOwnershipAsync,
            cancellationToken);

    internal Task<HBSales2025BatchSnapshot> Load2025HBSalesBatchSnapshotAsync(
        IReadOnlyCollection<DateTime> dates) =>
        _productEntry.Load2025HBSalesBatchSnapshotAsync(dates);

    internal Task<Posm2025DailySnapshot> Load2025PosmDailySnapshotAsync(DateTime date) =>
        SalesStatisticsProductStoreDailyEntrySlice.Load2025PosmDailySnapshotAsync(
            _posmContext,
            date
        );

    internal static Task<List<StoreCostRow>> LoadStoreCostsInBatchesAsync(
        SqlSugarContext context,
        IReadOnlyCollection<string> productCodes,
        IReadOnlyCollection<string> branchCodes) =>
        SalesStatisticsProductStoreDailyRefreshSlice.LoadStoreCostsInBatchesAsync(
            context,
            productCodes,
            branchCodes
        );

    internal Task Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(
        DateTime date,
        HBSales2025BatchSnapshot snapshot) =>
        _productEntry.Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(date, snapshot);

    internal Task Finalize2025BatchSnapshotDateAsync(DateTime date) =>
        _productEntry.Finalize2025BatchSnapshotDateAsync(date);

    internal Task Fail2025BatchSnapshotDatesAsync(
        IReadOnlyCollection<DateTime> dates,
        string errorMessage) =>
        _productEntry.Fail2025BatchSnapshotDatesAsync(dates, errorMessage);

    internal Task RefreshRecentProductStoreDailyStatistics(int days = 7) =>
        _productEntry.RefreshRecentProductStoreDailyStatistics(days);

    internal Task<BatchStatisticsUpdateResult> BatchUpdateStoreStatistics(
        DateTime startDate,
        DateTime endDate,
        List<string>? branchCodes = null) =>
        _supplierBatch.BatchUpdateStoreStatistics(startDate, endDate, branchCodes);

    internal Task<BatchStatisticsUpdateResult> BatchUpdateSupplierStatistics(
        DateTime startDate,
        DateTime endDate,
        List<string>? supplierCodes = null) =>
        _supplierBatch.BatchUpdateSupplierStatistics(startDate, endDate, supplierCodes);

    internal Task<BatchStatisticsUpdateResult> BatchUpdateSupplierStatisticsConcurrent(
        DateTime startDate,
        DateTime endDate,
        List<string>? supplierCodes = null,
        int? maxConcurrency = null) =>
        _supplierBatch.BatchUpdateSupplierStatisticsConcurrent(
            startDate,
            endDate,
            supplierCodes,
            maxConcurrency
        );

    internal Task<BatchStatisticsUpdateResult> BatchUpdateDailyStatistics(
        DateTime startDate,
        DateTime endDate) =>
        _supplierBatch.BatchUpdateDailyStatistics(startDate, endDate);

    internal Task<BatchStatisticsUpdateResult> BatchUpdateHourlyStatistics(
        DateTime startDate,
        DateTime endDate,
        int? hour = null) =>
        _supplierBatch.BatchUpdateHourlyStatistics(startDate, endDate, hour);

    internal Task UpdateStoreSupplierStatistics(
        DateTime? date = null,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null) =>
        _storeSupplier.UpdateStoreSupplierStatistics(date, branchCodes, supplierCodes);

    internal Task<BatchStatisticsUpdateResult> BatchUpdateStoreSupplierStatistics(
        DateTime startDate,
        DateTime endDate,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null) =>
        _storeSupplier.BatchUpdateStoreSupplierStatistics(
            startDate,
            endDate,
            branchCodes,
            supplierCodes
        );

    internal Task<BatchStatisticsUpdateResult> BatchFullRefreshByMonths(
        string startYearMonth,
        string endYearMonth,
        int maxMonths = 12) =>
        _orchestration.BatchFullRefreshByMonths(startYearMonth, endYearMonth, maxMonths);

    internal Task<BatchStatisticsUpdateResult> BatchFullRefreshConcurrent(
        DateTime startDate,
        DateTime endDate,
        int? maxConcurrency = null) =>
        _orchestration.BatchFullRefreshConcurrent(startDate, endDate, maxConcurrency);

    internal Task UpdateAustralianSupplierStoreStatistics(
        DateTime? date = null,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null) =>
        _supplierStore.UpdateAustralianSupplierStoreStatistics(date, branchCodes, supplierCodes);

    internal Task UpdateChinaSupplierStoreStatistics(
        DateTime? date = null,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null) =>
        _supplierStore.UpdateChinaSupplierStoreStatistics(date, branchCodes, supplierCodes);

    internal Task<IReadOnlyList<ProductStoreDailyBranchRollup>> GetProductStoreDailyReturnAdjustmentsAsync(
        DateTime date) =>
        _productSupport.GetProductStoreDailyReturnAdjustmentsAsync(date);

    internal Task Update2025StoreAndProductStatisticsAtomicallyAsync(
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
        _productRefresh.Update2025StoreAndProductStatisticsAtomically(
            context,
            posmContext,
            hbSalesContext,
            logger,
            date,
            sourceWatermarkOverride,
            preloadedHBSalesRows,
            expectedHBSalesSignature,
            deferHBSalesStabilityToBatchEnd,
            preloadedPosmSnapshot,
            expectedJobId,
            validateExecutionOwnershipAsync
        );

    internal Task<bool> RunLeasedProductStoreDailyRefreshAsync(DateTime date) =>
        _storeDaily.RunLeasedProductStoreDailyRefreshAsync(date);

    internal Task UpdateStoreStatisticsWithContext(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        HBSalesRecordSqlSugarContext? hbSalesContext,
        ILogger logger,
        DateTime date,
        List<string>? branchCodes) =>
        _orchestration.UpdateStoreStatisticsWithContext(
            context,
            posmContext,
            hbSalesContext,
            logger,
            date,
            branchCodes
        );

    internal Task UpdateAustralianSupplierStoreStatisticsWithContext(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        ILogger logger,
        DateTime? date,
        List<string>? branchCodes,
        List<string>? supplierCodes) =>
        SalesStatisticsSupplierStoreSlice.UpdateAustralianSupplierStoreStatisticsWithContext(
            context,
            posmContext,
            logger,
            date,
            branchCodes,
            supplierCodes
        );

    Task ISalesStatisticsRefreshOperations.UpdateStoreStatisticsAsync(
        DateTime date,
        List<string>? branchCodes) =>
        _storeDaily.UpdateStoreStatistics(date, branchCodes);

    Task ISalesStatisticsRefreshOperations.UpdateHourlyStatisticsAsync(
        DateTime date,
        int? hour) =>
        _dailyHourly.UpdateHourlyStatistics(date, hour);

    Task ISalesStatisticsRecalculationExecutor.MarkProductStatisticJobRunningAsync(
        Guid jobId,
        DateTime date) =>
        Task.CompletedTask;

    Task ISalesStatisticsRecalculationExecutor.UpdateProductStoreDailyStatisticsAsync(
        DateTime? date) =>
        _productEntry.UpdateProductStoreDailyStatistics(date);
}
