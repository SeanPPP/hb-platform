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

namespace BlazorApp.Api.Services
{
    /// <summary>销售统计垂直切片：SalesStatisticsProductStoreDailyRefreshSlice。</summary>
    internal sealed class SalesStatisticsProductStoreDailyRefreshSlice : SalesStatisticsSliceBase
    {
        private readonly SalesStatisticsProductStoreDailySupportSlice _productSupport;

        public SalesStatisticsProductStoreDailyRefreshSlice(
            SalesStatisticsSliceContext shared,
            SalesStatisticsProductStoreDailySupportSlice productSupport)
            : base(shared)
        {
            _productSupport = productSupport;
        }

    internal async Task UpdateProductStoreDailyStatisticsWithContext(
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
        Posm2025DailySnapshot? preloadedPosmSnapshot = null,
        Guid? expectedJobId = null,
        Func<Task>? validateExecutionOwnershipBeforeCommitAsync = null
    )
    {
        var targetDate = date.Date;
        var originalCommandTimeout = context.Db.Ado.CommandTimeOut;
        context.Db.Ado.CommandTimeOut = Math.Max(originalCommandTimeout, CommandTimeoutSeconds);
        try
        {
            logger.LogInformation("开始更新商品分店每日统计: {Date}", targetDate);
            var input = await new SalesStatisticsProductStoreDailySourceReader().LoadAsync(
                context, posmContext, hbSalesContext, logger, targetDate,
                preloadedHBSalesRows, preloadedPosmSnapshot);
            var build = new SalesStatisticsProductStoreDailyBuilder().Build(input);
            var status = await new SalesStatisticsProductStoreDailyCommandWriter().PersistAsync(
                context, logger, input, build, atomicStoreStatistics, sourceWatermarkOverride,
                validateSourceWatermarkBeforeCommitAsync,
                atomicSuccessStatusOverride,
                expectedJobId,
                validateExecutionOwnershipBeforeCommitAsync);
            logger.LogInformation(
                "商品分店每日统计更新完成: {Date}, 总记录: {Total}, 状态: {Status}",
                targetDate, build.Statistics.Count, status.Status);
        }
        catch (Exception ex)
        {
            if (atomicStoreStatistics == null)
            {
                try
                {
                    await PersistProductStatisticFailureAsync(
                        context,
                        logger,
                        targetDate,
                        ex,
                        expectedJobId,
                        validateExecutionOwnershipBeforeCommitAsync);
                }
                catch (Exception stateException)
                {
                    // 失败状态无法持久化时仅记录附加错误，不能覆盖原始统计异常。
                    logger.LogError(stateException, "写入商品分店每日统计失败状态失败: {Date}", targetDate);
                }
            }
            else
            {
                // 2025 原子入口统一由外层成对写入失败状态，避免内外层重复或单边落状态。
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

    // 拆分迁移期间保留旧实现作为行为对照；下一步删除其重复逻辑。
    internal async Task Update2025StoreAndProductStatisticsAtomically(
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
        Func<Task>? validateExecutionOwnershipAsync = null
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
                ?? await SalesStatisticsProductStoreDailySourceReader
                    .LoadHBSalesProductStoreDailyRowsAsync(
                    requiredHBSalesContext,
                    targetDate,
                    targetDate.AddDays(1)
                );
            if (expectedHBSalesSignature != null)
            {
                var actualSignature = SalesStatisticsProductStoreDailyDomainRules
                    .CreateHBSales2025DailySnapshotSignature(
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
                ? await SalesStatisticsProductStoreDailyStateSlice
                    .QueryDailyPosmSourceWatermarkAsync(posmContext, targetDate)
                : SalesStatisticsProductStoreDailyDomainRules.GetPosmSnapshotWatermark(
                    preloadedPosmSnapshot
                );
            var preSourceWatermark = SalesStatisticsProductStoreDailyDomainRules.GetLatestSourceTime(
                prePosmSourceWatermark,
                SalesStatisticsProductStoreDailyDomainRules.GetHBSalesSourceWatermark(hbSalesRows)
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
            var storeStatistics = await _productSupport.BuildStoreStatisticsAsync(
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
                        var postPosmSnapshot = await SalesStatisticsProductStoreDailySourceQueries
                            .Load2025PosmDailySnapshotAsync(posmContext, targetDate);
                        if (postPosmSnapshot.Signature != preloadedPosmSnapshot.Signature)
                        {
                            throw new InvalidOperationException(
                                $"2025 Runner POSM 日快照签名发生变化，拒绝提交: {targetDate:yyyy-MM-dd}"
                            );
                        }
                    }
                    var postSourceWatermark = deferHBSalesStabilityToBatchEnd
                        // 批量路径把 HBSales 放到批末一次复核；日内仍必须独立复核 POSM。
                        ? await SalesStatisticsProductStoreDailyStateSlice
                            .QueryDailyPosmSourceWatermarkAsync(posmContext, targetDate)
                        : await SalesStatisticsProductStoreDailyStateSlice.QueryDailySourceWatermarkAsync(
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
                preloadedPosmSnapshot,
                expectedJobId,
                validateExecutionOwnershipAsync
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
                ex,
                expectedJobId,
                validateExecutionOwnershipAsync
            );
            throw;
        }
    }

    internal async Task Persist2025AtomicFailureStatesAsync(
        SqlSugarContext context,
        ILogger logger,
        DateTime targetDate,
        DateTime? effectiveSourceWatermark,
        Exception originalException,
        Guid? expectedJobId = null,
        Func<Task>? validateExecutionOwnershipAsync = null
    )
    {
        try
        {
            // 主统计事务已回滚或尚未开始；此处独立事务保证两类 Failed 状态成对提交。
            await SalesStatisticsTransactionExecutor.ExecuteAsync(
                beginAsync: () => context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    await SalesStatisticsProductStoreDailyStateSlice.FenceProductStatisticExecutionOwnerAsync(
                        context,
                        targetDate,
                        expectedJobId);
                    if (validateExecutionOwnershipAsync != null)
                        await validateExecutionOwnershipAsync();
                    await SalesStatisticsProductStoreDailyStateSlice.UpsertProductStatisticStateAsync(
                        context,
                        targetDate,
                        new SalesStatisticsProductStoreDailyStateSlice.ProductStatisticStatusResult(
                            SalesStatisticRefreshStatus.Failed,
                            originalException.Message
                        ),
                        effectiveSourceWatermark,
                        overwriteLastSourceUploadTime: true
                    );
                    await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
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

    private static Task PersistProductStatisticFailureAsync(
        SqlSugarContext context,
        ILogger logger,
        DateTime targetDate,
        Exception originalException,
        Guid? expectedJobId,
        Func<Task>? validateExecutionOwnershipAsync)
    {
        if (!expectedJobId.HasValue)
        {
            return SalesStatisticsProductStoreDailyStateSlice.UpsertProductStatisticStateAsync(
                context,
                targetDate,
                new SalesStatisticsProductStoreDailyStateSlice.ProductStatisticStatusResult(
                    SalesStatisticRefreshStatus.Failed,
                    originalException.Message),
                null);
        }

        return SalesStatisticsTransactionExecutor.ExecuteAsync(
            beginAsync: () => context.Db.Ado.BeginTranAsync(),
            workAsync: async () =>
            {
                await SalesStatisticsProductStoreDailyStateSlice.FenceProductStatisticExecutionOwnerAsync(
                    context, targetDate, expectedJobId);
                if (validateExecutionOwnershipAsync != null)
                    await validateExecutionOwnershipAsync();
                await SalesStatisticsProductStoreDailyStateSlice.UpsertProductStatisticStateAsync(
                    context,
                    targetDate,
                    new SalesStatisticsProductStoreDailyStateSlice.ProductStatisticStatusResult(
                        SalesStatisticRefreshStatus.Failed,
                        originalException.Message),
                    null);
            },
            commitAsync: () => context.Db.Ado.CommitTranAsync(),
            rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
            logger: logger,
            operationName: "商品分店每日统计失败状态写入");
    }

    internal static string ResolveBranchCode(
        string? branchCode,
        string? deviceCode,
        Dictionary<string, string> deviceBranchMap
    ) => SalesStatisticsCodeRules.ResolveBranchCode(branchCode, deviceCode, deviceBranchMap);

    internal static DateTime? GetPosmSnapshotWatermark(Posm2025DailySnapshot snapshot) =>
        SalesStatisticsProductStoreDailyDomainRules.GetPosmSnapshotWatermark(snapshot);

    internal static Task<List<StoreCostRow>> LoadStoreCostsInBatchesAsync(
        SqlSugarContext context,
        IReadOnlyCollection<string> productCodes,
        IReadOnlyCollection<string> branchCodes
    ) => SalesStatisticsProductStoreDailySourceReader.LoadStoreCostsInBatchesAsync(
        context, productCodes, branchCodes
    );
    internal static Task<List<ProductStoreDailySourceRow>> LoadHBSalesProductStoreDailyRowsAsync(
        HBSalesRecordSqlSugarContext hbSalesContext,
        DateTime targetDate,
        DateTime nextDate,
        int? maxRows = null
    ) => SalesStatisticsProductStoreDailySourceReader.LoadHBSalesProductStoreDailyRowsAsync(
        hbSalesContext, targetDate, nextDate, maxRows
    );
    internal static HBSales2025DailySnapshotSignature CreateHBSales2025DailySnapshotSignature(
        DateTime date,
        IEnumerable<ProductStoreDailySourceRow> rows
    ) => SalesStatisticsProductStoreDailyDomainRules
        .CreateHBSales2025DailySnapshotSignature(date, rows);

    internal static void AppendHBSalesSignatureValue(IncrementalHash checksum, object? value) =>
        SalesStatisticsProductStoreDailyDomainRules.AppendSignatureValue(checksum, value);

    internal static Posm2025DailySnapshotSignature CreatePosm2025DailySnapshotSignature(
        DateTime date,
        IEnumerable<StoreStatisticOrderRow> orders,
        IEnumerable<ProductStoreDailySourceRow> details,
        IEnumerable<StoreStatisticPaymentRow> payments,
        IEnumerable<ProductStoreDailySourceRow> salesReturns
    ) => SalesStatisticsProductStoreDailyDomainRules.CreatePosm2025DailySnapshotSignature(
        date,
        orders,
        details,
        payments,
        salesReturns
    );

    internal static Posm2025DailyTableSignature CreatePosmTableSignature<T>(
        IReadOnlyList<T> rows,
        Func<T, DateTime?> lastModifiedSelector,
        Func<T, DateTime?> createdSelector,
        Func<T, object?[]> valuesSelector
    ) => SalesStatisticsProductStoreDailyDomainRules.CreatePosmTableSignature(
        rows,
        lastModifiedSelector,
        createdSelector,
        valuesSelector
    );

    internal static Task ResolveMissingHBSalesProductCodesAsync(
        SqlSugarContext context,
        IReadOnlyList<ProductStoreDailySourceRow> hbSalesRows
    ) => SalesStatisticsProductStoreDailySourceReader.ResolveMissingHBSalesProductCodesAsync(
        context, hbSalesRows
    );
    internal static string SelectDeterministicProductCode(IEnumerable<string> candidates) =>
        SalesStatisticsProductStoreDailyDomainRules.SelectDeterministicProductCode(candidates);

    internal static DateTime? GetLatestSourceTime(params DateTime?[] timestamps) =>
        SalesStatisticsProductStoreDailyDomainRules.GetLatestSourceTime(timestamps);

    internal static DateTime? GetLatestSourceTime(IEnumerable<ProductStoreDailySourceRow> rows) =>
        SalesStatisticsProductStoreDailyDomainRules.GetLatestSourceTime(rows);

    internal static DateTime? GetHBSalesSourceWatermark(
        IEnumerable<ProductStoreDailySourceRow> hbSalesRows
    ) => SalesStatisticsProductStoreDailyDomainRules.GetHBSalesSourceWatermark(hbSalesRows);

    internal static async Task<List<HBSalesStoreAggregateRow>> LoadHBSalesStoreAggregatesAsync(
        HBSalesRecordSqlSugarContext hbSalesContext,
        DateTime targetDate,
        DateTime nextDate
    ) => await SalesStatisticsProductStoreDailySourceQueries.LoadHBSalesStoreAggregatesAsync(
        hbSalesContext,
        targetDate,
        nextDate
    );

    internal static List<HBSalesStoreAggregateRow> BuildHBSalesStoreAggregates(
        IReadOnlyList<ProductStoreDailySourceRow> hbSalesRows
    ) => SalesStatisticsProductStoreDailyDomainRules.BuildHBSalesStoreAggregates(hbSalesRows);

    }
}
