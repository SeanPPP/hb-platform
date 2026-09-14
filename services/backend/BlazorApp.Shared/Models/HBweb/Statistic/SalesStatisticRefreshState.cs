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

        /// <summary>供应商汇总所消费的已完成商品日统计版本。</summary>
        [SugarColumn(Length = 64, IsNullable = true)]
        public string? SourceProductVersion { get; set; }

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
        public const string DailySales = "DailySales";
        public const string HourlySales = "HourlySales";
        public const string StoreSales = "StoreSales";
        public const string SupplierSales = "SupplierSales";
        public const string StoreSupplierSales = "StoreSupplierSales";
        public const string ProductStoreDaily = "ProductStoreDaily";
        public const string AustralianSupplierStoreSales = "AustralianSupplierStoreSales";
        public const string ChinaSupplierStoreSales = "ChinaSupplierStoreSales";
        /// <summary>完整日统计已核验并可供报表读取的发布记录。</summary>
        public const string RevenueReportPublished = "RevenueReportPublished";

        public static readonly string[] DailyAlignmentTypes =
        {
            DailySales,
            HourlySales,
            StoreSales,
            SupplierSales,
            StoreSupplierSales,
            ProductStoreDaily,
            AustralianSupplierStoreSales,
            ChinaSupplierStoreSales,
        };
    }

    public static class SalesStatisticRefreshStatus
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Pending = "Pending";
        public const string Fresh = "Fresh";
        public const string Stale = "Stale";
        public const string Failed = "Failed";
        public const string ProvisionalFresh = "ProvisionalFresh";
    }

    /// <summary>
    /// 当日全量统计的明确执行结果。跳过代表另一执行者仍持有日期租约，不能被记为成功。
    /// </summary>
    public sealed record SalesStatisticsRefreshExecutionResult(
        SalesStatisticsRefreshExecutionStatus Status,
        string? Message = null
    )
    {
        public bool IsCompleted => Status == SalesStatisticsRefreshExecutionStatus.Completed;
        public bool IsSkipped => Status == SalesStatisticsRefreshExecutionStatus.Skipped;

        public static SalesStatisticsRefreshExecutionResult Completed() =>
            new(SalesStatisticsRefreshExecutionStatus.Completed);

        public static SalesStatisticsRefreshExecutionResult Skipped(string? message = null) =>
            new(SalesStatisticsRefreshExecutionStatus.Skipped, message);
    }

    public enum SalesStatisticsRefreshExecutionStatus
    {
        Completed,
        Skipped,
    }
}
