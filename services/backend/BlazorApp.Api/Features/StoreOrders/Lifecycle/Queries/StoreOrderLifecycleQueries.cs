namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Queries;

internal sealed record GetStoreOrderLifecycleQuery(string OrderGuid);

internal sealed record GetStoreOrderLifecyclesQuery(IReadOnlyList<string> OrderGuids);
