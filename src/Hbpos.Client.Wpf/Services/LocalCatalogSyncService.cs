using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

public interface ILocalCatalogSyncService
{
    Task<LocalCatalogSyncResult> FullSyncAsync(
        string storeCode,
        CancellationToken cancellationToken = default);
}

public sealed record LocalCatalogSyncResult(
    string StoreCode,
    int ComparePages,
    int RemotePages,
    int UpsertedCount,
    int DeletedCount);

public sealed class LocalCatalogSyncService(
    ILocalCatalogRepository localCatalogRepository,
    ICatalogApiClient catalogApiClient) : ILocalCatalogSyncService
{
    private const int PageSize = 500;

    public async Task<LocalCatalogSyncResult> FullSyncAsync(
        string storeCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);

        var comparePages = 0;
        var remotePages = 0;
        var upsertedCount = 0;
        var deletedCount = 0;
        string? afterLookupCodeNormalized = null;

        while (true)
        {
            var localPage = await localCatalogRepository.LoadSellableItemComparePageAsync(
                storeCode,
                afterLookupCodeNormalized,
                PageSize,
                cancellationToken);

            if (localPage.Count == 0)
            {
                break;
            }

            afterLookupCodeNormalized = localPage[^1].LookupCodeNormalized;
            var request = new CatalogCompareRequest(
                storeCode,
                localPage.Select(row => row.ToCompareVersion()).ToArray());
            var response = await catalogApiClient.CompareSellableItemsAsync(request, cancellationToken);
            var applied = await ApplyChangesAsync(
                storeCode,
                response.UpsertedLookups,
                response.DeletedLookups,
                cancellationToken);

            comparePages++;
            upsertedCount += applied.UpsertedCount;
            deletedCount += applied.DeletedCount;
        }

        string? cursor = null;
        while (true)
        {
            var response = await catalogApiClient.GetSellableItemsPageAsync(
                storeCode,
                cursor,
                PageSize,
                cancellationToken);
            var applied = await ApplyChangesAsync(
                storeCode,
                response.Items,
                response.DeletedLookups,
                cancellationToken);

            remotePages++;
            upsertedCount += applied.UpsertedCount;
            deletedCount += applied.DeletedCount;

            if (!response.HasMore)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(response.NextCursor))
            {
                throw new CatalogApiException("Catalog API indicated more pages but did not return a next cursor.");
            }

            cursor = response.NextCursor;
        }

        return new LocalCatalogSyncResult(
            storeCode,
            comparePages,
            remotePages,
            upsertedCount,
            deletedCount);
    }

    private async Task<(int UpsertedCount, int DeletedCount)> ApplyChangesAsync(
        string storeCode,
        IReadOnlyList<CatalogLookupItemDto> upsertedLookups,
        IReadOnlyList<DeletedLookupDto> deletedLookups,
        CancellationToken cancellationToken)
    {
        var upsertItems = upsertedLookups
            .Select(item => item.ToSellableItemDto())
            .ToArray();
        if (upsertItems.Length > 0)
        {
            await localCatalogRepository.UpsertSellableItemsAsync(upsertItems, cancellationToken);
        }

        var deletedCodes = deletedLookups
            .Select(GetDeleteLookupCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deletedCount = deletedCodes.Length == 0
            ? 0
            : await localCatalogRepository.DeleteByLookupCodesAsync(storeCode, deletedCodes, cancellationToken);

        return (upsertItems.Length, deletedCount);
    }

    private static string GetDeleteLookupCode(DeletedLookupDto deletedLookup)
    {
        return string.IsNullOrWhiteSpace(deletedLookup.LookupCodeNormalized)
            ? deletedLookup.LookupCode
            : deletedLookup.LookupCodeNormalized;
    }
}
