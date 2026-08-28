using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Application;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance;

public interface IStoreOrderImportPriceVarianceSlice
{
    Task<ApiResponse<StoreOrderImportPriceVarianceResultDto>> GetImportPriceVarianceAsync(
        StoreOrderImportPriceVarianceQueryDto query
    );

    Task<
        ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>
    > GetImportPriceVarianceDetailsAsync(StoreOrderImportPriceVarianceDetailQueryDto query);

    Task<
        ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
    > UpdateImportPriceVarianceDomesticPriceAsync(
        StoreOrderImportPriceVarianceDomesticPriceUpdateDto request
    );

    Task<
        ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
    > UpdateImportPriceVarianceWarehouseImportPriceAsync(
        StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto request
    );

    Task<
        ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
    > UpdateImportPriceVarianceWarehouseImportPriceBatchAsync(
        StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto request
    );
}

internal sealed class StoreOrderImportPriceVarianceSlice(
    GetImportPriceVarianceHandler getImportPriceVarianceHandler,
    GetImportPriceVarianceDetailsHandler getImportPriceVarianceDetailsHandler,
    UpdateImportPriceVarianceDomesticPriceHandler updateDomesticPriceHandler,
    UpdateImportPriceVarianceWarehouseImportPriceHandler updateWarehouseImportPriceHandler,
    UpdateImportPriceVarianceWarehouseImportPriceBatchHandler updateWarehouseImportPriceBatchHandler
) : IStoreOrderImportPriceVarianceSlice
{
    public Task<ApiResponse<StoreOrderImportPriceVarianceResultDto>> GetImportPriceVarianceAsync(
        StoreOrderImportPriceVarianceQueryDto query
    )
    {
        return getImportPriceVarianceHandler.HandleAsync(
            new GetImportPriceVarianceQuery(query)
        );
    }

    public Task<
        ApiResponse<StoreOrderImportPriceVarianceDetailResultDto>
    > GetImportPriceVarianceDetailsAsync(StoreOrderImportPriceVarianceDetailQueryDto query)
    {
        return getImportPriceVarianceDetailsHandler.HandleAsync(
            new GetImportPriceVarianceDetailsQuery(query)
        );
    }

    public Task<
        ApiResponse<StoreOrderImportPriceVarianceDomesticPriceUpdateResultDto>
    > UpdateImportPriceVarianceDomesticPriceAsync(
        StoreOrderImportPriceVarianceDomesticPriceUpdateDto request
    )
    {
        return updateDomesticPriceHandler.HandleAsync(
            new UpdateImportPriceVarianceDomesticPriceCommand(request)
        );
    }

    public Task<
        ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceUpdateResultDto>
    > UpdateImportPriceVarianceWarehouseImportPriceAsync(
        StoreOrderImportPriceVarianceWarehouseImportPriceUpdateDto request
    )
    {
        return updateWarehouseImportPriceHandler.HandleAsync(
            new UpdateImportPriceVarianceWarehouseImportPriceCommand(request)
        );
    }

    public Task<
        ApiResponse<StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateResultDto>
    > UpdateImportPriceVarianceWarehouseImportPriceBatchAsync(
        StoreOrderImportPriceVarianceWarehouseImportPriceBatchUpdateDto request
    )
    {
        return updateWarehouseImportPriceBatchHandler.HandleAsync(
            new UpdateImportPriceVarianceWarehouseImportPriceBatchCommand(request)
        );
    }
}
