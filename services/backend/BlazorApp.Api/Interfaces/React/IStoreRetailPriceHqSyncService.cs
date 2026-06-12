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

        Task<ApiResponse<SyncRetailPriceFromHqResult>> SyncForPageAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        );
    }
}
