using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 销售统计刷新状态，用于记录 POSM 上传水位、统计状态和失败原因。
    /// </summary>
    [SugarTable("SalesStatisticRefreshState")]
    public class SalesStatisticRefreshState
    {
        [SugarColumn(IsPrimaryKey = true, Length = 80)]
        public string StatisticType { get; set; } = string.Empty;

        [SugarColumn(IsPrimaryKey = true)]
        public DateTime Date { get; set; }

        [SugarColumn(Length = 20, IsNullable = false)]
        public string Status { get; set; } = SalesStatisticRefreshStatus.Pending;

        [SugarColumn(IsNullable = true)]
        public DateTime? LastSourceUploadTime { get; set; }

        [SugarColumn(Length = 40, IsNullable = false)]
        public string SourceTimeZone { get; set; } = "POSM_LOCAL";

        [SugarColumn(IsNullable = true)]
        public DateTime? LastAggregatedAtUtc { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? LastCheckedAtUtc { get; set; }

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? ErrorMessage { get; set; }

        [SugarColumn(IsNullable = true)]
        public Guid? JobId { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? RequestedBy { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? RequestedAtUtc { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? StartedAtUtc { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? CompletedAtUtc { get; set; }
    }

    public static class SalesStatisticType
    {
        public const string ProductStoreDaily = "ProductStoreDaily";
    }

    public static class SalesStatisticRefreshStatus
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Pending = "Pending";
        public const string Fresh = "Fresh";
        public const string Stale = "Stale";
        public const string Failed = "Failed";
    }
}
