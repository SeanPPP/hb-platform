using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Options;
using SqlSugar;

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
            string updatedBy
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

                if (
                    string.Equals(
                        normalizedRequest.Direction,
                        StorePriceTransferDirectionConstants.HqToLocal,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    await TransferHqToLocalAsync(normalizedRequest, operatorName, result);
                }
                else
                {
                    await TransferLocalToHqAsync(normalizedRequest, operatorName, result);
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
            StorePriceTransferResult result
        )
        {
            if (request.SyncRetailPrices)
            {
                await TransferHqRetailToLocalAsync(request, updatedBy, result);
            }

            if (request.SyncMultiCodePrices)
            {
                await TransferHqMultiToLocalAsync(request, updatedBy, result);
            }
        }

        private async Task TransferLocalToHqAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result
        )
        {
            if (request.SyncRetailPrices)
            {
                await TransferLocalRetailToHqAsync(request, updatedBy, result);
            }

            if (request.SyncMultiCodePrices)
            {
                await TransferLocalMultiToHqAsync(request, updatedBy, result);
            }
        }

        private async Task TransferHqRetailToLocalAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result
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
                var existingRows = await LoadLocalRetailRowsAsync(
                    targetStoreCode,
                    hqBatch.Select(row => row.H商品编码)
                );
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
                        ApplyHqRetailFields(existing, hqRow, request, updatedBy, now);
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        toInsert.Add(BuildLocalRetailPrice(hqRow, targetStoreCode, request, updatedBy, now));
                    }
                }

                await WriteLocalRetailBatchAsync(toInsert, toUpdate, request);
                AddRetailCounts(result, toInsert.Count, toUpdate.Count);
            }
        }

        private async Task TransferHqMultiToLocalAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result
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
                var existingRows = await LoadLocalMultiRowsAsync(
                    targetStoreCode,
                    hqBatch.Select(row => row.H商品编码)
                );
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
                        ApplyHqMultiFields(existing, hqRow, request, updatedBy, now);
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        toInsert.Add(BuildLocalMultiCodeProduct(hqRow, targetStoreCode, request, updatedBy, now));
                    }
                }

                await WriteLocalMultiBatchAsync(toInsert, toUpdate, request);
                AddMultiCounts(result, toInsert.Count, toUpdate.Count);
            }
        }

        private async Task TransferLocalRetailToHqAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result
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

                var existingRows = await LoadHqRetailRowsAsync(
                    request.TargetStoreCode!,
                    localBatch.Select(row => row.ProductCode)
                );
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
                        ApplyLocalRetailFields(existing, localRow, request, updatedBy, now);
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        toInsert.Add(BuildHqRetailPrice(localRow, request.TargetStoreCode!, request, updatedBy, now));
                    }
                }

                await WriteHqRetailBatchAsync(toInsert, toUpdate, request);
                AddRetailCounts(result, toInsert.Count, toUpdate.Count);
            }
        }

        private async Task TransferLocalMultiToHqAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            StorePriceTransferResult result
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

                var existingRows = await LoadHqMultiRowsAsync(
                    request.TargetStoreCode!,
                    localBatch.Select(row => row.ProductCode)
                );
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
                        ApplyLocalMultiFields(existing, localRow, request, updatedBy, now);
                        toUpdate.Add(existing);
                    }
                    else
                    {
                        toInsert.Add(BuildHqMultiCodeProduct(localRow, request.TargetStoreCode!, request, updatedBy, now));
                    }
                }

                await WriteHqMultiBatchAsync(toInsert, toUpdate, request);
                AddMultiCounts(result, toInsert.Count, toUpdate.Count);
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
                   FROM [StoreRetailPrice] WITH (NOLOCK)
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
                   FROM [StoreMultiCodeProduct] WITH (NOLOCK)
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

        private static void AddRetailCounts(StorePriceTransferResult result, int inserted, int updated)
        {
            result.RetailPriceInserted += inserted;
            result.RetailPriceUpdated += updated;
            result.InsertedCount += inserted;
            result.UpdatedCount += updated;
            result.TotalProcessed += inserted + updated;
        }

        private static void AddMultiCounts(StorePriceTransferResult result, int inserted, int updated)
        {
            result.MultiCodeInserted += inserted;
            result.MultiCodeUpdated += updated;
            result.InsertedCount += inserted;
            result.UpdatedCount += updated;
            result.TotalProcessed += inserted + updated;
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

            if (
                !string.IsNullOrWhiteSpace(request.SourceStoreCode)
                && string.Equals(request.SourceStoreCode, request.TargetStoreCode, StringComparison.OrdinalIgnoreCase)
            )
            {
                errors.Add("源分店和目标分店不能相同");
            }

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

        private int HqReadBatchSize => Math.Max(1, _options.HqReadBatchSize);

        private int LocalReadBatchSize => Math.Max(1, _options.HqReadBatchSize);

        private int WriteBatchSize => Math.Max(1, _options.WriteBatchSize);
    }
}
