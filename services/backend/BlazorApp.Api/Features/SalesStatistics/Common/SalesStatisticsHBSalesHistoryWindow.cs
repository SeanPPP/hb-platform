namespace BlazorApp.Api.Services;

/// <summary>
/// HBSalesRecord 已核验可参与商品/分店日统计的历史窗口。
/// 该范围是来源数据边界，不等同于任意年份都可读取 HBSales。
/// </summary>
internal static class SalesStatisticsHBSalesHistoryWindow
{
    internal static readonly DateTime StartDate = new(2024, 9, 14);
    internal static readonly DateTime EndExclusive = new(2026, 1, 1);

    internal static bool Includes(DateTime date)
    {
        var day = date.Date;
        return day >= StartDate && day < EndExclusive;
    }
}
