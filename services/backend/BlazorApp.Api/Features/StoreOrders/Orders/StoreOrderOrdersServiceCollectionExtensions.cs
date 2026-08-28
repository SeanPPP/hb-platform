using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Orders.Application;
using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Features.StoreOrders.Orders;

internal static class StoreOrderOrdersServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderOrdersSlice(
        this IServiceCollection services
    )
    {
        services.AddStoreOrderCommon();
        services.AddScoped<IStoreOrderOrdersHqConnectionFactory>(serviceProvider =>
            new StoreOrderOrdersHqConnectionFactory(
                serviceProvider.GetRequiredService<IConfiguration>()
            )
        );
        services.AddScoped(serviceProvider =>
            new StoreOrderStoreIdentityReader(
                serviceProvider.GetRequiredService<SqlSugarContext>()
            )
        );
        services.AddScoped(serviceProvider =>
            new StoreOrderListQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<IStoreOrderAccessScope>(),
                serviceProvider.GetRequiredService<IStoreOrderActorContext>()
            )
        );
        services.AddScoped(serviceProvider =>
            new StoreOrderDetailQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<IStoreOrderAccessScope>()
            )
        );
        services.AddScoped(serviceProvider =>
            new StoreOrderLookupQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<StoreOrderStoreIdentityReader>(),
                serviceProvider.GetRequiredService<IStoreOrderOrdersHqConnectionFactory>(),
                serviceProvider.GetRequiredService<ILogger<StoreOrderLookupQueryStore>>()
            )
        );
        services.AddScoped(serviceProvider =>
            new StoreOrderCommandStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<IStoreOrderActorContext>(),
                serviceProvider.GetRequiredService<StoreOrderStoreIdentityReader>()
            )
        );

        services.AddScoped(_ => new GetOrderListValidator());
        services.AddScoped(serviceProvider =>
            new GetOrderListHandler(
                serviceProvider.GetRequiredService<GetOrderListValidator>(),
                serviceProvider.GetRequiredService<StoreOrderListQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<GetOrderListHandler>>()
            )
        );
        services.AddScoped(_ => new GetOrderDetailValidator());
        services.AddScoped(serviceProvider =>
            new GetOrderDetailHandler(
                serviceProvider.GetRequiredService<GetOrderDetailValidator>(),
                serviceProvider.GetRequiredService<StoreOrderDetailQueryStore>()
            )
        );
        services.AddScoped(_ => new GetOrderDetailFullValidator());
        services.AddScoped(serviceProvider =>
            new GetOrderDetailFullHandler(
                serviceProvider.GetRequiredService<GetOrderDetailFullValidator>(),
                serviceProvider.GetRequiredService<StoreOrderDetailQueryStore>()
            )
        );
        services.AddScoped(_ => new GetOrderDetailProductCodesValidator());
        services.AddScoped(serviceProvider =>
            new GetOrderDetailProductCodesHandler(
                serviceProvider.GetRequiredService<GetOrderDetailProductCodesValidator>(),
                serviceProvider.GetRequiredService<StoreOrderDetailQueryStore>()
            )
        );
        services.AddScoped(_ => new UpdateStoreContactValidator());
        services.AddScoped(serviceProvider =>
            new UpdateStoreContactHandler(
                serviceProvider.GetRequiredService<UpdateStoreContactValidator>(),
                serviceProvider.GetRequiredService<StoreOrderCommandStore>()
            )
        );
        services.AddScoped(_ => new GetUsedBranchesValidator());
        services.AddScoped(serviceProvider =>
            new GetUsedBranchesHandler(
                serviceProvider.GetRequiredService<GetUsedBranchesValidator>(),
                serviceProvider.GetRequiredService<StoreOrderLookupQueryStore>(),
                serviceProvider.GetRequiredService<ILogger<GetUsedBranchesHandler>>()
            )
        );
        services.AddScoped(_ => new GetUnmatchedStoreOrderGroupsValidator());
        services.AddScoped(serviceProvider =>
            new GetUnmatchedStoreOrderGroupsHandler(
                serviceProvider.GetRequiredService<GetUnmatchedStoreOrderGroupsValidator>(),
                serviceProvider.GetRequiredService<StoreOrderLookupQueryStore>(),
                serviceProvider.GetRequiredService<
                    ILogger<GetUnmatchedStoreOrderGroupsHandler>
                >()
            )
        );
        services.AddScoped(_ => new BatchMapStoreOrderStoreCodeValidator());
        services.AddScoped(serviceProvider =>
            new BatchMapStoreOrderStoreCodeHandler(
                serviceProvider.GetRequiredService<BatchMapStoreOrderStoreCodeValidator>(),
                serviceProvider.GetRequiredService<StoreOrderCommandStore>(),
                serviceProvider.GetRequiredService<
                    ILogger<BatchMapStoreOrderStoreCodeHandler>
                >()
            )
        );

        services.AddScoped<IStoreOrderOrdersSlice>(serviceProvider =>
            new StoreOrderOrdersSlice(
                serviceProvider.GetRequiredService<GetOrderListHandler>(),
                serviceProvider.GetRequiredService<GetOrderDetailHandler>(),
                serviceProvider.GetRequiredService<GetOrderDetailFullHandler>(),
                serviceProvider.GetRequiredService<GetOrderDetailProductCodesHandler>(),
                serviceProvider.GetRequiredService<UpdateStoreContactHandler>(),
                serviceProvider.GetRequiredService<GetUsedBranchesHandler>(),
                serviceProvider.GetRequiredService<GetUnmatchedStoreOrderGroupsHandler>(),
                serviceProvider.GetRequiredService<BatchMapStoreOrderStoreCodeHandler>()
            )
        );
        return services;
    }
}
