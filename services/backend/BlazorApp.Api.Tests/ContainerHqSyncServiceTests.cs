using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerHqSyncServiceTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;
    private readonly IMapper _mapper;

    public ContainerHqSyncServiceTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
        _mapper = CreateMapper();

        // 只初始化货柜 HQ 同步核心依赖的最小表，避免测试基建过重。
        _localDb.CodeFirst.InitTables(typeof(Container), typeof(ContainerDetail));
        _hqDb.CodeFirst.InitTables(
            typeof(CPT_RED_货柜单主表Store),
            typeof(CPT_RED_货柜单详情表Store)
        );
    }

    [Fact]
    public async Task SyncIncrementalAsync_旧装柜日期但最后修改日期新_应该同步主表()
    {
        var startDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqContainerAsync(
            "C-OLD-LOAD-NEW-MODIFY",
            "CONT-001",
            loadingDate: startDate.AddDays(-30),
            lastModifyDate: startDate.AddDays(1)
        );

        var result = await CreateService().SyncIncrementalAsync(startDate);

        Assert.True(result.IsSuccess, result.Message);
        var local = await _localDb.Queryable<Container>()
            .SingleAsync(x => x.ContainerCode == "C-OLD-LOAD-NEW-MODIFY");
        Assert.Equal("CONT-001", local.ContainerNumber);
    }

    [Fact]
    public async Task SyncIncrementalAsync_只有明细最后修改日期新_应该同步父主表和完整明细快照()
    {
        var startDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqContainerAsync(
            "C-DETAIL-CHANGED",
            "CONT-DETAIL",
            loadingDate: startDate.AddDays(-40),
            lastModifyDate: startDate.AddDays(-20)
        );
        await SeedHqDetailAsync(
            "D-CHANGED",
            "C-DETAIL-CHANGED",
            "P-001",
            startDate.AddDays(1)
        );
        await SeedHqDetailAsync(
            "D-SNAPSHOT",
            "C-DETAIL-CHANGED",
            "P-002",
            startDate.AddDays(-20)
        );

        var result = await CreateService().SyncIncrementalAsync(startDate);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(await _localDb.Queryable<Container>().SingleAsync(x => x.ContainerCode == "C-DETAIL-CHANGED"));
        var details = await _localDb.Queryable<ContainerDetail>()
            .Where(x => x.ContainerCode == "C-DETAIL-CHANGED")
            .OrderBy(x => x.DetailCode)
            .ToListAsync();
        Assert.Equal(new[] { "D-CHANGED", "D-SNAPSHOT" }, details.Select(x => x.DetailCode).ToArray());
    }

    [Fact]
    public async Task SyncIncrementalAsync_HQ快照缺少本地明细_应该软删本地缺失明细()
    {
        var startDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalContainerAsync("C-SOFT-DELETE");
        await SeedLocalDetailAsync("D-KEEP", "C-SOFT-DELETE");
        await SeedLocalDetailAsync("D-DELETE", "C-SOFT-DELETE");
        await SeedHqContainerAsync(
            "C-SOFT-DELETE",
            "CONT-SOFT",
            loadingDate: startDate.AddDays(-10),
            lastModifyDate: startDate.AddDays(1)
        );
        await SeedHqDetailAsync("D-KEEP", "C-SOFT-DELETE", "P-KEEP", startDate.AddDays(1));

        var result = await CreateService().SyncIncrementalAsync(startDate);

        Assert.True(result.IsSuccess, result.Message);
        var deleted = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-DELETE");
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task SyncIncrementalAsync_HQ明细缺HGUID_应该返回数据质量错误且不写DETAIL回退主键()
    {
        var startDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqContainerAsync(
            "C-BAD-DETAIL",
            "CONT-BAD",
            loadingDate: startDate.AddDays(-10),
            lastModifyDate: startDate.AddDays(1)
        );
        await SeedHqDetailAsync(null, "C-BAD-DETAIL", "P-BAD", startDate.AddDays(1));

        var result = await CreateService().SyncIncrementalAsync(startDate);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContainerHqSyncErrorCodes.InvalidSourceData, result.ErrorCode);
        var localDetails = await _localDb.Queryable<ContainerDetail>().ToListAsync();
        Assert.DoesNotContain(
            localDetails,
            x => x.DetailCode.StartsWith("DETAIL_", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task SyncIncrementalAsync_明细写入失败时_应该回滚主表和明细事务()
    {
        var startDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqContainerAsync(
            "C-ROLLBACK",
            "CONT-ROLLBACK",
            loadingDate: startDate.AddDays(-10),
            lastModifyDate: startDate.AddDays(1)
        );
        await SeedHqDetailAsync("D-DUPLICATE", "C-ROLLBACK", "P-001", startDate.AddDays(1));
        await SeedHqDetailAsync("D-DUPLICATE", "C-ROLLBACK", "P-002", startDate.AddDays(1));

        var result = await CreateService().SyncIncrementalAsync(startDate);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContainerHqSyncErrorCodes.InternalError, result.ErrorCode);
        Assert.Empty(await _localDb.Queryable<Container>().Where(x => x.ContainerCode == "C-ROLLBACK").ToListAsync());
        Assert.Empty(await _localDb.Queryable<ContainerDetail>().Where(x => x.ContainerCode == "C-ROLLBACK").ToListAsync());
    }

    [Fact]
    public async Task SyncIncrementalAsync_已有同步持有锁时_第二次同步返回409语义()
    {
        var syncLock = GetSyncLock();
        await syncLock.WaitAsync();
        try
        {
            var result = await CreateService(lockWaitSeconds: 0).SyncIncrementalAsync(DateTime.UtcNow);

            Assert.False(result.IsSuccess);
            Assert.Equal(ContainerHqSyncErrorCodes.Conflict, result.ErrorCode);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hqConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
    }

    private ContainerHqSyncService CreateService(int lockWaitSeconds = 1)
    {
        var localContext = CreateSqlSugarContext(_localDb);
        return new ContainerHqSyncService(
            localContext,
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            _mapper,
            NullLogger<ContainerHqSyncService>.Instance,
            new ScheduledTaskLogService(
                localContext,
                NullLogger<ScheduledTaskLogService>.Instance
            ),
            Options.Create(
                new ContainerHqSyncOptions
                {
                    HqReadBatchSize = 50,
                    LocalContainerBatchSize = 20,
                    WriteBatchSize = 20,
                    LockWaitSeconds = lockWaitSeconds,
                }
            )
        );
    }

    private async Task SeedHqContainerAsync(
        string containerCode,
        string containerNumber,
        DateTime loadingDate,
        DateTime lastModifyDate
    )
    {
        await _hqDb.Insertable(
            new CPT_RED_货柜单主表Store
            {
                HGUID = containerCode,
                货柜编号 = containerNumber,
                装柜日期 = loadingDate,
                预计到岸日期 = loadingDate.AddDays(28),
                合计件数 = 1,
                合计数量 = 1,
                合计金额 = 10,
                总体积 = 2,
                状态 = 1,
                FGC_Creator = "hq",
                FGC_CreateDate = loadingDate,
                FGC_LastModifier = "hq",
                FGC_LastModifyDate = lastModifyDate,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqDetailAsync(
        string? detailCode,
        string containerCode,
        string productCode,
        DateTime lastModifyDate
    )
    {
        await _hqDb.Insertable(
            new CPT_RED_货柜单详情表Store
            {
                HGUID = detailCode,
                主表GUID = containerCode,
                商品编码 = productCode,
                装柜类型 = "单品",
                装柜件数 = 1,
                装柜数量 = 1,
                国内价格 = 10,
                合计装柜金额 = 10,
                合计装柜体积 = 1,
                状态 = 1,
                FGC_Creator = "hq",
                FGC_CreateDate = lastModifyDate.AddDays(-1),
                FGC_LastModifier = "hq",
                FGC_LastModifyDate = lastModifyDate,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalContainerAsync(string containerCode)
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = containerCode,
                ContainerNumber = $"LOCAL-{containerCode}",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-10),
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalDetailAsync(string detailCode, string containerCode)
    {
        await _localDb.Insertable(
            new ContainerDetail
            {
                DetailCode = detailCode,
                ContainerCode = containerCode,
                ProductCode = $"LOCAL-{detailCode}",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-10),
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private static SemaphoreSlim GetSyncLock()
    {
        var field = typeof(ContainerHqSyncService).GetField(
            "SyncLock",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        return Assert.IsType<SemaphoreSlim>(field!.GetValue(null));
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(
            cfg =>
            {
                cfg.AddProfile<ReactContainerMappingProfile>();
                cfg.AddProfile<ReactContainerDetailProfile>();
            },
            NullLoggerFactory.Instance
        );
        return configuration.CreateMapper();
    }

    private static IConfiguration CreateHqConfiguration(string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StoreHzgHQConnection"] = connectionString,
                }
            )
            .Build();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(
        ISqlSugarClient db,
        IConfiguration configuration
    )
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);

        var configurationField = typeof(HqSqlSugarContext).GetField(
            "<Configuration>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        configurationField!.SetValue(context, configuration);
        return context;
    }
}
