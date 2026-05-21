using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

public interface IRemoteLookupRefreshService
{
    Task<RemoteLookupRefreshResult> RefreshLookupAsync(
        string storeCode,
        string lookupCode,
        CancellationToken cancellationToken = default);
}

public sealed record RemoteLookupRefreshResult(
    string StoreCode,
    string LookupCode,
    bool Found,
    SellableItemDto? Item,
    int DeletedCount)
{
    public bool Updated => Found && Item is not null;

    public bool Deleted => !Found;
}

public sealed class RemoteLookupRefreshService(
    ILocalCatalogRepository localCatalogRepository,
    ICatalogApiClient catalogApiClient) : IRemoteLookupRefreshService
{
    public async Task<RemoteLookupRefreshResult> RefreshLookupAsync(
        string storeCode,
        string lookupCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(lookupCode);

        var response = await catalogApiClient.LookupSellableItemAsync(storeCode, lookupCode, cancellationToken);
        if (response is { Found: true })
        {
            if (response.Item is null)
            {
                throw new CatalogApiException("Catalog lookup response was found but did not include an item.");
            }

            var item = response.Item.ToSellableItemDto();
            await localCatalogRepository.UpsertSellableItemsAsync([item], cancellationToken);
            return new RemoteLookupRefreshResult(storeCode, lookupCode, true, item, 0);
        }

        var deleteLookupCode = response is null
            ? lookupCode
            : GetDeleteLookupCode(response);
        var deletedCount = await localCatalogRepository.DeleteByLookupCodesAsync(
            storeCode,
            [deleteLookupCode],
            cancellationToken);

        return new RemoteLookupRefreshResult(storeCode, lookupCode, false, null, deletedCount);
    }

    private static string GetDeleteLookupCode(CatalogLookupResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.LookupCodeNormalized))
        {
            return response.LookupCodeNormalized;
        }

        return response.LookupCode;
    }
}
