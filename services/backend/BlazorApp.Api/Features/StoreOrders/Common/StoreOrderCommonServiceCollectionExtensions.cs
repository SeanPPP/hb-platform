using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorApp.Api.Features.StoreOrders.Common;

internal static class StoreOrderCommonServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderCommon(this IServiceCollection services)
    {
        services.TryAddScoped<IStoreOrderActorContext, StoreOrderActorContext>();
        services.TryAddScoped<IStoreOrderAccessScope, StoreOrderAccessScope>();
        services.TryAddScoped<
            IStoreOrderProductCostCoordinator,
            StoreOrderProductCostCoordinator
        >();
        return services;
    }
}
