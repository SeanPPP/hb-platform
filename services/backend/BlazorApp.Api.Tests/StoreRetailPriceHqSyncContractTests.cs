using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public class StoreRetailPriceHqSyncContractTests
{
    [Fact]
    public void SyncFromHq_允许Admin和管理员角色调用()
    {
        var method = typeof(ReactStoreProductPricesController).GetMethod(
            nameof(ReactStoreProductPricesController.SyncFromHq)
        );

        var authorizeAttribute = Assert.Single(
            method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
        );
        Assert.Equal("Admin,管理员", ((AuthorizeAttribute)authorizeAttribute).Roles);
    }

    [Fact]
    public async Task SyncFromHq_结束日期早于起始日期_返回BadRequest()
    {
        var controller = CreateController();
        var request = new SyncRetailPriceFromHqRequest
        {
            SelectedStoreCodes = new List<string> { "S01" },
            StartDate = new DateTime(2026, 5, 31),
            EndDate = new DateTime(2026, 5, 30),
        };

        var response = await controller.SyncFromHq(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<SyncRetailPriceFromHqResult>>(badRequest.Value);
        Assert.False(payload.Success);
        Assert.Equal("INVALID_DATE_RANGE", payload.ErrorCode);
    }

    [Fact]
    public async Task SyncFromHq_未选择分店_返回BadRequest且不调用服务()
    {
        var retailPriceService = new Mock<IStoreRetailPriceReactService>(MockBehavior.Strict);
        var controller = CreateController(retailPriceService.Object);
        var request = new SyncRetailPriceFromHqRequest
        {
            SelectedStoreCodes = new List<string>(),
            StartDate = new DateTime(2026, 5, 1),
            EndDate = new DateTime(2026, 5, 31),
        };

        var response = await controller.SyncFromHq(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<SyncRetailPriceFromHqResult>>(badRequest.Value);
        Assert.False(payload.Success);
        Assert.Equal("INVALID_STORE_SCOPE", payload.ErrorCode);
        retailPriceService.Verify(
            service => service.SyncFromHqAsync(
                It.IsAny<List<string>?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>()
            ),
            Times.Never
        );
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SyncFromHq_缺少起止日期_返回BadRequest且不调用服务(
        bool hasStartDate,
        bool hasEndDate
    )
    {
        var retailPriceService = new Mock<IStoreRetailPriceReactService>(MockBehavior.Strict);
        var controller = CreateController(retailPriceService.Object);
        var request = new SyncRetailPriceFromHqRequest
        {
            SelectedStoreCodes = new List<string> { "S01" },
            StartDate = hasStartDate ? new DateTime(2026, 5, 1) : null,
            EndDate = hasEndDate ? new DateTime(2026, 5, 31) : null,
        };

        var response = await controller.SyncFromHq(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<SyncRetailPriceFromHqResult>>(badRequest.Value);
        Assert.False(payload.Success);
        Assert.Equal("INVALID_DATE_RANGE", payload.ErrorCode);
        retailPriceService.Verify(
            service => service.SyncFromHqAsync(
                It.IsAny<List<string>?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task SyncFromHq_服务失败时_返回非2xx响应()
    {
        var retailPriceService = new Mock<IStoreRetailPriceReactService>();
        retailPriceService
            .Setup(service =>
                service.SyncFromHqAsync(
                    It.IsAny<List<string>?>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>()
                )
            )
            .ReturnsAsync(
                ApiResponse<SyncRetailPriceFromHqResult>.Error(
                    "HQ连接失败",
                    "HQ_RETAIL_PRICE_SYNC_ERROR"
                )
            );

        var controller = CreateController(retailPriceService.Object);
        var request = new SyncRetailPriceFromHqRequest
        {
            SelectedStoreCodes = new List<string> { "S01" },
            StartDate = new DateTime(2026, 5, 1),
            EndDate = new DateTime(2026, 5, 31),
        };

        var response = await controller.SyncFromHq(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<SyncRetailPriceFromHqResult>>(badRequest.Value);
        Assert.False(payload.Success);
        Assert.Equal("HQ_RETAIL_PRICE_SYNC_ERROR", payload.ErrorCode);
    }

    [Fact]
    public async Task SyncForPageAsync_未知分店_返回校验错误且不退化为全部分店()
    {
        await using var localConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await localConnection.OpenAsync();
        await using var hqConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await hqConnection.OpenAsync();
        using var localDb = new SqlSugarClient(CreateConnectionConfig(localConnection.ConnectionString));
        using var hqDb = new SqlSugarClient(CreateConnectionConfig(hqConnection.ConnectionString));
        localDb.CodeFirst.InitTables(typeof(Store), typeof(StoreRetailPrice));
        CreateScheduledTaskLogTable(localDb);
        hqDb.CodeFirst.InitTables(typeof(DIC_商品零售价表));
        await localDb.Insertable(new Store
        {
            StoreGUID = "store-S01",
            StoreCode = "S01",
            StoreName = "S01",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await hqDb.Insertable(new DIC_商品零售价表
        {
            ID = 1,
            HGUID = "retail-S01-P01",
            H分店代码 = "S01",
            H商品编码 = "P01",
            H分店商品编码 = "S01-P01",
            H供应商编码 = "SUP01",
            H进货价 = 1.2m,
            H分店零售价 = 2.4m,
            H折扣率 = 0.5m,
            H使用状态 = true,
            FGC_CreateDate = new DateTime(2026, 5, 1),
            FGC_LastModifyDate = new DateTime(2026, 5, 20),
        }).ExecuteCommandAsync();
        var service = CreateHqSyncService(localDb, hqDb, CreateMapper());

        var result = await service.SyncForPageAsync(
            new List<string> { "UNKNOWN" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_STORE_SCOPE", result.ErrorCode);
        Assert.False(await localDb.Queryable<StoreRetailPrice>().AnyAsync());
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("S02")]
    public async Task SyncForPageAsync_混入未知或停用分店_整体拒绝且不部分同步(string invalidStoreCode)
    {
        await using var localConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await localConnection.OpenAsync();
        await using var hqConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await hqConnection.OpenAsync();
        using var localDb = new SqlSugarClient(CreateConnectionConfig(localConnection.ConnectionString));
        using var hqDb = new SqlSugarClient(CreateConnectionConfig(hqConnection.ConnectionString));
        localDb.CodeFirst.InitTables(typeof(Store), typeof(StoreRetailPrice));
        hqDb.CodeFirst.InitTables(typeof(DIC_商品零售价表));
        await localDb.Insertable(new[]
        {
            new Store
            {
                StoreGUID = "store-S01",
                StoreCode = "S01",
                StoreName = "S01",
                IsActive = true,
                IsDeleted = false,
            },
            new Store
            {
                StoreGUID = "store-S02",
                StoreCode = "S02",
                StoreName = "S02",
                IsActive = false,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await hqDb.Insertable(new DIC_商品零售价表
        {
            ID = 1,
            HGUID = "retail-S01-P01",
            H分店代码 = "S01",
            H商品编码 = "P01",
            H分店商品编码 = "S01-P01",
            H供应商编码 = "SUP01",
            H进货价 = 1.2m,
            H分店零售价 = 2.4m,
            H折扣率 = 0.5m,
            H使用状态 = true,
            FGC_CreateDate = new DateTime(2026, 5, 1),
            FGC_LastModifyDate = new DateTime(2026, 5, 1),
        }).ExecuteCommandAsync();
        var service = CreateHqSyncService(localDb, hqDb, CreateMapper());

        var result = await service.SyncForPageAsync(
            new List<string> { "S01", invalidStoreCode },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_STORE_SCOPE", result.ErrorCode);
        Assert.False(await localDb.Queryable<StoreRetailPrice>().AnyAsync());
    }

    [Fact]
    public async Task SyncForPageAsync_按分店和日期新增更新且不删除HQ缺失本地行()
    {
        await using var localConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await localConnection.OpenAsync();
        await using var hqConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await hqConnection.OpenAsync();
        using var localDb = new SqlSugarClient(CreateConnectionConfig(localConnection.ConnectionString));
        using var hqDb = new SqlSugarClient(CreateConnectionConfig(hqConnection.ConnectionString));
        localDb.CodeFirst.InitTables(typeof(Store), typeof(StoreRetailPrice));
        CreateScheduledTaskLogTable(localDb);
        hqDb.CodeFirst.InitTables(typeof(DIC_商品零售价表));
        await localDb.Insertable(new Store
        {
            StoreGUID = "store-S01",
            StoreCode = "S01",
            StoreName = "S01",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await localDb.Insertable(new StoreRetailPrice
        {
            UUID = "retail-keep",
            StoreCode = "S01",
            ProductCode = "KEEP",
            SupplierCode = "SUP-OLD",
            PurchasePrice = 1.00m,
            StoreRetailPriceValue = 2.00m,
            IsDeleted = false,
            CreatedAt = new DateTime(2026, 4, 1),
            UpdatedAt = new DateTime(2026, 4, 1),
        }).ExecuteCommandAsync();
        await localDb.Insertable(new StoreRetailPrice
        {
            UUID = "retail-update",
            StoreCode = "S01",
            ProductCode = "P02",
            SupplierCode = "SUP-OLD",
            PurchasePrice = 2.00m,
            StoreRetailPriceValue = 4.00m,
            IsDeleted = false,
            CreatedAt = new DateTime(2026, 4, 1),
            UpdatedAt = new DateTime(2026, 4, 1),
        }).ExecuteCommandAsync();
        await hqDb.Insertable(new[]
        {
            new DIC_商品零售价表
            {
                ID = 1,
                HGUID = "retail-add",
                H分店代码 = "S01",
                H商品编码 = "P01",
                H分店商品编码 = "S01-P01",
                H供应商编码 = "SUP01",
                H进货价 = 1.2m,
                H分店零售价 = 2.4m,
                H折扣率 = 0.5m,
                H使用状态 = true,
                FGC_CreateDate = new DateTime(2026, 5, 1),
                FGC_LastModifyDate = new DateTime(2026, 5, 1),
            },
            new DIC_商品零售价表
            {
                ID = 2,
                HGUID = "retail-update",
                H分店代码 = "S01",
                H商品编码 = "P02",
                H分店商品编码 = "S01-P02",
                H供应商编码 = "SUP02",
                H进货价 = 3.3m,
                H分店零售价 = 6.6m,
                H折扣率 = 0.7m,
                H使用状态 = true,
                FGC_CreateDate = new DateTime(2026, 5, 1),
                FGC_LastModifyDate = new DateTime(2026, 5, 21),
            },
            new DIC_商品零售价表
            {
                ID = 3,
                HGUID = "retail-outside-date",
                H分店代码 = "S01",
                H商品编码 = "OUTSIDE",
                H分店商品编码 = "S01-OUTSIDE",
                H供应商编码 = "SUP03",
                H进货价 = 9.9m,
                H分店零售价 = 19.9m,
                H折扣率 = 0.9m,
                H使用状态 = true,
                FGC_CreateDate = new DateTime(2026, 4, 1),
                FGC_LastModifyDate = new DateTime(2026, 4, 15),
            },
        }).ExecuteCommandAsync();
        var service = CreateHqSyncService(localDb, hqDb, CreateMapper());

        var result = await service.SyncForPageAsync(
            new List<string> { "S01" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.AddedCount);
        Assert.Equal(1, result.Data.UpdatedCount);
        Assert.Equal(3, await localDb.Queryable<StoreRetailPrice>().CountAsync());
        Assert.True(await localDb.Queryable<StoreRetailPrice>().AnyAsync(x => x.UUID == "retail-keep" && x.IsDeleted == false));
        Assert.False(await localDb.Queryable<StoreRetailPrice>().AnyAsync(x => x.ProductCode == "OUTSIDE"));
        var updated = await localDb.Queryable<StoreRetailPrice>().SingleAsync(x => x.UUID == "retail-update");
        Assert.Equal("SUP02", updated.SupplierCode);
        Assert.Equal(6.6m, updated.StoreRetailPriceValue);
    }

    [Fact]
    public void StoreRetailPriceHqMapping_使用总部供应商编码和折扣率字段()
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<ReactStoreRetailPriceMappingProfile>(),
            NullLoggerFactory.Instance
        ).CreateMapper();
        var hq = new DIC_商品零售价表
        {
            HGUID = "retail-1",
            H分店代码 = "S01",
            H商品编码 = "P01",
            H分店商品编码 = "S01-P01",
            H供应商编码 = "SUP-HQ",
            H分店供应商编码 = "SUP-STORE",
            H进货价 = 3.2m,
            H分店零售价 = 5.6m,
            H折扣率 = 0.87m,
            H动态销售数量 = 9,
            H使用状态 = true,
            H是否自动定价 = true,
            H是否特殊商品 = false,
            FGC_CreateDate = new DateTime(2026, 5, 1),
            FGC_LastModifyDate = new DateTime(2026, 5, 31),
        };

        var local = mapper.Map<StoreRetailPrice>(hq);

        Assert.Equal("SUP-HQ", local.SupplierCode);
        Assert.Equal(0.87m, local.DiscountRate);
    }

    [Fact]
    public async Task UpsertBatchAsync_总部GUID已存在但供应商编码变化时_应该更新旧行避免重复主键()
    {
        await using var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        var db = new SqlSugarClient(CreateConnectionConfig(connection.ConnectionString));
        db.CodeFirst.InitTables(typeof(StoreRetailPrice));

        await db.Insertable(
            new StoreRetailPrice
            {
                UUID = "002E2605-6B96-4B78-A71A-149F1041CA24",
                StoreCode = "1025",
                ProductCode = "P001",
                StoreProductCode = "1025-P001-OLD",
                SupplierCode = "SUP-OLD",
                PurchasePrice = 1.00m,
                StoreRetailPriceValue = 2.00m,
                DiscountRate = 0.5m,
                IsActive = false,
                IsAutoPricing = false,
                IsSpecialProduct = false,
                IsDeleted = true,
                CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        ).ExecuteCommandAsync();

        var service = CreateHqSyncService();
        var method = typeof(StoreRetailPriceHqSyncService).GetMethod(
            "UpsertBatchAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var incoming = new List<StoreRetailPrice>
        {
            new()
            {
                UUID = "002E2605-6B96-4B78-A71A-149F1041CA24",
                StoreCode = "1025",
                ProductCode = "P001",
                StoreProductCode = "1025-P001-NEW",
                SupplierCode = "SUP-NEW",
                PurchasePrice = 3.00m,
                StoreRetailPriceValue = 6.00m,
                DiscountRate = 0.8m,
                IsActive = true,
                IsAutoPricing = true,
                IsSpecialProduct = true,
                IsDeleted = false,
                UpdatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        };

        var task = Assert.IsAssignableFrom<Task<BatchResultDto>>(
            method.Invoke(service, new object[] { db, incoming })
        );
        var result = await task;

        var rows = await db.Queryable<StoreRetailPrice>().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(0, result.Inserted);
        Assert.Equal(1, result.Updated);
        Assert.Equal("SUP-NEW", row.SupplierCode);
        Assert.Equal("1025-P001-NEW", row.StoreProductCode);
        Assert.Equal(6.00m, row.StoreRetailPriceValue);
        Assert.False(row.IsDeleted);
    }

    private static ReactStoreProductPricesController CreateController(
        IStoreRetailPriceReactService? retailPriceService = null
    )
    {
        return new ReactStoreProductPricesController(
            Mock.Of<IStoreProductPriceReactService>(),
            retailPriceService ?? Mock.Of<IStoreRetailPriceReactService>(),
            Mock.Of<IUserService>(),
            Mock.Of<ILogger<ReactStoreProductPricesController>>()
        );
    }

    private static StoreRetailPriceHqSyncService CreateHqSyncService()
    {
        return new StoreRetailPriceHqSyncService(
            (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext)),
            (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext)),
            Mock.Of<IMapper>(),
            NullLogger<StoreRetailPriceHqSyncService>.Instance,
            null!,
            Options.Create(new StoreRetailPriceHqSyncOptions { WriteBatchSize = 100 })
        );
    }

    private static StoreRetailPriceHqSyncService CreateHqSyncService(
        ISqlSugarClient localDb,
        ISqlSugarClient hqDb,
        IMapper mapper
    )
    {
        var localContext = CreateSqlSugarContext(localDb);
        return new StoreRetailPriceHqSyncService(
            localContext,
            CreateHqSqlSugarContext(hqDb),
            mapper,
            NullLogger<StoreRetailPriceHqSyncService>.Instance,
            new ScheduledTaskLogService(
                localContext,
                NullLogger<ScheduledTaskLogService>.Instance
            ),
            Options.Create(new StoreRetailPriceHqSyncOptions
            {
                HqReadBatchSize = 100,
                WriteBatchSize = 100,
            })
        );
    }

    private static IMapper CreateMapper()
    {
        return new MapperConfiguration(
            cfg => cfg.AddProfile<ReactStoreRetailPriceMappingProfile>(),
            NullLoggerFactory.Instance
        ).CreateMapper();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(ISqlSugarClient db)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HqSqlSugarContext)
        );
        typeof(HqSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };
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
}
