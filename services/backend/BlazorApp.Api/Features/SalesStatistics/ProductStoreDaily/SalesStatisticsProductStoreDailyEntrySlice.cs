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
        private readonly SalesStatisticsOrchestrationSlice _orchestration;

        public SalesStatisticsProductStoreDailyEntrySlice(
            SalesStatisticsSliceContext shared,
            SalesStatisticsProductStoreDailyRefreshSlice productRefresh,
            SalesStatisticsOrchestrationSlice orchestration)
            : base(shared)
        {
            _productRefresh = productRefresh;
            _orchestration = orchestration;
        }

    public async Task UpdateProductStoreDailyStatistics(DateTime? date = null)
    {
        var targetDate = (date ?? SalesStatisticsBusinessDate.Today()).Date;
        if (SalesStatisticsHBSalesHistoryWindow.Includes(targetDate))
        {
            // HBSales 历史窗口的双来源统计必须同时切换两张日表，不能留下新旧口径混合的中间状态。
            await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                _context,
                _posmContext,
                GetHBSalesContextForVerifiedHistory(targetDate)!,
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
        if (SalesStatisticsHBSalesHistoryWindow.Includes(targetDate))
        {
            await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                _context,
                _posmContext,
                GetHBSalesContextForVerifiedHistory(targetDate)!,
                _logger,
                targetDate,
                expectedJobId: expectedJobId,
                validateExecutionOwnershipAsync: ValidateBeforeCommitAsync);
            return;
        }

        if (SalesStatisticsBusinessDate.IsToday(targetDate))
        {
            // 当天必须保留单份 POSM 快照驱动的 Store/Product 原子发布；不能拆成两次来源读取。
            await _productRefresh.UpdateProductStoreDailyStatisticsWithContext(
                _context,
                _posmContext,
                null,
                _logger,
                targetDate,
                expectedJobId: expectedJobId,
                validateExecutionOwnershipBeforeCommitAsync: ValidateBeforeCommitAsync);
            return;
        }

        var initialProductStateFence = await SalesStatisticsProductStoreDailyRefreshSlice
            .CaptureProductStatisticFailureFenceAsync(
            _context,
            targetDate);
        // 非 2025 队列以前只替换商品日统计，会把迟到来源的新商品金额与旧分店营业额混用。
        // 先提交同一日期的分店统计；其提交前和商品写入事务内均复用队列 owner 校验。
        DateTime? sourceWatermark = null;
        Func<Task>? validateQueuedSourceWatermarkAsync = null;
        try
        {
            sourceWatermark = await SalesStatisticsProductStoreDailyStateSlice
                .QueryDailySourceWatermarkAsync(
                _posmContext,
                GetHBSalesContextFor2025(targetDate),
                targetDate);
            validateQueuedSourceWatermarkAsync = async () =>
            {
                var currentWatermark = await SalesStatisticsProductStoreDailyStateSlice
                    .QueryDailySourceWatermarkAsync(
                    _posmContext,
                    GetHBSalesContextFor2025(targetDate),
                    targetDate);
                if (currentWatermark != sourceWatermark)
                {
                    throw new InvalidOperationException(
                        $"队列分店/商品统计构建期间来源水位发生变化，拒绝提交: {targetDate:yyyy-MM-dd}");
                }
            };
            await _orchestration.UpdateStoreStatisticsWithContext(
                _context,
                _posmContext,
                GetHBSalesContextFor2025(targetDate),
                _logger,
                targetDate,
                null,
                expectedJobId,
                ValidateBeforeCommitAsync,
                sourceWatermark,
                validateQueuedSourceWatermarkAsync);
        }
        catch (Exception ex)
        {
            // 分店前置失败也必须按原 JobId 写 Product Failed；CAS/fencing 会让已换主的任务保持新状态。
            try
            {
                await SalesStatisticsProductStoreDailyRefreshSlice.PersistProductStatisticFailureAsync(
                    _context,
                    _logger,
                    targetDate,
                    ex,
                    expectedJobId,
                    ValidateBeforeCommitAsync,
                    initialProductStateFence);
            }
            catch (Exception stateException)
            {
                _logger.LogError(stateException, "写入分店前置失败的商品统计状态失败: {Date}", targetDate);
            }
            throw;
        }

        // Store 提交和 Product 事务是两个原子提交点；开始后者前再次确认队列租约，
        // 后续仍由 Product 事务内的 JobId fence 拒绝已经换主的旧 worker。
        await ValidateBeforeCommitAsync();
        await _productRefresh.UpdateProductStoreDailyStatisticsWithContext(
            _context,
            _posmContext,
            GetHBSalesContextFor2025(targetDate),
            _logger,
            targetDate,
            sourceWatermarkOverride: sourceWatermark,
            validateSourceWatermarkBeforeCommitAsync: validateQueuedSourceWatermarkAsync,
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
    public async Task<ProductStoreDailyBatchFence> Update2025StoreAndProductStatisticsFromBatchSnapshotAsync(
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
        return await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
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
    public async Task Finalize2025BatchSnapshotDateAsync(ProductStoreDailyBatchFence expectedFence)
    {
        ArgumentNullException.ThrowIfNull(expectedFence);
        var targetDate = expectedFence.Date.Date;
        await SalesStatisticsTransactionExecutor.ExecuteAsync(
            beginAsync: () => _context.Db.Ado.BeginTranAsync(),
            workAsync: async () =>
            {
                var states = await _context.Db.Queryable<SalesStatisticRefreshState>()
                    .Where(state =>
                        state.Date >= targetDate
                        && state.Date < targetDate.AddDays(1)
                        && (state.StatisticType == SalesStatisticType.ProductStoreDaily
                            || state.StatisticType == SalesStatisticType.StoreSales
                            || state.StatisticType == SalesStatisticType.AustralianSupplierStoreSales
                            || state.StatisticType == SalesStatisticType.ChinaSupplierStoreSales)
                    )
                    .With(SqlWith.UpdLock)
                    .ToListAsync();
                SalesStatisticsProductStoreDailyBatchFenceOperations.Validate(states, expectedFence, ProvisionalFreshStatus);
                // 批末确认只升级状态，不重写水位；水位仍精确绑定到预载快照和 POSM pre/post 复核。
                foreach (var state in states)
                {
                    state.Status = SalesStatisticRefreshStatus.Fresh;
                    state.ErrorMessage = null;
                    state.LastCheckedAtUtc = DateTime.UtcNow;
                    state.CompletedAtUtc = DateTime.UtcNow;
                    await _context.Db.Updateable(state).ExecuteCommandAsync();
                }
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
        IReadOnlyCollection<ProductStoreDailyBatchFence> fences,
        string errorMessage
    )
    {
        return Fail2025BatchSnapshotDatesSequentiallyAsync(fences, errorMessage);
    }

    internal async Task Fail2025BatchSnapshotDatesSequentiallyAsync(
        IReadOnlyCollection<ProductStoreDailyBatchFence> fences,
        string errorMessage
    )
    {
        foreach (var fence in fences.OrderBy(item => item.Date))
        {
            var sourceWatermark = fence.States
                .Single(state => state.StatisticType == SalesStatisticType.ProductStoreDaily)
                .LastSourceUploadTime;
            await _productRefresh.Persist2025AtomicFailureStatesAsync(
                _context,
                _logger,
                fence.Date,
                sourceWatermark,
                new InvalidOperationException(errorMessage),
                expectedBatchFence: fence
            );
        }
    }

    /// <summary>
    /// 滚动刷新最近几天的商品分店每日统计，处理 POSM 延迟上传。
    /// </summary>
    public async Task RefreshRecentProductStoreDailyStatistics(int days = 7)
    {
        var safeDays = Math.Max(1, days);
        var endDate = SalesStatisticsBusinessDate.Today();
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
