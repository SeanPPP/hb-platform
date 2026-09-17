using System.Globalization;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.PosmSalesOrders;

/// <summary>移动端销售订单查询中不依赖数据库的口径判断，供控制器与回归测试共享。</summary>
public static class PosmSalesOrderMobileRules
{
    /// <summary>查询区间上限；含首尾计算。销售订单明细表逐单扫描，区间过长会拖慢手机端翻页。</summary>
    public const int MaxRangeDays = 30;

    public const int DefaultPageSize = 20;

    /// <summary>手机端不允许一次拉取过多订单，超出按上限截断。</summary>
    public const int MaxPageSize = 100;

    public const string ScopeAllStores = "all-stores";
    public const string ScopeAuthorizedStores = "authorized-stores";

    /// <summary>区间天数含首尾：同一天为 1 天。</summary>
    public static int CountDays(DateTime startDate, DateTime endDate) =>
        (endDate.Date - startDate.Date).Days + 1;

    public static bool TryParseDate(string? raw, out DateTime date) =>
        DateTime.TryParseExact(
            raw?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date
        );

    public static string FormatDate(DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 区间必须成对提供、格式正确、先后有序且不超过上限。
    /// 后端与前端同时把关，拼接请求绕过前端也不能放大扫描范围。
    /// </summary>
    public static bool TryResolveRange(
        string? startDate,
        string? endDate,
        out (DateTime StartDate, DateTime EndDate) range,
        out string? error,
        out string? errorCode
    )
    {
        range = default;
        error = null;
        errorCode = null;
        if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
        {
            error = "开始日期和结束日期必须同时提供。";
            errorCode = "INVALID_DATE_RANGE";
            return false;
        }
        if (!TryParseDate(startDate, out var parsedStart) || !TryParseDate(endDate, out var parsedEnd))
        {
            error = "日期必须使用 YYYY-MM-DD 格式。";
            errorCode = "INVALID_DATE_RANGE";
            return false;
        }
        if (parsedStart.Date > parsedEnd.Date)
        {
            error = "开始日期不能晚于结束日期。";
            errorCode = "INVALID_DATE_RANGE";
            return false;
        }
        if (CountDays(parsedStart, parsedEnd) > MaxRangeDays)
        {
            error = $"查询区间不能超过 {MaxRangeDays} 天。";
            errorCode = "DATE_RANGE_TOO_LONG";
            return false;
        }

        range = (parsedStart.Date, parsedEnd.Date);
        return true;
    }

    /// <summary>只允许 asc / desc，其余一律按最新在前。</summary>
    public static string NormalizeSortDirection(string? raw) =>
        string.Equals(raw?.Trim(), "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

    /// <summary>All 等同于不过滤，避免把 -1 传进 Status 比较。</summary>
    public static OrderType? NormalizeOrderType(OrderType? orderType) =>
        orderType is null or OrderType.All ? null : orderType;

    public static int NormalizePageNumber(int pageNumber) => Math.Max(1, pageNumber);

    public static int NormalizePageSize(int pageSize) =>
        pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    public static List<string> NormalizeBranchCodes(IEnumerable<string>? branchCodes) =>
        (branchCodes ?? Array.Empty<string>())
            .Select(code => code?.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 把用户请求的分店与授权范围求交集。
    /// 授权范围为 null 表示全分店，直接采用请求值；否则请求值只能收窄授权范围，不能放大。
    /// 返回 null 表示不过滤分店；返回空列表表示请求的分店全部越权，调用方应返回空结果。
    /// </summary>
    public static List<string>? ResolveEffectiveBranchCodes(
        IEnumerable<string>? requestedBranchCodes,
        List<string>? authorizedBranchCodes
    )
    {
        var requested = NormalizeBranchCodes(requestedBranchCodes);
        if (authorizedBranchCodes == null)
        {
            return requested.Count > 0 ? requested : null;
        }
        if (requested.Count == 0)
        {
            return authorizedBranchCodes;
        }
        return requested
            .Where(code => authorizedBranchCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
