using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductIntegrityServiceTests : IDisposable
{
    [Fact]
    public void ShouldReturnBusyFixResponse_默认零变更报告不掩盖全部BUSY()
    {
        var reports = new[]
        {
            new TableFixReport { TableName = "StoreRetailPrice" },
            new TableFixReport
            {
                TableName = "StoreMultiCodeProduct",
                FailureDetails = new List<BatchOperationFailureDto>
                {
                    new()
                    {
                        ItemKey = "S001|P001",
                        Message = "请稍后重试",
                        ErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY",
                    },
                },
            },
            new TableFixReport { TableName = "ProductSetCode" },
        };

        Assert.True(ProductIntegrityService.ShouldReturnBusyFixResponse(reports));
    }

    [Fact]
    public void ShouldReturnBusyFixResponse_存在成功组时保持部分成功语义()
    {
        var reports = new[]
        {
            new TableFixReport
            {
                TableName = "StoreMultiCodeProduct",
                SuccessfulGroupCount = 1,
                FailureDetails = new List<BatchOperationFailureDto>
                {
                    new()
                    {
                        ItemKey = "S002|P002",
                        Message = "请稍后重试",
                        ErrorCode = "SET_CHILD_PURCHASE_PRICE_BUSY",
                    },
                },
            },
        };

        Assert.False(ProductIntegrityService.ShouldReturnBusyFixResponse(reports));
    }

    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public ProductIntegrityServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(StoreRetailPrice),
            typeof(ProductSetCode),
            typeof(StoreMultiCodeProduct)
        );
        RecreateProductSetCodeTableWithNullableKeys();
    }

    [Fact]
    public async Task FixProductSetCodeAsync_空白关键编码只报告不软删()
    {
        await _db.Insertable(new Product
        {
            ProductCode = "P-KEEP",
            ProductName = "Keep",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-keep", "P-KEEP", "P-KEEP-SET")).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-orphan", "P-MISSING", "P-MISSING-SET")).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-blank", "   ", "   ")).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-null", null, "P-NULL-SET")).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-empty", "P-EMPTY", "")).ExecuteCommandAsync();

        var service = CreateService();

        var dryRunReport = await InvokeFixProductSetCodeAsync(service, dryRun: true);
        Assert.Equal(1, dryRunReport.DeletedCount);
        Assert.Equal(1, dryRunReport.ErrorCount);
        Assert.Contains(dryRunReport.Errors, message => message.Contains("缺少 ProductCode 或 SetProductCode"));
        Assert.Contains(dryRunReport.Errors, message => message.Contains("3 条"));

        var fixReport = await InvokeFixProductSetCodeAsync(service, dryRun: false);
        Assert.Equal(1, fixReport.DeletedCount);
        Assert.Equal(1, fixReport.ErrorCount);
        Assert.Contains(fixReport.Errors, message => message.Contains("3 条"));

        Assert.True((await _db.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-orphan")).IsDeleted);
        Assert.False((await _db.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-blank")).IsDeleted);
        Assert.False((await _db.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-null")).IsDeleted);
        Assert.False((await _db.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-empty")).IsDeleted);
        Assert.False((await _db.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-keep")).IsDeleted);
    }

    [Fact]
    public async Task CheckProductSetCodeAsync_无效关键编码进入检查报告()
    {
        await _db.Insertable(new Product
        {
            ProductCode = "P-KEEP",
            ProductName = "Keep",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-keep", "P-KEEP", "P-KEEP-SET")).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-null", null, "P-NULL-SET")).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("set-empty", "P-EMPTY", "")).ExecuteCommandAsync();

        var service = CreateService();

        var report = await InvokeCheckProductSetCodeAsync(service);

        Assert.Equal(2, report.InvalidKeyCount);
        Assert.Contains(report.Errors, message => message.Contains("缺少 ProductCode 或 SetProductCode"));
        Assert.Equal(0, report.OrphanedCount);
    }

    [Fact]
    public async Task FixStoreMultiCodeProductAsync_按复合键软删不误删交叉组合()
    {
        await _db.Insertable(new[]
        {
            // 该用例只验证常规投影的复合键软删，不进入 Type1/Type2 完整组事务。
            BuildSetCode("set-p1-b", "P1", "B", setType: 3),
            BuildSetCode("set-p2-a", "P2", "A", setType: 3),
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreMultiCode("valid-p1-b", "S01", "P1", "B"),
            BuildStoreMultiCode("valid-p2-a", "S01", "P2", "A"),
            BuildStoreMultiCode("orphan-p1-a", "S01", "P1", "A"),
            BuildStoreMultiCode("orphan-p2-b", "S01", "P2", "B"),
        }).ExecuteCommandAsync();

        var service = CreateService();

        var report = await InvokeFixStoreMultiCodeProductAsync(
            service,
            new List<string> { "S01" },
            dryRun: false
        );

        Assert.Equal(2, report.DeletedCount);
        Assert.False((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "valid-p1-b")).IsDeleted);
        Assert.False((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "valid-p2-a")).IsDeleted);
        Assert.True((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "orphan-p1-a")).IsDeleted);
        Assert.True((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "orphan-p2-b")).IsDeleted);
    }

    [Fact]
    public async Task FixStoreMultiCodeProductAsync_Type1_按门店和主商品完整重算且DryRun不写入()
    {
        await _db.Insertable(new Product
        {
            ProductCode = "P-TYPE1",
            ProductName = "组合套装",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "type1-parent-store",
            StoreCode = "S01",
            ProductCode = "P-TYPE1",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("type1-a", "P-TYPE1", "CHILD-A", setType: 1, setRetailPrice: 20m),
            BuildSetCode("type1-b", "P-TYPE1", "CHILD-B", setType: 1, setRetailPrice: 30m),
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreMultiCode("type1-a-store", "S01", "P-TYPE1", "CHILD-A", purchasePrice: 99m, retailPrice: 20m),
            BuildStoreMultiCode("type1-orphan", "S01", "P-TYPE1", "ORPHAN", purchasePrice: 99m, retailPrice: 1m),
        }).ExecuteCommandAsync();

        var service = CreateService();

        var dryRunReport = await InvokeFixStoreMultiCodeProductAsync(
            service,
            new List<string> { "S01" },
            dryRun: true
        );

        Assert.Equal(1, dryRunReport.DeletedCount);
        Assert.Equal(1, dryRunReport.AddedCount);
        Assert.False((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "type1-orphan")).IsDeleted);
        Assert.Null((await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-TYPE1" && x.MultiCodeProductCode == "CHILD-B")
            .FirstAsync())?.PurchasePrice);

        var report = await InvokeFixStoreMultiCodeProductAsync(
            service,
            new List<string> { "S01" },
            dryRun: false
        );

        Assert.Equal(1, report.DeletedCount);
        Assert.Equal(1, report.AddedCount);
        var setRows = await _db.Queryable<ProductSetCode>()
            .Where(x => x.ProductCode == "P-TYPE1")
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-TYPE1" && !x.IsDeleted)
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        // 旧完整性修复这里只负责门店投影；门店专用重算不得污染全局成本及其统计。
        Assert.Equal(new decimal?[] { 99m, 99m }, setRows.Select(x => x.SetPurchasePrice));
        Assert.Equal(new decimal?[] { 4m, 6m }, storeRows.Select(x => x.PurchasePrice));
        Assert.True((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "type1-orphan")).IsDeleted);
    }

    [Fact]
    public async Task FixStoreMultiCodeProductAsync_Type2缺行先置空并由门店主成本统一回写()
    {
        await _db.Insertable(new Product
        {
            ProductCode = "P-TYPE2",
            ProductName = "固定套装",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "type2-parent-store",
            StoreCode = "S01",
            ProductCode = "P-TYPE2",
            PurchasePrice = 15m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("type2-a", "P-TYPE2", "CHILD-A", setType: 2, setRetailPrice: 20m),
            BuildSetCode("type2-b", "P-TYPE2", "CHILD-B", setType: 2, setRetailPrice: 30m),
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreMultiCode("type2-a-store", "S01", "P-TYPE2", "CHILD-A", purchasePrice: 99m, retailPrice: 20m),
            BuildStoreMultiCode("type2-orphan", "S01", "P-TYPE2", "ORPHAN", purchasePrice: 99m, retailPrice: 1m),
        }).ExecuteCommandAsync();

        var service = CreateService();
        var dryRunReport = await InvokeFixStoreMultiCodeProductAsync(
            service,
            new List<string> { "S01" },
            dryRun: true
        );

        Assert.Equal(1, dryRunReport.DeletedCount);
        Assert.Equal(1, dryRunReport.AddedCount);
        Assert.False((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "type2-orphan")).IsDeleted);
        Assert.Null((await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-TYPE2" && x.MultiCodeProductCode == "CHILD-B")
            .FirstAsync())?.PurchasePrice);

        var report = await InvokeFixStoreMultiCodeProductAsync(
            service,
            new List<string> { "S01" },
            dryRun: false
        );

        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-TYPE2" && !x.IsDeleted)
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(1, report.DeletedCount);
        Assert.Equal(1, report.AddedCount);
        Assert.Equal(new decimal?[] { 15m, 15m }, storeRows.Select(x => x.PurchasePrice));
        Assert.True((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "type2-orphan")).IsDeleted);
    }

    [Fact]
    public async Task FixStoreMultiCodeProductAsync_Type2缺门店主成本时整组回滚()
    {
        await _db.Insertable(new Product
        {
            ProductCode = "P-TYPE2-NO-STORE-PARENT",
            ProductName = "固定套装缺门店主成本",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = "type2-no-store-parent",
            ProductCode = "P-TYPE2-NO-STORE-PARENT",
            SetProductCode = "CHILD-A",
            SetItemNumber = "type2-no-store-parent-item",
            SetPurchasePrice = 99m,
            SetRetailPrice = 20m,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = "type2-no-store-parent-orphan",
            StoreCode = "S01",
            ProductCode = "P-TYPE2-NO-STORE-PARENT",
            MultiCodeProductCode = "ORPHAN",
            PurchasePrice = 99m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var report = await InvokeFixStoreMultiCodeProductAsync(
            CreateService(),
            new List<string> { "S01" },
            dryRun: false
        );

        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(0, report.AddedCount);
        Assert.NotEmpty(report.Errors);
        Assert.False((await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(x => x.UUID == "type2-no-store-parent-orphan")).IsDeleted);
        Assert.Empty(await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-TYPE2-NO-STORE-PARENT" && x.MultiCodeProductCode == "CHILD-A")
            .ToListAsync());
    }

    [Fact]
    public async Task FixStoreMultiCodeProductAsync_同键跨Type冲突时整组回滚()
    {
        await _db.Insertable(new Product
        {
            ProductCode = "P-TYPE-CONFLICT",
            ProductName = "冲突套装",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "type-conflict-parent-store",
            StoreCode = "S01",
            ProductCode = "P-TYPE-CONFLICT",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("type-conflict-1", "P-TYPE-CONFLICT", "CHILD-A", setType: 1, setRetailPrice: 20m),
            BuildSetCode("type-conflict-2", "P-TYPE-CONFLICT", "CHILD-A", setType: 2, setRetailPrice: 20m),
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = "type-conflict-orphan",
            StoreCode = "S01",
            ProductCode = "P-TYPE-CONFLICT",
            MultiCodeProductCode = "ORPHAN",
            PurchasePrice = 99m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var report = await InvokeFixStoreMultiCodeProductAsync(
            CreateService(),
            new List<string> { "S01" },
            dryRun: false
        );

        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(0, report.AddedCount);
        Assert.Contains(report.Errors, error => error.Contains("活跃Type1/Type2冲突"));
        Assert.False((await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(x => x.UUID == "type-conflict-orphan")).IsDeleted);
        Assert.Empty(await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-TYPE-CONFLICT" && x.MultiCodeProductCode == "CHILD-A")
            .ToListAsync());
    }

    [Fact]
    public async Task FixStoreMultiCodeProductAsync_Type1完整组无法重算时回滚该组()
    {
        await _db.Insertable(new Product
        {
            ProductCode = "P-TYPE1-ROLLBACK",
            ProductName = "组合套装回滚",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("rollback-a", "P-TYPE1-ROLLBACK", "CHILD-A", setType: 1, setRetailPrice: 20m),
            BuildSetCode("rollback-b", "P-TYPE1-ROLLBACK", "CHILD-B", setType: 1, setRetailPrice: 0m),
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreMultiCode("rollback-a-store", "S01", "P-TYPE1-ROLLBACK", "CHILD-A", purchasePrice: 99m, retailPrice: 20m),
            BuildStoreMultiCode("rollback-orphan", "S01", "P-TYPE1-ROLLBACK", "ORPHAN", purchasePrice: 99m, retailPrice: 1m),
        }).ExecuteCommandAsync();

        var report = await InvokeFixStoreMultiCodeProductAsync(
            CreateService(),
            new List<string> { "S01" },
            dryRun: false
        );

        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(0, report.AddedCount);
        Assert.NotEmpty(report.Errors);
        Assert.False((await _db.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "rollback-orphan")).IsDeleted);
        Assert.Empty(await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-TYPE1-ROLLBACK" && x.MultiCodeProductCode == "CHILD-B")
            .ToListAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private ProductIntegrityService CreateService()
    {
        return new ProductIntegrityService(
            CreateSqlSugarContext(_db),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _sqliteConnection.ConnectionString,
                })
                .Build(),
            NullLogger<ProductIntegrityService>.Instance
        );
    }

    private static ProductSetCode BuildSetCode(
        string setCodeId,
        string? productCode,
        string? setProductCode,
        int setType = 2,
        decimal? setRetailPrice = null
    ) => new()
    {
        SetCodeId = setCodeId,
        ProductCode = productCode!,
        SetProductCode = setProductCode!,
        SetItemNumber = $"{setCodeId}-item",
        SetBarcode = $"{setCodeId}-barcode",
        SetPurchasePrice = 99m,
        SetRetailPrice = setRetailPrice,
        SetType = setType,
        IsActive = true,
        IsDeleted = false,
    };

    private static StoreMultiCodeProduct BuildStoreMultiCode(
        string uuid,
        string storeCode,
        string productCode,
        string multiCodeProductCode,
        decimal? purchasePrice = null,
        decimal? retailPrice = null
    ) => new()
    {
        UUID = uuid,
        StoreCode = storeCode,
        ProductCode = productCode,
        MultiCodeProductCode = multiCodeProductCode,
        StoreMultiCodeProductCode = $"{storeCode}-{multiCodeProductCode}",
        PurchasePrice = purchasePrice,
        MultiCodeRetailPrice = retailPrice,
        IsActive = true,
        IsDeleted = false,
    };

    private void RecreateProductSetCodeTableWithNullableKeys()
    {
        // 生产模型仍要求关键编码非空；测试库放宽约束，用来覆盖历史脏数据的修复边界。
        _db.Ado.ExecuteCommand("DROP TABLE ProductSetCode");
        _db.Ado.ExecuteCommand(
            """
            CREATE TABLE ProductSetCode (
                SetCodeId TEXT PRIMARY KEY NOT NULL,
                ProductCode TEXT NULL,
                SetProductCode TEXT NULL,
                SetItemNumber TEXT NOT NULL,
                SetBarcode TEXT NULL,
                SetPurchasePrice NUMERIC NULL,
                SetRetailPrice NUMERIC NULL,
                SetQuantity INTEGER NOT NULL,
                SetType INTEGER NOT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedAt TEXT NULL,
                UpdatedBy TEXT NULL,
                IsDeleted INTEGER NULL
            )
            """
        );
    }

    private static async Task<TableFixReport> InvokeFixProductSetCodeAsync(
        ProductIntegrityService service,
        bool dryRun
    )
    {
        var method = typeof(ProductIntegrityService).GetMethod(
            "FixProductSetCodeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var task = (Task<TableFixReport>)method.Invoke(service, new object[] { dryRun })!;
        return await task;
    }

    private static async Task<TableIntegrityReport> InvokeCheckProductSetCodeAsync(
        ProductIntegrityService service
    )
    {
        var method = typeof(ProductIntegrityService).GetMethod(
            "CheckProductSetCodeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var task = (Task<TableIntegrityReport>)method.Invoke(service, Array.Empty<object>())!;
        return await task;
    }

    private static async Task<TableFixReport> InvokeFixStoreMultiCodeProductAsync(
        ProductIntegrityService service,
        List<string> activeStoreCodes,
        bool dryRun
    )
    {
        var method = typeof(ProductIntegrityService).GetMethod(
            "FixStoreMultiCodeProductAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var task = (Task<TableFixReport>)method.Invoke(
            service,
            new object[] { activeStoreCodes, dryRun }
        )!;
        return await task;
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
