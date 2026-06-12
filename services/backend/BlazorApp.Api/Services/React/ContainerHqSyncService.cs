using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Options;
using SqlSugar;
using TaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 货柜 HQ 增量同步统一实现。
    /// 采用“先找受影响货柜，再拉完整快照，再按批事务写入”的方式，确保主表和明细一致。
    /// </summary>
    public class ContainerHqSyncService : IContainerHqSyncService
    {
        private const string TaskIncremental = "SyncContainersCoreIncremental";
        private const string SyncActor = "ReactSync";
        private static readonly SemaphoreSlim SyncLock = new(1, 1);

        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly IMapper _mapper;
        private readonly ILogger<ContainerHqSyncService> _logger;
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly ContainerHqSyncOptions _options;

        public ContainerHqSyncService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            IMapper mapper,
            ILogger<ContainerHqSyncService> logger,
            ScheduledTaskLogService taskLogService,
            IOptions<ContainerHqSyncOptions>? options = null
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _mapper = mapper;
            _logger = logger;
            _taskLogService = taskLogService;
            _options = options?.Value ?? new ContainerHqSyncOptions();
        }

        public async Task<SyncResult> SyncIncrementalAsync(DateTime? startDate = null)
        {
            if (!await SyncLock.WaitAsync(TimeSpan.FromSeconds(_options.LockWaitSeconds)))
            {
                return BuildFailedResult(
                    "已有货柜 HQ 同步任务正在执行，请稍后再试",
                    ContainerHqSyncErrorCodes.Conflict
                );
            }

            var result = new SyncResult { StartTime = DateTime.UtcNow };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskIncremental,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            var db = _localContext.Db;
            var originalTimeout = db.Ado.CommandTimeOut;
            db.Ado.CommandTimeOut = _options.CommandTimeoutSeconds;

            try
            {
                _hqContext.CheckConnection();

                var effectiveStart = await ResolveEffectiveStartAsync(startDate);
                var affectedContainerCodes = await GetAffectedContainerCodesAsync(effectiveStart);

                if (affectedContainerCodes.Count == 0)
                {
                    result.IsSuccess = true;
                    result.Message = "没有需要同步的货柜变更";
                    result.Details = $"水位起点: {effectiveStart:O}；受影响货柜: 0";
                    await FinishTaskAsync(taskLog.Id, result);
                    return result;
                }

                var hqContainers = await LoadHqContainersSnapshotAsync(affectedContainerCodes);
                var hqDetails = await LoadHqDetailsSnapshotAsync(affectedContainerCodes);

                var missingContainers = affectedContainerCodes
                    .Where(code => !hqContainers.ContainsKey(code))
                    .ToList();
                if (missingContainers.Count > 0)
                {
                    throw new ContainerSyncInvalidSourceDataException(
                        $"HQ 快照不完整：{missingContainers.Count} 个受影响货柜未找到主表，示例: {string.Join(", ", missingContainers.Take(10))}"
                    );
                }

                var invalidDetails = hqDetails
                    .Where(x => string.IsNullOrWhiteSpace(x.HGUID))
                    .Select(x => $"ID={x.ID}, 主表GUID={x.主表GUID}")
                    .Take(10)
                    .ToList();
                if (invalidDetails.Count > 0)
                {
                    result.ErrorCount = invalidDetails.Count;
                    throw new ContainerSyncInvalidSourceDataException(
                        $"HQ 数据质量失败：货柜明细缺少 HGUID，禁止使用 DETAIL_{{ID}} 回退。示例: {string.Join(" | ", invalidDetails)}"
                    );
                }

                var stats = new SyncStats();
                foreach (var containerChunk in affectedContainerCodes.Chunk(_options.LocalContainerBatchSize))
                {
                    var batchCodes = containerChunk.ToList();
                    var batchSet = batchCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var batchContainers = batchCodes.Select(code => hqContainers[code]).ToList();
                    var batchDetails = hqDetails
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.主表GUID)
                            && batchSet.Contains(x.主表GUID!)
                        )
                        .ToList();

                    await UpsertBatchAsync(db, batchContainers, batchDetails, stats);
                }

                result.AddedCount = stats.AddedCount;
                result.UpdatedCount = stats.UpdatedCount;
                result.DeletedCount = stats.SoftDeletedDetailCount;
                result.TotalCount =
                    stats.AddedCount + stats.UpdatedCount + stats.SoftDeletedDetailCount;
                result.IsSuccess = true;
                result.Message =
                    $"货柜 HQ 增量同步完成，受影响货柜 {affectedContainerCodes.Count:N0} 个，新增 {result.AddedCount:N0} 条，更新 {result.UpdatedCount:N0} 条，软删明细 {result.DeletedCount:N0} 条";
                result.Details =
                    $"水位起点: {effectiveStart:O}；HQ 主表快照: {hqContainers.Count:N0}；HQ 明细快照: {hqDetails.Count:N0}";

                await FinishTaskAsync(taskLog.Id, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "货柜 HQ 增量同步失败");
                result.IsSuccess = false;
                result.ErrorCount = Math.Max(result.ErrorCount, 1);
                result.Message = $"货柜 HQ 增量同步失败: {ex.Message}";
                result.ErrorCode = ex is ContainerSyncInvalidSourceDataException
                    ? ContainerHqSyncErrorCodes.InvalidSourceData
                    : ContainerHqSyncErrorCodes.InternalError;
                result.Details ??= ex.ToString();
                await FinishTaskAsync(taskLog.Id, result);
                return result;
            }
            finally
            {
                db.Ado.CommandTimeOut = originalTimeout;
                SyncLock.Release();
            }
        }

        /// <summary>
        /// 兼容旧门面契约，内部统一转发到新的核心增量入口。
        /// </summary>
        public Task<SyncResult> SyncContainersWithDetailsFromHqAsync(DateTime? startDate = null)
        {
            return SyncIncrementalAsync(startDate);
        }

        /// <summary>
        /// 解析本次同步水位。
        /// </summary>
        private async Task<DateTime> ResolveEffectiveStartAsync(DateTime? requestedStartDate)
        {
            if (requestedStartDate.HasValue)
            {
                // 前端日期筛选通常是不带时区的业务日期，不能按服务器时区转换成前一天。
                return requestedStartDate.Value.Kind == DateTimeKind.Local
                    ? requestedStartDate.Value.ToUniversalTime()
                    : requestedStartDate.Value;
            }

            var recentTasks = await _taskLogService.GetRecentTasksAsync(20, TaskIncremental);
            var lastSuccessTask = recentTasks.FirstOrDefault(x => x.Status == TaskStatus.Success);
            if (lastSuccessTask?.StartedAt != null)
            {
                return lastSuccessTask.StartedAt.AddDays(-_options.ReplayOverlapDays);
            }

            return DateTime.UtcNow.AddDays(-_options.DefaultIncrementalDays);
        }

        /// <summary>
        /// 找出本次受影响的货柜编码集合。
        /// </summary>
        private async Task<List<string>> GetAffectedContainerCodesAsync(DateTime effectiveStart)
        {
            var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastMainId = 0;

            while (true)
            {
                var mainBatch = await _hqContext
                    .Db.Queryable<CPT_RED_货柜单主表Store>()
                    .Where(x => x.ID > lastMainId && !string.IsNullOrEmpty(x.HGUID))
                    .Where(x =>
                        (x.FGC_LastModifyDate != null && x.FGC_LastModifyDate >= effectiveStart)
                        || (
                            x.FGC_LastModifyDate == null
                            && x.FGC_CreateDate != null
                            && x.FGC_CreateDate >= effectiveStart
                        )
                        || (
                            x.FGC_LastModifyDate == null
                            && x.FGC_CreateDate == null
                            && x.装柜日期 != null
                            && x.装柜日期 >= effectiveStart
                        )
                    )
                    .OrderBy(x => x.ID)
                    .Take(_options.HqReadBatchSize)
                    .ToListAsync();

                if (mainBatch.Count == 0)
                {
                    break;
                }

                foreach (var item in mainBatch)
                {
                    if (!string.IsNullOrWhiteSpace(item.HGUID))
                    {
                        affected.Add(item.HGUID.Trim());
                    }
                }

                lastMainId = mainBatch[^1].ID;
            }

            var lastDetailId = 0;
            while (true)
            {
                var detailBatch = await _hqContext
                    .Db.Queryable<CPT_RED_货柜单详情表Store, CPT_RED_货柜单主表Store>(
                        (detail, master) => new JoinQueryInfos(
                            JoinType.Left,
                            detail.主表GUID == master.HGUID
                        )
                    )
                    .Where((detail, master) => detail.ID > lastDetailId)
                    .Where((detail, master) => !string.IsNullOrEmpty(detail.主表GUID))
                    .Where((detail, master) =>
                        (detail.FGC_LastModifyDate != null && detail.FGC_LastModifyDate >= effectiveStart)
                        || (
                            detail.FGC_LastModifyDate == null
                            && detail.FGC_CreateDate != null
                            && detail.FGC_CreateDate >= effectiveStart
                        )
                        || (
                            detail.FGC_LastModifyDate == null
                            && detail.FGC_CreateDate == null
                            && master.装柜日期 != null
                            && master.装柜日期 >= effectiveStart
                        )
                    )
                    .OrderBy((detail, master) => detail.ID)
                    .Take(_options.HqReadBatchSize)
                    .Select((detail, master) => new DetailParentProjection
                    {
                        DetailId = detail.ID,
                        ParentGuid = detail.主表GUID,
                    })
                    .ToListAsync();

                if (detailBatch.Count == 0)
                {
                    break;
                }

                foreach (var item in detailBatch)
                {
                    if (!string.IsNullOrWhiteSpace(item.ParentGuid))
                    {
                        affected.Add(item.ParentGuid.Trim());
                    }
                }

                lastDetailId = detailBatch[^1].DetailId;
            }

            return affected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 拉取受影响货柜的 HQ 主表完整快照。
        /// </summary>
        private async Task<Dictionary<string, CPT_RED_货柜单主表Store>> LoadHqContainersSnapshotAsync(
            List<string> containerCodes
        )
        {
            var snapshot = new Dictionary<string, CPT_RED_货柜单主表Store>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var chunk in containerCodes.Chunk(_options.LocalContainerBatchSize))
            {
                var batch = await _hqContext
                    .Db.Queryable<CPT_RED_货柜单主表Store>()
                    .Where(x => !string.IsNullOrEmpty(x.HGUID))
                    .Where(x => chunk.Contains(x.HGUID!))
                    .ToListAsync();

                foreach (var item in batch)
                {
                    if (!string.IsNullOrWhiteSpace(item.HGUID))
                    {
                        snapshot[item.HGUID.Trim()] = item;
                    }
                }
            }

            return snapshot;
        }

        /// <summary>
        /// 拉取受影响货柜的 HQ 明细完整快照。
        /// </summary>
        private async Task<List<CPT_RED_货柜单详情表Store>> LoadHqDetailsSnapshotAsync(
            List<string> containerCodes
        )
        {
            var snapshot = new List<CPT_RED_货柜单详情表Store>();

            foreach (var chunk in containerCodes.Chunk(_options.LocalContainerBatchSize))
            {
                var batch = await _hqContext
                    .Db.Queryable<CPT_RED_货柜单详情表Store>()
                    .Where(x => !string.IsNullOrEmpty(x.主表GUID))
                    .Where(x => chunk.Contains(x.主表GUID!))
                    .ToListAsync();

                snapshot.AddRange(batch);
            }

            return snapshot;
        }

        /// <summary>
        /// 按货柜批次执行本地事务 upsert，并软删快照缺失的明细。
        /// </summary>
        private async Task UpsertBatchAsync(
            ISqlSugarClient db,
            List<CPT_RED_货柜单主表Store> hqContainers,
            List<CPT_RED_货柜单详情表Store> hqDetails,
            SyncStats stats
        )
        {
            var now = DateTime.UtcNow;
            var containerCodes = hqContainers
                .Select(x => x.HGUID!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await db.Ado.BeginTranAsync();
            try
            {
                var existingContainers = await db.Queryable<Container>()
                    .Where(x => containerCodes.Contains(x.ContainerCode))
                    .ToListAsync();
                var existingContainerMap = existingContainers.ToDictionary(
                    x => x.ContainerCode,
                    StringComparer.OrdinalIgnoreCase
                );

                var existingDetails = await db.Queryable<ContainerDetail>()
                    .Where(x => containerCodes.Contains(x.ContainerCode))
                    .ToListAsync();
                var existingDetailMap = existingDetails.ToDictionary(
                    x => x.DetailCode,
                    StringComparer.OrdinalIgnoreCase
                );

                var containersToInsert = new List<Container>();
                var containersToUpdate = new List<Container>();
                foreach (var hqContainer in hqContainers)
                {
                    var localEntity = MapContainer(hqContainer, now);
                    if (existingContainerMap.TryGetValue(localEntity.ContainerCode, out var existing))
                    {
                        // 更新路径保留本地创建审计字段，避免覆盖既有创建记录。
                        localEntity.CreatedAt = existing.CreatedAt;
                        localEntity.CreatedBy = existing.CreatedBy;
                        containersToUpdate.Add(localEntity);
                    }
                    else
                    {
                        containersToInsert.Add(localEntity);
                    }
                }

                var detailsToInsert = new List<ContainerDetail>();
                var detailsToUpdate = new List<ContainerDetail>();
                foreach (var hqDetail in hqDetails)
                {
                    var localEntity = MapDetail(hqDetail, now);
                    if (existingDetailMap.TryGetValue(localEntity.DetailCode, out var existing))
                    {
                        // 更新路径保留本地创建审计字段，避免把旧记录“重建”为新记录。
                        localEntity.CreatedAt = existing.CreatedAt;
                        localEntity.CreatedBy = existing.CreatedBy;
                        detailsToUpdate.Add(localEntity);
                    }
                    else
                    {
                        detailsToInsert.Add(localEntity);
                    }
                }

                if (containersToInsert.Count > 0)
                {
                    await db.Fastest<Container>()
                        .AS("Container")
                        .PageSize(_options.WriteBatchSize)
                        .BulkCopyAsync(containersToInsert);
                }

                if (containersToUpdate.Count > 0)
                {
                    await db.Fastest<Container>()
                        .AS("Container")
                        .PageSize(_options.WriteBatchSize)
                        .BulkUpdateAsync(containersToUpdate);
                }

                if (detailsToInsert.Count > 0)
                {
                    await db.Fastest<ContainerDetail>()
                        .AS("ContainerDetail")
                        .PageSize(_options.WriteBatchSize)
                        .BulkCopyAsync(detailsToInsert);
                }

                if (detailsToUpdate.Count > 0)
                {
                    await db.Fastest<ContainerDetail>()
                        .AS("ContainerDetail")
                        .PageSize(_options.WriteBatchSize)
                        .BulkUpdateAsync(detailsToUpdate);
                }

                var hqDetailCodes = hqDetails
                    .Select(x => x.HGUID!.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var detailCodesToSoftDelete = existingDetails
                    .Where(x => !x.IsDeleted && !hqDetailCodes.Contains(x.DetailCode))
                    .Select(x => x.DetailCode)
                    .ToList();

                if (detailCodesToSoftDelete.Count > 0)
                {
                    await db.Updateable<ContainerDetail>()
                        .SetColumns(x => x.IsDeleted == true)
                        .SetColumns(x => x.UpdatedAt == now)
                        .SetColumns(x => x.UpdatedBy == SyncActor)
                        .Where(x => detailCodesToSoftDelete.Contains(x.DetailCode))
                        .ExecuteCommandAsync();
                }

                await db.Ado.CommitTranAsync();

                stats.AddedCount += containersToInsert.Count + detailsToInsert.Count;
                stats.UpdatedCount += containersToUpdate.Count + detailsToUpdate.Count;
                stats.SoftDeletedDetailCount += detailCodesToSoftDelete.Count;
            }
            catch
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
        }

        /// <summary>
        /// 将 HQ 主表映射为本地主表。
        /// </summary>
        private Container MapContainer(CPT_RED_货柜单主表Store source, DateTime now)
        {
            var entity = _mapper.Map<Container>(source);
            entity.ContainerCode = NormalizeRequiredGuid(source.HGUID, "货柜主表 HGUID", source.ID);
            entity.ContainerNumber = TrimLen(source.货柜编号, 50);
            entity.Remarks = TrimLen(source.备注, 1000);
            entity.Remarks2 = TrimLen(source.备注2, 1000);
            entity.IsDeleted = false;
            entity.CreatedAt = source.FGC_CreateDate ?? source.FGC_LastModifyDate ?? source.装柜日期 ?? now;
            entity.CreatedBy = TrimLen(source.FGC_Creator, 100) ?? SyncActor;
            entity.UpdatedAt = source.FGC_LastModifyDate ?? source.FGC_CreateDate ?? source.装柜日期 ?? now;
            entity.UpdatedBy =
                TrimLen(source.FGC_LastModifier, 100)
                ?? TrimLen(source.FGC_Creator, 100)
                ?? SyncActor;
            return entity;
        }

        /// <summary>
        /// 将 HQ 明细映射为本地明细。
        /// </summary>
        private ContainerDetail MapDetail(CPT_RED_货柜单详情表Store source, DateTime now)
        {
            var entity = _mapper.Map<ContainerDetail>(source);
            entity.DetailCode = NormalizeRequiredGuid(source.HGUID, "货柜明细 HGUID", source.ID);
            entity.ContainerCode = NormalizeRequiredGuid(source.主表GUID, "货柜明细主表GUID", source.ID);
            entity.ProductCode = TrimLen(source.商品编码, 50);
            entity.LoadingType = TrimLen(source.装柜类型, 20);
            entity.MixedGroupCode = TrimLen(source.混装GUID, 50);
            entity.ProductType = TrimLen(source.商品类型, 20);
            entity.Remarks = TrimLen(source.备注, 500);
            entity.IsDeleted = false;
            entity.CreatedAt = source.FGC_CreateDate ?? source.FGC_LastModifyDate ?? now;
            entity.CreatedBy = TrimLen(source.FGC_Creator, 100) ?? SyncActor;
            entity.UpdatedAt = source.FGC_LastModifyDate ?? source.FGC_CreateDate ?? now;
            entity.UpdatedBy =
                TrimLen(source.FGC_LastModifier, 100)
                ?? TrimLen(source.FGC_Creator, 100)
                ?? SyncActor;
            return entity;
        }

        /// <summary>
        /// 结束任务日志并补齐结果时间。
        /// </summary>
        private async Task FinishTaskAsync(Guid taskLogId, SyncResult result)
        {
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;

            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLogId);
            }
            else
            {
                await _taskLogService.LogTaskFailureAsync(taskLogId, result.Message, false);
            }
        }

        private static SyncResult BuildFailedResult(string message, string errorCode)
        {
            var now = DateTime.UtcNow;
            return new SyncResult
            {
                StartTime = now,
                EndTime = now,
                Duration = TimeSpan.Zero,
                IsSuccess = false,
                Message = message,
                ErrorCode = errorCode,
                ErrorCount = 1,
            };
        }

        private static string NormalizeRequiredGuid(string? value, string fieldName, int sourceId)
        {
            var normalized = TrimLen(value, 50);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            throw new ContainerSyncInvalidSourceDataException(
                $"HQ 数据质量失败：{fieldName} 为空，来源 ID={sourceId}"
            );
        }

        private static string? TrimLen(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private sealed class DetailParentProjection
        {
            public int DetailId { get; set; }
            public string? ParentGuid { get; set; }
        }

        private sealed class SyncStats
        {
            public int AddedCount { get; set; }
            public int UpdatedCount { get; set; }
            public int SoftDeletedDetailCount { get; set; }
        }
    }
}
