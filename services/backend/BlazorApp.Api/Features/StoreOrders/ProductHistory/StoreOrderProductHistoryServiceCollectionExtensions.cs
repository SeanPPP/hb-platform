using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory;

internal static class StoreOrderProductHistoryServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderProductHistorySlice(
        this IServiceCollection services
    )
    {
        services.AddStoreOrderCommon();

        // 显式工厂允许切片类型继续保持 internal，不依赖默认 DI 反射其构造函数。
        services.AddScoped(serviceProvider =>
            new ProductSalesHistoryQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<ILogger<ProductSalesHistoryQueryStore>>(),
                serviceProvider.GetService<TimeProvider>()
            )
        );
        services.AddScoped(serviceProvider =>
            new ProductsDynamicDataQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<IStoreOrderActorContext>(),
                serviceProvider.GetRequiredService<ProductSalesHistoryQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<ProductsDynamicDataQueryStore>>()
            )
        );
        services.AddScoped(serviceProvider =>
            new ProductOrderHistoryQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<ProductSalesHistoryQueryStore>()
            )
        );
        services.AddScoped<IProductHistoryQueryStore>(serviceProvider =>
            new ProductHistoryQueryStore(
                serviceProvider.GetRequiredService<ProductsDynamicDataQueryStore>(),
                serviceProvider.GetRequiredService<ProductOrderHistoryQueryStore>(),
                serviceProvider.GetRequiredService<ProductSalesHistoryQueryStore>()
            )
        );

        services.AddScoped(_ => new GetProductsDynamicDataValidator());
        services.AddScoped(serviceProvider =>
            new GetProductsDynamicDataHandler(
                serviceProvider.GetRequiredService<GetProductsDynamicDataValidator>(),
                serviceProvider.GetRequiredService<IProductHistoryQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<GetProductsDynamicDataHandler>>()
            )
        );
        services.AddScoped(_ => new GetProductOrderHistoryValidator());
        services.AddScoped(serviceProvider =>
            new GetProductOrderHistoryHandler(
                serviceProvider.GetRequiredService<GetProductOrderHistoryValidator>(),
                serviceProvider.GetRequiredService<IProductHistoryQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<GetProductOrderHistoryHandler>>()
            )
        );
        services.AddScoped(_ => new GetProductActivityHistoryValidator());
        services.AddScoped(serviceProvider =>
            new GetProductActivityHistoryHandler(
                serviceProvider.GetRequiredService<GetProductActivityHistoryValidator>(),
                serviceProvider.GetRequiredService<IProductHistoryQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<GetProductActivityHistoryHandler>>()
            )
        );
        services.AddScoped(_ => new GetSalesSinceLastArrivalValidator());
        services.AddScoped(serviceProvider =>
            new GetSalesSinceLastArrivalHandler(
                serviceProvider.GetRequiredService<GetSalesSinceLastArrivalValidator>(),
                serviceProvider.GetRequiredService<IProductHistoryQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<GetSalesSinceLastArrivalHandler>>()
            )
        );
        services.AddScoped(_ => new GetSalesSinceLastArrivalSummaryValidator());
        services.AddScoped(serviceProvider =>
            new GetSalesSinceLastArrivalSummaryHandler(
                serviceProvider.GetRequiredService<GetSalesSinceLastArrivalSummaryValidator>(),
                serviceProvider.GetRequiredService<IProductHistoryQueryStore>(),
                serviceProvider.GetRequiredService<
                    ILogger<GetSalesSinceLastArrivalSummaryHandler>
                >()
            )
        );

        services.AddScoped<IStoreOrderProductHistorySlice>(serviceProvider =>
            new StoreOrderProductHistorySlice(
                serviceProvider.GetRequiredService<GetProductsDynamicDataHandler>(),
                serviceProvider.GetRequiredService<GetProductOrderHistoryHandler>(),
                serviceProvider.GetRequiredService<GetProductActivityHistoryHandler>(),
                serviceProvider.GetRequiredService<GetSalesSinceLastArrivalHandler>(),
                serviceProvider.GetRequiredService<GetSalesSinceLastArrivalSummaryHandler>()
            )
        );
        return services;
    }
}
