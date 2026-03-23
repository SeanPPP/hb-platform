namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 分店同步错误信息
    /// </summary>
    public class StoreSyncError
    {
        /// <summary>
        /// 分店代码
        /// </summary>
        public string StoreCode { get; set; } = "";

        /// <summary>
        /// 分店名称（可选）
        /// </summary>
        public string? StoreName { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// 异常类型
        /// </summary>
        public string? ExceptionType { get; set; }

        /// <summary>
        /// 是否已重试
        /// </summary>
        public bool IsRetried { get; set; }

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 处理的记录数
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// 成功插入的记录数
        /// </summary>
        public int InsertedCount { get; set; }

        /// <summary>
        /// 失败的记录数
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 处理耗时（秒）
        /// </summary>
        public double DurationSeconds { get; set; }
    }

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

        public int TotalCount { get; set; }
        public int SuccessCount => AddedCount;
        public int FailedCount => ErrorCount;
        public double SuccessRate =>
            TotalCount > 0 ? (double)SuccessCount / TotalCount * 100 : 0;
        public int TotalStores { get; set; }
        public int SuccessStores { get; set; }
        public int FailedStores { get; set; }
        public List<StoreSyncError> StoreErrors { get; set; } = new();
        public int Progress { get; set; }
        public string? CurrentProcessingStore { get; set; }
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