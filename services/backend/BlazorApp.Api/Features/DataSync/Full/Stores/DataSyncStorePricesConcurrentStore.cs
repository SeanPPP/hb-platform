using BlazorApp.Api.Data;
using BlazorApp.Api.Features.DataSync.Common;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using System.Data;
using System.Data.Common;
using System.Threading.Channels;

namespace BlazorApp.Api.Features.DataSync.Full.Stores;

/// <summary>
/// 并发读取 HQ 分店零售价后，以单一事务替换本地目标分店数据。
/// </summary>
internal sealed class DataSyncStorePricesConcurrentStore : DataSyncSliceBase
{
    private const int MaximumSourceBatchSize = 50000;
    // 并发度控制 HQ 读取，不得同时扩大本地内存队列；每槽最多一个受限来源批次。
    private const int StagingChannelCapacity = 2;
    private static readonly string[] StorePriceColumns =
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
        "CreatedAt",
        "CreatedBy",
        "UpdatedAt",
        "UpdatedBy",
        "IsDeleted",
    ];

    public DataSyncStorePricesConcurrentStore(DataSyncSliceContext context)
        : base(context)
    {
    }

    public async Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(
        List<string>? selectedStoreCodes = null,
        int maxConcurrency = 15,
        int batchSize = 200000
    )
    {
        var result = new SyncResult { StartTime = DateTime.Now };
        var counters = new StorePriceSyncCounters();
        using var cancellationSource = new CancellationTokenSource();
        Task? producerTask = null;

        try
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "并发数必须大于零");
            if (batchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(batchSize), "批次大小必须大于零");

            var storeCodes = await ResolveStoreCodesAsync(selectedStoreCodes);
            if (storeCodes.Count == 0)
            {
                result.IsSuccess = true;
                result.Message = "✅ 没有找到需要同步的分店，同步完成";
                return result;
            }

            Logger.LogInformation(
                "开始并发读取 {StoreCount} 个分店的 HQ 零售价，并发数={MaxConcurrency}",
                storeCodes.Count,
                maxConcurrency
            );

            var sourceBatchSize = Math.Min(batchSize, MaximumSourceBatchSize);
            var sourceBatches = Channel.CreateBounded<StorePriceSourceBatch>(
                new BoundedChannelOptions(StagingChannelCapacity)
                {
                    // 每个元素至多 sourceBatchSize 行；满时等待，不能为赶进度而丢弃数据。
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                }
            );
            await using var spool = new DataSyncStorePricesSpool(Logger);
            producerTask = ProduceStorePricesAsync(
                storeCodes,
                maxConcurrency,
                sourceBatchSize,
                sourceBatches.Writer,
                cancellationSource
            );

            // 远程生产期间只消费到临时 spool，绝不提前开启或删除本地 live 数据。
            await StageStorePricesAsync(sourceBatches.Reader, spool, counters, cancellationSource.Token);
            await producerTask;
            await using var staging = await StageStorePricesInDatabaseAsync(
                storeCodes,
                spool,
                sourceBatchSize
            );
            await ReplaceLocalPricesAsync(staging, counters);

            result.AddedCount = counters.AddedCount;
            result.ErrorCount = 0;
            result.IsSuccess = true;
            result.Message =
                $"🎉 按分店并发同步成功！共处理 {storeCodes.Count} 个分店，{counters.ProcessedCount:N0} 条记录，全部成功插入";
            Logger.LogInformation(result.Message);
        }
        catch (Exception ex)
        {
            // 消费者失败时先解除生产者的写入等待，再等待其退出，避免留下后台任务或通道死锁。
            cancellationSource.Cancel();
            var producerFailure = producerTask is null
                ? null
                : await ObserveProducerFailureAsync(producerTask, cancellationSource.Token);
            var failure = producerFailure is null || ReferenceEquals(ex, producerFailure)
                ? ex
                : new AggregateException(ex, producerFailure);

            // 本地事务失败时所有已写入记录都会回滚，因此不能再报告部分成功。
            result.AddedCount = 0;
            result.ErrorCount = counters.ProcessedCount > 0 ? counters.ProcessedCount : 1;
            result.IsSuccess = false;
            result.Message = $"❌ 同步失败: {failure.Message}";
            Logger.LogError(failure, "按分店并发同步零售价数据时发生错误");
        }
        finally
        {
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
        }

        return result;
    }

    private async Task<List<string>> ResolveStoreCodesAsync(List<string>? selectedStoreCodes)
    {
        if (selectedStoreCodes?.Any() == true)
        {
            return selectedStoreCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return await HqContext.Db.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                (price, product) => new JoinQueryInfos(JoinType.Inner, price.H商品编码 == product.H商品编码)
            )
            .Where(
                (price, product) =>
                    !string.IsNullOrEmpty(price.H商品编码)
                    && !string.IsNullOrEmpty(price.H分店代码)
                    && price.H使用状态
                    && product.H使用状态
            )
            .GroupBy((price, product) => price.H分店代码)
            .Select((price, product) => price.H分店代码)
            .ToListAsync();
    }

    private async Task ProduceStorePricesAsync(
        IReadOnlyCollection<string> storeCodes,
        int maxConcurrency,
        int sourceBatchSize,
        ChannelWriter<StorePriceSourceBatch> writer,
        CancellationTokenSource cancellationSource
    )
    {
        Exception? sourceFailure = null;
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        try
        {
            // 只等待读取任务结束；读取结果直接进入有界通道，不会汇集所有门店批次。
            var readTasks = storeCodes.Select(storeCode => ReadStorePricesFromHqAsync(
                storeCode,
                semaphore,
                sourceBatchSize,
                writer,
                cancellationSource,
                exception => Interlocked.CompareExchange(ref sourceFailure, exception, null)
            ));
            await Task.WhenAll(readTasks);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            cancellationSource.Cancel();
            // 让读端收到原始 HQ/映射异常并触发本地事务回滚。
            writer.TryComplete(sourceFailure ?? ex);
            throw;
        }
    }

    private async Task ReadStorePricesFromHqAsync(
        string storeCode,
        SemaphoreSlim semaphore,
        int sourceBatchSize,
        ChannelWriter<StorePriceSourceBatch> writer,
        CancellationTokenSource cancellationSource,
        Action<Exception> recordSourceFailure
    )
    {
        var cancellationToken = cancellationSource.Token;
        var semaphoreEntered = false;
        try
        {
            await semaphore.WaitAsync(cancellationToken);
            semaphoreEntered = true;

            // HQ 读取仍使用独立连接并发执行；本地库不会在此阶段发生任何写入。
            using var hqDb = HqSqlSugarContext.CreateConcurrentConnection(Configuration);
            var lastSeenId = 0;
            while (true)
            {
                // DIC_商品零售价表 的 ID 是主键：按 ID keyset 翻页，避免 Offset 在并发变化下漏行或重行。
                var hqPrices = await hqDb.Queryable<DIC_商品零售价表>()
                    .Where(price =>
                        !string.IsNullOrEmpty(price.H商品编码)
                        && price.H分店代码 == storeCode
                        && price.H使用状态
                        && price.ID > lastSeenId
                        && SqlFunc.Subqueryable<DIC_商品信息字典表>()
                            .Where(product =>
                                product.H商品编码 == price.H商品编码 && product.H使用状态
                            )
                            .Any()
                    )
                    .OrderBy(price => price.ID)
                    .Take(sourceBatchSize)
                    .ToListAsync();

                if (hqPrices.Count == 0)
                    return;

                // SqlSugar 查询没有取消令牌参数时，查询完成后也必须在映射和入队前响应取消。
                cancellationToken.ThrowIfCancellationRequested();
                var prices = Mapper.Map<List<StoreRetailPrice>>(hqPrices);
                if (prices.Count != hqPrices.Count)
                    throw new InvalidOperationException("HQ 零售价映射结果行数不一致");

                await writer.WriteAsync(new StorePriceSourceBatch(storeCode, prices), cancellationToken);
                lastSeenId = hqPrices[^1].ID;

                if (hqPrices.Count < sourceBatchSize)
                    return;
            }
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                recordSourceFailure(ex);
                // 首个生产失败立刻唤醒消费者回滚，不等待其他不可取消的 HQ 查询返回。
                writer.TryComplete(ex);
            }

            // 任一生产者失败会停止其余读/写任务，消费者随后从已完成的通道收到异常并回滚。
            cancellationSource.Cancel();
            throw;
        }
        finally
        {
            if (semaphoreEntered)
                semaphore.Release();
        }
    }

    private async Task<StorePriceDatabaseStaging> StageStorePricesInDatabaseAsync(
        IReadOnlyCollection<string> storeCodes,
        DataSyncStorePricesSpool spool,
        int batchSize
    )
    {
        var localDb = LocalContext.Db;
        return localDb.CurrentConnectionConfig.DbType == SqlSugar.DbType.SqlServer
            ? await StageStorePricesInSqlServerAsync(localDb, storeCodes, spool, batchSize)
            : await StageStorePricesInCompatibleDatabaseAsync(localDb, storeCodes, spool);
    }

    private async Task<StorePriceDatabaseStaging> StageStorePricesInSqlServerAsync(
        ISqlSugarClient localDb,
        IReadOnlyCollection<string> storeCodes,
        DataSyncStorePricesSpool spool,
        int batchSize
    )
    {
        var bulkCopyCommandTimeoutSeconds = Configuration.GetValue<int>(
            "Database:BulkCopyCommandTimeoutSeconds",
            900
        );
        var commandTimeoutSeconds = Configuration.GetValue<int>(
            "Database:ConcurrentCommandTimeoutSeconds",
            300
        );
        // 独立连接整个 staging 生命周期保持打开，保证 #temp、SqlBulkCopy 和最终交换严格同一会话。
        var connection = new SqlConnection(localDb.CurrentConnectionConfig.ConnectionString);
        await connection.OpenAsync();
        var staging = new StorePriceDatabaseStaging(
            connection,
            ownsConnection: true,
            stageTableName: "#DataSyncStorePrices",
            selectedStoreTableName: "#DataSyncStorePriceStores",
            commandTimeoutSeconds,
            Logger
        );

        try
        {
            await ExecuteNonQueryAsync(
                connection,
                null,
                """
                CREATE TABLE #DataSyncStorePriceStores (
                    [StoreCode] nvarchar(50) NOT NULL PRIMARY KEY
                );
                CREATE TABLE #DataSyncStorePrices (
                    [UUID] nvarchar(50) NOT NULL,
                    [StoreCode] nvarchar(50) NULL,
                    [ProductCode] nvarchar(50) NULL,
                    [StoreProductCode] nvarchar(50) NULL,
                    [SupplierCode] nvarchar(50) NULL,
                    [PurchasePrice] decimal(18, 4) NULL,
                    [StoreRetailPriceValue] decimal(18, 4) NULL,
                    [DiscountRate] decimal(18, 6) NULL,
                    [IsActive] bit NOT NULL,
                    [IsAutoPricing] bit NOT NULL,
                    [IsSpecialProduct] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [CreatedBy] nvarchar(max) NULL,
                    [UpdatedAt] datetime2 NULL,
                    [UpdatedBy] nvarchar(max) NULL,
                    [IsDeleted] bit NULL
                );
                """,
                commandTimeoutSeconds
            );
            await InsertSelectedStoreCodesAsync(connection, null, staging, storeCodes);

            await foreach (var sourceBatch in spool.ReadBatchesAsync(CancellationToken.None))
            {
                if (sourceBatch.Prices.Count == 0)
                    continue;

                var table = CreateStorePriceStageTable(staging.StageTableName);
                foreach (var price in sourceBatch.Prices)
                    AddStorePriceStageRow(table, price);

                using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, null)
                {
                    DestinationTableName = staging.StageTableName,
                    BatchSize = batchSize,
                    BulkCopyTimeout = bulkCopyCommandTimeoutSeconds,
                };
                foreach (DataColumn column in table.Columns)
                    bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);

                // staging 在事务外完成；BulkCopy 失败时不会接触线上目标表。
                await bulkCopy.WriteToServerAsync(table);
                staging.AddedCount += sourceBatch.Prices.Count;
            }
        }
        catch
        {
            await staging.DisposeAsync();
            throw;
        }

        return staging;
    }

    private async Task<StorePriceDatabaseStaging> StageStorePricesInCompatibleDatabaseAsync(
        ISqlSugarClient localDb,
        IReadOnlyCollection<string> storeCodes,
        DataSyncStorePricesSpool spool
    )
    {
        var commandTimeoutSeconds = Configuration.GetValue<int>(
            "Database:ConcurrentCommandTimeoutSeconds",
            300
        );
        var connection = localDb.Ado.Connection as DbConnection
            ?? throw new InvalidOperationException("本地数据库连接不支持 staging");
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var staging = new StorePriceDatabaseStaging(
            connection,
            ownsConnection: false,
            stageTableName: $"DataSyncStorePrices_{suffix}",
            selectedStoreTableName: $"DataSyncStorePriceStores_{suffix}",
            commandTimeoutSeconds,
            Logger
        );
        var stageTable = QuoteCompatibleIdentifier(staging.StageTableName);
        var selectedStoreTable = QuoteCompatibleIdentifier(staging.SelectedStoreTableName);

        try
        {
            // SQLite 测试也使用同一连接的 TEMP staging；语义与 SQL Server 的会话临时表一致。
            await ExecuteNonQueryAsync(
                connection,
                null,
                $"""
                CREATE TEMP TABLE {selectedStoreTable} ([StoreCode] TEXT NOT NULL PRIMARY KEY);
                CREATE TEMP TABLE {stageTable} (
                    [UUID] TEXT NOT NULL,
                    [StoreCode] TEXT NULL,
                    [ProductCode] TEXT NULL,
                    [StoreProductCode] TEXT NULL,
                    [SupplierCode] TEXT NULL,
                    [PurchasePrice] REAL NULL,
                    [StoreRetailPriceValue] REAL NULL,
                    [DiscountRate] REAL NULL,
                    [IsActive] INTEGER NOT NULL,
                    [IsAutoPricing] INTEGER NOT NULL,
                    [IsSpecialProduct] INTEGER NOT NULL,
                    [CreatedAt] TEXT NOT NULL,
                    [CreatedBy] TEXT NULL,
                    [UpdatedAt] TEXT NULL,
                    [UpdatedBy] TEXT NULL,
                    [IsDeleted] INTEGER NULL
                );
                """,
                commandTimeoutSeconds
            );
            await InsertSelectedStoreCodesAsync(connection, null, staging, storeCodes);

            await foreach (var sourceBatch in spool.ReadBatchesAsync(CancellationToken.None))
            {
                await InsertCompatibleStorePriceBatchAsync(
                    connection,
                    null,
                    staging,
                    sourceBatch.Prices
                );
                staging.AddedCount += sourceBatch.Prices.Count;
            }
        }
        catch
        {
            await staging.DisposeAsync();
            throw;
        }

        return staging;
    }

    private async Task ReplaceLocalPricesAsync(
        StorePriceDatabaseStaging staging,
        StorePriceSyncCounters counters
    )
    {
        if (staging.Connection is SqlConnection sqlConnection)
        {
            await ReplaceSqlServerLocalPricesAsync(sqlConnection, staging, counters);
            return;
        }

        await ReplaceCompatibleLocalPricesAsync(staging, counters);
    }

    private async Task ReplaceSqlServerLocalPricesAsync(
        SqlConnection connection,
        StorePriceDatabaseStaging staging,
        StorePriceSyncCounters counters
    )
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            // 目标表锁只覆盖受保护的删除和 set-based 交换，不覆盖远端读取、spool 或 BulkCopy。
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                DECLARE @lockResult int;
                EXEC @lockResult = sp_getapplock
                    @Resource = N'DataSync:StoreRetailPrice:FullReplace',
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 30000;
                IF @lockResult < 0
                    THROW 51000, '无法获取分店零售价同步锁', 1;

                DELETE target
                FROM [StoreRetailPrice] AS target
                INNER JOIN #DataSyncStorePriceStores AS selected
                    ON selected.[StoreCode] = target.[StoreCode];

                INSERT INTO [StoreRetailPrice] ([UUID], [StoreCode], [ProductCode], [StoreProductCode], [SupplierCode], [PurchasePrice], [StoreRetailPriceValue], [DiscountRate], [IsActive], [IsAutoPricing], [IsSpecialProduct], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy], [IsDeleted])
                SELECT [UUID], [StoreCode], [ProductCode], [StoreProductCode], [SupplierCode], [PurchasePrice], [StoreRetailPriceValue], [DiscountRate], [IsActive], [IsAutoPricing], [IsSpecialProduct], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy], [IsDeleted]
                FROM #DataSyncStorePrices;
                """,
                staging.CommandTimeoutSeconds
            );
            await transaction.CommitAsync();
            counters.AddedCount += staging.AddedCount;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task ReplaceCompatibleLocalPricesAsync(
        StorePriceDatabaseStaging staging,
        StorePriceSyncCounters counters
    )
    {
        await using var transaction = await staging.Connection.BeginTransactionAsync();
        var stageTable = QuoteCompatibleIdentifier(staging.StageTableName);
        var selectedStoreTable = QuoteCompatibleIdentifier(staging.SelectedStoreTableName);
        try
        {
            // 兼容 provider 同样只在这里开始目标表事务，确保 SQLite 测试覆盖 staging 后原子交换。
            await ExecuteNonQueryAsync(
                staging.Connection,
                transaction,
                $"""
                DELETE FROM [StoreRetailPrice]
                WHERE [StoreCode] IN (SELECT [StoreCode] FROM {selectedStoreTable});

                INSERT INTO [StoreRetailPrice] ([UUID], [StoreCode], [ProductCode], [StoreProductCode], [SupplierCode], [PurchasePrice], [StoreRetailPriceValue], [DiscountRate], [IsActive], [IsAutoPricing], [IsSpecialProduct], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy], [IsDeleted])
                SELECT [UUID], [StoreCode], [ProductCode], [StoreProductCode], [SupplierCode], [PurchasePrice], [StoreRetailPriceValue], [DiscountRate], [IsActive], [IsAutoPricing], [IsSpecialProduct], [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy], [IsDeleted]
                FROM {stageTable};
                """,
                staging.CommandTimeoutSeconds
            );
            await transaction.CommitAsync();
            counters.AddedCount += staging.AddedCount;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task InsertSelectedStoreCodesAsync(
        DbConnection connection,
        DbTransaction? transaction,
        StorePriceDatabaseStaging staging,
        IEnumerable<string> storeCodes
    )
    {
        var tableName = connection is SqlConnection
            ? staging.SelectedStoreTableName
            : QuoteCompatibleIdentifier(staging.SelectedStoreTableName);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var commandTimeoutSeconds = staging.CommandTimeoutSeconds;
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = $"INSERT INTO {tableName} ([StoreCode]) VALUES (@storeCode);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@storeCode";
        command.Parameters.Add(parameter);

        foreach (var storeCode in storeCodes)
        {
            parameter.Value = storeCode;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertCompatibleStorePriceBatchAsync(
        DbConnection connection,
        DbTransaction? transaction,
        StorePriceDatabaseStaging staging,
        IEnumerable<StoreRetailPrice> prices
    )
    {
        var tableName = QuoteCompatibleIdentifier(staging.StageTableName);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var commandTimeoutSeconds = staging.CommandTimeoutSeconds;
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText =
            $"INSERT INTO {tableName} ({string.Join(", ", StorePriceColumns.Select(column => $"[{column}]"))}) VALUES ({string.Join(", ", StorePriceColumns.Select(column => $"@{column}"))});";
        var parameters = StorePriceColumns.ToDictionary(
            column => column,
            column =>
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = $"@{column}";
                command.Parameters.Add(parameter);
                return parameter;
            },
            StringComparer.Ordinal
        );

        foreach (var price in prices)
        {
            SetStorePriceParameters(parameters, price);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        int commandTimeoutSeconds
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static DataTable CreateStorePriceStageTable(string tableName)
    {
        var table = new DataTable(tableName);
        table.Columns.Add("UUID", typeof(string));
        table.Columns.Add("StoreCode", typeof(string));
        table.Columns.Add("ProductCode", typeof(string));
        table.Columns.Add("StoreProductCode", typeof(string));
        table.Columns.Add("SupplierCode", typeof(string));
        table.Columns.Add("PurchasePrice", typeof(decimal));
        table.Columns.Add("StoreRetailPriceValue", typeof(decimal));
        table.Columns.Add("DiscountRate", typeof(decimal));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("IsAutoPricing", typeof(bool));
        table.Columns.Add("IsSpecialProduct", typeof(bool));
        table.Columns.Add("CreatedAt", typeof(DateTime));
        table.Columns.Add("CreatedBy", typeof(string));
        table.Columns.Add("UpdatedAt", typeof(DateTime));
        table.Columns.Add("UpdatedBy", typeof(string));
        table.Columns.Add("IsDeleted", typeof(bool));
        return table;
    }

    private static void AddStorePriceStageRow(DataTable table, StoreRetailPrice price)
    {
        table.Rows.Add(
            price.UUID,
            DbValue(price.StoreCode),
            DbValue(price.ProductCode),
            DbValue(price.StoreProductCode),
            DbValue(price.SupplierCode),
            DbValue(price.PurchasePrice),
            DbValue(price.StoreRetailPriceValue),
            DbValue(price.DiscountRate),
            price.IsActive,
            price.IsAutoPricing,
            price.IsSpecialProduct,
            price.CreatedAt,
            DbValue(price.CreatedBy),
            DbValue(price.UpdatedAt),
            DbValue(price.UpdatedBy),
            price.IsDeleted
        );
    }

    private static void SetStorePriceParameters(
        IReadOnlyDictionary<string, DbParameter> parameters,
        StoreRetailPrice price
    )
    {
        parameters["UUID"].Value = price.UUID;
        parameters["StoreCode"].Value = DbValue(price.StoreCode);
        parameters["ProductCode"].Value = DbValue(price.ProductCode);
        parameters["StoreProductCode"].Value = DbValue(price.StoreProductCode);
        parameters["SupplierCode"].Value = DbValue(price.SupplierCode);
        parameters["PurchasePrice"].Value = DbValue(price.PurchasePrice);
        parameters["StoreRetailPriceValue"].Value = DbValue(price.StoreRetailPriceValue);
        parameters["DiscountRate"].Value = DbValue(price.DiscountRate);
        parameters["IsActive"].Value = price.IsActive;
        parameters["IsAutoPricing"].Value = price.IsAutoPricing;
        parameters["IsSpecialProduct"].Value = price.IsSpecialProduct;
        parameters["CreatedAt"].Value = price.CreatedAt;
        parameters["CreatedBy"].Value = DbValue(price.CreatedBy);
        parameters["UpdatedAt"].Value = DbValue(price.UpdatedAt);
        parameters["UpdatedBy"].Value = DbValue(price.UpdatedBy);
        parameters["IsDeleted"].Value = price.IsDeleted;
    }

    private static object DbValue<T>(T? value)
        where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static string QuoteCompatibleIdentifier(string identifier) => $"\"{identifier}\"";

    private sealed class StorePriceDatabaseStaging : IAsyncDisposable
    {
        private readonly bool _ownsConnection;
        private readonly ILogger _logger;
        private bool _disposed;

        public StorePriceDatabaseStaging(
            DbConnection connection,
            bool ownsConnection,
            string stageTableName,
            string selectedStoreTableName,
            int commandTimeoutSeconds,
            ILogger logger
        )
        {
            Connection = connection;
            _ownsConnection = ownsConnection;
            StageTableName = stageTableName;
            SelectedStoreTableName = selectedStoreTableName;
            CommandTimeoutSeconds = commandTimeoutSeconds;
            _logger = logger;
        }

        public DbConnection Connection { get; }

        public string StageTableName { get; }

        public string SelectedStoreTableName { get; }

        public int CommandTimeoutSeconds { get; }

        public int AddedCount { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                var stageTable = Connection is SqlConnection
                    ? StageTableName
                    : QuoteCompatibleIdentifier(StageTableName);
                var selectedStoreTable = Connection is SqlConnection
                    ? SelectedStoreTableName
                    : QuoteCompatibleIdentifier(SelectedStoreTableName);
                await ExecuteNonQueryAsync(
                    Connection,
                    null,
                    $"DROP TABLE IF EXISTS {stageTable}; DROP TABLE IF EXISTS {selectedStoreTable};",
                    CommandTimeoutSeconds
                );
            }
            catch (Exception cleanupException)
            {
                // 提交后的临时对象清理失败不能把已完成同步反报为失败；连接关闭时 SQL Server #temp 仍会自动清理。
                _logger.LogWarning(cleanupException, "DataSync store price staging cleanup failed");
            }
            finally
            {
                if (_ownsConnection)
                    await Connection.DisposeAsync();
            }
        }
    }

    private static async Task StageStorePricesAsync(
        ChannelReader<StorePriceSourceBatch> sourceBatches,
        DataSyncStorePricesSpool spool,
        StorePriceSyncCounters counters,
        CancellationToken cancellationToken
    )
    {
        await foreach (var sourceBatch in sourceBatches.ReadAllAsync(cancellationToken))
        {
            await spool.WriteBatchAsync(sourceBatch, cancellationToken);
            counters.ProcessedCount += sourceBatch.Prices.Count;
        }

        // 通道只有在所有生产者成功退出后才完成；此处刷盘后才允许本地替换。
        await spool.CompleteWritingAsync(cancellationToken);
    }

    private static async Task<Exception?> ObserveProducerFailureAsync(
        Task producerTask,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await producerTask;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 消费端故障主动取消后，生产者的取消异常不是另一条业务错误。
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class StorePriceSyncCounters
    {
        public int ProcessedCount { get; set; }

        public int AddedCount { get; set; }
    }
}
