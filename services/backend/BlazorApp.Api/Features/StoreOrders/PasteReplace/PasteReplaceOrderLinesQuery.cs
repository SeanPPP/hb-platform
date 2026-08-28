using BlazorApp.Api.Features.StoreOrders.PasteReplace.Domain;
using BlazorApp.Api.Features.StoreOrders.PasteReplace.Infrastructure;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace;

/// <summary>
/// 只负责事务外读取和写入计划准备。
/// </summary>
internal sealed class PasteReplaceOrderLinesQuery(
    IPasteReplaceOrderLinesInfrastructure infrastructure
)
{
    internal Task<WareHouseOrder?> GetEditableOrderAsync(string orderGuid)
    {
        return infrastructure.GetEditableOrderAsync(orderGuid);
    }

    internal Task<PasteReplaceMutationPlan> PrepareAsync(
        WareHouseOrder order,
        IReadOnlyCollection<ProductQuantityDto> items,
        string targetField
    )
    {
        return infrastructure.PrepareAsync(order, items, targetField);
    }
}
