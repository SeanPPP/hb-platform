using System.Diagnostics;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店订货 HQ 同步核心。全量负责全库当前态对齐，增量只处理日期窗口内变化和本地软删恢复。
    /// </summary>
    public class StoreOrderHqSyncService : IStoreOrderHqSyncService
    {
        private const int BatchSize = 1000;

        private readonly ISqlSugarClient _db;
        private readonly IMapper _mapper;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StoreOrderHqSyncService> _logger;
        private Func<ISqlSugarClient> _createHqConnection;

        public StoreOrderHqSyncService(
            SqlSugarContext context,
            IMapper mapper,
            IConfiguration configuration,
            ILogger<StoreOrderHqSyncService> logger
        )
        {
            _db = context.Db;
            _mapper = mapper;
            _configuration = configuration;
            _logger = logger;
            _createHqConnection = () => HqSqlSugarContext.CreateConcurrentConnection(_configuration);
        }

        public async Task<SyncMissingOrdersResultDto> SyncAsync(
            StoreOrderHqSyncMode mode,
            StoreOrderHqSyncRequestDto? request,
            string? jobId = null,
            CancellationToken cancellationToken = default
        )
        {
            return mode == StoreOrderHqSyncMode.Full
                ? await SyncFullAsync(request, jobId, cancellationToken)
                : await SyncIncrementalAsync(request, jobId, cancellationToken);
        }

        private async Task<SyncMissingOrdersResultDto> SyncFullAsync(
            StoreOrderHqSyncRequestDto? request,
            string? jobId,
            CancellationToken cancellationToken
        )
        {
            var stopwatch = Stopwatch.StartNew();
            var runId = string.IsNullOrWhiteSpace(jobId) ? Guid.NewGuid().ToString("N") : jobId!;
            var result = CreateResult(StoreOrderHqSyncMode.Full, runId);
            var stage = "开始";

            try
            {
                _logger.LogInformation("分店订货 HQ 全量同步开始: jobId={JobId}", runId);

                using var hqDb = _createHqConnection();
                stage = "读取 HQ 全量主表";
                var hqOrdersRaw = await hqDb.Queryable<CBP_RED_分店订货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .ToListAsync();
                cancellationToken.ThrowIfCancellationRequested();

                stage = "读取 HQ 全量明细";
                var hqDetailsRaw = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID) && SqlFunc.HasValue(x.主表GUID))
                    .ToListAsync();
                cancellationToken.ThrowIfCancellationRequested();

                result.HqOrderCount = hqOrdersRaw.Count;
                result.HqDetailCount = hqDetailsRaw.Count;
                _logger.LogInformation(
                    "分店订货 HQ 全量读取完成: jobId={JobId}, hqOrders={HqOrders}, hqDetails={HqDetails}",
                    runId,
                    result.HqOrderCount,
                    result.HqDetailCount
                );

                stage = "校验 staging 数据";
                var validationErrors = ValidateHqSnapshot(hqOrdersRaw, hqDetailsRaw);
                if (validationErrors.Count > 0)
                {
                    result.Success = false;
                    result.Errors = validationErrors;
                    result.Message = $"HQ 全量 staging 校验失败：{validationErrors[0]}";
                    _logger.LogWarning(
                        "分店订货 HQ 全量 staging 校验失败: jobId={JobId}, errors={Errors}",
                        runId,
                        string.Join(" | ", validationErrors.Take(5))
                    );
                    return CompleteResult(result, stopwatch);
                }

                var hqOrders = DeduplicateOrders(hqOrdersRaw);
                var hqDetails = DeduplicateDetails(hqDetailsRaw);
                var shadowTables = new StoreOrderShadowTables();

                try
                {
                    stage = "写入 HQ shadow 表";
                    shadowTables = await CreateAndFillShadowTablesAsync(
                        hqOrders,
                        hqDetails,
                        runId,
                        cancellationToken
                    );
                    result.ShadowRowCount = shadowTables.OrderCount + shadowTables.DetailCount;

                    stage = "正式表 upsert 与软删";
                    await ApplySnapshotAsync(
                        hqOrders,
                        hqDetails,
                        storeCodes: new List<string>(),
                        softDeleteMissingOrders: true,
                        softDeleteMissingDetails: true,
                        result,
                        cancellationToken
                    );
                }
                finally
                {
                    await DropShadowTablesQuietlyAsync(shadowTables, runId);
                }

                result.Success = true;
                result.Message = BuildResultMessage("全量同步完成", result);
                _logger.LogInformation(
                    "分店订货 HQ 全量同步完成: jobId={JobId}, ordersNew={OrdersNew}, ordersUpdated={OrdersUpdated}, ordersDeleted={OrdersDeleted}, detailsNew={DetailsNew}, detailsUpdated={DetailsUpdated}, detailsDeleted={DetailsDeleted}, elapsedMs={ElapsedMs}",
                    runId,
                    result.OrdersSynced,
                    result.OrdersUpdated,
                    result.OrdersSoftDeleted,
                    result.DetailsSynced,
                    result.DetailsUpdated,
                    result.DetailsSoftDeleted,
                    stopwatch.ElapsedMilliseconds
                );
                return CompleteResult(result, stopwatch);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"HQ 全量同步失败：{ex.Message}";
                result.Errors.Add($"{stage}: {ex.Message}");
                _logger.LogError(ex, "分店订货 HQ 全量同步失败: jobId={JobId}, stage={Stage}", runId, stage);
                return CompleteResult(result, stopwatch);
            }
        }

        private async Task<SyncMissingOrdersResultDto> SyncIncrementalAsync(
            StoreOrderHqSyncRequestDto? request,
            string? jobId,
            CancellationToken cancellationToken
        )
        {
            var stopwatch = Stopwatch.StartNew();
            var runId = string.IsNullOrWhiteSpace(jobId) ? Guid.NewGuid().ToString("N") : jobId!;
            var result = CreateResult(StoreOrderHqSyncMode.Incremental, runId);
            var storeCodes = NormalizeStoreCodes(request);
            var hasStoreFilter = storeCodes.Count > 0;
            var endDate = request?.EndDate ?? DateTime.UtcNow;
            var startDate = request?.StartDate ?? endDate.AddDays(-30);
            var stage = "开始";
            var stageStopwatch = Stopwatch.StartNew();

            void LogStage(string completedStage)
            {
                _logger.LogInformation(
                    "分店订货 HQ 增量阶段完成: jobId={JobId}, stage={Stage}, elapsedMs={ElapsedMs}",
                    runId,
                    completedStage,
                    stageStopwatch.ElapsedMilliseconds
                );
                stageStopwatch.Restart();
            }

            if (startDate > endDate)
            {
                (startDate, endDate) = (endDate, startDate);
            }

            try
            {
                _logger.LogInformation(
                    "分店订货 HQ 增量同步开始: jobId={JobId}, scope={Scope}, start={StartDate}, end={EndDate}",
                    runId,
                    hasStoreFilter ? string.Join(",", storeCodes) : "全部",
                    startDate,
                    endDate
                );

                using var hqDb = _createHqConnection();

                stage = "读取 HQ 日期命中主表";
                var changedOrders = await hqDb.Queryable<CBP_RED_分店订货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .WhereIF(hasStoreFilter, x => storeCodes.Contains(x.分店代码!))
                    .Where(x =>
                        (x.FGC_CreateDate.HasValue && x.FGC_CreateDate >= startDate && x.FGC_CreateDate <= endDate)
                        || (x.FGC_LastModifyDate.HasValue && x.FGC_LastModifyDate >= startDate && x.FGC_LastModifyDate <= endDate)
                    )
                    .ToListAsync();
                cancellationToken.ThrowIfCancellationRequested();
                LogStage(stage);

                stage = "读取 HQ 日期命中明细";
                var changedDetails = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID) && SqlFunc.HasValue(x.主表GUID))
                    .WhereIF(hasStoreFilter, x => storeCodes.Contains(x.分店代码!))
                    .Where(x =>
                        (x.FGC_CreateDate.HasValue && x.FGC_CreateDate >= startDate && x.FGC_CreateDate <= endDate)
                        || (x.FGC_LastModifyDate.HasValue && x.FGC_LastModifyDate >= startDate && x.FGC_LastModifyDate <= endDate)
                    )
                    .ToListAsync();
                cancellationToken.ThrowIfCancellationRequested();
                LogStage(stage);

                var targetOrderGuidSet = changedOrders
                    .Select(x => x.HGUID)
                    .Concat(changedDetails.Select(x => x.主表GUID))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                stage = "补拉目标主表";
                var targetOrders = await QueryHqOrdersByGuidAsync(hqDb, targetOrderGuidSet.ToList());
                targetOrders = DeduplicateOrders(targetOrders)
                    .Where(x => !hasStoreFilter || storeCodes.Contains(x.分店代码 ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                LogStage(stage);

                var targetOrderGuids = targetOrders
                    .Select(x => x.HGUID!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                stage = "读取目标完整明细";
                var targetDetails = await QueryHqDetailsByOrderGuidAsync(hqDb, targetOrderGuids.ToList());
                targetDetails = DeduplicateDetails(targetDetails)
                    .Where(x => !hasStoreFilter || storeCodes.Contains(x.分店代码 ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                LogStage(stage);

                result.HqOrderCount = targetOrders.Count;
                result.HqDetailCount = targetDetails.Count;

                _logger.LogInformation(
                    "分店订货 HQ 增量目标计算完成: jobId={JobId}, changedOrders={ChangedOrders}, changedDetails={ChangedDetails}, targetOrders={TargetOrders}, targetDetails={TargetDetails}, physicalDeleteCheck=Skipped",
                    runId,
                    changedOrders.Count,
                    changedDetails.Count,
                    targetOrders.Count,
                    targetDetails.Count
                );

                stage = "正式表增量写入与本地软删恢复";
                await ApplySnapshotAsync(
                    targetOrders,
                    targetDetails,
                    storeCodes,
                    softDeleteMissingOrders: false,
                    softDeleteMissingDetails: false,
                    result,
                    cancellationToken
                );
                LogStage(stage);

                result.Success = true;
                result.Message = BuildIncrementalResultMessage(result);
                _logger.LogInformation(
                    "分店订货 HQ 增量同步完成: jobId={JobId}, ordersNewOrRestored={OrdersNew}, ordersUpdated={OrdersUpdated}, detailsNewOrRestored={DetailsNew}, detailsUpdated={DetailsUpdated}, physicalDeleteCheck=Skipped, elapsedMs={ElapsedMs}",
                    runId,
                    result.OrdersSynced,
                    result.OrdersUpdated,
                    result.DetailsSynced,
                    result.DetailsUpdated,
                    stopwatch.ElapsedMilliseconds
                );
                return CompleteResult(result, stopwatch);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"HQ 增量同步失败：{ex.Message}";
                result.Errors.Add($"{stage}: {ex.Message}");
                _logger.LogError(ex, "分店订货 HQ 增量同步失败: jobId={JobId}, stage={Stage}", runId, stage);
                return CompleteResult(result, stopwatch);
            }
        }

        private async Task ApplySnapshotAsync(
            List<CBP_RED_分店订货单主表Store> hqOrders,
            List<CBP_RED_分店订单详情表Store> hqDetails,
            List<string> storeCodes,
            bool softDeleteMissingOrders,
            bool softDeleteMissingDetails,
            SyncMissingOrdersResultDto result,
            CancellationToken cancellationToken,
            HashSet<string>? currentHqOrderGuids = null,
            HashSet<string>? currentHqDetailGuids = null
        )
        {
            var hasStoreFilter = storeCodes.Count > 0;
            if (softDeleteMissingOrders)
            {
                currentHqOrderGuids ??= hqOrders
                    .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                    .Select(x => x.HGUID!.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            if (softDeleteMissingDetails)
            {
                currentHqDetailGuids ??= hqDetails
                    .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                    .Select(x => x.HGUID!.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var targetOrderGuids = hqOrders
                .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                .Select(x => x.HGUID!.Trim())
                .ToList();
            var targetDetailGuids = hqDetails
                .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                .Select(x => x.HGUID!.Trim())
                .ToList();

            var localTargetOrders = await QueryLocalOrdersByGuidAsync(targetOrderGuids);
            var localTargetDetails = await QueryLocalDetailsByGuidAsync(targetDetailGuids);
            var localActiveOrdersInScope = softDeleteMissingOrders
                ? await _db.Queryable<WareHouseOrder>()
                    .Where(x => !x.IsDeleted)
                    .WhereIF(hasStoreFilter, x => storeCodes.Contains(x.StoreCode!))
                    .ToListAsync()
                : new List<WareHouseOrder>();
            var localActiveDetailsInScope = softDeleteMissingDetails
                ? await _db.Queryable<WareHouseOrderDetails>()
                    .Where(x => !x.IsDeleted)
                    .WhereIF(hasStoreFilter, x => storeCodes.Contains(x.StoreCode!))
                    .ToListAsync()
                : new List<WareHouseOrderDetails>();

            var localOrderMap = localTargetOrders
                .GroupBy(x => x.OrderGUID, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var localDetailMap = localTargetDetails
                .GroupBy(x => x.DetailGUID, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var ordersToInsert = new List<WareHouseOrder>();
            var ordersToUpdate = new List<WareHouseOrder>();
            foreach (var hqOrder in hqOrders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mapped = MapHqOrder(hqOrder);
                if (!localOrderMap.TryGetValue(mapped.OrderGUID, out var local))
                {
                    ordersToInsert.Add(mapped);
                }
                else if (local.IsDeleted || IsOrderChanged(mapped, local))
                {
                    ordersToUpdate.Add(mapped);
                }
            }

            var detailsToInsert = new List<WareHouseOrderDetails>();
            var detailsToUpdate = new List<WareHouseOrderDetails>();
            foreach (var hqDetail in hqDetails)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mapped = MapHqDetail(hqDetail);
                if (!localDetailMap.TryGetValue(mapped.DetailGUID, out var local))
                {
                    detailsToInsert.Add(mapped);
                }
                else if (local.IsDeleted || IsDetailChanged(mapped, local))
                {
                    detailsToUpdate.Add(mapped);
                }
            }

            var ordersToSoftDelete = softDeleteMissingOrders
                ? localActiveOrdersInScope
                    .Where(x => !string.IsNullOrWhiteSpace(x.OrderGUID) && currentHqOrderGuids is not null && !currentHqOrderGuids.Contains(x.OrderGUID))
                    .ToList()
                : new List<WareHouseOrder>();
            var softDeletedOrderGuidSet = ordersToSoftDelete
                .Select(x => x.OrderGUID)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var detailsToSoftDelete = softDeleteMissingDetails
                ? localActiveDetailsInScope
                    .Where(x =>
                        (
                            !string.IsNullOrWhiteSpace(x.DetailGUID)
                            && currentHqDetailGuids is not null
                            && !currentHqDetailGuids.Contains(x.DetailGUID)
                        )
                        || (!string.IsNullOrWhiteSpace(x.OrderGUID) && softDeletedOrderGuidSet.Contains(x.OrderGUID))
                    )
                    .ToList()
                : new List<WareHouseOrderDetails>();

            var transactionResult = await _db.Ado.UseTranAsync(async () =>
            {
                await ExecuteInsertInBatchesAsync(ordersToInsert, BatchSize);
                await ExecuteUpdateInBatchesAsync(ordersToUpdate, BatchSize);
                await ExecuteInsertInBatchesAsync(detailsToInsert, BatchSize);
                await ExecuteUpdateInBatchesAsync(detailsToUpdate, BatchSize);

                var now = DateTime.Now;
                foreach (var order in ordersToSoftDelete)
                {
                    order.IsDeleted = true;
                    order.UpdatedAt = now;
                    order.UpdatedBy = "HQ同步-软删";
                }

                foreach (var detail in detailsToSoftDelete)
                {
                    detail.IsDeleted = true;
                    detail.UpdatedAt = now;
                    detail.UpdatedBy = "HQ同步-软删";
                }

                await ExecuteUpdateInBatchesAsync(ordersToSoftDelete, BatchSize);
                await ExecuteUpdateInBatchesAsync(detailsToSoftDelete, BatchSize);
            });

            if (!transactionResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    transactionResult.ErrorException?.Message ?? "分店订货 HQ 同步事务失败",
                    transactionResult.ErrorException
                );
            }

            result.OrdersSynced = ordersToInsert.Count + ordersToUpdate.Count(x => localOrderMap[x.OrderGUID].IsDeleted);
            result.OrdersUpdated = ordersToUpdate.Count(x => !localOrderMap[x.OrderGUID].IsDeleted);
            result.DetailsSynced = detailsToInsert.Count + detailsToUpdate.Count(x => localDetailMap[x.DetailGUID].IsDeleted);
            result.DetailsUpdated = detailsToUpdate.Count(x => !localDetailMap[x.DetailGUID].IsDeleted);
            result.OrdersSoftDeleted = ordersToSoftDelete.Count;
            result.DetailsSoftDeleted = detailsToSoftDelete.Count;
        }

        private async Task<StoreOrderShadowTables> CreateAndFillShadowTablesAsync(
            List<CBP_RED_分店订货单主表Store> hqOrders,
            List<CBP_RED_分店订单详情表Store> hqDetails,
            string runId,
            CancellationToken cancellationToken
        )
        {
            var safeRunId = new string(runId.Where(char.IsLetterOrDigit).ToArray());
            if (safeRunId.Length > 24)
            {
                safeRunId = safeRunId[..24];
            }

            var tables = new StoreOrderShadowTables
            {
                OrderTableName = $"WareHouseOrder_HqShadow_{safeRunId}",
                DetailTableName = $"WareHouseOrderDetails_HqShadow_{safeRunId}",
            };

            await CreateShadowTableAsync(tables.OrderTableName, "WareHouseOrder");
            await CreateShadowTableAsync(tables.DetailTableName, "WareHouseOrderDetails");

            var shadowOrders = hqOrders.Select(MapHqOrder).ToList();
            var shadowDetails = hqDetails.Select(MapHqDetail).ToList();

            foreach (var batch in shadowOrders.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _db.Insertable(batch.ToList()).AS(tables.OrderTableName).ExecuteCommandAsync();
            }

            foreach (var batch in shadowDetails.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _db.Insertable(batch.ToList()).AS(tables.DetailTableName).ExecuteCommandAsync();
            }

            tables.OrderCount = await CountTableRowsAsync(tables.OrderTableName);
            tables.DetailCount = await CountTableRowsAsync(tables.DetailTableName);

            if (tables.OrderCount != hqOrders.Count || tables.DetailCount != hqDetails.Count)
            {
                throw new InvalidOperationException(
                    $"shadow 行数校验失败：主表 {tables.OrderCount}/{hqOrders.Count}，明细 {tables.DetailCount}/{hqDetails.Count}"
                );
            }

            _logger.LogInformation(
                "分店订货 HQ shadow 写入完成: jobId={JobId}, orderTable={OrderTable}, detailTable={DetailTable}, orders={Orders}, details={Details}",
                runId,
                tables.OrderTableName,
                tables.DetailTableName,
                tables.OrderCount,
                tables.DetailCount
            );
            return tables;
        }

        private async Task CreateShadowTableAsync(string shadowTableName, string sourceTableName)
        {
            await DropTableIfExistsAsync(shadowTableName);

            if (_db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            {
                await _db.Ado.ExecuteCommandAsync(
                    $"CREATE TABLE \"{shadowTableName}\" AS SELECT * FROM \"{sourceTableName}\" WHERE 1 = 0"
                );
                return;
            }

            await _db.Ado.ExecuteCommandAsync(
                $"SELECT TOP 0 * INTO [{shadowTableName}] FROM [{sourceTableName}]"
            );
        }

        private async Task<int> CountTableRowsAsync(string tableName)
        {
            if (_db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            {
                return await _db.Ado.GetIntAsync($"SELECT COUNT(1) FROM \"{tableName}\"");
            }

            return await _db.Ado.GetIntAsync($"SELECT COUNT(1) FROM [{tableName}]");
        }

        private async Task DropShadowTablesQuietlyAsync(StoreOrderShadowTables tables, string runId)
        {
            foreach (var tableName in new[] { tables.DetailTableName, tables.OrderTableName })
            {
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    continue;
                }

                try
                {
                    await DropTableIfExistsAsync(tableName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "分店订货 HQ shadow 清理失败: jobId={JobId}, table={TableName}",
                        runId,
                        tableName
                    );
                }
            }
        }

        private async Task DropTableIfExistsAsync(string tableName)
        {
            if (_db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            {
                await _db.Ado.ExecuteCommandAsync($"DROP TABLE IF EXISTS \"{tableName}\"");
                return;
            }

            await _db.Ado.ExecuteCommandAsync(
                $"IF OBJECT_ID(N'{tableName}', N'U') IS NOT NULL DROP TABLE [{tableName}]"
            );
        }

        private static List<string> ValidateHqSnapshot(
            List<CBP_RED_分店订货单主表Store> orders,
            List<CBP_RED_分店订单详情表Store> details
        )
        {
            var errors = new List<string>();
            var duplicateOrders = orders
                .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                .GroupBy(x => x.HGUID!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .Take(5)
                .ToList();
            if (duplicateOrders.Count > 0)
            {
                errors.Add($"HQ 主表 HGUID 重复：{string.Join(",", duplicateOrders)}");
            }

            var duplicateDetails = details
                .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                .GroupBy(x => x.HGUID!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .Take(5)
                .ToList();
            if (duplicateDetails.Count > 0)
            {
                errors.Add($"HQ 明细 HGUID 重复：{string.Join(",", duplicateDetails)}");
            }

            var orderGuids = orders
                .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                .Select(x => x.HGUID!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orphanDetails = details
                .Where(x => !string.IsNullOrWhiteSpace(x.主表GUID) && !orderGuids.Contains(x.主表GUID!.Trim()))
                .Select(x => x.HGUID ?? "(空明细GUID)")
                .Take(5)
                .ToList();
            if (orphanDetails.Count > 0)
            {
                errors.Add($"HQ 明细主表不存在：{string.Join(",", orphanDetails)}");
            }

            return errors;
        }

        private async Task<List<CBP_RED_分店订货单主表Store>> QueryHqOrdersByGuidAsync(
            ISqlSugarClient hqDb,
            List<string> orderGuids
        )
        {
            var result = new List<CBP_RED_分店订货单主表Store>();
            foreach (var batch in orderGuids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            {
                var batchGuids = batch.ToList();
                result.AddRange(
                    await hqDb.Queryable<CBP_RED_分店订货单主表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID))
                        .Where(x => batchGuids.Contains(x.HGUID!))
                        .ToListAsync()
                );
            }

            return result;
        }

        private async Task<List<CBP_RED_分店订单详情表Store>> QueryHqDetailsByOrderGuidAsync(
            ISqlSugarClient hqDb,
            List<string> orderGuids
        )
        {
            var result = new List<CBP_RED_分店订单详情表Store>();
            foreach (var batch in orderGuids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            {
                var batchGuids = batch.ToList();
                result.AddRange(
                    await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID) && SqlFunc.HasValue(x.主表GUID))
                        .Where(x => batchGuids.Contains(x.主表GUID!))
                        .ToListAsync()
                );
            }

            return result;
        }

        private async Task<List<WareHouseOrder>> QueryLocalOrdersByGuidAsync(List<string> orderGuids)
        {
            var result = new List<WareHouseOrder>();
            foreach (var batch in orderGuids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            {
                var batchGuids = batch.ToList();
                result.AddRange(
                    await _db.Queryable<WareHouseOrder>()
                        .Where(x => batchGuids.Contains(x.OrderGUID))
                        .ToListAsync()
                );
            }

            return result;
        }

        private async Task<List<WareHouseOrderDetails>> QueryLocalDetailsByGuidAsync(List<string> detailGuids)
        {
            var result = new List<WareHouseOrderDetails>();
            foreach (var batch in detailGuids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
            {
                var batchGuids = batch.ToList();
                result.AddRange(
                    await _db.Queryable<WareHouseOrderDetails>()
                        .Where(x => batchGuids.Contains(x.DetailGUID))
                        .ToListAsync()
                );
            }

            return result;
        }

        private WareHouseOrder MapHqOrder(CBP_RED_分店订货单主表Store hqOrder)
        {
            var order = _mapper.Map<WareHouseOrder>(hqOrder);
            order.IsDeleted = false;
            order.CreatedAt = hqOrder.FGC_CreateDate ?? DateTime.Now;
            order.UpdatedAt = hqOrder.FGC_LastModifyDate ?? DateTime.Now;
            order.CreatedBy = hqOrder.FGC_Creator ?? "HQ同步";
            order.UpdatedBy = hqOrder.FGC_LastModifier ?? "HQ同步";
            return order;
        }

        private WareHouseOrderDetails MapHqDetail(CBP_RED_分店订单详情表Store hqDetail)
        {
            var detail = _mapper.Map<WareHouseOrderDetails>(hqDetail);
            detail.IsDeleted = false;
            detail.CreatedAt = hqDetail.FGC_CreateDate ?? DateTime.Now;
            detail.UpdatedAt = hqDetail.FGC_LastModifyDate ?? DateTime.Now;
            detail.CreatedBy = hqDetail.FGC_Creator ?? "HQ同步";
            detail.UpdatedBy = hqDetail.FGC_LastModifier ?? "HQ同步";
            return detail;
        }

        private static bool IsOrderChanged(WareHouseOrder source, WareHouseOrder local)
        {
            return !SameText(source.StoreCode, local.StoreCode)
                || !SameText(source.OrderNo, local.OrderNo)
                || source.OrderDate != local.OrderDate
                || source.OutboundDate != local.OutboundDate
                || source.ShippingFee != local.ShippingFee
                || source.ImportTotalAmount != local.ImportTotalAmount
                || source.OEMTotalAmount != local.OEMTotalAmount
                || !SameText(source.Remarks, local.Remarks)
                || source.FlowStatus != local.FlowStatus
                || source.InboundStatus != local.InboundStatus
                || (source.UpdatedAt.HasValue && (!local.UpdatedAt.HasValue || source.UpdatedAt > local.UpdatedAt));
        }

        private static bool IsDetailChanged(WareHouseOrderDetails source, WareHouseOrderDetails local)
        {
            return !SameText(source.OrderGUID, local.OrderGUID)
                || !SameText(source.StoreCode, local.StoreCode)
                || !SameText(source.StoreProductCode, local.StoreProductCode)
                || !SameText(source.ProductCode, local.ProductCode)
                || source.Quantity != local.Quantity
                || source.AllocQuantity != local.AllocQuantity
                || source.LastCost != local.LastCost
                || source.ImportPrice != local.ImportPrice
                || source.ImportAmount != local.ImportAmount
                || source.OEMPrice != local.OEMPrice
                || source.OEMAmount != local.OEMAmount
                || (source.UpdatedAt.HasValue && (!local.UpdatedAt.HasValue || source.UpdatedAt > local.UpdatedAt));
        }

        private async Task ExecuteInsertInBatchesAsync<T>(List<T> entities, int size)
            where T : class, new()
        {
            foreach (var batch in entities.Chunk(size))
            {
                await _db.Insertable(batch.ToList()).ExecuteCommandAsync();
            }
        }

        private async Task ExecuteUpdateInBatchesAsync<T>(List<T> entities, int size)
            where T : class, new()
        {
            foreach (var batch in entities.Chunk(size))
            {
                await _db.Updateable(batch.ToList()).ExecuteCommandAsync();
            }
        }

        private static List<CBP_RED_分店订货单主表Store> DeduplicateOrders(
            List<CBP_RED_分店订货单主表Store> orders
        )
        {
            return orders
                .Where(x => !string.IsNullOrWhiteSpace(x.HGUID))
                .GroupBy(x => x.HGUID!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static List<CBP_RED_分店订单详情表Store> DeduplicateDetails(
            List<CBP_RED_分店订单详情表Store> details
        )
        {
            return details
                .Where(x => !string.IsNullOrWhiteSpace(x.HGUID) && !string.IsNullOrWhiteSpace(x.主表GUID))
                .GroupBy(x => x.HGUID!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static List<string> NormalizeStoreCodes(StoreOrderHqSyncRequestDto? request)
        {
            var storeCodes = (request?.StoreCodes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (storeCodes.Count == 0 && !string.IsNullOrWhiteSpace(request?.StoreCode))
            {
                storeCodes.Add(request.StoreCode.Trim());
            }

            return storeCodes;
        }

        private static SyncMissingOrdersResultDto CreateResult(StoreOrderHqSyncMode mode, string runId)
        {
            return new StoreOrderHqSyncResultDto
            {
                Success = true,
                Mode = mode.ToString(),
                RunId = runId,
            };
        }

        private static SyncMissingOrdersResultDto CompleteResult(
            SyncMissingOrdersResultDto result,
            Stopwatch stopwatch
        )
        {
            stopwatch.Stop();
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        private static string BuildResultMessage(string prefix, SyncMissingOrdersResultDto result)
        {
            return $"{prefix}：新增订单 {result.OrdersSynced} 条、更新订单 {result.OrdersUpdated} 条、软删订单 {result.OrdersSoftDeleted} 条；"
                + $"新增明细 {result.DetailsSynced} 条、更新明细 {result.DetailsUpdated} 条、软删明细 {result.DetailsSoftDeleted} 条";
        }

        private static string BuildIncrementalResultMessage(SyncMissingOrdersResultDto result)
        {
            return $"增量同步完成：新增/恢复订单 {result.OrdersSynced} 条、更新订单 {result.OrdersUpdated} 条；"
                + $"新增/恢复明细 {result.DetailsSynced} 条、更新明细 {result.DetailsUpdated} 条";
        }

        private static bool SameText(string? left, string? right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private sealed class StoreOrderShadowTables
        {
            public string OrderTableName { get; set; } = string.Empty;

            public string DetailTableName { get; set; } = string.Empty;

            public int OrderCount { get; set; }

            public int DetailCount { get; set; }
        }
    }
}
