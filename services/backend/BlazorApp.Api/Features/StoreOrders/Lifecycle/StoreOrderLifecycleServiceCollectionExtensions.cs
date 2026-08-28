using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.BatchUpdateOrderStatus;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.CompleteOrder;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.StartPicking;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Commands.UpdateOrderStatus;
using BlazorApp.Api.Features.StoreOrders.Lifecycle.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorApp.Api.Features.StoreOrders.Lifecycle;

internal static class StoreOrderLifecycleServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderLifecycleSlice(
        this IServiceCollection services
    )
    {
        services.AddStoreOrderCommon();

        // 两个端口必须解析到同一个 scoped 持久化实例，确保批量 Query 复用 Command 事务。
        services.TryAddScoped<SqlSugarStoreOrderLifecyclePersistence>();
        services.TryAddScoped<IStoreOrderLifecycleQueryHandler>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlSugarStoreOrderLifecyclePersistence>()
        );
        services.TryAddScoped<IStoreOrderLifecycleCommandStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlSugarStoreOrderLifecyclePersistence>()
        );
        services.TryAddScoped<
            IStoreOrderLifecycleExecutionContext,
            StoreOrderLifecycleExecutionContext
        >();

        services.TryAddScoped<CompleteOrderValidator>();
        services.TryAddScoped<CompleteOrderHandler>();
        services.TryAddScoped<StartPickingValidator>();
        services.TryAddScoped<StartPickingHandler>();
        services.TryAddScoped<UpdateOrderStatusValidator>();
        services.TryAddScoped<UpdateOrderStatusHandler>();
        services.TryAddScoped<BatchUpdateOrderStatusValidator>();
        services.TryAddScoped<BatchUpdateOrderStatusHandler>();
        services.TryAddScoped<IStoreOrderLifecycleSlice, StoreOrderLifecycleSlice>();
        return services;
    }
}
