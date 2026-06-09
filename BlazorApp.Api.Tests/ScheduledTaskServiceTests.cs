using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ScheduledTaskServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly ScheduledTaskLogService _taskLogService;

    public ScheduledTaskServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        CreateScheduledTaskLogTable(_db);
        _taskLogService = new ScheduledTaskLogService(
            CreateSqlSugarContext(_db),
            NullLogger<ScheduledTaskLogService>.Instance
        );
    }

    [Fact]
    public async Task ExecuteHourlyTask_前序任务失败时_仍为每个子任务创建独立作用域并继续执行后续任务()
    {
        var dataSyncService = new Mock<IDataSyncReactService>(MockBehavior.Strict);
        dataSyncService
            .Setup(x => x.SyncPosmProductSupplierMappingsIncrementalAsync())
            .ThrowsAsync(new InvalidOperationException("sync boom"));

        var cacheWarmer = new Mock<IStoreOrderCacheWarmer>(MockBehavior.Strict);
        var cacheWarmExecuted = false;
        cacheWarmer
            .Setup(x => x.WarmUpHomePageAsync())
            .Callback(() => cacheWarmExecuted = true)
            .Returns(Task.CompletedTask);

        var scopeFactory = CreateScopeFactory(
            CreateScope(new Dictionary<Type, object?>()),
            CreateScope(
                new Dictionary<Type, object?>
                {
                    [typeof(ScheduledTaskLogService)] = _taskLogService,
                    [typeof(IDataSyncReactService)] = dataSyncService.Object,
                }
            ),
            CreateScope(
                new Dictionary<Type, object?>
                {
                    [typeof(ScheduledTaskLogService)] = _taskLogService,
                    [typeof(SalesStatisticsJobService)] = new InvalidOperationException("stats boom"),
                }
            ),
            CreateScope(
                new Dictionary<Type, object?>
                {
                    [typeof(ScheduledTaskLogService)] = _taskLogService,
                    [typeof(IStoreOrderCacheWarmer)] = cacheWarmer.Object,
                }
            )
        );

        var service = CreateScheduledTaskService(scopeFactory.Object, NullLogger<ScheduledTaskService>.Instance);

        await InvokeExecuteHourlyTaskAsync(service);

        Assert.Equal(4, scopeFactory.Invocations.Count(x => x.Method.Name == nameof(IServiceScopeFactory.CreateScope)));
        Assert.True(cacheWarmExecuted);
        cacheWarmer.Verify(x => x.WarmUpHomePageAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteHourlyTask_同步服务返回失败结果时_不应覆盖成成功日志()
    {
        var dataSyncService = new Mock<IDataSyncReactService>(MockBehavior.Strict);
        dataSyncService
            .Setup(x => x.SyncPosmProductSupplierMappingsIncrementalAsync())
            .ReturnsAsync(
                new SyncResult
                {
                    IsSuccess = false,
                    Message = "同步失败: 事务已完成",
                }
            );

        var cacheWarmer = new Mock<IStoreOrderCacheWarmer>(MockBehavior.Strict);
        cacheWarmer.Setup(x => x.WarmUpHomePageAsync()).Returns(Task.CompletedTask);

        var scopeFactory = CreateScopeFactory(
            CreateScope(new Dictionary<Type, object?>()),
            CreateScope(
                new Dictionary<Type, object?>
                {
                    [typeof(ScheduledTaskLogService)] = _taskLogService,
                    [typeof(IDataSyncReactService)] = dataSyncService.Object,
                }
            ),
            CreateScope(
                new Dictionary<Type, object?>
                {
                    [typeof(ScheduledTaskLogService)] = _taskLogService,
                    [typeof(SalesStatisticsJobService)] = new InvalidOperationException("stats boom"),
                }
            ),
            CreateScope(
                new Dictionary<Type, object?>
                {
                    [typeof(ScheduledTaskLogService)] = _taskLogService,
                    [typeof(IStoreOrderCacheWarmer)] = cacheWarmer.Object,
                }
            )
        );

        var service = CreateScheduledTaskService(scopeFactory.Object, NullLogger<ScheduledTaskService>.Instance);

        await InvokeExecuteHourlyTaskAsync(service);

        var syncTaskLog = await _db.Queryable<ScheduledTaskLog>()
            .SingleAsync(x => x.TaskType == TaskType.SyncPosmProductSupplierMappingsIncremental);
        Assert.Equal(BlazorApp.Shared.Models.HBweb.TaskStatus.Failed, syncTaskLog.Status);
        Assert.Contains("事务已完成", syncTaskLog.ErrorMessage);
    }

    [Fact]
    public async Task TryLogTaskFailureAsync_记录失败日志再次异常时_应吞掉异常并写入错误日志()
    {
        var logger = new TestLogger<ScheduledTaskService>();
        var helper = typeof(ScheduledTaskService).GetMethod(
            "TryLogTaskFailureAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.NotNull(helper);

        var task = helper!.Invoke(
            null,
            new object[]
            {
                (Func<Task>)(() => throw new InvalidOperationException("log boom")),
                logger,
                "商品-供应商映射增量同步",
            }
        ) as Task;

        Assert.NotNull(task);
        await task!;

        Assert.Contains(
            logger.Entries,
            entry =>
                entry.LogLevel == LogLevel.Error
                && entry.Message.Contains("记录任务失败日志时发生异常", StringComparison.Ordinal)
                && entry.Message.Contains("商品-供应商映射增量同步", StringComparison.Ordinal)
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

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

    private static ScheduledTaskService CreateScheduledTaskService(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledTaskService> logger
    )
    {
        var service = (ScheduledTaskService)RuntimeHelpers.GetUninitializedObject(
            typeof(ScheduledTaskService)
        );

        typeof(ScheduledTaskService)
            .GetField("_scopeFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, scopeFactory);
        typeof(ScheduledTaskService)
            .GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, logger);
        typeof(ScheduledTaskService)
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, new ScheduledTaskOptions());
        typeof(ScheduledTaskService)
            .GetField("_sydneyTimeZone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, CreateAllowedTimeZone());

        return service;
    }

    private static async Task InvokeExecuteHourlyTaskAsync(ScheduledTaskService service)
    {
        var method = typeof(ScheduledTaskService).GetMethod(
            "ExecuteHourlyTask",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var task = method!.Invoke(service, null) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private static TimeZoneInfo CreateAllowedTimeZone()
    {
        var targetHour = 10;
        var currentHour = DateTime.UtcNow.Hour;
        var offsetHours = targetHour - currentHour;

        if (offsetHours < -12)
        {
            offsetHours += 24;
        }
        else if (offsetHours > 14)
        {
            offsetHours -= 24;
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            $"TestZone_{offsetHours}",
            TimeSpan.FromHours(offsetHours),
            "TestZone",
            "TestZone"
        );
    }

    private static Mock<IServiceScopeFactory> CreateScopeFactory(params IServiceScope[] scopes)
    {
        var queue = new Queue<IServiceScope>(scopes);
        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopeFactory.Setup(x => x.CreateScope()).Returns(() => queue.Dequeue());
        return scopeFactory;
    }

    private static IServiceScope CreateScope(Dictionary<Type, object?> services)
    {
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider
            .Setup(x => x.GetService(It.IsAny<Type>()))
            .Returns<Type>(serviceType =>
            {
                if (!services.TryGetValue(serviceType, out var service))
                {
                    return null;
                }

                if (service is Exception exception)
                {
                    throw exception;
                }

                return service;
            });

        var scope = new Mock<IServiceScope>(MockBehavior.Strict);
        scope.SetupGet(x => x.ServiceProvider).Returns(provider.Object);
        scope.Setup(x => x.Dispose());
        return scope.Object;
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose() { }
    }
}
