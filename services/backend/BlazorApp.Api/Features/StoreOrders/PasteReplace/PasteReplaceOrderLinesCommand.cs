using BlazorApp.Api.Features.StoreOrders.PasteReplace.Domain;
using BlazorApp.Api.Features.StoreOrders.PasteReplace.Infrastructure;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace;

/// <summary>
/// 粘贴替换的唯一事务入口。
/// </summary>
internal sealed class PasteReplaceOrderLinesCommand(
    IPasteReplaceOrderLinesInfrastructure infrastructure
)
{
    internal Task ExecuteAsync(PasteReplaceMutationPlan plan)
    {
        return infrastructure.ApplyAsync(plan);
    }
}
