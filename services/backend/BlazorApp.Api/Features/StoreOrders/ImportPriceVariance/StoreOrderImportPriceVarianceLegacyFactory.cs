using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;
using BlazorApp.Api.Interfaces.React;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance;

internal static class StoreOrderImportPriceVarianceLegacyFactory
{
    internal static IStoreOrderImportPriceVarianceSlice Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor,
        IWarehouseProductChangeHistoryService changeHistoryService
    )
    {
        var actorContext = new StoreOrderActorContext(httpContextAccessor);
        var accessScope = new StoreOrderAccessScope(context, actorContext);
        var productCostCoordinator = new StoreOrderProductCostCoordinator(context);
        var queryStore = new ImportPriceVarianceQueryStore(context);
        var commandStore = new ImportPriceVarianceCommandStore(
            context,
            changeHistoryService,
            productCostCoordinator,
            actorContext
        );

        return new StoreOrderImportPriceVarianceSlice(
            new GetImportPriceVarianceHandler(
                new GetImportPriceVarianceValidator(),
                accessScope,
                queryStore,
                NullLogger<GetImportPriceVarianceHandler>.Instance
            ),
            new GetImportPriceVarianceDetailsHandler(
                new GetImportPriceVarianceDetailsValidator(),
                accessScope,
                queryStore,
                NullLogger<GetImportPriceVarianceDetailsHandler>.Instance
            ),
            new UpdateImportPriceVarianceDomesticPriceHandler(
                new UpdateImportPriceVarianceDomesticPriceValidator(),
                commandStore,
                NullLogger<UpdateImportPriceVarianceDomesticPriceHandler>.Instance
            ),
            new UpdateImportPriceVarianceWarehouseImportPriceHandler(
                new UpdateImportPriceVarianceWarehouseImportPriceValidator(),
                commandStore,
                productCostCoordinator,
                NullLogger<UpdateImportPriceVarianceWarehouseImportPriceHandler>.Instance
            ),
            new UpdateImportPriceVarianceWarehouseImportPriceBatchHandler(
                new UpdateImportPriceVarianceWarehouseImportPriceBatchValidator(),
                commandStore,
                productCostCoordinator,
                NullLogger<UpdateImportPriceVarianceWarehouseImportPriceBatchHandler>.Instance
            )
        );
    }
}
