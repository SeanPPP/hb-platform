using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;

internal interface IStoreOrderCartQueryStore
{
    Task<StoreOrderCartDto?> GetFullAsync(StoreOrderCartScope scope);

    Task<StoreOrderCartDto?> GetSummaryAsync(StoreOrderCartScope scope);

    Task<StoreOrderCartMutationResultDto> GetMutationResultAsync(
        StoreOrderCartMutationWrite write
    );
}

internal interface IStoreOrderCartCommandStore
{
    Task<StoreOrderCartMutationOutcome> AddAsync(
        StoreOrderCartScope scope,
        string productCode,
        decimal quantity,
        StoreOrderProductDto? knownProduct,
        bool omitNonPositiveNewDetail
    );

    Task<StoreOrderCartMutationOutcome> SetQuantityAsync(
        StoreOrderCartScope scope,
        string productCode,
        decimal quantity,
        bool omitNonPositiveNewDetail
    );

    Task<bool> RemoveAsync(StoreOrderCartScope scope, string detailGuid);

    Task<StoreOrderCartClearOutcome> ClearAsync(StoreOrderCartScope scope);
}
