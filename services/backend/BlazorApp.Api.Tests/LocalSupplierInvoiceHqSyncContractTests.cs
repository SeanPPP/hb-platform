using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierInvoiceHqSyncContractTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;
    private readonly IMapper _mapper;

    public LocalSupplierInvoiceHqSyncContractTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = CreateSqlSugarClient(_localConnection.ConnectionString);
        _hqDb = CreateSqlSugarClient(_hqConnection.ConnectionString);

        _localDb.CodeFirst.InitTables(
            typeof(Store),
            typeof(StoreLocalSupplierInvoice),
            typeof(StoreLocalSupplierInvoiceDetails)
        );
        _hqDb.CodeFirst.InitTables(
            typeof(RED_进货单主表Store),
            typeof(RED_进货单详情表Store)
        );

        _mapper = new MapperConfiguration(
            cfg =>
            {
                cfg.AddProfile<ReactStoreLocalSupplierInvoiceProfile>();
                cfg.AddProfile<ReactStoreLocalSupplierInvoiceDetailProfile>();
                cfg.AddProfile<ReactHBwebToHqInvoiceProfile>();
                cfg.AddProfile<ReactHBwebToHqInvoiceDetailProfile>();
            },
            NullLoggerFactory.Instance
        ).CreateMapper();
    }

    [Fact]
    public void SyncFromHq_只允许Admin和管理员角色调用()
    {
        var method = typeof(ReactLocalSupplierInvoicesController).GetMethod(
            nameof(ReactLocalSupplierInvoicesController.SyncFromHq)
        );

        var attribute = Assert.Single(
            method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
        );
        Assert.Equal("Admin,管理员", ((AuthorizeAttribute)attribute).Roles);
    }

    [Fact]
    public async Task SyncFromHq_结束日期早于起始日期_返回BadRequest()
    {
        var controller = CreateController(Mock.Of<ILocalSupplierInvoiceHqSyncService>());

        var response = await controller.SyncFromHq(new LocalSupplierInvoiceHqSyncRequest
        {
            StartDate = new DateTime(2026, 5, 31),
            EndDate = new DateTime(2026, 5, 30),
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<LocalSupplierInvoiceHqSyncResult>>(
            badRequest.Value
        );
        Assert.False(payload.Success);
        Assert.Equal("INVALID_DATE_RANGE", payload.ErrorCode);
    }

    [Fact]
    public async Task SyncForPageAsync_未知分店_返回校验错误()
    {
        await SeedActiveStoreAsync("S01");
        var service = CreateSyncService();

        var result = await service.SyncForPageAsync(
            new List<string> { "S02" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_STORE_SCOPE", result.ErrorCode);
    }

    [Fact]
    public async Task SyncForPageAsync_新增主表和明细_写入本地并返回独立计数()
    {
        await SeedActiveStoreAsync("S01");
        await SeedHqInvoiceAsync("INV-GUID-1", "S01", "SUP01", "INV-001", 88m);
        await SeedHqDetailAsync("DET-GUID-1", "INV-GUID-1", "S01", "SUP01", "ITEM-1", 3m);

        var service = CreateSyncService();

        var result = await service.SyncForPageAsync(
            new List<string> { "S01" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.InvoiceAddedCount);
        Assert.Equal(0, result.Data.InvoiceUpdatedCount);
        Assert.Equal(1, result.Data.DetailAddedCount);
        Assert.Equal(0, result.Data.DetailUpdatedCount);

        var invoice = await _localDb.Queryable<StoreLocalSupplierInvoice>()
            .SingleAsync(x => x.InvoiceGUID == "INV-GUID-1");
        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.DetailGUID == "DET-GUID-1");
        Assert.Equal("INV-001", invoice.InvoiceNo);
        Assert.Equal("ITEM-1", detail.ItemNumber);
    }

    [Fact]
    public async Task SyncForPageAsync_更新已有明细_保留本地检测和操作状态()
    {
        await SeedActiveStoreAsync("S01");
        await SeedHqInvoiceAsync("INV-GUID-1", "S01", "SUP01", "INV-HQ", 99m);
        await SeedHqDetailAsync("DET-GUID-1", "INV-GUID-1", "S01", "SUP01", "ITEM-HQ", 7m);
        await _localDb.Insertable(new StoreLocalSupplierInvoice
        {
            InvoiceGUID = "INV-GUID-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            InvoiceNo = "INV-OLD",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "DET-GUID-1",
            InvoiceGUID = "INV-GUID-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-OLD",
            ExistingProductCount = 2,
            BarcodeStatus = 1,
            BarcodeMatchCount = 2,
            ActivityType = 4,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var service = CreateSyncService();

        var result = await service.SyncForPageAsync(
            new List<string> { "S01" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.InvoiceUpdatedCount);
        Assert.Equal(1, result.Data.DetailUpdatedCount);

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .SingleAsync(x => x.DetailGUID == "DET-GUID-1");
        Assert.Equal("ITEM-HQ", detail.ItemNumber);
        Assert.Equal(2, detail.ExistingProductCount);
        Assert.Equal(1, detail.BarcodeStatus);
        Assert.Equal(2, detail.BarcodeMatchCount);
        Assert.Equal(4, detail.ActivityType);
    }

    [Fact]
    public async Task SyncForPageAsync_只有明细在日期范围内_回补缺失父单()
    {
        await SeedActiveStoreAsync("S01");
        await SeedHqInvoiceAsync(
            "INV-GUID-OLD",
            "S01",
            "SUP01",
            "INV-OLD-PARENT",
            66m,
            new DateTime(2026, 4, 1)
        );
        await SeedHqDetailAsync("DET-GUID-NEW", "INV-GUID-OLD", "S01", "SUP01", "ITEM-NEW", 5m);

        var service = CreateSyncService();

        var result = await service.SyncForPageAsync(
            new List<string> { "S01" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.InvoiceAddedCount);
        Assert.Equal(1, result.Data.DetailAddedCount);

        var parentExists = await _localDb.Queryable<StoreLocalSupplierInvoice>()
            .AnyAsync(x => x.InvoiceGUID == "INV-GUID-OLD");
        var detailExists = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .AnyAsync(x => x.DetailGUID == "DET-GUID-NEW");
        Assert.True(parentExists);
        Assert.True(detailExists);
    }

    [Fact]
    public async Task SyncForPageAsync_主表超过Contains和父Guid分块_明细不重复计数()
    {
        await SeedActiveStoreAsync("S01");
        for (var i = 0; i < 1205; i++)
        {
            var invoiceGuid = $"INV-GUID-{i:D3}";
            await SeedHqInvoiceAsync(invoiceGuid, "S01", "SUP01", $"INV-{i:D3}", i);
            await SeedHqDetailAsync($"DET-GUID-{i:D3}", invoiceGuid, "S01", "SUP01", $"ITEM-{i:D3}", 1m);
        }

        var service = CreateSyncService();

        var result = await service.SyncForPageAsync(
            new List<string> { "S01" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1205, result.Data!.InvoiceAddedCount);
        Assert.Equal(1205, result.Data.DetailAddedCount);
        Assert.Equal(0, result.Data.DetailUpdatedCount);
    }

    [Fact]
    public async Task SyncForPageAsync_失败响应_保留Data统计Payload()
    {
        await SeedActiveStoreAsync("S01");
        var service = CreateSyncService();

        var result = await service.SyncForPageAsync(
            new List<string> { "UNKNOWN" },
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 31)
        );

        Assert.False(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Failed", result.Data!.Status);
        Assert.Contains("请选择有效的启用分店", result.Data.Errors);
    }

    [Fact]
    public async Task PushInvoicesToHqAsync_明细失败时回滚该单并返回失败计数()
    {
        await _localDb.Insertable(new StoreLocalSupplierInvoice
        {
            InvoiceGUID = "PUSH-ROLLBACK",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            InvoiceNo = "PUSH-ROLLBACK",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "PUSH-DETAIL-ROLLBACK",
            InvoiceGUID = "PUSH-ROLLBACK",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-ROLLBACK",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _hqDb.Ado.ExecuteCommandAsync(
            "CREATE TRIGGER reject_push_detail BEFORE INSERT ON \"RED_进货单详情表\" "
                + "BEGIN SELECT RAISE(ABORT, 'forced hq detail failure'); END;"
        );

        var result = await CreatePushService().PushInvoicesToHqAsync(
            new List<string> { "PUSH-ROLLBACK" }
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Contains("推送失败", result.Message);
        Assert.False(await _hqDb.Queryable<RED_进货单主表Store>()
            .AnyAsync(x => x.HGUID == "PUSH-ROLLBACK"));
    }

    [Fact]
    public void PushInvoicesToHqAsync_回滚失败不得覆盖原异常且连接失败停止后续单据()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/HqSync/LocalSupplierInvoicesHqSyncHandler.cs"
            )
        );

        Assert.Contains("var originalException = ex;", source, StringComparison.Ordinal);
        Assert.Contains("推送进货单到HQ回滚失败", source, StringComparison.Ordinal);
        Assert.Contains("IsConnectionLevelFailure", source, StringComparison.Ordinal);
        Assert.Contains("invoices.Count - invoiceIndex", source, StringComparison.Ordinal);
        Assert.Contains("await hqDb.Ado.CommitTranAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PushInvoicesToHqAsync_本地单头和明细必须在同一可串行化快照读取()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/HqSync/LocalSupplierInvoicesHqSyncHandler.cs"
            )
        );

        Assert.Contains("ReadSourceSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("BeginTranAsync(IsolationLevel.Serializable)", source, StringComparison.Ordinal);
        Assert.Contains("await localDb.Ado.CommitTranAsync();", source, StringComparison.Ordinal);
        Assert.Contains("await localDb.Ado.RollbackTranAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PushInvoicesToHqAsync_并发幂等必须在逐单事务内取得规范化业务锁并重读HQ()
    {
        var handlerSource = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/HqSync/LocalSupplierInvoicesHqSyncHandler.cs"
            )
        );
        var lockSource = File.ReadAllText(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/HqSync/LocalSupplierInvoiceHqSyncMutationLock.cs"
            )
        );

        Assert.Contains("LocalSupplierInvoiceHqSyncMutationLock.AcquireAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("await hqDb.Ado.BeginTranAsync();", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ReadHqInvoiceAsync", handlerSource, StringComparison.Ordinal);
        Assert.Contains("ReadHqDetailAsync", handlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("existingHqInvoiceSet", handlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("existingHqDetails", handlerSource, StringComparison.Ordinal);
        Assert.Contains("NormalizeInvoiceKey", lockSource, StringComparison.Ordinal);
        Assert.Contains("@LockOwner = N'Transaction'", lockSource, StringComparison.Ordinal);
        Assert.Contains("DbType.SqlServer", lockSource, StringComparison.Ordinal);
        Assert.Contains("TryAddReference", lockSource, StringComparison.Ordinal);
        Assert.Contains("ReleaseReference", lockSource, StringComparison.Ordinal);
        Assert.Contains("ICollection<KeyValuePair", lockSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ConcurrentDictionary<string, SemaphoreSlim>",
            lockSource,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PushInvoicesToHqAsync_并发重复推送同一单据_两次成功且HQ仅保留一组数据()
    {
        await _localDb.Insertable(new StoreLocalSupplierInvoice
        {
            InvoiceGUID = " push-concurrent ",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            InvoiceNo = "PUSH-CONCURRENT",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "PUSH-CONCURRENT-DETAIL",
            InvoiceGUID = " push-concurrent ",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-CONCURRENT",
            IsDeleted = false,
        }).ExecuteCommandAsync();

        using var localDbOne = CreateSqlSugarClient(_localConnection.ConnectionString);
        using var hqDbOne = CreateSqlSugarClient(_hqConnection.ConnectionString);
        using var localDbTwo = CreateSqlSugarClient(_localConnection.ConnectionString);
        using var hqDbTwo = CreateSqlSugarClient(_hqConnection.ConnectionString);
        var serviceOne = CreatePushService(localDbOne, hqDbOne);
        var serviceTwo = CreatePushService(localDbTwo, hqDbTwo);

        var results = await Task.WhenAll(
            Task.Run(() => serviceOne.PushInvoicesToHqAsync(new List<string> { " push-concurrent " })),
            Task.Run(() => serviceTwo.PushInvoicesToHqAsync(new List<string> { " push-concurrent " }))
        );

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Message));
        Assert.Equal(
            1,
            await _hqDb.Queryable<RED_进货单主表Store>()
                .CountAsync(x => x.HGUID == " push-concurrent ")
        );
        Assert.Equal(
            1,
            await _hqDb.Queryable<RED_进货单详情表Store>()
                .CountAsync(x => x.HGUID == "PUSH-CONCURRENT-DETAIL")
        );
        Assert.Equal(2, results.Sum(result => result.AddedCount));
        Assert.Equal(2, results.Sum(result => result.UpdatedCount));
    }

    private ReactLocalSupplierInvoicesController CreateController(
        ILocalSupplierInvoiceHqSyncService syncService
    )
    {
        return new ReactLocalSupplierInvoicesController(
            Mock.Of<ILocalSupplierInvoicesReactService>(),
            CreateSqlSugarContext(_localDb),
            syncService,
            Mock.Of<ILocalSupplierInvoiceImportService>()
        );
    }

    private LocalSupplierInvoiceHqSyncService CreateSyncService()
    {
        return new LocalSupplierInvoiceHqSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb),
            _mapper,
            NullLogger<LocalSupplierInvoiceHqSyncService>.Instance
        );
    }

    private LocalSupplierInvoicesReactService CreatePushService()
    {
        return CreatePushService(_localDb, _hqDb);
    }

    private LocalSupplierInvoicesReactService CreatePushService(
        ISqlSugarClient localDb,
        ISqlSugarClient hqDb
    )
    {
        var autoPricing = new Mock<IAutoPricingService>();
        autoPricing.Setup(x => x.GetAllActiveStrategiesAsync())
            .ReturnsAsync(new List<BlazorApp.Shared.Models.HBweb.PricingStrategy>());
        return new LocalSupplierInvoicesReactService(
            CreateSqlSugarContext(localDb),
            CreateHqSqlSugarContext(hqDb),
            _mapper,
            NullLogger<LocalSupplierInvoicesReactService>.Instance,
            autoPricing.Object,
            WarehouseProductChangeHistoryTestDouble.CreateNoop()
        );
    }

    private async Task SeedActiveStoreAsync(string storeCode)
    {
        await _localDb.Insertable(new Store
        {
            StoreGUID = $"store-{storeCode}",
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHqInvoiceAsync(
        string guid,
        string storeCode,
        string supplierCode,
        string invoiceNo,
        decimal amount,
        DateTime? lastModifyDate = null
    )
    {
        await _hqDb.Insertable(new RED_进货单主表Store
        {
            HGUID = guid,
            H分店代码 = storeCode,
            H供应商编码 = supplierCode,
            H随货同行单号 = invoiceNo,
            H订单日期 = new DateTime(2026, 5, 10),
            H总金额 = amount,
            FGC_CreateDate = new DateTime(2026, 5, 10),
            FGC_LastModifyDate = lastModifyDate ?? new DateTime(2026, 5, 20),
        }).ExecuteCommandAsync();
    }

    private async Task SeedHqDetailAsync(
        string guid,
        string invoiceGuid,
        string storeCode,
        string supplierCode,
        string itemNumber,
        decimal purchasePrice
    )
    {
        await _hqDb.Insertable(new RED_进货单详情表Store
        {
            HGUID = guid,
            H主表GUID = invoiceGuid,
            H分店代码 = storeCode,
            H供应商编码 = supplierCode,
            H货号 = itemNumber,
            H主条形码 = $"BAR-{itemNumber}",
            H商品名称 = $"Product {itemNumber}",
            H数量 = 2,
            H进货价 = purchasePrice,
            H合计金额 = purchasePrice * 2,
            FGC_CreateDate = new DateTime(2026, 5, 10),
            FGC_LastModifyDate = new DateTime(2026, 5, 20),
        }).ExecuteCommandAsync();
    }

    private static SqlSugarClient CreateSqlSugarClient(string connectionString)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
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

    private static string FindRepoRoot([CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory != null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }

    public void Dispose()
    {
        _localConnection.Dispose();
        _hqConnection.Dispose();
        _localDb.Dispose();
        _hqDb.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
    }
}
