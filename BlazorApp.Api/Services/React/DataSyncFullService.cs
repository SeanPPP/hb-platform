using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using BlazorApp.Shared.Models.POSM;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.SqlClient;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class DataSyncFullService : IDataSyncFullService
    {
        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly HBSalesSqlSugarContext _hbSalesContext;
        private readonly POSMSqlSugarContext _posmContext;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ILogger<DataSyncFullService> _logger;
        private readonly ScheduledTaskLogService _taskLogService;

        public DataSyncFullService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            HBSalesSqlSugarContext hbSalesContext,
            POSMSqlSugarContext posmContext,
            IConfiguration configuration,
            IMapper mapper,
            ILogger<DataSyncFullService> logger,
            ScheduledTaskLogService taskLogService
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _hbSalesContext = hbSalesContext;
            _posmContext = posmContext;
            _configuration = configuration;
            _mapper = mapper;
            _logger = logger;
            _taskLogService = taskLogService;
        }

        /// <summary>
        /// 全量同步商品：HQ 商品字典表 → 本地 Product 表
        /// 流程：
        /// 1) 检查 HQ 连接；
        /// 2) 开启本地事务并清空 Product；
        /// 3) 分页（50,000）拉取 HQ 数据，转换为 Product 后以 10,000 批量写入；
        /// 4) 统计新增/错误数与耗时，提交事务；异常则回滚。
        /// </summary>
        public async Task<SyncResult> SyncProductsFromHqAsync()
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            try
            {
                // 检查 HQ 数据库连接是否可用
                _logger.LogInformation("[ReactSync] 商品同步：检查HQ连接");
                _hqContext.CheckConnection();

                // 开启本地事务，确保清空与写入的一致性
                await _localContext.Db.Ado.BeginTranAsync();

                try
                {
                    // 为保证全量同步的正确性，先清空本地 Product 表
                    _logger.LogInformation("[ReactSync] 清空本地 Product 表");
                    await _localContext
                        .Db.Deleteable<Product>()
                        .AS("Product")
                        .ExecuteCommandAsync();
                    const int batchSize = 20000;
                    const int writePageSize = 5000;
                    const int maxConcurrency = 8;

                    var hqCountDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var totalCount = await hqCountDb.Queryable<DIC_商品信息字典表>().CountAsync();
                    hqCountDb.Dispose();
                    var pageCount = (int)Math.Ceiling(totalCount / (double)batchSize);

                    var channel = System.Threading.Channels.Channel.CreateBounded<List<Product>>(
                        capacity: Math.Min(12, Math.Max(4, maxConcurrency * 2))
                    );

                    var totalAdded = 0;
                    var totalErrors = 0;
                    var fetchErrors = 0;

                    // 消费者：单线程在事务中批量插入
                    var consumer = Task.Run(async () =>
                    {
                        await foreach (var localBatch in channel.Reader.ReadAllAsync())
                        {
                            try
                            {
                                await _localContext
                                    .Db.Fastest<Product>()
                                    .AS("Product")
                                    .PageSize(writePageSize)
                                    .BulkCopyAsync(localBatch);
                                totalAdded += localBatch.Count;
                                _logger.LogInformation(
                                    "[ReactSync] 商品第{Page}批：{Count}",
                                    localBatch,
                                    localBatch.Count
                                );
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "[ReactSync] 商品批量插入失败（批大小:{Size}）",
                                    localBatch.Count
                                );
                                totalErrors += localBatch.Count;
                                await Task.Delay(1500);
                            }
                        }
                    });

                    // 生产者：并发分页读取+映射
                    var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                    _logger.LogInformation(
                        "[ReactSync] 商品并发读取初始化：Total={Total}, Pages={Pages}, BatchSize={Batch}",
                        totalCount,
                        pageCount,
                        batchSize
                    );

                    var producers = new List<Task>();
                    for (var page = 1; page <= pageCount; page++)
                    {
                        var pageIndex = page; // 关键：在闭包外捕获当前页索引，避免并发闭包变量问题
                        var p = Task.Run(async () =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                var skip = (pageIndex - 1) * batchSize;
                                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(
                                    _configuration
                                );
                                var hqBatch = await hqDb.Queryable<DIC_商品信息字典表>()
                                    .Skip(skip)
                                    .Take(batchSize)
                                    .ToListAsync();

                                if (hqBatch.Any())
                                {
                                    _logger.LogInformation(
                                        "[ReactSync] 商品第{Page}批：{Count}",
                                        pageIndex,
                                        hqBatch.Count
                                    );
                                    var localBatch = _mapper.Map<List<Product>>(hqBatch);
                                    await channel.Writer.WriteAsync(localBatch);
                                }
                                hqDb.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "[ReactSync] 商品第{Page}批查询/映射失败",
                                    pageIndex
                                );
                                fetchErrors++;
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });
                        producers.Add(p);
                    }

                    // 等待生产结束并关闭通道
                    await Task.WhenAll(producers);
                    channel.Writer.Complete();
                    await consumer; // 消费完毕

                    // 提交事务：清空 + 批量写入整体成功
                    await _localContext.Db.Ado.CommitTranAsync();

                    result.AddedCount = totalAdded;
                    result.ErrorCount = totalErrors + fetchErrors;
                    result.IsSuccess = (totalErrors == 0 && fetchErrors == 0);
                    result.Message = totalErrors == 0 ? "商品同步成功" : "商品同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    // 异常时回滚事务，保证数据一致性
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("商品同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 商品同步异常");
                result.IsSuccess = false;
                result.Message = "商品同步异常";
            }
            finally
            {
                // 记录耗时统计
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 全量同步分店零售价（按分店并发）：HQ 价格表 → 本地 StoreRetailPrice
        /// 流程：
        /// 1) 选定分店列表（为空则扫描 HQ 有数据的分店）；
        /// 2) 清理本地目标分店历史价格数据（避免重复累计）；
        /// 3) 使用 SemaphoreSlim 控并发，为每个分店创建独立 HQ/本地连接；
        /// 4) 分店级 JOIN 过滤有效商品与价格，仅写入有效记录；
        /// 5) 批量写入 + 重试机制，汇总统计新增/错误与耗时。
        /// </summary>
        public async Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int? maxConcurrency = null,
            int? batchSize = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            var effectiveMaxConcurrency =
                maxConcurrency ?? _configuration.GetValue<int>("Database:SyncMaxConcurrency", 5);
            var effectiveBatchSize =
                batchSize ?? _configuration.GetValue<int>("Database:SyncBatchSize", 50000);
            var actualBatchSize = effectiveBatchSize;

            // 并发控制信号量：限制并发分店数，避免数据库压力过大
            var semaphore = new SemaphoreSlim(effectiveMaxConcurrency, effectiveMaxConcurrency);
            // 进度锁：跨任务汇总统计时保持原子性
            var progressLock = new object();
            var storeErrors = new List<StoreSyncError>();

            var totalProcessed = 0;
            var totalAdded = 0;
            var totalErrors = 0;
            var totalQueryTime = 0.0;
            var totalInsertTime = 0.0;

            try
            {
                List<string> storeCodesToProcess;
                if (selectedStoreCodes?.Any() == true)
                {
                    // 使用前端传入的分店列表
                    storeCodesToProcess = selectedStoreCodes;
                }
                else
                {
                    // 扫描 HQ：查询存在有效价格且商品可用的分店代码
                    var startQuery = DateTime.Now;
                    storeCodesToProcess = await _hqContext
                        .Db.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                            (price, product) =>
                                new JoinQueryInfos(
                                    JoinType.Inner,
                                    price.H商品编码 == product.H商品编码
                                )
                        )
                        .Where(
                            (price, product) =>
                                !string.IsNullOrEmpty(price.H商品编码)
                                && !string.IsNullOrEmpty(price.H分店代码)
                                && price.H使用状态 == true
                                && product.H使用状态 == true
                        )
                        .GroupBy((price, product) => price.H分店代码)
                        .Select((price, product) => price.H分店代码)
                        .ToListAsync();
                    var duration = DateTime.Now - startQuery;
                    _logger.LogInformation(
                        "[ReactSync] 发现分店{Count}个，查询耗时{Seconds:F1}s",
                        storeCodesToProcess.Count,
                        duration.TotalSeconds
                    );
                }

                if (!storeCodesToProcess.Any())
                {
                    // 无需同步，直接返回成功
                    result.IsSuccess = true;
                    result.Message = "未发现需要同步的分店";
                    return result;
                }

                // 仅清理目标分店的历史价格数据，避免数据重复（大批量删除使用更长超时）
                var deleteStart = DateTime.Now;
                var prevTimeout = _localContext.Db.Ado.CommandTimeOut;
                int deleted = 0;
                try
                {
                    _localContext.Db.Ado.CommandTimeOut = _configuration.GetValue<int>(
                        "Database:BulkCopyCommandTimeoutSeconds",
                        600
                    );
                    deleted = await _localContext
                        .Db.Deleteable<StoreRetailPrice>()
                        .Where(x =>
                            x.StoreCode != null && storeCodesToProcess.Contains(x.StoreCode)
                        )
                        .ExecuteCommandAsync();
                }
                finally
                {
                    _localContext.Db.Ado.CommandTimeOut = prevTimeout;
                }

                var deleteDuration = DateTime.Now - deleteStart;
                _logger.LogInformation(
                    "[ReactSync] 清理本地分店数据：删除{Deleted:N0}条，耗时{Seconds:F1}s",
                    deleted,
                    deleteDuration.TotalSeconds
                );

                // 按分店并发执行同步任务
                var tasks =
                    new List<
                        Task<(
                            int processed,
                            int added,
                            int errors,
                            double queryTime,
                            double insertTime,
                            StoreSyncError? error
                        )>
                    >();
                var completedStores = 0;
                var processStart = DateTime.Now;

                for (int i = 0; i < storeCodesToProcess.Count; i++)
                {
                    var storeCode = storeCodesToProcess[i];
                    // 使用线程池并发执行，确保每分店独立连接与独立写入
                    var t = Task.Run(async () =>
                        await ProcessSingleStoreAsync(i, storeCode, effectiveBatchSize, semaphore)
                    );
                    tasks.Add(t);

                    if (tasks.Count >= effectiveMaxConcurrency)
                    {
                        // 达到并发上限后收集一个已完成任务，释放窗口
                        var done = await Task.WhenAny(tasks);
                        tasks.Remove(done);
                        var r = await done;
                        lock (progressLock)
                        {
                            totalProcessed += r.processed;
                            totalAdded += r.added;
                            totalErrors += r.errors;
                            totalQueryTime += r.queryTime;
                            totalInsertTime += r.insertTime;
                            if (r.error != null)
                            {
                                storeErrors.Add(r.error);
                            }
                            completedStores++;
                        }
                    }
                }

                // 等待剩余任务完成并汇总
                var rest = await Task.WhenAll(tasks);
                foreach (var r in rest)
                {
                    lock (progressLock)
                    {
                        totalProcessed += r.processed;
                        totalAdded += r.added;
                        totalErrors += r.errors;
                        totalQueryTime += r.queryTime;
                        totalInsertTime += r.insertTime;
                        completedStores++;
                    }
                }

                result.AddedCount = totalAdded;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    totalErrors == 0 ? "分店零售价同步成功" : "分店零售价同步完成，但存在错误";

                var finalDuration = DateTime.Now - processStart;
                result.EndTime = DateTime.Now;
                result.Duration = finalDuration;

                result.TotalCount = totalProcessed;
                result.TotalStores = storeCodesToProcess.Count;
                result.SuccessStores = completedStores - storeErrors.Count;
                result.FailedStores = storeErrors.Count;
                result.StoreErrors = storeErrors;

                var successRate = result.TotalCount > 0
                    ? (double)result.SuccessCount / result.TotalCount * 100
                    : 0;

                result.Message = totalErrors == 0
                    ? $"分店零售价同步成功：共处理{result.TotalCount:N0}条记录，{result.SuccessStores}个分店全部成功，成功率{successRate:F1}%"
                    : $"分店零售价同步完成：共处理{result.TotalCount:N0}条记录，成功{result.SuccessCount:N0}条，失败{result.ErrorCount:N0}条，{result.SuccessStores}个分店成功，{result.FailedStores}个分店失败，成功率{successRate:F1}%";

                if (storeErrors.Any())
                {
                    var errorDetails = storeErrors
                        .Select(e => $"分店{e.StoreCode}: {e.ErrorMessage} (处理{e.ProcessedCount:N0}条, 成功{e.InsertedCount:N0}条, 耗时{e.DurationSeconds:F1}s)")
                        .ToList();

                    result.Details = "错误分店列表：\n" + string.Join("\n", errorDetails);
                }

                _logger.LogInformation(
                    "[ReactSync] 分店同步完成：门店{StoreCount} 记录{Processed:N0} 成功{Added:N0} 失败{Errors:N0} 耗时{Seconds:F1}s 成功率{Rate:F1}%",
                    storeCodesToProcess.Count,
                    totalProcessed,
                    totalAdded,
                    totalErrors,
                    finalDuration.TotalSeconds,
                    successRate
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 分店零售价同步异常");
                result.IsSuccess = false;
                result.Message = "分店零售价同步异常";
            }
            finally
            {
                // 释放并发控制资源并记录耗时
                semaphore.Dispose();
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 处理单个分店的同步：
        /// - 独立创建 HQ 与本地数据库连接；
        /// - JOIN 过滤有效商品与价格；
        /// - 映射为本地实体并批量插入（带重试）；
        /// - 返回该分店的处理统计与耗时。
        /// </summary>
        private async Task<(
            int processed,
            int added,
            int errors,
            double queryTime,
            double insertTime,
            StoreSyncError? error
        )> ProcessSingleStoreAsync(
            int storeIndex,
            string storeCode,
            int batchSize,
            SemaphoreSlim semaphore
        )
        {
            // 使用信号量限制并发，避免超出最大并发数
            await semaphore.WaitAsync();

            ISqlSugarClient? localDb = null;
            ISqlSugarClient? hqDb = null;
            var processed = 0;
            var added = 0;
            var errors = 0;
            var storeStart = DateTime.Now;

            try
            {
                // 为当前分店创建独立的本地与 HQ 连接，避免连接复用导致争用
                localDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 分店级总数
                var countStart = DateTime.Now;
                var totalCount = await hqDb.Queryable<DIC_商品零售价表>()
                    .Where(price =>
                        !string.IsNullOrEmpty(price.H商品编码)
                        && price.H分店代码 == storeCode
                        && price.H使用状态 == true
                        && SqlFunc
                            .Subqueryable<DIC_商品信息字典表>()
                            .Where(product =>
                                product.H商品编码 == price.H商品编码 && product.H使用状态 == true
                            )
                            .Any()
                    )
                    .CountAsync();
                var qDuration = DateTime.Now - countStart;

                if (totalCount == 0)
                {
                    _logger.LogWarning("[ReactSync] 分店{Store}无数据", storeCode);
                    return (0, 0, 0, qDuration.TotalSeconds, 0, null);
                }

                var pages = (int)Math.Ceiling(totalCount / (double)batchSize);
                var insertTotalSeconds = 0.0;

                for (var page = 1; page <= pages; page++)
                {
                    // 每页查询重试
                    var retries = 0;
                    List<DIC_商品零售价表> hqPage = new();
                    while (true)
                    {
                        try
                        {
                            var skip = (page - 1) * batchSize;
                            var pageStart = DateTime.Now;
                            hqPage = await hqDb.Queryable<DIC_商品零售价表>()
                                .Where(price =>
                                    !string.IsNullOrEmpty(price.H商品编码)
                                    && price.H分店代码 == storeCode
                                    && price.H使用状态 == true
                                    && SqlFunc
                                        .Subqueryable<DIC_商品信息字典表>()
                                        .Where(product =>
                                            product.H商品编码 == price.H商品编码
                                            && product.H使用状态 == true
                                        )
                                        .Any()
                                )
                                .OrderBy(price => price.ID)
                                .Skip(skip)
                                .Take(batchSize)
                                .ToListAsync();
                            var pageQuerySec = (DateTime.Now - pageStart).TotalSeconds;
                            _logger.LogInformation(
                                "[ReactSync] 分店{Store} 页{Page}/{Pages} 拉取{Count} 用时{Sec:F1}s",
                                storeCode,
                                page,
                                pages,
                                hqPage.Count,
                                pageQuerySec
                            );
                            break;
                        }
                        catch (SqlSugarException ex) when (retries < 3)
                        {
                            retries++;
                            var delay = TimeSpan.FromSeconds(Math.Pow(2, retries));
                            _logger.LogWarning(
                                ex,
                                "[ReactSync] 分店{Store} 页{Page} 查询失败，重试{Retry}/3，等待{Delay}s",
                                storeCode,
                                page,
                                retries,
                                delay.TotalSeconds
                            );
                            await Task.Delay(delay);
                            // 重建 HQ 连接
                            hqDb?.Dispose();
                            hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                            continue;
                        }
                    }

                    if (!hqPage.Any())
                    {
                        continue;
                    }

                    var localBatch = _mapper.Map<List<StoreRetailPrice>>(hqPage);
                    var insertStart = DateTime.Now;
                    try
                    {
                        var inserted = await RetryBulkInsertAsync(localDb, localBatch, 1);
                        added += inserted;
                        errors += localBatch.Count - inserted;

                        var insertDuration = DateTime.Now - insertStart;
                        _logger.LogInformation(
                            "[ReactSync] 分店{Store} 页{Page}/{Pages} 插入成功：{Inserted:N0}/{Total:N0}条，耗时{Sec:F1}s",
                            storeCode,
                            page,
                            pages,
                            inserted,
                            localBatch.Count,
                            insertDuration.TotalSeconds
                        );
                    }
                    catch (Exception ex)
                    {
                        var insertDuration = DateTime.Now - insertStart;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 分店{Store} 页{Page}/{Pages} 插入失败，耗时{Sec:F1}s",
                            storeCode,
                            page,
                            pages,
                            insertDuration.TotalSeconds
                        );
                        errors += localBatch.Count;
                    }
                    insertTotalSeconds += (DateTime.Now - insertStart).TotalSeconds;
                    processed += hqPage.Count;
                    hqPage.Clear();
                    localBatch.Clear();
                }

                var storeDuration = DateTime.Now - storeStart;
                _logger.LogInformation(
                    "[ReactSync] 分店{Store}完成：处理{Processed:N0} 成功{Added:N0} 失败{Errors:N0} 总耗时{Sec:F1}s 查询{Q:F1}s 插入{I:F1}s",
                    storeCode,
                    processed,
                    added,
                    errors,
                    storeDuration.TotalSeconds,
                    qDuration.TotalSeconds,
                    insertTotalSeconds
                );

                return (processed, added, errors, qDuration.TotalSeconds, insertTotalSeconds, null);
            }
            catch (Exception ex)
            {
                var error = new StoreSyncError
                {
                    StoreCode = storeCode,
                    ErrorMessage = ex.Message,
                    ExceptionType = ex.GetType().Name,
                    IsRetried = false,
                    RetryCount = 0,
                    ProcessedCount = processed,
                    InsertedCount = added,
                    FailedCount = errors,
                    DurationSeconds = (DateTime.Now - storeStart).TotalSeconds
                };

                _logger.LogError(ex, "[ReactSync] 分店{Store}处理异常", storeCode);
                return (0, 0, 1, 0, 0, error);
            }
            finally
            {
                // 释放连接与并发锁
                localDb?.Dispose();
                hqDb?.Dispose();
                semaphore.Release();
            }
        }

        /// <summary>
        /// 批量插入重试封装：动态批量大小 + 分级超时 + 指数退避
        /// 说明：
        /// - 使用 SqlSugar Fastest BulkCopy 提升吞吐；
        /// - 动态调整批量大小：超时时降级到更小的批量（10000→5000→2500→1000）；
        /// - 分级超时策略：重试时增加超时时间（600s→1200s→1800s）；
        /// - 指数退避：每次重试前等待（1s→2s→4s）；
        /// - 返回实际插入的记录数，便于上层统计。
        /// </summary>
        private async Task<int> RetryBulkInsertAsync<T>(
            ISqlSugarClient db,
            List<T> data,
            int maxRetries = 5,
            int? initialPageSize = null,
            int? timeoutSeconds = null
        )
            where T : class, new()
        {
            var defaultPageSize =
                initialPageSize
                ?? _configuration.GetValue<int>("Database:BulkCopyInitialPageSize", 5000);
            var defaultTimeout =
                timeoutSeconds
                ?? _configuration.GetValue<int>("Database:BulkCopyCommandTimeoutSeconds", 900);

            var dataCount = data.Count;
            var dynamicTimeout = Math.Max(defaultTimeout, (dataCount / 1000) * 60);

            var retries = 0;
            var pageSize = defaultPageSize;
            var currentTimeout = dynamicTimeout;
            int inserted = 0;

            while (retries < maxRetries)
            {
                try
                {
                    db.Ado.CommandTimeOut = currentTimeout;

                    for (int i = 0; i < data.Count; i += pageSize)
                    {
                        var batch = data.Skip(i).Take(pageSize).ToList();
                        await db.Fastest<T>().BulkCopyAsync(batch);
                        inserted += batch.Count;
                    }

                    return inserted;
                }
                catch (SqlException ex) when (ex.Number == -2 && retries < maxRetries - 1)
                {
                    retries++;
                    pageSize = Math.Max(
                        _configuration.GetValue<int>("Database:BulkCopyMinPageSize", 1000),
                        pageSize / 2
                    );
                    currentTimeout = Math.Min(currentTimeout * 2, 1800);

                    _logger.LogWarning(
                        ex,
                        "[ReactSync] 批量插入超时（PageSize={PageSize}, Timeout={Timeout}s），重试{Retry}/{Max}，新PageSize={NewPageSize}, 新Timeout={NewTimeout}s",
                        pageSize * 2,
                        currentTimeout / 2,
                        retries,
                        maxRetries,
                        pageSize,
                        currentTimeout
                    );

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                }
                catch (SqlException ex) when (ex.Number == 1205 && retries < maxRetries - 1)
                {
                    retries++;
                    pageSize = Math.Max(
                        _configuration.GetValue<int>("Database:BulkCopyMinPageSize", 1000),
                        pageSize / 2
                    );
                    currentTimeout = Math.Min(currentTimeout * 2, 1800);

                    _logger.LogWarning(
                        ex,
                        "[ReactSync] 批量插入死锁（PageSize={PageSize}, Timeout={Timeout}s），重试{Retry}/{Max}，新PageSize={NewPageSize}, 新Timeout={NewTimeout}s",
                        pageSize * 2,
                        currentTimeout / 2,
                        retries,
                        maxRetries,
                        pageSize,
                        currentTimeout
                    );

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                }
                catch (Exception ex) when (retries < maxRetries - 1)
                {
                    retries++;
                    currentTimeout = Math.Min(currentTimeout * 2, 1800);

                    _logger.LogWarning(
                        ex,
                        "[ReactSync] 批量插入失败（Timeout={Timeout}s），重试{Retry}/{Max}，新Timeout={NewTimeout}s：{Msg}",
                        currentTimeout / 2,
                        retries,
                        maxRetries,
                        currentTimeout,
                        ex.Message
                    );

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                }
                finally
                {
                    db.Ado.CommandTimeOut = _configuration.GetValue<int>(
                        "Database:ConcurrentCommandTimeoutSeconds",
                        300
                    );
                }
            }

            throw new Exception(
                $"批量插入最终失败：已重试 {maxRetries} 次，成功插入 {inserted}/{data.Count} 条记录"
            );
        }

        /// <summary>
        /// 全量同步分店清货价：HQ 清货价表 → 本地 StoreClearancePrice（串行批量写入）
        /// 支持按分店过滤；事务包裹清理与写入，失败回滚。
        /// </summary>
        public async Task<SyncResult> SyncStoreClearancePricesFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int? maxConcurrency = null,
            int? batchSize = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            var effectiveMaxConcurrency =
                maxConcurrency ?? _configuration.GetValue<int>("Database:SyncMaxConcurrency", 2);
            var effectiveBatchSize =
                batchSize ?? _configuration.GetValue<int>("Database:SyncBatchSize", 50000);
            var actualBatchSize = effectiveBatchSize;
            try
            {
                _hqContext.CheckConnection();

                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    // 本地清理：如果指定了分店，只清理这些分店；否则清理全部
                    var deleteStart = DateTime.Now;
                    int deleted;
                    if (selectedStoreCodes?.Any() == true)
                    {
                        deleted = await _localContext
                            .Db.Deleteable<StoreClearancePrice>()
                            .Where(x =>
                                x.StoreCode != null && selectedStoreCodes.Contains(x.StoreCode)
                            )
                            .ExecuteCommandAsync();
                    }
                    else
                    {
                        deleted = await _localContext
                            .Db.Deleteable<StoreClearancePrice>()
                            .ExecuteCommandAsync();
                    }
                    var deleteDuration = DateTime.Now - deleteStart;
                    _logger.LogInformation(
                        "[ReactSync] 清理清货价本地数据：删除{Deleted:N0}条，耗时{Seconds:F1}s",
                        deleted,
                        deleteDuration.TotalSeconds
                    );

                    // 全量（或按分店）拉取 HQ 清货价数据，分页串行写入
                    var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var hasStores = selectedStoreCodes != null && selectedStoreCodes.Count > 0;
                    var baseQuery = hqDb.Queryable<DIC_商品清货价表>()
                        .Where(c => !string.IsNullOrEmpty(c.商品编码))
                        .WhereIF(
                            hasStores,
                            c => SqlFunc.ContainsArray(selectedStoreCodes!, c.分店代码)
                        )
                        .Where(c =>
                            SqlFunc
                                .Subqueryable<DIC_商品信息字典表>()
                                .Where(product =>
                                    product.H商品编码 == c.商品编码 && product.H使用状态 == true
                                )
                                .Any()
                        );

                    var totalCount = await baseQuery.CountAsync();
                    if (totalCount == 0)
                    {
                        await _localContext.Db.Ado.CommitTranAsync();
                        result.IsSuccess = true;
                        result.Message = "未发现需要同步的清货价数据";
                        return result;
                    }

                    var pages = (int)Math.Ceiling(totalCount / (double)actualBatchSize);
                    var totalAdded = 0;
                    var totalErrors = 0;

                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * actualBatchSize;
                        var pageStart = DateTime.Now;
                        var hqPage = await baseQuery
                            .OrderBy(c => c.ID)
                            .Skip(skip)
                            .Take(actualBatchSize)
                            .ToListAsync();
                        var pageQuerySec = (DateTime.Now - pageStart).TotalSeconds;
                        _logger.LogInformation(
                            "[ReactSync] 清货价批{Page}/{Pages} 拉取{Count} 用时{Sec:F1}s",
                            page,
                            pages,
                            hqPage.Count,
                            (double)pageQuerySec
                        );

                        if (!hqPage.Any())
                            continue;

                        var localBatch = _mapper.Map<List<StoreClearancePrice>>(hqPage);
                        try
                        {
                            await _localContext
                                .Db.Fastest<StoreClearancePrice>()
                                .AS("StoreClearancePrice")
                                .PageSize(50000)
                                .BulkCopyAsync(localBatch);
                            totalAdded += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] 清货价批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            totalErrors += localBatch.Count;
                            await Task.Delay(1500);
                        }

                        hqPage.Clear();
                        localBatch.Clear();
                    }

                    hqDb.Dispose();
                    await _localContext.Db.Ado.CommitTranAsync();

                    result.AddedCount = totalAdded;
                    result.ErrorCount = totalErrors;
                    result.IsSuccess = totalErrors == 0;
                    result.Message =
                        totalErrors == 0 ? "分店清货价同步成功" : "分店清货价同步完成，但存在错误";

                    var finalDuration = DateTime.Now - result.StartTime;
                    result.EndTime = DateTime.Now;
                    result.Duration = finalDuration;

                    result.TotalCount = totalAdded + totalErrors;
                    result.TotalStores = 0;
                    result.SuccessStores = 0;
                    result.FailedStores = 0;

                    var successRate = result.TotalCount > 0
                        ? (double)result.SuccessCount / result.TotalCount * 100
                        : 0;

                    result.Message = totalErrors == 0
                        ? $"分店清货价同步成功：共处理{result.TotalCount:N0}条记录，成功率{successRate:F1}%"
                        : $"分店清货价同步完成：共处理{result.TotalCount:N0}条记录，成功{result.SuccessCount:N0}条，失败{result.ErrorCount:N0}条，成功率{successRate:F1}%";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("分店清货价同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 分店清货价同步异常");
                result.IsSuccess = false;
                result.Message = "分店清货价同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        private async Task<(
            int processed,
            int added,
            int errors,
            double queryTime,
            double insertTime,
            StoreSyncError? error
        )> ProcessSingleStoreClearanceAsync(
            int storeIndex,
            string storeCode,
            int batchSize,
            SemaphoreSlim semaphore
        )
        {
            await semaphore.WaitAsync();
            ISqlSugarClient? localDb = null;
            ISqlSugarClient? hqDb = null;
            var processed = 0;
            var added = 0;
            var errors = 0;
            var storeStart = DateTime.Now;
            try
            {
                storeStart = DateTime.Now;
                localDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var countStart = DateTime.Now;
                var totalCount = await hqDb.Queryable<DIC_商品清货价表>()
                    .Where(c =>
                        !string.IsNullOrEmpty(c.商品编码)
                        && c.分店代码 == storeCode
                        && SqlFunc
                            .Subqueryable<DIC_商品信息字典表>()
                            .Where(product =>
                                product.H商品编码 == c.商品编码 && product.H使用状态 == true
                            )
                            .Any()
                    )
                    .CountAsync();
                var qDuration = DateTime.Now - countStart;
                if (totalCount == 0)
                    return (0, 0, 0, qDuration.TotalSeconds, 0, null);

                var pages = (int)Math.Ceiling(totalCount / (double)batchSize);
                var insertTotalSeconds = 0.0;

                for (var page = 1; page <= pages; page++)
                {
                    var retries = 0;
                    List<DIC_商品清货价表> hqPage = new();
                    while (true)
                    {
                        try
                        {
                            var skip = (page - 1) * batchSize;
                            var pageStart = DateTime.Now;
                            hqPage = await hqDb.Queryable<DIC_商品清货价表>()
                                .Where(c =>
                                    !string.IsNullOrEmpty(c.商品编码)
                                    && c.分店代码 == storeCode
                                    && SqlFunc
                                        .Subqueryable<DIC_商品信息字典表>()
                                        .Where(product =>
                                            product.H商品编码 == c.商品编码
                                            && product.H使用状态 == true
                                        )
                                        .Any()
                                )
                                .OrderBy(c => c.ID)
                                .Skip(skip)
                                .Take(batchSize)
                                .ToListAsync();
                            var pageQuerySec = (DateTime.Now - pageStart).TotalSeconds;
                            _logger.LogInformation(
                                "[ReactSync] 分店{Store} 清货价 页{Page}/{Pages} 拉取{Count} 用时{Sec:F1}s",
                                storeCode,
                                page,
                                pages,
                                hqPage.Count,
                                pageQuerySec
                            );
                            break;
                        }
                        catch (SqlSugarException ex) when (retries < 3)
                        {
                            retries++;
                            var delay = TimeSpan.FromSeconds(Math.Pow(2, retries));
                            _logger.LogWarning(
                                ex,
                                "[ReactSync] 分店{Store} 清货价 页{Page} 查询失败，重试{Retry}/3，等待{Delay}s",
                                storeCode,
                                page,
                                retries,
                                delay.TotalSeconds
                            );
                            await Task.Delay(delay);
                            hqDb?.Dispose();
                            hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                            continue;
                        }
                    }

                    if (!hqPage.Any())
                        continue;

                    var localBatch = _mapper.Map<List<StoreClearancePrice>>(hqPage);
                    var insertStart = DateTime.Now;
                    try
                    {
                        var inserted = await RetryBulkInsertAsync(localDb, localBatch, 3);
                        added += inserted;
                        errors += localBatch.Count - inserted;

                        var insertDuration = DateTime.Now - insertStart;
                        _logger.LogInformation(
                            "[ReactSync] 分店{Store} 清货价 页{Page}/{Pages} 插入成功：{Inserted:N0}/{Total:N0}条，耗时{Sec:F1}s",
                            storeCode,
                            page,
                            pages,
                            inserted,
                            localBatch.Count,
                            insertDuration.TotalSeconds
                        );
                    }
                    catch (Exception ex)
                    {
                        var insertDuration = DateTime.Now - insertStart;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 分店{Store} 清货价 页{Page}/{Pages} 插入失败，耗时{Sec:F1}s",
                            storeCode,
                            page,
                            pages,
                            insertDuration.TotalSeconds
                        );
                        errors += localBatch.Count;
                    }
                    insertTotalSeconds += (DateTime.Now - insertStart).TotalSeconds;
                    processed += hqPage.Count;
                    hqPage.Clear();
                    localBatch.Clear();
                }

                var storeDuration = DateTime.Now - storeStart;
                _logger.LogInformation(
                    "[ReactSync] 分店{Store} 清货价完成：处理{Processed:N0} 成功{Added:N0} 失败{Errors:N0} 总耗时{Sec:F1}s",
                    storeCode,
                    processed,
                    added,
                    errors,
                    storeDuration.TotalSeconds
                );
                return (processed, added, errors, qDuration.TotalSeconds, insertTotalSeconds, null);
            }
            catch (Exception ex)
            {
                var error = new StoreSyncError
                {
                    StoreCode = storeCode,
                    ErrorMessage = ex.Message,
                    ExceptionType = ex.GetType().Name,
                    IsRetried = false,
                    RetryCount = 0,
                    ProcessedCount = processed,
                    InsertedCount = added,
                    FailedCount = errors,
                    DurationSeconds = (DateTime.Now - storeStart).TotalSeconds
                };

                _logger.LogError(ex, "[ReactSync] 分店{Store} 清货价处理异常", storeCode);
                return (0, 0, 1, 0, 0, error);
            }
            finally
            {
                localDb?.Dispose();
                hqDb?.Dispose();
                semaphore.Release();
            }
        }

        /// <summary>
        /// 按分店并发同步“一品多码”：HQ 分店多码表 → 本地 StoreMultiCodeProduct。
        /// 先清理目标分店历史数据，再分店并发拉取与批量写入。
        /// </summary>
        public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int? maxConcurrency = null,
            int? batchSize = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            var effectiveMaxConcurrency =
                maxConcurrency ?? _configuration.GetValue<int>("Database:SyncMaxConcurrency", 5);
            var effectiveBatchSize =
                batchSize ?? _configuration.GetValue<int>("Database:SyncBatchSize", 50000);

            var semaphore = new SemaphoreSlim(effectiveMaxConcurrency, effectiveMaxConcurrency);
            var progressLock = new object();
            var storeErrors = new List<StoreSyncError>();

            var totalProcessed = 0;
            var totalAdded = 0;
            var totalErrors = 0;
            var totalQueryTime = 0.0;
            var totalInsertTime = 0.0;

            try
            {
                List<string> storeCodesToProcess;
                if (selectedStoreCodes?.Any() == true)
                {
                    storeCodesToProcess = selectedStoreCodes;
                }
                else
                {
                    var startQuery = DateTime.Now;
                    var storeCodesNullable = await _hqContext
                        .Db.Queryable<DIC_分店一品多码表, DIC_商品信息字典表>(
                            (mc, product) =>
                                new JoinQueryInfos(
                                    JoinType.Inner,
                                    mc.H商品编码 == product.H商品编码
                                )
                        )
                        .Where(
                            (mc, product) =>
                                !string.IsNullOrEmpty(mc.H商品编码)
                                && !string.IsNullOrEmpty(mc.H分店代码)
                                && mc.H使用状态 == true
                                && product.H使用状态 == true
                        )
                        .GroupBy((mc, product) => mc.H分店代码)
                        .Select((mc, product) => mc.H分店代码)
                        .ToListAsync();
                    storeCodesToProcess = storeCodesNullable
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .ToList();
                    var duration = DateTime.Now - startQuery;
                    _logger.LogInformation(
                        "[ReactSync] 一品多码分店数{Count}，查询耗时{Seconds:F1}s",
                        storeCodesToProcess.Count,
                        duration.TotalSeconds
                    );
                }

                if (!storeCodesToProcess.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "未发现需要同步的分店";
                    return result;
                }

                var deleteStart = DateTime.Now;
                var deleted = await _localContext
                    .Db.Deleteable<StoreMultiCodeProduct>()
                    .Where(x => x.StoreCode != null && storeCodesToProcess.Contains(x.StoreCode))
                    .ExecuteCommandAsync();
                var deleteDuration = DateTime.Now - deleteStart;
                _logger.LogInformation(
                    "[ReactSync] 清理一品多码本地数据：删除{Deleted:N0}条，耗时{Seconds:F1}s",
                    deleted,
                    deleteDuration.TotalSeconds
                );

                var tasks =
                    new List<
                        Task<(
                            int processed,
                            int added,
                            int errors,
                            double queryTime,
                            double insertTime,
                            StoreSyncError? error
                        )>
                    >();
                var processStart = DateTime.Now;

                for (int i = 0; i < storeCodesToProcess.Count; i++)
                {
                    var storeCode = storeCodesToProcess[i];
                    var t = Task.Run(async () =>
                        await ProcessSingleStoreMultiCodeAsync(
                            i,
                            storeCode,
                            effectiveBatchSize,
                            semaphore
                        )
                    );
                    tasks.Add(t);

                    if (tasks.Count >= effectiveMaxConcurrency)
                    {
                        var done = await Task.WhenAny(tasks);
                        tasks.Remove(done);
                        var r = await done;
                        lock (progressLock)
                        {
                            totalProcessed += r.processed;
                            totalAdded += r.added;
                            totalErrors += r.errors;
                            totalQueryTime += r.queryTime;
                            totalInsertTime += r.insertTime;
                            if (r.error != null)
                            {
                                storeErrors.Add(r.error);
                            }
                        }
                    }
                }

                var rest = await Task.WhenAll(tasks);
                foreach (var r in rest)
                {
                    lock (progressLock)
                    {
                        totalProcessed += r.processed;
                        totalAdded += r.added;
                        totalErrors += r.errors;
                        totalQueryTime += r.queryTime;
                        totalInsertTime += r.insertTime;
                        if (r.error != null)
                        {
                            storeErrors.Add(r.error);
                        }
                    }
                }

                result.AddedCount = totalAdded;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    totalErrors == 0 ? "分店一品多码同步成功" : "分店一品多码同步完成，但存在错误";

                var finalDuration = DateTime.Now - processStart;
                result.EndTime = DateTime.Now;
                result.Duration = finalDuration;

                result.TotalCount = totalProcessed;
                result.TotalStores = storeCodesToProcess.Count;
                result.SuccessStores = storeCodesToProcess.Count - storeErrors.Count;
                result.FailedStores = storeErrors.Count;
                result.StoreErrors = storeErrors;

                var successRate = result.TotalCount > 0
                    ? (double)result.SuccessCount / result.TotalCount * 100
                    : 0;

                result.Message = totalErrors == 0
                    ? $"分店一品多码同步成功：共处理{result.TotalCount:N0}条记录，{result.SuccessStores}个分店全部成功，成功率{successRate:F1}%"
                    : $"分店一品多码同步完成：共处理{result.TotalCount:N0}条记录，成功{result.SuccessCount:N0}条，失败{result.ErrorCount:N0}条，{result.SuccessStores}个分店成功，{result.FailedStores}个分店失败，成功率{successRate:F1}%";

                if (storeErrors.Any())
                {
                    var errorDetails = storeErrors
                        .Select(e => $"分店{e.StoreCode}: {e.ErrorMessage} (处理{e.ProcessedCount:N0}条, 成功{e.InsertedCount:N0}条, 耗时{e.DurationSeconds:F1}s)")
                        .ToList();

                    result.Details = "错误分店列表：\n" + string.Join("\n", errorDetails);
                }

                _logger.LogInformation(
                    "[ReactSync] 一品多码分店同步完成：门店{StoreCount} 记录{Processed:N0} 耗时{Seconds:F1}s 错误{Errors:N0}",
                    storeCodesToProcess.Count,
                    totalProcessed,
                    finalDuration.TotalSeconds,
                    totalErrors
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 分店一品多码同步异常");
                result.IsSuccess = false;
                result.Message = "分店一品多码同步异常";
            }
            finally
            {
                semaphore.Dispose();
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        private async Task<(
            int processed,
            int added,
            int errors,
            double queryTime,
            double insertTime,
            StoreSyncError? error
        )> ProcessSingleStoreMultiCodeAsync(
            int storeIndex,
            string storeCode,
            int batchSize,
            SemaphoreSlim semaphore
        )
        {
            await semaphore.WaitAsync();

            ISqlSugarClient? localDb = null;
            ISqlSugarClient? hqDb = null;
            var processed = 0;
            var added = 0;
            var errors = 0;
            var storeStart = DateTime.Now;
            try
            {
                storeStart = DateTime.Now;
                localDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                var countStart = DateTime.Now;
                var totalCount = await hqDb.Queryable<DIC_分店一品多码表>()
                    .Where(mc =>
                        !string.IsNullOrEmpty(mc.H商品编码)
                        && mc.H分店代码 == storeCode
                        && mc.H使用状态 == true
                        && SqlFunc
                            .Subqueryable<DIC_商品信息字典表>()
                            .Where(product =>
                                product.H商品编码 == mc.H商品编码 && product.H使用状态 == true
                            )
                            .Any()
                    )
                    .CountAsync();
                var qDuration = DateTime.Now - countStart;
                if (totalCount == 0)
                    return (0, 0, 0, qDuration.TotalSeconds, 0, null);

                var pages = (int)Math.Ceiling(totalCount / (double)batchSize);
                var insertTotalSeconds = 0.0;

                for (var page = 1; page <= pages; page++)
                {
                    var retries = 0;
                    List<DIC_分店一品多码表> hqPage = new();
                    while (true)
                    {
                        try
                        {
                            var skip = (page - 1) * batchSize;
                            var pageStart = DateTime.Now;
                            hqPage = await hqDb.Queryable<DIC_分店一品多码表>()
                                .Where(mc =>
                                    !string.IsNullOrEmpty(mc.H商品编码)
                                    && mc.H分店代码 == storeCode
                                    && mc.H使用状态 == true
                                    && SqlFunc
                                        .Subqueryable<DIC_商品信息字典表>()
                                        .Where(product =>
                                            product.H商品编码 == mc.H商品编码
                                            && product.H使用状态 == true
                                        )
                                        .Any()
                                )
                                .OrderBy(mc => mc.ID)
                                .Skip(skip)
                                .Take(batchSize)
                                .ToListAsync();
                            var pageQuerySec = (DateTime.Now - pageStart).TotalSeconds;
                            _logger.LogInformation(
                                "[ReactSync] 分店{Store} 一品多码 页{Page}/{Pages} 拉取{Count} 用时{Sec:F1}s",
                                storeCode,
                                page,
                                pages,
                                hqPage.Count,
                                pageQuerySec
                            );
                            break;
                        }
                        catch (SqlSugarException ex) when (retries < 3)
                        {
                            retries++;
                            var delay = TimeSpan.FromSeconds(Math.Pow(2, retries));
                            _logger.LogWarning(
                                ex,
                                "[ReactSync] 分店{Store} 一品多码 页{Page} 查询失败，重试{Retry}/3，等待{Delay}s",
                                storeCode,
                                page,
                                retries,
                                delay.TotalSeconds
                            );
                            await Task.Delay(delay);
                            hqDb?.Dispose();
                            hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                            continue;
                        }
                    }

                    if (!hqPage.Any())
                        continue;

                    var localBatch = _mapper.Map<List<StoreMultiCodeProduct>>(hqPage);
                    var insertStart = DateTime.Now;
                    try
                    {
                        var inserted = await RetryBulkInsertAsync(localDb, localBatch, 3);
                        added += inserted;
                        errors += localBatch.Count - inserted;

                        var insertDuration = DateTime.Now - insertStart;
                        _logger.LogInformation(
                            "[ReactSync] 分店{Store} 一品多码 页{Page}/{Pages} 插入成功：{Inserted:N0}/{Total:N0}条，耗时{Sec:F1}s",
                            storeCode,
                            page,
                            pages,
                            inserted,
                            localBatch.Count,
                            insertDuration.TotalSeconds
                        );
                    }
                    catch (Exception ex)
                    {
                        var insertDuration = DateTime.Now - insertStart;
                        _logger.LogError(
                            ex,
                            "[ReactSync] 分店{Store} 一品多码 页{Page}/{Pages} 插入失败，耗时{Sec:F1}s",
                            storeCode,
                            page,
                            pages,
                            insertDuration.TotalSeconds
                        );
                        errors += localBatch.Count;
                    }
                    insertTotalSeconds += (DateTime.Now - insertStart).TotalSeconds;
                    processed += hqPage.Count;
                    hqPage.Clear();
                    localBatch.Clear();
                }

                var storeDuration = DateTime.Now - storeStart;
                _logger.LogInformation(
                    "[ReactSync] 分店{Store} 一品多码完成：处理{Processed:N0} 成功{Added:N0} 失败{Errors:N0} 总耗时{Sec:F1}s",
                    storeCode,
                    processed,
                    added,
                    errors,
                    storeDuration.TotalSeconds
                );
                return (processed, added, errors, qDuration.TotalSeconds, insertTotalSeconds, null);
            }
            catch (Exception ex)
            {
                var error = new StoreSyncError
                {
                    StoreCode = storeCode,
                    ErrorMessage = ex.Message,
                    ExceptionType = ex.GetType().Name,
                    IsRetried = false,
                    RetryCount = 0,
                    ProcessedCount = processed,
                    InsertedCount = added,
                    FailedCount = errors,
                    DurationSeconds = (DateTime.Now - storeStart).TotalSeconds
                };

                _logger.LogError(ex, "[ReactSync] 分店{Store} 一品多码处理异常", storeCode);
                return (0, 0, 1, 0, 0, error);
            }
            finally
            {
                localDb?.Dispose();
                hqDb?.Dispose();
                semaphore.Release();
            }
        }

        /// <summary>
        /// 全量同步“套装多码”：HQ 一品多码 → 本地 ProductSetCode。
        /// 读取并行、写入单通道；二次校验并过滤缺失必填字段，输出详细日志。
        /// </summary>
        public async Task<SyncResult> SyncProductSetCodesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000,
            int maxReadConcurrency = 8
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<ProductSetCode>()
                        .AS("ProductSetCode")
                        .ExecuteCommandAsync();

                    var hqCountDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var totalCount = await hqCountDb
                        .Queryable<DIC_一品多码表>()
                        .Where(x =>
                            !string.IsNullOrEmpty(x.H商品编码)
                            && (SqlFunc.HasValue(x.H多码商品编号) || SqlFunc.HasValue(x.H主条形码))
                            && (x.H使用状态 ?? false) == true
                        )
                        .CountAsync();
                    hqCountDb.Dispose();
                    var pageCount = (int)Math.Ceiling(totalCount / (double)hqBatchSize);

                    var channel = System.Threading.Channels.Channel.CreateBounded<
                        List<ProductSetCode>
                    >(Math.Min(12, Math.Max(4, maxReadConcurrency * 2)));
                    var totalAdded = 0;
                    var totalErrors = 0;
                    var fetchErrors = 0;
                    var totalMissingProductCode = 0;
                    var totalMissingSetProductCode = 0;
                    var totalMissingSetItemNumber = 0;

                    var consumer = Task.Run(async () =>
                    {
                        await foreach (var batch in channel.Reader.ReadAllAsync())
                        {
                            try
                            {
                                await _localContext
                                    .Db.Fastest<ProductSetCode>()
                                    .AS("ProductSetCode")
                                    .PageSize(writePageSize)
                                    .BulkCopyAsync(batch);
                                totalAdded += batch.Count;
                            }
                            catch (Exception)
                            {
                                totalErrors += batch.Count;
                                await Task.Delay(1500);
                            }
                        }
                    });

                    var semaphore = new SemaphoreSlim(maxReadConcurrency, maxReadConcurrency);
                    var producers = new List<Task>();
                    for (var page = 1; page <= pageCount; page++)
                    {
                        var pageIndex = page;
                        var p = Task.Run(async () =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                var skip = (pageIndex - 1) * hqBatchSize;
                                var hqDb = HqSqlSugarContext.CreateConcurrentConnection(
                                    _configuration
                                );
                                var hqBatch = await hqDb.Queryable<DIC_一品多码表>()
                                    .Where(x =>
                                        !string.IsNullOrEmpty(x.H商品编码)
                                        && (
                                            SqlFunc.HasValue(x.H多码商品编号)
                                            || SqlFunc.HasValue(x.H主条形码)
                                        )
                                        && (x.H使用状态 ?? false) == true
                                    )
                                    .OrderBy(x => x.ID)
                                    .Skip(skip)
                                    .Take(hqBatchSize)
                                    .ToListAsync();
                                if (hqBatch.Any())
                                {
                                    var localBatch = _mapper.Map<List<ProductSetCode>>(hqBatch);
                                    // 二次字段校验与过滤
                                    var before = localBatch.Count;
                                    var missingProductCode = localBatch.Count(x =>
                                        string.IsNullOrWhiteSpace(x.ProductCode)
                                    );
                                    var missingSetProductCode = localBatch.Count(x =>
                                        string.IsNullOrWhiteSpace(x.SetProductCode)
                                    );
                                    var missingSetItemNumber = localBatch.Count(x =>
                                        string.IsNullOrWhiteSpace(x.SetItemNumber)
                                    );
                                    totalMissingProductCode += missingProductCode;
                                    totalMissingSetProductCode += missingSetProductCode;
                                    totalMissingSetItemNumber += missingSetItemNumber;

                                    localBatch = localBatch
                                        .Where(x =>
                                            !string.IsNullOrWhiteSpace(x.ProductCode)
                                            && !string.IsNullOrWhiteSpace(x.SetProductCode)
                                            && !string.IsNullOrWhiteSpace(x.SetItemNumber)
                                        )
                                        .ToList();

                                    var after = localBatch.Count;
                                    _logger.LogInformation(
                                        "[ReactSync] 套装批{Page} 校验：缺失ProductCode={A} 缺失SetProductCode={B} 缺失SetItemNumber={C} 保留={Valid}",
                                        pageIndex,
                                        missingProductCode,
                                        missingSetProductCode,
                                        missingSetItemNumber,
                                        after
                                    );
                                    await channel.Writer.WriteAsync(localBatch);
                                }
                                hqDb.Dispose();
                            }
                            catch (Exception)
                            {
                                fetchErrors++;
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });
                        producers.Add(p);
                    }

                    await Task.WhenAll(producers);
                    channel.Writer.Complete();
                    await consumer;

                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = totalAdded;
                    result.ErrorCount = totalErrors + fetchErrors;
                    result.IsSuccess = (totalErrors == 0 && fetchErrors == 0);
                    result.Message =
                        totalErrors == 0 ? "套装多码同步成功" : "套装多码同步完成，但存在错误";
                    _logger.LogInformation(
                        "[ReactSync] 套装多码汇总：缺失ProductCode={A} 缺失SetProductCode={B} 缺失SetItemNumber={C} 新增={Added} 错误={Err}",
                        totalMissingProductCode,
                        totalMissingSetProductCode,
                        totalMissingSetItemNumber,
                        totalAdded,
                        totalErrors + fetchErrors
                    );
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("套装多码同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 套装多码同步异常");
                result.IsSuccess = false;
                result.Message = "套装多码同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 全量同步国内商品：HQ 国内商品字典 → 本地 DomesticProduct。
        /// 分页拉取、批量写入，事务保障一致性。
        /// </summary>
        public async Task<SyncResult> SyncDomesticProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                if (!(await _hbSalesContext.TestConnectionAsync()))
                {
                    throw new Exception("HBSales 数据库连接失败");
                }
                await _localContext.Db.Ado.BeginTranAsync();
                var bulkCopyTimeout = _configuration.GetValue<int>(
                    "Database:BulkCopyCommandTimeoutSeconds",
                    600
                );
                var prevTimeout = _localContext.Db.Ado.CommandTimeOut;
                _localContext.Db.Ado.CommandTimeOut = bulkCopyTimeout;
                try
                {
                    await _localContext
                        .Db.Deleteable<DomesticProduct>()
                        .AS("DomesticProduct")
                        .ExecuteCommandAsync();
                    var total = await _hbSalesContext
                        .Db.Queryable<CPT_DIC_商品信息字典表>()
                        .Where(x => !string.IsNullOrEmpty(x.商品编码))
                        .CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await _hbSalesContext
                            .Db.Queryable<CPT_DIC_商品信息字典表>()
                            .Where(x => !string.IsNullOrEmpty(x.商品编码))
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        if (!batch.Any())
                            continue;
                        var localBatch = _mapper.Map<List<DomesticProduct>>(batch);
                        try
                        {
                            await _localContext
                                .Db.Fastest<DomesticProduct>()
                                .AS("DomesticProduct")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] DomesticProduct 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "DomesticProduct 同步成功"
                            : "DomesticProduct 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("DomesticProduct 同步事务失败", exTran);
                }
                finally
                {
                    _localContext.Db.Ado.CommandTimeOut = prevTimeout;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] DomesticProduct 同步异常");
                result.IsSuccess = false;
                result.Message = "DomesticProduct 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        /// <summary>
        /// 全量同步国内套装：HQ 国内套装信息 → 本地 DomesticSetProduct。
        /// 写入前过滤 ProductCode/SetProductNo 为空的数据。
        /// </summary>
        public async Task<SyncResult> SyncDomesticSetProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                if (!(await _hbSalesContext.TestConnectionAsync()))
                {
                    throw new Exception("HBSales 数据库连接失败");
                }
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<DomesticSetProduct>()
                        .AS("DomesticSetProduct")
                        .ExecuteCommandAsync();
                    var total = await _hbSalesContext
                        .Db.Queryable<CPT_DIC_商品套装信息表>()
                        .CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;
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
                        var localBatch = _mapper
                            .Map<List<DomesticSetProduct>>(batch)
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(x.ProductCode)
                                && !string.IsNullOrWhiteSpace(x.SetProductNo)
                            )
                            .ToList();
                        try
                        {
                            await _localContext
                                .Db.Fastest<DomesticSetProduct>()
                                .AS("DomesticSetProduct")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] DomesticSetProduct 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "DomesticSetProduct 同步成功"
                            : "DomesticSetProduct 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("DomesticSetProduct 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] DomesticSetProduct 同步异常");
                result.IsSuccess = false;
                result.Message = "DomesticSetProduct 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        /// <summary>
        /// 全量同步货号前缀：HQ 前缀信息 → 本地 ProductPrefixCode。
        /// 写入前过滤 SupplierCode/PrefixName 为空的数据。
        /// </summary>
        public async Task<SyncResult> SyncProductPrefixCodesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                if (!(await _hbSalesContext.TestConnectionAsync()))
                {
                    throw new Exception("HBSales 数据库连接失败");
                }
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<ProductPrefixCode>()
                        .AS("ProductPrefixCode")
                        .ExecuteCommandAsync();
                    var total = await _hbSalesContext
                        .Db.Queryable<CPT_DIC_货号前缀信息表>()
                        .CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await _hbSalesContext
                            .Db.Queryable<CPT_DIC_货号前缀信息表>()
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        if (!batch.Any())
                            continue;
                        var localBatch = _mapper
                            .Map<List<ProductPrefixCode>>(batch)
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(x.SupplierCode)
                                && !string.IsNullOrWhiteSpace(x.PrefixName)
                            )
                            .ToList();
                        try
                        {
                            await _localContext
                                .Db.Fastest<ProductPrefixCode>()
                                .AS("ProductPrefixCode")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] ProductPrefixCode 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "ProductPrefixCode 同步成功"
                            : "ProductPrefixCode 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("ProductPrefixCode 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] ProductPrefixCode 同步异常");
                result.IsSuccess = false;
                result.Message = "ProductPrefixCode 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        /// <summary>
        /// 全量同步国内供应商：HQ 供应商信息 → 本地 ChinaSupplier。
        /// 分页拉取、批量写入，统一日志汇总。
        /// </summary>
        public async Task<SyncResult> SyncChinaSuppliersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                if (!(await _hbSalesContext.TestConnectionAsync()))
                {
                    throw new Exception("HBSales 数据库连接失败");
                }
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<ChinaSupplier>()
                        .AS("ChinaSupplier")
                        .ExecuteCommandAsync();
                    var total = await _hbSalesContext
                        .Db.Queryable<CBP_DIC_国内供应商信息表>()
                        .CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await _hbSalesContext
                            .Db.Queryable<CBP_DIC_国内供应商信息表>()
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        if (!batch.Any())
                            continue;
                        var localBatch = _mapper.Map<List<ChinaSupplier>>(batch);
                        try
                        {
                            await _localContext
                                .Db.Fastest<ChinaSupplier>()
                                .AS("ChinaSupplier")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] ChinaSupplier 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "ChinaSupplier 同步成功"
                            : "ChinaSupplier 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("ChinaSupplier 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] ChinaSupplier 同步异常");
                result.IsSuccess = false;
                result.Message = "ChinaSupplier 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        /// <summary>
        /// 全量同步仓库分类码：HQ 分类码表 → 本地 WarehouseCategory。
        /// 分页拉取、批量写入。
        /// </summary>
        public async Task<SyncResult> SyncWarehouseCategoriesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<WarehouseCategory>()
                        .AS("WarehouseCategory")
                        .ExecuteCommandAsync();
                    var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var total = await hqDb.Queryable<CBP_DIC_商品分类码表>().CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await hqDb.Queryable<CBP_DIC_商品分类码表>()
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        if (!batch.Any())
                            continue;
                        var localBatch = _mapper.Map<List<WarehouseCategory>>(batch);
                        try
                        {
                            await _localContext
                                .Db.Fastest<WarehouseCategory>()
                                .AS("WarehouseCategory")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] WarehouseCategory 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    hqDb.Dispose();
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "WarehouseCategory 同步成功"
                            : "WarehouseCategory 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("WarehouseCategory 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] WarehouseCategory 同步异常");
                result.IsSuccess = false;
                result.Message = "WarehouseCategory 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        /// <summary>
        /// 全量同步货柜详情：HQ 详情表 → 本地 ContainerDetail（不派生主表 Container）。
        /// 支持主表GUID筛选；事务保障，失败回滚。
        /// </summary>
        public async Task<SyncResult> SyncContainerDetailsFromHqAsync(
            List<string>? masterGuids = null,
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncContainerDetails,
                new TaskParameters
                {
                    CustomParameters = new Dictionary<string, object>
                    {
                        ["MasterGuids"] = masterGuids ?? new List<string>(),
                    },
                },
                TaskTrigger.Manual
            );
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<ContainerDetail>()
                        .AS("ContainerDetail")
                        .ExecuteCommandAsync();
                    // 不再清理或派生主表 Container，仅同步详情

                    var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var hasFilter = masterGuids != null && masterGuids.Count > 0;

                    // 不派生主表 Container：移除 distinctMasters 与 Container 批量写入逻辑

                    var total = await hqDb.Queryable<CPT_RED_货柜单详情表Store>()
                        .Where(x => !string.IsNullOrEmpty(x.主表GUID))
                        .WhereIF(hasFilter, x => SqlFunc.ContainsArray(masterGuids!, x.主表GUID))
                        .CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await hqDb.Queryable<CPT_RED_货柜单详情表Store>()
                            .Where(x => !string.IsNullOrEmpty(x.主表GUID))
                            .WhereIF(
                                hasFilter,
                                x => SqlFunc.ContainsArray(masterGuids!, x.主表GUID)
                            )
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        if (!batch.Any())
                            continue;
                        var localBatch = _mapper
                            .Map<List<ContainerDetail>>(batch)
                            .Where(x => !string.IsNullOrWhiteSpace(x.ContainerCode))
                            .ToList();
                        try
                        {
                            await _localContext
                                .Db.Fastest<ContainerDetail>()
                                .AS("ContainerDetail")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] ContainerDetail 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    hqDb.Dispose();
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "ContainerDetail 同步成功"
                            : "ContainerDetail 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("ContainerDetail 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] ContainerDetail 同步异常");
                result.IsSuccess = false;
                result.Message = "ContainerDetail 同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
            }
            return result;
        }

        /// <summary>
        /// 全量同步货柜主表：HQ `CPT_RED_货柜单主表` → 本地 `Container`
        /// 流程：检查连接 → 事务清理 → 分页读取HQ → AutoMapper映射 → 批量写入 → 汇总日志。
        /// 约束：仅同步 HGUID 非空的主表记录，以保证与详情的主表GUID一致性。
        /// </summary>
        public async Task<SyncResult> SyncContainersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncContainers,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<Container>()
                        .AS("Container")
                        .ExecuteCommandAsync();
                    var hqDb = HqSqlSugarContext.CreateConcurrentConnection(_configuration);
                    var total = await hqDb.Queryable<CPT_RED_货柜单主表Store>()
                        .Where(x => !string.IsNullOrEmpty(x.HGUID))
                        .CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;

                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await hqDb.Queryable<CPT_RED_货柜单主表Store>()
                            .Where(x => !string.IsNullOrEmpty(x.HGUID))
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        if (!batch.Any())
                            continue;

                        var localBatch = _mapper
                            .Map<List<Container>>(batch)
                            .Where(x => !string.IsNullOrWhiteSpace(x.ContainerCode))
                            .ToList();
                        try
                        {
                            await _localContext
                                .Db.Fastest<Container>()
                                .AS("Container")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] Container 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }

                        batch.Clear();
                        localBatch.Clear();
                    }
                    hqDb.Dispose();
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0 ? "Container 同步成功" : "Container 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("Container 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] Container 同步异常");
                result.IsSuccess = false;
                result.Message = "Container 同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
            }
            return result;
        }

        /// <summary>
        /// 全量同步仓库商品：HQ `CBP_DIC_商品库存表` → 本地 `WarehouseProduct`
        /// 使用分页读取+批量写入，事务保障一致性。
        /// </summary>
        public async Task<SyncResult> SyncWarehouseProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<WarehouseProduct>()
                        .AS("WarehouseProduct")
                        .ExecuteCommandAsync();
                    var total = await _hqContext
                        .Db.Queryable<CBP_DIC_商品库存表>()
                        .Where(x => SqlFunc.HasValue(x.H商品编码))
                        .CountAsync();
                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;
                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await _hqContext
                            .Db.Queryable<CBP_DIC_商品库存表>()
                            .Where(x => SqlFunc.HasValue(x.H商品编码))
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();
                        if (!batch.Any())
                            continue;
                        var localBatch = _mapper
                            .Map<List<WarehouseProduct>>(batch)
                            .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                            .ToList();
                        try
                        {
                            await _localContext
                                .Db.Fastest<WarehouseProduct>()
                                .AS("WarehouseProduct")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] WarehouseProduct 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "WarehouseProduct 同步成功"
                            : "WarehouseProduct 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("WarehouseProduct 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] WarehouseProduct 同步异常");
                result.IsSuccess = false;
                result.Message = "WarehouseProduct 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        public async Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncStoreLocalSupplierInvoices,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            try
            {
                _hqContext.CheckConnection();
                // 删除阶段使用事务，写入阶段取消全局事务，避免大事务中途失败导致后续批次“事务已完成”
                await _localContext.Db.Ado.UseTranAsync(async () =>
                {
                    await _localContext
                        .Db.Deleteable<StoreLocalSupplierInvoice>()
                        .AS("StoreLocalSupplierInvoice")
                        .ExecuteCommandAsync();
                });
                var total = await _hqContext
                    .Db.Queryable<RED_进货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .CountAsync();
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var errors = 0;
                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var batch = await _hqContext
                        .Db.Queryable<RED_进货单主表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID))
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    if (!batch.Any())
                        continue;
                    var localBatch = _mapper
                        .Map<List<StoreLocalSupplierInvoice>>(batch)
                        .Where(x => !string.IsNullOrWhiteSpace(x.InvoiceGUID))
                        .ToList();
                    try
                    {
                        await _localContext
                            .Db.Fastest<StoreLocalSupplierInvoice>()
                            .AS("StoreLocalSupplierInvoice")
                            .PageSize(writePageSize)
                            .BulkCopyAsync(localBatch);
                        added += localBatch.Count;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "[ReactSync] StoreLocalSupplierInvoice 批{Page} 插入失败 批大小{Size}",
                            page,
                            localBatch.Count
                        );
                        errors += localBatch.Count;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, 2 * page)));
                    }
                    batch.Clear();
                    localBatch.Clear();
                }
                await _localContext.Db.Ado.CommitTranAsync();
                result.AddedCount = added;
                result.ErrorCount = errors;
                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? "StoreLocalSupplierInvoice 同步成功"
                        : "StoreLocalSupplierInvoice 同步完成，但存在错误";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] StoreLocalSupplierInvoice 同步异常");
                result.IsSuccess = false;
                result.Message = "StoreLocalSupplierInvoice 同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
            }
            return result;
        }

        public async Task<SyncResult> SyncStoreLocalSupplierInvoiceDetailsFromHqAsync(
            int hqBatchSize = 100000,
            int writePageSize = 50000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncStoreLocalSupplierInvoiceDetails,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.UseTranAsync(async () =>
                {
                    await _localContext
                        .Db.Deleteable<StoreLocalSupplierInvoiceDetails>()
                        .AS("StoreLocalSupplierInvoiceDetails")
                        .ExecuteCommandAsync();
                });
                var total = await _hqContext
                    .Db.Queryable<RED_进货单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.H主表GUID))
                    .CountAsync();
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var errors = 0;

                var writeConcurrency = 4;
                var channel = System.Threading.Channels.Channel.CreateBounded<
                    List<StoreLocalSupplierInvoiceDetails>
                >(writeConcurrency * 2);

                var consumers = new List<Task>();
                for (int i = 0; i < writeConcurrency; i++)
                {
                    var consumer = Task.Run(async () =>
                    {
                        using var localDb = SqlSugarContext.CreateConcurrentConnection(
                            _configuration
                        );
                        // BulkCopy 大批量写入易超时，使用更长命令超时（默认 600 秒，可配置）
                        localDb.Ado.CommandTimeOut = _configuration.GetValue<int>(
                            "Database:BulkCopyCommandTimeoutSeconds",
                            600
                        );
                        await foreach (var localBatch in channel.Reader.ReadAllAsync())
                        {
                            try
                            {
                                await localDb
                                    .Fastest<StoreLocalSupplierInvoiceDetails>()
                                    .AS("StoreLocalSupplierInvoiceDetails")
                                    .PageSize(writePageSize)
                                    .BulkCopyAsync(localBatch);
                                System.Threading.Interlocked.Add(ref added, localBatch.Count);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "[ReactSync] 详情并发写入失败 批大小{Size}",
                                    localBatch.Count
                                );
                                System.Threading.Interlocked.Add(ref errors, localBatch.Count);
                                await Task.Delay(TimeSpan.FromSeconds(3));
                            }
                        }
                    });
                    consumers.Add(consumer);
                }

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var batch = await _hqContext
                        .Db.Queryable<RED_进货单详情表Store>()
                        .Where(x => SqlFunc.HasValue(x.H主表GUID))
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    if (!batch.Any())
                        continue;
                    var localBatch = _mapper
                        .Map<List<StoreLocalSupplierInvoiceDetails>>(batch)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.DetailGUID)
                            && !string.IsNullOrWhiteSpace(x.InvoiceGUID)
                        )
                        .ToList();
                    _logger.LogInformation(
                        "[ReactSync] 详情页{Page}/{Pages} 准备写入批{Count}",
                        page,
                        pages,
                        localBatch.Count
                    );
                    await channel.Writer.WriteAsync(localBatch);
                    batch.Clear();
                }

                channel.Writer.Complete();
                await Task.WhenAll(consumers);

                result.AddedCount = added;
                result.ErrorCount = errors;
                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? "StoreLocalSupplierInvoiceDetails 同步成功"
                        : "StoreLocalSupplierInvoiceDetails 同步完成，但存在错误";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] StoreLocalSupplierInvoiceDetails 同步异常");
                result.IsSuccess = false;
                result.Message = "StoreLocalSupplierInvoiceDetails 同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
            }
            return result;
        }

        public async Task<SyncResult> SyncStoreLocalSupplierInvoicesAndDetailsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncStoreLocalSupplierInvoicesAll,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            try
            {
                var mainResult = await SyncStoreLocalSupplierInvoicesFromHqAsync(
                    hqBatchSize,
                    writePageSize
                );
                var detailResult = await SyncStoreLocalSupplierInvoiceDetailsFromHqAsync(
                    hqBatchSize,
                    writePageSize
                );
                result = new SyncResult
                {
                    StartTime = mainResult.StartTime,
                    EndTime = detailResult.EndTime,
                    Duration = detailResult.EndTime - mainResult.StartTime,
                    AddedCount = (mainResult.AddedCount) + (detailResult.AddedCount),
                    ErrorCount = (mainResult.ErrorCount) + (detailResult.ErrorCount),
                    IsSuccess = (mainResult.IsSuccess && detailResult.IsSuccess),
                    Message =
                        (mainResult.IsSuccess && detailResult.IsSuccess)
                            ? "主+详情同步成功"
                            : "主+详情同步完成，但存在错误",
                };
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = "主+详情同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                return result;
            }
        }

        public async Task<SyncResult> SyncWareHouseOrdersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncWareHouseOrders,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.UseTranAsync(async () =>
                {
                    await _localContext
                        .Db.Deleteable<WareHouseOrder>()
                        .AS("WareHouseOrder")
                        .ExecuteCommandAsync();
                });
                var total = await _hqContext
                    .Db.Queryable<CBP_RED_分店订货单主表Store>()
                    .Where(x => SqlFunc.HasValue(x.HGUID))
                    .CountAsync();
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var errors = 0;
                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var batch = await _hqContext
                        .Db.Queryable<CBP_RED_分店订货单主表Store>()
                        .Where(x => SqlFunc.HasValue(x.HGUID))
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    if (!batch.Any())
                        continue;
                    var localBatch = _mapper
                        .Map<List<WareHouseOrder>>(batch)
                        .Where(x => !string.IsNullOrWhiteSpace(x.OrderGUID))
                        .ToList();
                    try
                    {
                        await _localContext
                            .Db.Fastest<WareHouseOrder>()
                            .AS("WareHouseOrder")
                            .PageSize(writePageSize)
                            .BulkCopyAsync(localBatch);
                        added += localBatch.Count;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "[ReactSync] WareHouseOrder 批{Page} 插入失败 批大小{Size}",
                            page,
                            localBatch.Count
                        );
                        errors += localBatch.Count;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, 2 * page)));
                    }
                    batch.Clear();
                    localBatch.Clear();
                }
                result.AddedCount = added;
                result.ErrorCount = errors;
                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0 ? "WareHouseOrder 同步成功" : "WareHouseOrder 同步完成，但存在错误";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] WareHouseOrder 同步异常");
                result.IsSuccess = false;
                result.Message = "WareHouseOrder 同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
            }
            return result;
        }

        public async Task<SyncResult> SyncWareHouseOrderDetailsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncWareHouseOrderDetails,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.UseTranAsync(async () =>
                {
                    await _localContext
                        .Db.Deleteable<WareHouseOrderDetails>()
                        .AS("WareHouseOrderDetails")
                        .ExecuteCommandAsync();
                });

                var total = await _hqContext
                    .Db.Queryable<CBP_RED_分店订单详情表Store>()
                    .Where(x => SqlFunc.HasValue(x.主表GUID))
                    .CountAsync();
                var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                var added = 0;
                var errors = 0;

                var writeConcurrency = 4;
                var channel = System.Threading.Channels.Channel.CreateBounded<
                    List<WareHouseOrderDetails>
                >(writeConcurrency * 2);
                var consumers = new List<Task>();
                for (int i = 0; i < writeConcurrency; i++)
                {
                    var consumer = Task.Run(async () =>
                    {
                        using var localDb = SqlSugarContext.CreateConcurrentConnection(
                            _configuration
                        );
                        localDb.Ado.CommandTimeOut = _configuration.GetValue<int>(
                            "Database:BulkCopyCommandTimeoutSeconds",
                            600
                        );
                        await foreach (var localBatch in channel.Reader.ReadAllAsync())
                        {
                            try
                            {
                                await localDb
                                    .Fastest<WareHouseOrderDetails>()
                                    .AS("WareHouseOrderDetails")
                                    .PageSize(writePageSize)
                                    .BulkCopyAsync(localBatch);
                                System.Threading.Interlocked.Add(ref added, localBatch.Count);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "[ReactSync] WareHouseOrderDetails 并发写入失败 批大小{Size}",
                                    localBatch.Count
                                );
                                System.Threading.Interlocked.Add(ref errors, localBatch.Count);
                                await Task.Delay(TimeSpan.FromSeconds(3));
                            }
                        }
                    });
                    consumers.Add(consumer);
                }

                for (var page = 1; page <= pages; page++)
                {
                    var skip = (page - 1) * hqBatchSize;
                    var batch = await _hqContext
                        .Db.Queryable<CBP_RED_分店订单详情表Store>()
                        .Where(x => SqlFunc.HasValue(x.主表GUID))
                        .OrderBy(x => x.ID)
                        .Skip(skip)
                        .Take(hqBatchSize)
                        .ToListAsync();
                    if (!batch.Any())
                        continue;
                    var localBatch = _mapper
                        .Map<List<WareHouseOrderDetails>>(batch)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.DetailGUID)
                            && !string.IsNullOrWhiteSpace(x.OrderGUID)
                        )
                        .ToList();
                    _logger.LogInformation(
                        "[ReactSync] 订货详情页{Page}/{Pages} 准备写入批{Count}",
                        page,
                        pages,
                        localBatch.Count
                    );
                    await channel.Writer.WriteAsync(localBatch);
                    batch.Clear();
                }
                channel.Writer.Complete();
                await Task.WhenAll(consumers);

                result.AddedCount = added;
                result.ErrorCount = errors;
                result.IsSuccess = errors == 0;
                result.Message =
                    errors == 0
                        ? "WareHouseOrderDetails 同步成功"
                        : "WareHouseOrderDetails 同步完成，但存在错误";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] WareHouseOrderDetails 同步异常");
                result.IsSuccess = false;
                result.Message = "WareHouseOrderDetails 同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            if (result.IsSuccess)
            {
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
            }
            return result;
        }

        public async Task<SyncResult> SyncWareHouseOrdersAllFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.SyncWareHouseOrdersAll,
                new TaskParameters(),
                TaskTrigger.Manual
            );
            try
            {
                var main = await SyncWareHouseOrdersFromHqAsync(hqBatchSize, writePageSize);
                var det = await SyncWareHouseOrderDetailsFromHqAsync(hqBatchSize, writePageSize);
                result = new SyncResult
                {
                    StartTime = main.StartTime,
                    EndTime = det.EndTime,
                    Duration = det.EndTime - main.StartTime,
                    AddedCount = (main.AddedCount) + (det.AddedCount),
                    ErrorCount = (main.ErrorCount) + (det.ErrorCount),
                    IsSuccess = main.IsSuccess && det.IsSuccess,
                    Message =
                        (main.IsSuccess && det.IsSuccess)
                            ? "WareHouseOrder(主+详情)同步成功"
                            : "WareHouseOrder(主+详情)同步完成，但存在错误",
                };
                await _taskLogService.LogTaskSuccessAsync(taskLog.Id);
                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = "WareHouseOrder(主+详情)同步异常";
                await _taskLogService.LogTaskFailureAsync(taskLog.Id, ex.Message);
                return result;
            }
        }

        public async Task<SyncResult> SyncLocationsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<BlazorApp.Shared.Models.Location>()
                        .AS("Location")
                        .ExecuteCommandAsync();

                    var total = await _hqContext
                        .Db.Queryable<CPT_DIC_货位编码信息表>()
                        .CountAsync();

                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;

                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await _hqContext
                            .Db.Queryable<CPT_DIC_货位编码信息表>()
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();

                        if (!batch.Any())
                            continue;

                        var localBatch = _mapper.Map<List<BlazorApp.Shared.Models.Location>>(batch);

                        foreach (var item in localBatch)
                        {
                            if (string.IsNullOrEmpty(item.LocationGuid))
                                item.LocationGuid = Guid.NewGuid().ToString();
                        }

                        try
                        {
                            await _localContext
                                .Db.Fastest<BlazorApp.Shared.Models.Location>()
                                .AS("Location")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] Location 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }
                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0 ? "Location 同步成功" : "Location 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("Location 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] Location 同步异常");
                result.IsSuccess = false;
                result.Message = "Location 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        public async Task<SyncResult> SyncProductLocationsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                _hqContext.CheckConnection();
                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    await _localContext
                        .Db.Deleteable<ProductLocation>()
                        .AS("ProductLocation")
                        .ExecuteCommandAsync();

                    // Pre-load Location Code to GUID map
                    var locationMap = await _hqContext
                        .Db.Queryable<CPT_DIC_货位编码信息表>()
                        .Select(x => new { x.货位编码, x.HGUID })
                        .ToListAsync();

                    var codeToGuid = locationMap
                        .Where(x =>
                            !string.IsNullOrEmpty(x.货位编码) && !string.IsNullOrEmpty(x.HGUID)
                        )
                        .GroupBy(x => x.货位编码)
                        .ToDictionary(g => g.Key!, g => g.First().HGUID!);

                    var added = 0;
                    var errors = 0;

                    async Task ProcessBatch<T>(
                        List<T> sourceBatch,
                        Func<T, ProductLocation?> mapper
                    )
                    {
                        var localBatch = new List<ProductLocation>();
                        foreach (var item in sourceBatch)
                        {
                            var pl = mapper(item);
                            if (pl != null)
                                localBatch.Add(pl);
                        }

                        if (localBatch.Any())
                        {
                            try
                            {
                                await _localContext
                                    .Db.Fastest<ProductLocation>()
                                    .AS("ProductLocation")
                                    .PageSize(writePageSize)
                                    .BulkCopyAsync(localBatch);
                                added += localBatch.Count;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "[ReactSync] ProductLocation 批量插入失败");
                                errors += localBatch.Count;
                            }
                        }
                    }

                    // 1. Sync CPT_RED_货位存货信息表
                    var totalStock = await _hqContext
                        .Db.Queryable<CPT_RED_货位存货信息表>()
                        .CountAsync();
                    var pagesStock = (int)Math.Ceiling(totalStock / (double)hqBatchSize);

                    for (var page = 1; page <= pagesStock; page++)
                    {
                        var batch = await _hqContext
                            .Db.Queryable<CPT_RED_货位存货信息表>()
                            .OrderBy(x => x.ID)
                            .Skip((page - 1) * hqBatchSize)
                            .Take(hqBatchSize)
                            .ToListAsync();

                        await ProcessBatch(
                            batch,
                            item =>
                            {
                                if (
                                    string.IsNullOrEmpty(item.货位编码)
                                    || !codeToGuid.TryGetValue(item.货位编码, out var locGuid)
                                )
                                    return null;

                                return new ProductLocation
                                {
                                    Guid = item.HGUID ?? Guid.NewGuid().ToString(),
                                    ProductCode = item.商品编码 ?? "",
                                    LocationGuid = locGuid,
                                };
                            }
                        );
                    }

                    // 2. Sync CPT_RED_货位配货信息表
                    var totalPick = await _hqContext
                        .Db.Queryable<CPT_RED_货位配货信息表>()
                        .CountAsync();
                    var pagesPick = (int)Math.Ceiling(totalPick / (double)hqBatchSize);

                    for (var page = 1; page <= pagesPick; page++)
                    {
                        var batch = await _hqContext
                            .Db.Queryable<CPT_RED_货位配货信息表>()
                            .OrderBy(x => x.ID)
                            .Skip((page - 1) * hqBatchSize)
                            .Take(hqBatchSize)
                            .ToListAsync();

                        await ProcessBatch(
                            batch,
                            item =>
                            {
                                if (
                                    string.IsNullOrEmpty(item.货位编码)
                                    || !codeToGuid.TryGetValue(item.货位编码, out var locGuid)
                                )
                                    return null;

                                return new ProductLocation
                                {
                                    Guid = item.HGUID ?? Guid.NewGuid().ToString(),
                                    ProductCode = item.商品编码 ?? "",
                                    LocationGuid = locGuid,
                                };
                            }
                        );
                    }

                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0
                            ? "ProductLocation 同步成功"
                            : "ProductLocation 同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("ProductLocation 同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] ProductLocation 同步异常");
                result.IsSuccess = false;
                result.Message = "ProductLocation 同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        /// <summary>
        /// 全量同步收银用户：HQ 收银用户信息表 → 本地 CashRegisterUser 表
        /// 流程：
        /// 1) 检查 HQ 连接；
        /// 2) 开启本地事务并清空 CashRegisterUser；
        /// 3) 分页（50,000）拉取 HQ 数据，转换为 CashRegisterUser 后以 10,000 批量写入；
        /// 4) 统计新增/错误数与耗时，提交事务；异常则回滚。
        /// </summary>
        public async Task<SyncResult> SyncCashRegisterUsersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                _logger.LogInformation("[ReactSync] 收银用户同步：检查HQ连接");
                _hqContext.CheckConnection();

                await _localContext.Db.Ado.BeginTranAsync();
                try
                {
                    _logger.LogInformation("[ReactSync] 清空本地 CashRegisterUser 表");
                    await _localContext
                        .Db.Deleteable<CashRegisterUser>()
                        .AS("CashRegisterUsers")
                        .ExecuteCommandAsync();

                    var total = await _hqContext.Db.Queryable<DIC_收银用户信息表>().CountAsync();

                    var pages = (int)Math.Ceiling(total / (double)hqBatchSize);
                    var added = 0;
                    var errors = 0;

                    _logger.LogInformation(
                        "[ReactSync] 收银用户总计: {Total}, 分为 {Pages} 页",
                        total,
                        pages
                    );

                    for (var page = 1; page <= pages; page++)
                    {
                        var skip = (page - 1) * hqBatchSize;
                        var batch = await _hqContext
                            .Db.Queryable<DIC_收银用户信息表>()
                            .OrderBy(x => x.ID)
                            .Skip(skip)
                            .Take(hqBatchSize)
                            .ToListAsync();

                        if (!batch.Any())
                            continue;

                        var localBatch = _mapper.Map<List<CashRegisterUser>>(batch);

                        try
                        {
                            await _localContext
                                .Db.Fastest<CashRegisterUser>()
                                .AS("CashRegisterUsers")
                                .PageSize(writePageSize)
                                .BulkCopyAsync(localBatch);
                            added += localBatch.Count;
                            _logger.LogInformation(
                                "[ReactSync] 收银用户第{Page}/{TotalPages}页完成，新增{Count}条",
                                page,
                                pages,
                                localBatch.Count
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "[ReactSync] CashRegisterUser 批{Page} 插入失败 批大小{Size}",
                                page,
                                localBatch.Count
                            );
                            errors += localBatch.Count;
                            await Task.Delay(1500);
                        }
                        batch.Clear();
                        localBatch.Clear();
                    }

                    await _localContext.Db.Ado.CommitTranAsync();
                    result.AddedCount = added;
                    result.ErrorCount = errors;
                    result.IsSuccess = errors == 0;
                    result.Message =
                        errors == 0 ? "收银用户同步成功" : "收银用户同步完成，但存在错误";
                }
                catch (Exception exTran)
                {
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("收银用户同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 收银用户同步异常");
                result.IsSuccess = false;
                result.Message = "收银用户同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }

        /// <summary>
        /// 同步商品-供应商映射表：主数据库 → POSM 数据库
        /// 流程：
        /// 1) 并发查询 Product 和 HBLocalSupplier；
        /// 2) 查询 LocalSupplierCode == "200" 的商品对应的 ChinaSupplier；
        /// 3) 构建完整映射关系数据；
        /// 4) 查询现有映射数据，区分更新和插入操作；
        /// 5) Upsert 操作：批量更新已有记录，批量插入新记录；
        /// 6) 清理已删除商品的映射数据。
        /// </summary>
        public async Task<SyncResult> SyncPosmProductSupplierMappingsAsync()
        {
            var result = new SyncResult { StartTime = DateTime.Now };
            try
            {
                _logger.LogInformation("[ReactSync] 商品-供应商映射同步：开始");

                // 步骤 1：使用独立 SqlSugarClient 并发查询 products 和 localSuppliers（两个查询互不依赖，可以并发执行以提升性能）
                // 使用独立的数据库连接避免并发连接冲突
                using var productsClient = SqlSugarContext.CreateConcurrentConnection(
                    _configuration
                );
                using var suppliersClient = SqlSugarContext.CreateConcurrentConnection(
                    _configuration
                );

                var productsDb = new SimpleClient<Product>(productsClient);
                var suppliersDb = new SimpleClient<HBLocalSupplier>(suppliersClient);

                var productsTask = productsDb.GetListAsync(p => !p.IsDeleted);
                var localSuppliersTask = suppliersDb.GetListAsync(ls => !ls.IsDeleted);

                // 等待两个查询都完成
                await Task.WhenAll(productsTask, localSuppliersTask);

                var products = await productsTask;
                var localSuppliers = await localSuppliersTask;

                _logger.LogInformation("[ReactSync] 读取到 {Count} 个商品", products.Count);
                _logger.LogInformation(
                    "[ReactSync] 读取到 {Count} 个本地供应商",
                    localSuppliers.Count
                );

                // 步骤 2：筛选出 LocalSupplierCode == "200" 的商品，这些商品需要关联中国供应商
                var productsWithSupplier200 = products
                    .Where(p =>
                        p.LocalSupplierCode == "200" && !string.IsNullOrEmpty(p.ProductCode)
                    )
                    .Select(p => p.ProductCode!)
                    .Distinct()
                    .ToList();

                // 步骤 3：批量获取 WarehouseProduct 及其对应的 ChinaSupplierCode
                var productChinaSupplierDict = new Dictionary<string, string>();
                if (productsWithSupplier200.Any())
                {
                    // 查询这些商品在 WarehouseProduct 表中的记录，并关联 DomesticProduct 获取中国供应商代码
                    var warehouseProducts = await _localContext
                        .Db.Queryable<WarehouseProduct>()
                        .Includes(wp => wp.DomesticProduct)
                        .Where(wp =>
                            wp.ProductCode != null
                            && productsWithSupplier200.Contains(wp.ProductCode)
                            && !wp.IsDeleted
                        )
                        .ToListAsync();

                    // 构建商品代码 -> 中国供应商代码的字典映射
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

                // 步骤 4：为所有商品构建映射关系数据
                var mappings = new List<PosmProductSupplierMapping>();
                foreach (var product in products)
                {
                    // 跳过没有商品编码的商品
                    if (string.IsNullOrEmpty(product.ProductCode))
                        continue;

                    var mapping = new PosmProductSupplierMapping
                    {
                        ProductCode = product.ProductCode,
                        LocalSupplierCode = product.LocalSupplierCode ?? string.Empty,
                        ChinaSupplierCode = null, // 默认为 null，下面根据情况设置
                        LastUpdateTime = DateTime.Now,
                        IsDeleted = false,
                    };

                    // 如果本地供应商代码是 "200"，则需要设置中国供应商代码
                    if (product.LocalSupplierCode == "200")
                    {
                        if (
                            productChinaSupplierDict.TryGetValue(
                                product.ProductCode,
                                out var chinaCode
                            )
                        )
                        {
                            mapping.ChinaSupplierCode = chinaCode;
                        }
                    }

                    mappings.Add(mapping);
                }

                // 步骤 5：开启事务，查询现有映射数据用于对比
                await _posmContext.Db.Ado.BeginTranAsync();
                try
                {
                    _logger.LogInformation("[ReactSync] 查询现有映射数据");
                    var existingMappings = await _posmContext
                        .Db.Queryable<PosmProductSupplierMapping>()
                        .Where(m => !m.IsDeleted)
                        .ToListAsync();

                    // 将现有映射转为字典，以 ProductCode 为键，便于快速查找
                    var existingDict = existingMappings.ToDictionary(m => m.ProductCode, m => m);

                    _logger.LogInformation(
                        "[ReactSync] 现有映射数据 {Count} 条",
                        existingMappings.Count
                    );

                    // 步骤 6：区分更新和插入数据（Upsert 策略）
                    var toUpdate = new List<PosmProductSupplierMapping>();
                    var toInsert = new List<PosmProductSupplierMapping>();
                    // 收集当前所有商品代码，用于后续识别需要删除的映射
                    var currentProductCodes = new HashSet<string>(
                        mappings.Select(m => m.ProductCode)
                    );

                    foreach (var mapping in mappings)
                    {
                        if (existingDict.TryGetValue(mapping.ProductCode, out var existing))
                        {
                            // 商品已存在：检查是否需要更新（本地供应商或中国供应商代码变化）
                            if (
                                existing.LocalSupplierCode != mapping.LocalSupplierCode
                                || existing.ChinaSupplierCode != mapping.ChinaSupplierCode
                            )
                            {
                                // 保留原有的创建时间
                                mapping.CreatedAt = existing.CreatedAt;
                                toUpdate.Add(mapping);
                            }
                        }
                        else
                        {
                            // 商品不存在：需要插入新记录
                            toInsert.Add(mapping);
                        }
                    }

                    // 步骤 7：找出需要删除的数据（当前商品列表中不存在的映射）
                    var toDelete = existingMappings
                        .Where(m => !currentProductCodes.Contains(m.ProductCode))
                        .ToList();

                    _logger.LogInformation(
                        "[ReactSync] 需要更新 {UpdateCount} 条，插入 {InsertCount} 条，删除 {DeleteCount} 条",
                        toUpdate.Count,
                        toInsert.Count,
                        toDelete.Count
                    );

                    var updatedCount = 0;
                    var insertedCount = 0;
                    var deletedCount = 0;

                    // 步骤 8：批量更新已存在的记录
                    if (toUpdate.Any())
                    {
                        await _posmContext
                            .Db.Fastest<PosmProductSupplierMapping>()
                            .PageSize(10000)
                            .BulkUpdateAsync(toUpdate);
                        updatedCount = toUpdate.Count;
                        _logger.LogInformation("[ReactSync] 更新完成，共 {Count} 条", updatedCount);
                    }

                    // 步骤 9：批量插入新记录
                    if (toInsert.Any())
                    {
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

                    // 步骤 10：批量删除已不存在商品的映射数据
                    if (toDelete.Any())
                    {
                        var deleteProductCodes = toDelete.Select(m => m.ProductCode).ToList();
                        await _posmContext
                            .Db.Deleteable<PosmProductSupplierMapping>()
                            .In(deleteProductCodes)
                            .ExecuteCommandAsync();
                        deletedCount = toDelete.Count;
                        _logger.LogInformation("[ReactSync] 删除完成，共 {Count} 条", deletedCount);
                    }

                    // 记录操作统计
                    result.UpdatedCount = updatedCount;
                    result.AddedCount = insertedCount;
                    result.DeletedCount = deletedCount;

                    // 提交事务
                    await _posmContext.Db.Ado.CommitTranAsync();
                    result.IsSuccess = true;
                    result.Message =
                        $"商品-供应商映射表同步成功：更新 {updatedCount} 条，插入 {insertedCount} 条，删除 {deletedCount} 条";
                }
                catch (Exception exTran)
                {
                    // 事务异常：回滚所有操作
                    await _posmContext.Db.Ado.RollbackTranAsync();
                    throw new Exception("商品-供应商映射表同步事务失败", exTran);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReactSync] 商品-供应商映射表同步异常");
                result.IsSuccess = false;
                result.Message = "商品-供应商映射表同步异常";
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
            return result;
        }
    }
}
