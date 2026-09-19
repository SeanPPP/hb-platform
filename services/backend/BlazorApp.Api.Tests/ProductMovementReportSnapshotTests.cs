using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ProductMovementReportSnapshotTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc);

    private static ProductMovementReportSnapshotRun Run(string store, double minutesAgo) => new()
    {
        RunId = Guid.NewGuid(),
        StoreCode = store,
        CompletedAtUtc = Now.AddMinutes(-minutesAgo),
    };

    [Fact]
    public void SelectStoresDue_ShouldPutNeverBuiltStoresFirstThenOldest()
    {
        var due = ProductMovementReportSnapshotPolicy.SelectStoresDue(
            new[] { "1002", "1003", "1004", "1005" },
            new[] { Run("1002", 90), Run("1003", 10), Run("1004", 200) },
            Now
        );

        // 1005 从未生成排最前；1003 未到 60 分钟刷新间隔不排；其余按完成时间由旧到新。
        Assert.Equal(new[] { "1005", "1004", "1002" }, due);
    }

    [Fact]
    public void SelectCoveringRuns_ShouldRequireEveryTargetStore()
    {
        var runs = new[] { Run("1002", 5), Run("1003", 5) };

        Assert.NotNull(ProductMovementReportSnapshotPolicy.SelectCoveringRuns(new[] { "1002", "1003" }, runs, Now));
        // 缺任何一家都整体回到实时计算，不混用快照与实时两种时点的数据。
        Assert.Null(ProductMovementReportSnapshotPolicy.SelectCoveringRuns(new[] { "1002", "1004" }, runs, Now));
        Assert.Null(ProductMovementReportSnapshotPolicy.SelectCoveringRuns(Array.Empty<string>(), runs, Now));
    }

    [Fact]
    public void SelectCoveringRuns_ShouldIgnoreSnapshotsOlderThanMaxAge()
    {
        var stale = Run("1002", ProductMovementReportSnapshotPolicy.MaxSnapshotAge.TotalMinutes + 1);

        // 后台任务停摆时不能长期展示旧快照。
        Assert.Null(ProductMovementReportSnapshotPolicy.SelectCoveringRuns(new[] { "1002" }, new[] { stale }, Now));
    }

    [Fact]
    public void BuildSnapshotWrite_ShouldPublishRowsAndRunStatusInOneTransaction()
    {
        var runId = Guid.NewGuid();
        var sql = ProductMovementReportSqlBuilder.BuildSnapshotWrite(new DateTime(2026, 9, 19), "1002", runId);

        Assert.StartsWith("SET XACT_ABORT ON;", sql.Sql, StringComparison.Ordinal);
        var begin = sql.Sql.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal);
        var insert = sql.Sql.IndexOf("INSERT INTO dbo.ProductMovementReportSnapshot (", StringComparison.Ordinal);
        var publish = sql.Sql.IndexOf("SET [Status] = N'Ready'", StringComparison.Ordinal);
        var commit = sql.Sql.IndexOf("COMMIT;", StringComparison.Ordinal);
        Assert.True(begin > 0 && begin < insert && insert < publish && publish < commit, "快照行与批次状态必须同一事务提交。");

        // 物化计算放在事务外，长时间计算不持有快照表锁。
        Assert.True(sql.Sql.IndexOf("INTO #FinalRows", StringComparison.Ordinal) < begin);

        // 按单店生成，且不带可信度/关键词筛选。
        Assert.Contains("AND i.StoreCode IN (@StoreCode0)", sql.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@DataCredibility", sql.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@Keyword", sql.Sql, StringComparison.Ordinal);
        Assert.Contains(sql.Parameters, p => p.ParameterName == "@RunId" && (Guid)p.Value == runId);

        // 含除法的三列按实时查询输出精度落库，避免读取时二次舍入。
        Assert.Contains("CAST(DailySalesQty30 AS decimal(18, 2))", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("CAST(GrossMarginRate90 AS decimal(18, 4))", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("CAST(EstimatedCoverDays AS decimal(18, 2))", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSnapshotRead_ShouldReuseLiveResultSetsAndStayReadOnly()
    {
        var query = ProductMovementReportSqlBuilder.NormalizeQuery(new ProductMovementReportQueryDto
        {
            Suggestion = "需要订货",
            DataCredibility = "高",
            Keyword = "milk",
        });
        var snapshot = ProductMovementReportSqlBuilder.BuildSnapshotRead(query, new[] { Guid.NewGuid(), Guid.NewGuid() }).Sql;
        var live = ProductMovementReportSqlBuilder.Build(query, null).Sql;

        Assert.Contains("FROM dbo.ProductMovementReportSnapshot\n    WHERE RunId IN (@Run0, @Run1)", snapshot, StringComparison.Ordinal);
        Assert.Contains("AND DataCredibility = @DataCredibility", snapshot, StringComparison.Ordinal);
        // 带关键词时先把命中行物化一次，分页与两段汇总复用，关键词匹配只做一遍。
        Assert.Contains("SELECT * INTO #FinalRows FROM", snapshot, StringComparison.Ordinal);
        Assert.Contains("ProductCode LIKE @Keyword", snapshot, StringComparison.Ordinal);
        Assert.Contains("COALESCE(RowSalesStatLastUpdate, @SalesStatLastUpdate) AS SalesStatLastUpdate", snapshot, StringComparison.Ordinal);

        // 不带关键词时快照已是物化结果，直接读快照表，不再整批复制进临时表。
        var withoutKeyword = ProductMovementReportSqlBuilder.BuildSnapshotRead(
            ProductMovementReportSqlBuilder.NormalizeQuery(new ProductMovementReportQueryDto { Suggestion = "需要订货" }),
            new[] { Guid.NewGuid() }
        ).Sql;
        Assert.DoesNotContain("#FinalRows", withoutKeyword, StringComparison.Ordinal);

        // 两条路径的三个结果集除数据来源外必须逐字相同，输出列、排序和汇总口径才能保证一致。
        var marker = "-- 结果集 1";
        var liveResultSets = live[live.IndexOf(marker, StringComparison.Ordinal)..];
        var snapshotResultSets = snapshot[snapshot.IndexOf(marker, StringComparison.Ordinal)..];
        Assert.Equal(liveResultSets, snapshotResultSets);
        Assert.False(ProductMovementReportSqlBuilder.ContainsWriteKeyword(snapshot));
    }
}
