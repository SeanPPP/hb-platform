using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StorePriceTransferJobServiceTests
{
    [Fact]
    public async Task TransferAsync_HqToLocal_同步价格和多码并按目标分店新增缺失行()
    {
        await using var localConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await localConnection.OpenAsync();
        await using var hqConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await hqConnection.OpenAsync();
        using var localDb = new SqlSugarClient(CreateConnectionConfig(localConnection.ConnectionString));
        using var hqDb = new SqlSugarClient(CreateConnectionConfig(hqConnection.ConnectionString));
        InitLocalTables(localDb);
        InitHqTables(hqDb);
        await localDb.Insertable(new StoreRetailPrice
        {
            UUID = "local-retail-target",
            StoreCode = "T01",
            ProductCode = "P01",
            StoreProductCode = "T01P01",
            SupplierCode = "OLD",
            PurchasePrice = 1m,
            StoreRetailPriceValue = 2m,
            DiscountRate = 0.1m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await localDb.Insertable(BuildLocalMulti(
            "local-multi-target",
            "T01",
            "P01",
            "M01",
            1m,
            2m,
            0.1m,
            false,
            false
        )).ExecuteCommandAsync();
        await hqDb.Insertable(new[]
        {
            BuildHqRetail(1, "S01", "P01", 5m, 9m, 0.25m, true, true),
            BuildHqRetail(2, "S01", "P02", 6m, 10m, 0.35m, true, false),
        }).ExecuteCommandAsync();
        await hqDb.Insertable(new[]
        {
            BuildHqMulti(1, "S01", "P01", "M01", 3m, 7m, 0.15m, true, false),
            BuildHqMulti(2, "S01", "P02", "M02", 4m, 8m, 0.20m, false, true),
        }).ExecuteCommandAsync();
        var service = CreateTransferService(localDb, hqDb);

        var result = await service.TransferAsync(new StorePriceTransferRequest
        {
            Direction = StorePriceTransferDirectionConstants.HqToLocal,
            SourceStoreCode = "S01",
            TargetStoreCode = "T01",
            SyncRetailPrices = true,
            SyncMultiCodePrices = true,
            SyncPurchasePrice = true,
            SyncRetailPrice = true,
            SyncDiscountRate = true,
            SyncIsAutoPricing = true,
            SyncIsSpecialProduct = true,
        }, "tester");

        Assert.True(result.Success, result.Message);
        Assert.Equal(4, result.Data!.TotalProcessed);
        Assert.Equal(2, result.Data.InsertedCount);
        Assert.Equal(2, result.Data.UpdatedCount);
        Assert.Equal(1, result.Data.RetailPriceInserted);
        Assert.Equal(1, result.Data.RetailPriceUpdated);
        Assert.Equal(1, result.Data.MultiCodeInserted);
        Assert.Equal(1, result.Data.MultiCodeUpdated);

        var updatedRetail = await localDb.Queryable<StoreRetailPrice>()
            .SingleAsync(x => x.StoreCode == "T01" && x.ProductCode == "P01");
        Assert.Equal(5m, updatedRetail.PurchasePrice);
        Assert.Equal(9m, updatedRetail.StoreRetailPriceValue);
        Assert.Equal(0.25m, updatedRetail.DiscountRate);
        Assert.True(updatedRetail.IsAutoPricing);
        Assert.True(updatedRetail.IsSpecialProduct);

        var insertedRetail = await localDb.Queryable<StoreRetailPrice>()
            .SingleAsync(x => x.StoreCode == "T01" && x.ProductCode == "P02");
        Assert.Equal("T01P02", insertedRetail.StoreProductCode);
        Assert.Equal(6m, insertedRetail.PurchasePrice);
        Assert.Equal(10m, insertedRetail.StoreRetailPriceValue);

        var insertedMulti = await localDb.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(x => x.StoreCode == "T01" && x.ProductCode == "P02" && x.MultiCodeProductCode == "M02");
        Assert.Equal("T01M02", insertedMulti.StoreMultiCodeProductCode);
        Assert.Equal(4m, insertedMulti.PurchasePrice);
        Assert.Equal(8m, insertedMulti.MultiCodeRetailPrice);
        Assert.True(insertedMulti.IsSpecialProduct);
    }

    [Fact]
    public async Task TransferAsync_LocalToHq_同步价格和多码并只覆盖勾选字段()
    {
        await using var localConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await localConnection.OpenAsync();
        await using var hqConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await hqConnection.OpenAsync();
        using var localDb = new SqlSugarClient(CreateConnectionConfig(localConnection.ConnectionString));
        using var hqDb = new SqlSugarClient(CreateConnectionConfig(hqConnection.ConnectionString));
        InitLocalTables(localDb);
        InitHqTables(hqDb);
        await localDb.Insertable(new[]
        {
            BuildLocalRetail("local-retail-1", "S01", "P01", 11m, 21m, 0.44m, true, true),
            BuildLocalRetail("local-retail-2", "S01", "P02", 12m, 22m, 0.55m, false, false),
        }).ExecuteCommandAsync();
        await localDb.Insertable(new[]
        {
            BuildLocalMulti("local-multi-1", "S01", "P01", "M01", 31m, 41m, 0.66m, true, false),
            BuildLocalMulti("local-multi-2", "S01", "P02", "M02", 32m, 42m, 0.77m, false, true),
        }).ExecuteCommandAsync();
        await hqDb.Insertable(BuildHqRetail(1, "T01", "P01", 1m, 2m, 0.1m, false, false))
            .ExecuteCommandAsync();
        await hqDb.Insertable(BuildHqMulti(1, "T01", "P01", "M01", 3m, 4m, 0.2m, false, false))
            .ExecuteCommandAsync();
        var service = CreateTransferService(localDb, hqDb);

        var result = await service.TransferAsync(new StorePriceTransferRequest
        {
            Direction = StorePriceTransferDirectionConstants.LocalToHq,
            SourceStoreCode = "S01",
            TargetStoreCode = "T01",
            SyncRetailPrices = true,
            SyncMultiCodePrices = true,
            SyncPurchasePrice = true,
            SyncRetailPrice = false,
            SyncDiscountRate = false,
            SyncIsAutoPricing = true,
            SyncIsSpecialProduct = false,
        }, "tester");

        Assert.True(result.Success, result.Message);
        Assert.Equal(4, result.Data!.TotalProcessed);
        Assert.Equal(2, result.Data.InsertedCount);
        Assert.Equal(2, result.Data.UpdatedCount);

        var updatedRetail = await hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(x => x.H分店代码 == "T01" && x.H商品编码 == "P01");
        Assert.Equal(11m, updatedRetail.H进货价);
        Assert.Equal(2m, updatedRetail.H分店零售价);
        Assert.Equal(0.1m, updatedRetail.H折扣率);
        Assert.True(updatedRetail.H是否自动定价);
        Assert.False(updatedRetail.H是否特殊商品);

        var insertedRetail = await hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(x => x.H分店代码 == "T01" && x.H商品编码 == "P02");
        Assert.Equal("T01P02", insertedRetail.H分店商品编码);
        Assert.Equal(12m, insertedRetail.H进货价);
        Assert.Equal(0m, insertedRetail.H分店零售价);
        Assert.False(insertedRetail.H是否特殊商品);

        var updatedMulti = await hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(x => x.H分店代码 == "T01" && x.H商品编码 == "P01" && x.H多码商品编码 == "M01");
        Assert.Equal(31m, updatedMulti.H进货价);
        Assert.Equal(4m, updatedMulti.H一品多码零售价);
        Assert.True(updatedMulti.H是否自动定价);

        var insertedMulti = await hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(x => x.H分店代码 == "T01" && x.H商品编码 == "P02" && x.H多码商品编码 == "M02");
        Assert.Equal("T01M02", insertedMulti.H分店多码商品编码);
        Assert.Equal(32m, insertedMulti.H进货价);
        Assert.Equal(0m, insertedMulti.H一品多码零售价);
    }

    [Fact]
    public async Task TransferAsync_LocalToHq_Hq更新只写勾选字段和审计字段()
    {
        await using var localConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await localConnection.OpenAsync();
        await using var hqConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await hqConnection.OpenAsync();
        using var localDb = new SqlSugarClient(CreateConnectionConfig(localConnection.ConnectionString));
        using var hqDb = new SqlSugarClient(CreateConnectionConfig(hqConnection.ConnectionString));
        InitLocalTables(localDb);
        InitHqTables(hqDb);
        await localDb.Insertable(BuildLocalRetail("local-retail-1", "S01", "P01", 11m, 21m, 0.44m, true, true))
            .ExecuteCommandAsync();
        await localDb.Insertable(BuildLocalMulti("local-multi-1", "S01", "P01", "M01", 31m, 41m, 0.66m, true, false))
            .ExecuteCommandAsync();
        var hqRetail = BuildHqRetail(1, "T01", "P01", 1m, 2m, 0.1m, false, false);
        hqRetail.H库存 = 123m;
        hqRetail.H活动类型 = "PROMO";
        hqRetail.H动态销售额 = 456m;
        await hqDb.Insertable(hqRetail).ExecuteCommandAsync();
        var hqMulti = BuildHqMulti(1, "T01", "P01", "M01", 3m, 4m, 0.2m, false, false);
        hqMulti.H库存 = 789m;
        hqMulti.H活动类型 = "MULTI";
        hqMulti.H动态销售额 = 987m;
        await hqDb.Insertable(hqMulti).ExecuteCommandAsync();
        var executedSql = new List<string>();
        hqDb.Aop.OnLogExecuting = (sql, _) => executedSql.Add(sql);
        var service = CreateTransferService(localDb, hqDb);

        var result = await service.TransferAsync(new StorePriceTransferRequest
        {
            Direction = StorePriceTransferDirectionConstants.LocalToHq,
            SourceStoreCode = "S01",
            TargetStoreCode = "T01",
            SyncRetailPrices = true,
            SyncMultiCodePrices = true,
            SyncPurchasePrice = true,
            SyncRetailPrice = true,
        }, "tester");

        Assert.True(result.Success, result.Message);
        var updateSql = executedSql
            .Where(sql => sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Contains(updateSql, sql => sql.Contains("DIC_商品零售价表", StringComparison.Ordinal));
        Assert.Contains(updateSql, sql => sql.Contains("DIC_分店一品多码表", StringComparison.Ordinal));
        Assert.DoesNotContain(updateSql, sql => sql.Contains("H库存", StringComparison.Ordinal));
        Assert.DoesNotContain(updateSql, sql => sql.Contains("H活动类型", StringComparison.Ordinal));
        Assert.DoesNotContain(updateSql, sql => sql.Contains("H动态销售额", StringComparison.Ordinal));

        var updatedRetail = await hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(x => x.H分店代码 == "T01" && x.H商品编码 == "P01");
        Assert.Equal(123m, updatedRetail.H库存);
        Assert.Equal("PROMO", updatedRetail.H活动类型);
        Assert.Equal(456m, updatedRetail.H动态销售额);
        var updatedMulti = await hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(x => x.H分店代码 == "T01" && x.H商品编码 == "P01" && x.H多码商品编码 == "M01");
        Assert.Equal(789m, updatedMulti.H库存);
        Assert.Equal("MULTI", updatedMulti.H活动类型);
        Assert.Equal(987m, updatedMulti.H动态销售额);
    }

    [Fact]
    public void StorePriceTransferService_HqInsert路径应忽略SqlServer自增ID()
    {
        var source = File.ReadAllText(ResolveStorePriceTransferServicePath());

        Assert.Contains("IgnoreColumns(row => row.ID)", source);
    }

    [Theory]
    [InlineData("", "T01", true, true, true)]
    [InlineData("S01", "", true, true, true)]
    [InlineData("S01", "S01", true, true, true)]
    [InlineData("S01", "T01", false, false, true)]
    [InlineData("S01", "T01", true, true, false)]
    public async Task TransferAsync_非法请求返回校验错误(
        string sourceStoreCode,
        string targetStoreCode,
        bool syncRetailPrices,
        bool syncMultiCodePrices,
        bool syncAnyField
    )
    {
        await using var localConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await localConnection.OpenAsync();
        await using var hqConnection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await hqConnection.OpenAsync();
        using var localDb = new SqlSugarClient(CreateConnectionConfig(localConnection.ConnectionString));
        using var hqDb = new SqlSugarClient(CreateConnectionConfig(hqConnection.ConnectionString));
        InitLocalTables(localDb);
        InitHqTables(hqDb);
        var service = CreateTransferService(localDb, hqDb);

        var result = await service.TransferAsync(new StorePriceTransferRequest
        {
            Direction = StorePriceTransferDirectionConstants.HqToLocal,
            SourceStoreCode = sourceStoreCode,
            TargetStoreCode = targetStoreCode,
            SyncRetailPrices = syncRetailPrices,
            SyncMultiCodePrices = syncMultiCodePrices,
            SyncPurchasePrice = syncAnyField,
        }, "tester");

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task StartJobAsync_相同Operation运行中时复用同一个任务()
    {
        var release = new TaskCompletionSource<ApiResponse<StorePriceTransferResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var transferService = new Mock<IStorePriceTransferService>();
        transferService
            .Setup(service => service.TransferAsync(It.IsAny<StorePriceTransferRequest>(), It.IsAny<string>()))
            .Returns(release.Task);
        var service = CreateJobService(transferService);

        var first = await service.StartJobAsync(BuildRequest(), "admin");
        var duplicate = await service.StartJobAsync(BuildRequest(), "admin");

        Assert.Equal(first.JobId, duplicate.JobId);
        Assert.True(duplicate.IsDuplicateRequest);

        release.SetResult(ApiResponse<StorePriceTransferResult>.OK(new StorePriceTransferResult { TotalProcessed = 1 }));
        var completed = await WaitForJobAsync(service, first.JobId);
        Assert.Equal(StorePriceTransferJobStatusConstants.Succeeded, completed.Status);
        transferService.Verify(service => service.TransferAsync(It.IsAny<StorePriceTransferRequest>(), "admin"), Times.Once);
    }

    [Fact]
    public async Task ReactStoreProductPricesController_价格同步Job接口委托Job服务并要求管理员角色()
    {
        var startMethod = typeof(ReactStoreProductPricesController)
            .GetMethod(nameof(ReactStoreProductPricesController.StartStorePriceTransferJob))!;
        var startAuthorize = startMethod.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin,管理员", startAuthorize?.Roles);

        var getMethod = typeof(ReactStoreProductPricesController)
            .GetMethod(nameof(ReactStoreProductPricesController.GetStorePriceTransferJob))!;
        var getAuthorize = getMethod.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin,管理员", getAuthorize?.Roles);

        var jobService = new Mock<IStorePriceTransferJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.StartJobAsync(
                It.Is<StorePriceTransferRequest>(request => request.SourceStoreCode == "S01"),
                "admin",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new StorePriceTransferJobDto
            {
                JobId = "job-1",
                Status = StorePriceTransferJobStatusConstants.Running,
            });
        jobService
            .Setup(service => service.GetJobAsync("job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorePriceTransferJobDto
            {
                JobId = "job-1",
                Status = StorePriceTransferJobStatusConstants.Succeeded,
                Result = new StorePriceTransferResult { TotalProcessed = 1 },
            });
        var controller = CreateController(jobService.Object);
        controller.ControllerContext.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin") },
                    "test"
                )
            ),
        };

        var startResponse = await controller.StartStorePriceTransferJob(BuildRequest());
        var getResponse = await controller.GetStorePriceTransferJob("job-1");

        Assert.IsType<OkObjectResult>(startResponse);
        Assert.IsType<OkObjectResult>(getResponse);
        jobService.VerifyAll();
    }

    private static StorePriceTransferRequest BuildRequest() => new()
    {
        Direction = StorePriceTransferDirectionConstants.HqToLocal,
        SourceStoreCode = "S01",
        TargetStoreCode = "T01",
        SyncRetailPrices = true,
        SyncMultiCodePrices = true,
        SyncPurchasePrice = true,
        SyncRetailPrice = true,
    };

    private static StorePriceTransferService CreateTransferService(ISqlSugarClient localDb, ISqlSugarClient hqDb)
    {
        return new StorePriceTransferService(
            CreateSqlSugarContext(localDb),
            CreateHqSqlSugarContext(hqDb),
            Options.Create(new StoreRetailPriceHqSyncOptions
            {
                HqReadBatchSize = 1,
                WriteBatchSize = 1,
            }),
            NullLogger<StorePriceTransferService>.Instance
        );
    }

    private static StorePriceTransferJobService CreateJobService(Mock<IStorePriceTransferService> transferService)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => transferService.Object);
        var provider = services.BuildServiceProvider();
        return new StorePriceTransferJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StorePriceTransferJobService>.Instance
        );
    }

    private static ReactStoreProductPricesController CreateController(IStorePriceTransferJobService jobService)
    {
        return new ReactStoreProductPricesController(
            Mock.Of<IStoreProductPriceReactService>(),
            Mock.Of<IStoreRetailPriceReactService>(),
            Mock.Of<IUserService>(),
            Mock.Of<ILogger<ReactStoreProductPricesController>>(),
            jobService
        );
    }

    private static async Task<StorePriceTransferJobDto> WaitForJobAsync(
        StorePriceTransferJobService service,
        string jobId
    )
    {
        for (var i = 0; i < 50; i++)
        {
            var job = await service.GetJobAsync(jobId);
            if (job is { Status: not StorePriceTransferJobStatusConstants.Running })
            {
                return job;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("job did not complete");
    }

    private static void InitLocalTables(ISqlSugarClient db)
    {
        db.CodeFirst.InitTables(typeof(StoreRetailPrice), typeof(StoreMultiCodeProduct));
    }

    private static void InitHqTables(ISqlSugarClient db)
    {
        db.CodeFirst.InitTables(typeof(DIC_商品零售价表), typeof(DIC_分店一品多码表));
    }

    private static StoreRetailPrice BuildLocalRetail(
        string uuid,
        string storeCode,
        string productCode,
        decimal purchasePrice,
        decimal retailPrice,
        decimal discountRate,
        bool isAutoPricing,
        bool isSpecialProduct
    ) => new()
    {
        UUID = uuid,
        StoreCode = storeCode,
        ProductCode = productCode,
        StoreProductCode = storeCode + productCode,
        SupplierCode = "200",
        PurchasePrice = purchasePrice,
        StoreRetailPriceValue = retailPrice,
        DiscountRate = discountRate,
        IsAutoPricing = isAutoPricing,
        IsSpecialProduct = isSpecialProduct,
        IsActive = true,
        IsDeleted = false,
    };

    private static StoreMultiCodeProduct BuildLocalMulti(
        string uuid,
        string storeCode,
        string productCode,
        string multiCode,
        decimal purchasePrice,
        decimal retailPrice,
        decimal discountRate,
        bool isAutoPricing,
        bool isSpecialProduct
    ) => new()
    {
        UUID = uuid,
        StoreCode = storeCode,
        ProductCode = productCode,
        MultiCodeProductCode = multiCode,
        StoreMultiCodeProductCode = storeCode + multiCode,
        MultiBarcode = "B" + multiCode,
        PurchasePrice = purchasePrice,
        MultiCodeRetailPrice = retailPrice,
        DiscountRate = discountRate,
        IsAutoPricing = isAutoPricing,
        IsSpecialProduct = isSpecialProduct,
        IsActive = true,
        IsDeleted = false,
    };

    private static DIC_商品零售价表 BuildHqRetail(
        int id,
        string storeCode,
        string productCode,
        decimal purchasePrice,
        decimal retailPrice,
        decimal discountRate,
        bool isAutoPricing,
        bool isSpecialProduct
    ) => new()
    {
        ID = id,
        HGUID = $"hq-retail-{storeCode}-{productCode}",
        H分店代码 = storeCode,
        H商品编码 = productCode,
        H分店商品编码 = storeCode + productCode,
        H供应商编码 = "200",
        H分店供应商编码 = storeCode + "200",
        H进货价 = purchasePrice,
        H分店零售价 = retailPrice,
        H折扣率 = discountRate,
        H使用状态 = true,
        H是否自动定价 = isAutoPricing,
        H是否特殊商品 = isSpecialProduct,
        FGC_Creator = "test",
        FGC_CreateDate = new DateTime(2026, 1, 1),
        FGC_LastModifier = "test",
        FGC_LastModifyDate = new DateTime(2026, 1, 2),
    };

    private static DIC_分店一品多码表 BuildHqMulti(
        int id,
        string storeCode,
        string productCode,
        string multiCode,
        decimal purchasePrice,
        decimal retailPrice,
        decimal discountRate,
        bool isAutoPricing,
        bool isSpecialProduct
    ) => new()
    {
        ID = id,
        HGUID = $"hq-multi-{storeCode}-{productCode}-{multiCode}",
        H分店代码 = storeCode,
        H商品编码 = productCode,
        H分店商品编码 = storeCode + productCode,
        H多码商品编码 = multiCode,
        H分店多码商品编码 = storeCode + multiCode,
        H供应商编码 = "200",
        H主条形码 = "B" + productCode,
        H多条形码 = "B" + multiCode,
        H进货价 = purchasePrice,
        H一品多码零售价 = retailPrice,
        H折扣率 = discountRate,
        H使用状态 = true,
        H是否自动定价 = isAutoPricing,
        H是否特殊商品 = isSpecialProduct,
        FGC_Creator = "test",
        FGC_CreateDate = new DateTime(2026, 1, 1),
        FGC_LastModifier = "test",
        FGC_LastModifyDate = new DateTime(2026, 1, 2),
    };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(ISqlSugarClient db)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
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

    private static string ResolveStorePriceTransferServicePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException("无法解析测试文件目录");
        return Path.GetFullPath(
            Path.Combine(testDirectory, "..", "BlazorApp.Api", "Services", "React", "StorePriceTransferService.cs")
        );
    }
}
