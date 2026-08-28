using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Invoice.Application;
using BlazorApp.Api.Features.StoreOrders.Invoice.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Invoice.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Features.StoreOrders.Invoice;

public static class StoreOrderInvoiceServiceCollectionExtensions
{
    public static IServiceCollection AddStoreOrderInvoiceSlice(
        this IServiceCollection services
    )
    {
        services.AddStoreOrderCommon();
        services.AddScoped<IStoreOrderInvoiceDetailQueryStore>(serviceProvider =>
            new StoreOrderInvoiceDetailQueryStore(
                serviceProvider.GetRequiredService<SqlSugarContext>(),
                serviceProvider.GetRequiredService<IStoreOrderAccessScope>()
            )
        );
        services.AddScoped(_ => new GetStoreOrderInvoiceDetailValidator());
        services.AddScoped(serviceProvider =>
            new GetStoreOrderInvoiceDetailHandler(
                serviceProvider.GetRequiredService<GetStoreOrderInvoiceDetailValidator>(),
                serviceProvider.GetRequiredService<IStoreOrderInvoiceDetailQueryStore>()
            )
        );
        services.AddScoped<IStoreOrderInvoiceDetailReader>(serviceProvider =>
            new StoreOrderInvoiceDetailReader(
                serviceProvider.GetRequiredService<GetStoreOrderInvoiceDetailHandler>()
            )
        );
        return services;
    }
}
