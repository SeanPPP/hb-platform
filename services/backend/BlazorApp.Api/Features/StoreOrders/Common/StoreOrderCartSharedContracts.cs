using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Common;

internal sealed record StoreOrderCartScope(
    string StoreCode,
    string? CartOwnerUserGuid
);

internal sealed record StoreOrderCartSubmissionSnapshot(
    string OrderGuid,
    int? FlowStatus
);

internal interface IStoreOrderCartOwnerScope
{
    bool IsWarehouseStaffOnly { get; }

    StoreOrderCartScope Resolve(string? storeCode);
}

internal interface IStoreOrderCartCommandCoordinator
{
    Task<ApiResponse<T>> ExecuteAsync<T>(
        StoreOrderCartScope scope,
        Func<Task<ApiResponse<T>>> command
    );
}

/// <summary>
/// Cart 扫码入口与 ProductPicker 之间的共享窄查询端口。
/// </summary>
public interface IStoreOrderCartProductLookup
{
    Task<ApiResponse<StoreOrderScanLookupResultDto>> LookupAsync(
        StoreOrderScanLookupRequestDto request
    );
}

/// <summary>
/// OrderPlacement 提交购物车时使用的共享窄持久化端口。
/// </summary>
internal interface IStoreOrderCartPlacementPort
{
    Task<StoreOrderCartSubmissionSnapshot?> GetActiveForSubmissionAsync(
        StoreOrderCartScope scope
    );

    Task<int> CountActiveItemsAsync(string orderGuid);

    /// <summary>
    /// 购物车里已暂停供货（仓库下架）的商品货号，用于提交前拦截并告知分店具体是哪几行。
    /// 仓库侧代下单不受限，此时恒返回空。
    /// </summary>
    Task<IReadOnlyList<string>> GetSupplyPausedItemLabelsAsync(string orderGuid);

    Task<int> CompareExchangeSubmitAsync(
        StoreOrderCartSubmissionSnapshot snapshot,
        string orderNo,
        string? remarks,
        DateTime submittedAt,
        string submittedBy
    );
}
