using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;
using HbTaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncIncrementalPosmSupplierMappingsTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public DataSyncIncrementalPosmSupplierMappingsTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _localConnection.Open();
        _posmConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(typeof(Product), typeof(WarehouseProduct), typeof(DomesticProduct));
        _posmDb.CodeFirst.InitTables(typeof(PosmProductSupplierMapping));
        CreateScheduledTaskLogTable(_localDb);
    }

    [Fact]
    public async Task SyncPosmProductSupplierMappingsIncrementalAsync_外部已有运行中定时任务时复用任务日志并完成同步()
    {
        var now = DateTime.UtcNow;
        var externalTaskLog = await SeedRunningScheduledTaskLogAsync(now);
        await SeedProductAsync("P-UPDATE", "100", now);
        await SeedProductAsync("P-INSERT", "200", now);
        await SeedWarehouseProductWithDomesticSupplierAsync("P-INSERT", "CN-200");
        await _posmDb.Insertable(
            new PosmProductSupplierMapping
            {
                ProductCode = "P-UPDATE",
                LocalSupplierCode = "OLD",
                ChinaSupplierCode = "OLD-CN",
                LastUpdateTime = now.AddDays(-1),
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncPosmProductSupplierMappingsIncrementalAsync(
            now.AddDays(-1)
        );

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.UpdatedCount);

        var mappings = await _posmDb.Queryable<PosmProductSupplierMapping>()
            .OrderBy(x => x.ProductCode)
            .ToListAsync();
        Assert.Equal(2, mappings.Count);

        var updated = Assert.Single(mappings, x => x.ProductCode == "P-UPDATE");
        Assert.Equal("100", updated.LocalSupplierCode);
        Assert.Null(updated.ChinaSupplierCode);

        var inserted = Assert.Single(mappings, x => x.ProductCode == "P-INSERT");
        Assert.Equal("200", inserted.LocalSupplierCode);
        Assert.Equal("CN-200", inserted.ChinaSupplierCode);

        var taskLogs = await _localDb.Queryable<ScheduledTaskLog>()
            .Where(x => x.TaskType == TaskType.SyncPosmProductSupplierMappingsIncremental)
            .ToListAsync();
        var taskLog = Assert.Single(taskLogs);
        Assert.Equal(externalTaskLog.Id, taskLog.Id);
        Assert.Equal(HbTaskStatus.Success, taskLog.Status);
        Assert.NotNull(taskLog.CompletedAt);
    }

    [Fact]
    public async Task SyncPosmProductSupplierMappingsIncrementalAsync_复用外部任务日志时失败会回写失败状态()
    {
        var externalTaskLog = await SeedRunningScheduledTaskLogAsync(DateTime.UtcNow);
        var service = CreateService(posmContext: CreateContext<POSMSqlSugarContext>());

        var result = await service.SyncPosmProductSupplierMappingsIncrementalAsync(DateTime.UtcNow.AddDays(-1));

        Assert.False(result.IsSuccess);

        var taskLogs = await _localDb.Queryable<ScheduledTaskLog>()
            .Where(x => x.TaskType == TaskType.SyncPosmProductSupplierMappingsIncremental)
            .ToListAsync();
        var taskLog = Assert.Single(taskLogs);
        Assert.Equal(externalTaskLog.Id, taskLog.Id);
        Assert.Equal(HbTaskStatus.Failed, taskLog.Status);
        Assert.False(string.IsNullOrWhiteSpace(taskLog.ErrorMessage));
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _posmDb.Dispose();
        _localConnection.Dispose();
        _posmConnection.Dispose();
        DeleteIfExists(_localDbPath);
        DeleteIfExists(_posmDbPath);
    }

    private DataSyncIncrementalService CreateService(POSMSqlSugarContext? posmContext = null)
    {
        var localContext = CreateSqlSugarContext(_localDb);

        return new DataSyncIncrementalService(
            localContext,
            CreateContext<HqSqlSugarContext>(),
            CreateContext<HBSalesSqlSugarContext>(),
            posmContext ?? CreatePosmSqlSugarContext(_posmDb),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<DataSyncIncrementalService>.Instance,
            new ScheduledTaskLogService(
                localContext,
                NullLogger<ScheduledTaskLogService>.Instance
            ),
            Mock.Of<IStoreRetailPriceHqSyncService>(),
            new MemoryCache(new MemoryCacheOptions())
        );
    }

    private async Task<ScheduledTaskLog> SeedRunningScheduledTaskLogAsync(DateTime startedAt)
    {
        var taskLog = new ScheduledTaskLog
        {
            Id = Guid.NewGuid(),
            TaskType = TaskType.SyncPosmProductSupplierMappingsIncremental,
            Status = HbTaskStatus.Running,
            StartedAt = startedAt,
            ScheduledTime = startedAt,
            TriggeredBy = TaskTrigger.Scheduled,
            RetryCount = 0,
            CanRetry = true,
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
            IsDeleted = false,
        };
        await _localDb.Insertable(taskLog).ExecuteCommandAsync();
        return taskLog;
    }

    private async Task SeedProductAsync(string productCode, string localSupplierCode, DateTime updatedAt)
    {
        await _localDb.Insertable(
            new Product
            {
                UUID = Guid.NewGuid().ToString("N"),
                ProductCode = productCode,
                LocalSupplierCode = localSupplierCode,
                ProductName = productCode,
                UpdatedAt = updatedAt,
                CreatedAt = updatedAt.AddDays(-1),
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedWarehouseProductWithDomesticSupplierAsync(
        string productCode,
        string supplierCode
    )
    {
        await _localDb.Insertable(
            new DomesticProduct
            {
                ProductCode = productCode,
                SupplierCode = supplierCode,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static void CreateScheduledTaskLogTable(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand(
            """
            CREATE TABLE IF NOT EXISTS ScheduledTaskLog (
                Id TEXT PRIMARY KEY,
                TaskType TEXT NOT NULL,
                TaskParameters TEXT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                DurationMs INTEGER NULL,
                ErrorMessage TEXT NULL,
                RetryCount INTEGER NOT NULL,
                CanRetry INTEGER NOT NULL,
                ScheduledTime TEXT NOT NULL,
                TriggeredBy TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedAt TEXT NULL,
                UpdatedBy TEXT NULL,
                IsDeleted INTEGER NULL
            );
            """
        );
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            SqliteTempFileCleanup.DeleteIfExists(path);
        }
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(POSMSqlSugarContext)
        );
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static TContext CreateContext<TContext>()
        where TContext : class
    {
        return (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
    }
}
