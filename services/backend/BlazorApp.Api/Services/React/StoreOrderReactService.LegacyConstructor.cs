using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders;
using BlazorApp.Api.Interfaces.React;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BlazorApp.Api.Services.React;

public partial class StoreOrderReactService
{
    /// <summary>
    /// 保留 7f5f3ee 的公开构造签名，供外部程序集、反射工具和旧测试组合根继续使用。
    /// 新生产 DI 由标记了 ActivatorUtilitiesConstructor 的窄切片构造函数承载。
    /// </summary>
    public StoreOrderReactService(
        SqlSugarContext context,
        ILogger<StoreOrderReactService> logger,
        IHttpContextAccessor httpContextAccessor,
        IOrderNumberGenerator orderNumberGenerator,
        IConfiguration configuration,
        IMapper mapper,
        IInvoiceEmailService invoiceEmailService,
        IStoreOrderLocationProductLookupService locationProductLookupService,
        IWarehouseProductChangeHistoryService changeHistoryService,
        TimeProvider? timeProvider = null
    )
        : this(
            StoreOrderLegacyFacadeComposition.Create(
                context,
                logger,
                httpContextAccessor,
                orderNumberGenerator,
                configuration,
                mapper,
                invoiceEmailService,
                locationProductLookupService,
                changeHistoryService,
                timeProvider
            )
        ) { }

    private StoreOrderReactService(StoreOrderLegacyFacadeSlices slices)
        : this(
            slices.ProductPicker,
            slices.Cart,
            slices.OrderPlacement,
            slices.ProductHistory,
            slices.Orders,
            slices.ImportPriceVariance,
            slices.OrderManagement,
            slices.PasteReplace,
            slices.Sync,
            slices.Lifecycle
        ) { }
}
