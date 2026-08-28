using BlazorApp.Api.Features.StoreOrders.Cart;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance;
using BlazorApp.Api.Features.StoreOrders.Invoice;
using BlazorApp.Api.Features.StoreOrders.Lifecycle;
using BlazorApp.Api.Features.StoreOrders.OrderManagement;
using BlazorApp.Api.Features.StoreOrders.OrderPlacement;
using BlazorApp.Api.Features.StoreOrders.Orders;
using BlazorApp.Api.Features.StoreOrders.PasteReplace;
using BlazorApp.Api.Features.StoreOrders.ProductHistory;
using BlazorApp.Api.Features.StoreOrders.ProductPicker;
using BlazorApp.Api.Features.StoreOrders.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Features.StoreOrders;

internal static class StoreOrderFeatureServiceCollectionExtensions
{
    internal static IServiceCollection AddStoreOrderFeatures(this IServiceCollection services)
    {
        services.AddStoreOrderAccessPolicy();
        services.AddStoreOrderImportPriceVarianceSlice();
        services.AddStoreOrderInvoiceSlice();
        services.AddStoreOrderProductHistorySlice();
        services.AddStoreOrderOrdersSlice();
        services.AddStoreOrderLifecycleSlice();
        services.AddStoreOrderOrderManagementSlice();
        services.AddStoreOrderProductPickerSlice();
        services.AddStoreOrderCartSlice();
        services.AddStoreOrderPlacementSlice();
        services.AddStoreOrderPasteReplaceSlice();
        services.AddStoreOrderSyncSlice();
        return services;
    }
}
