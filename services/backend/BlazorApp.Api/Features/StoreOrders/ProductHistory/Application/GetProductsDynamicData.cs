using System.Diagnostics;
using BlazorApp.Api.Features.StoreOrders.ProductHistory.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductHistory.Application;

internal sealed record GetProductsDynamicDataQuery(StoreOrderDynamicDataRequestDto Request);

internal sealed class GetProductsDynamicDataValidator
{
    internal ProductsDynamicDataQueryInput Validate(GetProductsDynamicDataQuery query)
    {
        return new ProductsDynamicDataQueryInput(
            query.Request.StoreCode,
            ProductHistoryRules.NormalizeProductCodes(query.Request.ProductCodes),
            query.Request.IncludeSales
        );
    }
}

internal sealed class GetProductsDynamicDataHandler(
    GetProductsDynamicDataValidator validator,
    IProductHistoryQueryStore queryStore,
    ILogger<GetProductsDynamicDataHandler> logger
)
{
    internal async Task<ApiResponse<List<StoreOrderDynamicDataDto>>> HandleAsync(
        GetProductsDynamicDataQuery query
    )
    {
        var totalSw = Stopwatch.StartNew();
        try
        {
            var input = validator.Validate(query);
            if (input.ProductCodes.Count == 0)
            {
                return new ApiResponse<List<StoreOrderDynamicDataDto>>
                {
                    Success = true,
                    Data = new List<StoreOrderDynamicDataDto>(),
                };
            }

            var readResult = await queryStore.GetProductsDynamicDataAsync(input);
            totalSw.Stop();
            logger.LogInformation(
                "[shop-home-perf] stage=dynamic-data.service.done storeCode={StoreCode} requestCount={RequestCount} cartRows={CartRows} latestDateRows={LatestDateRows} historyRows={HistoryRows} cartMs={CartMs} latestDateMs={LatestDateMs} historyMs={HistoryMs} salesContextLoaded={SalesContextLoaded} salesRows={SalesRows} salesMs={SalesMs} totalMs={TotalMs}",
                input.StoreCode,
                input.ProductCodes.Count,
                readResult.CartRows,
                readResult.LatestDateRows,
                readResult.HistoryRows,
                readResult.CartMilliseconds,
                readResult.LatestDateMilliseconds,
                readResult.HistoryMilliseconds,
                readResult.SalesContextLoaded,
                readResult.SalesRows,
                readResult.SalesMilliseconds,
                totalSw.ElapsedMilliseconds
            );

            return new ApiResponse<List<StoreOrderDynamicDataDto>>
            {
                Success = true,
                Data = readResult.Items,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetProductsDynamicDataAsync failed");
            return new ApiResponse<List<StoreOrderDynamicDataDto>>
            {
                Success = false,
                Message = ex.Message,
            };
        }
    }
}
