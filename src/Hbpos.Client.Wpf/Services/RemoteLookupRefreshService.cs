using System.Diagnostics;
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

        var stopwatch = Stopwatch.StartNew();
        Log($"refresh start storeCode={storeCode} lookupCode={lookupCode}");
        try
        {
            Log($"catalog api lookup dispatch storeCode={storeCode} lookupCode={lookupCode}");
            var response = await catalogApiClient.LookupSellableItemAsync(storeCode, lookupCode, cancellationToken);
            if (response is { Found: true })
            {
                if (response.Item is null)
                {
                    throw new CatalogApiException("Catalog lookup response was found but did not include an item.");
                }

                var item = response.Item.ToSellableItemDto();
                await localCatalogRepository.UpsertSellableItemsAsync([item], cancellationToken);
                stopwatch.Stop();
                Log($"refresh completed storeCode={storeCode} lookupCode={lookupCode} found=True upserted=1 deleted=0 productCode={item.ProductCode} referenceCode={item.ReferenceCode ?? "<null>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
                return new RemoteLookupRefreshResult(storeCode, lookupCode, true, item, 0);
            }

            var deleteLookupCode = response is null
                ? lookupCode
                : GetDeleteLookupCode(response);
            var deletedCount = await localCatalogRepository.DeleteByLookupCodesAsync(
                storeCode,
                [deleteLookupCode],
                cancellationToken);

            stopwatch.Stop();
            Log($"refresh completed storeCode={storeCode} lookupCode={lookupCode} found=False upserted=0 deleted={deletedCount} deleteLookupCode={deleteLookupCode} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return new RemoteLookupRefreshResult(storeCode, lookupCode, false, null, deletedCount);
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            Log($"refresh canceled storeCode={storeCode} lookupCode={lookupCode} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log($"refresh failed storeCode={storeCode} lookupCode={lookupCode} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}");
            throw;
        }
    }

    private static string GetDeleteLookupCode(CatalogLookupResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.LookupCodeNormalized))
        {
            return response.LookupCodeNormalized;
        }

        return response.LookupCode;
    }

    private static void Log(string message)
    {
        ConsoleLog.Write("RemoteLookupRefresh", message);
    }
}
