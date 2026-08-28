using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;
using TaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;
using TaskTrigger = BlazorApp.Shared.Models.HBweb.TaskTrigger;
using TaskType = BlazorApp.Shared.Models.HBweb.TaskType;

namespace BlazorApp.Api.Tests;

[CollectionDefinition(nameof(ProductStoreDailyStatisticQueueCollection), DisableParallelization = true)]
public sealed class ProductStoreDailyStatisticQueueCollection { }

[Collection(nameof(ProductStoreDailyStatisticQueueCollection))]
public sealed class ProductStoreDailyStatisticQueueServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly SqlSugarContext _context;
    private readonly ScheduledTaskLogService _taskLogService;
    private readonly List<ServiceProvider> _providers = new();
    private readonly List<SqlSugarClient> _independentDbs = new();
    private readonly List<string> _additionalDbPaths = new();

    public ProductStoreDailyStatisticQueueServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables<SalesStatisticRefreshState, ScheduledTaskLease>();
        CreateScheduledTaskLogTable(_db);
        _context = CreateSqlSugarContext(_db);
        _taskLogService = new ScheduledTaskLogService(
            _context,
            NullLogger<ScheduledTaskLogService>.Instance
        );
    }

    [Fact]
    public void TaskParameters_商品每日重算复用兼容的CustomParameters日期清单()
    {
        var parameters = new TaskParameters
        {
            CustomParameters = new Dictionary<string, object>
            {
                ["dates"] = new List<string> { "2025-01-01", "2025-01-02" },
            },
        };
        var log = new ScheduledTaskLog();
        log.SetParameters(parameters);

        var restored = log.GetParameters();

        Assert.NotNull(restored.CustomParameters);
        Assert.True(restored.CustomParameters!.ContainsKey("dates"));
        Assert.NotNull(typeof(TaskType).GetField("RecalculateProductStoreDaily"));
    }

    [Fact]
    public async Task EnqueueAsync_31天_同事务写入真实任务日志清单和排队状态()
    {
        var queue = CreateQueue();
        var dates = Enumerable.Range(0, 31)
            .Select(offset => new DateTime(2026, 1, 31).AddDays(-offset))
            .Append(new DateTime(2026, 1, 1));

        var result = await queue.EnqueueAsync(dates, "admin", 4);

        Assert.NotEqual(Guid.Empty, result.JobId);
        Assert.Equal(31, result.SubmittedDates.Count);
        Assert.Empty(result.SkippedDates);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, result.Status);
        var taskLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == result.JobId);
        Assert.Equal(TaskType.RecalculateProductStoreDaily, taskLog.TaskType);
        Assert.Equal(TaskStatus.Running, taskLog.Status);
        Assert.Equal(TaskTrigger.Manual, taskLog.TriggeredBy);
        Assert.False(taskLog.CanRetry);
        var taskParameters = taskLog.GetParameters();
        Assert.Equal("2026-01-01", taskParameters.StartDate);
        Assert.Equal("2026-01-31", taskParameters.EndDate);
        Assert.Equal(4, taskParameters.MaxConcurrency);
        Assert.Equal(
            result.SubmittedDates.Select(date => date.ToString("yyyy-MM-dd")),
            ReadManifestDates(taskParameters)
        );
        var states = await _db.Queryable<SalesStatisticRefreshState>()
            .Where(x => x.StatisticType == SalesStatisticType.ProductStoreDaily)
            .OrderBy(x => x.Date)
            .ToListAsync();
        Assert.Equal(31, states.Count);
        Assert.All(states, state =>
        {
            Assert.Equal(result.JobId, state.JobId);
            Assert.Equal(SalesStatisticRefreshStatus.Queued, state.Status);
            Assert.Equal("admin", state.RequestedBy);
        });
    }

    [Fact]
    public async Task EnqueueAsync_32天_拒绝且不创建空任务日志()
    {
        var queue = CreateQueue();
        var dates = Enumerable.Range(0, 32)
            .Select(offset => new DateTime(2026, 2, 1).AddDays(offset));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.EnqueueAsync(dates, "admin")
        );

        Assert.Contains("31", error.Message);
        Assert.Equal(0, await _db.Queryable<ScheduledTaskLog>().CountAsync());
    }

    [Fact]
    public async Task EnqueueYearBackfillAsync_365天_保留完整不可变清单()
    {
        var queue = CreateQueue();
        var endDate = new DateTime(2025, 12, 31);
        var dates = Enumerable.Range(0, 365).Select(offset => endDate.AddDays(-offset));

        var result = await queue.EnqueueYearBackfillAsync(dates, "admin", 8);

        Assert.Equal(365, result.SubmittedDates.Count);
        var taskLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == result.JobId);
        var taskParameters = taskLog.GetParameters();
        Assert.Equal(365, ReadManifestDates(taskParameters).Count);
        Assert.Equal(1, taskParameters.MaxConcurrency);
    }

    [Fact]
    public async Task EnqueueYearBackfillAsync_366天_拒绝且不建日志()
    {
        var queue = CreateQueue();
        var dates = Enumerable.Range(0, 366)
            .Select(offset => new DateTime(2025, 12, 31).AddDays(-offset));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.EnqueueYearBackfillAsync(dates, "admin")
        );

        Assert.Contains("365", error.Message);
        Assert.Equal(0, await _db.Queryable<ScheduledTaskLog>().CountAsync());
    }

    [Fact]
    public async Task EnqueueAsync_空日期_拒绝且不建日志()
    {
        var queue = CreateQueue();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.EnqueueAsync(Array.Empty<DateTime>(), "admin")
        );

        Assert.Equal(0, await _db.Queryable<ScheduledTaskLog>().CountAsync());
    }

    [Fact]
    public async Task EnqueueAsync_终态日期关联Running日志_仍视为Active且复用真实JobId()
    {
        var date = new DateTime(2026, 3, 1);
        var jobId = Guid.NewGuid();
        await SeedTaskLogAsync(jobId, new[] { date });
        await SeedStateAsync(date, SalesStatisticRefreshStatus.Fresh, jobId);
        var queue = CreateQueue();

        var result = await queue.EnqueueAsync(new[] { date }, "admin");

        Assert.Equal(jobId, result.JobId);
        Assert.Empty(result.SubmittedDates);
        Assert.Equal(new[] { date }, result.SkippedDates);
        Assert.Equal(SalesStatisticRefreshStatus.Running, result.Status);
        Assert.Equal(1, await _db.Queryable<ScheduledTaskLog>().CountAsync());
    }

    [Fact]
    public async Task EnqueueAsync_全跳过且横跨多个Active任务_返回完整任务清单而不伪造单一JobId()
    {
        var firstDate = new DateTime(2026, 3, 3);
        var secondDate = new DateTime(2026, 3, 4);
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();
        await SeedTaskLogAsync(firstJobId, new[] { firstDate });
        await SeedTaskLogAsync(secondJobId, new[] { secondDate });
        await SeedStateAsync(firstDate, SalesStatisticRefreshStatus.Queued, firstJobId);
        await SeedStateAsync(secondDate, SalesStatisticRefreshStatus.Running, secondJobId);
        var queue = CreateQueue();

        var result = await queue.EnqueueAsync(new[] { firstDate, secondDate }, "admin");

        Assert.Equal(Guid.Empty, result.JobId);
        Assert.Empty(result.SubmittedDates);
        Assert.Equal(new[] { firstDate, secondDate }, result.SkippedDates);
        Assert.Equal(
            new[] { firstJobId, secondJobId }.OrderBy(id => id),
            result.ActiveJobIds.OrderBy(id => id)
        );
        Assert.Contains("2 个活动", result.Message);
        Assert.Equal(2, await _db.Queryable<ScheduledTaskLog>().CountAsync());
    }

    [Fact]
    public async Task EnqueueAsync_状态写入失败_真实日志和状态一起回滚()
    {
        _db.Ado.ExecuteCommand(
            """
            CREATE TRIGGER RejectProductQueueInsert
            BEFORE INSERT ON SalesStatisticRefreshState
            BEGIN
                SELECT RAISE(ABORT, 'queue-state-rejected');
            END;
            """
        );
        var queue = CreateQueue();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            queue.EnqueueAsync(new[] { new DateTime(2026, 3, 2) }, "admin")
        );

        Assert.Equal(0, await _db.Queryable<ScheduledTaskLog>().CountAsync());
        Assert.Equal(0, await _db.Queryable<SalesStatisticRefreshState>().CountAsync());
    }

    [Fact]
    public async Task DrainOnceAsync_条件Claim后只执行一次并完成日志()
    {
        var date = new DateTime(2026, 4, 1);
        var expectedJobId = Guid.Empty;
        var executor = new RecordingExecutor(async (_, jobId, guard, _) =>
        {
            Assert.Equal(expectedJobId, jobId);
            await guard();
            var state = await _db.Queryable<SalesStatisticRefreshState>()
                .SingleAsync(x => x.Date == date && x.StatisticType == SalesStatisticType.ProductStoreDaily);
            var updated = await _db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(x => x.Status == SalesStatisticRefreshStatus.Fresh)
                .SetColumns(x => x.CompletedAtUtc == DateTime.UtcNow)
                .Where(x =>
                    x.Date == date
                    && x.StatisticType == SalesStatisticType.ProductStoreDaily
                    && x.JobId == state.JobId
                    && x.Status == SalesStatisticRefreshStatus.Running
                )
                .ExecuteCommandAsync();
            Assert.Equal(1, updated);
        });
        var cacheWarmer = CreateCacheWarmer();
        var queue = CreateQueue(executor, cacheWarmer.Object);
        var submit = await queue.EnqueueAsync(new[] { date }, "admin");
        expectedJobId = submit.JobId;

        var first = await queue.DrainOnceAsync();
        var second = await queue.DrainOnceAsync();
        var finalized = await queue.FinalizeJobsAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, finalized);
        Assert.Equal(1, executor.CallCount);
        var state = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == date && x.StatisticType == SalesStatisticType.ProductStoreDaily);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state.Status);
        Assert.Equal(submit.JobId, state.JobId);
        var taskLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == submit.JobId);
        Assert.Equal(TaskStatus.Success, taskLog.Status);
        var lease = await _db.Queryable<ScheduledTaskLease>()
            .SingleAsync(x => x.ScopeKey == "2026-04-01");
        Assert.Equal(ScheduledTaskLeaseStatus.Success, lease.Status);
        cacheWarmer.Verify(x => x.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task DrainOnceAsync_缓存清理异常_仍把计算租约完成为Success()
    {
        var date = new DateTime(2026, 4, 3);
        var executor = new RecordingExecutor(async (_, jobId, guard, _) =>
        {
            await guard();
            var updated = await _db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(x => x.Status == SalesStatisticRefreshStatus.Fresh)
                .SetColumns(x => x.CompletedAtUtc == DateTime.UtcNow)
                .Where(x =>
                    x.StatisticType == SalesStatisticType.ProductStoreDaily
                    && x.Date == date
                    && x.JobId == jobId
                    && x.Status == SalesStatisticRefreshStatus.Running
                )
                .ExecuteCommandAsync();
            Assert.Equal(1, updated);
        });
        var cacheWarmer = new Mock<ISalesDashboardCacheWarmer>();
        cacheWarmer.Setup(x => x.ClearCacheAsync())
            .ThrowsAsync(new InvalidOperationException("cache-unavailable"));
        var queue = CreateQueue(executor, cacheWarmer.Object);
        await queue.EnqueueAsync(new[] { date }, "admin");

        var processed = await queue.DrainOnceAsync();

        Assert.Equal(1, processed);
        var lease = await _db.Queryable<ScheduledTaskLease>()
            .SingleAsync(x =>
                x.TaskType == SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType
                && x.ScopeKey == "2026-04-03"
            );
        Assert.Equal(ScheduledTaskLeaseStatus.Success, lease.Status);
        cacheWarmer.Verify(x => x.ClearCacheAsync(), Times.Once);
    }

    [Fact]
    public async Task DrainOnceAsync_计算租约完成失败_保留Fresh并等待租约失效后再终结日志()
    {
        var date = new DateTime(2026, 4, 4);
        var executor = new RecordingExecutor(async (_, jobId, guard, _) =>
        {
            await guard();
            var updated = await _db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(x => x.Status == SalesStatisticRefreshStatus.Fresh)
                .SetColumns(x => x.CompletedAtUtc == DateTime.UtcNow)
                .Where(x =>
                    x.StatisticType == SalesStatisticType.ProductStoreDaily
                    && x.Date == date
                    && x.JobId == jobId
                    && x.Status == SalesStatisticRefreshStatus.Running
                )
                .ExecuteCommandAsync();
            Assert.Equal(1, updated);
            await _db.Updateable<ScheduledTaskLease>()
                .SetColumns(x => x.LeaseToken == "ownership-changed")
                .Where(x =>
                    x.TaskType == SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType
                    && x.ScopeKey == "2026-04-04"
                )
                .ExecuteCommandAsync();
        });
        var queue = CreateQueue(executor);
        var submit = await queue.EnqueueAsync(new[] { date }, "admin");

        var processed = await queue.DrainOnceAsync();
        var finalizedWhileLeaseActive = await queue.FinalizeJobsAsync();

        Assert.Equal(1, processed);
        Assert.Equal(0, finalizedWhileLeaseActive);
        var state = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == date && x.JobId == submit.JobId);
        Assert.Equal(SalesStatisticRefreshStatus.Fresh, state.Status);
        var taskLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == submit.JobId);
        Assert.Equal(TaskStatus.Running, taskLog.Status);
        var lease = await _db.Queryable<ScheduledTaskLease>()
            .SingleAsync(x => x.ScopeKey == "2026-04-04");
        Assert.Equal(ScheduledTaskLeaseStatus.Running, lease.Status);

        await _db.Updateable<ScheduledTaskLease>()
            .SetColumns(x => x.LeaseUntilUtc == DateTime.UtcNow.AddMinutes(-1))
            .Where(x => x.ScopeKey == "2026-04-04")
            .ExecuteCommandAsync();
        Assert.Equal(1, await queue.FinalizeJobsAsync());
    }

    [Fact]
    public async Task DrainOnceAsync_2025全局租约阻止两个实例执行不同日期()
    {
        var firstDate = new DateTime(2025, 8, 1);
        var secondDate = new DateTime(2025, 8, 2);
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SqlSugarClient firstDb = null!;
        var firstExecutor = new RecordingExecutor(async (_, jobId, guard, _) =>
        {
            await guard();
            firstEntered.TrySetResult(true);
            await releaseFirst.Task;
            await guard();
            var updated = await firstDb.Updateable<SalesStatisticRefreshState>()
                .SetColumns(x => x.Status == SalesStatisticRefreshStatus.Fresh)
                .Where(x =>
                    x.StatisticType == SalesStatisticType.ProductStoreDaily
                    && x.Date == firstDate
                    && x.JobId == jobId
                    && x.Status == SalesStatisticRefreshStatus.Running
                )
                .ExecuteCommandAsync();
            Assert.Equal(1, updated);
        });
        var secondExecutor = new RecordingExecutor(async (_, jobId, guard, _) =>
        {
            await guard();
            var updated = await _db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(x => x.Status == SalesStatisticRefreshStatus.Fresh)
                .Where(x =>
                    x.StatisticType == SalesStatisticType.ProductStoreDaily
                    && x.Date == secondDate
                    && x.JobId == jobId
                    && x.Status == SalesStatisticRefreshStatus.Running
                )
                .ExecuteCommandAsync();
            Assert.Equal(1, updated);
        });
        var firstQueueAndDb = CreateIndependentQueue(firstExecutor, "queue-2025-a");
        var secondQueueAndDb = CreateIndependentQueue(secondExecutor, "queue-2025-b");
        firstDb = firstQueueAndDb.Db;
        await firstQueueAndDb.Queue.EnqueueAsync(new[] { firstDate }, "admin-a");
        await secondQueueAndDb.Queue.EnqueueAsync(new[] { secondDate }, "admin-b");

        var firstDrain = firstQueueAndDb.Queue.DrainOnceAsync();
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        int secondProcessed;
        try
        {
            secondProcessed = await secondQueueAndDb.Queue.DrainOnceAsync();
        }
        finally
        {
            releaseFirst.TrySetResult(true);
        }
        var firstProcessed = await firstDrain;

        Assert.Equal(1, firstProcessed);
        Assert.Equal(0, secondProcessed);
        Assert.Equal(0, secondExecutor.CallCount);
        var secondState = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == secondDate);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, secondState.Status);
        var globalLease = await _db.Queryable<ScheduledTaskLease>()
            .SingleAsync(x =>
                x.TaskType == "RecalculateProductStoreDaily2025Serial"
                && x.ScopeKey == "product-store-daily-2025-global"
            );
        Assert.Equal(ScheduledTaskLeaseStatus.Success, globalLease.Status);
    }

    [Fact]
    public async Task DrainOnceAsync_首项Claim后取消_本批已Claim状态条件退回Queued()
    {
        var dates = new[] { new DateTime(2026, 4, 5), new DateTime(2026, 4, 6) };
        var executor = new RecordingExecutor((_, _, _, _) =>
            throw new InvalidOperationException("claim 阶段取消时不应执行")
        );
        var queue = CreateQueue(executor);
        var submit = await queue.EnqueueAsync(dates, "admin", 2);
        using var cancellation = new CancellationTokenSource();
        var cancelledAfterFirstClaim = false;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                !cancelledAfterFirstClaim
                && sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("SalesStatisticRefreshState", StringComparison.OrdinalIgnoreCase)
            )
            {
                cancelledAfterFirstClaim = true;
                cancellation.Cancel();
            }
        };

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                queue.DrainOnceAsync(cancellation.Token)
            );
        }
        finally
        {
            _db.Aop.OnLogExecuting = null;
        }

        Assert.True(cancelledAfterFirstClaim);
        Assert.Equal(0, executor.CallCount);
        var states = await _db.Queryable<SalesStatisticRefreshState>()
            .Where(x => x.JobId == submit.JobId)
            .OrderBy(x => x.Date)
            .ToListAsync();
        Assert.Equal(2, states.Count);
        Assert.All(states, state => Assert.Equal(SalesStatisticRefreshStatus.Queued, state.Status));
    }

    [Fact]
    public async Task DrainOnceAsync_存在有效全刷新租约_条件退回Queued且不执行()
    {
        var date = new DateTime(2026, 4, 2);
        var executor = new RecordingExecutor((_, _, _, _) =>
            throw new InvalidOperationException("已有租约时不应执行")
        );
        var queue = CreateQueue(executor, instanceId: "queue-worker");
        var submit = await queue.EnqueueAsync(new[] { date }, "admin");
        await _db.Insertable(new ScheduledTaskLease
        {
            TaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
            ScopeKey = "2026-04-02",
            Status = ScheduledTaskLeaseStatus.Running,
            OwnerInstanceId = "another-worker",
            LeaseToken = "active-token",
            LeaseUntilUtc = DateTime.UtcNow.AddHours(1),
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var processed = await queue.DrainOnceAsync();

        Assert.Equal(0, processed);
        Assert.Equal(0, executor.CallCount);
        var state = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == date && x.StatisticType == SalesStatisticType.ProductStoreDaily);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, state.Status);
        Assert.Equal(submit.JobId, state.JobId);
    }

    [Fact]
    public async Task RecoverExpiredRunningClaimsAsync_超过两小时且无租约_保留JobId退回Queued()
    {
        var date = new DateTime(2026, 5, 1);
        var jobId = Guid.NewGuid();
        await SeedTaskLogAsync(jobId, new[] { date });
        await SeedStateAsync(date, SalesStatisticRefreshStatus.Running, jobId, DateTime.UtcNow.AddHours(-3));
        var queue = CreateQueue();

        var recovered = await queue.RecoverExpiredRunningClaimsAsync();

        Assert.Equal(1, recovered);
        var state = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == date && x.StatisticType == SalesStatisticType.ProductStoreDaily);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, state.Status);
        Assert.Equal(jobId, state.JobId);
        Assert.Null(state.StartedAtUtc);
    }

    [Fact]
    public async Task RecoverExpiredRunningClaimsAsync_Queued永不被改为Pending()
    {
        var date = new DateTime(2026, 5, 2);
        var jobId = Guid.NewGuid();
        await SeedTaskLogAsync(jobId, new[] { date });
        await SeedStateAsync(date, SalesStatisticRefreshStatus.Queued, jobId, DateTime.UtcNow.AddDays(-1));
        var queue = CreateQueue();

        var recovered = await queue.RecoverExpiredRunningClaimsAsync();

        Assert.Equal(0, recovered);
        var state = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == date && x.StatisticType == SalesStatisticType.ProductStoreDaily);
        Assert.Equal(SalesStatisticRefreshStatus.Queued, state.Status);
        Assert.Equal(jobId, state.JobId);
    }

    [Fact]
    public async Task RecoverExpiredRunningClaimsAsync_有效租约保护过期Running不被接管()
    {
        var date = new DateTime(2026, 5, 3);
        var jobId = Guid.NewGuid();
        await SeedTaskLogAsync(jobId, new[] { date });
        await SeedStateAsync(date, SalesStatisticRefreshStatus.Running, jobId, DateTime.UtcNow.AddHours(-3));
        await _db.Insertable(new ScheduledTaskLease
        {
            TaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
            ScopeKey = "2026-05-03",
            Status = ScheduledTaskLeaseStatus.Running,
            OwnerInstanceId = "active-worker",
            LeaseToken = "active-token",
            LeaseUntilUtc = DateTime.UtcNow.AddMinutes(30),
            StartedAtUtc = DateTime.UtcNow.AddHours(-3),
            UpdatedAtUtc = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var queue = CreateQueue();

        var recovered = await queue.RecoverExpiredRunningClaimsAsync();

        Assert.Equal(0, recovered);
        var state = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == date && x.StatisticType == SalesStatisticType.ProductStoreDaily);
        Assert.Equal(SalesStatisticRefreshStatus.Running, state.Status);
    }

    [Fact]
    public async Task RecoverExpiredRunningClaimsAsync_旧Orphan先建立宽限水位再补同Id日志()
    {
        var date = new DateTime(2026, 5, 4);
        var jobId = Guid.NewGuid();
        await _db.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = date,
            Status = SalesStatisticRefreshStatus.Queued,
            JobId = jobId,
            RequestedAtUtc = null,
            StartedAtUtc = null,
            LastCheckedAtUtc = null,
        }).ExecuteCommandAsync();
        var queue = CreateQueue();

        var firstProgress = await queue.RecoverExpiredRunningClaimsAsync();

        Assert.Equal(1, firstProgress);
        Assert.Equal(0, await _db.Queryable<ScheduledTaskLog>().CountAsync());
        var watermarked = await _db.Queryable<SalesStatisticRefreshState>()
            .SingleAsync(x => x.Date == date && x.StatisticType == SalesStatisticType.ProductStoreDaily);
        Assert.NotNull(watermarked.LastCheckedAtUtc);
        await _db.Updateable<SalesStatisticRefreshState>()
            .SetColumns(x => x.LastCheckedAtUtc == DateTime.UtcNow.AddHours(-3))
            .Where(x => x.Date == date && x.JobId == jobId)
            .ExecuteCommandAsync();

        var secondProgress = await queue.RecoverExpiredRunningClaimsAsync();

        Assert.Equal(1, secondProgress);
        var recoveredLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == jobId);
        Assert.Equal(TaskType.RecalculateProductStoreDaily, recoveredLog.TaskType);
        Assert.False(recoveredLog.CanRetry);
        Assert.Equal(new[] { "2026-05-04" }, ReadManifestDates(recoveredLog.GetParameters()));
    }

    [Fact]
    public async Task RecoverExpiredRunningClaimsAsync_确认Orphan后按JobId把Failed和Queued都写入Manifest()
    {
        var failedDate = new DateTime(2026, 5, 6);
        var queuedDate = new DateTime(2026, 5, 7);
        var jobId = Guid.NewGuid();
        var oldWatermark = DateTime.UtcNow.AddHours(-3);
        await SeedStateAsync(failedDate, SalesStatisticRefreshStatus.Failed, jobId, oldWatermark);
        await SeedStateAsync(queuedDate, SalesStatisticRefreshStatus.Queued, jobId, oldWatermark);
        var queue = CreateQueue();

        var progress = await queue.RecoverExpiredRunningClaimsAsync();

        Assert.Equal(1, progress);
        var recoveredLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == jobId);
        Assert.Equal(
            new[] { "2026-05-06", "2026-05-07" },
            ReadManifestDates(recoveredLog.GetParameters())
        );
    }

    [Fact]
    public async Task FinalizeJobsAsync_无Pending且存在Failed_严格完成失败日志()
    {
        var date = new DateTime(2026, 5, 5);
        var jobId = Guid.NewGuid();
        await SeedTaskLogAsync(jobId, new[] { date });
        await _db.Updateable<ScheduledTaskLog>()
            .SetColumns(x => x.CanRetry == true)
            .Where(x => x.Id == jobId)
            .ExecuteCommandAsync();
        await SeedStateAsync(date, SalesStatisticRefreshStatus.Failed, jobId);
        await _db.Updateable<SalesStatisticRefreshState>()
            .SetColumns(x => x.ErrorMessage == "来源水位变化")
            .Where(x => x.Date == date && x.JobId == jobId)
            .ExecuteCommandAsync();
        var queue = CreateQueue();

        var finalized = await queue.FinalizeJobsAsync();

        Assert.Equal(1, finalized);
        var taskLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == jobId);
        Assert.Equal(TaskStatus.Failed, taskLog.Status);
        Assert.False(taskLog.CanRetry);
        Assert.Equal(0, taskLog.RetryCount);
        Assert.Contains("2026-05-05", taskLog.ErrorMessage);
        Assert.Contains("来源水位变化", taskLog.ErrorMessage);
    }

    [Fact]
    public async Task FinalizeJobsAsync_Manifest日期仍有有效全刷新租约_保持Running()
    {
        var date = new DateTime(2026, 5, 8);
        var jobId = Guid.NewGuid();
        await SeedTaskLogAsync(jobId, new[] { date });
        await SeedStateAsync(date, SalesStatisticRefreshStatus.Fresh, jobId);
        await _db.Insertable(new ScheduledTaskLease
        {
            TaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
            ScopeKey = "2026-05-08",
            Status = ScheduledTaskLeaseStatus.Running,
            OwnerInstanceId = "active-worker",
            LeaseToken = "active-token",
            LeaseUntilUtc = DateTime.UtcNow.AddMinutes(30),
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var queue = CreateQueue();

        var finalized = await queue.FinalizeJobsAsync();

        Assert.Equal(0, finalized);
        var log = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == jobId);
        Assert.Equal(TaskStatus.Running, log.Status);
    }

    [Fact]
    public async Task FinalizeJobsAsync_两个Context并发只终结一次且只发布一次Completion()
    {
        var date = new DateTime(2026, 5, 9);
        var jobId = Guid.NewGuid();
        await SeedTaskLogAsync(jobId, new[] { date });
        await SeedStateAsync(date, SalesStatisticRefreshStatus.Fresh, jobId);
        var performanceDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _additionalDbPaths.Add(performanceDbPath);
        var performanceDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={performanceDbPath}",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });
        _independentDbs.Add(performanceDb);
        performanceDb.CodeFirst.InitTables<PerformanceOperationalRunTransitionOutbox>();
        var performanceServices = new ServiceCollection();
        performanceServices.AddScoped(_ => CreateSqlSugarContext(performanceDb));
        var performanceProvider = performanceServices.BuildServiceProvider();
        _providers.Add(performanceProvider);
        PerformanceOperationalRunBridge.Configure(new PerformanceOperationalRunQueue(
            performanceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PerformanceOperationalRunQueue>.Instance
        ));
        var first = CreateIndependentQueue(instanceId: "finalizer-a");
        var second = CreateIndependentQueue(instanceId: "finalizer-b");
        using var barrier = new Barrier(2);
        var firstWaited = false;
        var secondWaited = false;
        first.Db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (!firstWaited && IsScheduledTaskLogUpdate(sql))
            {
                firstWaited = true;
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            }
        };
        second.Db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (!secondWaited && IsScheduledTaskLogUpdate(sql))
            {
                secondWaited = true;
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            }
        };

        try
        {
            var results = await Task.WhenAll(
                Task.Run(() => first.Queue.FinalizeJobsAsync()),
                Task.Run(() => second.Queue.FinalizeJobsAsync())
            );

            Assert.True(firstWaited);
            Assert.True(secondWaited);
            Assert.Equal(1, results.Sum());
            var log = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == jobId);
            Assert.Equal(TaskStatus.Success, log.Status);
            var completionEvent = Assert.Single(
                await performanceDb.Queryable<PerformanceOperationalRunTransitionOutbox>()
                    .ToListAsync()
            );
            Assert.Equal(jobId.ToString("N"), completionEvent.ExternalRunId);
            Assert.Equal("success", completionEvent.Status);
        }
        finally
        {
            first.Db.Aop.OnLogExecuting = null;
            second.Db.Aop.OnLogExecuting = null;
            PerformanceOperationalRunBridge.Configure(null);
        }
    }

    [Theory]
    [InlineData("missing", "缺少")]
    [InlineData("invalid-json", "JSON")]
    [InlineData("mixed-invalid-date", "日期")]
    public async Task FinalizeJobsAsync_Manifest损坏_失败日志和关联状态后允许重新提交(
        string corruption,
        string expectedDiagnostic
    )
    {
        var dates = new[] { new DateTime(2026, 5, 10), new DateTime(2026, 5, 11) };
        var jobId = Guid.NewGuid();
        var rawParameters = corruption switch
        {
            "missing" => null,
            "invalid-json" => "{not-json",
            "mixed-invalid-date" => JsonSerializer.Serialize(new TaskParameters
            {
                CustomParameters = new Dictionary<string, object>
                {
                    ["dates"] = new[] { "2026-05-10", "not-a-date" },
                },
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
        await SeedRawTaskLogAsync(jobId, rawParameters);
        await SeedStateAsync(dates[0], SalesStatisticRefreshStatus.Queued, jobId);
        await SeedStateAsync(dates[1], SalesStatisticRefreshStatus.Running, jobId, DateTime.UtcNow);
        var queue = CreateQueue();

        var finalized = await queue.FinalizeJobsAsync();

        Assert.Equal(1, finalized);
        var failedLog = await _db.Queryable<ScheduledTaskLog>().SingleAsync(x => x.Id == jobId);
        Assert.Equal(TaskStatus.Failed, failedLog.Status);
        Assert.Contains(expectedDiagnostic, failedLog.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var failedStates = await _db.Queryable<SalesStatisticRefreshState>()
            .Where(x => x.JobId == jobId)
            .ToListAsync();
        Assert.Equal(2, failedStates.Count);
        Assert.All(failedStates, state =>
        {
            Assert.Equal(SalesStatisticRefreshStatus.Failed, state.Status);
            Assert.Contains(expectedDiagnostic, state.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        });

        var retry = await queue.EnqueueAsync(dates, "admin");
        Assert.NotEqual(Guid.Empty, retry.JobId);
        Assert.Equal(2, retry.SubmittedDates.Count);
    }

    [Fact]
    public async Task Controller_提交响应的TaskId和JobId都指向真实日志()
    {
        var jobId = Guid.NewGuid();
        var queue = new Mock<IProductStoreDailyStatisticQueueService>();
        queue.Setup(x => x.EnqueueAsync(
                It.IsAny<IEnumerable<DateTime>>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new ProductStoreDailyRecalculationSubmitResult
            {
                JobId = jobId,
                SubmittedDates = new List<DateTime> { new(2026, 6, 1) },
                Status = SalesStatisticRefreshStatus.Queued,
                Message = "已排队",
            });
        var controller = CreateController(queue.Object);

        var response = await controller.TriggerProductStoreDailyStatistics(
            new ProductStoreDailyJobTriggerRequest { Date = new DateTime(2026, 6, 1) }
        );

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(jobId, ReadAnonymousProperty<Guid>(ok.Value, "taskId"));
        Assert.Equal(jobId, ReadAnonymousProperty<Guid>(ok.Value, "jobId"));
    }

    [Fact]
    public async Task Controller_年度补算366天返回BadRequest且不提交队列()
    {
        var queue = new Mock<IProductStoreDailyStatisticQueueService>(MockBehavior.Strict);
        var controller = CreateController(queue.Object);

        var response = await controller.BackfillProductStoreDailyYear(
            new ProductStoreDailyYearBackfillRequest { Days = 366 }
        );

        Assert.IsType<BadRequestObjectResult>(response);
        queue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Controller_多个Active任务时TaskId和JobId为空并返回ActiveTaskIds()
    {
        var activeJobIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var queue = new Mock<IProductStoreDailyStatisticQueueService>();
        queue.Setup(x => x.EnqueueAsync(
                It.IsAny<IEnumerable<DateTime>>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new ProductStoreDailyRecalculationSubmitResult
            {
                JobId = Guid.Empty,
                ActiveJobIds = activeJobIds,
                SkippedDates = new List<DateTime>
                {
                    new(2026, 6, 1),
                    new(2026, 6, 2),
                },
                Status = SalesStatisticRefreshStatus.Running,
                Message = "所选日期分属 2 个活动任务",
            });
        var controller = CreateController(queue.Object);

        var response = await controller.BatchProductStoreDailyStatistics(
            new BatchProductStoreDailyUpdateRequest
            {
                StartDate = new DateTime(2026, 6, 1),
                EndDate = new DateTime(2026, 6, 2),
            }
        );

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Null(ok.Value!.GetType().GetProperty("taskId")!.GetValue(ok.Value));
        Assert.Null(ok.Value.GetType().GetProperty("jobId")!.GetValue(ok.Value));
        Assert.Equal(
            activeJobIds.OrderBy(id => id),
            ReadAnonymousProperty<List<Guid>>(ok.Value, "activeTaskIds").OrderBy(id => id)
        );
    }

    [Fact]
    public void 队列和HostedDrainer不得使用TaskRun或FireAndForget()
    {
        var root = FindRepositoryRoot();
        var queueSource = File.ReadAllText(Path.Combine(
            root,
            "services/backend/BlazorApp.Api/Services/Background/ProductStoreDailyStatisticQueueService.cs"
        ));
        var recoverySource = File.ReadAllText(Path.Combine(
            root,
            "services/backend/BlazorApp.Api/Services/Background/ProductStoreDailyStatisticRecoveryService.cs"
        ));

        Assert.DoesNotContain("Task.Run", queueSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", recoverySource, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Task", queueSource, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        PerformanceOperationalRunBridge.Configure(null);
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
        foreach (var independentDb in _independentDbs)
        {
            independentDb.Dispose();
        }
        foreach (var path in _additionalDbPaths)
        {
            SqliteTempFileCleanup.DeleteIfExists(path);
        }
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private ProductStoreDailyStatisticQueueService CreateQueue(
        IProductStoreDailyStatisticExecutor? executor = null,
        ISalesDashboardCacheWarmer? cacheWarmer = null,
        string instanceId = "queue-test"
    )
    {
        return CreateQueueForDb(_db, executor, cacheWarmer, instanceId);
    }

    private (ProductStoreDailyStatisticQueueService Queue, SqlSugarClient Db) CreateIndependentQueue(
        IProductStoreDailyStatisticExecutor? executor = null,
        string instanceId = "queue-independent"
    )
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={_dbPath};Cache=Shared;Default Timeout=10",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });
        _independentDbs.Add(db);
        return (CreateQueueForDb(db, executor, null, instanceId), db);
    }

    private ProductStoreDailyStatisticQueueService CreateQueueForDb(
        ISqlSugarClient db,
        IProductStoreDailyStatisticExecutor? executor,
        ISalesDashboardCacheWarmer? cacheWarmer,
        string instanceId
    )
    {
        executor ??= new RecordingExecutor((_, _, _, _) =>
            throw new InvalidOperationException("此测试不应执行队列日期")
        );
        cacheWarmer ??= CreateCacheWarmer().Object;
        var context = CreateSqlSugarContext(db);
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateSqlSugarContext(db));
        services.AddScoped(_ => new ScheduledTaskLeaseService(
            CreateSqlSugarContext(db),
            Options.Create(new ScheduledTaskOptions { InstanceId = instanceId }),
            NullLogger<ScheduledTaskLeaseService>.Instance
        ));
        services.AddSingleton(executor);
        services.AddSingleton(cacheWarmer);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return new ProductStoreDailyStatisticQueueService(
            context,
            new ScheduledTaskLogService(context, NullLogger<ScheduledTaskLogService>.Instance),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProductStoreDailyStatisticQueueService>.Instance
        );
    }

    private StatisticsJobTriggerController CreateController(IProductStoreDailyStatisticQueueService queue)
    {
        var controller = new StatisticsJobTriggerController(
            (SalesStatisticsJobService)RuntimeHelpers.GetUninitializedObject(typeof(SalesStatisticsJobService)),
            _taskLogService,
            _context,
            NullLogger<StatisticsJobTriggerController>.Instance,
            queue,
            (SalesStatisticsAlignmentService)RuntimeHelpers.GetUninitializedObject(typeof(SalesStatisticsAlignmentService)),
            (SalesStatisticsAlignmentBackgroundRecalculateService)RuntimeHelpers.GetUninitializedObject(
                typeof(SalesStatisticsAlignmentBackgroundRecalculateService)
            )
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private async Task SeedTaskLogAsync(Guid jobId, IEnumerable<DateTime> dates)
    {
        var manifest = dates.Select(date => date.Date).Distinct().OrderBy(date => date).ToList();
        var log = new ScheduledTaskLog
        {
            Id = jobId,
            TaskType = TaskType.RecalculateProductStoreDaily,
            Status = TaskStatus.Running,
            StartedAt = DateTime.UtcNow.AddHours(-3),
            ScheduledTime = DateTime.UtcNow.AddHours(-3),
            TriggeredBy = TaskTrigger.Manual,
            ErrorMessage = string.Empty,
        };
        log.SetParameters(new TaskParameters
        {
            StartDate = manifest.First().ToString("yyyy-MM-dd"),
            EndDate = manifest.Last().ToString("yyyy-MM-dd"),
            MaxConcurrency = 3,
            CustomParameters = new Dictionary<string, object>
            {
                ["dates"] = manifest.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
            },
        });
        await _db.Insertable(log).ExecuteCommandAsync();
    }

    private Task SeedRawTaskLogAsync(Guid jobId, string? rawParameters)
    {
        return _db.Insertable(new ScheduledTaskLog
        {
            Id = jobId,
            TaskType = TaskType.RecalculateProductStoreDaily,
            TaskParameters = rawParameters,
            Status = TaskStatus.Running,
            StartedAt = DateTime.UtcNow.AddHours(-3),
            ScheduledTime = DateTime.UtcNow.AddHours(-3),
            TriggeredBy = TaskTrigger.Manual,
            ErrorMessage = string.Empty,
        }).ExecuteCommandAsync();
    }

    private Task SeedStateAsync(
        DateTime date,
        string status,
        Guid jobId,
        DateTime? startedAtUtc = null
    )
    {
        return _db.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily,
            Date = date.Date,
            Status = status,
            JobId = jobId,
            RequestedBy = "admin",
            RequestedAtUtc = startedAtUtc ?? DateTime.UtcNow,
            StartedAtUtc = status == SalesStatisticRefreshStatus.Running ? startedAtUtc : null,
            LastCheckedAtUtc = startedAtUtc,
        }).ExecuteCommandAsync();
    }

    private static Mock<ISalesDashboardCacheWarmer> CreateCacheWarmer()
    {
        var mock = new Mock<ISalesDashboardCacheWarmer>();
        mock.Setup(x => x.ClearCacheAsync()).Returns(Task.CompletedTask);
        return mock;
    }

    private static IReadOnlyList<string> ReadManifestDates(TaskParameters parameters)
    {
        Assert.NotNull(parameters.CustomParameters);
        Assert.True(parameters.CustomParameters!.TryGetValue("dates", out var rawDates));
        return rawDates switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.Array => element
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToList(),
            IEnumerable<string> values => values.ToList(),
            _ => throw new Xunit.Sdk.XunitException("任务清单 dates 不是可识别的字符串数组"),
        };
    }

    private static T ReadAnonymousProperty<T>(object? value, string propertyName)
    {
        Assert.NotNull(value);
        var property = value!.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(value));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "services/backend/BlazorApp.Api/Program.cs")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("无法定位 hb-platform 仓库根目录");
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

    private static bool IsScheduledTaskLogUpdate(string sql)
    {
        return sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("ScheduledTaskLog", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingExecutor : IProductStoreDailyStatisticExecutor
    {
        private readonly Func<DateTime, Guid, Func<Task>, CancellationToken, Task> _handler;

        public RecordingExecutor(
            Func<DateTime, Guid, Func<Task>, CancellationToken, Task> handler
        )
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public async Task ExecuteQueuedDateAsync(
            DateTime date,
            Guid expectedJobId,
            Func<Task> validateExecutionOwnershipAsync,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            await _handler(date, expectedJobId, validateExecutionOwnershipAsync, cancellationToken);
        }
    }
}
