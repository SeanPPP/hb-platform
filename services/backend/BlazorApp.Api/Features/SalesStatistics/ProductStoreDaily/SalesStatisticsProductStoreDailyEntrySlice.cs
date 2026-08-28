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
    /// <summary>销售统计垂直切片：SalesStatisticsProductStoreDailyEntrySlice。</summary>
    internal sealed class SalesStatisticsProductStoreDailyEntrySlice : SalesStatisticsSliceBase
    {
        private readonly SalesStatisticsProductStoreDailyRefreshSlice _productRefresh;

        public SalesStatisticsProductStoreDailyEntrySlice(
            SalesStatisticsSliceContext shared,
            SalesStatisticsProductStoreDailyRefreshSlice productRefresh)
            : base(shared)
        {
            _productRefresh = productRefresh;
        }

    public async Task UpdateProductStoreDailyStatistics(DateTime? date = null)
    {
        var targetDate = (date ?? DateTime.Now.Date).Date;
        if (targetDate.Year == 2025)
        {
            // 2025 双来源统计必须同时切换两张日表，不能留下新旧口径混合的中间状态。
            await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                _context,
                _posmContext,
                GetHBSalesContextFor2025(targetDate)!,
                _logger,
                targetDate
            );
            return;
        }
        await _productRefresh.UpdateProductStoreDailyStatisticsWithContext(
            _context,
            _posmContext,
            GetHBSalesContextFor2025(targetDate),
            _logger,
            targetDate
        );
    }

    /// <summary>
    /// 持久队列 worker 的执行入口。实际写入前后都保留 owner 校验，并把 JobId 传入写入切片完成数据库 fencing。
    /// </summary>
    internal async Task ExecuteQueuedDateAsync(
        DateTime date,
        Guid expectedJobId,
        Func<Task> validateExecutionOwnershipAsync,
        CancellationToken cancellationToken)
    {
        if (expectedJobId == Guid.Empty)
            throw new ArgumentException("队列任务 JobId 不能为空", nameof(expectedJobId));
        ArgumentNullException.ThrowIfNull(validateExecutionOwnershipAsync);
        cancellationToken.ThrowIfCancellationRequested();
        await validateExecutionOwnershipAsync();

        async Task ValidateBeforeCommitAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            await validateExecutionOwnershipAsync();
        }

        var targetDate = date.Date;
        if (targetDate.Year == 2025)
        {
            await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                _context,
                _posmContext,
                GetHBSalesContextFor2025(targetDate)!,
                _logger,
                targetDate,
                expectedJobId: expectedJobId,
                validateExecutionOwnershipAsync: ValidateBeforeCommitAsync);
            return;
        }

        await _productRefresh.UpdateProductStoreDailyStatisticsWithContext(
            _context,
            _posmContext,
            GetHBSalesContextFor2025(targetDate),
            _logger,
            targetDate,
            expectedJobId: expectedJobId,
            validateExecutionOwnershipBeforeCommitAsync: ValidateBeforeCommitAsync);
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

        var rows = await SalesStatisticsProductStoreDailySourceReader
            .LoadHBSalesProductStoreDailyRowsAsync(
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
            entry => SalesStatisticsProductStoreDailyDomainRules
                .CreateHBSales2025DailySnapshotSignature(entry.Key, entry.Value)
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
    internal static async Task<Posm2025DailySnapshot> Load2025PosmDailySnapshotAsync(
        POSMSqlSugarContext posmContext,
        DateTime date) =>
        await SalesStatisticsProductStoreDailySourceQueries.Load2025PosmDailySnapshotAsync(
            posmContext,
            date
        );

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
        var posmSnapshot = await SalesStatisticsProductStoreDailySourceQueries
            .Load2025PosmDailySnapshotAsync(_posmContext, targetDate);
        _logger.LogInformation(
            "2025 Runner POSM snapshot load 完成: {Date}, {ElapsedMilliseconds}ms, orders={Orders}, details={Details}, payments={Payments}, returns={Returns}",
            targetDate,
            snapshotStopwatch.ElapsedMilliseconds,
            posmSnapshot.Signature.Orders.RowCount,
            posmSnapshot.Signature.Details.RowCount,
            posmSnapshot.Signature.Payments.RowCount,
            posmSnapshot.Signature.SalesReturns.RowCount
        );
        await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
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
        await SalesStatisticsTransactionExecutor.ExecuteAsync(
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

    internal async Task Fail2025BatchSnapshotDatesSequentiallyAsync(
        IReadOnlyCollection<DateTime> dates,
        string errorMessage
    )
    {
        foreach (var date in dates.Select(date => date.Date).Distinct().OrderBy(date => date))
        {
            await _productRefresh.Persist2025AtomicFailureStatesAsync(
                _context,
                _logger,
                date,
                null,
                new InvalidOperationException(errorMessage)
            );
        }
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

    internal static int NormalizeProductStatisticMaxConcurrency(int maxConcurrency) =>
        maxConcurrency < 1 ? 3 : Math.Min(maxConcurrency, 10);

    internal static int ResolveProductStatisticMaxConcurrency(
        IReadOnlyCollection<DateTime> dates,
        int maxConcurrency) =>
        dates.Any(date => date.Year == 2025)
            ? 1
            : NormalizeProductStatisticMaxConcurrency(maxConcurrency);
    }
}
