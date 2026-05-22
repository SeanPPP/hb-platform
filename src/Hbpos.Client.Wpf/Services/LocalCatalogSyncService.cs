using System.Diagnostics;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

public interface ILocalCatalogSyncService
{
    Task<LocalCatalogSyncResult> FullSyncAsync(
        string storeCode,
        CancellationToken cancellationToken = default,
        IProgress<CatalogSyncProgress>? progress = null);
}

public sealed record LocalCatalogSyncResult(
    string StoreCode,
    int ComparePages,
    int RemotePages,
    int UpsertedCount,
    int DeletedCount);

public enum CatalogSyncProgressStage
{
    Preparing,
    Comparing,
    Downloading,
    Completed,
    Failed
}

public sealed record CatalogSyncProgress(
    string StoreCode,
    CatalogSyncProgressStage Stage,
    int TotalCount,
    int DownloadedCount,
    int Percent,
    int ComparePages,
    int RemotePages,
    int UpsertedCount,
    int DeletedCount,
    long ElapsedMilliseconds,
    string? ErrorMessage = null);

public sealed class LocalCatalogSyncService(
    ILocalCatalogRepository localCatalogRepository,
    ICatalogApiClient catalogApiClient) : ILocalCatalogSyncService
{
    private const int PageSize = 1000;

    public async Task<LocalCatalogSyncResult> FullSyncAsync(
        string storeCode,
        CancellationToken cancellationToken = default,
        IProgress<CatalogSyncProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        var totalStopwatch = Stopwatch.StartNew();
        Log($"full sync start store={storeCode} pageSize={PageSize}");

        var comparePages = 0;
        var remotePages = 0;
        var upsertedCount = 0;
        var deletedCount = 0;
        var totalCount = 0;
        var downloadedCount = 0;
        string? afterLookupCodeNormalized = null;

        try
        {
            ReportProgress(
                progress,
                storeCode,
                CatalogSyncProgressStage.Preparing,
                totalCount,
                downloadedCount,
                comparePages,
                remotePages,
                upsertedCount,
                deletedCount,
                totalStopwatch);

            while (true)
            {
                var localPage = await localCatalogRepository.LoadSellableItemComparePageAsync(
                    storeCode,
                    afterLookupCodeNormalized,
                    PageSize,
                    cancellationToken);

                if (localPage.Count == 0)
                {
                    Log($"local compare finished store={storeCode} pages={comparePages}");
                    break;
                }

                afterLookupCodeNormalized = localPage[^1].LookupCodeNormalized;
                Log($"local compare page store={storeCode} page={comparePages + 1} rows={localPage.Count} after={afterLookupCodeNormalized}");
                var request = new CatalogCompareRequest(
                    storeCode,
                    localPage.Select(row => row.ToCompareVersion()).ToArray());
                var compareStopwatch = Stopwatch.StartNew();
                var response = await catalogApiClient.CompareSellableItemsAsync(request, cancellationToken);
                compareStopwatch.Stop();
                Log($"compare response store={storeCode} page={comparePages + 1} upsertedLookups={response.UpsertedLookups.Count} deletedLookups={response.DeletedLookups.Count} elapsedMs={compareStopwatch.ElapsedMilliseconds}");

                var applyStopwatch = Stopwatch.StartNew();
                var applied = await ApplyChangesAsync(
                    storeCode,
                    response.UpsertedLookups,
                    response.DeletedLookups,
                    cancellationToken);
                applyStopwatch.Stop();

                comparePages++;
                upsertedCount += applied.UpsertedCount;
                deletedCount += applied.DeletedCount;
                Log($"compare applied store={storeCode} page={comparePages} upserted={applied.UpsertedCount} deleted={applied.DeletedCount} elapsedMs={applyStopwatch.ElapsedMilliseconds}");
                ReportProgress(
                    progress,
                    storeCode,
                    CatalogSyncProgressStage.Comparing,
                    totalCount,
                    downloadedCount,
                    comparePages,
                    remotePages,
                    upsertedCount,
                    deletedCount,
                    totalStopwatch);
            }

            string? cursor = null;
            while (true)
            {
                Log($"download page request store={storeCode} page={remotePages + 1} cursor={cursor ?? "<start>"}");
                var downloadStopwatch = Stopwatch.StartNew();
                var response = await catalogApiClient.GetSellableItemsPageAsync(
                    storeCode,
                    cursor,
                    PageSize,
                    cancellationToken);
                downloadStopwatch.Stop();
                totalCount = Math.Max(totalCount, response.TotalCount);
                Log($"download page response store={storeCode} page={remotePages + 1} items={response.Items.Count} total={response.TotalCount} deletedLookups={response.DeletedLookups.Count} hasMore={response.HasMore} next={response.NextCursor ?? "<end>"} elapsedMs={downloadStopwatch.ElapsedMilliseconds}");

                var applyStopwatch = Stopwatch.StartNew();
                var applied = await ApplyChangesAsync(
                    storeCode,
                    response.Items,
                    response.DeletedLookups,
                    cancellationToken);
                applyStopwatch.Stop();

                remotePages++;
                downloadedCount += response.Items.Count;
                upsertedCount += applied.UpsertedCount;
                deletedCount += applied.DeletedCount;
                Log($"download page applied store={storeCode} page={remotePages} upserted={applied.UpsertedCount} deleted={applied.DeletedCount} elapsedMs={applyStopwatch.ElapsedMilliseconds}");
                ReportProgress(
                    progress,
                    storeCode,
                    CatalogSyncProgressStage.Downloading,
                    totalCount,
                    downloadedCount,
                    comparePages,
                    remotePages,
                    upsertedCount,
                    deletedCount,
                    totalStopwatch,
                    forceComplete: !response.HasMore);

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

            totalStopwatch.Stop();
            Log($"full sync completed store={storeCode} comparePages={comparePages} remotePages={remotePages} upserted={upsertedCount} deleted={deletedCount} elapsedMs={totalStopwatch.ElapsedMilliseconds}");
            ReportProgress(
                progress,
                storeCode,
                CatalogSyncProgressStage.Completed,
                totalCount,
                totalCount == 0 ? 0 : Math.Max(downloadedCount, totalCount),
                comparePages,
                remotePages,
                upsertedCount,
                deletedCount,
                totalStopwatch,
                forceComplete: true);
            return new LocalCatalogSyncResult(
                storeCode,
                comparePages,
                remotePages,
                upsertedCount,
                deletedCount);
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            ReportProgress(
                progress,
                storeCode,
                CatalogSyncProgressStage.Failed,
                totalCount,
                downloadedCount,
                comparePages,
                remotePages,
                upsertedCount,
                deletedCount,
                totalStopwatch,
                ex.Message);
            throw;
        }
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

    private static void ReportProgress(
        IProgress<CatalogSyncProgress>? progress,
        string storeCode,
        CatalogSyncProgressStage stage,
        int totalCount,
        int downloadedCount,
        int comparePages,
        int remotePages,
        int upsertedCount,
        int deletedCount,
        Stopwatch stopwatch,
        string? errorMessage = null,
        bool forceComplete = false)
    {
        if (progress is null)
        {
            return;
        }

        var percent = CalculatePercent(totalCount, downloadedCount, forceComplete);
        progress.Report(new CatalogSyncProgress(
            storeCode,
            stage,
            totalCount,
            totalCount == 0 && forceComplete ? 0 : Math.Min(downloadedCount, totalCount),
            percent,
            comparePages,
            remotePages,
            upsertedCount,
            deletedCount,
            stopwatch.ElapsedMilliseconds,
            errorMessage));
    }

    private static int CalculatePercent(int totalCount, int downloadedCount, bool forceComplete)
    {
        if (forceComplete)
        {
            return 100;
        }

        if (totalCount <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(downloadedCount * 100d / totalCount), 0, 99);
    }

    private static void Log(string message)
    {
        ConsoleLog.Write("CatalogSync", message);
    }
}
