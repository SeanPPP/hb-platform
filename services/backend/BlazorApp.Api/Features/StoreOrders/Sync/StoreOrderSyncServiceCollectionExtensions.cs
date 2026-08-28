using BlazorApp.Api.Features.StoreOrders.Sync.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorApp.Api.Features.StoreOrders.Sync;

public static class StoreOrderSyncServiceCollectionExtensions
{
    public static IServiceCollection AddStoreOrderSyncSlice(
        this IServiceCollection services
    )
    {
        services.TryAddScoped<IStoreOrderSyncInfrastructure, StoreOrderSyncInfrastructure>();
        services.TryAddScoped<SyncMissingOrdersValidator>();
        services.TryAddScoped<SyncMissingOrdersQuery>();
        services.TryAddScoped<SyncMissingOrdersCommand>();
        services.TryAddScoped<SyncMissingOrdersHandler>();
        services.TryAddScoped<IStoreOrderMissingOrdersSyncExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<SyncMissingOrdersHandler>()
        );
        return services;
    }
}
