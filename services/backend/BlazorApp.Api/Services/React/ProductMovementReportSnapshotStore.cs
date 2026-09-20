using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 商品经营分析快照的批次读写。表由 SqlScripts/ProductMovementReportSnapshot.sql 单独部署；
    /// 未部署时 <see cref="SchemaReady"/> 为 false，读取端回到实时计算，后台任务跳过。
    /// </summary>
    public sealed class ProductMovementReportSnapshotStore
    {
        internal const string RunTable = "ProductMovementReportSnapshotRun";
        internal const string SnapshotTable = "ProductMovementReportSnapshot";

        // 部署后表不会自行消失，确认一次即可，避免每个请求都查系统视图。
        private static volatile bool _schemaConfirmed;

        private readonly ISqlSugarClient _db;

        public ProductMovementReportSnapshotStore(ISqlSugarClient db)
        {
            _db = db;
        }

        public bool SchemaReady
        {
            get
            {
                if (_schemaConfirmed)
                {
                    return true;
                }

                var ready = _db.DbMaintenance.IsAnyTable(RunTable, false)
                    && _db.DbMaintenance.IsAnyTable(SnapshotTable, false);
                if (ready)
                {
                    _schemaConfirmed = true;
                }
                return ready;
            }
        }

        /// <summary>与门店下拉选项同口径：启用、未删除、编码非空。</summary>
        public async Task<List<string>> GetActiveStoreCodesAsync()
        {
            var rows = await _db.Ado.SqlQueryAsync<string>(
                "SELECT s.StoreCode FROM [Store] s "
                    + "WHERE s.IsActive = 1 AND s.IsDeleted = 0 AND NULLIF(s.StoreCode, N'') IS NOT NULL "
                    + "ORDER BY s.StoreCode"
            );
            return rows.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim()).ToList();
        }

        /// <summary>每个门店在指定业务日期、当前规则版本下最新的一批 Ready 快照。</summary>
        public async Task<List<ProductMovementReportSnapshotRun>> GetLatestReadyRunsAsync(
            DateTime asOfDate,
            IReadOnlyCollection<string>? storeCodes
        )
        {
            var parameters = new List<SugarParameter>
            {
                new("@AsOfDate", asOfDate.Date),
                new("@RuleVersion", ProductMovementReportSqlBuilder.SnapshotRuleVersion),
            };
            var storeFilter = string.Empty;
            if (storeCodes != null)
            {
                var names = new List<string>();
                foreach (var (code, index) in storeCodes.Select((code, index) => (code, index)))
                {
                    names.Add("@Store" + index);
                    parameters.Add(new SugarParameter("@Store" + index, code));
                }
                storeFilter = names.Count == 0 ? " AND 1 = 0" : " AND StoreCode IN (" + string.Join(", ", names) + ")";
            }

            return await _db.Ado.SqlQueryAsync<ProductMovementReportSnapshotRun>(
                """
SELECT RunId, StoreCode, SalesStatLastUpdate, CompletedAtUtc
FROM (
    SELECT RunId, StoreCode, SalesStatLastUpdate, CompletedAtUtc,
        ROW_NUMBER() OVER (PARTITION BY StoreCode ORDER BY CompletedAtUtc DESC) AS RowNo
    FROM dbo.ProductMovementReportSnapshotRun
    WHERE AsOfDate = @AsOfDate AND RuleVersion = @RuleVersion AND [Status] = N'Ready'
"""
                    + storeFilter
                    + """

) latest
WHERE RowNo = 1
""",
                parameters.ToArray()
            );
        }

        /// <summary>登记一个 Building 批次；写入与发布在 <see cref="PublishAsync"/> 的同一事务里完成。</summary>
        public async Task<Guid> BeginRunAsync(DateTime asOfDate, string storeCode, DateTime nowUtc)
        {
            var runId = Guid.NewGuid();
            await _db.Ado.ExecuteCommandAsync(
                "INSERT INTO dbo.ProductMovementReportSnapshotRun (RunId, AsOfDate, StoreCode, RuleVersion, [Status], StartedAtUtc) "
                    + "VALUES (@RunId, @AsOfDate, @StoreCode, @RuleVersion, N'Building', @StartedAtUtc)",
                new SugarParameter("@RunId", runId),
                new SugarParameter("@AsOfDate", asOfDate.Date),
                new SugarParameter("@StoreCode", storeCode),
                new SugarParameter("@RuleVersion", ProductMovementReportSqlBuilder.SnapshotRuleVersion),
                new SugarParameter("@StartedAtUtc", nowUtc)
            );
            return runId;
        }

        public async Task PublishAsync(DateTime asOfDate, string storeCode, Guid runId)
        {
            var sql = ProductMovementReportSqlBuilder.BuildSnapshotWrite(asOfDate, storeCode, runId);
            await _db.Ado.ExecuteCommandAsync(sql.Sql, sql.Parameters.ToArray());
        }

        public async Task MarkFailedAsync(Guid runId, string error, DateTime nowUtc)
        {
            await _db.Ado.ExecuteCommandAsync(
                "UPDATE dbo.ProductMovementReportSnapshotRun SET [Status] = N'Failed', CompletedAtUtc = @Now, LastError = @Error "
                    + "WHERE RunId = @RunId AND [Status] = N'Building'",
                new SugarParameter("@RunId", runId),
                new SugarParameter("@Now", nowUtc),
                new SugarParameter("@Error", error.Length <= 2000 ? error : error[..2000])
            );
        }

        /// <summary>
        /// 每个门店保留当天最新两批 Ready：读取端取到批次号后才去读明细，若刚发布就删掉上一批，
        /// 正在读取的请求会读到空页。上一批再多留一个刷新周期即可避开这个竞争。
        /// 过期日期、失败批次以及超时未完成的 Building 批次一并清理。
        /// </summary>
        public async Task CleanupStoreAsync(DateTime asOfDate, string storeCode, DateTime staleBuildingBeforeUtc)
        {
            await _db.Ado.ExecuteCommandAsync(
                """
SET NOCOUNT ON;
SET XACT_ABORT ON;

WITH KeepRuns AS (
    SELECT TOP (2) RunId
    FROM dbo.ProductMovementReportSnapshotRun
    WHERE StoreCode = @StoreCode AND AsOfDate = @AsOfDate AND RuleVersion = @RuleVersion AND [Status] = N'Ready'
    ORDER BY CompletedAtUtc DESC
)
SELECT r.RunId
INTO #ExpiredRuns
FROM dbo.ProductMovementReportSnapshotRun r
WHERE r.StoreCode = @StoreCode
    AND r.RunId NOT IN (SELECT RunId FROM KeepRuns)
    AND (r.[Status] <> N'Building' OR r.StartedAtUtc < @StaleBefore);

BEGIN TRANSACTION;
DELETE s FROM dbo.ProductMovementReportSnapshot s WHERE s.RunId IN (SELECT RunId FROM #ExpiredRuns);
DELETE r FROM dbo.ProductMovementReportSnapshotRun r WHERE r.RunId IN (SELECT RunId FROM #ExpiredRuns);
COMMIT;
""",
                new SugarParameter("@StoreCode", storeCode),
                new SugarParameter("@AsOfDate", asOfDate.Date),
                new SugarParameter("@RuleVersion", ProductMovementReportSqlBuilder.SnapshotRuleVersion),
                new SugarParameter("@StaleBefore", staleBuildingBeforeUtc)
            );
        }

        /// <summary>门店停用后不再逐店清理，按日期兜底删除更早的批次。</summary>
        public async Task CleanupBeforeAsync(DateTime keepFromDate)
        {
            await _db.Ado.ExecuteCommandAsync(
                """
SET NOCOUNT ON;
SET XACT_ABORT ON;

SELECT RunId INTO #ExpiredRuns FROM dbo.ProductMovementReportSnapshotRun WHERE AsOfDate < @KeepFrom;

BEGIN TRANSACTION;
DELETE s FROM dbo.ProductMovementReportSnapshot s WHERE s.RunId IN (SELECT RunId FROM #ExpiredRuns);
DELETE r FROM dbo.ProductMovementReportSnapshotRun r WHERE r.RunId IN (SELECT RunId FROM #ExpiredRuns);
COMMIT;
""",
                new SugarParameter("@KeepFrom", keepFromDate.Date)
            );
        }
    }

    public sealed class ProductMovementReportSnapshotRun
    {
        public Guid RunId { get; set; }
        public string StoreCode { get; set; } = string.Empty;
        public DateTime? SalesStatLastUpdate { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    /// <summary>快照刷新与读取的判定规则，纯函数便于测试。</summary>
    public static class ProductMovementReportSnapshotPolicy
    {
        /// <summary>同一门店两次刷新的最小间隔：新进货单、日统计重算最迟在这个间隔后反映到快照。</summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(60);

        /// <summary>快照超过这个年龄就不再使用，读取端回到实时计算，避免后台任务停摆时长期展示旧数据。</summary>
        public static readonly TimeSpan MaxSnapshotAge = TimeSpan.FromHours(3);

        /// <summary>
        /// 需要刷新的门店：从没生成过的门店优先，其余按上次完成时间由旧到新；未到刷新间隔的不排。
        /// </summary>
        public static IReadOnlyList<string> SelectStoresDue(
            IReadOnlyList<string> activeStoreCodes,
            IReadOnlyCollection<ProductMovementReportSnapshotRun> latestRuns,
            DateTime nowUtc
        )
        {
            var latestByStore = latestRuns
                .GroupBy(run => run.StoreCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(run => run.CompletedAtUtc), StringComparer.OrdinalIgnoreCase);

            return activeStoreCodes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(store => (Store: store, CompletedAt: latestByStore.GetValueOrDefault(store)))
                .Where(item => item.CompletedAt == null || nowUtc - item.CompletedAt.Value >= RefreshInterval)
                .OrderBy(item => item.CompletedAt.HasValue ? 1 : 0)
                .ThenBy(item => item.CompletedAt ?? DateTime.MinValue)
                .ThenBy(item => item.Store, StringComparer.Ordinal)
                .Select(item => item.Store)
                .ToList();
        }

        /// <summary>
        /// 只有目标门店全部都有未过期的 Ready 快照时才走快照；缺任何一家就返回 null，由调用方实时计算，
        /// 不拼接快照与实时两种来源，避免同一页里混入不同时点的数据。
        /// </summary>
        public static IReadOnlyList<ProductMovementReportSnapshotRun>? SelectCoveringRuns(
            IReadOnlyCollection<string> targetStoreCodes,
            IReadOnlyCollection<ProductMovementReportSnapshotRun> latestRuns,
            DateTime nowUtc
        )
        {
            if (targetStoreCodes.Count == 0)
            {
                return null;
            }

            var runsByStore = latestRuns
                .Where(run => run.CompletedAtUtc.HasValue && nowUtc - run.CompletedAtUtc.Value <= MaxSnapshotAge)
                .GroupBy(run => run.StoreCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(run => run.CompletedAtUtc).First(),
                    StringComparer.OrdinalIgnoreCase
                );

            var selected = new List<ProductMovementReportSnapshotRun>();
            foreach (var store in targetStoreCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!runsByStore.TryGetValue(store, out var run))
                {
                    return null;
                }
                selected.Add(run);
            }
            return selected;
        }
    }
}
