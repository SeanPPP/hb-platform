using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;
using BlazorApp.Api.Interfaces.React;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance;

internal static class StoreOrderImportPriceVarianceServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderImportPriceVarianceSlice(
        this IServiceCollection services
    )
    {
        services.AddStoreOrderCommon();
        // 类型保持切片内部可见，通过显式工厂让默认 DI 无需反射内部构造函数。
        services.AddScoped(serviceProvider =>
            new ImportPriceVarianceQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>()
            )
        );
        services.AddScoped(serviceProvider =>
            new ImportPriceVarianceCommandStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<IWarehouseProductChangeHistoryService>(),
                serviceProvider.GetRequiredService<IStoreOrderProductCostCoordinator>(),
                serviceProvider.GetRequiredService<IStoreOrderActorContext>()
            )
        );

        services.AddScoped(_ => new GetImportPriceVarianceValidator());
        services.AddScoped(serviceProvider =>
            new GetImportPriceVarianceHandler(
                serviceProvider.GetRequiredService<GetImportPriceVarianceValidator>(),
                serviceProvider.GetRequiredService<IStoreOrderAccessScope>(),
                serviceProvider.GetRequiredService<ImportPriceVarianceQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<GetImportPriceVarianceHandler>>()
            )
        );
        services.AddScoped(_ => new GetImportPriceVarianceDetailsValidator());
        services.AddScoped(serviceProvider =>
            new GetImportPriceVarianceDetailsHandler(
                serviceProvider.GetRequiredService<GetImportPriceVarianceDetailsValidator>(),
                serviceProvider.GetRequiredService<IStoreOrderAccessScope>(),
                serviceProvider.GetRequiredService<ImportPriceVarianceQueryStore>(),
                serviceProvider.GetRequiredService<
                    ILogger<GetImportPriceVarianceDetailsHandler>
                >()
            )
        );
        services.AddScoped(_ => new UpdateImportPriceVarianceDomesticPriceValidator());
        services.AddScoped(serviceProvider =>
            new UpdateImportPriceVarianceDomesticPriceHandler(
                serviceProvider.GetRequiredService<
                    UpdateImportPriceVarianceDomesticPriceValidator
                >(),
                serviceProvider.GetRequiredService<ImportPriceVarianceCommandStore>(),
                serviceProvider.GetRequiredService<
                    ILogger<UpdateImportPriceVarianceDomesticPriceHandler>
                >()
            )
        );
        services.AddScoped(_ => new UpdateImportPriceVarianceWarehouseImportPriceValidator());
        services.AddScoped(serviceProvider =>
            new UpdateImportPriceVarianceWarehouseImportPriceHandler(
                serviceProvider.GetRequiredService<
                    UpdateImportPriceVarianceWarehouseImportPriceValidator
                >(),
                serviceProvider.GetRequiredService<ImportPriceVarianceCommandStore>(),
                serviceProvider.GetRequiredService<IStoreOrderProductCostCoordinator>(),
                serviceProvider.GetRequiredService<
                    ILogger<UpdateImportPriceVarianceWarehouseImportPriceHandler>
                >()
            )
        );
        services.AddScoped(
            _ => new UpdateImportPriceVarianceWarehouseImportPriceBatchValidator()
        );
        services.AddScoped(serviceProvider =>
            new UpdateImportPriceVarianceWarehouseImportPriceBatchHandler(
                serviceProvider.GetRequiredService<
                    UpdateImportPriceVarianceWarehouseImportPriceBatchValidator
                >(),
                serviceProvider.GetRequiredService<ImportPriceVarianceCommandStore>(),
                serviceProvider.GetRequiredService<IStoreOrderProductCostCoordinator>(),
                serviceProvider.GetRequiredService<
                    ILogger<UpdateImportPriceVarianceWarehouseImportPriceBatchHandler>
                >()
            )
        );

        services.AddScoped<IStoreOrderImportPriceVarianceSlice>(serviceProvider =>
            new StoreOrderImportPriceVarianceSlice(
                serviceProvider.GetRequiredService<GetImportPriceVarianceHandler>(),
                serviceProvider.GetRequiredService<GetImportPriceVarianceDetailsHandler>(),
                serviceProvider.GetRequiredService<
                    UpdateImportPriceVarianceDomesticPriceHandler
                >(),
                serviceProvider.GetRequiredService<
                    UpdateImportPriceVarianceWarehouseImportPriceHandler
                >(),
                serviceProvider.GetRequiredService<
                    UpdateImportPriceVarianceWarehouseImportPriceBatchHandler
                >()
            )
        );
        return services;
    }
}
