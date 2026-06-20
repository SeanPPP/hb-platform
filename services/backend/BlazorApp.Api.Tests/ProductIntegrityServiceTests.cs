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

        _db.CodeFirst.InitTables(typeof(Product), typeof(ProductSetCode), typeof(StoreMultiCodeProduct));
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
            BuildSetCode("set-p1-b", "P1", "B"),
            BuildSetCode("set-p2-a", "P2", "A"),
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
        string? setProductCode
    ) => new()
    {
        SetCodeId = setCodeId,
        ProductCode = productCode!,
        SetProductCode = setProductCode!,
        SetItemNumber = $"{setCodeId}-item",
        SetBarcode = $"{setCodeId}-barcode",
        IsActive = true,
        IsDeleted = false,
    };

    private static StoreMultiCodeProduct BuildStoreMultiCode(
        string uuid,
        string storeCode,
        string productCode,
        string multiCodeProductCode
    ) => new()
    {
        UUID = uuid,
        StoreCode = storeCode,
        ProductCode = productCode,
        MultiCodeProductCode = multiCodeProductCode,
        StoreMultiCodeProductCode = $"{storeCode}-{multiCodeProductCode}",
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
