using System.Security.Cryptography;
using System.Data;
using System.Text;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>逐日预览、校验、应用和比较后回滚分时历史数据。</summary>
public sealed class HourlySalesBackfillService(
    SqlSugarContext context,
    POSMSqlSugarContext posm,
    HBSalesRecordSqlSugarContext history,
    ILogger<HourlySalesBackfillService> logger)
{
    public const string CurrentRuleVersion = HourlySalesBackfillRules.Version;
    private const int MaxDays = 365;
    private const int RecentSourceSettlementDays = 7;
    private const int SourceCommandTimeoutSeconds = 120;

    /// <summary>只读加载原始来源并执行逐日规则，不要求审计表迁移，也不写任何数据库。</summary>
    public async Task<HourlySalesBackfillReadOnlyPreview> PreviewDayReadOnlyAsync(
        DateTime date, CancellationToken token)
    {
        var day = date.Date;
        if (day >= SalesStatisticsBusinessDate.Today())
            throw new ArgumentException("只读预览仅允许已完成业务日", nameof(date));
        var snapshot = await LoadSnapshotAsync(day, token);
        var build = HourlySalesBackfillRules.Build(day, snapshot.Rows, snapshot.Targets,
            snapshot.RequiredSources, snapshot.Statuses);
        var candidateRows = build.Rows.Where(row => row.BranchCode != "ALL").ToList();
        return new HourlySalesBackfillReadOnlyPreview(
            day,
            build.Valid,
            SnapshotHash(snapshot.Rows, snapshot.Targets,
                snapshot.RequiredSources, snapshot.Statuses),
            snapshot.RequiredSources,
            snapshot.Statuses.Select(status => new HourlySalesBackfillReadOnlySourceStatus(
                status.Source, status.State.ToString(), status.RowCount, status.Watermark,
                status.ContentHash, status.Error, status.ObservedAtUtc)).ToList(),
            snapshot.Targets.Values.Sum(target => target.Amount),
            snapshot.Targets.Values.Sum(target => target.Quantity),
            snapshot.Targets.Values.Sum(target => target.OrderCount),
            candidateRows.Sum(row => row.TotalAmount),
            candidateRows.Sum(row => row.TotalQuantity),
            candidateRows.Sum(row => row.OrderCount ?? 0),
            build.Rows.Count,
            build.Rows.Select(row => new HourlySalesBackfillReadOnlyCandidateRow(
                row.Date.Date, row.Hour, row.BranchCode ?? "", row.BranchName ?? "",
                row.TotalAmount, row.TotalQuantity, row.OrderCount ?? 0,
                row.CustomerCount, row.AverageOrderValue)).ToList(),
            build.Issues.Select(issue => $"{issue.Code}:{issue.Message}").ToList());
    }

    public bool SchemaReady() => HourlySalesBackfillProtection.GetSchemaState(context)
        == HourlySalesBackfillSchemaState.Ready;

    public async Task<Guid> PreviewAsync(DateTime start, DateTime end, string actor)
    {
        var first = start.Date;
        var last = end.Date;
        if (!SchemaReady()) throw new InvalidOperationException("分时发布表或统一读取视图尚未完成受控迁移");
        if (last < first || (last - first).TotalDays + 1 > MaxDays
            || last >= SalesStatisticsBusinessDate.Today())
            throw new ArgumentException("回填范围必须是最多365个已完成业务日");
        var now = DateTime.UtcNow;
        var batch = new HourlySalesBackfillBatch
        {
            Id = Guid.NewGuid(), StartDate = first, EndDate = last,
            RuleVersion = HourlySalesBackfillRules.Version, RequestedBy = actor,
            CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        await context.Db.Ado.BeginTranAsync();
        try
        {
            await context.Db.Insertable(batch).ExecuteCommandAsync();
            var days = Enumerable.Range(0, (last - first).Days + 1).Select(offset =>
                new HourlySalesBackfillDay { BatchId = batch.Id, Date = first.AddDays(offset), UpdatedAtUtc = now })
                .ToList();
            foreach (var chunk in days.Chunk(100))
                await context.Db.Insertable(chunk.ToList()).ExecuteCommandAsync();
            await context.Db.Ado.CommitTranAsync();
            return batch.Id;
        }
        catch { await context.Db.Ado.RollbackTranAsync(); throw; }
    }

    public Task<HourlySalesBackfillBatchSnapshot?> GetAsync(Guid id) => GetSnapshotAsync(id);

    /// <summary>返回 runner 可持久化的强类型批次与逐日 checkpoint。</summary>
    public async Task<HourlySalesBackfillBatchSnapshot?> GetSnapshotAsync(Guid id)
    {
        var batch = await context.Db.Queryable<HourlySalesBackfillBatch>().InSingleAsync(id);
        if (batch == null) return null;
        var dayRows = await context.Db.Queryable<HourlySalesBackfillDay>()
            .Where(day => day.BatchId == id).OrderBy(day => day.Date).ToListAsync();
        var days = dayRows.Select(day => new HourlySalesBackfillDaySnapshot(
                day.Date, day.Status, day.SourceHash, day.AfterHash,
                day.ExpectedAmount, day.CandidateAmount,
                day.ExpectedOrderCount, day.CandidateOrderCount, day.RowCount,
                day.CandidateJson, day.SourceStatusJson, day.Error, day.UpdatedAtUtc))
            .ToList();
        return new HourlySalesBackfillBatchSnapshot(
            batch.Id, batch.StartDate, batch.EndDate, batch.RuleVersion, batch.Status,
            batch.RequestedBy, batch.AppliedBy, batch.RolledBackBy,
            batch.CreatedAtUtc, batch.UpdatedAtUtc, batch.Error, days);
    }

    public async Task<bool> RequestAsync(Guid id, bool rollback, string actor)
    {
        if (!SchemaReady()) return false;
        var current = await context.Db.Queryable<HourlySalesBackfillBatch>().InSingleAsync(id);
        if (current == null) return false;
        var allowed = rollback
            ? (current.Status is "Applied" or "AppliedWithIssues" or "Blocked")
                && await context.Db.Queryable<HourlySalesBackfillDay>()
                    .AnyAsync(day => day.BatchId == id && day.Status == "Applied")
            : current.Status is "Previewed" or "PreviewedWithIssues";
        if (!allowed) return false;
        var expected = current.Status;
        var next = rollback ? "RollingBack" : "Applying";
        var rows = await context.Db.Updateable<HourlySalesBackfillBatch>()
            .SetColumns(batch => batch.Status == next)
            .SetColumns(batch => batch.UpdatedAtUtc == DateTime.UtcNow)
            .SetColumnsIF(!rollback, batch => batch.AppliedBy == actor)
            .SetColumnsIF(rollback, batch => batch.RolledBackBy == actor)
            .Where(batch => batch.Id == id && batch.Status == expected)
            .ExecuteCommandAsync();
        return rows == 1;
    }

    /// <summary>显式重读原始来源；检测到水位、内容、日目标或发布版本漂移时撤销读取认证。</summary>
    public async Task<bool?> RevalidateAsync(Guid batchId, DateTime date, CancellationToken token)
    {
        if (!SchemaReady()) return null;
        var day = await context.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId && row.Date == date.Date && row.Status == "Applied")
            .FirstAsync();
        if (day == null) return null;
        var snapshot = await LoadSnapshotAsync(day.Date, token);
        var build = HourlySalesBackfillRules.Build(day.Date, snapshot.Rows, snapshot.Targets,
            snapshot.RequiredSources, snapshot.Statuses);
        var valid = build.Valid
            && SnapshotHash(snapshot.Rows, snapshot.Targets, snapshot.RequiredSources, snapshot.Statuses) == day.SourceHash
            && PublishedTargetHash(await ReadPublishedRowsAsync(day.BatchId, day.Date)) == day.AfterHash;
        var error = valid ? null : "source-drift:原始来源、水位、日统计或已落库小时摘要已变化，需先安全回滚再重新预览";
        await context.Db.Updateable<HourlySalesBackfillDay>()
            .SetColumns(row => row.Error == error)
            .SetColumns(row => row.UpdatedAtUtc == DateTime.UtcNow)
            .Where(row => row.BatchId == batchId && row.Date == date.Date && row.Status == "Applied")
            .ExecuteCommandAsync();
        return valid;
    }

    public async Task<bool> RunOneAsync(CancellationToken token, Func<Task<bool>>? ensureLeaseAsync = null)
    {
        var batch = await context.Db.Queryable<HourlySalesBackfillBatch>()
            .Where(row => row.RuleVersion == HourlySalesBackfillRules.Version
                && (row.Status == "Previewing" || row.Status == "Applying" || row.Status == "RollingBack"))
            .OrderBy(row => row.CreatedAtUtc).FirstAsync();
        if (batch == null) return false;
        return (await RunOneAsync(batch.Id, token, ensureLeaseAsync)).Worked;
    }

    /// <summary>
    /// 只推进指定批次的一个日期，并返回可安全写入 runner checkpoint 的强类型状态。
    /// 调用方必须持有全局租约，并通过 ensureLeaseAsync 在提交前复核租约仍有效。
    /// </summary>
    public async Task<HourlySalesBackfillStepResult> RunOneAsync(
        Guid batchId, CancellationToken token, Func<Task<bool>>? ensureLeaseAsync)
    {
        var batch = await context.Db.Queryable<HourlySalesBackfillBatch>()
            .Where(row => row.Id == batchId && row.RuleVersion == HourlySalesBackfillRules.Version)
            .FirstAsync();
        if (batch == null)
            return new(false, true, "Missing", null, null);
        if (batch.Status is not ("Previewing" or "Applying" or "RollingBack"))
            return new(false, true, batch.Status, null, await GetSnapshotAsync(batchId));
        var expected = batch.Status == "Previewing" ? "Pending"
            : batch.Status == "Applying" ? "Previewed" : "Applied";
        var day = await context.Db.Queryable<HourlySalesBackfillDay>()
            .Where(row => row.BatchId == batchId && row.Status == expected)
            .OrderBy(row => row.Date).FirstAsync();
        if (day == null)
        {
            await CompleteBatchAsync(batch);
            var completed = await GetSnapshotAsync(batchId);
            return new(true, true, completed?.Status ?? batch.Status, null, completed);
        }
        try
        {
            if (batch.Status == "Previewing") await PreviewDayAsync(day, token, ensureLeaseAsync);
            else await ChangeDayAsync(day, batch.Status == "RollingBack", token, ensureLeaseAsync);
        }
        catch (Exception ex)
        {
            // 回滚失败时发布指针仍为 Applied。停止批次，等待排除 hash 冲突后再次发起回滚。
            day.Status = batch.Status == "RollingBack" ? "Applied" : "Failed";
            day.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            day.UpdatedAtUtc = DateTime.UtcNow;
            await context.Db.Updateable(day).ExecuteCommandAsync();
            if (batch.Status == "RollingBack")
            {
                batch.Status = "Blocked";
                batch.Error = day.Error;
                batch.UpdatedAtUtc = DateTime.UtcNow;
                await context.Db.Updateable(batch).ExecuteCommandAsync();
            }
            logger.LogError(ex, "分时回填日期 {Date} 执行失败", day.Date);
        }
        var refreshedBatch = await context.Db.Queryable<HourlySalesBackfillBatch>().InSingleAsync(batchId);
        if (refreshedBatch?.Status is "Previewing" or "Applying" or "RollingBack")
        {
            var remaining = await context.Db.Queryable<HourlySalesBackfillDay>()
                .AnyAsync(row => row.BatchId == batchId && row.Status == expected);
            if (!remaining) await CompleteBatchAsync(refreshedBatch);
        }
        var snapshot = await GetSnapshotAsync(batchId);
        var terminal = snapshot?.Status is not ("Previewing" or "Applying" or "RollingBack");
        return new(true, terminal, snapshot?.Status ?? "Missing", day.Date, snapshot);
    }

    private async Task CompleteBatchAsync(HourlySalesBackfillBatch batch)
    {
        var days = await context.Db.Queryable<HourlySalesBackfillDay>()
            .Where(day => day.BatchId == batch.Id).ToListAsync();
        batch.Status = batch.Status switch
        {
            "Previewing" when days.All(day => day.Status == "Previewed") => "Previewed",
            "Previewing" when days.Any(day => day.Status == "Previewed") => "PreviewedWithIssues",
            "Applying" when days.All(day => day.Status == "Applied") => "Applied",
            "Applying" when days.Any(day => day.Status == "Applied") => "AppliedWithIssues",
            "RollingBack" when days.Where(day => day.Status != "Blocked" && day.Status != "Failed")
                .All(day => day.Status == "RolledBack") => "RolledBack",
            _ => "Blocked",
        };
        if (batch.Status is "Previewed" or "Applied" or "RolledBack")
            batch.Error = null;
        batch.UpdatedAtUtc = DateTime.UtcNow;
        await context.Db.Updateable(batch).ExecuteCommandAsync();
    }

    private async Task PreviewDayAsync(HourlySalesBackfillDay day, CancellationToken token,
        Func<Task<bool>>? ensureLeaseAsync)
    {
        var snapshot = await LoadSnapshotAsync(day.Date, token);
        var build = HourlySalesBackfillRules.Build(day.Date, snapshot.Rows, snapshot.Targets,
            snapshot.RequiredSources, snapshot.Statuses);
        day.SourceStatusJson = JsonSerializer.Serialize(snapshot.Statuses);
        day.SourceHash = SnapshotHash(snapshot.Rows, snapshot.Targets,
            snapshot.RequiredSources, snapshot.Statuses);
        day.CandidateJson = build.Valid ? JsonSerializer.Serialize(ToImages(build.Rows)) : null;
        day.ExpectedAmount = snapshot.Targets.Values.Sum(target => target.Amount);
        day.ExpectedOrderCount = snapshot.Targets.Values.Sum(target => target.OrderCount);
        day.CandidateAmount = build.Rows.Where(row => row.BranchCode != "ALL").Sum(row => row.TotalAmount);
        day.CandidateOrderCount = build.Rows.Where(row => row.BranchCode != "ALL").Sum(row => row.OrderCount ?? 0);
        day.RowCount = build.Rows.Count;
        day.Status = build.Valid ? "Previewed" : "Blocked";
        day.Error = build.Valid ? null : string.Join("; ", build.Issues.Select(issue => $"{issue.Code}:{issue.Message}"));
        day.UpdatedAtUtc = DateTime.UtcNow;
        if (ensureLeaseAsync != null && !await ensureLeaseAsync())
            throw new HourlySalesBackfillConflictException("全局执行租约已失效");
        await context.Db.Updateable(day).ExecuteCommandAsync();
    }

    private async Task ChangeDayAsync(HourlySalesBackfillDay day, bool rollback, CancellationToken token,
        Func<Task<bool>>? ensureLeaseAsync)
    {
        List<HourlyRowImage>? candidate = null;
        if (!rollback)
        {
            // 原始来源查询和聚合必须在主库事务外完成，避免慢 HBSales 查询长期占用报表行锁。
            var snapshot = await LoadSnapshotAsync(day.Date, token);
            var build = HourlySalesBackfillRules.Build(day.Date, snapshot.Rows, snapshot.Targets,
                snapshot.RequiredSources, snapshot.Statuses);
            if (!build.Valid || SnapshotHash(snapshot.Rows, snapshot.Targets,
                    snapshot.RequiredSources, snapshot.Statuses) != day.SourceHash)
                throw new HourlySalesBackfillConflictException("来源或日统计已变化，需要重新预览");
            candidate = ToImages(build.Rows);
            if (JsonSerializer.Serialize(candidate) != day.CandidateJson)
                throw new HourlySalesBackfillConflictException("候选结果与冻结预览不一致");

            // 二次完整摘要仍在事务外，缩短来源读取结束到替换的时间窗口。
            var confirmation = await LoadSnapshotAsync(day.Date, token);
            var confirmationBuild = HourlySalesBackfillRules.Build(day.Date,
                confirmation.Rows, confirmation.Targets,
                confirmation.RequiredSources, confirmation.Statuses);
            if (!confirmationBuild.Valid
                || SnapshotHash(confirmation.Rows, confirmation.Targets,
                    confirmation.RequiredSources, confirmation.Statuses) != day.SourceHash
                || JsonSerializer.Serialize(ToImages(confirmationBuild.Rows)) != day.CandidateJson)
                throw new HourlySalesBackfillConflictException("来源在应用前发生变化，需要重新预览");
        }

        // Worker/测试可在同一 scope 连续推进多个阶段。前一日期 guard 会把 SqlSugar 切为固定连接，
        // 后续只读查询可能再次打开它；取得下一次会话锁前显式关闭无事务连接。
        if (context.Db.CurrentConnectionConfig.DbType == SqlSugar.DbType.SqlServer
            && context.Db.Ado.Transaction == null
            && context.Db.Ado.Connection.State != ConnectionState.Closed)
            context.Db.Ado.Close();
        await using var guard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(context, day.Date, logger);
        if (!guard.Acquired) throw new HourlySalesBackfillConflictException("日期统计执行锁繁忙");
        await context.Db.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            if (!rollback && await context.Db.Queryable<HourlySalesBackfillDay>().With(SqlWith.UpdLock)
                .AnyAsync(row => row.Date == day.Date && row.Status == "Applied"
                    && row.BatchId != day.BatchId))
                throw new HourlySalesBackfillConflictException("该日期已有另一批次完成认证，请先回滚原批次");
            if (rollback)
            {
                var publishedRows = await ReadPublishedRowsAsync(day.BatchId, day.Date, true);
                if (PublishedTargetHash(publishedRows) != day.AfterHash)
                    throw new HourlySalesBackfillConflictException("发布版本已变化，拒绝切换回滚指针");
                // 同一事务内切换当前指针；失败会随事务一起回滚为 Applied。
                day.Status = "RollingBack";
                day.UpdatedAtUtc = DateTime.UtcNow;
                await context.Db.Updateable(day).ExecuteCommandAsync();
                day.Status = "RolledBack";
                day.Error = null;
            }
            else
            {
                if (await context.Db.Queryable<HourlySalesBackfillPublishedRow>().With(SqlWith.UpdLock)
                    .AnyAsync(row => row.BatchId == day.BatchId && row.Date == day.Date))
                    throw new HourlySalesBackfillConflictException("该批次日期已存在发布版本，拒绝重复写入");
                await InsertPublishedRowsAsync(day.BatchId, candidate!);
                // 哈希必须取数据库实际落库值，避免 decimal scale 舍入导致认证和回滚失配。
                var persistedRows = await ReadPublishedRowsAsync(day.BatchId, day.Date, true);
                if (!RowsMatchCandidateCore(persistedRows, candidate!))
                    throw new HourlySalesBackfillConflictException("分时候选发布后业务字段校验失败");
                day.AfterHash = PublishedTargetHash(persistedRows);
                day.BeforeHash = null;
                day.BeforeJson = null;
                day.Status = "Applied";
                day.Error = null;
            }
            day.UpdatedAtUtc = DateTime.UtcNow;
            await context.Db.Updateable(day).ExecuteCommandAsync();
            if (ensureLeaseAsync != null && !await ensureLeaseAsync())
                throw new HourlySalesBackfillConflictException("全局执行租约已失效，拒绝提交分时发布");
            await context.Db.Ado.CommitTranAsync();
        }
        catch { await context.Db.Ado.RollbackTranAsync(); throw; }
    }

    private async Task<Snapshot> LoadSnapshotAsync(DateTime date, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var observedAtUtc = DateTime.UtcNow;
        var targets = (await context.Db.Queryable<StoreSalesStatistic>()
            .Where(row => row.Date == date.Date && row.BranchCode != "ALL").ToListAsync(token))
            .ToDictionary(row => row.BranchCode,
                row => new HourlySalesBackfillDailyTarget(
                    row.TotalAmount, row.TotalQuantity, row.OrderCount, row.BranchName),
                StringComparer.OrdinalIgnoreCase);
        var rows = new List<HourlySalesBackfillSourceRow>();
        var requiredSources = new List<string> { "POSM" };
        List<HourlySalesBackfillSourceRow> posmRows = [];
        DateTime? posmWatermark = null;
        Exception? posmError = null;
        try
        {
            var source = await LoadPosmSnapshotAsync(date, token);
            posmRows = source.Rows;
            posmWatermark = source.Watermark;
            rows.AddRange(posmRows);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && token.IsCancellationRequested) throw;
            posmError = ex;
        }
        List<HourlySalesBackfillSourceRow> historyRows = [];
        DateTime? historyWatermark = null;
        Exception? historyError = null;
        if (SalesStatisticsHBSalesHistoryWindow.Includes(date))
        {
            requiredSources.Add("HBSales");
            try
            {
                var source = await LoadHistorySnapshotAsync(date, token);
                historyRows = source.Rows;
                historyWatermark = source.Watermark;
                rows.AddRange(historyRows);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && token.IsCancellationRequested) throw;
                historyError = ex;
            }
        }
        var combinedWatermark = new[] { posmWatermark, historyWatermark }
            .Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Max();
        DateTime? effectiveCombinedWatermark = combinedWatermark == default ? null : combinedWatermark;

        token.ThrowIfCancellationRequested();
        var coverage = await ReadCoverageEvidenceAsync(date, effectiveCombinedWatermark, token);
        var statuses = new List<HourlySalesBackfillSourceStatus>
        {
            Status("POSM", posmRows, posmWatermark, observedAtUtc,
                posmError, coverage),
        };
        if (SalesStatisticsHBSalesHistoryWindow.Includes(date))
            statuses.Add(Status("HBSales", historyRows, historyWatermark, observedAtUtc,
                historyError, coverage));
        return new(rows, targets, requiredSources, statuses);
    }

    private static HourlySalesBackfillSourceStatus Status(
        string source, List<HourlySalesBackfillSourceRow> rows, DateTime? sourceWatermark,
        DateTime observedAtUtc, Exception? sourceError, CoverageEvidence coverage)
    {
        var error = sourceError?.Message ?? coverage.Error;
        var state = error == null
            ? rows.Count == 0 ? HourlySalesBackfillSourceState.Empty : HourlySalesBackfillSourceState.Success
            : HourlySalesBackfillSourceState.Unavailable;
        var watermark = $"source-max:{sourceWatermark?.ToUniversalTime():O};{coverage.Watermark}";
        return new(source, state, rows.Count, watermark, SourceHash(rows), error, observedAtUtc);
    }

    private async Task<CoverageEvidence> ReadCoverageEvidenceAsync(
        DateTime date, DateTime? combinedWatermark, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settledBefore = SalesStatisticsBusinessDate.Today().AddDays(-RecentSourceSettlementDays);
        if (date.Date <= settledBefore)
            return new(HistoricalSettlementWatermark(date, SalesStatisticsBusinessDate.Today()), null);
        try
        {
            var types = new[] { SalesStatisticType.ProductStoreDaily, SalesStatisticType.RevenueReportPublished };
            var states = await context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(state => state.Date == date.Date && types.Contains(state.StatisticType)).ToListAsync(token);
            var product = states.SingleOrDefault(state => state.StatisticType == SalesStatisticType.ProductStoreDaily);
            var published = states.SingleOrDefault(state => state.StatisticType == SalesStatisticType.RevenueReportPublished);
            var valid = product?.Status == SalesStatisticRefreshStatus.Fresh
                && product.CompletedAtUtc.HasValue && product.LastCheckedAtUtc.HasValue
                && product.LastSourceUploadTime == combinedWatermark
                && published?.Status == SalesStatisticRefreshStatus.Fresh
                && published.CompletedAtUtc.HasValue && published.LastCheckedAtUtc.HasValue;
            var watermark = $"product-completed:{product?.CompletedAtUtc:O};"
                + $"published-completed:{published?.CompletedAtUtc:O}";
            return valid ? new(watermark, null)
                : new(watermark, "近期来源尚无与当前真实水位匹配的Fresh/Published日终证据");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new("coverage-state-unavailable", $"近期来源覆盖状态不可用: {ex.Message}");
        }
    }

    internal static string HistoricalSettlementWatermark(DateTime targetDate, DateTime asOfDate)
    {
        if (targetDate.Date > asOfDate.Date.AddDays(-RecentSourceSettlementDays))
            throw new ArgumentException("目标日尚未达到历史来源稳定期", nameof(targetDate));
        // token 只描述目标日采用的稳定期规则，不能包含每天变化的 cutoff，
        // 否则隔日审批会让相同来源快照产生不同 SourceHash。
        return $"historical-settled:v1;date:{targetDate:yyyy-MM-dd};minimum-age-days:{RecentSourceSettlementDays}";
    }

    private async Task<SourceSnapshot> LoadPosmSnapshotAsync(
        DateTime date, CancellationToken token)
    {
        var db = posm.Db;
        var originalTimeout = db.Ado.CommandTimeOut;
        var originalCancellation = db.Ado.CancellationToken;
        db.Ado.CommandTimeOut = Math.Clamp(originalTimeout, 1, SourceCommandTimeoutSeconds);
        db.Ado.CancellationToken = token;
        await db.Ado.BeginTranAsync(IsolationLevel.ReadCommitted);
        try
        {
            var source = await LoadPosmAsync(date, token);
            var dailyWatermark = await SalesStatisticsProductStoreDailyStateSlice
                .QueryDailyPosmSourceWatermarkAsync(posm, date);
            var watermark = new[] { dailyWatermark, source.Watermark }
                .Where(value => value.HasValue).Select(value => value!.Value)
                .DefaultIfEmpty().Max();
            await db.Ado.CommitTranAsync();
            return new(source.Rows, watermark == default ? null : watermark);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
        finally
        {
            db.Ado.CommandTimeOut = originalTimeout;
            db.Ado.CancellationToken = originalCancellation;
        }
    }

    private async Task<SourceSnapshot> LoadPosmAsync(
        DateTime date, CancellationToken token)
    {
        var next = date.Date.AddDays(1);
        var orders = await posm.Db.Queryable<SalesOrder>()
            .Where(order => order.Status != null && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null && order.OrderTime >= date.Date && order.OrderTime < next)
            .ToListAsync(token);
        var deviceMap = await SalesStatisticsProductStoreDailySourceQueries.LoadDeviceBranchMapAsync(
            posm, orders.Where(order => string.IsNullOrWhiteSpace(order.BranchCode)).Select(order => order.DeviceCode));
        var byId = orders.Where(order => !string.IsNullOrWhiteSpace(order.OrderGuid))
            .ToDictionary(order => order.OrderGuid!, StringComparer.OrdinalIgnoreCase);
        var result = orders.Select(order => new HourlySalesBackfillSourceRow("POSM",
            SalesStatisticsCodeRules.ResolveBranchCode(order.BranchCode, order.DeviceCode, deviceMap),
            order.OrderTime?.Hour, order.OrderGuid ?? "", 0m, 0, true, order.OrderGuid)).ToList();
        if (byId.Count == 0) return new(result, null);
        var ids = byId.Keys.ToList();
        // SQL Server 单语句参数上限为 2100，按 1000 订单分块避免繁忙日回填失败。
        var payments = new List<PaymentDetail>();
        var details = new List<SalesOrderDetail>();
        foreach (var chunk in ids.Chunk(1000))
        {
            var chunkIds = chunk.ToList();
            payments.AddRange(await posm.Db.Queryable<PaymentDetail>()
                .Where(row => chunkIds.Contains(row.OrderGuid!)).ToListAsync(token));
            details.AddRange(await posm.Db.Queryable<SalesOrderDetail>()
                .Where(row => chunkIds.Contains(row.OrderGuid)).ToListAsync(token));
        }
        result.AddRange(payments.Where(row => row.OrderGuid != null && byId.TryGetValue(row.OrderGuid, out _)).Select(row =>
        {
            var order = byId[row.OrderGuid!];
            return new HourlySalesBackfillSourceRow("POSM", SalesStatisticsCodeRules.ResolveBranchCode(
                order.BranchCode, order.DeviceCode, deviceMap), order.OrderTime?.Hour, row.OrderGuid!,
                row.Amount ?? 0m, 0, false, row.OrderGuid);
        }));
        result.AddRange(details.Where(row => byId.ContainsKey(row.OrderGuid)).Select(row =>
        {
            var order = byId[row.OrderGuid];
            return new HourlySalesBackfillSourceRow("POSM", SalesStatisticsCodeRules.ResolveBranchCode(
                order.BranchCode, order.DeviceCode, deviceMap), order.OrderTime?.Hour, row.OrderGuid,
                0m, row.Quantity ?? 0, false, row.OrderGuid);
        }));
        // StoreSalesStatistic 的 POSM 口径已由 PaymentDetail 体现退款金额，数量则来自
        // SalesOrderDetail；SalesReturnRecord 只属于商品日统计，不能在营业额回填中再次扣减。
        return new(result, null);
    }

    private async Task<SourceSnapshot> LoadHistorySnapshotAsync(
        DateTime date, CancellationToken token)
    {
        var db = history.Db;
        var originalTimeout = db.Ado.CommandTimeOut;
        var originalCancellation = db.Ado.CancellationToken;
        db.Ado.CommandTimeOut = Math.Clamp(originalTimeout, 1, SourceCommandTimeoutSeconds);
        db.Ado.CancellationToken = token;
        await db.Ado.BeginTranAsync(IsolationLevel.ReadCommitted);
        try
        {
            var snapshot = await LoadHistoryAsync(date, token);
            await db.Ado.CommitTranAsync();
            return snapshot;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
        finally
        {
            db.Ado.CommandTimeOut = originalTimeout;
            db.Ado.CancellationToken = originalCancellation;
        }
    }

    private async Task<SourceSnapshot> LoadHistoryAsync(DateTime date, CancellationToken token)
    {
        var next = date.Date.AddDays(1);
        // 业务关联键是“营业日 + 销售单号”。主表日期列有索引，因此只读取目标日，
        // 既能验证该营业日内唯一匹配，也避免每个回填日扫描十五天主表。
        var mains = await history.Db.Queryable<SalesOrderMain>()
            .Where(main => main.B结账日期.HasValue && main.B结账日期 >= date.Date
                && main.B结账日期 < next)
            .Select(main => new HistoryMain
            {
                MainId = main.ID,
                OrderId = main.B销售单号,
                BranchCode = main.B分店代码,
                CheckoutDate = main.B结账日期,
                CheckoutTime = main.B结账时间,
                DocumentType = main.B单据类型,
                CreatedAt = main.FGC_CreateDate,
                LastModifiedAt = main.FGC_LastModifyDate,
            }).ToListAsync(token);
        var details = await history.Db.Queryable<SalesOrderDetailRecord>()
            .Where(detail => detail.B结账日期.HasValue && detail.B结账日期 >= date.Date
                && detail.B结账日期 < next)
            .Select(detail => new HistoryDetail
            {
                DetailId = detail.ID,
                OrderId = detail.B销售单号,
                BranchCode = detail.B分店代码,
                ProductCode = detail.B产品编号,
                ItemNumber = detail.B货号,
                Barcode = detail.B条形码,
                CheckoutTime = detail.B结账时间,
                Amount = detail.B合计金额,
                Quantity = detail.B数量,
                CreatedAt = detail.FGC_CreateDate,
                LastModifiedAt = detail.FGC_LastModifyDate,
            }).ToListAsync(token);
        var mainByOrder = mains.Where(main => !string.IsNullOrWhiteSpace(main.OrderId))
            .GroupBy(main => main.OrderId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var resolved = new List<HistoryRow>(details.Count);
        foreach (var detail in details)
        {
            if (string.IsNullOrWhiteSpace(detail.OrderId)
                || !mainByOrder.TryGetValue(detail.OrderId, out var matches)
                || matches.Count != 1)
                throw new HourlySalesBackfillConflictException(
                    $"HBSales详情 {detail.DetailId} 的销售单号主表匹配数不为1，日期已隔离待人工裁决");
            var main = matches[0];
            var mainBranch = main.BranchCode?.Trim();
            var detailBranch = detail.BranchCode?.Trim();
            if (main.CheckoutDate?.Date != date.Date)
                throw new HourlySalesBackfillConflictException(
                    $"HBSales销售单 {detail.OrderId} 主表营业日与详情日期不一致，日期已隔离待人工裁决");
            if (!main.CheckoutTime.HasValue)
                throw new HourlySalesBackfillConflictException(
                    $"HBSales销售单 {detail.OrderId} 主表缺少最终结账时间，日期已隔离待人工裁决");
            if (string.IsNullOrWhiteSpace(mainBranch)
                || string.IsNullOrWhiteSpace(detailBranch)
                || !string.Equals(mainBranch, detailBranch, StringComparison.OrdinalIgnoreCase))
                throw new HourlySalesBackfillConflictException(
                    $"HBSales销售单 {detail.OrderId} 主表与详情分店不一致，日期已隔离待人工裁决");
            resolved.Add(new HistoryRow
            {
                MainId = main.MainId,
                DetailId = detail.DetailId,
                OrderId = detail.OrderId,
                BranchCode = mainBranch,
                ProductCode = detail.ProductCode,
                ItemNumber = detail.ItemNumber,
                Barcode = detail.Barcode,
                // 同一销售单的多条详情时间可能跨小时；最终小时以唯一主表结账时间为准。
                CheckoutTime = main.CheckoutTime,
                DocumentType = main.DocumentType,
                Amount = detail.Amount,
                Quantity = detail.Quantity,
                MainCreatedAt = main.CreatedAt,
                MainLastModifiedAt = main.LastModifiedAt,
                DetailCreatedAt = detail.CreatedAt,
                DetailLastModifiedAt = detail.LastModifiedAt,
            });
        }
        var included = resolved.Where(row => row.DocumentType?.Trim() != "2")
            .Where(row => (row.Quantity ?? 0m) != 0m || (row.Amount ?? 0m) != 0m).ToList();
        var productRows = included.Select(row =>
        {
            var sign = row.DocumentType?.Trim() is "3" or "4" ? -1 : 1;
            return new ProductStoreDailySourceRow
            {
                IsHBSalesSource = true,
                Date = date.Date,
                DetailGuid = row.DetailId.ToString(),
                BranchCode = row.BranchCode,
                ProductCode = row.ProductCode,
                ItemNumber = row.ItemNumber,
                Barcode = row.Barcode,
                Quantity = (row.Quantity ?? 0m) * sign,
                ActualAmount = (row.Amount ?? 0m) * sign,
            };
        }).ToList();
        var originalCatalogTimeout = context.Db.Ado.CommandTimeOut;
        var originalCatalogCancellation = context.Db.Ado.CancellationToken;
        context.Db.Ado.CommandTimeOut = Math.Clamp(originalCatalogTimeout, 1, SourceCommandTimeoutSeconds);
        context.Db.Ado.CancellationToken = token;
        try
        {
            await SalesStatisticsProductStoreDailySourceReader.ResolveMissingHBSalesProductCodesAsync(
                context, productRows, includeInactive: true);
        }
        finally
        {
            context.Db.Ado.CommandTimeOut = originalCatalogTimeout;
            context.Db.Ado.CancellationToken = originalCatalogCancellation;
        }
        var productByDetail = productRows.ToDictionary(row => int.Parse(row.DetailGuid!));
        var invalidProduct = included.FirstOrDefault(row =>
            string.IsNullOrWhiteSpace(row.BranchCode)
            || string.IsNullOrWhiteSpace(productByDetail[row.DetailId].ProductCode));
        if (invalidProduct != null)
            throw new HourlySalesBackfillConflictException(
                $"HBSales详情 {invalidProduct.DetailId} 缺少分店编码或无法解析唯一商品编码，日期已隔离待人工裁决");
        var fractionalQuantity = productRows.FirstOrDefault(row =>
            row.Quantity != decimal.Truncate(row.Quantity));
        if (fractionalQuantity != null)
            throw new HourlySalesBackfillConflictException(
                $"HBSales详情 {fractionalQuantity.DetailGuid} 数量不是整数，日期已隔离待人工裁决");
        var rows = included.Select(row =>
        {
            var product = productByDetail[row.DetailId];
            return new HourlySalesBackfillSourceRow("HBSales", row.BranchCode?.Trim() ?? "",
                row.CheckoutTime.HasValue ? row.CheckoutTime.Value.Hours : null,
                row.OrderId ?? "", product.ActualAmount,
                decimal.ToInt32(product.Quantity), true, row.OrderId);
        }).ToList();
        var watermark = included.SelectMany(row => new[]
            {
                row.MainCreatedAt, row.MainLastModifiedAt,
                row.DetailCreatedAt, row.DetailLastModifiedAt,
            })
            .Where(value => value.HasValue).Select(value => value!.Value)
            .DefaultIfEmpty().Max();
        return new(rows, watermark == default ? null : watermark);
    }

    private Task<List<HourlySalesBackfillPublishedRow>> ReadPublishedRowsAsync(
        Guid batchId, DateTime date, bool forUpdate = false)
    {
        var query = context.Db.Queryable<HourlySalesBackfillPublishedRow>()
            .Where(row => row.BatchId == batchId && row.Date == date.Date);
        if (forUpdate) query = query.With(SqlWith.UpdLock);
        return query.ToListAsync();
    }

    private async Task InsertPublishedRowsAsync(Guid batchId, IReadOnlyCollection<HourlyRowImage> images)
    {
        var now = DateTime.UtcNow;
        var rows = images.Select(image => image.ToPublishedModel(batchId, now)).ToList();
        // 一天最多只有“门店数 x 24 + ALL x 24”行；普通批量 Insert 会复用 manifest 事务。
        foreach (var chunk in rows.Chunk(500))
            await context.Db.Insertable(chunk.ToList()).ExecuteCommandAsync();
    }

    private static List<HourlyRowImage> ToImages(IEnumerable<HourlySalesStatistic> rows) => rows
        .Select(row => new HourlyRowImage(row.Date.Date, row.Hour, row.BranchCode ?? "", row.BranchName ?? "",
            row.TotalAmount, row.TotalQuantity, row.OrderCount ?? 0, row.CustomerCount, row.AverageOrderValue))
        .OrderBy(row => row.Hour).ThenBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase).ToList();

    private static List<HourlyRowImage> ToImages(IEnumerable<HourlySalesBackfillPublishedRow> rows) => rows
        .Select(row => new HourlyRowImage(row.Date.Date, row.Hour, row.BranchCode, row.BranchName ?? "",
            row.TotalAmount, row.TotalQuantity, row.OrderCount, row.CustomerCount, row.AverageOrderValue))
        .OrderBy(row => row.Hour).ThenBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase).ToList();

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();

    internal static string SourceHash(IEnumerable<HourlySalesBackfillSourceRow> rows) => Hash(rows
        .OrderBy(row => row.Source, StringComparer.Ordinal)
        .ThenBy(row => row.BranchCode, StringComparer.Ordinal)
        .ThenBy(row => row.Hour)
        .ThenBy(row => row.OrderId, StringComparer.Ordinal)
        .ThenBy(row => row.CountOrder)
        .ThenBy(row => row.Amount)
        .ThenBy(row => row.Quantity)
        .ThenBy(row => row.CrossSourceBusinessKey, StringComparer.Ordinal)
        .ToList());

    internal static string SnapshotHash(
        IEnumerable<HourlySalesBackfillSourceRow> rows,
        IReadOnlyDictionary<string, HourlySalesBackfillDailyTarget> targets,
        IEnumerable<string> requiredSources,
        IEnumerable<HourlySalesBackfillSourceStatus> statuses) => Hash(new
        {
            RowsHash = SourceHash(rows),
            RequiredSources = requiredSources.OrderBy(source => source, StringComparer.OrdinalIgnoreCase).ToList(),
            Statuses = statuses.OrderBy(status => status.Source, StringComparer.OrdinalIgnoreCase)
                .Select(status => new
                {
                    status.Source, status.State, status.RowCount,
                    status.Watermark, status.ContentHash, status.Error,
                }).ToList(),
            Targets = targets.OrderBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
                .Select(target => new
                {
                    BranchCode = target.Key,
                    target.Value.Amount,
                    target.Value.Quantity,
                    target.Value.OrderCount,
                    target.Value.BranchName,
                }).ToList(),
        });

    internal static string TargetHash(IEnumerable<HourlySalesStatistic> rows) => Hash(ToImages(rows));

    internal static string PublishedTargetHash(IEnumerable<HourlySalesBackfillPublishedRow> rows) =>
        Hash(ToImages(rows));

    private static bool RowsMatchCandidateCore(
        IEnumerable<HourlySalesBackfillPublishedRow> persistedRows,
        IReadOnlyCollection<HourlyRowImage> candidate)
    {
        var persisted = ToImages(persistedRows);
        if (persisted.Count != candidate.Count) return false;
        return persisted.Zip(candidate).All(pair =>
            pair.First.Date == pair.Second.Date
            && pair.First.Hour == pair.Second.Hour
            && string.Equals(pair.First.BranchCode, pair.Second.BranchCode, StringComparison.OrdinalIgnoreCase)
            && pair.First.BranchName == pair.Second.BranchName
            && pair.First.TotalAmount == pair.Second.TotalAmount
            && pair.First.TotalQuantity == pair.Second.TotalQuantity
            && pair.First.OrderCount == pair.Second.OrderCount
            && pair.First.CustomerCount == pair.Second.CustomerCount
            // SQL decimal scale会舍入循环小数，核心金额/订单相等时允许一分以内的AOV落库差异。
            && Math.Abs(pair.First.AverageOrderValue - pair.Second.AverageOrderValue) <= 0.01m);
    }

    private sealed record Snapshot(List<HourlySalesBackfillSourceRow> Rows,
        Dictionary<string, HourlySalesBackfillDailyTarget> Targets,
        List<string> RequiredSources,
        List<HourlySalesBackfillSourceStatus> Statuses);
    private sealed record SourceSnapshot(List<HourlySalesBackfillSourceRow> Rows, DateTime? Watermark);
    private sealed record CoverageEvidence(string Watermark, string? Error);
    private sealed class HistoryRow
    {
        public int MainId { get; init; }
        public int DetailId { get; init; }
        public string? OrderId { get; init; }
        public string? BranchCode { get; init; }
        public string? ProductCode { get; init; }
        public string? ItemNumber { get; init; }
        public string? Barcode { get; init; }
        public TimeSpan? CheckoutTime { get; init; }
        public string? DocumentType { get; init; }
        public decimal? Amount { get; init; }
        public decimal? Quantity { get; init; }
        public DateTime? MainCreatedAt { get; init; }
        public DateTime? MainLastModifiedAt { get; init; }
        public DateTime? DetailCreatedAt { get; init; }
        public DateTime? DetailLastModifiedAt { get; init; }
    }
    private sealed class HistoryMain
    {
        public int MainId { get; init; }
        public string? OrderId { get; init; }
        public string? BranchCode { get; init; }
        public DateTime? CheckoutDate { get; init; }
        public TimeSpan? CheckoutTime { get; init; }
        public string? DocumentType { get; init; }
        public DateTime? CreatedAt { get; init; }
        public DateTime? LastModifiedAt { get; init; }
    }
    private sealed class HistoryDetail
    {
        public int DetailId { get; init; }
        public string? OrderId { get; init; }
        public string? BranchCode { get; init; }
        public string? ProductCode { get; init; }
        public string? ItemNumber { get; init; }
        public string? Barcode { get; init; }
        public TimeSpan? CheckoutTime { get; init; }
        public decimal? Amount { get; init; }
        public decimal? Quantity { get; init; }
        public DateTime? CreatedAt { get; init; }
        public DateTime? LastModifiedAt { get; init; }
    }
    private sealed record HourlyRowImage(DateTime Date, int Hour, string BranchCode, string BranchName,
        decimal TotalAmount, int TotalQuantity, int OrderCount, int CustomerCount, decimal AverageOrderValue)
    {
        internal HourlySalesBackfillPublishedRow ToPublishedModel(Guid batchId, DateTime publishedAtUtc) => new()
        {
            BatchId = batchId,
            Date = Date, Hour = Hour, BranchCode = BranchCode, BranchName = BranchName,
            TotalAmount = TotalAmount, TotalQuantity = TotalQuantity, OrderCount = OrderCount,
            CustomerCount = CustomerCount, AverageOrderValue = AverageOrderValue,
            PublishedAtUtc = publishedAtUtc,
        };
    }
}

