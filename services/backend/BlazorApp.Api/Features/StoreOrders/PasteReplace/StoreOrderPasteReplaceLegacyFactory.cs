using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.PasteReplace.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace;

public static class StoreOrderPasteReplaceLegacyFactory
{
    public static IStoreOrderPasteReplaceExecutor Create(
        SqlSugarContext context,
        IHttpContextAccessor httpContextAccessor
    )
    {
        var infrastructure = new PasteReplaceOrderLinesInfrastructure(
            context,
            httpContextAccessor
        );

        return new PasteReplaceOrderLinesHandler(
            new PasteReplaceOrderLinesValidator(),
            new PasteReplaceOrderLinesQuery(infrastructure),
            new PasteReplaceOrderLinesCommand(infrastructure),
            NullLogger<PasteReplaceOrderLinesHandler>.Instance
        );
    }
}
