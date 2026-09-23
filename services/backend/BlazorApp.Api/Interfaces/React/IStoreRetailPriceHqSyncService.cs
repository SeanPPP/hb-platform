using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 分店零售价 HQ 同步统一服务。
    /// 页面入口、旧全量同步和旧增量同步都应委托到这里，避免字段映射和事务语义分叉。
    /// </summary>
    public interface IStoreRetailPriceHqSyncService
    {
        Task<SyncResult> SyncFullAsync(List<string>? selectedStoreCodes = null);

        Task<SyncResult> SyncIncrementalAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        );

        /// <summary>
        /// 旧增量接口的全分店水位续跑：不限分店、不设结束日期，从水位（为空时用默认窗口）同步到当前。
        /// 只有这个入口写出的成功日志才会被当作下一次增量的水位。
        /// </summary>
        Task<SyncResult> SyncAllStoresFromWatermarkAsync(DateTime? watermarkStart = null);

        Task<ApiResponse<SyncRetailPriceFromHqResult>> SyncForPageAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        );
    }
}
