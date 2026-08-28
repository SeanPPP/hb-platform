using BlazorApp.Api.Features.StoreOrders.Cart;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance;
using BlazorApp.Api.Features.StoreOrders.Lifecycle;
using BlazorApp.Api.Features.StoreOrders.OrderManagement;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement;
using BlazorApp.Api.Features.StoreOrders.Orders;
using BlazorApp.Api.Features.StoreOrders.PasteReplace;
using BlazorApp.Api.Features.StoreOrders.ProductHistory;
using BlazorApp.Api.Features.StoreOrders.ProductPicker;
using BlazorApp.Api.Features.StoreOrders.Sync;
using BlazorApp.Api.Interfaces.React;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Services.React;

internal static class StoreOrderReactServiceRegistration
{
    internal static IServiceCollection AddStoreOrderReactFacade(this IServiceCollection services)
    {
        // 默认容器不会依据 ActivatorUtilitiesConstructor 在两个公开构造器间做选择，
        // 因此生产组合根必须显式调用窄切片构造器，避免兼容构造器同时可解析时启动失败。
        services.AddScoped<IStoreOrderReactService>(provider =>
            new StoreOrderReactService(
                provider.GetRequiredService<IStoreOrderProductPickerSlice>(),
                provider.GetRequiredService<IStoreOrderCartSlice>(),
                provider.GetRequiredService<IStoreOrderPlacementSlice>(),
                provider.GetRequiredService<IStoreOrderProductHistorySlice>(),
                provider.GetRequiredService<IStoreOrderOrdersSlice>(),
                provider.GetRequiredService<IStoreOrderImportPriceVarianceSlice>(),
                provider.GetRequiredService<IStoreOrderOrderManagementSlice>(),
                provider.GetRequiredService<IStoreOrderPasteReplaceExecutor>(),
                provider.GetRequiredService<IStoreOrderMissingOrdersSyncExecutor>(),
                provider.GetRequiredService<IStoreOrderLifecycleSlice>()
            )
        );
        return services;
    }
}
