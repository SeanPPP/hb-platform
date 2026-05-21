using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogController(ICatalogService catalogService) : ControllerBase
{
    private const int MaxPageSize = 1000;

    [HttpGet("stores")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<StoreDto>>>> GetStores(
        CancellationToken cancellationToken)
    {
        var stores = await catalogService.GetStoresAsync(cancellationToken);
        return Ok(ApiResult<IReadOnlyList<StoreDto>>.Ok(stores));
    }

    [HttpGet("sellable-items")]
    public async Task<ActionResult<ApiResult<SellableItemsResponse>>> GetSellableItems(
        [FromQuery] string storeCode,
        [FromQuery] DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return BadRequest(ApiResult<SellableItemsResponse>.Fail("STORE_CODE_REQUIRED", "storeCode 不能为空"));
        }

        var response = await catalogService.GetSellableItemsAsync(storeCode, since, cancellationToken);
        return response is null
            ? NotFound(ApiResult<SellableItemsResponse>.Fail("STORE_NOT_FOUND", "门店不存在或已停用"))
            : Ok(ApiResult<SellableItemsResponse>.Ok(response));
    }

    [HttpGet("sellable-items/page")]
    public async Task<ActionResult<ApiResult<CatalogSyncPageResponse>>> GetSellableItemsPage(
        [FromQuery] string storeCode,
        [FromQuery] DateTimeOffset? since,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return BadRequest(ApiResult<CatalogSyncPageResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (pageSize <= 0 || pageSize > MaxPageSize)
        {
            return BadRequest(ApiResult<CatalogSyncPageResponse>.Fail("PAGE_SIZE_INVALID", $"pageSize must be between 1 and {MaxPageSize}"));
        }

        var response = await catalogService.GetSellableItemsPageAsync(
            storeCode,
            since,
            cursor,
            pageSize,
            cancellationToken);

        return response is null
            ? NotFound(ApiResult<CatalogSyncPageResponse>.Fail("STORE_NOT_FOUND", "store was not found or inactive"))
            : Ok(ApiResult<CatalogSyncPageResponse>.Ok(response));
    }

    [HttpPost("sellable-items/compare")]
    public async Task<ActionResult<ApiResult<CatalogCompareResponse>>> CompareSellableItems(
        [FromBody] CatalogCompareRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(ApiResult<CatalogCompareResponse>.Fail("COMPARE_REQUEST_REQUIRED", "request body is required"));
        }

        if (string.IsNullOrWhiteSpace(request.StoreCode))
        {
            return BadRequest(ApiResult<CatalogCompareResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        var response = await catalogService.CompareSellableItemsAsync(request, cancellationToken);
        return response is null
            ? NotFound(ApiResult<CatalogCompareResponse>.Fail("STORE_NOT_FOUND", "store was not found or inactive"))
            : Ok(ApiResult<CatalogCompareResponse>.Ok(response));
    }

    [HttpGet("sellable-items/lookup")]
    public async Task<ActionResult<ApiResult<CatalogLookupResponse>>> LookupSellableItem(
        [FromQuery] string storeCode,
        [FromQuery] string? lookupCode,
        [FromQuery] string? lookupCodeNormalized,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return BadRequest(ApiResult<CatalogLookupResponse>.Fail("STORE_CODE_REQUIRED", "storeCode is required"));
        }

        if (string.IsNullOrWhiteSpace(lookupCode) && string.IsNullOrWhiteSpace(lookupCodeNormalized))
        {
            return BadRequest(ApiResult<CatalogLookupResponse>.Fail("LOOKUP_CODE_REQUIRED", "lookupCode or lookupCodeNormalized is required"));
        }

        var response = await catalogService.LookupSellableItemAsync(
            storeCode,
            lookupCode,
            lookupCodeNormalized,
            cancellationToken);

        return response is null
            ? NotFound(ApiResult<CatalogLookupResponse>.Fail("STORE_NOT_FOUND", "store was not found or inactive"))
            : Ok(ApiResult<CatalogLookupResponse>.Ok(response));
    }
}
