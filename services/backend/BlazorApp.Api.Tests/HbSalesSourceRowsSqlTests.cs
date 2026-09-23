using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// HBSales 明细来源读取在 SQL Server 上必须按实际日期重编译：结账日期是 date 列，SqlSugar 下发 datetime 参数，
/// 带参缓存的计划会聚集扫描约 1166 万行明细表（生产实测每次 11–18 秒）。
/// </summary>
public sealed class HbSalesSourceRowsSqlTests
{
    private static SqlSugarClient CreateSqlServerClient() => new(new ConnectionConfig
    {
        // ToSql 只生成语句，不会打开连接。
        DbType = DbType.SqlServer,
        ConnectionString = "Server=localhost;Database=HbSalesSourceRowsSqlTests;User Id=unused;Password=unused;TrustServerCertificate=True",
        IsAutoCloseConnection = true,
    });

    [Fact]
    public void ProductStoreDailyRowsSql_RecompilesWithTheRequestedDay()
    {
        using var db = CreateSqlServerClient();
        var day = new DateTime(2025, 10, 9);

        var sql = SalesStatisticsProductStoreDailySourceReader.BuildHBSalesProductStoreDailyRowsSql(
            db,
            day,
            day.AddDays(1)
        );

        Assert.EndsWith("OPTION (RECOMPILE)", sql.Key.TrimEnd());
        Assert.Equal(1, CountOccurrences(sql.Key, "OPTION (RECOMPILE)"));
        Assert.Contains("[B销售清单主表副本]", sql.Key);
        Assert.Contains("[B销售清单详情表副本]", sql.Key);
        Assert.Contains("AS [OrderGuid]", sql.Key);
        Assert.Contains("AS [DetailGuid]", sql.Key);
        Assert.DoesNotContain("TOP", sql.Key, StringComparison.OrdinalIgnoreCase);
        // 日期窗口与原查询一致：明细按当天，主表放宽前后 7 天；订单键仍带 HBSALES: 前缀。
        var dateValues = sql.Value.Select(parameter => parameter.Value).OfType<DateTime>().ToList();
        Assert.Contains(day, dateValues);
        Assert.Contains(day.AddDays(1), dateValues);
        Assert.Contains(day.AddDays(-7), dateValues);
        Assert.Contains(day.AddDays(8), dateValues);
        Assert.Contains(sql.Value, parameter => Equals(parameter.Value, "HBSALES:"));
    }

    [Fact]
    public void ProductStoreDailyRowsSql_BatchSnapshotKeepsRowGuardBeforeRecompileHint()
    {
        using var db = CreateSqlServerClient();

        var sql = SalesStatisticsProductStoreDailySourceReader.BuildHBSalesProductStoreDailyRowsSql(
            db,
            new DateTime(2025, 10, 1),
            new DateTime(2025, 11, 1),
            maxRows: 1000
        );

        // 批量快照的内存保护仍由 SQL 只取 maxRows + 1 行，提示追加在整条语句末尾且只出现一次。
        Assert.Contains("1001", sql.Key);
        Assert.EndsWith("OPTION (RECOMPILE)", sql.Key.TrimEnd());
        Assert.Equal(1, CountOccurrences(sql.Key, "OPTION (RECOMPILE)"));
    }

    [Fact]
    public void DiscountSnapshotHBSalesRowsSql_RecompilesWithTheRequestedDay()
    {
        using var db = CreateSqlServerClient();
        var day = new DateTime(2025, 10, 9);

        var sql = BatchProductSalesDiscountSnapshotSourceReader.BuildHBSalesRowsSql(db, day, day.AddDays(1));

        Assert.EndsWith("OPTION (RECOMPILE)", sql.Key.TrimEnd());
        Assert.Equal(1, CountOccurrences(sql.Key, "OPTION (RECOMPILE)"));
        Assert.Contains("[B销售清单主表副本]", sql.Key);
        Assert.Contains("[B销售清单详情表副本]", sql.Key);
        // 来源围栏只取数值明细 ID，物化后再生成签名键，不能在 SQL 中转成 nvarchar(max)。
        Assert.Contains("AS [HBSalesDetailId]", sql.Key);
        Assert.DoesNotContain("NVARCHAR(MAX)", sql.Key, StringComparison.OrdinalIgnoreCase);
        var dateValues = sql.Value.Select(parameter => parameter.Value).OfType<DateTime>().ToList();
        Assert.Contains(day, dateValues);
        Assert.Contains(day.AddDays(1), dateValues);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
