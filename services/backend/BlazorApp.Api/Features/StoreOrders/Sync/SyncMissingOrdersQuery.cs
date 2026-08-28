using BlazorApp.Api.Features.StoreOrders.Sync.Domain;
using BlazorApp.Api.Features.StoreOrders.Sync.Infrastructure;

namespace BlazorApp.Api.Features.StoreOrders.Sync;

/// <summary>
/// 事务外完成本地/HQ 最小范围读取和目标订单判定。
/// </summary>
internal sealed class SyncMissingOrdersQuery(IStoreOrderSyncInfrastructure infrastructure)
{
    internal Task<StoreOrderSyncQueryResult> ExecuteAsync(List<string> storeCodes)
    {
        return infrastructure.PrepareAsync(storeCodes);
    }
}
