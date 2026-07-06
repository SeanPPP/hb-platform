using System.Runtime.CompilerServices;
using System.Reflection;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;
using HbTaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncIncrementalStoreRetailPriceTests : IDisposable
{
    private const string StoreRetailPricesIncrementalTaskType = "SyncStoreRetailPricesIncremental";
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public DataSyncIncrementalStoreRetailPriceTests()
    {
        _connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _db = new SqlSugarClient(CreateConnectionConfig(_connection.ConnectionString));
        CreateScheduledTaskLogTable(_db);
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_未指定起始日期时_传入最近成功任务开始时间()
    {
        var lastSuccessStartedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        await SeedTaskLogAsync(lastSuccessStartedAt, HbTaskStatus.Success);
        for (var i = 1; i <= 11; i++)
        {
            await SeedTaskLogAsync(
                lastSuccessStartedAt.AddHours(i),
                HbTaskStatus.Failed
            );
        }
        var selectedStoreCodes = new List<string> { "S01" };
        DateTime? capturedStartDate = null;
        var hqSyncService = new Mock<IStoreRetailPriceHqSyncService>();
        hqSyncService
            .Setup(service => service.SyncIncrementalAsync(
                It.Is<List<string>?>(codes => codes != null && codes.SequenceEqual(selectedStoreCodes)),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>()
            ))
            .Callback<List<string>?, DateTime?, DateTime?>((_, startDate, _) =>
            {
                capturedStartDate = startDate;
            })
            .ReturnsAsync(new SyncResult { IsSuccess = true });
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync(selectedStoreCodes);

        Assert.True(result.IsSuccess);
        Assert.Equal(lastSuccessStartedAt, capturedStartDate);
        hqSyncService.VerifyAll();
    }

    [Fact]
    public async Task SyncStoreRetailPricesFromHqIncrementalAsync_没有历史成功任务时_保留统一服务默认窗口()
    {
        await SeedTaskLogAsync(DateTime.UtcNow.AddHours(-1), HbTaskStatus.Failed);
        DateTime? capturedStartDate = new DateTime(2026, 1, 1);
        var hqSyncService = new Mock<IStoreRetailPriceHqSyncService>();
        hqSyncService
            .Setup(service => service.SyncIncrementalAsync(
                It.IsAny<List<string>?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>()
            ))
            .Callback<List<string>?, DateTime?, DateTime?>((_, startDate, _) =>
            {
                capturedStartDate = startDate;
            })
            .ReturnsAsync(new SyncResult { IsSuccess = true });
        var service = CreateService(hqSyncService.Object);

        var result = await service.SyncStoreRetailPricesFromHqIncrementalAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(capturedStartDate);
        hqSyncService.VerifyAll();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task SeedTaskLogAsync(DateTime startedAt, string status)
    {
        await _db.Insertable(new ScheduledTaskLog
        {
            Id = Guid.NewGuid(),
            TaskType = StoreRetailPricesIncrementalTaskType,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = status == HbTaskStatus.Success ? startedAt.AddMinutes(5) : startedAt.AddMinutes(1),
            ScheduledTime = startedAt,
            TriggeredBy = TaskTrigger.Manual,
            RetryCount = 0,
            CanRetry = true,
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private DataSyncIncrementalService CreateService(IStoreRetailPriceHqSyncService hqSyncService)
    {
        var localContext = CreateSqlSugarContext(_db);
        return new DataSyncIncrementalService(
            localContext,
            CreateContext<HqSqlSugarContext>(),
            CreateContext<HBSalesSqlSugarContext>(),
            CreateContext<POSMSqlSugarContext>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<DataSyncIncrementalService>.Instance,
            new ScheduledTaskLogService(
                localContext,
                NullLogger<ScheduledTaskLogService>.Instance
            ),
            hqSyncService,
            new MemoryCache(new MemoryCacheOptions())
        );
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

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static T CreateContext<T>()
        where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
