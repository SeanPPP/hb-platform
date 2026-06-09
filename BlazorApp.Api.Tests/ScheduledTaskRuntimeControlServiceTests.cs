using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ScheduledTaskRuntimeControlServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public ScheduledTaskRuntimeControlServiceTests()
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
        _db.CodeFirst.InitTables<ScheduledTaskRuntimeControl, ScheduledTaskInstanceState>();
    }

    [Fact]
    public async Task GetStatusAsync_控制记录不存在时_只返回只读状态且不创建控制记录()
    {
        var service = CreateService("api-a");

        var status = await service.GetStatusAsync();
        var control = await _db.Queryable<ScheduledTaskRuntimeControl>()
            .Where(x => x.Id == ScheduledTaskRuntimeControl.DefaultId)
            .FirstAsync();

        Assert.Null(status.ActiveInstanceId);
        Assert.False(status.EffectiveSchedulerEnabled);
        Assert.Null(control);
        Assert.Empty(await _db.Queryable<ScheduledTaskInstanceState>().ToListAsync());
    }

    [Fact]
    public async Task IsCurrentInstanceSchedulerEnabledAsync_控制记录不存在且配置启用时_应把当前实例写为调度实例()
    {
        var service = CreateService("api-a");

        var enabled = await service.IsCurrentInstanceSchedulerEnabledAsync();
        var control = await _db.Queryable<ScheduledTaskRuntimeControl>()
            .Where(x => x.Id == ScheduledTaskRuntimeControl.DefaultId)
            .FirstAsync();

        Assert.True(enabled);
        Assert.Equal("api-a", control.ActiveInstanceId);
        Assert.Single(await _db.Queryable<ScheduledTaskInstanceState>().ToListAsync());
    }

    [Fact]
    public async Task IsCurrentInstanceSchedulerEnabledAsync_当前实例配置禁用时_不创建控制记录()
    {
        var service = CreateService("api-a", enabled: false);

        var enabled = await service.IsCurrentInstanceSchedulerEnabledAsync();
        var control = await _db.Queryable<ScheduledTaskRuntimeControl>()
            .Where(x => x.Id == ScheduledTaskRuntimeControl.DefaultId)
            .FirstAsync();

        Assert.False(enabled);
        Assert.Null(control);
        Assert.Single(await _db.Queryable<ScheduledTaskInstanceState>().ToListAsync());
    }

    [Fact]
    public async Task IsCurrentInstanceSchedulerEnabledAsync_控制记录未指定调度实例时_当前实例不可执行调度()
    {
        await _db.Insertable(new ScheduledTaskRuntimeControl
        {
            Id = ScheduledTaskRuntimeControl.DefaultId,
            SchedulerEnabled = true,
            ActiveInstanceId = null,
            UpdatedAtUtc = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var service = CreateService("api-a");

        var enabled = await service.IsCurrentInstanceSchedulerEnabledAsync();
        var status = await service.GetStatusAsync();

        Assert.False(enabled);
        Assert.False(status.EffectiveSchedulerEnabled);
        Assert.Null(status.ActiveInstanceId);
    }

    [Fact]
    public async Task IsCurrentInstanceSchedulerEnabledAsync_多实例场景下_只有调度实例可执行调度()
    {
        var activeService = CreateService("api-a");
        var passiveService = CreateService("api-b");

        var activeEnabled = await activeService.IsCurrentInstanceSchedulerEnabledAsync();
        var passiveEnabled = await passiveService.IsCurrentInstanceSchedulerEnabledAsync();
        var passiveStatus = await passiveService.GetStatusAsync();

        Assert.True(activeEnabled);
        Assert.False(passiveEnabled);
        Assert.Equal("api-a", passiveStatus.ActiveInstanceId);
        Assert.False(passiveStatus.EffectiveSchedulerEnabled);
    }

    [Fact]
    public async Task IsCurrentInstanceSchedulerEnabledAsync_重复触达同一实例时_不应抛出主键冲突()
    {
        var service = CreateService("api-a");

        var firstEnabled = await service.IsCurrentInstanceSchedulerEnabledAsync();
        var secondEnabled = await service.IsCurrentInstanceSchedulerEnabledAsync();

        Assert.True(firstEnabled);
        Assert.True(secondEnabled);
        Assert.Single(await _db.Queryable<ScheduledTaskInstanceState>().ToListAsync());
    }

    [Fact]
    public async Task IsCurrentInstanceSchedulerEnabledAsync_选中其他实例时_当前实例不可执行调度()
    {
        var service = CreateService("api-a");

        await service.UpdateControlAsync(
            new ScheduledTaskRuntimeControlUpdateDto
            {
                SchedulerEnabled = true,
                ActiveInstanceId = "api-b",
            },
            "admin"
        );

        var enabled = await service.IsCurrentInstanceSchedulerEnabledAsync();

        Assert.False(enabled);
    }

    [Fact]
    public async Task UpdateControlAsync_切换到当前实例后_状态返回当前实例可执行调度()
    {
        var service = CreateService("api-a");

        var status = await service.UpdateControlAsync(
            new ScheduledTaskRuntimeControlUpdateDto
            {
                SchedulerEnabled = true,
                ActiveInstanceId = "api-a",
            },
            "admin"
        );

        Assert.True(status.EffectiveSchedulerEnabled);
        Assert.Equal("api-a", status.ActiveInstanceId);
        Assert.Contains(status.KnownInstances, instance => instance.InstanceId == "api-a");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private ScheduledTaskRuntimeControlService CreateService(string instanceId, bool enabled = true)
    {
        var options = Options.Create(
            new ScheduledTaskOptions
            {
                Enabled = enabled,
                InstanceId = instanceId,
            }
        );

        return new ScheduledTaskRuntimeControlService(
            CreateSqlSugarContext(_db),
            options,
            NullLogger<ScheduledTaskRuntimeControlService>.Instance
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        typeof(SqlSugarContext)
            .GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
