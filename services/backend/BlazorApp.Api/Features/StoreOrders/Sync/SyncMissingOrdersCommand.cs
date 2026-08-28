using BlazorApp.Api.Features.StoreOrders.Sync.Domain;
using BlazorApp.Api.Features.StoreOrders.Sync.Infrastructure;

namespace BlazorApp.Api.Features.StoreOrders.Sync;

/// <summary>
/// 同步写入的唯一事务入口。
/// </summary>
internal sealed class SyncMissingOrdersCommand(IStoreOrderSyncInfrastructure infrastructure)
{
    internal Task<StoreOrderSyncWriteResult> ExecuteAsync(StoreOrderSyncPreparation preparation)
    {
        return infrastructure.PersistAsync(preparation);
    }
}
