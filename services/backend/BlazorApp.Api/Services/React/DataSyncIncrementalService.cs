using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class DataSyncIncrementalService : IDataSyncIncrementalService
    {
        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly HBSalesSqlSugarContext _hbSalesContext;
        private readonly POSMSqlSugarContext _posmContext;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ILogger<DataSyncIncrementalService> _logger;
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly IStoreRetailPriceHqSyncService _storeRetailPriceHqSyncService;
        private readonly IMemoryCache _cache;
        private readonly IWarehouseProductChangeHistoryService _changeHistoryService;
        private readonly ICurrentUserService _currentUserService;
        private const string StoreRetailPricesIncrementalTaskType = "SyncStoreRetailPricesIncremental";

        public DataSyncIncrementalService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            HBSalesSqlSugarContext hbSalesContext,
            POSMSqlSugarContext posmContext,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<DataSyncIncrementalService> logger,
            ScheduledTaskLogService taskLogService,
            IStoreRetailPriceHqSyncService storeRetailPriceHqSyncService,
            IMemoryCache cache,
            IWarehouseProductChangeHistoryService changeHistoryService,
            ICurrentUserService currentUserService
        )
        {
            ArgumentNullException.ThrowIfNull(currentUserService);

            _localContext = localContext;
            _hqContext = hqContext;
            _hbSalesContext = hbSalesContext;
            _posmContext = posmContext;
            _configuration = configuration;
            _mapper = mapper;
            _logger = logger;
            _taskLogService = taskLogService;
            _storeRetailPriceHqSyncService = storeRetailPriceHqSyncService;
            _cache = cache;
            _changeHistoryService = changeHistoryService;
            _currentUserService = currentUserService;
        }

        public async Task<SyncResult> SyncPosmProductSupplierMappingsIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            ScheduledTaskLog? taskLog = null;
            const string taskType = TaskType.SyncPosmProductSupplierMappingsIncremental;

            try
            {
                _logger.LogInformation("[ReactSync] 商品-供应商映射增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 商品-供应商映射增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    // 先从历史成功任务里找增量起点，避免当前运行中的任务把最近成功记录顶掉。
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(10, taskType);
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 商品-供应商映射增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 商品-供应商映射增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                taskLog = await StartPosmIncrementalTaskLogAsync(taskType);

                var updatedCount = 0;
                var insertedCount = 0;
                var deletedCount = 0;

                _logger.LogInformation("[ReactSync] 查询现有映射数据");
                var existingMappings = await _posmContext
                    .Db.Queryable<PosmProductSupplierMapping>()
                    .Where(m => !m.IsDeleted)
                    .ToListAsync();

                var existingDict = existingMappings.ToDictionary(m => m.ProductCode, m => m);
                _logger.LogInformation(
                    "[ReactSync] 现有映射数据 {Count} 条",
                    existingMappings.Count
                );

                var productsWithSupplier200 = await _localContext
                    .Db.Queryable<Product>()
                    .Where(p =>
                        p.LocalSupplierCode == "200"
                        && !string.IsNullOrEmpty(p.ProductCode)
                        && p.UpdatedAt >= effectiveStart
                        && !p.IsDeleted
                    )
                    .Select(p => p.ProductCode!)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation(
                    "[ReactSync] 找到 {Count} 个本地供应商为200的商品需要更新",
                    productsWithSupplier200.Count
                );

                var productChinaSupplierDict = new Dictionary<string, string>();
                if (productsWithSupplier200.Any())
                {
                    var warehouseProducts = await _localContext
                        .Db.Queryable<WarehouseProduct>()
                        .Includes(wp => wp.DomesticProduct)
                        .Where(wp =>
                            wp.ProductCode != null
                            && productsWithSupplier200.Contains(wp.ProductCode)
                            && !wp.IsDeleted
                        )
                        .ToListAsync();

                    foreach (var wp in warehouseProducts)
                    {
                        if (
                            !string.IsNullOrEmpty(wp.ProductCode)
                            && wp.DomesticProduct != null
                            && !string.IsNullOrEmpty(wp.DomesticProduct.SupplierCode)
                        )
                        {
                            productChinaSupplierDict[wp.ProductCode] =
                                wp.DomesticProduct.SupplierCode;
                        }
                    }
                }

                _logger.LogInformation(
                    "[ReactSync] 找到 {Count} 个商品关联了中国供应商",
                    productChinaSupplierDict.Count
                );

                var updatedProducts = await _localContext
                    .Db.Queryable<Product>()
                    .Where(p => p.UpdatedAt >= effectiveStart && !p.IsDeleted)
                    .ToListAsync();

                _logger.LogInformation(
                    "[ReactSync] 读取到 {Count} 个最近更新的商品",
                    updatedProducts.Count
                );

                var toUpdate = new List<PosmProductSupplierMapping>();
                var toInsert = new List<PosmProductSupplierMapping>();

                foreach (var product in updatedProducts)
                {
                    if (string.IsNullOrEmpty(product.ProductCode))
                        continue;

                    var mapping = new PosmProductSupplierMapping
                    {
                        ProductCode = product.ProductCode,
                        LocalSupplierCode = product.LocalSupplierCode ?? string.Empty,
                        ChinaSupplierCode = null,
                        LastUpdateTime = DateTime.Now,
                        IsDeleted = false,
                    };

                    if (product.LocalSupplierCode == "200")
                    {
                        if (
                            productChinaSupplierDict.TryGetValue(product.ProductCode, out var chinaCode)
                        )
                        {
                            mapping.ChinaSupplierCode = chinaCode;
                        }
                    }

                    if (existingDict.TryGetValue(mapping.ProductCode, out var existing))
                    {
                        if (
                            existing.LocalSupplierCode != mapping.LocalSupplierCode
                            || existing.ChinaSupplierCode != mapping.ChinaSupplierCode
                        )
                        {
                            mapping.CreatedAt = existing.CreatedAt;
                            toUpdate.Add(mapping);
                        }
                    }
                    else
                    {
                        toInsert.Add(mapping);
                    }
                }

                _logger.LogInformation(
                    "[ReactSync] 需要更新 {UpdateCount} 条，插入 {InsertCount} 条",
                    toUpdate.Count,
                    toInsert.Count
                );

                var transactionStarted = false;
                var transactionCompleted = false;
                try
                {
                    // 关键：所有读取和待写集合构造都放在事务外，POSM 事务只包裹真正的写操作。
                    if (toUpdate.Any() || toInsert.Any())
                    {
                        await _posmContext.Db.Ado.BeginTranAsync();
                        transactionStarted = true;
                    }

                    if (toUpdate.Any())
                    {
                        await _posmContext
                            .Db.Fastest<PosmProductSupplierMapping>()
                            .PageSize(10000)
                            .BulkUpdateAsync(toUpdate);
                        updatedCount = toUpdate.Count;
                        _logger.LogInformation("[ReactSync] 更新完成，共 {Count} 条", updatedCount);
                    }

                    if (toInsert.Any())
                    {
                        var productCodesToInsert = toInsert.Select(m => m.ProductCode).ToList();

                        deletedCount += await _posmContext
                            .Db.Deleteable<PosmProductSupplierMapping>()
                            .In(productCodesToInsert)
                            .ExecuteCommandAsync();

                        _logger.LogInformation(
                            "[ReactSync] 删除已存在的 {Count} 条映射记录",
                            productCodesToInsert.Count
                        );

                        await _posmContext
                            .Db.Fastest<PosmProductSupplierMapping>()
                            .PageSize(10000)
                            .BulkCopyAsync(toInsert);
                        insertedCount = toInsert.Count;
                        _logger.LogInformation(
                            "[ReactSync] 插入完成，共 {Count} 条",
                            insertedCount
                        );
                    }

                    if (transactionStarted)
                    {
                        await _posmContext.Db.Ado.CommitTranAsync();
                        transactionCompleted = true;
                    }

                    result.IsSuccess = true;
                    result.Message =
                        $"增量同步完成，新增 {insertedCount} 条，更新 {updatedCount} 条，删除 {deletedCount} 条";
                    result.AddedCount = insertedCount;
                    result.UpdatedCount = updatedCount;
                    result.DeletedCount = deletedCount;

                    if (taskLog != null)
                    {
                        await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    }
                }
                catch (Exception exTran)
                {
                    // 只有事务已启动且尚未完成时才允许回滚，避免再次操作已结束的事务对象。
                    if (transactionStarted && !transactionCompleted)
                    {
                        try
                        {
                            await _posmContext.Db.Ado.RollbackTranAsync();
                            transactionCompleted = true;
                        }
                        catch (Exception rollbackEx)
                        {
                            _logger.LogError(
                                rollbackEx,
                                "[ReactSync] 商品-供应商映射增量同步回滚失败"
                            );
                        }
                    }

                    throw new Exception("商品-供应商映射增量同步事务失败", exTran);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ReactSync] 商品-供应商映射增量同步异常: {Error}",
                    ex.Message
                );
                if (taskLog != null)
                {
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                }
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        private async Task<ScheduledTaskLog> StartPosmIncrementalTaskLogAsync(string taskType)
        {
            var existingScheduledTask = await TryGetRunningScheduledTaskLogAsync(taskType);
            if (existingScheduledTask != null)
            {
                _logger.LogInformation(
                    "[ReactSync] 商品-供应商映射增量：复用外层定时任务日志 {TaskId}",
                    existingScheduledTask.Id
                );
                return existingScheduledTask;
            }

            return await _taskLogService.LogTaskStartAsync(
                taskType,
                new TaskParameters(),
                TaskTrigger.Manual
            );
        }

        private async Task<ScheduledTaskLog?> TryGetRunningScheduledTaskLogAsync(string taskType)
        {
            // 只复用刚刚由调度外层创建的运行中任务，避免手动入口误复用历史脏日志。
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            var recentTasks = await _taskLogService.GetRecentTasksAsync(5, taskType);
            return recentTasks.FirstOrDefault(task =>
                task.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Running
                && task.TriggeredBy == TaskTrigger.Scheduled
                && task.CompletedAt == null
                && task.StartedAt >= cutoff
            );
        }

        public async Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncStoreLocalSupplierInvoicesIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 进货单增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 进货单增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncStoreLocalSupplierInvoicesIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 增量同步：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 增量同步：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = syncStartTime ?? startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var total = await hqDb.Queryable<RED_进货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .Where(x => x.H订单日期 >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 进货单增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingInvoices = await _localContext
                    .Db.Queryable<StoreLocalSupplierInvoice>()
                    .Select(x => x.InvoiceGUID)
                    .ToListAsync();
                var existingGuids = new HashSet<string>(existingInvoices);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var db = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await db.Queryable<RED_进货单主表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID))
                        .Where(x => x.H订单日期 >= effectiveStart)
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    db.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper
                        .Map<List<StoreLocalSupplierInvoice>>(batch)
                        .Where(x => !string.IsNullOrWhiteSpace(x.InvoiceGUID))
                        .ToList();

                    var toInsert = localBatch
                        .Where(x => !existingGuids.Contains(x.InvoiceGUID!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingGuids.Contains(x.InvoiceGUID!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<StoreLocalSupplierInvoice>()
                                .AS("StoreLocalSupplierInvoice")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }

                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<StoreLocalSupplierInvoice>()
                                .AS("StoreLocalSupplierInvoice")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }

                        _logger.LogInformation(
                            "[ReactSync] 进货单增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 进货单增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 进货单增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        public async Task<SyncResult> SyncContainersFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncContainersIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 货柜增量同步：开始");

                DateTime effectiveStartDate;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStartDate = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 货柜增量：使用请求指定起始日期: {Time}",
                        effectiveStartDate
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncContainersIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 货柜增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 货柜增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    const int overlapDays = 7;
                    effectiveStartDate = syncStartTime.HasValue
                        ? (
                            syncStartTime.Value.AddDays(-overlapDays) < startDate
                                ? syncStartTime.Value.AddDays(-overlapDays)
                                : startDate
                        )
                        : startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var total = await hqDb.Queryable<CPT_RED_货柜单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .Where(x => x.装柜日期 >= effectiveStartDate)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 货柜增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingContainers = await _localContext
                    .Db.Queryable<Container>()
                    .Select(x => x.ContainerCode)
                    .ToListAsync();
                var existingCodes = new HashSet<string>(existingContainers);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var db = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await db.Queryable<CPT_RED_货柜单主表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID))
                        .Where(x => x.装柜日期 >= effectiveStartDate)
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    db.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper
                        .Map<List<Container>>(batch)
                        .Where(x => !string.IsNullOrWhiteSpace(x.ContainerCode))
                        .ToList();

                    var toInsert = localBatch
                        .Where(x => !existingCodes.Contains(x.ContainerCode!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingCodes.Contains(x.ContainerCode!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<Container>()
                                .AS("Container")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }

                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<Container>()
                                .AS("Container")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }

                        _logger.LogInformation(
                            "[ReactSync] 货柜增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );

                        foreach (
                            var c in localBatch.Where(x =>
                                !string.IsNullOrWhiteSpace(x.ContainerCode)
                            )
                        )
                            existingCodes.Add(c.ContainerCode!);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 货柜增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 货柜增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        public async Task<SyncResult> SyncContainerDetailsFromHqIncrementalAsync(
            List<string>? masterGuids = null,
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncContainerDetailsIncremental",
                new TaskParameters { MasterGuids = masterGuids },
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 货柜详情增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 货柜详情增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncContainerDetailsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 货柜详情增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 货柜详情增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                var hasFilter = masterGuids != null && masterGuids.Any();

                var total = await hqDb.Queryable<CPT_RED_货柜单详情表Store>()
                    .Where(x => !string.IsNullOrEmpty(x.主表GUID))
                    .WhereIF(hasFilter, x => SqlFunc.ContainsArray(masterGuids!, x.主表GUID))
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 货柜详情增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingDetails = await _localContext
                    .Db.Queryable<ContainerDetail>()
                    .Select(x => x.DetailCode)
                    .ToListAsync();
                var existingGuids = new HashSet<string>(existingDetails);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var db = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await db.Queryable<CPT_RED_货柜单详情表Store>()
                        .Where(x => !string.IsNullOrEmpty(x.主表GUID))
                        .WhereIF(hasFilter, x => SqlFunc.ContainsArray(masterGuids!, x.主表GUID))
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    db.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper
                        .Map<List<ContainerDetail>>(batch)
                        .Where(x => !string.IsNullOrWhiteSpace(x.ContainerCode))
                        .ToList();

                    var toInsert = localBatch
                        .Where(x => !existingGuids.Contains(x.DetailCode!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingGuids.Contains(x.DetailCode!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<ContainerDetail>()
                                .AS("ContainerDetail")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }

                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<ContainerDetail>()
                                .AS("ContainerDetail")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }

                        _logger.LogInformation(
                            "[ReactSync] 货柜详情增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );

                        foreach (
                            var d in localBatch.Where(x => !string.IsNullOrWhiteSpace(x.DetailCode))
                        )
                            existingGuids.Add(d.DetailCode!);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 货柜详情增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 货柜详情增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        public async Task<SyncResult> SyncWareHouseOrdersFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncWareHouseOrdersIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 仓库订单增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 仓库订单增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncWareHouseOrdersIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 仓库订单增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 仓库订单增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = syncStartTime ?? startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var total = await hqDb.Queryable<CBP_RED_分店订货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .Where(x => x.订单日期 >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 仓库订单增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingOrders = await _localContext
                    .Db.Queryable<WareHouseOrder>()
                    .Select(x => x.OrderGUID)
                    .ToListAsync();
                var existingGuids = new HashSet<string>(existingOrders);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var db = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await db.Queryable<CBP_RED_分店订货单主表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID))
                        .Where(x => x.订单日期 >= effectiveStart)
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    db.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper
                        .Map<List<WareHouseOrder>>(batch)
                        .Where(x => !string.IsNullOrWhiteSpace(x.OrderGUID))
                        .ToList();

                    var toInsert = localBatch
                        .Where(x => !existingGuids.Contains(x.OrderGUID!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingGuids.Contains(x.OrderGUID!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<WareHouseOrder>()
                                .AS("WareHouseOrder")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }

                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<WareHouseOrder>()
                                .AS("WareHouseOrder")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }

                        _logger.LogInformation(
                            "[ReactSync] 仓库订单增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 仓库订单增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 仓库订单增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步商品信息：DIC_商品信息字典表 → Product
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncProductsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncProductsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                // 记录开始日志
                _logger.LogInformation("[ReactSync] 商品信息增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 商品增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncProductsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 商品增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 商品增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【商品增量同步】
                // 数据源：HQ.dbo.DIC_商品信息字典表
                // 目标表：HBweb.dbo.Product
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await hqDb.Queryable<DIC_商品信息字典表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 商品增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;
                var syncBatchGuid = Guid.NewGuid();

                var existingProducts = await _localContext
                    .Db.Queryable<Product>()
                    .Select(x => x.ProductCode)
                    .ToListAsync();
                var existingCodes = new HashSet<string>(
                    existingProducts
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code!),
                    StringComparer.OrdinalIgnoreCase
                );
                var auditedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var beforeSnapshots =
                    new Dictionary<string, WarehouseProductChangeSnapshotDto>(
                        StringComparer.OrdinalIgnoreCase
                    );
                var occurredAtUtc = DateTime.UtcNow;
                var transactionResult = await _localContext.Db.Ado.UseTranAsync(async () =>
                {
                    // 所有分页复用同一个本地事务，确保业务数据和历史记录要么一起提交，要么一起回滚。
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                        var batch = await hqDb.Queryable<DIC_商品信息字典表>()
                            .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                            .OrderBy(x => x.FGC_LastModifyDate)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        hqDb.Dispose();

                        if (!batch.Any())
                            continue;

                        var localBatch = _mapper.Map<List<Product>>(batch);

                        var toInsert = localBatch
                            .Where(x => !existingCodes.Contains(x.ProductCode!))
                            .ToList();
                        var toUpdate = localBatch
                            .Where(x => existingCodes.Contains(x.ProductCode!))
                            .ToList();

                        var pageAuditCodes = toInsert
                            .Concat(toUpdate)
                            .Select(item => item.ProductCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var firstAuditCodes = pageAuditCodes
                            .Where(code => auditedProductCodes.Add(code))
                            .ToList();
                        if (firstAuditCodes.Any())
                        {
                            var pageBeforeSnapshots = await CaptureChangeSnapshotsAsync(firstAuditCodes);
                            foreach (var snapshot in pageBeforeSnapshots)
                            {
                                beforeSnapshots.TryAdd(snapshot.Key, snapshot.Value);
                            }
                        }

                        foreach (var item in toInsert)
                        {
                            item.CreatedAt = occurredAtUtc;
                            item.CreatedBy = "System";
                            item.UpdatedAt = occurredAtUtc;
                            item.UpdatedBy = "System";
                        }
                        foreach (var item in toUpdate)
                        {
                            item.UpdatedAt = occurredAtUtc;
                            item.UpdatedBy = "System";
                        }
                        using var auditScope = SqlSugarAuditScope.PreserveExplicitAuditFields();
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<Product>()
                                .AS("Product")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<Product>()
                                .AS("Product")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                        }

                        added += toInsert.Count;
                        updated += toUpdate.Count;
                        foreach (var item in toInsert)
                        {
                            if (!string.IsNullOrWhiteSpace(item.ProductCode))
                            {
                                existingCodes.Add(item.ProductCode!);
                            }
                        }
                        _logger.LogInformation(
                            "[ReactSync] 商品增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    // 所有业务写入完成后才读取最终快照，整轮只生成一次历史事件。
                    var afterSnapshots = await CaptureChangeSnapshotsAsync(auditedProductCodes);
                    await RecordChangeHistoryAsync(
                        beforeSnapshots,
                        afterSnapshots,
                        "Sync",
                        "DataSyncIncremental",
                        syncBatchGuid,
                        occurredAtUtc
                    );
                });
                if (!transactionResult.IsSuccess)
                {
                    throw transactionResult.ErrorException
                        ?? new InvalidOperationException("商品增量事务失败");
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 商品信息增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                // 业务锁冲突可重试，不能被包装成普通同步失败。
                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict))
                {
                    result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                    result.BusyErrorCount = 1;
                    result.Message = conflict!.Message;
                }
                else
                {
                    result.Message = $"同步失败: {ex.Message}";
                }
                result.AddedCount = 0;
                result.UpdatedCount = 0;
                result.ErrorCount = 1;
                return result;
            }
        }

        /// <summary>
        /// 增量同步分店零售价：DIC_商品零售价表 → StoreRetailPrice
        /// 基于最近一次成功同步时间点或请求指定起始时间，
        /// 找不到历史成功记录时由统一服务的 DefaultIncrementalDays 配置决定，支持分店筛选。
        /// </summary>
        public async Task<SyncResult> SyncStoreRetailPricesFromHqIncrementalAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDateFromRequest = null
        )
        {
            if (_storeRetailPriceHqSyncService != null)
            {
                var effectiveStart = await ResolveStoreRetailPriceIncrementalStartAsync(
                    startDateFromRequest
                );
                return await _storeRetailPriceHqSyncService.SyncIncrementalAsync(
                    selectedStoreCodes,
                    effectiveStart
                );
            }

            throw new InvalidOperationException("分店零售价 HQ 统一同步服务未注册");
        }

        private async Task<DateTime?> ResolveStoreRetailPriceIncrementalStartAsync(
            DateTime? startDateFromRequest
        )
        {
            if (startDateFromRequest.HasValue)
            {
                _logger.LogInformation(
                    "[ReactSync] 分店零售价增量：使用请求指定起始日期: {Time}",
                    startDateFromRequest.Value
                );
                return startDateFromRequest.Value;
            }

            var lastSuccessTask = await _localContext.Db.Queryable<ScheduledTaskLog>()
                .Where(t =>
                    t.TaskType == StoreRetailPricesIncrementalTaskType
                    && t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                )
                .OrderByDescending(t => t.StartedAt)
                .FirstAsync();

            if (lastSuccessTask?.StartedAt is DateTime syncStartTime)
            {
                // 旧增量入口按最近成功任务的开始时间恢复窗口，避免委托统一服务后扩大同步范围。
                _logger.LogInformation(
                    "[ReactSync] 分店零售价增量：上次成功同步时间: {Time}",
                    syncStartTime
                );
                return syncStartTime;
            }

            _logger.LogInformation(
                "[ReactSync] 分店零售价增量：未找到历史成功记录，使用统一服务默认窗口"
            );
            return null;
        }

        /// <summary>
        /// 增量同步国货商品：CPT_DIC_商品信息字典表 → DomesticProduct
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncDomesticProductsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncDomesticProductsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 国货商品增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 国货商品增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncDomesticProductsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 国货商品增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 国货商品增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                if (!(await _hbSalesContext.TestConnectionAsync()))
                {
                    throw new Exception("HBSales 数据库连接失败");
                }

                // 【国货商品增量同步】
                // 数据源：HBSales.dbo.CPT_DIC_商品信息字典表
                // 目标表：HBweb.dbo.DomesticProduct
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await _hbSalesContext
                    .Db.Queryable<CPT_DIC_商品信息字典表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .Where(x => !string.IsNullOrEmpty(x.商品编码))
                    .CountAsync();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 国货商品增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;
                var syncBatchGuid = Guid.NewGuid();

                var existingCodes = await _localContext
                    .Db.Queryable<DomesticProduct>()
                    .Select(x => x.ProductCode)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
                var auditedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var beforeSnapshots =
                    new Dictionary<string, WarehouseProductChangeSnapshotDto>(
                        StringComparer.OrdinalIgnoreCase
                    );
                var occurredAtUtc = DateTime.UtcNow;
                var transactionResult = await _localContext.Db.Ado.UseTranAsync(async () =>
                {
                    // 所有分页复用同一个本地事务，确保业务数据和历史记录要么一起提交，要么一起回滚。
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        // 【批次查询】从HQ获取增量数据
                        var batch = await _hbSalesContext
                            .Db.Queryable<CPT_DIC_商品信息字典表>()
                            .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                            .Where(x => !string.IsNullOrEmpty(x.商品编码))
                            .OrderBy(x => x.FGC_LastModifyDate)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();

                        if (!batch.Any())
                            continue;

                        // 【AutoMapper映射】HQ实体 → 本地实体
                        var localBatch = _mapper.Map<List<DomesticProduct>>(batch);

                        // 【数据分类】根据ProductCode判断是新增还是更新
                        var toInsert = localBatch
                            .Where(x => !existingSet.Contains(x.ProductCode!))
                            .ToList();
                        var toUpdate = localBatch
                            .Where(x => existingSet.Contains(x.ProductCode!))
                            .ToList();

                        var pageAuditCodes = toInsert
                            .Concat(toUpdate)
                            .Select(item => item.ProductCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var firstAuditCodes = pageAuditCodes
                            .Where(code => auditedProductCodes.Add(code))
                            .ToList();
                        if (firstAuditCodes.Any())
                        {
                            var pageBeforeSnapshots = await CaptureChangeSnapshotsAsync(firstAuditCodes);
                            foreach (var snapshot in pageBeforeSnapshots)
                            {
                                beforeSnapshots.TryAdd(snapshot.Key, snapshot.Value);
                            }
                        }

                        foreach (var item in toInsert)
                        {
                            item.CreatedAt = occurredAtUtc;
                            item.CreatedBy = "System";
                            item.UpdatedAt = occurredAtUtc;
                            item.UpdatedBy = "System";
                        }
                        foreach (var item in toUpdate)
                        {
                            item.UpdatedAt = occurredAtUtc;
                            item.UpdatedBy = "System";
                        }
                        using var auditScope = SqlSugarAuditScope.PreserveExplicitAuditFields();
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<DomesticProduct>()
                                .AS("DomesticProduct")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<DomesticProduct>()
                                .AS("DomesticProduct")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                        }

                        added += toInsert.Count;
                        updated += toUpdate.Count;
                        foreach (var item in toInsert)
                        {
                            if (!string.IsNullOrWhiteSpace(item.ProductCode))
                            {
                                existingSet.Add(item.ProductCode!);
                            }
                        }
                        _logger.LogInformation(
                            "[ReactSync] 国货商品增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    // 所有业务写入完成后才读取最终快照，整轮只生成一次历史事件。
                    var afterSnapshots = await CaptureChangeSnapshotsAsync(auditedProductCodes);
                    await RecordChangeHistoryAsync(
                        beforeSnapshots,
                        afterSnapshots,
                        "Sync",
                        "DataSyncIncremental",
                        syncBatchGuid,
                        occurredAtUtc
                    );
                });
                if (!transactionResult.IsSuccess)
                {
                    throw transactionResult.ErrorException
                        ?? new InvalidOperationException("国货商品增量事务失败");
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 国货商品增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                result.AddedCount = 0;
                result.UpdatedCount = 0;
                result.ErrorCount = 1;
                return result;
            }
        }

        /// <summary>
        /// 增量同步国内供应商：CBP_DIC_国内供应商信息表 → ChinaSupplier
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncChinaSuppliersFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncChinaSuppliersIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 国内供应商增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 供应商增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncChinaSuppliersIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 供应商增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 供应商增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                if (!(await _hbSalesContext.TestConnectionAsync()))
                {
                    throw new Exception("HBSales 数据库连接失败");
                }

                var total = await _hbSalesContext
                    .Db.Queryable<CBP_DIC_国内供应商信息表>()
                    .Where(x => x.FGC_LastModifyDate != null && x.FGC_LastModifyDate != "")
                    .Where(x =>
                        x.FGC_LastModifyDate != null
                        && x.FGC_LastModifyDate.CompareTo(
                            effectiveStart.ToString("yyyy-MM-dd HH:mm:ss")
                        ) >= 0
                    )
                    .CountAsync();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 供应商增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingCodes = await _localContext
                    .Db.Queryable<ChinaSupplier>()
                    .Select(x => x.SupplierCode)
                    .ToListAsync();
                var existingSet = new HashSet<string>(
                    existingCodes
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code!)
                );

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var batch = await _hbSalesContext
                        .Db.Queryable<CBP_DIC_国内供应商信息表>()
                        .Where(x => x.FGC_LastModifyDate != null && x.FGC_LastModifyDate != "")
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<ChinaSupplier>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.SupplierCode!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.SupplierCode!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<ChinaSupplier>()
                                .AS("ChinaSupplier")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<ChinaSupplier>()
                                .AS("ChinaSupplier")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 供应商增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 供应商增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 国内供应商增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步仓库分类：CBP_DIC_商品分类码表 → WarehouseCategory
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncWarehouseCategoriesFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncWarehouseCategoriesIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 仓库分类增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 仓库分类增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncWarehouseCategoriesIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 仓库分类增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 仓库分类增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【仓库分类增量同步】
                // 数据源：HQ.dbo.CBP_DIC_商品分类码表
                // 目标表：HBweb.dbo.WarehouseCategory
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await hqDb.Queryable<CBP_DIC_商品分类码表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 仓库分类增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingGuids = await _localContext
                    .Db.Queryable<WarehouseCategory>()
                    .Select(x => x.CategoryGUID)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingGuids);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<CBP_DIC_商品分类码表>()
                        .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<WarehouseCategory>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.CategoryGUID!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.CategoryGUID!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<WarehouseCategory>()
                                .AS("WarehouseCategory")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<WarehouseCategory>()
                                .AS("WarehouseCategory")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 仓库分类增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 仓库分类增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (added + updated > 0)
                {
                    // 仓库分类增量同步直接写 WarehouseCategory，需同步清理移动端分类树缓存。
                    WarehouseCategoryReactService.InvalidateTreeCache(_cache);
                }

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 仓库分类增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步仓库商品：CBP_DIC_商品库存表 → WarehouseProduct
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncWarehouseProductsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncWarehouseProductsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 仓库商品增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 仓库商品增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncWarehouseProductsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 仓库商品增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 仓库商品增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【仓库商品增量同步】
                // 数据源：HQ.dbo.CBP_DIC_商品库存表
                // 目标表：HBweb.dbo.WarehouseProduct
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await hqDb.Queryable<CBP_DIC_商品库存表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .Where(x => SqlFunc.HasValue(x.H商品编码))
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 仓库商品增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var syncBatchGuid = Guid.NewGuid();
                var existingCodes = await _localContext
                    .Db.Queryable<WarehouseProduct>()
                    .Select(x => x.ProductCode)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
                var auditedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var beforeSnapshots =
                    new Dictionary<string, WarehouseProductChangeSnapshotDto>(
                        StringComparer.OrdinalIgnoreCase
                    );
                var occurredAtUtc = DateTime.UtcNow;
                var transactionResult = await _localContext.Db.Ado.UseTranAsync(async () =>
                {
                    // 所有分页复用同一个本地事务，确保业务数据和历史记录要么一起提交，要么一起回滚。
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                        var batch = await hqDb.Queryable<CBP_DIC_商品库存表>()
                            .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                            .Where(x => SqlFunc.HasValue(x.H商品编码))
                            .OrderBy(x => x.FGC_LastModifyDate)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        hqDb.Dispose();

                        if (!batch.Any())
                            continue;

                        var localBatch = _mapper.Map<List<WarehouseProduct>>(batch);

                        var pageProductCodes = localBatch
                            .Select(item => item.ProductCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code!.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (pageProductCodes.Count == 0)
                        {
                            continue;
                        }
                        var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            _localContext.Db,
                            pageProductCodes
                        );

                        var toInsert = localBatch
                            .Where(x => !existingSet.Contains(x.ProductCode!))
                            .ToList();
                        var toUpdate = localBatch
                            .Where(x => existingSet.Contains(x.ProductCode!))
                            .ToList();

                        var pageAuditCodes = toInsert
                            .Concat(toUpdate)
                            .Select(item => item.ProductCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var firstAuditCodes = pageAuditCodes
                            .Where(code => auditedProductCodes.Add(code))
                            .ToList();
                        if (firstAuditCodes.Any())
                        {
                            var pageBeforeSnapshots = await CaptureChangeSnapshotsAsync(firstAuditCodes);
                            foreach (var snapshot in pageBeforeSnapshots)
                            {
                                beforeSnapshots.TryAdd(snapshot.Key, snapshot.Value);
                            }
                        }

                        foreach (var item in toInsert)
                        {
                            item.CreatedAt = occurredAtUtc;
                            item.CreatedBy = "System";
                            item.UpdatedAt = occurredAtUtc;
                            item.UpdatedBy = "System";
                        }
                        foreach (var item in toUpdate)
                        {
                            item.UpdatedAt = occurredAtUtc;
                            item.UpdatedBy = "System";
                        }

                        using var auditScope = SqlSugarAuditScope.PreserveExplicitAuditFields();
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<WarehouseProduct>()
                                .AS("WarehouseProduct")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<WarehouseProduct>()
                                .AS("WarehouseProduct")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                        }

                        // HQ ImportPrice 落库后，统一重算当前批次 Type1/Type2 关系成本。
                        await new SetChildPurchasePriceService(_localContext.Db).RecalculateLockedAsync(
                            lockScope,
                            pageProductCodes,
                            storeCodes: null,
                            "DataSync"
                        );

                        added += toInsert.Count;
                        updated += toUpdate.Count;
                        foreach (var item in toInsert)
                        {
                            if (!string.IsNullOrWhiteSpace(item.ProductCode))
                            {
                                existingSet.Add(item.ProductCode!);
                            }
                        }
                        _logger.LogInformation(
                            "[ReactSync] 仓库商品增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    // 所有业务写入完成后才读取最终快照，整轮只生成一次历史事件。
                    var afterSnapshots = await CaptureChangeSnapshotsAsync(auditedProductCodes);
                    await RecordChangeHistoryAsync(
                        beforeSnapshots,
                        afterSnapshots,
                        "Sync",
                        "DataSyncIncremental",
                        syncBatchGuid,
                        occurredAtUtc
                    );
                });
                if (!transactionResult.IsSuccess)
                {
                    throw transactionResult.ErrorException
                        ?? new InvalidOperationException("仓库商品增量事务失败");
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 仓库商品增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict))
                {
                    result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                    result.BusyErrorCount = 1;
                    result.Message = conflict!.Message;
                }
                else
                {
                    result.Message = $"同步失败: {ex.Message}";
                }
                result.AddedCount = 0;
                result.UpdatedCount = 0;
                result.ErrorCount = 1;
                return result;
            }
        }

        /// <summary>
        /// 增量同步库位：CPT_DIC_货位编码信息表 → Location
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncLocationsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncLocationsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 库位增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 库位增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncLocationsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 库位增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 库位增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【库位增量同步】
                // 数据源：HQ.dbo.CPT_DIC_货位编码信息表
                // 目标表：HBweb.dbo.Location
                // 同步策略：全量同步（CPT_DIC_货位编码信息表没有FGC_LastModifyDate字段）
                // 注意：此表无时间戳字段，采用全量同步策略
                var total = await hqDb.Queryable<CPT_DIC_货位编码信息表>()
                    .Where(x => x.货位编码 != null && x.货位编码 != "")
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 库位增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingGuids = await _localContext
                    .Db.Queryable<Location>()
                    .Select(x => x.LocationGuid)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingGuids);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<CPT_DIC_货位编码信息表>()
                        .Where(x => x.FGC_LastModifyDate != null && x.FGC_LastModifyDate != "")
                        .Where(x =>
                            x.FGC_LastModifyDate != null
                            && x.FGC_LastModifyDate.CompareTo(
                                effectiveStart.ToString("yyyy-MM-dd HH:mm:ss")
                            ) >= 0
                        )
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<Location>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.LocationGuid!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.LocationGuid!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<Location>()
                                .AS("Location")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<Location>()
                                .AS("Location")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 库位增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 库位增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 库位增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步商品库位：CPT_RED_货位存货信息表 → ProductLocation
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncProductLocationsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncProductLocationsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 商品库位增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 商品库位增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncProductLocationsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 商品库位增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 商品库位增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }
                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【商品库位增量同步】
                // 数据源：HQ.dbo.CPT_RED_货位存货信息表
                // 目标表：HBweb.dbo.ProductLocation
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await hqDb.Queryable<CPT_RED_货位存货信息表>()
                    .Where(x => x.FGC_LastModifyDate != null && x.FGC_LastModifyDate != "")
                    .Where(x =>
                        x.FGC_LastModifyDate != null
                        && x.FGC_LastModifyDate.CompareTo(
                            effectiveStart.ToString("yyyy-MM-dd HH:mm:ss")
                        ) >= 0
                    )
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 商品库位增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingIds = await _localContext
                    .Db.Queryable<ProductLocation>()
                    .Select(x => x.Guid)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingIds);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<CPT_RED_货位存货信息表>()
                        .Where(x => x.FGC_LastModifyDate != null && x.FGC_LastModifyDate != "")
                        .Where(x =>
                            x.FGC_LastModifyDate != null
                            && x.FGC_LastModifyDate.CompareTo(
                                effectiveStart.ToString("yyyy-MM-dd HH:mm:ss")
                            ) >= 0
                        )
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<ProductLocation>>(batch);

                    var toInsert = localBatch.Where(x => !existingSet.Contains(x.Guid!)).ToList();
                    var toUpdate = localBatch.Where(x => existingSet.Contains(x.Guid!)).ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<ProductLocation>()
                                .AS("ProductLocation")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<ProductLocation>()
                                .AS("ProductLocation")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 商品库位增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 商品库位增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 商品库位增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步收银用户：DIC_收银用户信息表 → CashRegisterUser
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncCashRegisterUsersFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncCashRegisterUsersIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 收银用户增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 收银用户增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncCashRegisterUsersIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 收银用户增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 收银用户增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }
                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【收银用户增量同步】
                // 数据源：HQ.dbo.DIC_收银用户信息表
                // 目标表：HBweb.dbo.CashRegisterUser
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await hqDb.Queryable<DIC_收银用户信息表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 收银用户增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;

                var existingCodes = await _localContext
                    .Db.Queryable<CashRegisterUser>()
                    .Select(x => x.HGUID)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingCodes);

                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await CashierBarcodeMutationLock.AcquireAsync(_localContext.Db);
                    // 关键逻辑：一次增量调用的全部分页共用一个事务，任何页冲突都回滚本次全部写入。
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                        var batch = await hqDb.Queryable<DIC_收银用户信息表>()
                            .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                            .OrderBy(x => x.FGC_LastModifyDate)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        hqDb.Dispose();

                        if (!batch.Any())
                            continue;

                        var localBatch = _mapper.Map<List<CashRegisterUser>>(batch);
                        var toInsert = localBatch.Where(x => !existingSet.Contains(x.HGUID!)).ToList();
                        var toUpdate = localBatch.Where(x => existingSet.Contains(x.HGUID!)).ToList();

                        await CashierBarcodeSyncGuard.ValidateAndReserveHqBatchAsync(
                            _localContext.Db,
                            localBatch
                        );
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<CashRegisterUser>()
                                .AS("CashRegisterUser")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<CashRegisterUser>()
                                .AS("CashRegisterUser")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        foreach (var item in toInsert)
                        {
                            if (!string.IsNullOrWhiteSpace(item.HGUID))
                            {
                                existingSet.Add(item.HGUID);
                            }
                        }
                        _logger.LogInformation(
                            "[ReactSync] 收银用户增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    await _localContext.Db.Ado.CommitTranAsync();
                }
                catch
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw;
                }

                result.IsSuccess = true;
                result.Message = $"增量同步完成，新增 {added} 条，更新 {updated} 条";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = 0;

                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 收银用户增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步分店一品多码：DIC_分店一品多码表 → StoreMultiCodeProduct
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步，支持分店筛选
        /// </summary>
        public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqIncrementalAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncStoreMultiCodeProductsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 分店一品多码增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 一品多码增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncStoreMultiCodeProductsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 一品多码增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 一品多码增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }
                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【分店一品多码增量同步】
                // 数据源：HQ.dbo.DIC_分店一品多码表
                // 目标表：HBweb.dbo.StoreMultiCodeProduct
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                // 支持按分店筛选：selectedStoreCodes
                var query = hqDb.Queryable<DIC_分店一品多码表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart);

                if (selectedStoreCodes?.Any() == true)
                    query = query.Where(x => selectedStoreCodes.Contains(x.H分店代码!));

                var total = await query.CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 一品多码增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;
                var busyErrors = 0;

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<DIC_分店一品多码表>()
                        .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                        .WhereIF(
                            selectedStoreCodes?.Any() == true,
                            x => selectedStoreCodes!.Contains(x.H分店代码!)
                        )
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<StoreMultiCodeProduct>>(batch)
                        .Select(item =>
                        {
                            // HQ 普通多码成本不能覆盖套装分摊成本，统一在锁内回写。
                            item.PurchasePrice = null;
                            return item;
                        })
                        .Where(item =>
                            !string.IsNullOrWhiteSpace(item.StoreCode)
                            && !string.IsNullOrWhiteSpace(item.ProductCode)
                            && !string.IsNullOrWhiteSpace(item.MultiCodeProductCode)
                        )
                        .GroupBy(GetStoreMultiCodeIdentityKey, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.Last())
                        .ToList();
                    var pageProductCodes = localBatch
                        .Select(x => x.ProductCode!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (pageProductCodes.Count == 0)
                        continue;

                    try
                    {
                        await _localContext.Db.Ado.BeginTranAsync();
                        var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            _localContext.Db,
                            pageProductCodes
                        );

                        // 必须在产品锁内重读保护关系，防止锁前旧快照覆盖刚创建或启用的套装子项。
                        var protectedStoreMultiCodeKeys = await GetProtectedStoreMultiCodeKeysAsync(
                            _localContext.Db,
                            pageProductCodes
                        );
                        var writableBatch = localBatch
                            .Where(item =>
                                !protectedStoreMultiCodeKeys.Contains(
                                    GetStoreMultiCodeBusinessKey(item)
                                )
                            )
                            .ToList();
                        var pageStoreCodes = writableBatch
                            .Select(x => x.StoreCode!.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var existingRows = pageStoreCodes.Count == 0
                            ? new List<StoreMultiCodeProduct>()
                            : await _localContext.Db.Queryable<StoreMultiCodeProduct>()
                                .Where(x =>
                                    x.StoreCode != null
                                    && x.ProductCode != null
                                    && x.MultiCodeProductCode != null
                                    && pageStoreCodes.Contains(x.StoreCode)
                                    && pageProductCodes.Contains(x.ProductCode)
                                )
                                .ToListAsync();
                        var tombstoneKeys = new HashSet<string>(
                            existingRows
                                .Where(row => row.IsDeleted)
                                .Select(GetStoreMultiCodeIdentityKey),
                            StringComparer.OrdinalIgnoreCase
                        );
                        var existingByKey = existingRows
                            .Where(row => !row.IsDeleted)
                            .GroupBy(GetStoreMultiCodeIdentityKey, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(
                                group => group.Key,
                                group => group.First(),
                                StringComparer.OrdinalIgnoreCase
                            );
                        var toInsert = new List<StoreMultiCodeProduct>();
                        var toUpdate = new List<StoreMultiCodeProduct>();
                        foreach (var item in writableBatch)
                        {
                            var identityKey = GetStoreMultiCodeIdentityKey(item);
                            if (
                                existingByKey.TryGetValue(
                                    identityKey,
                                    out var existing
                                )
                            )
                            {
                                existing.StoreMultiCodeProductCode =
                                    item.StoreMultiCodeProductCode;
                                existing.MultiBarcode = item.MultiBarcode;
                                existing.PurchasePrice = item.PurchasePrice;
                                existing.MultiCodeRetailPrice = item.MultiCodeRetailPrice;
                                existing.DiscountRate = item.DiscountRate;
                                existing.IsAutoPricing = item.IsAutoPricing;
                                existing.IsSpecialProduct = item.IsSpecialProduct;
                                existing.IsActive = item.IsActive;
                                existing.UpdatedAt = DateTime.UtcNow;
                                toUpdate.Add(existing);
                            }
                            // 同键已有有效行时只更新有效行；仅在没有有效行时，软删除墓碑阻止复活或旁路新增。
                            else if (tombstoneKeys.Contains(identityKey))
                            {
                                continue;
                            }
                            else
                            {
                                toInsert.Add(item);
                            }
                        }

                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Updateable(toUpdate)
                                .AS("StoreMultiCodeProduct")
                                // 只写 HQ 业务字段，绝不覆盖软删除状态、主键或创建审计。
                                .UpdateColumns(row => new
                                {
                                    row.StoreMultiCodeProductCode,
                                    row.MultiBarcode,
                                    row.PurchasePrice,
                                    row.MultiCodeRetailPrice,
                                    row.DiscountRate,
                                    row.IsAutoPricing,
                                    row.IsSpecialProduct,
                                    row.IsActive,
                                    row.UpdatedAt,
                                })
                                .ExecuteCommandAsync();
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<StoreMultiCodeProduct>()
                                .AS("StoreMultiCodeProduct")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                        }
                        if (toInsert.Count > 0 || toUpdate.Count > 0)
                        {
                            // 仅重算本批实际写入的门店商品组，避免门店与商品集合发生笛卡尔扩张。
                            await new SetChildPurchasePriceService(_localContext.Db)
                                .RecalculateStoreGroupsLockedAsync(
                                    lockScope,
                                    toInsert
                                        .Concat(toUpdate)
                                        .Select(item => (item.StoreCode, item.ProductCode)),
                                    "DataSync"
                                );
                        }
                        await _localContext.Db.Ado.CommitTranAsync();
                        updated += toUpdate.Count;
                        added += toInsert.Count;
                        _logger.LogInformation(
                            "[ReactSync] 一品多码增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        await _localContext.Db.Ado.RollbackTranAsync();
                        errors++;
                        if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
                        {
                            // 当前页已回滚；保留 BUSY 供调用方按可重试冲突处理。
                            result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                            busyErrors++;
                        }
                        _logger.LogError(
                            ex,
                            "[ReactSync] 一品多码增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;
                result.BusyErrorCount = busyErrors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 分店一品多码增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.ErrorCount = Math.Max(result.ErrorCount, 1);
                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict))
                {
                    result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                    result.BusyErrorCount = result.ErrorCount;
                    result.Message = conflict!.Message;
                }
                else
                {
                    result.Message = $"同步失败: {ex.Message}";
                }
                return result;
            }
        }

        /// <summary>
        /// 增量同步套装多码：DIC_一品多码表 → ProductSetCode
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncProductSetCodesIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 套装多码增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 套装多码增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncProductSetCodesIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 套装多码增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 套装多码增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }
                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var sourceIdentityRows = await hqDb.Queryable<DIC_一品多码表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .Where(x => !string.IsNullOrEmpty(x.H商品编码))
                    // 映射前先保留 HQ 原始 GUID，避免空 GUID 被 AutoMapper 随机值掩盖。
                    .Select(x => new DIC_一品多码表
                    {
                        ID = x.ID,
                        HGUID = x.HGUID,
                        H商品编码 = x.H商品编码,
                        H多码商品编号 = x.H多码商品编号,
                        FGC_LastModifyDate = x.FGC_LastModifyDate,
                    })
                    .ToListAsync();
                hqDb.Dispose();
                var sourcePreflight = ProductSetCodeIdentityResolver.PreflightSource(
                    sourceIdentityRows,
                    row => row.HGUID,
                    row => row.H商品编码,
                    row => row.H多码商品编号,
                    row => row.FGC_LastModifyDate,
                    row => row.ID
                );
                var conflictingProductCodes = sourceIdentityRows
                    .Where(row =>
                    {
                        var identity = ProductSetCodeIdentityResolver.CreateIdentity(
                            row.HGUID,
                            row.H商品编码,
                            row.H多码商品编号
                        );
                        return identity.Guid != null
                                && sourcePreflight.ConflictingGuids.Contains(identity.Guid)
                            || identity.BusinessKey != null
                                && sourcePreflight.ConflictingBusinessKeys.Contains(
                                    identity.BusinessKey
                                );
                    })
                    .Select(row => ProductSetCodeIdentityResolver.Normalize(row.H商品编码))
                    .Where(code => code != null)
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var identityConflictDetails = sourcePreflight.Conflicts
                    .Select(conflict => conflict.ToErrorMessage())
                    .ToList();

                var localIdentityRows = await _localContext.Db.Queryable<ProductSetCode>()
                    .ToListAsync();
                var preflightIdentityIndex = ProductSetCodeIdentityResolver.CreateIndex(
                    localIdentityRows
                );
                foreach (var sourceRow in sourcePreflight.Rows)
                {
                    var productCode = ProductSetCodeIdentityResolver.Normalize(
                        sourceRow.H商品编码
                    );
                    var businessKey = ProductSetCodeIdentityResolver.BuildBusinessKey(
                        sourceRow.H商品编码,
                        sourceRow.H多码商品编号
                    );
                    if (productCode == null || businessKey == null)
                    {
                        continue;
                    }

                    var resolution = preflightIdentityIndex.Resolve(
                        sourceRow.HGUID,
                        sourceRow.H商品编码,
                        sourceRow.H多码商品编号
                    );
                    if (
                        resolution.Kind != ProductSetCodeIdentityMatchKind.Conflict
                        && !IsCrossParentGuidOnly(resolution, sourceRow.H商品编码)
                    )
                    {
                        continue;
                    }

                    conflictingProductCodes.Add(productCode);
                    identityConflictDetails.Add(
                        $"本地 ProductSetCode 身份冲突：HQ ID={sourceRow.ID}，GUID={resolution.Guid ?? "(空)"}，业务键={ProductSetCodeIdentityResolver.FormatBusinessKey(businessKey)}，本地记录={ProductSetCodeIdentityResolver.FormatLocalRecords(resolution.AllMatches)}；增量同步不会在当前锁范围外跨父商品改键"
                    );
                }

                var acceptedSourceRows = sourcePreflight.Rows
                    .Where(row =>
                    {
                        var productCode = ProductSetCodeIdentityResolver.Normalize(
                            row.H商品编码
                        );
                        return productCode != null
                            && !conflictingProductCodes.Contains(productCode);
                    })
                    .ToList();
                var total = sourceIdentityRows.Count;

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 套装多码增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var sourcePages = BuildProductSetCodeSourcePages(
                    acceptedSourceRows,
                    hqBatchSize
                );
                var pages = sourcePages.Count;
                var added = 0;
                var updated = 0;
                var errors = conflictingProductCodes.Count;
                var busyErrors = 0;
                var identityErrorProductCodes = new HashSet<string>(
                    conflictingProductCodes,
                    StringComparer.OrdinalIgnoreCase
                );
                var validatedSourcePages = new List<List<DIC_一品多码表>>(pages);

                // 在任何本地写事务前冻结并校验完整增量负载。这样后页发现身份变化时，
                // 同一父商品在前页尚未提交；后续 HQ 变化留给下一轮同步处理。
                for (var page = 1; page <= pages; page++)
                {
                    var expectedSourcePage = sourcePages[page - 1];
                    var batch = new List<DIC_一品多码表>(expectedSourcePage.Count);
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    try
                    {
                        // 按预检快照中的稳定 ID 读取，避免相同修改时间跨 Skip/Take 边界
                        // 时重复或遗漏；每批限制 1000 个 ID，避免 SQL Server 参数膨胀。
                        foreach (var idChunk in expectedSourcePage.Select(row => row.ID).Chunk(1000))
                        {
                            var ids = idChunk.ToList();
                            batch.AddRange(
                                await hqDb.Queryable<DIC_一品多码表>()
                                    .Where(row => ids.Contains(row.ID))
                                    .ToListAsync()
                            );
                        }
                    }
                    finally
                    {
                        hqDb.Dispose();
                    }

                    var snapshotValidation = ValidateProductSetCodeSourcePageSnapshot(
                        expectedSourcePage,
                        batch
                    );
                    identityConflictDetails.AddRange(snapshotValidation.Errors);
                    foreach (var productCode in snapshotValidation.ConflictProductCodes)
                    {
                        if (identityErrorProductCodes.Add(productCode))
                        {
                            errors++;
                        }
                    }
                    validatedSourcePages.Add(snapshotValidation.AcceptedRows);
                }

                for (var page = 1; page <= pages; page++)
                {
                    var batch = validatedSourcePages[page - 1]
                        .Where(row =>
                        {
                            var productCode = ProductSetCodeIdentityResolver.Normalize(
                                row.H商品编码
                            );
                            return productCode != null
                                && !identityErrorProductCodes.Contains(productCode);
                        })
                        .ToList();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<ProductSetCode>>(batch)
                        .Select(item =>
                        {
                            // HQ 一品多码只能落为 Type2，成本由锁内统一重算回写。
                            item.SetType = 2;
                            item.SetPurchasePrice = null;
                            return item;
                        })
                        .Where(item =>
                            !string.IsNullOrWhiteSpace(item.ProductCode)
                            && !string.IsNullOrWhiteSpace(item.SetProductCode)
                        )
                        .ToList();
                    var pageProductCodes = localBatch
                        .Select(x => x.ProductCode.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (pageProductCodes.Count == 0)
                        continue;

                    try
                    {
                        await _localContext.Db.Ado.BeginTranAsync();
                        var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            _localContext.Db,
                            pageProductCodes
                        );

                        // 保护键和现有行都在锁内重读，避免旧快照覆盖已提交的新关系。
                        var protectedSetCodeKeys = await GetProtectedSetCodeKeysAsync(
                            _localContext.Db,
                            pageProductCodes
                        );
                        var protectedSetCodeIds = await GetProtectedSetCodeIdsAsync(
                            _localContext.Db,
                            pageProductCodes
                        );
                        var existingRows = await _localContext.Db.Queryable<ProductSetCode>()
                            .ToListAsync();
                        var identityIndex = ProductSetCodeIdentityResolver.CreateIndex(existingRows);
                        var lockedConflictProductCodes = new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase
                        );
                        foreach (var item in localBatch)
                        {
                            var resolution = identityIndex.Resolve(
                                item.SetCodeId,
                                item.ProductCode,
                                item.SetProductCode
                            );
                            if (
                                resolution.Kind != ProductSetCodeIdentityMatchKind.Conflict
                                && !IsCrossParentGuidOnly(resolution, item.ProductCode)
                            )
                            {
                                continue;
                            }

                            lockedConflictProductCodes.Add(item.ProductCode.Trim());
                            var businessKey = ProductSetCodeIdentityResolver.BuildBusinessKey(
                                item.ProductCode,
                                item.SetProductCode
                            );
                            identityConflictDetails.Add(
                                $"锁内 ProductSetCode 身份冲突：GUID={ProductSetCodeIdentityResolver.Normalize(item.SetCodeId) ?? "(空)"}，业务键={(businessKey == null ? "(空)" : ProductSetCodeIdentityResolver.FormatBusinessKey(businessKey))}，本地记录={ProductSetCodeIdentityResolver.FormatLocalRecords(resolution.AllMatches)}；父商品 {item.ProductCode.Trim()} 本页整组跳过"
                            );
                        }
                        errors += lockedConflictProductCodes.Count;

                        var writableBatch = localBatch
                            .Where(item =>
                                !lockedConflictProductCodes.Contains(item.ProductCode.Trim())
                            )
                            .Where(item =>
                            {
                                var resolution = identityIndex.Resolve(
                                    item.SetCodeId,
                                    item.ProductCode,
                                    item.SetProductCode
                                );
                                return !resolution.AllMatches.Any(row => row.SetType == 1)
                                    && !protectedSetCodeIds.Contains(item.SetCodeId)
                                    && !protectedSetCodeKeys.Contains(
                                        GetSetCodeBusinessKey(item)
                                    );
                            })
                            .ToList();
                        var mutationPlan = BuildProductSetCodePageMutationPlan(
                            writableBatch,
                            identityIndex
                        );
                        var toInsert = mutationPlan.ToInsert;
                        var toUpdate = mutationPlan.ToUpdate;

                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Updateable(toUpdate)
                                .AS("ProductSetCode")
                                // 只写 HQ 业务字段，绝不覆盖软删除状态、主键或创建审计。
                                .UpdateColumns(row => new
                                {
                                    row.SetItemNumber,
                                    row.SetProductCode,
                                    row.SetBarcode,
                                    row.SetPurchasePrice,
                                    row.SetRetailPrice,
                                    row.SetQuantity,
                                    row.SetType,
                                    row.IsActive,
                                    row.UpdatedAt,
                                })
                                .ExecuteCommandAsync();
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<ProductSetCode>()
                                .AS("ProductSetCode")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                        }
                        if (toInsert.Count > 0 || toUpdate.Count > 0)
                        {
                            // 同一产品锁和事务内统一重算 Type1/Type2，失败由本页回滚。
                            await new SetChildPurchasePriceService(_localContext.Db).RecalculateLockedAsync(
                                lockScope,
                                pageProductCodes,
                                storeCodes: null,
                                "DataSync"
                            );
                        }
                        await _localContext.Db.Ado.CommitTranAsync();
                        updated += toUpdate.Count;
                        added += toInsert.Count;
                        _logger.LogInformation(
                            "[ReactSync] 套装多码增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        await _localContext.Db.Ado.RollbackTranAsync();
                        errors++;
                        if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
                        {
                            // 当前页已回滚；保留 BUSY 供调用方按可重试冲突处理。
                            result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                            busyErrors++;
                        }
                        _logger.LogError(
                            ex,
                            "[ReactSync] 套装多码增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 个商品组或分页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;
                result.BusyErrorCount = busyErrors;
                if (identityConflictDetails.Count > 0)
                {
                    result.ErrorCode ??= "PRODUCT_SET_CODE_IDENTITY_CONFLICT";
                    var detailLines = identityConflictDetails.Take(50).ToList();
                    var omittedCount = identityConflictDetails.Count - detailLines.Count;
                    result.Details =
                        string.Join("\n", detailLines)
                        + (omittedCount > 0
                            ? $"\n另有 {omittedCount} 个身份冲突未展开"
                            : string.Empty);
                }

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 套装多码增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.ErrorCount = Math.Max(result.ErrorCount, 1);
                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict))
                {
                    result.ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode;
                    result.BusyErrorCount = result.ErrorCount;
                    result.Message = conflict!.Message;
                }
                else
                {
                    result.Message = $"同步失败: {ex.Message}";
                }
                return result;
            }
        }

        internal static (
            List<ProductSetCode> ToInsert,
            List<ProductSetCode> ToUpdate
        ) BuildProductSetCodePageMutationPlan(
            IReadOnlyList<ProductSetCode> writableBatch,
            ProductSetCodeIdentityIndex identityIndex
        )
        {
            var toInsert = new List<ProductSetCode>();
            var toUpdate = new List<ProductSetCode>();

            // 先完成同父商品内全部 GuidOnly 迁移并立即重建双索引，再处理旧键
            // 被同页另一 GUID 接管的关系。否则后续 KeyOnly 仍会命中迁移前的旧键，
            // 把两个合法 HQ 身份覆盖到同一个本地对象上。
            foreach (var item in writableBatch)
            {
                var resolution = identityIndex.Resolve(
                    item.SetCodeId,
                    item.ProductCode,
                    item.SetProductCode
                );
                var existing = resolution.MatchedRow;
                if (
                    existing == null
                    || existing.IsDeleted
                    || resolution.Kind != ProductSetCodeIdentityMatchKind.GuidOnly
                )
                {
                    continue;
                }

                var previousIdentity = ProductSetCodeIdentityResolver.CreateIdentity(existing);
                existing.SetProductCode = item.SetProductCode;
                identityIndex.Reindex(existing, previousIdentity);
            }

            var updateSet = new HashSet<ProductSetCode>(ReferenceEqualityComparer.Instance);
            foreach (var item in writableBatch)
            {
                var resolution = identityIndex.Resolve(
                    item.SetCodeId,
                    item.ProductCode,
                    item.SetProductCode
                );
                var existing = resolution.MatchedRow;
                if (existing != null)
                {
                    // DataSync 不恢复软删除 Type2；KeyOnly 保留本地主键。
                    if (existing.IsDeleted)
                    {
                        continue;
                    }
                    existing.SetItemNumber = item.SetItemNumber;
                    existing.SetBarcode = item.SetBarcode;
                    existing.SetPurchasePrice = item.SetPurchasePrice;
                    existing.SetRetailPrice = item.SetRetailPrice;
                    existing.SetQuantity = item.SetQuantity;
                    existing.SetType = item.SetType;
                    existing.IsActive = item.IsActive;
                    existing.UpdatedAt = DateTime.UtcNow;
                    if (updateSet.Add(existing))
                    {
                        toUpdate.Add(existing);
                    }
                }
                else
                {
                    toInsert.Add(item);
                    identityIndex.Add(item);
                }
            }

            return (toInsert, toUpdate);
        }

        internal static List<List<DIC_一品多码表>> BuildProductSetCodeSourcePages(
            IEnumerable<DIC_一品多码表> sourceRows,
            int pageSize
        )
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
            var orderedGroups = sourceRows
                .GroupBy(
                    row =>
                        ProductSetCodeIdentityResolver.Normalize(row.H商品编码)
                        ?? $"(空):{row.ID}",
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(group => group
                    .OrderBy(row => row.FGC_LastModifyDate)
                    .ThenBy(row => row.ID)
                    .ToList())
                .OrderBy(group => group[0].FGC_LastModifyDate)
                .ThenBy(group => group[0].ID)
                .ToList();
            var pages = new List<List<DIC_一品多码表>>();
            var currentPage = new List<DIC_一品多码表>();
            foreach (var group in orderedGroups)
            {
                if (currentPage.Count > 0 && currentPage.Count + group.Count > pageSize)
                {
                    pages.Add(currentPage);
                    currentPage = new List<DIC_一品多码表>();
                }

                currentPage.AddRange(group);
                if (currentPage.Count >= pageSize)
                {
                    // 单个父商品可以超过页大小，但绝不能拆到两个本地事务。
                    pages.Add(currentPage);
                    currentPage = new List<DIC_一品多码表>();
                }
            }
            if (currentPage.Count > 0)
            {
                pages.Add(currentPage);
            }
            return pages;
        }

        internal static (
            List<DIC_一品多码表> AcceptedRows,
            HashSet<string> ConflictProductCodes,
            List<string> Errors
        ) ValidateProductSetCodeSourcePageSnapshot(
            IReadOnlyList<DIC_一品多码表> expectedRows,
            IReadOnlyList<DIC_一品多码表> actualRows
        )
        {
            var actualById = actualRows
                .GroupBy(row => row.ID)
                .ToDictionary(group => group.Key, group => group.First());
            var acceptedRows = new List<DIC_一品多码表>();
            var conflictProductCodes = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            var errors = new List<string>();

            foreach (var expected in expectedRows)
            {
                var expectedProductCode = ProductSetCodeIdentityResolver.Normalize(
                    expected.H商品编码
                );
                if (!actualById.TryGetValue(expected.ID, out var actual))
                {
                    if (expectedProductCode != null)
                    {
                        conflictProductCodes.Add(expectedProductCode);
                    }
                    errors.Add(
                        $"HQ ProductSetCode 快照变化：预检后的 HQ ID={expected.ID} 已不存在；父商品={expectedProductCode ?? "(空)"}，该商品组已跳过"
                    );
                    continue;
                }

                var expectedIdentity = ProductSetCodeIdentityResolver.CreateIdentity(
                    expected.HGUID,
                    expected.H商品编码,
                    expected.H多码商品编号
                );
                var actualIdentity = ProductSetCodeIdentityResolver.CreateIdentity(
                    actual.HGUID,
                    actual.H商品编码,
                    actual.H多码商品编号
                );
                var identityUnchanged = string.Equals(
                        expectedIdentity.Guid,
                        actualIdentity.Guid,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        expectedIdentity.BusinessKey,
                        actualIdentity.BusinessKey,
                        StringComparison.OrdinalIgnoreCase
                    );
                if (identityUnchanged)
                {
                    acceptedRows.Add(actual);
                    continue;
                }

                var actualProductCode = ProductSetCodeIdentityResolver.Normalize(
                    actual.H商品编码
                );
                if (expectedProductCode != null)
                {
                    conflictProductCodes.Add(expectedProductCode);
                }
                if (actualProductCode != null)
                {
                    conflictProductCodes.Add(actualProductCode);
                }
                var expectedKey = expectedIdentity.BusinessKey == null
                    ? "(空)"
                    : ProductSetCodeIdentityResolver.FormatBusinessKey(
                        expectedIdentity.BusinessKey
                    );
                var actualKey = actualIdentity.BusinessKey == null
                    ? "(空)"
                    : ProductSetCodeIdentityResolver.FormatBusinessKey(
                        actualIdentity.BusinessKey
                    );
                errors.Add(
                    $"HQ ProductSetCode 快照变化：HQ ID={expected.ID} 的身份在预检后改变，原 GUID={expectedIdentity.Guid ?? "(空)"}、业务键={expectedKey}，现 GUID={actualIdentity.Guid ?? "(空)"}、业务键={actualKey}；相关商品组已跳过"
                );
            }

            acceptedRows = acceptedRows
                .Where(row =>
                {
                    var productCode = ProductSetCodeIdentityResolver.Normalize(
                        row.H商品编码
                    );
                    return productCode != null
                        && !conflictProductCodes.Contains(productCode);
                })
                .ToList();
            return (acceptedRows, conflictProductCodes, errors);
        }

        private static bool IsCrossParentGuidOnly(
            ProductSetCodeIdentityResolution resolution,
            string? incomingProductCode
        )
        {
            return resolution.Kind == ProductSetCodeIdentityMatchKind.GuidOnly
                && !string.Equals(
                    ProductSetCodeIdentityResolver.Normalize(
                        resolution.MatchedRow?.ProductCode
                    ),
                    ProductSetCodeIdentityResolver.Normalize(incomingProductCode),
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static string GetSetCodeBusinessKey(ProductSetCode item) =>
            $"{item.ProductCode?.Trim()}\u001F{item.SetProductCode?.Trim()}";

        private static string GetStoreMultiCodeBusinessKey(StoreMultiCodeProduct item) =>
            $"{item.ProductCode?.Trim()}\u001F{item.MultiCodeProductCode?.Trim()}";

        private static string GetStoreMultiCodeIdentityKey(StoreMultiCodeProduct item) =>
            $"{item.StoreCode?.Trim()}\u001F{item.ProductCode?.Trim()}\u001F{item.MultiCodeProductCode?.Trim()}";

        private static async Task<HashSet<string>> GetProtectedSetCodeIdsAsync(
            ISqlSugarClient db,
            IReadOnlyCollection<string>? productCodes = null
        )
        {
            var query = db.Queryable<ProductSetCode>()
                // GUID 和父子业务键都保护所有状态的 Type1，禁止 HQ 将停用/软删除套装降级为 Type2。
                .Where(item => item.SetType == 1);
            if (productCodes is { Count: > 0 })
            {
                query = query.Where(item => productCodes.Contains(item.ProductCode));
            }

            var protectedIds = await query.Select(item => item.SetCodeId).ToListAsync();
            return new HashSet<string>(
                protectedIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase
            );
        }

        private static async Task<HashSet<string>> GetProtectedSetCodeKeysAsync(
            ISqlSugarClient db,
            IReadOnlyCollection<string>? productCodes = null
        )
        {
            var query = db.Queryable<ProductSetCode>()
                .Where(item => item.SetType == 1);
            if (productCodes is { Count: > 0 })
            {
                query = query.Where(item => productCodes.Contains(item.ProductCode));
            }
            var protectedRows = await query
                .Select(item => new { item.ProductCode, item.SetProductCode })
                .ToListAsync();

            return new HashSet<string>(
                protectedRows.Select(item =>
                    $"{item.ProductCode?.Trim()}\u001F{item.SetProductCode?.Trim()}"
                ),
                StringComparer.OrdinalIgnoreCase
            );
        }

        private static async Task<HashSet<string>> GetProtectedStoreMultiCodeKeysAsync(
            ISqlSugarClient db,
            IReadOnlyCollection<string>? productCodes = null
        )
        {
            var query = db.Queryable<ProductSetCode>()
                .Where(item => item.SetType == 1);
            if (productCodes is { Count: > 0 })
            {
                query = query.Where(item => productCodes.Contains(item.ProductCode));
            }
            var protectedRows = await query
                .Select(item => new { item.ProductCode, item.SetProductCode })
                .ToListAsync();

            return new HashSet<string>(
                protectedRows.Select(item =>
                    $"{item.ProductCode?.Trim()}\u001F{item.SetProductCode?.Trim()}"
                ),
                StringComparer.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// 增量同步分店清货价：DIC_商品清货价表 → StoreClearancePrice
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步，支持分店筛选
        /// </summary>
        public async Task<SyncResult> SyncStoreClearancePricesFromHqIncrementalAsync(
            List<string>? selectedStoreCodes = null,
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncStoreClearancePricesIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 分店清货价增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 清货价增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncStoreClearancePricesIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 清货价增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 清货价增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }
                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【分店清货价增量同步】
                // 数据源：HQ.dbo.DIC_商品清货价表
                // 目标表：HBweb.dbo.StoreClearancePrice
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                // 支持按分店筛选：selectedStoreCodes
                var query = hqDb.Queryable<DIC_商品清货价表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart);

                if (selectedStoreCodes?.Any() == true)
                    query = query.Where(x => selectedStoreCodes.Contains(x.分店代码!));

                var total = await query.CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 清货价增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<DIC_商品清货价表>()
                        .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                        .WhereIF(
                            selectedStoreCodes?.Any() == true,
                            x => selectedStoreCodes!.Contains(x.分店代码!)
                        )
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<StoreClearancePrice>>(batch);

                    var toInsert = localBatch
                        .Where(x => x.StoreCode != null && x.ProductCode != null)
                        .ToList();
                    var existingGuids = await _localContext
                        .Db.Queryable<StoreClearancePrice>()
                        .Where(x =>
                            toInsert
                                .Select(i => i.StoreCode + "-" + i.ProductCode)
                                .Contains(x.StoreCode + "-" + x.ProductCode)
                        )
                        .Select(x => x.StoreCode + "-" + x.ProductCode)
                        .ToListAsync();
                    var existingSet = new HashSet<string>(existingGuids);

                    var toUpdate = toInsert
                        .Where(x => existingSet.Contains(x.StoreCode + "-" + x.ProductCode))
                        .ToList();
                    toInsert = toInsert
                        .Where(x => !existingSet.Contains(x.StoreCode + "-" + x.ProductCode))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<StoreClearancePrice>()
                                .AS("StoreClearancePrice")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<StoreClearancePrice>()
                                .AS("StoreClearancePrice")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 清货价增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 清货价增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 分店清货价增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步国货套装：CPT_DIC_商品套装信息表 → DomesticSetProduct
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncDomesticSetProductsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncDomesticSetProductsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 国货套装增量同步：开始");

                if (startDateFromRequest.HasValue)
                {
                    _logger.LogInformation(
                        "[ReactSync] 国货套装增量：使用请求指定起始日期: {Time}（本同步为全量，日期仅作记录）",
                        startDateFromRequest.Value
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncDomesticSetProductsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 国货套装增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 国货套装增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }
                }

                if (!(await _hbSalesContext.TestConnectionAsync()))
                {
                    throw new Exception("HBSales 数据库连接失败");
                }

                // 【国货套装增量同步】
                // 数据源：HBSales.dbo.CPT_DIC_商品套装信息表
                // 目标表：HBweb.dbo.DomesticSetProduct
                // 同步策略：全量同步（此表无时间戳字段）
                var total = await _hbSalesContext
                    .Db.Queryable<CPT_DIC_商品套装信息表>()
                    .CountAsync();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 国货套装增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingCodes = await _localContext
                    .Db.Queryable<DomesticSetProduct>()
                    .Select(x => x.SetProductCode)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingCodes);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var batch = await _hbSalesContext
                        .Db.Queryable<CPT_DIC_商品套装信息表>()
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<DomesticSetProduct>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.SetProductCode!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.SetProductCode!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<DomesticSetProduct>()
                                .AS("DomesticSetProduct")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<DomesticSetProduct>()
                                .AS("DomesticSetProduct")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 国货套装增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 国货套装增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 国货套装增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步商品前缀码：CPT_DIC_货号前缀信息表 → ProductPrefixCode
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncProductPrefixCodesFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncProductPrefixCodesIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 商品前缀码增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 前缀码增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncProductPrefixCodesIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 前缀码增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 前缀码增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }
                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【货号前缀码增量同步】
                // 数据源：HQ.dbo.CPT_DIC_货号前缀信息表
                // 目标表：HBweb.dbo.ProductPrefixCode
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await hqDb.Queryable<CPT_DIC_货号前缀信息表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 前缀码增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingCodes = await _localContext
                    .Db.Queryable<ProductPrefixCode>()
                    .Select(x => x.PrefixCode)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingCodes);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<CPT_DIC_货号前缀信息表>()
                        .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<ProductPrefixCode>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.PrefixCode!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.PrefixCode!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<ProductPrefixCode>()
                                .AS("ProductPrefixCode")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<ProductPrefixCode>()
                                .AS("ProductPrefixCode")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 前缀码增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 前缀码增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 商品前缀码增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步进货单详情：RED_进货单详情表Store → StoreLocalSupplierInvoiceDetails
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncStoreLocalSupplierInvoiceDetailsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncStoreLocalSupplierInvoiceDetailsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 进货单详情增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 进货单详情增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncStoreLocalSupplierInvoiceDetailsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 进货单详情增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 进货单详情增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var total = await hqDb.Queryable<RED_进货单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.H主表GUID))
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 进货单详情增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingGuids = await _localContext
                    .Db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Select(x => x.DetailGUID)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingGuids);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<RED_进货单详情表Store>()
                        .Where(x => SqlFunc.HasValue(x.H主表GUID))
                        .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<StoreLocalSupplierInvoiceDetails>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.DetailGUID!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.DetailGUID!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<StoreLocalSupplierInvoiceDetails>()
                                .AS("StoreLocalSupplierInvoiceDetails")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<StoreLocalSupplierInvoiceDetails>()
                                .AS("StoreLocalSupplierInvoiceDetails")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 进货单详情增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 进货单详情增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 进货单详情增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// 增量同步仓库订单详情：CBP_RED_分店订单详情表Store → WareHouseOrderDetails
        /// 基于最近一次成功同步的时间点，默认100天内进行增量同步
        /// </summary>
        public async Task<SyncResult> SyncWareHouseOrderDetailsFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncWareHouseOrderDetailsIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 仓库订单详情增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 仓库订单详情增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncWareHouseOrderDetailsIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 仓库订单详情增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 仓库订单详情增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 【仓库订单详情增量同步】
                // 数据源：HQ.dbo.CBP_RED_分店订单详情表Store
                // 目标表：HBweb.dbo.WareHouseOrderDetails
                // 同步策略：按 FGC_LastModifyDate 字段增量同步
                var total = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.主表GUID))
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 仓库订单详情增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingGuids = await _localContext
                    .Db.Queryable<WareHouseOrderDetails>()
                    .Select(x => x.DetailGUID)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingGuids);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<CBP_RED_分店订单详情表Store>()
                        .Where(x => SqlFunc.HasValue(x.主表GUID))
                        .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<WareHouseOrderDetails>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.DetailGUID!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.DetailGUID!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<WareHouseOrderDetails>()
                                .AS("WareHouseOrderDetails")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<WareHouseOrderDetails>()
                                .AS("WareHouseOrderDetails")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 仓库订单详情增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 仓库订单详情增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 仓库订单详情增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        public async Task<SyncResult> SyncProductCategoriesFromHqIncrementalAsync(
            DateTime? startDateFromRequest = null
        )
        {
            var result = new SyncResult();
            var taskLog = await _taskLogService.LogTaskStartAsync(
                "SyncProductCategoriesIncremental",
                new TaskParameters(),
                TaskTrigger.Manual
            );

            try
            {
                _logger.LogInformation("[ReactSync] 商品分类增量同步：开始");

                DateTime effectiveStart;
                if (startDateFromRequest.HasValue)
                {
                    effectiveStart = startDateFromRequest.Value;
                    _logger.LogInformation(
                        "[ReactSync] 商品分类增量：使用请求指定起始日期: {Time}",
                        effectiveStart
                    );
                }
                else
                {
                    var recentTasks = await _taskLogService.GetRecentTasksAsync(
                        1,
                        "SyncProductCategoriesIncremental"
                    );
                    var lastSuccessTask = recentTasks.FirstOrDefault(t =>
                        t.Status == BlazorApp.Shared.Models.HBweb.TaskStatus.Success
                    );

                    DateTime? syncStartTime = lastSuccessTask?.StartedAt;
                    var daysRange = 30;

                    if (syncStartTime.HasValue)
                    {
                        daysRange = Math.Min(
                            daysRange,
                            (int)(DateTime.UtcNow - syncStartTime.Value).TotalDays
                        );
                        _logger.LogInformation(
                            "[ReactSync] 商品分类增量：上次成功同步时间: {Time}, 范围: {Days} 天",
                            syncStartTime,
                            daysRange
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[ReactSync] 商品分类增量：未找到历史记录，同步最近 {Days} 天的数据",
                            daysRange
                        );
                    }

                    var startDate = DateTime.UtcNow.AddDays(-daysRange);
                    effectiveStart = startDate;
                }

                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var total = await hqDb.Queryable<DIC_商品分类码表>()
                    .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                    .CountAsync();
                hqDb.Dispose();

                if (total == 0)
                {
                    _logger.LogInformation("[ReactSync] 商品分类增量：没有新数据需要同步");
                    result.IsSuccess = true;
                    result.Message = "没有新数据需要同步";
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    return result;
                }

                const int hqBatchSize = 50000;
                const int writePageSize = 10000;
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var updated = 0;
                var errors = 0;

                var existingGuids = await _localContext
                    .Db.Queryable<ProductCategory>()
                    .Select(x => x.CategoryGUID)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingGuids);

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var batch = await hqDb.Queryable<DIC_商品分类码表>()
                        .Where(x => x.FGC_LastModifyDate >= effectiveStart)
                        .OrderBy(x => x.FGC_LastModifyDate)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    hqDb.Dispose();

                    if (!batch.Any())
                        continue;

                    var localBatch = _mapper.Map<List<ProductCategory>>(batch);

                    var toInsert = localBatch
                        .Where(x => !existingSet.Contains(x.CategoryGUID!))
                        .ToList();
                    var toUpdate = localBatch
                        .Where(x => existingSet.Contains(x.CategoryGUID!))
                        .ToList();

                    try
                    {
                        if (toUpdate.Any())
                        {
                            await _localContext
                                .Db.Fastest<ProductCategory>()
                                .AS("ProductCategory")
                                .PageSize(writePageSize)
                                .BulkUpdateAsync(toUpdate);
                            updated += toUpdate.Count;
                        }
                        if (toInsert.Any())
                        {
                            await _localContext
                                .Db.Fastest<ProductCategory>()
                                .AS("ProductCategory")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(toInsert);
                            added += toInsert.Count;
                        }
                        _logger.LogInformation(
                            "[ReactSync] 商品分类增量页{Page}: 插入{Inserted}, 更新{Updated}",
                            page,
                            toInsert.Count,
                            toUpdate.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 商品分类增量页{Page}出错: {Error}",
                            page,
                            ex.Message
                        );
                    }
                }

                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? $"增量同步完成，新增 {added} 条，更新 {updated} 条"
                        : $"增量同步部分完成，新增 {added} 条，更新 {updated} 条，{errors} 页出错";
                result.AddedCount = added;
                result.UpdatedCount = updated;
                result.ErrorCount = errors;

                if (result.IsSuccess)
                    await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                else
                    await _taskLogService.LogTaskFailureAsync(taskLog.Id, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 商品分类增量同步异常: {Error}", ex.Message);
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                result.IsSuccess = false;
                result.Message = $"同步失败: {ex.Message}";
                return result;
            }
        }

        private async Task<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>
            CaptureChangeSnapshotsAsync(IEnumerable<string> productCodes)
        {
            return await _changeHistoryService.CaptureSnapshotsAsync(productCodes);
        }

        private async Task RecordChangeHistoryAsync(
            IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> beforeSnapshots,
            IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> afterSnapshots,
            string action,
            string source,
            Guid batchGuid,
            DateTime? occurredAtUtc = null
        )
        {
            // 历史操作人只能由服务端当前请求上下文提供，计划任务会自然回退为 System。
            var actorUserGuid = _currentUserService.GetCurrentUserGuid();
            var actorName = _currentUserService.GetCurrentUsername();
            // 是否是系统任务只由用户 GUID 决定；显示名缺失时由统一历史服务使用 GUID 回退。
            var isSystem = string.IsNullOrWhiteSpace(actorUserGuid);
            await _changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                new WarehouseProductChangeHistoryContextDto
                {
                    Action = action,
                    Source = source,
                    BatchGuid = batchGuid,
                    ActorUserGuid = isSystem ? null : actorUserGuid,
                    ActorName = isSystem ? "System" : actorName,
                    ActorType = isSystem ? "System" : "User",
                    OccurredAtUtc = occurredAtUtc,
                }
            );
        }
    }
}
