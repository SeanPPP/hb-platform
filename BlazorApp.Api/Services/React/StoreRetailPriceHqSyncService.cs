using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店零售价 HQ 同步统一实现。
    /// 这里集中处理映射、keyset 分页、批量事务和全量影子表切换，旧入口只做代理。
    /// </summary>
    public class StoreRetailPriceHqSyncService : IStoreRetailPriceHqSyncService
    {
        private const string ShadowTableName = "StoreRetailPrice_Shadow";
        private const string TaskFull = "SyncStoreRetailPricesFull";
        private const string TaskIncremental = "SyncStoreRetailPricesIncremental";
        private const string DatabaseLockResource = "StoreRetailPrice_HQ_FULL_SYNC_SWAP";
        private static readonly SemaphoreSlim SyncLock = new(1, 1);

        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly IMapper _mapper;
        private readonly ILogger<StoreRetailPriceHqSyncService> _logger;
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly StoreRetailPriceHqSyncOptions _options;

        public StoreRetailPriceHqSyncService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            IMapper mapper,
            ILogger<StoreRetailPriceHqSyncService> logger,
            ScheduledTaskLogService taskLogService,
            IOptions<StoreRetailPriceHqSyncOptions>? options = null
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _mapper = mapper;
            _logger = logger;
            _taskLogService = taskLogService;
            _options = options?.Value ?? new StoreRetailPriceHqSyncOptions();
        }

        public async Task<SyncResult> SyncFullAsync(List<string>? selectedStoreCodes = null)
        {
            if (!await SyncLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                return BuildFailedResult("已有分店零售价同步任务正在执行，请稍后再试");
            }

            var result = new SyncResult { StartTime = DateTime.UtcNow };
            ScheduledTaskLog? taskLog = null;
            ISqlSugarClient? db = null;
            int? originalTimeout = null;

            try
            {
                taskLog = await _taskLogService.LogTaskStartAsync(
                    TaskFull,
                    new TaskParameters(),
                    TaskTrigger.Manual
                );
                db = _localContext.Db;
                originalTimeout = db.Ado.CommandTimeOut;
                db.Ado.CommandTimeOut = _options.CommandTimeoutSeconds;

                CheckHqConnection();
                var isWholeTableSync = selectedStoreCodes?.Any() != true;
                var targetStoreCodes = await ResolveTargetStoreCodesAsync(selectedStoreCodes);
                var useShadowSwitch = isWholeTableSync;

                if (!useShadowSwitch)
                {
                    var inserted = await ReplaceSelectedStoresDirectAsync(db, targetStoreCodes);
                    result.AddedCount = inserted;
                    result.TotalCount = inserted;
                    result.IsSuccess = true;
                    result.Message = $"分店零售价指定分店全量同步完成，共写入 {inserted:N0} 条";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    await FinishTaskIfStartedAsync(taskLog, result);
                    return result;
                }

                await AcquireDatabaseLockAsync(db);
                try
                {
                    var syncRunId = await PrepareShadowTableAsync(db);
                    var totalInserted = await CopyHqToTableByKeysetAsync(
                        db,
                        new List<string>(),
                        ShadowTableName
                    );
                    await ValidateShadowTableAsync(db, syncRunId, totalInserted);
                    await SwitchShadowTableAsync(db, syncRunId);

                    result.AddedCount = totalInserted;
                    result.TotalCount = totalInserted;
                    result.IsSuccess = true;
                    result.Message = $"分店零售价全量同步完成，影子表切换成功，共 {totalInserted:N0} 条";
                }
                finally
                {
                    await ReleaseDatabaseLockAsync(db);
                }
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                await FinishTaskIfStartedAsync(taskLog, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店零售价全量同步失败");
                result.IsSuccess = false;
                result.ErrorCount = 1;
                result.Message = $"分店零售价全量同步失败: {ex.Message}";
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                await FinishTaskIfStartedAsync(taskLog, result);
                return result;
            }
            finally
            {
                if (db is not null && originalTimeout.HasValue)
                {
                    db.Ado.CommandTimeOut = originalTimeout.Value;
                }
                SyncLock.Release();
            }
        }

        public async Task<SyncResult> SyncIncrementalAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        )
        {
            if (!await SyncLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                return BuildFailedResult("已有分店零售价同步任务正在执行，请稍后再试");
            }

            var result = new SyncResult { StartTime = DateTime.UtcNow };
            ScheduledTaskLog? taskLog = null;
            ISqlSugarClient? db = null;
            int? originalTimeout = null;

            try
            {
                taskLog = await _taskLogService.LogTaskStartAsync(
                    TaskIncremental,
                    new TaskParameters(),
                    TaskTrigger.Manual
                );
                db = _localContext.Db;
                originalTimeout = db.Ado.CommandTimeOut;
                db.Ado.CommandTimeOut = _options.CommandTimeoutSeconds;

                CheckHqConnection();
                var effectiveStart = startDate ?? DateTime.UtcNow.AddDays(-_options.DefaultIncrementalDays);
                var effectiveEnd = endDate?.Date.AddDays(1).AddTicks(-1);
                if (effectiveEnd.HasValue && effectiveEnd.Value < effectiveStart)
                {
                    throw new InvalidOperationException("结束日期不能早于起始日期");
                }

                var targetStoreCodes = await ResolveTargetStoreCodesAsync(selectedStoreCodes);
                var inserted = 0;
                var updated = 0;

                foreach (var storeChunk in ToStoreChunks(targetStoreCodes, targetStoreCodes))
                {
                    if (_hqContext.Db.CurrentConnectionConfig.DbType == DbType.Sqlite)
                    {
                        var hqRows = await BuildHqQuery(storeChunk, effectiveStart, effectiveEnd)
                            .OrderBy(x => x.FGC_LastModifyDate)
                            .OrderBy(x => x.ID)
                            .ToListAsync();

                        foreach (var hqBatch in hqRows.Chunk(_options.HqReadBatchSize))
                        {
                            var localBatch = hqBatch
                                .Select(MapHqRetailPrice)
                                .Where(x => !string.IsNullOrWhiteSpace(x.StoreCode) && !string.IsNullOrWhiteSpace(x.ProductCode))
                                .ToList();

                            var batchResult = await UpsertBatchAsync(db, localBatch);
                            inserted += batchResult.Inserted;
                            updated += batchResult.Updated;
                        }

                        continue;
                    }

                    DateTime? lastModify = null;
                    var lastId = 0;

                    while (true)
                    {
                        var query = BuildHqQuery(storeChunk, effectiveStart, effectiveEnd);
                        if (lastModify.HasValue)
                        {
                            var cursorModify = lastModify.Value;
                            var cursorId = lastId;
                            query = query.Where(x => x.FGC_LastModifyDate > cursorModify || (x.FGC_LastModifyDate == cursorModify && x.ID > cursorId));
                        }

                        var hqBatch = await query
                            .OrderBy(x => x.FGC_LastModifyDate)
                            .OrderBy(x => x.ID)
                            .Take(_options.HqReadBatchSize)
                            .ToListAsync();

                        if (hqBatch.Count == 0)
                        {
                            break;
                        }

                        var localBatch = hqBatch
                            .Select(MapHqRetailPrice)
                            .Where(x => !string.IsNullOrWhiteSpace(x.StoreCode) && !string.IsNullOrWhiteSpace(x.ProductCode))
                            .ToList();

                        var batchResult = await UpsertBatchAsync(db, localBatch);
                        inserted += batchResult.Inserted;
                        updated += batchResult.Updated;

                        var last = hqBatch[^1];
                        lastModify = last.FGC_LastModifyDate;
                        lastId = last.ID;
                    }
                }

                result.AddedCount = inserted;
                result.UpdatedCount = updated;
                result.TotalCount = inserted + updated;
                result.IsSuccess = true;
                result.Message = $"分店零售价增量同步完成，新增 {inserted:N0} 条，更新 {updated:N0} 条";
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                await FinishTaskIfStartedAsync(taskLog, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店零售价增量同步失败");
                result.IsSuccess = false;
                result.ErrorCount = 1;
                result.Message = $"分店零售价增量同步失败: {ex.Message}";
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                await FinishTaskIfStartedAsync(taskLog, result);
                return result;
            }
            finally
            {
                if (db is not null && originalTimeout.HasValue)
                {
                    db.Ado.CommandTimeOut = originalTimeout.Value;
                }
                SyncLock.Release();
            }
        }

        public async Task<ApiResponse<SyncRetailPriceFromHqResult>> SyncForPageAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        )
        {
            var requestId = Guid.NewGuid().ToString("N");
            var startedAt = DateTime.UtcNow;
            var payload = new SyncRetailPriceFromHqResult
            {
                RequestId = requestId,
                Status = "Running",
                StartedAt = startedAt,
            };

            if (!startDate.HasValue || !endDate.HasValue)
            {
                return BuildPageFailedResponse(
                    payload,
                    startedAt,
                    "请选择起止日期",
                    "INVALID_DATE_RANGE"
                );
            }

            var effectiveEnd = endDate.Value.Date.AddDays(1).AddTicks(-1);
            if (effectiveEnd < startDate.Value)
            {
                return BuildPageFailedResponse(
                    payload,
                    startedAt,
                    "结束日期不能早于起始日期",
                    "INVALID_DATE_RANGE"
                );
            }

            var targetStoreCodes = await ResolveRequiredTargetStoreCodesAsync(selectedStoreCodes);
            if (targetStoreCodes.Count == 0)
            {
                return BuildPageFailedResponse(
                    payload,
                    startedAt,
                    "请选择有效的启用分店",
                    "INVALID_STORE_SCOPE"
                );
            }

            var syncResult = await SyncIncrementalAsync(targetStoreCodes, startDate.Value, effectiveEnd);
            payload = new SyncRetailPriceFromHqResult
            {
                RequestId = requestId,
                Status = syncResult.IsSuccess ? "Succeeded" : "Failed",
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                AddedCount = syncResult.AddedCount,
                UpdatedCount = syncResult.UpdatedCount,
                TotalProcessed = syncResult.TotalCount,
                DurationMs = (long)syncResult.Duration.TotalMilliseconds,
            };

            if (!syncResult.IsSuccess)
            {
                payload.Errors.Add(syncResult.Message);
                return ApiResponse<SyncRetailPriceFromHqResult>.Error(
                    syncResult.Message,
                    "HQ_RETAIL_PRICE_SYNC_ERROR",
                    payload
                );
            }

            return ApiResponse<SyncRetailPriceFromHqResult>.OK(payload, syncResult.Message);
        }

        private ISugarQueryable<DIC_商品零售价表> BuildHqQuery(
            List<string> targetStoreCodes,
            DateTime? startDate,
            DateTime? endDate
        )
        {
            var query = _hqContext.Db.Queryable<DIC_商品零售价表>()
                .Where(x =>
                    x.H使用状态 == true
                    && !string.IsNullOrEmpty(x.H分店代码)
                    && !string.IsNullOrEmpty(x.H商品编码)
                );

            if (targetStoreCodes.Count > 0)
            {
                query = query.Where(x => targetStoreCodes.Contains(x.H分店代码));
            }

            if (startDate.HasValue)
            {
                query = query.Where(x => x.FGC_LastModifyDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(x => x.FGC_LastModifyDate <= endDate.Value);
            }

            return query;
        }

        private void CheckHqConnection()
        {
            if (_hqContext.Db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            {
                return;
            }

            _hqContext.CheckConnection();
        }

        private static IEnumerable<List<string>> ToStoreChunks(
            List<string> targetStoreCodes,
            List<string>? selectedStoreCodes
        )
        {
            if (selectedStoreCodes?.Any() != true)
            {
                yield return new List<string>();
                yield break;
            }

            foreach (var chunk in targetStoreCodes.Chunk(1000))
            {
                yield return chunk.ToList();
            }
        }

        private async Task<List<string>> ResolveTargetStoreCodesAsync(List<string>? selectedStoreCodes)
        {
            var activeStoreCodes = await _localContext.Db.Queryable<Store>()
                .Where(x => x.IsActive && !x.IsDeleted && x.StoreCode != null)
                .Select(x => x.StoreCode!)
                .ToListAsync();

            if (selectedStoreCodes?.Any() != true)
            {
                return activeStoreCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            var activeSet = activeStoreCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return selectedStoreCodes
                .Where(x => !string.IsNullOrWhiteSpace(x) && activeSet.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> ResolveRequiredTargetStoreCodesAsync(
            List<string>? selectedStoreCodes
        )
        {
            var requested = selectedStoreCodes?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (requested.Count == 0)
            {
                return new List<string>();
            }

            var activeStoreCodes = await _localContext.Db.Queryable<Store>()
                .Where(x => x.IsActive && !x.IsDeleted && x.StoreCode != null)
                .Select(x => x.StoreCode!)
                .ToListAsync();
            var activeSet = activeStoreCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            return requested.All(activeSet.Contains)
                ? requested
                : new List<string>();
        }

        private static ApiResponse<SyncRetailPriceFromHqResult> BuildPageFailedResponse(
            SyncRetailPriceFromHqResult payload,
            DateTime startedAt,
            string message,
            string errorCode
        )
        {
            payload.Status = "Failed";
            payload.CompletedAt = DateTime.UtcNow;
            payload.DurationMs = (long)(payload.CompletedAt.Value - startedAt).TotalMilliseconds;
            payload.Errors.Add(message);
            return ApiResponse<SyncRetailPriceFromHqResult>.Error(message, errorCode, payload);
        }

        private StoreRetailPrice MapHqRetailPrice(DIC_商品零售价表 hq)
        {
            var entity = _mapper.Map<StoreRetailPrice>(hq);
            entity.IsDeleted = false;
            entity.CreatedBy ??= "ReactSync";
            entity.UpdatedBy = "ReactSync";
            return entity;
        }

        private async Task<int> CopyHqToTableByKeysetAsync(
            ISqlSugarClient targetDb,
            List<string> targetStoreCodes,
            string tableName
        )
        {
            var lastId = 0;
            var inserted = 0;

            foreach (var storeChunk in ToStoreChunks(targetStoreCodes, targetStoreCodes))
            {
                lastId = 0;

                while (true)
                {
                    var hqBatch = await _hqContext.Db.Queryable<DIC_商品零售价表>()
                        .Where(x =>
                            x.ID > lastId
                            && x.H使用状态 == true
                            && !string.IsNullOrEmpty(x.H分店代码)
                            && !string.IsNullOrEmpty(x.H商品编码)
                        )
                        .WhereIF(storeChunk.Count > 0, x => storeChunk.Contains(x.H分店代码))
                        .OrderBy(x => x.ID)
                        .Take(_options.HqReadBatchSize)
                        .ToListAsync();

                    if (hqBatch.Count == 0)
                    {
                        break;
                    }

                    var localBatch = hqBatch.Select(MapHqRetailPrice).ToList();
                    await targetDb.Fastest<StoreRetailPrice>()
                        .AS(tableName)
                        .PageSize(_options.WriteBatchSize)
                        .BulkCopyAsync(localBatch);

                    inserted += localBatch.Count;
                    lastId = hqBatch[^1].ID;
                }
            }

            return inserted;
        }

        private async Task<int> ReplaceSelectedStoresDirectAsync(
            ISqlSugarClient db,
            List<string> targetStoreCodes
        )
        {
            if (targetStoreCodes.Count == 0)
            {
                return 0;
            }

            var inserted = 0;
            await db.Ado.BeginTranAsync();
            try
            {
                foreach (var storeChunk in targetStoreCodes.Chunk(1000))
                {
                    var stores = storeChunk.ToList();
                    await db.Deleteable<StoreRetailPrice>()
                        .Where(x => x.StoreCode != null && stores.Contains(x.StoreCode))
                        .ExecuteCommandAsync();
                }

                inserted = await CopyHqToTableByKeysetAsync(db, targetStoreCodes, "StoreRetailPrice");
                await db.Ado.CommitTranAsync();
                return inserted;
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        private async Task<BatchResultDto> UpsertBatchAsync(ISqlSugarClient db, List<StoreRetailPrice> batch)
        {
            var result = new BatchResultDto();
            if (batch.Count == 0)
            {
                return result;
            }

            var dedupedBatch = batch
                .GroupBy(BuildIncomingDedupKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                        .ThenByDescending(x => x.UUID)
                        .First()
                )
                .ToList();

            var existing = await QueryExistingBySyncKeysAsync(db, dedupedBatch);
            var existingByUuid = existing
                .Where(x => !string.IsNullOrWhiteSpace(x.UUID))
                .GroupBy(x => x.UUID, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var existingByBusinessKey = existing
                .GroupBy(x => (x.StoreCode!, x.ProductCode!, x.SupplierCode ?? string.Empty))
                .ToDictionary(x => x.Key, x => x.First());

            var now = DateTime.UtcNow;
            var toInsert = new List<StoreRetailPrice>();
            var toUpdate = new List<StoreRetailPrice>();

            foreach (var incoming in dedupedBatch)
            {
                var key = (incoming.StoreCode!, incoming.ProductCode!, incoming.SupplierCode ?? string.Empty);
                StoreRetailPrice? current = null;
                if (!string.IsNullOrWhiteSpace(incoming.UUID))
                {
                    existingByUuid.TryGetValue(incoming.UUID, out current);
                }

                current ??= existingByBusinessKey.GetValueOrDefault(key);

                if (current is not null)
                {
                    // HQ 的 HGUID 是跨供应商变更最稳定的同步键，优先用它恢复旧行，避免重复主键插入。
                    current.StoreProductCode = incoming.StoreProductCode;
                    current.SupplierCode = incoming.SupplierCode;
                    current.PurchasePrice = incoming.PurchasePrice;
                    current.StoreRetailPriceValue = incoming.StoreRetailPriceValue;
                    current.DiscountRate = incoming.DiscountRate;
                    current.IsActive = incoming.IsActive;
                    current.IsAutoPricing = incoming.IsAutoPricing;
                    current.IsSpecialProduct = incoming.IsSpecialProduct;
                    current.IsDeleted = false;
                    current.UpdatedAt = incoming.UpdatedAt ?? now;
                    current.UpdatedBy = "ReactSync";
                    toUpdate.Add(current);
                    existingByBusinessKey[key] = current;
                    if (!string.IsNullOrWhiteSpace(current.UUID))
                    {
                        existingByUuid[current.UUID] = current;
                    }
                }
                else
                {
                    incoming.CreatedAt = incoming.CreatedAt == default ? now : incoming.CreatedAt;
                    incoming.UpdatedAt ??= now;
                    incoming.IsDeleted = false;
                    toInsert.Add(incoming);
                }
            }

            await db.Ado.BeginTranAsync();
            try
            {
                if (toUpdate.Count > 0)
                {
                    if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
                    {
                        await db.Updateable(toUpdate).ExecuteCommandAsync();
                    }
                    else
                    {
                        await db.Fastest<StoreRetailPrice>()
                            .AS("StoreRetailPrice")
                            .PageSize(_options.WriteBatchSize)
                            .BulkUpdateAsync(toUpdate);
                    }
                }

                if (toInsert.Count > 0)
                {
                    if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
                    {
                        await db.Insertable(toInsert).ExecuteCommandAsync();
                    }
                    else
                    {
                        await db.Fastest<StoreRetailPrice>()
                            .AS("StoreRetailPrice")
                            .PageSize(_options.WriteBatchSize)
                            .BulkCopyAsync(toInsert);
                    }
                }

                await db.Ado.CommitTranAsync();
                result.Inserted = toInsert.Count;
                result.Updated = toUpdate.Count;
                return result;
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        private static async Task<List<StoreRetailPrice>> QueryExistingBySyncKeysAsync(
            ISqlSugarClient db,
            List<StoreRetailPrice> batch
        )
        {
            var existing = new List<StoreRetailPrice>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var uuidChunk in batch
                .Select(x => x.UUID)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Chunk(1000))
            {
                var uuids = uuidChunk.ToList();
                var rows = await db.Queryable<StoreRetailPrice>()
                    .Where(x => uuids.Contains(x.UUID))
                    .ToListAsync();
                AddDistinct(existing, seenKeys, rows);
            }

            foreach (var group in batch.GroupBy(x => x.StoreCode!))
            {
                foreach (var productChunk in group.Select(x => x.ProductCode!).Distinct().Chunk(1000))
                {
                    var products = productChunk.ToList();
                    var rows = await db.Queryable<StoreRetailPrice>()
                        .Where(x =>
                            x.StoreCode == group.Key
                            && x.ProductCode != null
                            && products.Contains(x.ProductCode)
                        )
                        .ToListAsync();
                    AddDistinct(existing, seenKeys, rows);
                }
            }

            return existing;
        }

        private static string BuildIncomingDedupKey(StoreRetailPrice value)
        {
            if (!string.IsNullOrWhiteSpace(value.UUID))
            {
                return $"UUID:{value.UUID}";
            }

            return $"KEY:{value.StoreCode}|{value.ProductCode}|{value.SupplierCode ?? string.Empty}";
        }

        private static void AddDistinct(
            List<StoreRetailPrice> target,
            HashSet<string> seenKeys,
            IEnumerable<StoreRetailPrice> rows
        )
        {
            foreach (var row in rows)
            {
                var key = !string.IsNullOrWhiteSpace(row.UUID)
                    ? $"UUID:{row.UUID}"
                    : $"KEY:{row.StoreCode}|{row.ProductCode}|{row.SupplierCode ?? string.Empty}";
                if (seenKeys.Add(key))
                {
                    target.Add(row);
                }
            }
        }

        private static async Task<long> PrepareShadowTableAsync(ISqlSugarClient db)
        {
            var syncRunId = await db.Ado.SqlQuerySingleAsync<long>(
                """
                DECLARE @SyncRunId BIGINT;
                EXEC dbo.usp_StoreRetailPriceShadow_Prepare
                    @SyncRunId = @SyncRunId OUTPUT,
                    @TriggeredBy = N'ApiStoreRetailPriceHqSync',
                    @DropExistingShadow = 1;
                SELECT @SyncRunId;
                """
            );

            return syncRunId;
        }

        private static async Task AcquireDatabaseLockAsync(ISqlSugarClient db)
        {
            var lockResult = await db.Ado.SqlQuerySingleAsync<int>(
                """
                DECLARE @Result INT;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 30000;
                SELECT @Result;
                """,
                new SugarParameter("@Resource", DatabaseLockResource)
            );

            if (lockResult < 0)
            {
                throw new InvalidOperationException("获取 StoreRetailPrice HQ 全量同步数据库锁失败");
            }
        }

        private static async Task ReleaseDatabaseLockAsync(ISqlSugarClient db)
        {
            await db.Ado.ExecuteCommandAsync(
                """
                EXEC sys.sp_releaseapplock
                    @Resource = @Resource,
                    @LockOwner = N'Session';
                """,
                new SugarParameter("@Resource", DatabaseLockResource)
            );
        }

        private static async Task ValidateShadowTableAsync(
            ISqlSugarClient db,
            long syncRunId,
            int expectedCount
        )
        {
            await db.Ado.ExecuteCommandAsync(
                """
                EXEC dbo.usp_StoreRetailPriceShadow_Validate
                    @SyncRunId = @SyncRunId,
                    @SourceRowCount = @SourceRowCount;
                """,
                new SugarParameter("@SyncRunId", syncRunId),
                new SugarParameter("@SourceRowCount", expectedCount)
            );
        }

        private static async Task SwitchShadowTableAsync(ISqlSugarClient db, long syncRunId)
        {
            await db.Ado.ExecuteCommandAsync(
                """
                EXEC dbo.usp_StoreRetailPriceShadow_Swap
                    @SyncRunId = @SyncRunId,
                    @LockTimeoutMs = 30000;
                """,
                new SugarParameter("@SyncRunId", syncRunId)
            );
        }

        private async Task FinishTaskAsync(Guid taskLogId, SyncResult result)
        {
            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLogId);
            }
            else
            {
                await _taskLogService.LogTaskFailureAsync(taskLogId, result.Message);
            }
        }

        private async Task FinishTaskIfStartedAsync(ScheduledTaskLog? taskLog, SyncResult result)
        {
            if (taskLog is null)
            {
                return;
            }

            await FinishTaskAsync(taskLog.Id, result);
        }

        private static SyncResult BuildFailedResult(string message)
        {
            var now = DateTime.UtcNow;
            return new SyncResult
            {
                StartTime = now,
                EndTime = now,
                IsSuccess = false,
                ErrorCount = 1,
                Message = message,
            };
        }
    }
}
