using BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Queries;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;

internal interface IStoreOrderLifecycleQueryHandler
{
    Task<StoreOrderLifecycleSnapshot?> HandleAsync(GetStoreOrderLifecycleQuery query);

    Task<IReadOnlyList<StoreOrderLifecycleSnapshot>> HandleAsync(
        GetStoreOrderLifecyclesQuery query
    );
}
