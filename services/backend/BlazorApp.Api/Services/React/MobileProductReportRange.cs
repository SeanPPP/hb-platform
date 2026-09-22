using System.Globalization;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 复刻移动端商品报告的日期范围规则（apps/mobile/src/modules/reports/periods.ts），
/// 让服务端预热生成的缓存键与 App 真实请求完全一致。
/// 日范围的同期 = 去年同 ISO 周、同星期几；去年不足该周数时取去年最后一个 ISO 周；CompareMode 固定 ByWeek。
/// </summary>
internal static class MobileProductReportRange
{
    internal static DateRangeDto Day(DateTime date)
    {
        var day = date.Date;
        var compare = LastYearSameIsoWeekday(day);
        return new DateRangeDto
        {
            StartDate = day,
            EndDate = day,
            CompareStartDate = compare,
            CompareEndDate = compare,
            CompareMode = CompareMode.ByWeek,
        };
    }

    internal static DateTime LastYearSameIsoWeekday(DateTime date)
    {
        var day = date.Date;
        var weekYear = ISOWeek.GetYear(day);
        var week = ISOWeek.GetWeekOfYear(day);
        var compareYear = weekYear - 1;
        var compareWeek = Math.Min(week, ISOWeek.GetWeeksInYear(compareYear));
        return ISOWeek.ToDateTime(compareYear, compareWeek, day.DayOfWeek);
    }
}
