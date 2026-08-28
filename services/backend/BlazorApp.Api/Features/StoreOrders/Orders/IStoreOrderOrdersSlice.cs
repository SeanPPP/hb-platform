using BlazorApp.Api.Features.StoreOrders.Orders.Application;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders;

public interface IStoreOrderOrdersSlice
{
    Task<PagedListReactDto<StoreOrderListItemDto>> GetOrderListAsync(
        StoreOrderListFilterDto filter
    );

    Task<ApiResponse<StoreOrderDetailDto?>> GetOrderDetailAsync(
        string orderGuid,
        StoreOrderDetailQueryDto? query = null
    );

    Task<ApiResponse<StoreOrderCartDto?>> GetOrderDetailFullAsync(string orderGuid);

    Task<ApiResponse<List<string>>> GetOrderDetailProductCodesAsync(string orderGuid);

    Task<ApiResponse<StoreOrderStoreContactDto>> UpdateStoreContactAsync(
        UpdateStoreOrderStoreContactDto request
    );

    Task<ApiResponse<List<BranchDto>>> GetUsedBranchesAsync();

    Task<ApiResponse<List<UnmatchedStoreOrderGroupDto>>> GetUnmatchedStoreOrderGroupsAsync();

    Task<ApiResponse<BatchMapStoreOrderStoreCodeResultDto>> BatchMapStoreOrderStoreCodeAsync(
        BatchMapStoreOrderStoreCodeDto request
    );
}

internal sealed class StoreOrderOrdersSlice(
    GetOrderListHandler getOrderListHandler,
    GetOrderDetailHandler getOrderDetailHandler,
    GetOrderDetailFullHandler getOrderDetailFullHandler,
    GetOrderDetailProductCodesHandler getOrderDetailProductCodesHandler,
    UpdateStoreContactHandler updateStoreContactHandler,
    GetUsedBranchesHandler getUsedBranchesHandler,
    GetUnmatchedStoreOrderGroupsHandler getUnmatchedStoreOrderGroupsHandler,
    BatchMapStoreOrderStoreCodeHandler batchMapStoreOrderStoreCodeHandler
) : IStoreOrderOrdersSlice
{
    public Task<PagedListReactDto<StoreOrderListItemDto>> GetOrderListAsync(
        StoreOrderListFilterDto filter
    )
    {
        return getOrderListHandler.HandleAsync(new GetOrderListQuery(filter));
    }

    public Task<ApiResponse<StoreOrderDetailDto?>> GetOrderDetailAsync(
        string orderGuid,
        StoreOrderDetailQueryDto? query = null
    )
    {
        return getOrderDetailHandler.HandleAsync(new GetOrderDetailQuery(orderGuid, query));
    }

    public Task<ApiResponse<StoreOrderCartDto?>> GetOrderDetailFullAsync(string orderGuid)
    {
        return getOrderDetailFullHandler.HandleAsync(
            new GetOrderDetailFullQuery(orderGuid)
        );
    }

    public Task<ApiResponse<List<string>>> GetOrderDetailProductCodesAsync(string orderGuid)
    {
        return getOrderDetailProductCodesHandler.HandleAsync(
            new GetOrderDetailProductCodesQuery(orderGuid)
        );
    }

    public Task<ApiResponse<StoreOrderStoreContactDto>> UpdateStoreContactAsync(
        UpdateStoreOrderStoreContactDto request
    )
    {
        return updateStoreContactHandler.HandleAsync(new UpdateStoreContactCommand(request));
    }

    public Task<ApiResponse<List<BranchDto>>> GetUsedBranchesAsync()
    {
        return getUsedBranchesHandler.HandleAsync(new GetUsedBranchesQuery());
    }

    public Task<ApiResponse<List<UnmatchedStoreOrderGroupDto>>> GetUnmatchedStoreOrderGroupsAsync()
    {
        return getUnmatchedStoreOrderGroupsHandler.HandleAsync(
            new GetUnmatchedStoreOrderGroupsQuery()
        );
    }

    public Task<ApiResponse<BatchMapStoreOrderStoreCodeResultDto>> BatchMapStoreOrderStoreCodeAsync(
        BatchMapStoreOrderStoreCodeDto request
    )
    {
        return batchMapStoreOrderStoreCodeHandler.HandleAsync(
            new BatchMapStoreOrderStoreCodeCommand(request)
        );
    }
}
