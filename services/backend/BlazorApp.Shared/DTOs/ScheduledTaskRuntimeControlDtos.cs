namespace BlazorApp.Shared.DTOs
{
    public class ScheduledTaskRuntimeControlStatusDto
    {
        public bool SchedulerEnabled { get; set; }
        public bool SchedulerEnabledByConfig { get; set; }
        public bool EffectiveSchedulerEnabled { get; set; }
        public string CurrentInstanceId { get; set; } = string.Empty;
        public string? ActiveInstanceId { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
        public int RunningLeaseCount { get; set; }
        public int RecentDuplicateSkipCount { get; set; }
        public List<ScheduledTaskInstanceStateDto> KnownInstances { get; set; } = new();
    }

    public class ScheduledTaskRuntimeControlUpdateDto
    {
        public bool SchedulerEnabled { get; set; }
        public string? ActiveInstanceId { get; set; }
    }

    public class ScheduledTaskInstanceStateDto
    {
        public string InstanceId { get; set; } = string.Empty;
        public string? HostName { get; set; }
        public int ProcessId { get; set; }
        public bool SchedulerEnabledByConfig { get; set; }
        public DateTime LastSeenAtUtc { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsActive { get; set; }
    }
}
