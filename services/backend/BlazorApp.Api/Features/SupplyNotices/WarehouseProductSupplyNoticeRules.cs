using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.SupplyNotices;

/// <summary>归一化后的供货说明，写库与展示都只认这一种形状。</summary>
internal sealed record NormalizedSupplyNotice(
    string SupplyPlan,
    DateTime? ExpectedFrom,
    DateTime? ExpectedTo,
    string ExpectedPrecision,
    string? StoreFacingNote,
    string? InternalNote
);

internal static class WarehouseProductSupplyNoticeRules
{
    private const int NoteMaxLength = 500;
    private const string BusinessTimeZoneId = "Australia/Sydney";

    /// <summary>
    /// 校验并归一化仓库端录入。失败返回错误文案，成功返回归一化结果。
    /// 归一化把“某月”展开成整月、把“某日”收成同一天，这样逾期判断只需要看 ExpectedTo。
    /// </summary>
    internal static (NormalizedSupplyNotice? Notice, string? Error) Normalize(
        WarehouseProductSupplyNoticeInputDto? input
    )
    {
        if (input == null)
        {
            return (null, "缺少供货说明");
        }

        var plan = input.SupplyPlan?.Trim();
        if (!WarehouseProductSupplyPlans.IsValid(plan))
        {
            return (null, "请选择后续计划：会补货、尚未确定、季节性或不再供应");
        }

        var precision = string.IsNullOrWhiteSpace(input.ExpectedPrecision)
            ? WarehouseProductSupplyExpectedPrecisions.Unknown
            : input.ExpectedPrecision.Trim();
        if (!WarehouseProductSupplyExpectedPrecisions.IsValid(precision))
        {
            return (null, "预计恢复时间的精度无效");
        }

        DateOnly? from = input.ExpectedFrom;
        DateOnly? to = input.ExpectedTo;

        // 不再供应的商品没有“预计恢复时间”，即使前端误传也一律清空，避免分店看到自相矛盾的信息。
        if (plan == WarehouseProductSupplyPlans.Discontinued)
        {
            precision = WarehouseProductSupplyExpectedPrecisions.Unknown;
        }

        switch (precision)
        {
            case WarehouseProductSupplyExpectedPrecisions.Unknown:
                from = null;
                to = null;
                break;
            case WarehouseProductSupplyExpectedPrecisions.Day:
                if (from == null)
                {
                    return (null, "请选择预计恢复订货的日期");
                }
                to = from;
                break;
            case WarehouseProductSupplyExpectedPrecisions.Range:
                if (from == null || to == null)
                {
                    return (null, "请选择预计恢复订货的起止日期");
                }
                if (from > to)
                {
                    return (null, "预计恢复订货的开始日期不能晚于结束日期");
                }
                break;
            case WarehouseProductSupplyExpectedPrecisions.Month:
                if (from == null)
                {
                    return (null, "请选择预计恢复订货的月份");
                }
                from = new DateOnly(from.Value.Year, from.Value.Month, 1);
                to = from.Value.AddMonths(1).AddDays(-1);
                break;
        }

        return (
            new NormalizedSupplyNotice(
                plan!,
                from?.ToDateTime(TimeOnly.MinValue),
                to?.ToDateTime(TimeOnly.MinValue),
                precision,
                Truncate(input.StoreFacingNote),
                Truncate(input.InternalNote)
            ),
            null
        );
    }

    /// <summary>预计时间段已过。是否“仍未恢复订货”由调用方结合商品状态判断。</summary>
    internal static bool IsOverdue(DateTime? expectedTo, DateOnly businessToday)
    {
        return expectedTo.HasValue && DateOnly.FromDateTime(expectedTo.Value) < businessToday;
    }

    /// <summary>
    /// 业务“今天”按仓库所在的悉尼时区取。API 容器跑在 UTC，直接用 UtcNow.Date 会让逾期判断在上午晚半天。
    /// </summary>
    internal static DateOnly BusinessToday(DateTime utcNow)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // 找不到时区数据时退回 UTC：最多晚半天判逾期，不影响正确性。
            return DateOnly.FromDateTime(utcNow);
        }
    }

    internal static DateOnly? ToDateOnly(DateTime? value) =>
        value.HasValue ? DateOnly.FromDateTime(value.Value) : null;

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        return trimmed.Length <= NoteMaxLength ? trimmed : trimmed[..NoteMaxLength];
    }
}
