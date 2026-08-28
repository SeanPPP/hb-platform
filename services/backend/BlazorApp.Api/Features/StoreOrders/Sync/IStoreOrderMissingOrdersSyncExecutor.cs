using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Sync;

/// <summary>
/// 供 singleton 同步 Job 在新建 scope 内解析的窄执行端口。
/// </summary>
public interface IStoreOrderMissingOrdersSyncExecutor
{
    Task<SyncMissingOrdersResultDto> SyncMissingOrdersFromHqAsync(
        SyncMissingOrdersRequestDto? request
    );
}
