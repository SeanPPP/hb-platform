namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 货柜 HQ 同步参数。
    /// </summary>
    public class ContainerHqSyncOptions
    {
        /// <summary>
        /// HQ 侧单批读取的最大行数。
        /// </summary>
        public int HqReadBatchSize { get; set; } = 5000;

        /// <summary>
        /// 本地按货柜分块写入的最大货柜数。
        /// </summary>
        public int LocalContainerBatchSize { get; set; } = 200;

        /// <summary>
        /// Fastest 批量写入的分页大小。
        /// </summary>
        public int WriteBatchSize { get; set; } = 1000;

        /// <summary>
        /// 数据库命令超时秒数。
        /// </summary>
        public int CommandTimeoutSeconds { get; set; } = 1800;

        /// <summary>
        /// 默认增量回看天数。
        /// </summary>
        public int DefaultIncrementalDays { get; set; } = 30;

        /// <summary>
        /// 最近成功任务的回放重叠天数。
        /// </summary>
        public int ReplayOverlapDays { get; set; } = 7;

        /// <summary>
        /// single-flight 等待秒数。
        /// </summary>
        public int LockWaitSeconds { get; set; } = 5;
    }
}
