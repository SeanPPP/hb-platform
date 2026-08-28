using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker;

public interface IStoreOrderProductPickerSlice : IStoreOrderCartProductLookup
{
    Task<PagedListReactDto<StoreOrderProductDto>> GetPagedListAsync(
        StoreOrderFilterDto filter
    );

    Task<ApiResponse<List<StoreOrderBatchLookupItemDto>>> BatchLookupProductsAsync(
        StoreOrderBatchLookupRequestDto request
    );

    Task<ApiResponse<StoreOrderScanLookupResultDto>> ScanLookupProductsAsync(
        StoreOrderScanLookupRequestDto request
    );

    Task<PagedListReactDto<StoreOrderProductDto>> GetHomePageWarmUpPageAsync(
        int pageSize,
        CancellationToken cancellationToken = default
    );

    Task<PagedListReactDto<StoreOrderProductDto>> GetHomePageCachePageAsync(
        int pageSize,
        CancellationToken cancellationToken = default
    );

    Task WarmUpHomePageAsync();
}

internal sealed class StoreOrderProductPickerSlice(
    GetProductPickerPageHandler getPageHandler,
    BatchLookupProductsHandler batchLookupHandler,
    ScanLookupProductsHandler scanLookupHandler,
    GetHomePageWarmUpPageHandler warmUpPageHandler,
    GetHomePageCachePageHandler cachePageHandler,
    WarmUpProductPickerHomePageHandler homePageWarmUpHandler
) : IStoreOrderProductPickerSlice
{
    public Task<PagedListReactDto<StoreOrderProductDto>> GetPagedListAsync(
        StoreOrderFilterDto filter
    )
    {
        return getPageHandler.HandleAsync(new GetProductPickerPageQuery(filter));
    }

    public Task<ApiResponse<List<StoreOrderBatchLookupItemDto>>> BatchLookupProductsAsync(
        StoreOrderBatchLookupRequestDto request
    )
    {
        return batchLookupHandler.HandleAsync(new BatchLookupProductsQuery(request));
    }

    public Task<ApiResponse<StoreOrderScanLookupResultDto>> ScanLookupProductsAsync(
        StoreOrderScanLookupRequestDto request
    )
    {
        return scanLookupHandler.HandleAsync(new ScanLookupProductsQuery(request));
    }

    public Task<ApiResponse<StoreOrderScanLookupResultDto>> LookupAsync(
        StoreOrderScanLookupRequestDto request
    )
    {
        return ScanLookupProductsAsync(request);
    }

    public Task<PagedListReactDto<StoreOrderProductDto>> GetHomePageWarmUpPageAsync(
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        return warmUpPageHandler.HandleAsync(
            new GetHomePageWarmUpPageQuery(pageSize),
            cancellationToken
        );
    }

    public Task<PagedListReactDto<StoreOrderProductDto>> GetHomePageCachePageAsync(
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        return cachePageHandler.HandleAsync(
            new GetHomePageCachePageQuery(pageSize),
            cancellationToken
        );
    }

    public Task WarmUpHomePageAsync()
    {
        return homePageWarmUpHandler.HandleAsync(
            new WarmUpProductPickerHomePageCommand()
        );
    }
}
