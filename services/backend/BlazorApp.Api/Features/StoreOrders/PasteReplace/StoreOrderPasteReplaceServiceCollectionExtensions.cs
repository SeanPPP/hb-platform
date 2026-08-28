using BlazorApp.Api.Features.StoreOrders.PasteReplace.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace;

public static class StoreOrderPasteReplaceServiceCollectionExtensions
{
    public static IServiceCollection AddStoreOrderPasteReplaceSlice(
        this IServiceCollection services
    )
    {
        services.TryAddScoped<
            IPasteReplaceOrderLinesInfrastructure,
            PasteReplaceOrderLinesInfrastructure
        >();
        services.TryAddScoped<PasteReplaceOrderLinesValidator>();
        services.TryAddScoped<PasteReplaceOrderLinesQuery>();
        services.TryAddScoped<PasteReplaceOrderLinesCommand>();
        services.TryAddScoped<PasteReplaceOrderLinesHandler>();
        services.TryAddScoped<IStoreOrderPasteReplaceExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<PasteReplaceOrderLinesHandler>()
        );
        return services;
    }
}
