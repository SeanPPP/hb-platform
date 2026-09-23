using BlazorApp.Api.Services.React;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class HbSalesCoverageSqlTests
{
    private static SqlSugarClient CreateSqlServerClient() => new(new ConnectionConfig
    {
        // ToSql 只生成语句，不会打开连接。
        DbType = DbType.SqlServer,
        ConnectionString = "Server=localhost;Database=HbSalesCoverageSqlTests;User Id=unused;Password=unused;TrustServerCertificate=True",
        IsAutoCloseConnection = true,
    });

    [Fact]
    public void SqlServerCoverageSql_RecompilesWithTheRequestedDay()
    {
        using var db = CreateSqlServerClient();
        var day = new DateTime(2025, 9, 22);

        var sql = SalesDashboardReactService.BuildHbSalesStoreSalesCoverageSql(
            db,
            day,
            day,
            new List<string> { "1013", "1017" }
        );

        // 带参编译的计划会退化为全表联查（生产实测 11.3 秒），必须按实际日期重编译。
        Assert.EndsWith("OPTION (RECOMPILE)", sql.Key.TrimEnd());
        Assert.Equal(1, CountOccurrences(sql.Key, "OPTION (RECOMPILE)"));
        Assert.Contains("[B销售清单主表副本]", sql.Key);
        Assert.Contains("[B销售清单详情表副本]", sql.Key);
        Assert.Contains("GROUP BY", sql.Key, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'1013'", sql.Key);
        Assert.Contains("'1017'", sql.Key);
        // 日期窗口与原查询一致：明细按当天，主表放宽前后 7 天。
        var dateValues = sql.Value.Select(parameter => parameter.Value).OfType<DateTime>().ToList();
        Assert.Contains(day, dateValues);
        Assert.Contains(day.AddDays(1), dateValues);
        Assert.Contains(day.AddDays(-7), dateValues);
        Assert.Contains(day.AddDays(8), dateValues);
    }

    [Fact]
    public void CoverageQuery_WithoutBranchFilterStillGroupsByDateAndBranch()
    {
        using var db = CreateSqlServerClient();

        var sql = SalesDashboardReactService.BuildHbSalesStoreSalesCoverageSql(
            db,
            new DateTime(2025, 9, 1),
            new DateTime(2025, 9, 7),
            new List<string>()
        );

        Assert.DoesNotContain(" IN (", sql.Key, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AS [BranchCode]", sql.Key);
        Assert.EndsWith("OPTION (RECOMPILE)", sql.Key.TrimEnd());
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
