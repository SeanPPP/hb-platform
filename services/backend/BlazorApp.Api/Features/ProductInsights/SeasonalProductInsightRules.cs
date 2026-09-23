using System.Globalization;

namespace BlazorApp.Api.Features.ProductInsights;

/// <summary>季节商品查询中不依赖数据库的口径判断，服务与控制器共用，便于回归测试。</summary>
public static class SeasonalProductInsightRules
{
    /// <summary>季节统计默认从 8 月 1 日开始；1–7 月仍属于上一年 8 月起的季节。</summary>
    public const int SeasonStartMonth = 8;

    /// <summary>单个区间最长一年，防止任意长区间拖慢查询。</summary>
    public const int MaxRangeDays = 366;

    public const int MaxCandidates = 20;

    public static DateTime SeasonStart(DateTime storeToday)
    {
        var year = storeToday.Month >= SeasonStartMonth ? storeToday.Year : storeToday.Year - 1;
        return new DateTime(year, SeasonStartMonth, 1);
    }

    public static string FormatDate(DateTime date) =>
        date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 解析一个区间：起止都缺省时用默认区间；只给一端或格式错误、起点晚于终点、超过一年都视为无效。
    /// </summary>
    public static bool TryResolveRange(
        string? start,
        string? end,
        (DateTime Start, DateTime End) fallback,
        out (DateTime Start, DateTime End) range,
        out string? error
    )
    {
        range = fallback;
        error = null;
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
        {
            return true;
        }

        if (!TryParseDate(start, out var parsedStart) || !TryParseDate(end, out var parsedEnd))
        {
            error = "日期区间必须同时提供起止日期，格式为 YYYY-MM-DD";
            return false;
        }
        if (parsedStart > parsedEnd)
        {
            error = "开始日期不能晚于结束日期";
            return false;
        }
        if ((parsedEnd - parsedStart).TotalDays + 1 > MaxRangeDays)
        {
            error = $"日期区间不能超过 {MaxRangeDays} 天";
            return false;
        }

        range = (parsedStart, parsedEnd);
        return true;
    }

    /// <summary>理论存货 = 进货区间累计进货 − 销售区间累计销量；两个区间可以不同，允许为负。</summary>
    public static decimal TheoreticalStock(decimal inboundQuantity, decimal salesQuantity) =>
        inboundQuantity - salesQuantity;

    public static bool IsWarehouseSource(string? localSupplierCode) =>
        string.Equals(localSupplierCode?.Trim(), "200", StringComparison.Ordinal);

    private static bool TryParseDate(string? value, out DateTime date) =>
        DateTime.TryParseExact(
            value?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date
        );
}
