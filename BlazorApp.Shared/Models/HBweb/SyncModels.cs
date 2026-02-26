namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 同步结果
    /// </summary>
    public class SyncResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public int AddedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int DeletedCount { get; set; }
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// 同步历史记录
    /// </summary>
    public class SyncHistory
    {
        public DateTime SyncTime { get; set; }
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = "";
        public TimeSpan Duration { get; set; }
    }
}