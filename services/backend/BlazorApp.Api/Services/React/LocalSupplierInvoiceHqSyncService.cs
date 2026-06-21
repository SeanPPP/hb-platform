using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店进货单从 HQ 增量同步服务。
    /// 页面入口只允许安全增量 upsert，避免业务页面触发清表式全量同步。
    /// </summary>
    public class LocalSupplierInvoiceHqSyncService : ILocalSupplierInvoiceHqSyncService
    {
        private const int DefaultIncrementalDays = 30;
        private const int PageSize = 1000;
        private const int ParentGuidChunkSize = 500;
        private const int ContainsChunkSize = 1000;
        private const string DatabaseLockResource = "LocalSupplierInvoiceHqSync";
        private static readonly SemaphoreSlim SyncLock = new(1, 1);

        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly IMapper _mapper;
        private readonly ILogger<LocalSupplierInvoiceHqSyncService> _logger;

        public LocalSupplierInvoiceHqSyncService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            IMapper mapper,
            ILogger<LocalSupplierInvoiceHqSyncService> logger
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ApiResponse<LocalSupplierInvoiceHqSyncResult>> SyncForPageAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        )
        {
            if (!await SyncLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                return ApiResponse<LocalSupplierInvoiceHqSyncResult>.Error(
                    "已有分店进货单 HQ 同步任务正在执行，请稍后再试",
                    "LOCAL_SUPPLIER_INVOICE_HQ_SYNC_BUSY"
                );
            }

            var startedAt = DateTime.UtcNow;
            var payload = new LocalSupplierInvoiceHqSyncResult
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Status = "Running",
                StartedAt = startedAt,
            };
            ISqlSugarClient? databaseLockClient = null;

            try
            {
                var effectiveStart = startDate ?? DateTime.UtcNow.AddDays(-DefaultIncrementalDays);
                var effectiveEnd = endDate?.Date.AddDays(1).AddTicks(-1);
                if (effectiveEnd.HasValue && effectiveEnd.Value < effectiveStart)
                {
                    return BuildFailedResponse(payload, startedAt, "结束日期不能早于起始日期", "INVALID_DATE_RANGE");
                }

                var targetStoreCodes = await ResolveTargetStoreCodesAsync(selectedStoreCodes);
                if (targetStoreCodes.Count == 0)
                {
                    return BuildFailedResponse(payload, startedAt, "请选择有效的启用分店", "INVALID_STORE_SCOPE");
                }

                _hqContext.CheckConnection();
                databaseLockClient = await TryAcquireDatabaseLockAsync(_localContext.Db);

                var invoiceResult = await SyncInvoicesAsync(targetStoreCodes, effectiveStart, effectiveEnd);
                var detailResult = await SyncDetailsAsync(
                    targetStoreCodes,
                    effectiveStart,
                    effectiveEnd,
                    invoiceResult.AffectedInvoiceGuids,
                    invoiceResult
                );

                payload.InvoiceAddedCount = invoiceResult.Added;
                payload.InvoiceUpdatedCount = invoiceResult.Updated;
                payload.DetailAddedCount = detailResult.Added;
                payload.DetailUpdatedCount = detailResult.Updated;
                payload.TotalProcessed =
                    invoiceResult.Added
                    + invoiceResult.Updated
                    + detailResult.Added
                    + detailResult.Updated;
                payload.Errors.AddRange(invoiceResult.Errors);
                payload.Errors.AddRange(detailResult.Errors);
                payload.Status = payload.Errors.Count == 0 ? "Succeeded" : "Failed";
                payload.CompletedAt = DateTime.UtcNow;
                payload.DurationMs = (long)(payload.CompletedAt.Value - startedAt).TotalMilliseconds;

                if (payload.Errors.Count > 0)
                {
                    return BuildFailedPayloadResponse(
                        payload,
                        "分店进货单从 HQ 同步完成，但存在错误",
                        "LOCAL_SUPPLIER_INVOICE_HQ_SYNC_ERROR"
                    );
                }

                var message = payload.TotalProcessed == 0
                    ? "没有需要同步的数据"
                    : $"分店进货单从 HQ 同步完成，主表新增 {payload.InvoiceAddedCount} 条、更新 {payload.InvoiceUpdatedCount} 条，明细新增 {payload.DetailAddedCount} 条、更新 {payload.DetailUpdatedCount} 条";
                return ApiResponse<LocalSupplierInvoiceHqSyncResult>.OK(payload, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店进货单从 HQ 同步失败");
                return BuildFailedResponse(
                    payload,
                    startedAt,
                    $"分店进货单从 HQ 同步失败: {ex.Message}",
                    "LOCAL_SUPPLIER_INVOICE_HQ_SYNC_ERROR"
                );
            }
            finally
            {
                if (databaseLockClient != null)
                {
                    try
                    {
                        await ReleaseDatabaseLockAsync(databaseLockClient);
                    }
                    finally
                    {
                        databaseLockClient.Dispose();
                    }
                }
                SyncLock.Release();
            }
        }

        private async Task<List<string>> ResolveTargetStoreCodesAsync(List<string>? selectedStoreCodes)
        {
            var activeStoreCodes = await _localContext.Db.Queryable<Store>()
                .Where(x => x.IsActive && x.IsDeleted == false && x.StoreCode != null)
                .Select(x => x.StoreCode!)
                .ToListAsync();
            var activeSet = activeStoreCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (selectedStoreCodes?.Any() != true)
            {
                return activeSet.ToList();
            }

            var requested = selectedStoreCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return requested.Count > 0 && requested.All(activeSet.Contains)
                ? requested
                : new List<string>();
        }

        private async Task<SyncBatchResult> SyncInvoicesAsync(
            List<string> targetStoreCodes,
            DateTime startDate,
            DateTime? endDate
        )
        {
            var result = new SyncBatchResult();
            DateTime? lastModifyDate = null;
            var lastId = 0;

            while (true)
            {
                var query = BuildHqInvoiceQuery(targetStoreCodes, startDate, endDate);
                if (lastModifyDate.HasValue)
                {
                    query = query.Where(x =>
                        x.FGC_LastModifyDate > lastModifyDate.Value
                        || (x.FGC_LastModifyDate == lastModifyDate.Value && x.ID > lastId)
                    );
                }

                var hqBatch = await query
                    .OrderBy(x => x.FGC_LastModifyDate)
                    .OrderBy(x => x.ID)
                    .Take(PageSize)
                    .ToListAsync();
                if (hqBatch.Count == 0)
                    break;

                var localBatch = _mapper.Map<List<StoreLocalSupplierInvoice>>(hqBatch)
                    .Where(x => !string.IsNullOrWhiteSpace(x.InvoiceGUID))
                    .ToList();
                await UpsertInvoicesAsync(localBatch, result);

                var lastRow = hqBatch[^1];
                lastModifyDate = lastRow.FGC_LastModifyDate ?? lastModifyDate;
                lastId = lastRow.ID;
            }

            return result;
        }

        private async Task<SyncBatchResult> SyncDetailsAsync(
            List<string> targetStoreCodes,
            DateTime startDate,
            DateTime? endDate,
            HashSet<string> syncedInvoiceGuids,
            SyncBatchResult invoiceResult
        )
        {
            var result = new SyncBatchResult();
            var processedDetailGuids = new HashSet<string>();

            await SyncModifiedDetailsAsync(
                targetStoreCodes,
                startDate,
                endDate,
                invoiceResult,
                result,
                processedDetailGuids
            );

            foreach (var parentGuidChunk in syncedInvoiceGuids.Chunk(ParentGuidChunkSize))
            {
                await SyncDetailsByParentGuidsAsync(
                    targetStoreCodes,
                    parentGuidChunk.ToList(),
                    invoiceResult,
                    result,
                    processedDetailGuids
                );
            }

            return result;
        }

        private async Task SyncModifiedDetailsAsync(
            List<string> targetStoreCodes,
            DateTime startDate,
            DateTime? endDate,
            SyncBatchResult invoiceResult,
            SyncBatchResult detailResult,
            HashSet<string> processedDetailGuids
        )
        {
            DateTime? lastModifyDate = null;
            var lastId = 0;

            while (true)
            {
                var query = BuildHqModifiedDetailQuery(targetStoreCodes, startDate, endDate);
                if (lastModifyDate.HasValue)
                {
                    query = query.Where(x =>
                        x.FGC_LastModifyDate > lastModifyDate.Value
                        || (x.FGC_LastModifyDate == lastModifyDate.Value && x.ID > lastId)
                    );
                }

                var hqBatch = await query
                    .OrderBy(x => x.FGC_LastModifyDate)
                    .OrderBy(x => x.ID)
                    .Take(PageSize)
                    .ToListAsync();
                if (hqBatch.Count == 0)
                    break;

                await SyncDetailBatchAsync(hqBatch, invoiceResult, detailResult, processedDetailGuids);

                var lastRow = hqBatch[^1];
                lastModifyDate = lastRow.FGC_LastModifyDate ?? lastModifyDate;
                lastId = lastRow.ID;
            }
        }

        private async Task SyncDetailsByParentGuidsAsync(
            List<string> targetStoreCodes,
            List<string> parentGuids,
            SyncBatchResult invoiceResult,
            SyncBatchResult detailResult,
            HashSet<string> processedDetailGuids
        )
        {
            if (parentGuids.Count == 0)
                return;

            var lastId = 0;

            while (true)
            {
                var query = BuildHqDetailByParentQuery(targetStoreCodes, parentGuids)
                    .Where(x => x.ID > lastId);

                // 父单范围被固定在小块 GUID 内，避免生成无界 IN 条件。
                var hqBatch = await query
                    .OrderBy(x => x.ID)
                    .Take(PageSize)
                    .ToListAsync();
                if (hqBatch.Count == 0)
                    break;

                await SyncDetailBatchAsync(hqBatch, invoiceResult, detailResult, processedDetailGuids);

                var lastRow = hqBatch[^1];
                lastId = lastRow.ID;
            }
        }

        private async Task SyncDetailBatchAsync(
            List<RED_进货单详情表Store> hqBatch,
            SyncBatchResult invoiceResult,
            SyncBatchResult detailResult,
            HashSet<string> processedDetailGuids
        )
        {
            var newHqRows = hqBatch
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.HGUID)
                    && processedDetailGuids.Add(x.HGUID)
                )
                .ToList();
            if (newHqRows.Count == 0)
                return;

            var parentGuids = newHqRows
                .Select(x => x.H主表GUID)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct()
                .ToList();
            await BackfillMissingParentsAsync(parentGuids, invoiceResult);

            var localBatch = _mapper.Map<List<StoreLocalSupplierInvoiceDetails>>(newHqRows)
                .Where(x => !string.IsNullOrWhiteSpace(x.DetailGUID))
                .ToList();
            await UpsertDetailsAsync(localBatch, detailResult);
        }

        private ISugarQueryable<RED_进货单主表Store> BuildHqInvoiceQuery(
            List<string> targetStoreCodes,
            DateTime startDate,
            DateTime? endDate
        )
        {
            var query = _hqContext.Db.Queryable<RED_进货单主表Store>()
                .Where(x => !string.IsNullOrEmpty(x.HGUID))
                .Where(x => x.FGC_LastModifyDate >= startDate)
                .Where(x => x.H分店代码 != null && targetStoreCodes.Contains(x.H分店代码));
            if (endDate.HasValue)
            {
                query = query.Where(x => x.FGC_LastModifyDate <= endDate.Value);
            }
            return query;
        }

        private ISugarQueryable<RED_进货单详情表Store> BuildHqModifiedDetailQuery(
            List<string> targetStoreCodes,
            DateTime startDate,
            DateTime? endDate
        )
        {
            var query = BuildBaseHqDetailQuery(targetStoreCodes)
                .Where(x => x.FGC_LastModifyDate >= startDate);
            if (endDate.HasValue)
            {
                query = query.Where(x => x.FGC_LastModifyDate <= endDate.Value);
            }
            return query;
        }

        private ISugarQueryable<RED_进货单详情表Store> BuildHqDetailByParentQuery(
            List<string> targetStoreCodes,
            List<string> parentGuids
        )
        {
            return BuildBaseHqDetailQuery(targetStoreCodes)
                .Where(x => x.H主表GUID != null && parentGuids.Contains(x.H主表GUID));
        }

        private ISugarQueryable<RED_进货单详情表Store> BuildBaseHqDetailQuery(
            List<string> targetStoreCodes
        )
        {
            return _hqContext.Db.Queryable<RED_进货单详情表Store>()
                .Where(x => !string.IsNullOrEmpty(x.HGUID))
                .Where(x => !string.IsNullOrEmpty(x.H主表GUID))
                .Where(x => x.H分店代码 != null && targetStoreCodes.Contains(x.H分店代码));
        }

        private async Task BackfillMissingParentsAsync(List<string> parentGuids, SyncBatchResult result)
        {
            if (parentGuids.Count == 0)
                return;

            var existing = await QueryExistingInvoiceGuidsAsync(parentGuids);
            var missing = parentGuids.Except(existing).ToList();
            if (missing.Count == 0)
                return;

            // 明细可能比主表更新晚，回补父单可以避免产生孤儿明细。
            var parents = await QueryHqParentsByGuidAsync(missing);
            var localParents = _mapper.Map<List<StoreLocalSupplierInvoice>>(parents)
                .Where(x => !string.IsNullOrWhiteSpace(x.InvoiceGUID))
                .ToList();
            await UpsertInvoicesAsync(localParents, result);
        }

        private static async Task<ISqlSugarClient?> TryAcquireDatabaseLockAsync(ISqlSugarClient db)
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return null;
            }

            var config = db.CurrentConnectionConfig;
            var lockClient = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = config.ConnectionString,
                DbType = config.DbType,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            });

            var lockResult = await lockClient.Ado.SqlQuerySingleAsync<int>(
                """
                DECLARE @Result INT;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 0;
                SELECT @Result;
                """,
                new SugarParameter("@Resource", DatabaseLockResource)
            );

            if (lockResult < 0)
            {
                lockClient.Dispose();
                throw new InvalidOperationException("已有分店进货单 HQ 同步任务正在执行，请稍后再试");
            }

            return lockClient;
        }

        private static async Task ReleaseDatabaseLockAsync(ISqlSugarClient db)
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return;
            }

            await db.Ado.ExecuteCommandAsync(
                """
                EXEC sys.sp_releaseapplock
                    @Resource = @Resource,
                    @LockOwner = N'Session';
                """,
                new SugarParameter("@Resource", DatabaseLockResource)
            );
        }

        private async Task<List<string>> QueryExistingInvoiceGuidsAsync(List<string> guids)
        {
            var result = new List<string>();
            foreach (var chunk in NormalizeGuids(guids).Chunk(ContainsChunkSize))
            {
                var chunkList = chunk.ToList();
                var rows = await _localContext.Db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => chunkList.Contains(x.InvoiceGUID))
                    .Select(x => x.InvoiceGUID)
                    .ToListAsync();
                result.AddRange(rows);
            }
            return result;
        }

        private async Task<List<RED_进货单主表Store>> QueryHqParentsByGuidAsync(List<string> guids)
        {
            var result = new List<RED_进货单主表Store>();
            foreach (var chunk in NormalizeGuids(guids).Chunk(ContainsChunkSize))
            {
                var chunkList = chunk.ToList();
                var rows = await _hqContext.Db.Queryable<RED_进货单主表Store>()
                    .Where(x => x.HGUID != null && chunkList.Contains(x.HGUID))
                    .ToListAsync();
                result.AddRange(rows);
            }
            return result;
        }

        private async Task<List<StoreLocalSupplierInvoiceDetails>> QueryExistingDetailsByGuidAsync(
            List<string> guids
        )
        {
            var result = new List<StoreLocalSupplierInvoiceDetails>();
            foreach (var chunk in NormalizeGuids(guids).Chunk(ContainsChunkSize))
            {
                var chunkList = chunk.ToList();
                var rows = await _localContext.Db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x => chunkList.Contains(x.DetailGUID))
                    .ToListAsync();
                result.AddRange(rows);
            }
            return result;
        }

        private static List<string> NormalizeGuids(IEnumerable<string?> guids)
        {
            return guids
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct()
                .ToList();
        }

        private async Task UpsertInvoicesAsync(List<StoreLocalSupplierInvoice> batch, SyncBatchResult result)
        {
            if (batch.Count == 0)
                return;

            try
            {
                var existing = await QueryExistingInvoiceGuidsAsync(batch.Select(x => x.InvoiceGUID).ToList());
                var existingSet = existing.ToHashSet();
                var toInsert = batch.Where(x => !existingSet.Contains(x.InvoiceGUID)).ToList();
                var toUpdate = batch.Where(x => existingSet.Contains(x.InvoiceGUID)).ToList();

                if (toInsert.Count > 0)
                {
                    await _localContext.Db.Insertable(toInsert).ExecuteCommandAsync();
                    result.Added += toInsert.Count;
                }
                if (toUpdate.Count > 0)
                {
                    await _localContext.Db.Updateable(toUpdate).ExecuteCommandAsync();
                    result.Updated += toUpdate.Count;
                }
                foreach (var invoice in batch)
                {
                    result.AffectedInvoiceGuids.Add(invoice.InvoiceGUID);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"主表批量写入失败: {ex.Message}");
                _logger.LogError(ex, "分店进货单主表批量写入失败");
            }
        }

        private async Task UpsertDetailsAsync(
            List<StoreLocalSupplierInvoiceDetails> batch,
            SyncBatchResult result
        )
        {
            if (batch.Count == 0)
                return;

            try
            {
                var existingDetails = await QueryExistingDetailsByGuidAsync(
                    batch.Select(x => x.DetailGUID).ToList()
                );
                var existingByGuid = existingDetails.ToDictionary(x => x.DetailGUID);
                var toInsert = new List<StoreLocalSupplierInvoiceDetails>();
                var toUpdate = new List<StoreLocalSupplierInvoiceDetails>();

                foreach (var detail in batch)
                {
                    if (!existingByGuid.TryGetValue(detail.DetailGUID, out var existing))
                    {
                        toInsert.Add(detail);
                        continue;
                    }

                    // 检测与执行状态属于本地业务处理结果，HQ 增量覆盖时必须保留。
                    detail.ExistingProductCount = existing.ExistingProductCount;
                    detail.BarcodeStatus = existing.BarcodeStatus;
                    detail.BarcodeMatchCount = existing.BarcodeMatchCount;
                    detail.ActivityType = existing.ActivityType;
                    detail.ProductImage = existing.ProductImage;
                    toUpdate.Add(detail);
                }

                if (toInsert.Count > 0)
                {
                    await _localContext.Db.Insertable(toInsert).ExecuteCommandAsync();
                    result.Added += toInsert.Count;
                }
                if (toUpdate.Count > 0)
                {
                    await _localContext.Db.Updateable(toUpdate).ExecuteCommandAsync();
                    result.Updated += toUpdate.Count;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"明细批量写入失败: {ex.Message}");
                _logger.LogError(ex, "分店进货单明细批量写入失败");
            }
        }

        private static ApiResponse<LocalSupplierInvoiceHqSyncResult> BuildFailedResponse(
            LocalSupplierInvoiceHqSyncResult payload,
            DateTime startedAt,
            string message,
            string code
        )
        {
            payload.Status = "Failed";
            payload.CompletedAt = DateTime.UtcNow;
            payload.DurationMs = (long)(payload.CompletedAt.Value - startedAt).TotalMilliseconds;
            payload.Errors.Add(message);
            return BuildFailedPayloadResponse(payload, message, code);
        }

        private static ApiResponse<LocalSupplierInvoiceHqSyncResult> BuildFailedPayloadResponse(
            LocalSupplierInvoiceHqSyncResult payload,
            string message,
            string code
        )
        {
            return new ApiResponse<LocalSupplierInvoiceHqSyncResult>
            {
                Success = false,
                Message = message,
                ErrorCode = code,
                Data = payload,
                Details = payload,
                Timestamp = DateTime.UtcNow,
            };
        }

        private sealed class SyncBatchResult
        {
            public int Added { get; set; }
            public int Updated { get; set; }
            public List<string> Errors { get; } = new();
            public HashSet<string> AffectedInvoiceGuids { get; } = new();
        }
    }
}
