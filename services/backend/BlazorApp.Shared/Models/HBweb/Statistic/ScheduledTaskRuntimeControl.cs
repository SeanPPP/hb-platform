using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// 定时任务运行时控制，支持后台动态切换当前调度实例。
    /// </summary>
    public class ScheduledTaskRuntimeControl : BaseEntity
    {
        public const string DefaultId = "default";

        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string Id { get; set; } = DefaultId;

        [SugarColumn(IsNullable = false)]
        public bool SchedulerEnabled { get; set; } = true;

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? ActiveInstanceId { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? UpdatedByUser { get; set; }
    }

    /// <summary>
    /// 定时任务实例心跳，用于后台识别可切换的 API 实例。
    /// </summary>
    public class ScheduledTaskInstanceState : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, Length = 120)]
        public string InstanceId { get; set; } = string.Empty;

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? HostName { get; set; }

        [SugarColumn(IsNullable = false)]
        public int ProcessId { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool SchedulerEnabledByConfig { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 统计任务数据库租约，用于跨实例防止同一任务范围被重复刷新。
    /// </summary>
    [SugarTable("ScheduledTaskLease")]
    public class ScheduledTaskLease : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, Length = 100)]
        public string TaskType { get; set; } = string.Empty;

        [SugarColumn(IsPrimaryKey = true, Length = 120)]
        public string ScopeKey { get; set; } = string.Empty;

        [SugarColumn(Length = 20, IsNullable = false)]
        public string Status { get; set; } = ScheduledTaskLeaseStatus.Running;

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? OwnerInstanceId { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? LeaseToken { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? LeaseUntilUtc { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

        [SugarColumn(IsNullable = true)]
        public DateTime? CompletedAtUtc { get; set; }

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? LastError { get; set; }

        [SugarColumn(IsNullable = false)]
        public int DuplicateSkipCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public static class ScheduledTaskLeaseStatus
    {
        public const string Running = "Running";
        public const string Success = "Success";
        public const string Failed = "Failed";
    }
}
