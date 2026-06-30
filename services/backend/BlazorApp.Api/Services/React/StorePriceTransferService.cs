using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlSugar;
using DataColumn = System.Data.DataColumn;
using DataTable = System.Data.DataTable;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// HQ/本地分店价格与多码双向同步服务。
    /// 只处理价格和分店多码价格，不写库存、活动、动态销售、商品主档和全局一品多码表。
    /// </summary>
    public class StorePriceTransferService : IStorePriceTransferService
    {
        private const string ValidationErrorCode = "VALIDATION_ERROR";
        private const string TransferFailedCode = "STORE_PRICE_TRANSFER_FAILED";
        private const string DefaultSupplierCode = "200";

        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly StoreRetailPriceHqSyncOptions _options;
        private readonly ILogger<StorePriceTransferService> _logger;

        public StorePriceTransferService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            IOptions<StoreRetailPriceHqSyncOptions>? options,
            ILogger<StorePriceTransferService> logger
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _options = options?.Value ?? new StoreRetailPriceHqSyncOptions();
            _logger = logger;
        }

        public async Task<ApiResponse<StorePriceTransferResult>> TransferAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            Action<StorePriceTransferResult>? progressCallback = null
        )
        {
            var normalizedRequest = CloneRequest(request);
            var validationErrors = Validate(normalizedRequest);
            if (validationErrors.Count > 0)
            {
                var failedResult = new StorePriceTransferResult
                {
                    FailedCount = 1,
                    Errors = validationErrors,
                };
                return ApiResponse<StorePriceTransferResult>.Error(
                    validationErrors[0],
                    ValidationErrorCode,
                    failedResult
                );
            }

            var result = new StorePriceTransferResult();
            var operatorName = string.IsNullOrWhiteSpace(updatedBy) ? "system" : updatedBy.Trim();
            var localDb = _localContext.Db;
            var hqDb = _hqContext.Db;
            var originalLocalTimeout = localDb.Ado.CommandTimeOut;
            var originalHqTimeout = hqDb.Ado.CommandTimeOut;

            try
            {
                localDb.Ado.CommandTimeOut = _options.CommandTimeoutSeconds;
                hqDb.Ado.CommandTimeOut = _options.CommandTimeoutSeconds;

                await InitializeTotalsAsync(normalizedRequest, result);
                PublishProgress(result, progressCallback);

                if (
                    string.Equals(
                        normalizedRequest.Direction,
                        StorePriceTransferDirectionConstants.HqToLocal,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    await TransferHqToLocalAsync(normalizedRequest, operatorName, result, progressCallback);
                }
                else
                {
                    await TransferLocalToHqAsync(normalizedRequest, operatorName, result, progressCallback);
                }

                return ApiResponse<StorePriceTransferResult>.OK(result, "分店价格同步完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "HQ/本地分店价格同步失败: Direction={Direction}, Source={SourceStoreCode}, Target={TargetStoreCode}",
                    normalizedRequest.Direction,
                    normalizedRequest.SourceStoreCode,
                    normalizedRequest.TargetStoreCode
                );

                result.FailedCount++;
                var failureMessage = result.TotalProcessed > 0
                    ? $"分店价格同步失败: {ex.Message}。已提交 {result.TotalProcessed} 条，已提交批次不会自动回滚"
                    : $"分店价格同步失败: {ex.Message}";
                result.Errors.Add(failureMessage);
                return ApiResponse<StorePriceTransferResult>.Error(
                    failureMessage,
                    TransferFailedCode,
                    result
                );
            }
            finally
            {
                localDb.Ado.CommandTimeOut = originalLocalTimeout;
                hqDb.Ado.CommandTimeOut = originalHqTimeout;
            }
        }

        private async Task TransferHqToLocalAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result,
            Action<StorePriceTransferResult>? progressCallback
        )
        {
            if (request.SyncRetailPrices)
            {
                await TransferHqRetailToLocalAsync(request, updatedBy, result, progressCallback);
            }

            if (request.SyncMultiCodePrices)
            {
                await TransferHqMultiToLocalAsync(request, updatedBy, result, progressCallback);
            }
        }

        private async Task InitializeTotalsAsync(
            StorePriceTransferRequest request,
            StorePriceTransferResult result
        )
        {
            if (request.SyncRetailPrices)
            {
                result.RetailPriceTotal = string.Equals(
                    request.Direction,
                    StorePriceTransferDirectionConstants.HqToLocal,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? await CountHqRetailSourceAsync(request.SourceStoreCode!)
                    : await CountLocalRetailSourceAsync(request.SourceStoreCode!);
            }

            if (request.SyncMultiCodePrices)
            {
                result.MultiCodeTotal = string.Equals(
                    request.Direction,
                    StorePriceTransferDirectionConstants.HqToLocal,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? await CountHqMultiSourceAsync(request.SourceStoreCode!)
                    : await CountLocalMultiSourceAsync(request.SourceStoreCode!);
            }

            result.TotalCount = result.RetailPriceTotal + result.MultiCodeTotal;
        }

        private Task<int> CountHqRetailSourceAsync(string sourceStoreCode)
        {
            return _hqContext.Db.Queryable<DIC_商品零售价表>()
                .Where(row =>
                    row.H分店代码 == sourceStoreCode
                    && row.H使用状态 == true
                    && row.H商品编码 != null
                    && row.H商品编码 != string.Empty
                )
                .CountAsync();
        }

        private Task<int> CountHqMultiSourceAsync(string sourceStoreCode)
        {
            return _hqContext.Db.Queryable<DIC_分店一品多码表>()
                .Where(row =>
                    row.H分店代码 == sourceStoreCode
                    && row.H使用状态 == true
                    && row.H商品编码 != null
                    && row.H商品编码 != string.Empty
                    && row.H多码商品编码 != null
                    && row.H多码商品编码 != string.Empty
                )
                .CountAsync();
        }

        private Task<int> CountLocalRetailSourceAsync(string sourceStoreCode)
        {
            return _localContext.Db.Queryable<StoreRetailPrice>()
                .Where(row =>
                    row.StoreCode == sourceStoreCode
                    && row.IsDeleted == false
                    && row.ProductCode != null
                    && row.ProductCode != string.Empty
                )
                .CountAsync();
        }

        private Task<int> CountLocalMultiSourceAsync(string sourceStoreCode)
        {
            return _localContext.Db.Queryable<StoreMultiCodeProduct>()
                .Where(row =>
                    row.StoreCode == sourceStoreCode
                    && row.IsDeleted == false
                    && row.ProductCode != null
                    && row.ProductCode != string.Empty
                    && row.MultiCodeProductCode != null
                    && row.MultiCodeProductCode != string.Empty
                )
                .CountAsync();
        }

        private async Task TransferLocalToHqAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result,
            Action<StorePriceTransferResult>? progressCallback
        )
        {
            if (request.SyncRetailPrices)
            {
                await TransferLocalRetailToHqAsync(request, updatedBy, result, progressCallback);
            }

            if (request.SyncMultiCodePrices)
            {
                await TransferLocalMultiToHqAsync(request, updatedBy, result, progressCallback);
            }
        }

        private async Task TransferHqRetailToLocalAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result,
            Action<StorePriceTransferResult>? progressCallback
        )
        {
            var sourceStoreCode = request.SourceStoreCode!;
            var targetStoreCode = request.TargetStoreCode!;
            var lastId = 0;

            while (true)
            {
                var hqBatch = await _hqContext.Db.Queryable<DIC_商品零售价表>()
                    .Where(row =>
                        row.ID > lastId
                        && row.H分店代码 == sourceStoreCode
                        && row.H使用状态 == true
                        && row.H商品编码 != null
                        && row.H商品编码 != string.Empty
                    )
                    .OrderBy(row => row.ID)
                    .Take(HqReadBatchSize)
                    .ToListAsync();

                if (hqBatch.Count == 0)
                {
                    break;
                }

                lastId = hqBatch[^1].ID;
                if (IsSqlServer(_localContext.Db))
                {
                    var counts = await MergeHqRetailToLocalSqlServerAsync(hqBatch, request, updatedBy);
                    AddRetailCounts(result, counts.Inserted, counts.Updated, counts.Skipped);
                    PublishProgress(result, progressCallback);
                    continue;
                }

                var existingRows = await LoadLocalRetailRowsAsync(targetStoreCode, hqBatch.Select(row => row.H商品编码));
                var toInsert = new List<StoreRetailPrice>();
                var toUpdate = new List<StoreRetailPrice>();
                var now = DateTime.UtcNow;

                foreach (var hqRow in hqBatch)
                {
                    var productCode = NormalizeCode(hqRow.H商品编码);
                    if (productCode == null)
                    {
                        result.RetailPriceSkipped++;
                        result.SkippedCount++;
                        continue;
                    }

                    if (existingRows.TryGetValue(productCode, out var existing))
                    {
                        if (HasHqRetailChanges(existing, hqRow, request))
                        {
                            ApplyHqRetailFields(existing, hqRow, request, updatedBy, now);
                            toUpdate.Add(existing);
                        }
                        else
                        {
                            result.RetailPriceSkipped++;
                            result.SkippedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(BuildLocalRetailPrice(hqRow, targetStoreCode, request, updatedBy, now));
                    }
                }

                await WriteLocalRetailBatchAsync(toInsert, toUpdate, request);
                AddRetailCounts(result, toInsert.Count, toUpdate.Count);
                PublishProgress(result, progressCallback);
            }
        }

        private async Task TransferHqMultiToLocalAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result,
            Action<StorePriceTransferResult>? progressCallback
        )
        {
            var sourceStoreCode = request.SourceStoreCode!;
            var targetStoreCode = request.TargetStoreCode!;
            var lastId = 0;

            while (true)
            {
                var hqBatch = await _hqContext.Db.Queryable<DIC_分店一品多码表>()
                    .Where(row =>
                        row.ID > lastId
                        && row.H分店代码 == sourceStoreCode
                        && row.H使用状态 == true
                        && row.H商品编码 != null
                        && row.H商品编码 != string.Empty
                        && row.H多码商品编码 != null
                        && row.H多码商品编码 != string.Empty
                    )
                    .OrderBy(row => row.ID)
                    .Take(HqReadBatchSize)
                    .ToListAsync();

                if (hqBatch.Count == 0)
                {
                    break;
                }

                lastId = hqBatch[^1].ID;
                if (IsSqlServer(_localContext.Db))
                {
                    var counts = await MergeHqMultiToLocalSqlServerAsync(hqBatch, request, updatedBy);
                    AddMultiCounts(result, counts.Inserted, counts.Updated, counts.Skipped);
                    PublishProgress(result, progressCallback);
                    continue;
                }

                var existingRows = await LoadLocalMultiRowsAsync(targetStoreCode, hqBatch.Select(row => row.H商品编码));
                var toInsert = new List<StoreMultiCodeProduct>();
                var toUpdate = new List<StoreMultiCodeProduct>();
                var now = DateTime.UtcNow;

                foreach (var hqRow in hqBatch)
                {
                    var productCode = NormalizeCode(hqRow.H商品编码);
                    var multiCode = NormalizeCode(hqRow.H多码商品编码);
                    if (productCode == null || multiCode == null)
                    {
                        result.MultiCodeSkipped++;
                        result.SkippedCount++;
                        continue;
                    }

                    var key = BuildMultiKey(productCode, multiCode);
                    if (existingRows.TryGetValue(key, out var existing))
                    {
                        if (HasHqMultiChanges(existing, hqRow, request))
                        {
                            ApplyHqMultiFields(existing, hqRow, request, updatedBy, now);
                            toUpdate.Add(existing);
                        }
                        else
                        {
                            result.MultiCodeSkipped++;
                            result.SkippedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(BuildLocalMultiCodeProduct(hqRow, targetStoreCode, request, updatedBy, now));
                    }
                }

                await WriteLocalMultiBatchAsync(toInsert, toUpdate, request);
                AddMultiCounts(result, toInsert.Count, toUpdate.Count);
                PublishProgress(result, progressCallback);
            }
        }

        private async Task TransferLocalRetailToHqAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result,
            Action<StorePriceTransferResult>? progressCallback
        )
        {
            string? lastProductCode = null;
            string? lastUuid = null;

            while (true)
            {
                var localBatch = await ReadLocalRetailBatchAsync(
                    request.SourceStoreCode!,
                    lastProductCode,
                    lastUuid
                );

                if (localBatch.Count == 0)
                {
                    break;
                }

                var lastRow = localBatch[^1];
                lastProductCode = NormalizeCode(lastRow.ProductCode);
                lastUuid = lastRow.UUID;

                if (IsSqlServer(_hqContext.Db))
                {
                    var counts = await MergeLocalRetailToHqSqlServerAsync(localBatch, request, updatedBy);
                    AddRetailCounts(result, counts.Inserted, counts.Updated, counts.Skipped);
                    PublishProgress(result, progressCallback);
                    continue;
                }

                var existingRows = await LoadHqRetailRowsAsync(request.TargetStoreCode!, localBatch.Select(row => row.ProductCode));
                var toInsert = new List<DIC_商品零售价表>();
                var toUpdate = new List<DIC_商品零售价表>();
                var now = DateTime.Now;

                foreach (var localRow in localBatch)
                {
                    var productCode = NormalizeCode(localRow.ProductCode);
                    if (productCode == null)
                    {
                        result.RetailPriceSkipped++;
                        result.SkippedCount++;
                        continue;
                    }

                    if (existingRows.TryGetValue(productCode, out var existing))
                    {
                        if (HasLocalRetailChanges(existing, localRow, request))
                        {
                            ApplyLocalRetailFields(existing, localRow, request, updatedBy, now);
                            toUpdate.Add(existing);
                        }
                        else
                        {
                            result.RetailPriceSkipped++;
                            result.SkippedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(BuildHqRetailPrice(localRow, request.TargetStoreCode!, request, updatedBy, now));
                    }
                }

                await WriteHqRetailBatchAsync(toInsert, toUpdate, request);
                AddRetailCounts(result, toInsert.Count, toUpdate.Count);
                PublishProgress(result, progressCallback);
            }
        }

        private async Task TransferLocalMultiToHqAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result,
            Action<StorePriceTransferResult>? progressCallback
        )
        {
            string? lastProductCode = null;
            string? lastMultiCode = null;
            string? lastUuid = null;

            while (true)
            {
                var localBatch = await ReadLocalMultiBatchAsync(
                    request.SourceStoreCode!,
                    lastProductCode,
                    lastMultiCode,
                    lastUuid
                );

                if (localBatch.Count == 0)
                {
                    break;
                }

                var lastRow = localBatch[^1];
                lastProductCode = NormalizeCode(lastRow.ProductCode);
                lastMultiCode = NormalizeCode(lastRow.MultiCodeProductCode);
                lastUuid = lastRow.UUID;

                if (IsSqlServer(_hqContext.Db))
                {
                    var counts = await MergeLocalMultiToHqSqlServerAsync(localBatch, request, updatedBy);
                    AddMultiCounts(result, counts.Inserted, counts.Updated, counts.Skipped);
                    PublishProgress(result, progressCallback);
                    continue;
                }

                var existingRows = await LoadHqMultiRowsAsync(request.TargetStoreCode!, localBatch.Select(row => row.ProductCode));
                var toInsert = new List<DIC_分店一品多码表>();
                var toUpdate = new List<DIC_分店一品多码表>();
                var now = DateTime.Now;

                foreach (var localRow in localBatch)
                {
                    var productCode = NormalizeCode(localRow.ProductCode);
                    var multiCode = NormalizeCode(localRow.MultiCodeProductCode);
                    if (productCode == null || multiCode == null)
                    {
                        result.MultiCodeSkipped++;
                        result.SkippedCount++;
                        continue;
                    }

                    var key = BuildMultiKey(productCode, multiCode);
                    if (existingRows.TryGetValue(key, out var existing))
                    {
                        if (HasLocalMultiChanges(existing, localRow, request))
                        {
                            ApplyLocalMultiFields(existing, localRow, request, updatedBy, now);
                            toUpdate.Add(existing);
                        }
                        else
                        {
                            result.MultiCodeSkipped++;
                            result.SkippedCount++;
                        }
                    }
                    else
                    {
                        toInsert.Add(BuildHqMultiCodeProduct(localRow, request.TargetStoreCode!, request, updatedBy, now));
                    }
                }

                await WriteHqMultiBatchAsync(toInsert, toUpdate, request);
                AddMultiCounts(result, toInsert.Count, toUpdate.Count);
                PublishProgress(result, progressCallback);
            }
        }

        private async Task<Dictionary<string, StoreRetailPrice>> LoadLocalRetailRowsAsync(
            string targetStoreCode,
            IEnumerable<string?> productCodes
        )
        {
            var result = new Dictionary<string, StoreRetailPrice>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in NormalizeCodes(productCodes).Chunk(1000))
            {
                var codes = chunk.ToList();
                var rows = await _localContext.Db.Queryable<StoreRetailPrice>()
                    .Where(row =>
                        row.StoreCode == targetStoreCode
                        && row.IsDeleted == false
                        && row.ProductCode != null
                        && codes.Contains(row.ProductCode)
                    )
                    .ToListAsync();

                foreach (var row in rows)
                {
                    var productCode = NormalizeCode(row.ProductCode);
                    if (productCode != null && !result.ContainsKey(productCode))
                    {
                        result[productCode] = row;
                    }
                }
            }

            return result;
        }

        private async Task<Dictionary<string, StoreMultiCodeProduct>> LoadLocalMultiRowsAsync(
            string targetStoreCode,
            IEnumerable<string?> productCodes
        )
        {
            var result = new Dictionary<string, StoreMultiCodeProduct>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in NormalizeCodes(productCodes).Chunk(1000))
            {
                var codes = chunk.ToList();
                var rows = await _localContext.Db.Queryable<StoreMultiCodeProduct>()
                    .Where(row =>
                        row.StoreCode == targetStoreCode
                        && row.IsDeleted == false
                        && row.ProductCode != null
                        && codes.Contains(row.ProductCode)
                    )
                    .ToListAsync();

                foreach (var row in rows)
                {
                    var productCode = NormalizeCode(row.ProductCode);
                    var multiCode = NormalizeCode(row.MultiCodeProductCode);
                    if (productCode != null && multiCode != null)
                    {
                        result.TryAdd(BuildMultiKey(productCode, multiCode), row);
                    }
                }
            }

            return result;
        }

        private async Task<Dictionary<string, DIC_商品零售价表>> LoadHqRetailRowsAsync(
            string targetStoreCode,
            IEnumerable<string?> productCodes
        )
        {
            var result = new Dictionary<string, DIC_商品零售价表>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in NormalizeCodes(productCodes).Chunk(1000))
            {
                var codes = chunk.ToList();
                var rows = await _hqContext.Db.Queryable<DIC_商品零售价表>()
                    .Where(row =>
                        row.H分店代码 == targetStoreCode
                        && row.H商品编码 != null
                        && codes.Contains(row.H商品编码)
                    )
                    .ToListAsync();

                foreach (var row in rows)
                {
                    var productCode = NormalizeCode(row.H商品编码);
                    if (productCode != null && !result.ContainsKey(productCode))
                    {
                        result[productCode] = row;
                    }
                }
            }

            return result;
        }

        private async Task<Dictionary<string, DIC_分店一品多码表>> LoadHqMultiRowsAsync(
            string targetStoreCode,
            IEnumerable<string?> productCodes
        )
        {
            var result = new Dictionary<string, DIC_分店一品多码表>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in NormalizeCodes(productCodes).Chunk(1000))
            {
                var codes = chunk.ToList();
                var rows = await _hqContext.Db.Queryable<DIC_分店一品多码表>()
                    .Where(row =>
                        row.H分店代码 == targetStoreCode
                        && row.H商品编码 != null
                        && codes.Contains(row.H商品编码)
                    )
                    .ToListAsync();

                foreach (var row in rows)
                {
                    var productCode = NormalizeCode(row.H商品编码);
                    var multiCode = NormalizeCode(row.H多码商品编码);
                    if (productCode != null && multiCode != null)
                    {
                        result.TryAdd(BuildMultiKey(productCode, multiCode), row);
                    }
                }
            }

            return result;
        }

        private async Task<List<StoreRetailPrice>> ReadLocalRetailBatchAsync(
            string sourceStoreCode,
            string? lastProductCode,
            string? lastUuid
        )
        {
            var db = _localContext.Db;
            var parameters = new List<SugarParameter>
            {
                new("@SourceStoreCode", sourceStoreCode),
            };
            var cursorSql = string.Empty;
            if (!string.IsNullOrWhiteSpace(lastProductCode) && !string.IsNullOrWhiteSpace(lastUuid))
            {
                cursorSql = " AND (([ProductCode] > @LastProductCode) OR ([ProductCode] = @LastProductCode AND [UUID] > @LastUuid))";
                parameters.Add(new SugarParameter("@LastProductCode", lastProductCode));
                parameters.Add(new SugarParameter("@LastUuid", lastUuid));
            }

            var sql = db.CurrentConnectionConfig.DbType == DbType.Sqlite
                ? $"""
                   SELECT *
                   FROM [StoreRetailPrice]
                   WHERE [StoreCode] = @SourceStoreCode
                     AND COALESCE([IsDeleted], 0) = 0
                     AND [ProductCode] IS NOT NULL
                     AND [ProductCode] <> ''
                     {cursorSql}
                   ORDER BY [ProductCode], [UUID]
                   LIMIT {LocalReadBatchSize}
                   """
                : $"""
                   SELECT TOP {LocalReadBatchSize} *
                   FROM [StoreRetailPrice]
                   WHERE [StoreCode] = @SourceStoreCode
                     AND ISNULL([IsDeleted], 0) = 0
                     AND [ProductCode] IS NOT NULL
                     AND [ProductCode] <> ''
                     {cursorSql}
                   ORDER BY [ProductCode], [UUID]
                   """;

            return await db.Ado.SqlQueryAsync<StoreRetailPrice>(sql, parameters.ToArray());
        }

        private async Task<List<StoreMultiCodeProduct>> ReadLocalMultiBatchAsync(
            string sourceStoreCode,
            string? lastProductCode,
            string? lastMultiCode,
            string? lastUuid
        )
        {
            var db = _localContext.Db;
            var parameters = new List<SugarParameter>
            {
                new("@SourceStoreCode", sourceStoreCode),
            };
            var cursorSql = string.Empty;
            if (
                !string.IsNullOrWhiteSpace(lastProductCode)
                && !string.IsNullOrWhiteSpace(lastMultiCode)
                && !string.IsNullOrWhiteSpace(lastUuid)
            )
            {
                cursorSql = """
                     AND (
                       [ProductCode] > @LastProductCode
                       OR (
                         [ProductCode] = @LastProductCode
                         AND (
                           [MultiCodeProductCode] > @LastMultiCode
                           OR ([MultiCodeProductCode] = @LastMultiCode AND [UUID] > @LastUuid)
                         )
                       )
                     )
                    """;
                parameters.Add(new SugarParameter("@LastProductCode", lastProductCode));
                parameters.Add(new SugarParameter("@LastMultiCode", lastMultiCode));
                parameters.Add(new SugarParameter("@LastUuid", lastUuid));
            }

            var sql = db.CurrentConnectionConfig.DbType == DbType.Sqlite
                ? $"""
                   SELECT *
                   FROM [StoreMultiCodeProduct]
                   WHERE [StoreCode] = @SourceStoreCode
                     AND COALESCE([IsDeleted], 0) = 0
                     AND [ProductCode] IS NOT NULL
                     AND [ProductCode] <> ''
                     AND [MultiCodeProductCode] IS NOT NULL
                     AND [MultiCodeProductCode] <> ''
                     {cursorSql}
                   ORDER BY [ProductCode], [MultiCodeProductCode], [UUID]
                   LIMIT {LocalReadBatchSize}
                   """
                : $"""
                   SELECT TOP {LocalReadBatchSize} *
                   FROM [StoreMultiCodeProduct]
                   WHERE [StoreCode] = @SourceStoreCode
                     AND ISNULL([IsDeleted], 0) = 0
                     AND [ProductCode] IS NOT NULL
                     AND [ProductCode] <> ''
                     AND [MultiCodeProductCode] IS NOT NULL
                     AND [MultiCodeProductCode] <> ''
                     {cursorSql}
                   ORDER BY [ProductCode], [MultiCodeProductCode], [UUID]
                   """;

            return await db.Ado.SqlQueryAsync<StoreMultiCodeProduct>(sql, parameters.ToArray());
        }

        private async Task<StorePriceTransferWriteCounts> MergeHqRetailToLocalSqlServerAsync(
            List<DIC_商品零售价表> hqBatch,
            StorePriceTransferRequest request,
            string updatedBy
        )
        {
            var table = CreateLocalRetailStageTable();
            var skipped = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;

            foreach (var hqRow in hqBatch)
            {
                var productCode = NormalizeCode(hqRow.H商品编码);
                if (productCode == null || !seen.Add(productCode))
                {
                    skipped++;
                    continue;
                }

                AddLocalRetailStageRow(
                    table,
                    BuildLocalRetailPrice(hqRow, request.TargetStoreCode!, request, updatedBy, now)
                );
            }

            var counts = await BulkMergeAsync(
                _localContext.Db,
                table,
                BuildLocalRetailStageSql(),
                BuildLocalRetailMergeSql(request),
                new Dictionary<string, object?> { ["@TargetStoreCode"] = request.TargetStoreCode }
            );
            counts.Skipped += skipped;
            return counts;
        }

        private async Task<StorePriceTransferWriteCounts> MergeHqMultiToLocalSqlServerAsync(
            List<DIC_分店一品多码表> hqBatch,
            StorePriceTransferRequest request,
            string updatedBy
        )
        {
            var table = CreateLocalMultiStageTable();
            var skipped = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;

            foreach (var hqRow in hqBatch)
            {
                var productCode = NormalizeCode(hqRow.H商品编码);
                var multiCode = NormalizeCode(hqRow.H多码商品编码);
                if (productCode == null || multiCode == null || !seen.Add(BuildMultiKey(productCode, multiCode)))
                {
                    skipped++;
                    continue;
                }

                AddLocalMultiStageRow(
                    table,
                    BuildLocalMultiCodeProduct(hqRow, request.TargetStoreCode!, request, updatedBy, now)
                );
            }

            var counts = await BulkMergeAsync(
                _localContext.Db,
                table,
                BuildLocalMultiStageSql(),
                BuildLocalMultiMergeSql(request),
                new Dictionary<string, object?> { ["@TargetStoreCode"] = request.TargetStoreCode }
            );
            counts.Skipped += skipped;
            return counts;
        }

        private async Task<StorePriceTransferWriteCounts> MergeLocalRetailToHqSqlServerAsync(
            List<StoreRetailPrice> localBatch,
            StorePriceTransferRequest request,
            string updatedBy
        )
        {
            var table = CreateHqRetailStageTable();
            var skipped = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;

            foreach (var localRow in localBatch)
            {
                var productCode = NormalizeCode(localRow.ProductCode);
                if (productCode == null || !seen.Add(productCode))
                {
                    skipped++;
                    continue;
                }

                AddHqRetailStageRow(
                    table,
                    BuildHqRetailPrice(localRow, request.TargetStoreCode!, request, updatedBy, now)
                );
            }

            var counts = await BulkMergeAsync(
                _hqContext.Db,
                table,
                BuildHqRetailStageSql(),
                BuildHqRetailMergeSql(request),
                new Dictionary<string, object?> { ["@TargetStoreCode"] = request.TargetStoreCode }
            );
            counts.Skipped += skipped;
            return counts;
        }

        private async Task<StorePriceTransferWriteCounts> MergeLocalMultiToHqSqlServerAsync(
            List<StoreMultiCodeProduct> localBatch,
            StorePriceTransferRequest request,
            string updatedBy
        )
        {
            var table = CreateHqMultiStageTable();
            var skipped = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;

            foreach (var localRow in localBatch)
            {
                var productCode = NormalizeCode(localRow.ProductCode);
                var multiCode = NormalizeCode(localRow.MultiCodeProductCode);
                if (productCode == null || multiCode == null || !seen.Add(BuildMultiKey(productCode, multiCode)))
                {
                    skipped++;
                    continue;
                }

                AddHqMultiStageRow(
                    table,
                    BuildHqMultiCodeProduct(localRow, request.TargetStoreCode!, request, updatedBy, now)
                );
            }

            var counts = await BulkMergeAsync(
                _hqContext.Db,
                table,
                BuildHqMultiStageSql(),
                BuildHqMultiMergeSql(request),
                new Dictionary<string, object?> { ["@TargetStoreCode"] = request.TargetStoreCode }
            );
            counts.Skipped += skipped;
            return counts;
        }

        private async Task<StorePriceTransferWriteCounts> BulkMergeAsync(
            ISqlSugarClient db,
            DataTable table,
            string createStageSql,
            string mergeSql,
            IReadOnlyDictionary<string, object?> parameters
        )
        {
            if (table.Rows.Count == 0)
            {
                return new StorePriceTransferWriteCounts();
            }

            await using var connection = new SqlConnection(db.CurrentConnectionConfig.ConnectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                using (var createCommand = new SqlCommand(createStageSql, connection, transaction))
                {
                    createCommand.CommandTimeout = _options.CommandTimeoutSeconds;
                    await createCommand.ExecuteNonQueryAsync();
                }

                using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction))
                {
                    bulkCopy.DestinationTableName = table.TableName;
                    bulkCopy.BatchSize = WriteBatchSize;
                    bulkCopy.BulkCopyTimeout = _options.CommandTimeoutSeconds;
                    foreach (DataColumn column in table.Columns)
                    {
                        bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                    }

                    await bulkCopy.WriteToServerAsync(table);
                }

                using var mergeCommand = new SqlCommand(mergeSql, connection, transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds,
                };
                foreach (var parameter in parameters)
                {
                    mergeCommand.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
                }

                var inserted = 0;
                var updated = 0;
                using (var reader = await mergeCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        inserted = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                        updated = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                    }
                }

                // 关键位置：先释放 reader/command 再提交事务，避免 SQL Server 仍在消费结果时完成事务。
                transaction.Commit();
                return new StorePriceTransferWriteCounts
                {
                    Inserted = inserted,
                    Updated = updated,
                    Skipped = Math.Max(0, table.Rows.Count - inserted - updated),
                };
            }
            catch (Exception ex)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    // 关键位置：rollback 失败不能盖住真正的 bulk merge 异常。
                    _logger.LogWarning(
                        rollbackEx,
                        "回滚分店价格同步 bulk merge 事务失败，保留原始异常继续抛出: {OriginalMessage}",
                        ex.Message
                    );
                }

                throw;
            }
        }

        private static DataTable CreateLocalRetailStageTable()
        {
            var table = new DataTable("#StorePriceTransferRetail");
            AddStageColumn<string>(table, "UUID");
            AddStageColumn<string>(table, "StoreCode");
            AddStageColumn<string>(table, "ProductCode");
            AddStageColumn<string>(table, "StoreProductCode");
            AddStageColumn<string>(table, "SupplierCode");
            AddStageColumn<decimal>(table, "PurchasePrice");
            AddStageColumn<decimal>(table, "StoreRetailPriceValue");
            AddStageColumn<decimal>(table, "DiscountRate");
            AddStageColumn<bool>(table, "IsActive");
            AddStageColumn<bool>(table, "IsAutoPricing");
            AddStageColumn<bool>(table, "IsSpecialProduct");
            AddStageColumn<bool>(table, "IsDeleted");
            AddStageColumn<DateTime>(table, "CreatedAt");
            AddStageColumn<string>(table, "CreatedBy");
            AddStageColumn<DateTime>(table, "UpdatedAt");
            AddStageColumn<string>(table, "UpdatedBy");
            return table;
        }

        private static DataTable CreateLocalMultiStageTable()
        {
            var table = new DataTable("#StorePriceTransferMulti");
            AddStageColumn<string>(table, "UUID");
            AddStageColumn<string>(table, "StoreCode");
            AddStageColumn<string>(table, "ProductCode");
            AddStageColumn<string>(table, "MultiCodeProductCode");
            AddStageColumn<string>(table, "StoreMultiCodeProductCode");
            AddStageColumn<string>(table, "MultiBarcode");
            AddStageColumn<decimal>(table, "PurchasePrice");
            AddStageColumn<decimal>(table, "MultiCodeRetailPrice");
            AddStageColumn<decimal>(table, "DiscountRate");
            AddStageColumn<bool>(table, "IsActive");
            AddStageColumn<bool>(table, "IsAutoPricing");
            AddStageColumn<bool>(table, "IsSpecialProduct");
            AddStageColumn<bool>(table, "IsDeleted");
            AddStageColumn<DateTime>(table, "CreatedAt");
            AddStageColumn<string>(table, "CreatedBy");
            AddStageColumn<DateTime>(table, "UpdatedAt");
            AddStageColumn<string>(table, "UpdatedBy");
            return table;
        }

        private static DataTable CreateHqRetailStageTable()
        {
            var table = new DataTable("#StorePriceTransferRetail");
            foreach (var name in HqRetailStageColumns)
            {
                AddStageColumn(table, name, GetHqRetailColumnType(name));
            }

            return table;
        }

        private static DataTable CreateHqMultiStageTable()
        {
            var table = new DataTable("#StorePriceTransferMulti");
            foreach (var name in HqMultiStageColumns)
            {
                AddStageColumn(table, name, GetHqMultiColumnType(name));
            }

            return table;
        }

        private static void AddLocalRetailStageRow(DataTable table, StoreRetailPrice row)
        {
            table.Rows.Add(
                row.UUID,
                row.StoreCode,
                row.ProductCode,
                row.StoreProductCode,
                DbValue(row.SupplierCode),
                DbValue(row.PurchasePrice),
                DbValue(row.StoreRetailPriceValue),
                DbValue(row.DiscountRate),
                row.IsActive,
                row.IsAutoPricing,
                row.IsSpecialProduct,
                row.IsDeleted,
                row.CreatedAt,
                DbValue(row.CreatedBy),
                DbValue(row.UpdatedAt),
                DbValue(row.UpdatedBy)
            );
        }

        private static void AddLocalMultiStageRow(DataTable table, StoreMultiCodeProduct row)
        {
            table.Rows.Add(
                row.UUID,
                row.StoreCode,
                row.ProductCode,
                row.MultiCodeProductCode,
                row.StoreMultiCodeProductCode,
                DbValue(row.MultiBarcode),
                DbValue(row.PurchasePrice),
                DbValue(row.MultiCodeRetailPrice),
                DbValue(row.DiscountRate),
                row.IsActive,
                row.IsAutoPricing,
                row.IsSpecialProduct,
                row.IsDeleted,
                row.CreatedAt,
                DbValue(row.CreatedBy),
                DbValue(row.UpdatedAt),
                DbValue(row.UpdatedBy)
            );
        }

        private static void AddHqRetailStageRow(DataTable table, DIC_商品零售价表 row)
        {
            table.Rows.Add(HqRetailStageColumns.Select(name => DbValue(GetHqRetailColumnValue(row, name))).ToArray());
        }

        private static void AddHqMultiStageRow(DataTable table, DIC_分店一品多码表 row)
        {
            table.Rows.Add(HqMultiStageColumns.Select(name => DbValue(GetHqMultiColumnValue(row, name))).ToArray());
        }

        private static string BuildLocalRetailStageSql()
        {
            return """
                   -- 关键位置：临时表字符串列跟随目标库默认排序规则，避免 tempdb 与业务库排序规则冲突。
                   CREATE TABLE #StorePriceTransferRetail (
                       [UUID] nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
                       [StoreCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [ProductCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [StoreProductCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [SupplierCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [PurchasePrice] decimal(18, 4) NULL,
                       [StoreRetailPriceValue] decimal(18, 4) NULL,
                       [DiscountRate] decimal(18, 6) NULL,
                       [IsActive] bit NOT NULL,
                       [IsAutoPricing] bit NOT NULL,
                       [IsSpecialProduct] bit NOT NULL,
                       [IsDeleted] bit NOT NULL,
                       [CreatedAt] datetime2 NOT NULL,
                       [CreatedBy] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [UpdatedAt] datetime2 NULL,
                       [UpdatedBy] nvarchar(100) COLLATE DATABASE_DEFAULT NULL
                   );
                   """;
        }

        private static string BuildLocalMultiStageSql()
        {
            return """
                   -- 关键位置：临时表字符串列跟随目标库默认排序规则，避免 tempdb 与业务库排序规则冲突。
                   CREATE TABLE #StorePriceTransferMulti (
                       [UUID] nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
                       [StoreCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [ProductCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [MultiCodeProductCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [StoreMultiCodeProductCode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [MultiBarcode] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [PurchasePrice] decimal(18, 4) NULL,
                       [MultiCodeRetailPrice] decimal(18, 4) NULL,
                       [DiscountRate] decimal(18, 6) NULL,
                       [IsActive] bit NOT NULL,
                       [IsAutoPricing] bit NOT NULL,
                       [IsSpecialProduct] bit NOT NULL,
                       [IsDeleted] bit NOT NULL,
                       [CreatedAt] datetime2 NOT NULL,
                       [CreatedBy] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [UpdatedAt] datetime2 NULL,
                       [UpdatedBy] nvarchar(100) COLLATE DATABASE_DEFAULT NULL
                   );
                   """;
        }

        private static string BuildHqRetailStageSql()
        {
            return """
                   -- 关键位置：临时表字符串列跟随目标库默认排序规则，避免 tempdb 与业务库排序规则冲突。
                   CREATE TABLE #StorePriceTransferRetail (
                       [HGUID] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H分店代码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H商品编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H分店商品编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H供应商编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H分店供应商编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H进货价] decimal(18, 4) NOT NULL,
                       [H分店零售价] decimal(18, 4) NOT NULL,
                       [H库存] decimal(18, 4) NOT NULL,
                       [H库存金额] decimal(18, 4) NOT NULL,
                       [H库存预警数] decimal(18, 4) NOT NULL,
                       [H商品缺货日期] datetime2 NOT NULL,
                       [H是否缺货状态] bit NOT NULL,
                       [H最小订货量] decimal(18, 4) NOT NULL,
                       [H最小订货量合计金额] decimal(18, 4) NOT NULL,
                       [H活动类型] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [H满减活动代码] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [H活动开始日期] datetime2 NOT NULL,
                       [H活动结束日期] datetime2 NOT NULL,
                       [H折扣率] decimal(18, 6) NOT NULL,
                       [H满减数量] decimal(18, 4) NOT NULL,
                       [H满减金额] decimal(18, 4) NOT NULL,
                       [H多码数量] decimal(18, 4) NOT NULL,
                       [H使用状态] bit NOT NULL,
                       [H是否自动定价] bit NOT NULL,
                       [H自动新价格] decimal(18, 4) NOT NULL,
                       [H盘点入库记录数] decimal(18, 4) NOT NULL,
                       [H是否特殊商品] bit NOT NULL,
                       [H动态销售数量] decimal(18, 4) NOT NULL,
                       [H动态销售额] decimal(18, 4) NOT NULL,
                       [H动态成本] decimal(18, 4) NOT NULL,
                       [H动态毛利] decimal(18, 4) NOT NULL,
                       [H动态毛利率] decimal(18, 6) NOT NULL,
                       [H动态销售占比] decimal(18, 6) NOT NULL,
                       [FGC_Creator] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [FGC_CreateDate] datetime2 NOT NULL,
                       [FGC_LastModifier] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [FGC_LastModifyDate] datetime2 NOT NULL
                   );
                   """;
        }

        private static string BuildHqMultiStageSql()
        {
            return """
                   -- 关键位置：临时表字符串列跟随目标库默认排序规则，避免 tempdb 与业务库排序规则冲突。
                   CREATE TABLE #StorePriceTransferMulti (
                       [HGUID] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H分店代码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H商品编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H分店商品编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H多码商品编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H分店多码商品编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H供应商编码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H主条形码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H多条形码] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H进货价] decimal(18, 4) NULL,
                       [H折扣率] decimal(18, 6) NULL,
                       [H一品多码零售价] decimal(18, 4) NULL,
                       [H库存] decimal(18, 4) NULL,
                       [H库存金额] decimal(18, 4) NULL,
                       [H自动新价格] decimal(18, 4) NULL,
                       [H库存预警数] decimal(18, 4) NULL,
                       [H商品缺货日期] datetime2 NULL,
                       [H是否缺货状态] bit NULL,
                       [H最小订货量] decimal(18, 4) NULL,
                       [H最小订货量合计金额] decimal(18, 4) NULL,
                       [H活动类型] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [H满减活动代码] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [H活动开始日期] datetime2 NULL,
                       [H活动结束日期] datetime2 NULL,
                       [H满减数量] decimal(18, 4) NULL,
                       [H满减金额] decimal(18, 4) NULL,
                       [H是否自动定价] bit NULL,
                       [H是否特殊商品] bit NULL,
                       [H商品柜组号] nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
                       [H使用状态] bit NULL,
                       [H动态销售数量] decimal(18, 4) NULL,
                       [H动态销售额] decimal(18, 4) NULL,
                       [H动态成本] decimal(18, 4) NULL,
                       [H动态毛利] decimal(18, 4) NULL,
                       [H动态毛利率] decimal(18, 6) NULL,
                       [H动态销售占比] decimal(18, 6) NULL,
                       [FGC_Creator] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [FGC_CreateDate] datetime2 NULL,
                       [FGC_LastModifier] nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
                       [FGC_LastModifyDate] datetime2 NULL
                   );
                   """;
        }

        private static string BuildLocalRetailMergeSql(StorePriceTransferRequest request)
        {
            var assignments = new List<string>
            {
                "target.[UpdatedAt] = source.[UpdatedAt]",
                "target.[UpdatedBy] = source.[UpdatedBy]",
            };
            var changes = new List<string>();
            AddMergeField(request.SyncPurchasePrice, "[PurchasePrice]", assignments, changes);
            AddMergeField(request.SyncRetailPrice, "[StoreRetailPriceValue]", assignments, changes);
            AddMergeField(request.SyncDiscountRate, "[DiscountRate]", assignments, changes);
            AddMergeField(request.SyncIsAutoPricing, "[IsAutoPricing]", assignments, changes);
            AddMergeField(request.SyncIsSpecialProduct, "[IsSpecialProduct]", assignments, changes);
            return BuildMergeSql(
                "[dbo].[StoreRetailPrice]",
                "#StorePriceTransferRetail",
                "target.[StoreCode] = @TargetStoreCode AND target.[ProductCode] = source.[ProductCode] AND ISNULL(target.[IsDeleted], 0) = 0",
                changes,
                assignments,
                LocalRetailInsertColumns
            );
        }

        private static string BuildLocalMultiMergeSql(StorePriceTransferRequest request)
        {
            var assignments = new List<string>
            {
                "target.[UpdatedAt] = source.[UpdatedAt]",
                "target.[UpdatedBy] = source.[UpdatedBy]",
            };
            var changes = new List<string>();
            AddMergeField(request.SyncPurchasePrice, "[PurchasePrice]", assignments, changes);
            AddMergeField(request.SyncRetailPrice, "[MultiCodeRetailPrice]", assignments, changes);
            AddMergeField(request.SyncDiscountRate, "[DiscountRate]", assignments, changes);
            AddMergeField(request.SyncIsAutoPricing, "[IsAutoPricing]", assignments, changes);
            AddMergeField(request.SyncIsSpecialProduct, "[IsSpecialProduct]", assignments, changes);
            return BuildMergeSql(
                "[dbo].[StoreMultiCodeProduct]",
                "#StorePriceTransferMulti",
                "target.[StoreCode] = @TargetStoreCode AND target.[ProductCode] = source.[ProductCode] AND target.[MultiCodeProductCode] = source.[MultiCodeProductCode] AND ISNULL(target.[IsDeleted], 0) = 0",
                changes,
                assignments,
                LocalMultiInsertColumns
            );
        }

        private static string BuildHqRetailMergeSql(StorePriceTransferRequest request)
        {
            var assignments = new List<string>
            {
                "target.[FGC_LastModifier] = source.[FGC_LastModifier]",
                "target.[FGC_LastModifyDate] = source.[FGC_LastModifyDate]",
            };
            var changes = new List<string>();
            AddMergeField(request.SyncPurchasePrice, "[H进货价]", assignments, changes);
            AddMergeField(request.SyncRetailPrice, "[H分店零售价]", assignments, changes);
            AddMergeField(request.SyncDiscountRate, "[H折扣率]", assignments, changes);
            AddMergeField(request.SyncIsAutoPricing, "[H是否自动定价]", assignments, changes);
            AddMergeField(request.SyncIsSpecialProduct, "[H是否特殊商品]", assignments, changes);
            return BuildMergeSql(
                "[dbo].[DIC_商品零售价表]",
                "#StorePriceTransferRetail",
                "target.[H分店代码] = @TargetStoreCode AND target.[H商品编码] = source.[H商品编码]",
                changes,
                assignments,
                HqRetailStageColumns
            );
        }

        private static string BuildHqMultiMergeSql(StorePriceTransferRequest request)
        {
            var assignments = new List<string>
            {
                "target.[FGC_LastModifier] = source.[FGC_LastModifier]",
                "target.[FGC_LastModifyDate] = source.[FGC_LastModifyDate]",
            };
            var changes = new List<string>();
            AddMergeField(request.SyncPurchasePrice, "[H进货价]", assignments, changes);
            AddMergeField(request.SyncRetailPrice, "[H一品多码零售价]", assignments, changes);
            AddMergeField(request.SyncDiscountRate, "[H折扣率]", assignments, changes);
            AddMergeField(request.SyncIsAutoPricing, "[H是否自动定价]", assignments, changes);
            AddMergeField(request.SyncIsSpecialProduct, "[H是否特殊商品]", assignments, changes);
            return BuildMergeSql(
                "[dbo].[DIC_分店一品多码表]",
                "#StorePriceTransferMulti",
                "target.[H分店代码] = @TargetStoreCode AND target.[H商品编码] = source.[H商品编码] AND target.[H多码商品编码] = source.[H多码商品编码]",
                changes,
                assignments,
                HqMultiStageColumns
            );
        }

        private static string BuildMergeSql(
            string targetTable,
            string sourceTable,
            string matchCondition,
            List<string> changes,
            List<string> assignments,
            IReadOnlyList<string> insertColumns
        )
        {
            var insertColumnSql = string.Join(", ", insertColumns.Select(column => $"[{column.Trim('[', ']')}]"));
            var sourceValueSql = string.Join(", ", insertColumns.Select(column => $"source.[{column.Trim('[', ']')}]"));
            return $"""
                    DECLARE @actions TABLE ([MergeAction] nvarchar(10));

                    MERGE {targetTable} WITH (HOLDLOCK) AS target
                    USING {sourceTable} AS source
                    ON {matchCondition}
                    WHEN MATCHED AND ({string.Join(" OR ", changes)}) THEN
                        UPDATE SET {string.Join(", ", assignments)}
                    WHEN NOT MATCHED BY TARGET THEN
                        INSERT ({insertColumnSql})
                        VALUES ({sourceValueSql})
                    OUTPUT $action INTO @actions;

                    SELECT
                        SUM(CASE WHEN [MergeAction] = 'INSERT' THEN 1 ELSE 0 END) AS InsertedCount,
                        SUM(CASE WHEN [MergeAction] = 'UPDATE' THEN 1 ELSE 0 END) AS UpdatedCount
                    FROM @actions;
                    """;
        }

        private static void AddMergeField(
            bool enabled,
            string column,
            List<string> assignments,
            List<string> changes
        )
        {
            if (!enabled)
            {
                return;
            }

            assignments.Add($"target.{column} = source.{column}");
            changes.Add(BuildNullSafeChangeCondition($"target.{column}", $"source.{column}"));
        }

        private static string BuildNullSafeChangeCondition(string target, string source)
        {
            return $"(({target} <> {source}) OR ({target} IS NULL AND {source} IS NOT NULL) OR ({target} IS NOT NULL AND {source} IS NULL))";
        }

        private static void AddStageColumn<T>(DataTable table, string name)
        {
            AddStageColumn(table, name, typeof(T));
        }

        private static void AddStageColumn(DataTable table, string name, Type type)
        {
            table.Columns.Add(new DataColumn(name, type) { AllowDBNull = true });
        }

        private static object DbValue(object? value)
        {
            return value ?? DBNull.Value;
        }

        private async Task WriteLocalRetailBatchAsync(
            List<StoreRetailPrice> toInsert,
            List<StoreRetailPrice> toUpdate,
            StorePriceTransferRequest request
        )
        {
            foreach (var batch in toInsert.Chunk(WriteBatchSize))
            {
                await ExecuteInTransactionAsync(_localContext.Db, db => db.Insertable(batch.ToList()).ExecuteCommandAsync());
            }

            foreach (var batch in toUpdate.Chunk(WriteBatchSize))
            {
                var updateColumns = BuildLocalRetailUpdateColumns(request);
                await ExecuteInTransactionAsync(_localContext.Db, db => db.Updateable(batch.ToList()).UpdateColumns(updateColumns).ExecuteCommandAsync());
            }
        }

        private async Task WriteLocalMultiBatchAsync(
            List<StoreMultiCodeProduct> toInsert,
            List<StoreMultiCodeProduct> toUpdate,
            StorePriceTransferRequest request
        )
        {
            foreach (var batch in toInsert.Chunk(WriteBatchSize))
            {
                await ExecuteInTransactionAsync(_localContext.Db, db => db.Insertable(batch.ToList()).ExecuteCommandAsync());
            }

            foreach (var batch in toUpdate.Chunk(WriteBatchSize))
            {
                var updateColumns = BuildLocalMultiUpdateColumns(request);
                await ExecuteInTransactionAsync(_localContext.Db, db => db.Updateable(batch.ToList()).UpdateColumns(updateColumns).ExecuteCommandAsync());
            }
        }

        private async Task WriteHqRetailBatchAsync(
            List<DIC_商品零售价表> toInsert,
            List<DIC_商品零售价表> toUpdate,
            StorePriceTransferRequest request
        )
        {
            await AssignSqliteHqRetailIdsAsync(toInsert);
            foreach (var batch in toInsert.Chunk(WriteBatchSize))
            {
                await ExecuteInTransactionAsync(
                    _hqContext.Db,
                    db => db.CurrentConnectionConfig.DbType == DbType.Sqlite
                        ? db.Insertable(batch.ToList()).ExecuteCommandAsync()
                        : db.Insertable(batch.ToList()).IgnoreColumns(row => row.ID).ExecuteCommandAsync()
                );
            }

            foreach (var batch in toUpdate.Chunk(WriteBatchSize))
            {
                var updateColumns = BuildHqRetailUpdateColumns(request);
                await ExecuteInTransactionAsync(_hqContext.Db, db => db.Updateable(batch.ToList()).UpdateColumns(updateColumns).ExecuteCommandAsync());
            }
        }

        private async Task WriteHqMultiBatchAsync(
            List<DIC_分店一品多码表> toInsert,
            List<DIC_分店一品多码表> toUpdate,
            StorePriceTransferRequest request
        )
        {
            await AssignSqliteHqMultiIdsAsync(toInsert);
            foreach (var batch in toInsert.Chunk(WriteBatchSize))
            {
                await ExecuteInTransactionAsync(
                    _hqContext.Db,
                    db => db.CurrentConnectionConfig.DbType == DbType.Sqlite
                        ? db.Insertable(batch.ToList()).ExecuteCommandAsync()
                        : db.Insertable(batch.ToList()).IgnoreColumns(row => row.ID).ExecuteCommandAsync()
                );
            }

            foreach (var batch in toUpdate.Chunk(WriteBatchSize))
            {
                var updateColumns = BuildHqMultiUpdateColumns(request);
                await ExecuteInTransactionAsync(_hqContext.Db, db => db.Updateable(batch.ToList()).UpdateColumns(updateColumns).ExecuteCommandAsync());
            }
        }

        private static string[] BuildLocalRetailUpdateColumns(StorePriceTransferRequest request)
        {
            var columns = new List<string> { nameof(StoreRetailPrice.UpdatedAt), nameof(StoreRetailPrice.UpdatedBy) };
            if (request.SyncPurchasePrice) columns.Add(nameof(StoreRetailPrice.PurchasePrice));
            if (request.SyncRetailPrice) columns.Add(nameof(StoreRetailPrice.StoreRetailPriceValue));
            if (request.SyncDiscountRate) columns.Add(nameof(StoreRetailPrice.DiscountRate));
            if (request.SyncIsAutoPricing) columns.Add(nameof(StoreRetailPrice.IsAutoPricing));
            if (request.SyncIsSpecialProduct) columns.Add(nameof(StoreRetailPrice.IsSpecialProduct));
            return columns.ToArray();
        }

        private static string[] BuildLocalMultiUpdateColumns(StorePriceTransferRequest request)
        {
            var columns = new List<string> { nameof(StoreMultiCodeProduct.UpdatedAt), nameof(StoreMultiCodeProduct.UpdatedBy) };
            if (request.SyncPurchasePrice) columns.Add(nameof(StoreMultiCodeProduct.PurchasePrice));
            if (request.SyncRetailPrice) columns.Add(nameof(StoreMultiCodeProduct.MultiCodeRetailPrice));
            if (request.SyncDiscountRate) columns.Add(nameof(StoreMultiCodeProduct.DiscountRate));
            if (request.SyncIsAutoPricing) columns.Add(nameof(StoreMultiCodeProduct.IsAutoPricing));
            if (request.SyncIsSpecialProduct) columns.Add(nameof(StoreMultiCodeProduct.IsSpecialProduct));
            return columns.ToArray();
        }

        private static string[] BuildHqRetailUpdateColumns(StorePriceTransferRequest request)
        {
            // 关键位置：HQ 表包含库存、活动、动态销售等旁路字段，只允许写回本功能勾选字段和审计字段。
            var columns = new List<string>
            {
                nameof(DIC_商品零售价表.FGC_LastModifier),
                nameof(DIC_商品零售价表.FGC_LastModifyDate),
            };
            if (request.SyncPurchasePrice) columns.Add(nameof(DIC_商品零售价表.H进货价));
            if (request.SyncRetailPrice) columns.Add(nameof(DIC_商品零售价表.H分店零售价));
            if (request.SyncDiscountRate) columns.Add(nameof(DIC_商品零售价表.H折扣率));
            if (request.SyncIsAutoPricing) columns.Add(nameof(DIC_商品零售价表.H是否自动定价));
            if (request.SyncIsSpecialProduct) columns.Add(nameof(DIC_商品零售价表.H是否特殊商品));
            return columns.ToArray();
        }

        private static string[] BuildHqMultiUpdateColumns(StorePriceTransferRequest request)
        {
            // 关键位置：分店多码 HQ 表同样只更新价格相关字段，避免覆盖库存、活动和动态销售字段。
            var columns = new List<string>
            {
                nameof(DIC_分店一品多码表.FGC_LastModifier),
                nameof(DIC_分店一品多码表.FGC_LastModifyDate),
            };
            if (request.SyncPurchasePrice) columns.Add(nameof(DIC_分店一品多码表.H进货价));
            if (request.SyncRetailPrice) columns.Add(nameof(DIC_分店一品多码表.H一品多码零售价));
            if (request.SyncDiscountRate) columns.Add(nameof(DIC_分店一品多码表.H折扣率));
            if (request.SyncIsAutoPricing) columns.Add(nameof(DIC_分店一品多码表.H是否自动定价));
            if (request.SyncIsSpecialProduct) columns.Add(nameof(DIC_分店一品多码表.H是否特殊商品));
            return columns.ToArray();
        }

        private static async Task ExecuteInTransactionAsync(
            ISqlSugarClient db,
            Func<ISqlSugarClient, Task<int>> action
        )
        {
            await db.Ado.BeginTranAsync();
            try
            {
                await action(db);
                await db.Ado.CommitTranAsync();
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        private void ApplyHqRetailFields(
            StoreRetailPrice target,
            DIC_商品零售价表 source,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            if (request.SyncPurchasePrice)
            {
                target.PurchasePrice = source.H进货价;
            }

            if (request.SyncRetailPrice)
            {
                target.StoreRetailPriceValue = source.H分店零售价;
            }

            if (request.SyncDiscountRate)
            {
                target.DiscountRate = source.H折扣率;
            }

            if (request.SyncIsAutoPricing)
            {
                target.IsAutoPricing = source.H是否自动定价;
            }

            if (request.SyncIsSpecialProduct)
            {
                target.IsSpecialProduct = source.H是否特殊商品;
            }

            target.UpdatedAt = now;
            target.UpdatedBy = updatedBy;
        }

        private void ApplyHqMultiFields(
            StoreMultiCodeProduct target,
            DIC_分店一品多码表 source,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            if (request.SyncPurchasePrice)
            {
                target.PurchasePrice = source.H进货价 ?? 0;
            }

            if (request.SyncRetailPrice)
            {
                target.MultiCodeRetailPrice = source.H一品多码零售价 ?? 0;
            }

            if (request.SyncDiscountRate)
            {
                target.DiscountRate = source.H折扣率 ?? 0;
            }

            if (request.SyncIsAutoPricing)
            {
                target.IsAutoPricing = source.H是否自动定价 ?? false;
            }

            if (request.SyncIsSpecialProduct)
            {
                target.IsSpecialProduct = source.H是否特殊商品 ?? false;
            }

            target.UpdatedAt = now;
            target.UpdatedBy = updatedBy;
        }

        private void ApplyLocalRetailFields(
            DIC_商品零售价表 target,
            StoreRetailPrice source,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            if (request.SyncPurchasePrice)
            {
                target.H进货价 = source.PurchasePrice ?? 0;
            }

            if (request.SyncRetailPrice)
            {
                target.H分店零售价 = source.StoreRetailPriceValue ?? 0;
            }

            if (request.SyncDiscountRate)
            {
                target.H折扣率 = source.DiscountRate ?? 0;
            }

            if (request.SyncIsAutoPricing)
            {
                target.H是否自动定价 = source.IsAutoPricing;
            }

            if (request.SyncIsSpecialProduct)
            {
                target.H是否特殊商品 = source.IsSpecialProduct;
            }

            target.FGC_LastModifier = updatedBy;
            target.FGC_LastModifyDate = now;
        }

        private void ApplyLocalMultiFields(
            DIC_分店一品多码表 target,
            StoreMultiCodeProduct source,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            if (request.SyncPurchasePrice)
            {
                target.H进货价 = source.PurchasePrice ?? 0;
            }

            if (request.SyncRetailPrice)
            {
                target.H一品多码零售价 = source.MultiCodeRetailPrice ?? 0;
            }

            if (request.SyncDiscountRate)
            {
                target.H折扣率 = source.DiscountRate ?? 0;
            }

            if (request.SyncIsAutoPricing)
            {
                target.H是否自动定价 = source.IsAutoPricing;
            }

            if (request.SyncIsSpecialProduct)
            {
                target.H是否特殊商品 = source.IsSpecialProduct;
            }

            target.FGC_LastModifier = updatedBy;
            target.FGC_LastModifyDate = now;
        }

        private StoreRetailPrice BuildLocalRetailPrice(
            DIC_商品零售价表 source,
            string targetStoreCode,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            var productCode = NormalizeCode(source.H商品编码) ?? string.Empty;
            return new StoreRetailPrice
            {
                UUID = UuidHelper.GenerateUuid7(),
                StoreCode = targetStoreCode,
                ProductCode = productCode,
                StoreProductCode = targetStoreCode + productCode,
                SupplierCode = NormalizeCode(source.H供应商编码),
                PurchasePrice = request.SyncPurchasePrice ? source.H进货价 : null,
                StoreRetailPriceValue = request.SyncRetailPrice ? source.H分店零售价 : null,
                DiscountRate = request.SyncDiscountRate ? source.H折扣率 : null,
                IsAutoPricing = request.SyncIsAutoPricing && source.H是否自动定价,
                IsSpecialProduct = request.SyncIsSpecialProduct && source.H是否特殊商品,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = now,
                CreatedBy = updatedBy,
                UpdatedAt = now,
                UpdatedBy = updatedBy,
            };
        }

        private StoreMultiCodeProduct BuildLocalMultiCodeProduct(
            DIC_分店一品多码表 source,
            string targetStoreCode,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            var productCode = NormalizeCode(source.H商品编码) ?? string.Empty;
            var multiCode = NormalizeCode(source.H多码商品编码) ?? string.Empty;
            return new StoreMultiCodeProduct
            {
                UUID = UuidHelper.GenerateUuid7(),
                StoreCode = targetStoreCode,
                ProductCode = productCode,
                MultiCodeProductCode = multiCode,
                StoreMultiCodeProductCode = targetStoreCode + multiCode,
                MultiBarcode = NormalizeCode(source.H多条形码),
                PurchasePrice = request.SyncPurchasePrice ? source.H进货价 ?? 0 : null,
                MultiCodeRetailPrice = request.SyncRetailPrice ? source.H一品多码零售价 ?? 0 : null,
                DiscountRate = request.SyncDiscountRate ? source.H折扣率 ?? 0 : null,
                IsAutoPricing = request.SyncIsAutoPricing && (source.H是否自动定价 ?? false),
                IsSpecialProduct = request.SyncIsSpecialProduct && (source.H是否特殊商品 ?? false),
                IsActive = true,
                IsDeleted = false,
                CreatedAt = now,
                CreatedBy = updatedBy,
                UpdatedAt = now,
                UpdatedBy = updatedBy,
            };
        }

        private DIC_商品零售价表 BuildHqRetailPrice(
            StoreRetailPrice source,
            string targetStoreCode,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            var productCode = NormalizeCode(source.ProductCode) ?? string.Empty;
            var supplierCode = NormalizeCode(source.SupplierCode) ?? DefaultSupplierCode;
            var defaultDate = new DateTime(1900, 1, 1);
            return new DIC_商品零售价表
            {
                HGUID = UuidHelper.GenerateUuid7(),
                H分店代码 = targetStoreCode,
                H商品编码 = productCode,
                H分店商品编码 = targetStoreCode + productCode,
                H供应商编码 = supplierCode,
                H分店供应商编码 = targetStoreCode + supplierCode,
                H进货价 = request.SyncPurchasePrice ? source.PurchasePrice ?? 0 : 0,
                H分店零售价 = request.SyncRetailPrice ? source.StoreRetailPriceValue ?? 0 : 0,
                H库存 = 0,
                H库存金额 = 0,
                H库存预警数 = 0,
                H商品缺货日期 = defaultDate,
                H是否缺货状态 = false,
                H最小订货量 = 0,
                H最小订货量合计金额 = 0,
                H活动类型 = string.Empty,
                H满减活动代码 = string.Empty,
                H活动开始日期 = defaultDate,
                H活动结束日期 = defaultDate,
                H折扣率 = request.SyncDiscountRate ? source.DiscountRate ?? 0 : 0,
                H满减数量 = 0,
                H满减金额 = 0,
                H多码数量 = 0,
                H使用状态 = source.IsActive,
                H是否自动定价 = request.SyncIsAutoPricing && source.IsAutoPricing,
                H自动新价格 = 0,
                H盘点入库记录数 = 0,
                H是否特殊商品 = request.SyncIsSpecialProduct && source.IsSpecialProduct,
                H动态销售数量 = 0,
                H动态销售额 = 0,
                H动态成本 = 0,
                H动态毛利 = 0,
                H动态毛利率 = 0,
                H动态销售占比 = 0,
                FGC_Creator = updatedBy,
                FGC_CreateDate = now,
                FGC_LastModifier = updatedBy,
                FGC_LastModifyDate = now,
            };
        }

        private DIC_分店一品多码表 BuildHqMultiCodeProduct(
            StoreMultiCodeProduct source,
            string targetStoreCode,
            StorePriceTransferRequest request,
            string updatedBy,
            DateTime now
        )
        {
            var productCode = NormalizeCode(source.ProductCode) ?? string.Empty;
            var multiCode = NormalizeCode(source.MultiCodeProductCode) ?? string.Empty;
            var defaultDate = new DateTime(1900, 1, 1);
            return new DIC_分店一品多码表
            {
                HGUID = UuidHelper.GenerateUuid7(),
                H分店代码 = targetStoreCode,
                H商品编码 = productCode,
                H分店商品编码 = targetStoreCode + productCode,
                H多码商品编码 = multiCode,
                H分店多码商品编码 = targetStoreCode + multiCode,
                H供应商编码 = DefaultSupplierCode,
                H主条形码 = string.Empty,
                H多条形码 = NormalizeCode(source.MultiBarcode) ?? string.Empty,
                H进货价 = request.SyncPurchasePrice ? source.PurchasePrice ?? 0 : 0,
                H折扣率 = request.SyncDiscountRate ? source.DiscountRate ?? 0 : 0,
                H一品多码零售价 = request.SyncRetailPrice ? source.MultiCodeRetailPrice ?? 0 : 0,
                H库存 = 0,
                H库存金额 = 0,
                H自动新价格 = 0,
                H库存预警数 = 0,
                H商品缺货日期 = defaultDate,
                H是否缺货状态 = false,
                H最小订货量 = 0,
                H最小订货量合计金额 = 0,
                H活动类型 = string.Empty,
                H满减活动代码 = string.Empty,
                H活动开始日期 = defaultDate,
                H活动结束日期 = defaultDate,
                H满减数量 = 0,
                H满减金额 = 0,
                H是否自动定价 = request.SyncIsAutoPricing && source.IsAutoPricing,
                H是否特殊商品 = request.SyncIsSpecialProduct && source.IsSpecialProduct,
                H商品柜组号 = string.Empty,
                H使用状态 = source.IsActive,
                H动态销售数量 = 0,
                H动态销售额 = 0,
                H动态成本 = 0,
                H动态毛利 = 0,
                H动态毛利率 = 0,
                H动态销售占比 = 0,
                FGC_Creator = updatedBy,
                FGC_CreateDate = now,
                FGC_LastModifier = updatedBy,
                FGC_LastModifyDate = now,
            };
        }

        private async Task AssignSqliteHqRetailIdsAsync(List<DIC_商品零售价表> rows)
        {
            if (rows.Count == 0 || _hqContext.Db.CurrentConnectionConfig.DbType != DbType.Sqlite)
            {
                return;
            }

            var maxId = await _hqContext.Db.Queryable<DIC_商品零售价表>().MaxAsync(row => row.ID);
            foreach (var row in rows.Where(row => row.ID <= 0))
            {
                row.ID = ++maxId;
            }
        }

        private async Task AssignSqliteHqMultiIdsAsync(List<DIC_分店一品多码表> rows)
        {
            if (rows.Count == 0 || _hqContext.Db.CurrentConnectionConfig.DbType != DbType.Sqlite)
            {
                return;
            }

            var maxId = await _hqContext.Db.Queryable<DIC_分店一品多码表>().MaxAsync(row => row.ID);
            foreach (var row in rows.Where(row => row.ID <= 0))
            {
                row.ID = ++maxId;
            }
        }

        private void AddRetailCounts(StorePriceTransferResult result, int inserted, int updated, int skipped = 0)
        {
            result.RetailPriceInserted += inserted;
            result.RetailPriceUpdated += updated;
            result.RetailPriceSkipped += skipped;
            result.InsertedCount += inserted;
            result.UpdatedCount += updated;
            result.SkippedCount += skipped;
            result.TotalProcessed += inserted + updated;
            LogProgress(result);
        }

        private void AddMultiCounts(StorePriceTransferResult result, int inserted, int updated, int skipped = 0)
        {
            result.MultiCodeInserted += inserted;
            result.MultiCodeUpdated += updated;
            result.MultiCodeSkipped += skipped;
            result.InsertedCount += inserted;
            result.UpdatedCount += updated;
            result.SkippedCount += skipped;
            result.TotalProcessed += inserted + updated;
            LogProgress(result);
        }

        private void LogProgress(StorePriceTransferResult result)
        {
            _logger.LogInformation(
                "分店价格同步进度: Handled={HandledCount}/{TotalCount}, Inserted={InsertedCount}, Updated={UpdatedCount}, Skipped={SkippedCount}",
                result.TotalProcessed + result.SkippedCount,
                result.TotalCount,
                result.InsertedCount,
                result.UpdatedCount,
                result.SkippedCount
            );
        }

        private static void PublishProgress(
            StorePriceTransferResult result,
            Action<StorePriceTransferResult>? progressCallback
        )
        {
            if (progressCallback == null)
            {
                return;
            }

            // 关键位置：回调前复制快照，避免 job service 读到后续批次正在修改的同一个对象。
            progressCallback(new StorePriceTransferResult
            {
                TotalCount = result.TotalCount,
                RetailPriceTotal = result.RetailPriceTotal,
                MultiCodeTotal = result.MultiCodeTotal,
                TotalProcessed = result.TotalProcessed,
                InsertedCount = result.InsertedCount,
                UpdatedCount = result.UpdatedCount,
                SkippedCount = result.SkippedCount,
                FailedCount = result.FailedCount,
                RetailPriceInserted = result.RetailPriceInserted,
                RetailPriceUpdated = result.RetailPriceUpdated,
                RetailPriceSkipped = result.RetailPriceSkipped,
                MultiCodeInserted = result.MultiCodeInserted,
                MultiCodeUpdated = result.MultiCodeUpdated,
                MultiCodeSkipped = result.MultiCodeSkipped,
                Errors = result.Errors.ToList(),
            });
        }

        private static bool IsSqlServer(ISqlSugarClient db)
        {
            return db.CurrentConnectionConfig.DbType == DbType.SqlServer;
        }

        private static bool HasHqRetailChanges(
            StoreRetailPrice target,
            DIC_商品零售价表 source,
            StorePriceTransferRequest request
        )
        {
            return (request.SyncPurchasePrice && target.PurchasePrice != source.H进货价)
                || (request.SyncRetailPrice && target.StoreRetailPriceValue != source.H分店零售价)
                || (request.SyncDiscountRate && target.DiscountRate != source.H折扣率)
                || (request.SyncIsAutoPricing && target.IsAutoPricing != source.H是否自动定价)
                || (request.SyncIsSpecialProduct && target.IsSpecialProduct != source.H是否特殊商品);
        }

        private static bool HasHqMultiChanges(
            StoreMultiCodeProduct target,
            DIC_分店一品多码表 source,
            StorePriceTransferRequest request
        )
        {
            return (request.SyncPurchasePrice && target.PurchasePrice != (source.H进货价 ?? 0))
                || (request.SyncRetailPrice && target.MultiCodeRetailPrice != (source.H一品多码零售价 ?? 0))
                || (request.SyncDiscountRate && target.DiscountRate != (source.H折扣率 ?? 0))
                || (request.SyncIsAutoPricing && target.IsAutoPricing != (source.H是否自动定价 ?? false))
                || (request.SyncIsSpecialProduct && target.IsSpecialProduct != (source.H是否特殊商品 ?? false));
        }

        private static bool HasLocalRetailChanges(
            DIC_商品零售价表 target,
            StoreRetailPrice source,
            StorePriceTransferRequest request
        )
        {
            return (request.SyncPurchasePrice && target.H进货价 != (source.PurchasePrice ?? 0))
                || (request.SyncRetailPrice && target.H分店零售价 != (source.StoreRetailPriceValue ?? 0))
                || (request.SyncDiscountRate && target.H折扣率 != (source.DiscountRate ?? 0))
                || (request.SyncIsAutoPricing && target.H是否自动定价 != source.IsAutoPricing)
                || (request.SyncIsSpecialProduct && target.H是否特殊商品 != source.IsSpecialProduct);
        }

        private static bool HasLocalMultiChanges(
            DIC_分店一品多码表 target,
            StoreMultiCodeProduct source,
            StorePriceTransferRequest request
        )
        {
            return (request.SyncPurchasePrice && (target.H进货价 ?? 0) != (source.PurchasePrice ?? 0))
                || (request.SyncRetailPrice && (target.H一品多码零售价 ?? 0) != (source.MultiCodeRetailPrice ?? 0))
                || (request.SyncDiscountRate && (target.H折扣率 ?? 0) != (source.DiscountRate ?? 0))
                || (request.SyncIsAutoPricing && (target.H是否自动定价 ?? false) != source.IsAutoPricing)
                || (request.SyncIsSpecialProduct && (target.H是否特殊商品 ?? false) != source.IsSpecialProduct);
        }

        private static StorePriceTransferRequest CloneRequest(StorePriceTransferRequest request)
        {
            return new StorePriceTransferRequest
            {
                Direction = NormalizeCode(request.Direction) ?? string.Empty,
                SourceStoreCode = NormalizeCode(request.SourceStoreCode),
                TargetStoreCode = NormalizeCode(request.TargetStoreCode),
                SyncRetailPrices = request.SyncRetailPrices,
                SyncMultiCodePrices = request.SyncMultiCodePrices,
                SyncPurchasePrice = request.SyncPurchasePrice,
                SyncRetailPrice = request.SyncRetailPrice,
                SyncDiscountRate = request.SyncDiscountRate,
                SyncIsAutoPricing = request.SyncIsAutoPricing,
                SyncIsSpecialProduct = request.SyncIsSpecialProduct,
            };
        }

        private static List<string> Validate(StorePriceTransferRequest request)
        {
            var errors = new List<string>();
            if (
                !string.Equals(request.Direction, StorePriceTransferDirectionConstants.HqToLocal, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.Direction, StorePriceTransferDirectionConstants.LocalToHq, StringComparison.OrdinalIgnoreCase)
            )
            {
                errors.Add("同步方向不合法");
            }

            if (string.IsNullOrWhiteSpace(request.SourceStoreCode))
            {
                errors.Add("请选择源分店");
            }

            if (string.IsNullOrWhiteSpace(request.TargetStoreCode))
            {
                errors.Add("请选择目标分店");
            }

            // HQ 与本地是不同数据域，同名分店表示同一门店的双向同步，不能按同库复制规则拦截。

            if (!request.SyncRetailPrices && !request.SyncMultiCodePrices)
            {
                errors.Add("请至少选择一个同步表");
            }

            if (
                !request.SyncPurchasePrice
                && !request.SyncRetailPrice
                && !request.SyncDiscountRate
                && !request.SyncIsAutoPricing
                && !request.SyncIsSpecialProduct
            )
            {
                errors.Add("请至少选择一个同步字段");
            }

            return errors;
        }

        private static string? NormalizeCode(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static List<string> NormalizeCodes(IEnumerable<string?> values)
        {
            return values
                .Select(NormalizeCode)
                .Where(value => value != null)
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildMultiKey(string productCode, string multiCode)
        {
            return $"{productCode}\u001F{multiCode}";
        }

        private static Type GetHqRetailColumnType(string name)
        {
            return name switch
            {
                "H进货价" or "H分店零售价" or "H库存" or "H库存金额" or "H库存预警数" or
                    "H最小订货量" or "H最小订货量合计金额" or "H折扣率" or "H满减数量" or
                    "H满减金额" or "H多码数量" or "H自动新价格" or "H盘点入库记录数" or
                    "H动态销售数量" or "H动态销售额" or "H动态成本" or "H动态毛利" or
                    "H动态毛利率" or "H动态销售占比" => typeof(decimal),
                "H商品缺货日期" or "H活动开始日期" or "H活动结束日期" or "FGC_CreateDate" or
                    "FGC_LastModifyDate" => typeof(DateTime),
                "H是否缺货状态" or "H使用状态" or "H是否自动定价" or "H是否特殊商品" => typeof(bool),
                _ => typeof(string),
            };
        }

        private static Type GetHqMultiColumnType(string name)
        {
            return name switch
            {
                "H进货价" or "H折扣率" or "H一品多码零售价" or "H库存" or "H库存金额" or
                    "H自动新价格" or "H库存预警数" or "H最小订货量" or "H最小订货量合计金额" or
                    "H满减数量" or "H满减金额" or "H动态销售数量" or "H动态销售额" or
                    "H动态成本" or "H动态毛利" or "H动态毛利率" or "H动态销售占比" => typeof(decimal),
                "H商品缺货日期" or "H活动开始日期" or "H活动结束日期" or "FGC_CreateDate" or
                    "FGC_LastModifyDate" => typeof(DateTime),
                "H是否缺货状态" or "H是否自动定价" or "H是否特殊商品" or "H使用状态" => typeof(bool),
                _ => typeof(string),
            };
        }

        private static object? GetHqRetailColumnValue(DIC_商品零售价表 row, string name)
        {
            return name switch
            {
                "HGUID" => row.HGUID,
                "H分店代码" => row.H分店代码,
                "H商品编码" => row.H商品编码,
                "H分店商品编码" => row.H分店商品编码,
                "H供应商编码" => row.H供应商编码,
                "H分店供应商编码" => row.H分店供应商编码,
                "H进货价" => row.H进货价,
                "H分店零售价" => row.H分店零售价,
                "H库存" => row.H库存,
                "H库存金额" => row.H库存金额,
                "H库存预警数" => row.H库存预警数,
                "H商品缺货日期" => row.H商品缺货日期,
                "H是否缺货状态" => row.H是否缺货状态,
                "H最小订货量" => row.H最小订货量,
                "H最小订货量合计金额" => row.H最小订货量合计金额,
                "H活动类型" => row.H活动类型,
                "H满减活动代码" => row.H满减活动代码,
                "H活动开始日期" => row.H活动开始日期,
                "H活动结束日期" => row.H活动结束日期,
                "H折扣率" => row.H折扣率,
                "H满减数量" => row.H满减数量,
                "H满减金额" => row.H满减金额,
                "H多码数量" => row.H多码数量,
                "H使用状态" => row.H使用状态,
                "H是否自动定价" => row.H是否自动定价,
                "H自动新价格" => row.H自动新价格,
                "H盘点入库记录数" => row.H盘点入库记录数,
                "H是否特殊商品" => row.H是否特殊商品,
                "H动态销售数量" => row.H动态销售数量,
                "H动态销售额" => row.H动态销售额,
                "H动态成本" => row.H动态成本,
                "H动态毛利" => row.H动态毛利,
                "H动态毛利率" => row.H动态毛利率,
                "H动态销售占比" => row.H动态销售占比,
                "FGC_Creator" => row.FGC_Creator,
                "FGC_CreateDate" => row.FGC_CreateDate,
                "FGC_LastModifier" => row.FGC_LastModifier,
                "FGC_LastModifyDate" => row.FGC_LastModifyDate,
                _ => null,
            };
        }

        private static object? GetHqMultiColumnValue(DIC_分店一品多码表 row, string name)
        {
            return name switch
            {
                "HGUID" => row.HGUID,
                "H分店代码" => row.H分店代码,
                "H商品编码" => row.H商品编码,
                "H分店商品编码" => row.H分店商品编码,
                "H多码商品编码" => row.H多码商品编码,
                "H分店多码商品编码" => row.H分店多码商品编码,
                "H供应商编码" => row.H供应商编码,
                "H主条形码" => row.H主条形码,
                "H多条形码" => row.H多条形码,
                "H进货价" => row.H进货价,
                "H折扣率" => row.H折扣率,
                "H一品多码零售价" => row.H一品多码零售价,
                "H库存" => row.H库存,
                "H库存金额" => row.H库存金额,
                "H自动新价格" => row.H自动新价格,
                "H库存预警数" => row.H库存预警数,
                "H商品缺货日期" => row.H商品缺货日期,
                "H是否缺货状态" => row.H是否缺货状态,
                "H最小订货量" => row.H最小订货量,
                "H最小订货量合计金额" => row.H最小订货量合计金额,
                "H活动类型" => row.H活动类型,
                "H满减活动代码" => row.H满减活动代码,
                "H活动开始日期" => row.H活动开始日期,
                "H活动结束日期" => row.H活动结束日期,
                "H满减数量" => row.H满减数量,
                "H满减金额" => row.H满减金额,
                "H是否自动定价" => row.H是否自动定价,
                "H是否特殊商品" => row.H是否特殊商品,
                "H商品柜组号" => row.H商品柜组号,
                "H使用状态" => row.H使用状态,
                "H动态销售数量" => row.H动态销售数量,
                "H动态销售额" => row.H动态销售额,
                "H动态成本" => row.H动态成本,
                "H动态毛利" => row.H动态毛利,
                "H动态毛利率" => row.H动态毛利率,
                "H动态销售占比" => row.H动态销售占比,
                "FGC_Creator" => row.FGC_Creator,
                "FGC_CreateDate" => row.FGC_CreateDate,
                "FGC_LastModifier" => row.FGC_LastModifier,
                "FGC_LastModifyDate" => row.FGC_LastModifyDate,
                _ => null,
            };
        }

        private static readonly string[] LocalRetailInsertColumns =
        [
            "UUID",
            "StoreCode",
            "ProductCode",
            "StoreProductCode",
            "SupplierCode",
            "PurchasePrice",
            "StoreRetailPriceValue",
            "DiscountRate",
            "IsActive",
            "IsAutoPricing",
            "IsSpecialProduct",
            "IsDeleted",
            "CreatedAt",
            "CreatedBy",
            "UpdatedAt",
            "UpdatedBy",
        ];

        private static readonly string[] LocalMultiInsertColumns =
        [
            "UUID",
            "StoreCode",
            "ProductCode",
            "MultiCodeProductCode",
            "StoreMultiCodeProductCode",
            "MultiBarcode",
            "PurchasePrice",
            "MultiCodeRetailPrice",
            "DiscountRate",
            "IsActive",
            "IsAutoPricing",
            "IsSpecialProduct",
            "IsDeleted",
            "CreatedAt",
            "CreatedBy",
            "UpdatedAt",
            "UpdatedBy",
        ];

        private static readonly string[] HqRetailStageColumns =
        [
            "HGUID",
            "H分店代码",
            "H商品编码",
            "H分店商品编码",
            "H供应商编码",
            "H分店供应商编码",
            "H进货价",
            "H分店零售价",
            "H库存",
            "H库存金额",
            "H库存预警数",
            "H商品缺货日期",
            "H是否缺货状态",
            "H最小订货量",
            "H最小订货量合计金额",
            "H活动类型",
            "H满减活动代码",
            "H活动开始日期",
            "H活动结束日期",
            "H折扣率",
            "H满减数量",
            "H满减金额",
            "H多码数量",
            "H使用状态",
            "H是否自动定价",
            "H自动新价格",
            "H盘点入库记录数",
            "H是否特殊商品",
            "H动态销售数量",
            "H动态销售额",
            "H动态成本",
            "H动态毛利",
            "H动态毛利率",
            "H动态销售占比",
            "FGC_Creator",
            "FGC_CreateDate",
            "FGC_LastModifier",
            "FGC_LastModifyDate",
        ];

        private static readonly string[] HqMultiStageColumns =
        [
            "HGUID",
            "H分店代码",
            "H商品编码",
            "H分店商品编码",
            "H多码商品编码",
            "H分店多码商品编码",
            "H供应商编码",
            "H主条形码",
            "H多条形码",
            "H进货价",
            "H折扣率",
            "H一品多码零售价",
            "H库存",
            "H库存金额",
            "H自动新价格",
            "H库存预警数",
            "H商品缺货日期",
            "H是否缺货状态",
            "H最小订货量",
            "H最小订货量合计金额",
            "H活动类型",
            "H满减活动代码",
            "H活动开始日期",
            "H活动结束日期",
            "H满减数量",
            "H满减金额",
            "H是否自动定价",
            "H是否特殊商品",
            "H商品柜组号",
            "H使用状态",
            "H动态销售数量",
            "H动态销售额",
            "H动态成本",
            "H动态毛利",
            "H动态毛利率",
            "H动态销售占比",
            "FGC_Creator",
            "FGC_CreateDate",
            "FGC_LastModifier",
            "FGC_LastModifyDate",
        ];

        private sealed class StorePriceTransferWriteCounts
        {
            public int Inserted { get; set; }
            public int Updated { get; set; }
            public int Skipped { get; set; }
        }

        private int HqReadBatchSize => Math.Max(1, _options.HqReadBatchSize);

        private int LocalReadBatchSize => Math.Max(1, _options.HqReadBatchSize);

        private int WriteBatchSize => Math.Max(1, _options.WriteBatchSize);
    }
}
