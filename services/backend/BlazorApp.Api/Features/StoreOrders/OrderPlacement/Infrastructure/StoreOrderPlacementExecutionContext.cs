using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement.Application.Ports;

namespace BlazorApp.Api.Features.StoreOrders.OrderPlacement.Infrastructure;

internal sealed class StoreOrderPlacementExecutionContext(
    IStoreOrderActorContext actorContext,
    IStoreOrderAccessPolicy? accessPolicy = null
) : IStoreOrderPlacementExecutionContext
{
    public string ActorName => actorContext.ActorName;

    public DateTime LocalNow => DateTime.Now;

    public Task<bool> CanBypassPreorderCompletionAsync() =>
        accessPolicy?.CanBypassPreorderCompletionAsync() ?? Task.FromResult(false);
}
