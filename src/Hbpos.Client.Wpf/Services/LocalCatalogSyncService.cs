using System.Diagnostics;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.Services;

public interface ILocalCatalogSyncService
{
    Task<LocalCatalogSyncResult> FullSyncAsync(
        string storeCode,
        CancellationToken cancellationToken = default,
        IProgress<CatalogSyncProgress>? progress = null,
        bool forceFullDownload = false);
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
    ICatalogApiClient catalogApiClient,
    IUiPriorityCoordinator? uiPriorityCoordinator = null) : ILocalCatalogSyncService
{
    private const int ComparePageSize = 2000;
    private const int DownloadPageSize = 5000;
    private const int ApplyBatchSize = 500;
    private readonly IUiPriorityCoordinator _uiPriorityCoordinator = uiPriorityCoordinator ?? UiPriorityCoordinator.Noop;

    public async Task<LocalCatalogSyncResult> FullSyncAsync(
        string storeCode,
        CancellationToken cancellationToken = default,
        IProgress<CatalogSyncProgress>? progress = null,
        bool forceFullDownload = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        var totalStopwatch = Stopwatch.StartNew();
        Log($"full sync start store={storeCode} comparePageSize={ComparePageSize} pageSize={DownloadPageSize} forceFullDownload={forceFullDownload}");

        var comparePages = 0;
        var remotePages = 0;
        var upsertedCount = 0;
        var deletedCount = 0;
        var totalCount = 0;
        var downloadedCount = 0;
        var localItemCount = 0;
        var hasCompareChanges = false;
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

            if (forceFullDownload)
            {
                Log($"compare skipped store={storeCode} reason=force-full-download");
            }
            else
            {
                while (true)
                {
                    await _uiPriorityCoordinator.WaitForUiIdleAsync(cancellationToken);
                    var localPage = await localCatalogRepository.LoadSellableItemComparePageAsync(
                        storeCode,
                        afterLookupCodeNormalized,
                        ComparePageSize,
                        cancellationToken);

                    if (localPage.Count == 0)
                    {
                        Log($"local compare finished store={storeCode} pages={comparePages}");
                        break;
                    }

                    localItemCount += localPage.Count;
                    afterLookupCodeNormalized = localPage[^1].LookupCodeNormalized;
                    Log($"local compare page store={storeCode} page={comparePages + 1} rows={localPage.Count} after={afterLookupCodeNormalized}");
                    var request = new CatalogCompareRequest(
                        storeCode,
                        localPage.Select(row => row.ToCompareVersion()).ToArray());
                    var compareStopwatch = Stopwatch.StartNew();
                    await _uiPriorityCoordinator.WaitForUiIdleAsync(cancellationToken);
                    var response = await catalogApiClient.CompareSellableItemsAsync(request, cancellationToken);
                    compareStopwatch.Stop();
                    Log($"compare response store={storeCode} page={comparePages + 1} upsertedLookups={response.UpsertedLookups.Count} deletedLookups={response.DeletedLookups.Count} apiElapsedMs={compareStopwatch.ElapsedMilliseconds}");
                    hasCompareChanges |= response.UpsertedLookups.Count > 0 || response.DeletedLookups.Count > 0;

                    var applied = await ApplyChangesAsync(
                        storeCode,
                        response.UpsertedLookups,
                        response.DeletedLookups,
                        cancellationToken);

                    comparePages++;
                    upsertedCount += applied.UpsertedCount;
                    deletedCount += applied.DeletedCount;
                    Log($"compare applied store={storeCode} page={comparePages} upserted={applied.UpsertedCount} deleted={applied.DeletedCount} upsertElapsedMs={applied.UpsertElapsedMs} deleteElapsedMs={applied.DeleteElapsedMs} applyElapsedMs={applied.ApplyElapsedMs}");
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
            }

            string? cursor = null;
            while (true)
            {
                Log($"download page request store={storeCode} page={remotePages + 1} cursor={cursor ?? "<start>"}");
                var downloadStopwatch = Stopwatch.StartNew();
                await _uiPriorityCoordinator.WaitForUiIdleAsync(cancellationToken);
                var response = await catalogApiClient.GetSellableItemsPageAsync(
                    storeCode,
                    cursor,
                    DownloadPageSize,
                    cancellationToken);
                downloadStopwatch.Stop();
                totalCount = Math.Max(totalCount, response.TotalCount);
                Log($"download page response store={storeCode} page={remotePages + 1} items={response.Items.Count} total={response.TotalCount} deletedLookups={response.DeletedLookups.Count} hasMore={response.HasMore} next={response.NextCursor ?? "<end>"} apiElapsedMs={downloadStopwatch.ElapsedMilliseconds}");

                if (remotePages == 0 && !forceFullDownload && !hasCompareChanges && localItemCount == response.TotalCount)
                {
                    remotePages++;
                    downloadedCount = response.TotalCount;
                    Log($"download skipped store={storeCode} reason=no-changes localCount={localItemCount} total={response.TotalCount} comparePages={comparePages} remotePages={remotePages} elapsedMs={totalStopwatch.ElapsedMilliseconds}");
                    break;
                }

                var applied = await ApplyChangesAsync(
                    storeCode,
                    response.Items,
                    response.DeletedLookups,
                    cancellationToken);

                remotePages++;
                downloadedCount += response.Items.Count;
                upsertedCount += applied.UpsertedCount;
                deletedCount += applied.DeletedCount;
                Log($"download page applied store={storeCode} page={remotePages} upserted={applied.UpsertedCount} deleted={applied.DeletedCount} upsertElapsedMs={applied.UpsertElapsedMs} deleteElapsedMs={applied.DeleteElapsedMs} applyElapsedMs={applied.ApplyElapsedMs}");
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
        catch (OperationCanceledException)
        {
            totalStopwatch.Stop();
            Log($"full sync canceled store={storeCode} comparePages={comparePages} remotePages={remotePages} upserted={upsertedCount} deleted={deletedCount} elapsedMs={totalStopwatch.ElapsedMilliseconds}");
            throw;
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

    private async Task<(int UpsertedCount, int DeletedCount, long UpsertElapsedMs, long DeleteElapsedMs, long ApplyElapsedMs)> ApplyChangesAsync(
        string storeCode,
        IReadOnlyList<CatalogLookupItemDto> upsertedLookups,
        IReadOnlyList<DeletedLookupDto> deletedLookups,
        CancellationToken cancellationToken)
    {
        var applyStopwatch = Stopwatch.StartNew();
        var upsertItems = upsertedLookups
            .Select(item => item.ToSellableItemDto())
            .ToArray();
        var upsertElapsedMs = 0L;
        if (upsertItems.Length > 0)
        {
            var upsertStopwatch = Stopwatch.StartNew();
            foreach (var batch in upsertItems.Chunk(ApplyBatchSize))
            {
                await _uiPriorityCoordinator.WaitForUiIdleAsync(cancellationToken);
                await localCatalogRepository.UpsertSellableItemsAsync(batch, cancellationToken);
            }

            upsertStopwatch.Stop();
            upsertElapsedMs = upsertStopwatch.ElapsedMilliseconds;
        }

        var deletedCodes = deletedLookups
            .Select(GetDeleteLookupCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deleteElapsedMs = 0L;
        var deletedCount = deletedCodes.Length == 0
            ? 0
            : await DeleteByLookupCodesWithTimingAsync(storeCode, deletedCodes, cancellationToken);

        applyStopwatch.Stop();
        return (upsertItems.Length, deletedCount, upsertElapsedMs, deleteElapsedMs, applyStopwatch.ElapsedMilliseconds);

        async Task<int> DeleteByLookupCodesWithTimingAsync(
            string deleteStoreCode,
            IReadOnlyList<string> lookupCodes,
            CancellationToken deleteCancellationToken)
        {
            var deleteStopwatch = Stopwatch.StartNew();
            await _uiPriorityCoordinator.WaitForUiIdleAsync(deleteCancellationToken);
            var count = await localCatalogRepository.DeleteByLookupCodesAsync(
                deleteStoreCode,
                lookupCodes,
                deleteCancellationToken);
            deleteStopwatch.Stop();
            deleteElapsedMs = deleteStopwatch.ElapsedMilliseconds;
            return count;
        }
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
