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
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DataSyncFullServiceWarehouseProductsTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;
    private readonly IMapper _mapper;

    public DataSyncFullServiceWarehouseProductsTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(typeof(WarehouseProduct));
        _hqDb.CodeFirst.InitTables(typeof(CBP_DIC_商品库存表));

        _mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<ReactWarehouseProductStockProfile>(),
            NullLoggerFactory.Instance
        ).CreateMapper();
    }

    [Fact]
    public async Task SyncWarehouseProductsFromHqAsync_按商品编码新增更新且保留本地字段()
    {
        var originalCreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await SeedLocalWarehouseProductAsync(
            "P-UPDATE",
            domesticPrice: 1m,
            oemPrice: 2m,
            importPrice: 3m,
            stockQuantity: 4,
            minOrderQuantity: 5,
            stockValue: 6m,
            stockAlertQuantity: 7,
            isActive: true,
            volume: 8.8m,
            packingQuantity: 9,
            createdAt: originalCreatedAt,
            createdBy: "LocalUser",
            isDeleted: true
        );
        await SeedLocalWarehouseProductAsync(
            "P-KEEP",
            domesticPrice: 11m,
            oemPrice: 12m,
            importPrice: 13m,
            stockQuantity: 14,
            minOrderQuantity: 15,
            stockValue: 16m,
            stockAlertQuantity: 17,
            isActive: false,
            volume: 18.8m,
            packingQuantity: 19,
            createdAt: originalCreatedAt.AddDays(-1),
            createdBy: "KeepUser",
            isDeleted: false
        );
        await SeedHqStockAsync(
            "P-UPDATE",
            domesticPrice: 101m,
            oemPrice: 102m,
            importPrice: 103m,
            stockQuantity: 104m,
            minOrderQuantity: 105m,
            stockValue: 106m,
            stockAlertQuantity: 107,
            isActive: 0
        );
        await SeedHqStockAsync(
            "P-INSERT",
            domesticPrice: 201m,
            oemPrice: 202m,
            importPrice: 203m,
            stockQuantity: 204m,
            minOrderQuantity: 205m,
            stockValue: 206m,
            stockAlertQuantity: 207,
            isActive: 1
        );

        var result = await CreateService().SyncWarehouseProductsFromHqAsync(1, 1);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, result.ErrorCount);

        var updated = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-UPDATE");
        Assert.Equal(101m, updated.DomesticPrice);
        Assert.Equal(102m, updated.OEMPrice);
        Assert.Equal(103m, updated.ImportPrice);
        Assert.Equal(104, updated.StockQuantity);
        Assert.Equal(105, updated.MinOrderQuantity);
        Assert.Equal(106m, updated.StockValue);
        Assert.Equal(107, updated.StockAlertQuantity);
        Assert.False(updated.IsActive);
        Assert.Equal(8.8m, updated.Volume);
        Assert.Equal(9, updated.PackingQuantity);
        Assert.Equal(originalCreatedAt, updated.CreatedAt);
        Assert.Equal("LocalUser", updated.CreatedBy);
        Assert.True(updated.IsDeleted);
        Assert.Equal("System", updated.UpdatedBy);

        var inserted = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-INSERT");
        Assert.Equal(201m, inserted.DomesticPrice);
        Assert.Equal(202m, inserted.OEMPrice);
        Assert.Equal(203m, inserted.ImportPrice);
        Assert.Equal(204, inserted.StockQuantity);
        Assert.Equal(205, inserted.MinOrderQuantity);
        Assert.Equal(206m, inserted.StockValue);
        Assert.Equal(207, inserted.StockAlertQuantity);
        Assert.True(inserted.IsActive);
        Assert.Equal("System", inserted.CreatedBy);
        Assert.Equal("System", inserted.UpdatedBy);

        var kept = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-KEEP");
        Assert.Equal(11m, kept.DomesticPrice);
        Assert.Equal(18.8m, kept.Volume);
        Assert.Equal(19, kept.PackingQuantity);
        Assert.Equal("KeepUser", kept.CreatedBy);
        Assert.False(kept.IsDeleted);
    }

    [Fact]
    public async Task SyncWarehouseProductsFromHqAsync_商品编码大小写不同仍更新本地记录()
    {
        await SeedLocalWarehouseProductAsync(
            "P-CASE",
            domesticPrice: 1m,
            oemPrice: 2m,
            importPrice: 3m,
            stockQuantity: 4,
            minOrderQuantity: 5,
            stockValue: 6m,
            stockAlertQuantity: 7,
            isActive: true,
            volume: 8m,
            packingQuantity: 9,
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            createdBy: "LocalUser",
            isDeleted: false
        );
        await SeedHqStockAsync(
            "p-case",
            domesticPrice: 301m,
            oemPrice: 302m,
            importPrice: 303m,
            stockQuantity: 304m,
            minOrderQuantity: 305m,
            stockValue: 306m,
            stockAlertQuantity: 307,
            isActive: 1
        );

        var result = await CreateService().SyncWarehouseProductsFromHqAsync(10, 10);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(1, await _localDb.Queryable<WarehouseProduct>().CountAsync());
        var updated = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-CASE");
        Assert.Equal(301m, updated.DomesticPrice);
        Assert.Equal("P-CASE", updated.ProductCode);
    }

    [Fact]
    public async Task SyncWarehouseProductsFromHqAsync_跨页重复商品编码只统计一次新增且最后一条生效()
    {
        await SeedHqStockAsync(
            "P-DUP",
            domesticPrice: 401m,
            oemPrice: 402m,
            importPrice: 403m,
            stockQuantity: 404m,
            minOrderQuantity: 405m,
            stockValue: 406m,
            stockAlertQuantity: 407,
            isActive: 1
        );
        await SeedHqStockAsync(
            "p-dup",
            domesticPrice: 501m,
            oemPrice: 502m,
            importPrice: 503m,
            stockQuantity: 504m,
            minOrderQuantity: 505m,
            stockValue: 506m,
            stockAlertQuantity: 507,
            isActive: 0
        );

        var result = await CreateService().SyncWarehouseProductsFromHqAsync(1, 1);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, await _localDb.Queryable<WarehouseProduct>().CountAsync());
        var inserted = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-DUP");
        Assert.Equal(501m, inserted.DomesticPrice);
        Assert.Equal(504, inserted.StockQuantity);
        Assert.False(inserted.IsActive);
    }

    [Fact]
    public async Task SyncWarehouseProductsFromHqAsync_异常失败时返回错误计数()
    {
        var result = await CreateService(CreateContext<HqSqlSugarContext>())
            .SyncWarehouseProductsFromHqAsync(10, 10);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ErrorCount);
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

    private DataSyncFullService CreateService(HqSqlSugarContext? hqContext = null)
    {
        var localContext = CreateSqlSugarContext(_localDb);
        var configuration = CreateHqConfiguration(_hqConnection.ConnectionString);

        return new DataSyncFullService(
            localContext,
            hqContext ?? CreateHqSqlSugarContext(_hqDb, configuration),
            CreateContext<HBSalesSqlSugarContext>(),
            CreateContext<POSMSqlSugarContext>(),
            configuration,
            _mapper,
            NullLogger<DataSyncFullService>.Instance,
            new ScheduledTaskLogService(
                localContext,
                NullLogger<ScheduledTaskLogService>.Instance
            ),
            Mock.Of<IStoreRetailPriceHqSyncService>()
        );
    }

    private async Task SeedLocalWarehouseProductAsync(
        string productCode,
        decimal domesticPrice,
        decimal oemPrice,
        decimal importPrice,
        int stockQuantity,
        int minOrderQuantity,
        decimal stockValue,
        int stockAlertQuantity,
        bool isActive,
        decimal volume,
        int packingQuantity,
        DateTime createdAt,
        string createdBy,
        bool isDeleted
    )
    {
        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            DomesticPrice = domesticPrice,
            OEMPrice = oemPrice,
            ImportPrice = importPrice,
            StockQuantity = stockQuantity,
            MinOrderQuantity = minOrderQuantity,
            StockValue = stockValue,
            StockAlertQuantity = stockAlertQuantity,
            IsActive = isActive,
            Volume = volume,
            PackingQuantity = packingQuantity,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = createdAt,
            UpdatedBy = createdBy,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHqStockAsync(
        string productCode,
        decimal domesticPrice,
        decimal oemPrice,
        decimal importPrice,
        decimal stockQuantity,
        decimal minOrderQuantity,
        decimal stockValue,
        int stockAlertQuantity,
        int isActive
    )
    {
        await _hqDb.Insertable(new CBP_DIC_商品库存表
        {
            HGUID = $"HQ-{productCode}",
            H商品编码 = productCode,
            H国内价格 = domesticPrice,
            H贴牌价格 = oemPrice,
            H进口价格 = importPrice,
            H库存 = stockQuantity,
            H最小订货量 = minOrderQuantity,
            H库存金额 = stockValue,
            H库存预警数 = stockAlertQuantity,
            H使用状态 = isActive,
        }).ExecuteCommandAsync();
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static IConfiguration CreateHqConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StoreHzgHQConnection"] = connectionString,
                }
            )
            .Build();

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(
        ISqlSugarClient db,
        IConfiguration configuration
    )
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        typeof(HqSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        typeof(HqSqlSugarContext)
            .GetField("<Configuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, configuration);
        return context;
    }

    private static TContext CreateContext<TContext>()
        where TContext : class
    {
        return (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
    }
}
