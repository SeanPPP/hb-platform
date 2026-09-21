using BlazorApp.Api.Services.React;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class BatchProductSalesDiscountStatisticsSqlTests
{
    private static SqlSugarClient CreateSqlServerClient() => new(new ConnectionConfig
    {
        // ToSql 只生成语句，不会打开连接。
        DbType = DbType.SqlServer,
        ConnectionString = "Server=localhost;Database=BatchProductSalesDiscountStatisticsSqlTests;User Id=unused;Password=unused;TrustServerCertificate=True",
        IsAutoCloseConnection = true,
    });

    [Fact]
    public void DailyStatisticsSql_统计信息变化时不重编译()
    {
        using var db = CreateSqlServerClient();
        var day = new DateTime(2025, 12, 23);

        var sql = BatchProductSalesDiscountDailyStore.BuildDailyStatisticsSql(db, day);

        // 批量重算期间统计信息频繁过期，重编译会同步等统计更新（生产平均 5.9 秒，最长撞上 60 秒超时）。
        Assert.EndsWith("OPTION (KEEPFIXED PLAN)", sql.Key.TrimEnd());
        Assert.Equal(1, CountOccurrences(sql.Key, "OPTION ("));
        Assert.Contains("[ProductStoreDailySalesStatistic]", sql.Key);
        Assert.Contains("GROUP BY", sql.Key, StringComparison.OrdinalIgnoreCase);
        // 版本哈希只能基于已提交数据，不能带 NOLOCK。
        Assert.DoesNotContain("NOLOCK", sql.Key, StringComparison.OrdinalIgnoreCase);
        var dateValues = sql.Value.Select(parameter => parameter.Value).OfType<DateTime>().ToList();
        Assert.Contains(day, dateValues);
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
