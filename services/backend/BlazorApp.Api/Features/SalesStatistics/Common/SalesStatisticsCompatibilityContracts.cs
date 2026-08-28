using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

/// <summary>
/// 批量统计更新结果。保留历史命名空间，物理归属销售统计模块。
/// </summary>
public class BatchStatisticsUpdateResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>总天数。</summary>
    public int TotalDays { get; set; }

    /// <summary>已处理天数。</summary>
    public int ProcessedDays { get; set; }

    /// <summary>失败日期列表。</summary>
    public List<string> FailedDates { get; set; } = new();

    /// <summary>因已有运行租约而跳过的日期列表。</summary>
    public List<string> SkippedDates { get; set; } = new();

    /// <summary>结果消息。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>总月数。</summary>
    public int TotalMonths { get; set; }

    /// <summary>已处理月数。</summary>
    public int ProcessedMonths { get; set; }

    /// <summary>失败月份列表。</summary>
    public List<string> FailedMonths { get; set; } = new();

    /// <summary>任务 ID。</summary>
    public Guid TaskId { get; set; }
}

internal sealed class FullRefreshRangeExecutionResult
{
    public int ProcessedDays { get; set; }
    public List<string> SkippedDates { get; set; } = new();
    public List<string> FailedDates { get; set; } = new();
}

public class ProductStoreDailyRecalculationSubmitResult
{
    public Guid JobId { get; set; }
    /// <summary>
    /// 本次请求仍占有排队或执行状态的任务；持久队列以此向调用方暴露实际执行归属。
    /// </summary>
    public IReadOnlyCollection<Guid> ActiveJobIds { get; set; } = Array.Empty<Guid>();
    public List<DateTime> SubmittedDates { get; set; } = new();
    public List<DateTime> SkippedDates { get; set; } = new();
    public string Status { get; set; } = SalesStatisticRefreshStatus.Queued;
    public string Message { get; set; } = string.Empty;
}

/// <summary>日期范围。</summary>
public class DateRange
{
    /// <summary>开始日期。</summary>
    public DateTime StartDate { get; set; }

    /// <summary>结束日期。</summary>
    public DateTime EndDate { get; set; }

    /// <summary>天数。</summary>
    public int DayCount => (int)(EndDate - StartDate).TotalDays + 1;
}
