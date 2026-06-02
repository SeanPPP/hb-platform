using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 货柜 HQ 同步统一核心入口。
    /// </summary>
    public interface IContainerHqSyncService
    {
        /// <summary>
        /// 按 HQ 变更水位增量同步货柜主表和完整明细快照。
        /// </summary>
        Task<SyncResult> SyncIncrementalAsync(DateTime? startDate = null);

        /// <summary>
        /// 兼容旧调用方的组合入口，内部仍委托增量核心。
        /// </summary>
        Task<SyncResult> SyncContainersWithDetailsFromHqAsync(DateTime? startDate = null);
    }

    /// <summary>
    /// 货柜 HQ 同步错误码。
    /// </summary>
    public static class ContainerHqSyncErrorCodes
    {
        public const string Conflict = "CONTAINER_SYNC_CONFLICT";
        public const string InvalidSourceData = "CONTAINER_SYNC_INVALID_SOURCE_DATA";
        public const string InternalError = "INTERNAL_ERROR";
    }

    /// <summary>
    /// 货柜同步并发冲突异常。
    /// </summary>
    public sealed class ContainerSyncConflictException : Exception
    {
        public ContainerSyncConflictException(string message)
            : base(message) { }
    }

    /// <summary>
    /// HQ 源数据质量异常。
    /// </summary>
    public sealed class ContainerSyncInvalidSourceDataException : Exception
    {
        public ContainerSyncInvalidSourceDataException(string message)
            : base(message) { }
    }
}
