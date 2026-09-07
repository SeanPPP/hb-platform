using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// Executive revenue 页面的一次一致性快照。Branches 用于排行与 KPI，Hourly 和 Weekly
/// 使用 focus 分店范围；三者由同一个已提交读取快照生成。
/// </summary>
public sealed class RevenueReportSnapshotDto
{
    public List<ExecutiveBranchPerformanceDto> Branches { get; set; } = new();
    public List<ExecutiveHourlyTrafficDto> Hourly { get; set; } = new();
    public List<WeeklyPerformanceHierarchyDto> Weekly { get; set; } = new();

    /// <summary>当前返回的数据是否没有可证明的完整快照。</summary>
    public bool StatisticsPending { get; set; }

    /// <summary>当前期营业额、分时和周层级是否可读。</summary>
    public bool CurrentPeriodPending { get; set; }

    /// <summary>同期数据缺口只影响同比字段，不应隐藏当前期数据。</summary>
    public bool ComparePeriodPending { get; set; }
    public bool HourlyCurrentPending { get; set; }
    public bool HourlyComparePending { get; set; }
    public bool WeeklyComparePending { get; set; }

    /// <summary>已有完整快照可读，但后台正在发布下一版时为 true。</summary>
    public bool RefreshInProgress { get; set; }

    public string StatisticStatus { get; set; } = SalesStatisticRefreshStatus.Pending;
    public string? StatisticMessage { get; set; }
    public DateTime? StatisticUpdatedAt { get; set; }
    public string CacheVersion { get; set; } = string.Empty;
    public int StatisticsExpectedBranchCount { get; set; }
    public int StatisticsSnapshotBranchCount { get; set; }
}