public sealed record HourlySalesBackfillReadOnlySourceStatus(
    string Source,
    string State,
    int RowCount,
    string? Watermark,
    string? ContentHash,
    string? Error,
    DateTime? ObservedAtUtc);

public sealed record HourlySalesBackfillReadOnlyPreview(
    DateTime Date,
    bool Valid,
    string SourceHash,
    IReadOnlyList<string> RequiredSources,
    IReadOnlyList<HourlySalesBackfillReadOnlySourceStatus> SourceStatuses,
    decimal ExpectedAmount,
    int ExpectedQuantity,
    int ExpectedOrderCount,
    decimal CandidateAmount,
    int CandidateQuantity,
    int CandidateOrderCount,
    int CandidateRowCount,
    IReadOnlyList<HourlySalesBackfillReadOnlyCandidateRow> CandidateRows,
    IReadOnlyList<string> Issues);

public sealed record HourlySalesBackfillReadOnlyCandidateRow(
    DateTime Date,
    int Hour,
    string BranchCode,
    string BranchName,
    decimal TotalAmount,
    int TotalQuantity,
    int OrderCount,
    int CustomerCount,
    decimal AverageOrderValue);

public sealed record HourlySalesBackfillDaySnapshot(
    DateTime Date,
    string Status,
    string? SourceHash,
    string? AfterHash,
    decimal ExpectedAmount,
    decimal CandidateAmount,
    int ExpectedOrderCount,
    int CandidateOrderCount,
    int RowCount,
    string? CandidateJson,
    string? SourceStatusJson,
    string? Error,
    DateTime UpdatedAtUtc);

public sealed record HourlySalesBackfillBatchSnapshot(
    Guid BatchId,
    DateTime StartDate,
    DateTime EndDate,
    string RuleVersion,
    string Status,
    string RequestedBy,
    string? AppliedBy,
    string? RolledBackBy,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? Error,
    IReadOnlyList<HourlySalesBackfillDaySnapshot> Days);

public sealed record HourlySalesBackfillStepResult(
    bool Worked,
    bool Terminal,
    string BatchStatus,
    DateTime? ProcessedDate,
    HourlySalesBackfillBatchSnapshot? Snapshot);

internal sealed class HourlySalesBackfillConflictException(string message) : Exception(message);
