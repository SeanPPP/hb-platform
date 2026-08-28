using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker;

internal static class StoreOrderProductPickerServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderProductPickerSlice(
        this IServiceCollection services
    )
    {
        services.AddScoped<ProductPickerProductEnricher>();
        services.AddScoped<IProductPickerLocationLookup, ProductPickerLocationLookup>();
        services.AddScoped<ProductPickerPageQueryStore>();
        services.AddScoped<ProductPickerBatchQueryStore>();
        services.AddScoped<ProductPickerScanQueryStore>();
        services.AddScoped<ProductPickerPageCacheStore>();
        services.AddScoped<ProductPickerHomePageCacheStore>();

        services.AddScoped<GetProductPickerPageValidator>();
        services.AddScoped<GetProductPickerPageHandler>();
        services.AddScoped<BatchLookupProductsValidator>();
        services.AddScoped<BatchLookupProductsHandler>();
        services.AddScoped<ScanLookupProductsValidator>();
        services.AddScoped<ScanLookupProductsHandler>();
        services.AddScoped<GetHomePageWarmUpPageValidator>();
        services.AddScoped<GetHomePageWarmUpPageHandler>();
        services.AddScoped<GetHomePageCachePageValidator>();
        services.AddScoped<GetHomePageCachePageHandler>();
        services.AddScoped<WarmUpProductPickerHomePageValidator>();
        services.AddScoped<WarmUpProductPickerHomePageHandler>();

        services.AddScoped<StoreOrderProductPickerSlice>();
        services.AddScoped<IStoreOrderProductPickerSlice>(serviceProvider =>
            serviceProvider.GetRequiredService<StoreOrderProductPickerSlice>()
        );
        services.AddScoped<IStoreOrderCartProductLookup>(serviceProvider =>
            serviceProvider.GetRequiredService<StoreOrderProductPickerSlice>()
        );
        return services;
    }
}
