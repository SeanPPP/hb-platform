using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("ProductHqSyncServiceTests")]
public sealed class ProductHqSyncSplitTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;
    private readonly IMapper _mapper;

    public ProductHqSyncSplitTests()
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

        // 商品 HQ 解耦同步只需要最小表集合，关联表用来验证全量同步不会误触碰。
        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(ProductSetCode),
            typeof(Store),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct)
        );
        _hqDb.CodeFirst.InitTables(
            typeof(DIC_商品信息字典表),
            typeof(DIC_商品零售价表),
            typeof(DIC_一品多码表),
            typeof(DIC_分店一品多码表)
        );
    }

    [Fact]
    public async Task SyncFullAsync_只处理Product主表_不触碰价格和多码关联表()
    {
        var now = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-HQ-001", now, "HQ商品");
        await _localDb.Insertable(
            new StoreRetailPrice
            {
                UUID = "retail-keep",
                StoreCode = "S01",
                ProductCode = "P-LOCAL-ONLY",
                SupplierCode = "SUP",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new StoreMultiCodeProduct
            {
                UUID = "multi-keep",
                StoreCode = "S01",
                ProductCode = "P-LOCAL-ONLY",
                MultiCodeProductCode = "M01",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = "set-keep",
                ProductCode = "P-LOCAL-ONLY",
                SetProductCode = "M01",
                SetItemNumber = "M01",
                SetType = 2,
                SetQuantity = 1,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncFullAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ProductsAdded);
        Assert.True(result.Data.ProductsSwapped);
        Assert.NotNull(await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-HQ-001"));
        Assert.False((await _localDb.Queryable<StoreRetailPrice>().SingleAsync(x => x.UUID == "retail-keep")).IsDeleted);
        Assert.False((await _localDb.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "multi-keep")).IsDeleted);
        Assert.False((await _localDb.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-keep")).IsDeleted);
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_按ProductCode命中时_只同步选中商品和关联表()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalStoreAsync("S01");
        await SeedHqProductAsync("P-HQ-001", now, "HQ商品");
        await SeedHqProductSetCodeAsync("set-hq-001", "P-HQ-001", "P-HQ-001-M1", now);
        await SeedHqRetailPriceAsync("retail-hq-001", "S01", "P-HQ-001", "SUP", 3.4m, 5.6m, now);
        await SeedHqStoreMultiCodeAsync("multi-hq-001", "S01", "P-HQ-001", "P-HQ-001-M1", now);
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-selected",
                ProductCode = "P-HQ-001",
                ProductName = "旧商品",
                LocalSupplierCode = "SUP",
                ItemNumber = "OLD-ITEM",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = now.AddDays(-10),
                UpdatedAt = now.AddDays(-10),
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-unselected",
                ProductCode = "P-LOCAL-ONLY",
                ProductName = "不应被软删",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(new List<string> { "P-HQ-001" });

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ProductsUpdated);
        Assert.Equal(1, result.Data.ProductSetCodesAdded);
        Assert.Equal(1, result.Data.StoreRetailPricesCreated);
        Assert.Equal(1, result.Data.StoreMultiCodesCreated);
        var selected = await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-HQ-001");
        Assert.Equal("HQ商品", selected.ProductName);
        Assert.False(selected.IsDeleted);
        Assert.False((await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-LOCAL-ONLY")).IsDeleted);
        Assert.NotNull(await _localDb.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-HQ-001" && x.StoreCode == "S01"));
        Assert.NotNull(await _localDb.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.ProductCode == "P-HQ-001" && x.StoreCode == "S01"));
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_ProductCode未命中时_用供应商货号从分店零售价表兜底反查()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalStoreAsync("S01");
        await SeedHqProductAsync("P-HQ-FALLBACK", now, "兜底商品");
        await SeedHqRetailPriceAsync("retail-fallback", "S01", "P-HQ-FALLBACK", "SUP-FB", 1.2m, 2.3m, now);
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-fallback",
                ProductCode = "LOCAL-OLD-CODE",
                ProductName = "旧兜底商品",
                LocalSupplierCode = "SUP-FB",
                ItemNumber = "ITEM-P-HQ-FALLBACK",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(new List<string> { "LOCAL-OLD-CODE" });

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ProductsAdded);
        Assert.Empty(result.Data.Errors);
        Assert.NotNull(await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-HQ-FALLBACK"));
        Assert.NotNull(await _localDb.Queryable<StoreRetailPrice>().SingleAsync(x => x.ProductCode == "P-HQ-FALLBACK" && x.StoreCode == "S01"));
        Assert.False((await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "LOCAL-OLD-CODE")).IsDeleted);
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_本地没有选中商品时_不会只凭HQ编码新增()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-HQ-ONLY", now, "仅HQ商品");

        var result = await CreateService().SyncSelectedFromHqAsync(new List<string> { "P-HQ-ONLY" });

        Assert.False(result.Success);
        var details = Assert.IsType<HqProductSyncResult>(result.Details);
        Assert.Contains(details.Errors, item => item.Contains("本地商品不存在或已删除: P-HQ-ONLY"));
        Assert.Null(await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-HQ-ONLY"));
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_混合有效本地和仅HQ编码时_不会新增未选中本地商品()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-LOCAL-SELECTED", now, "选中商品");
        await SeedHqProductAsync("P-HQ-ONLY", now, "仅HQ商品");
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-selected-mixed",
                ProductCode = "P-LOCAL-SELECTED",
                ProductName = "旧选中商品",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(
            new List<string> { "P-LOCAL-SELECTED", "P-HQ-ONLY" }
        );

        Assert.True(result.Success, result.Message);
        Assert.Contains(result.Data!.Errors, item => item.Contains("本地商品不存在或已删除: P-HQ-ONLY"));
        Assert.NotNull(await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-LOCAL-SELECTED"));
        Assert.Null(await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-HQ-ONLY"));
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

    private ProductHqSyncService CreateService()
    {
        return new ProductHqSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            _mapper,
            NullLogger<ProductHqSyncService>.Instance
        );
    }

    private async Task SeedHqProductAsync(string productCode, DateTime lastModifyDate, string name)
    {
        await _hqDb.Insertable(
            new DIC_商品信息字典表
            {
                ID = Math.Abs(productCode.GetHashCode(StringComparison.Ordinal)) % 100000 + 1,
                HGUID = $"hq-{productCode}",
                H商品标签GUID = $"tag-{productCode}",
                H商品分类码GUID = "CAT",
                H供货商编码 = "SUP",
                H商品编码 = productCode,
                H货号 = $"ITEM-{productCode}",
                H主条形码 = $"BAR-{productCode}",
                H商品名称 = name,
                H商品类型 = 1,
                H大写名称 = name.ToUpperInvariant(),
                H规格 = "默认规格",
                H单位 = "EA",
                H进货价 = 1.2m,
                H零售价 = 2.3m,
                H是否自动定价 = false,
                H商品图片 = "image.png",
                中包数量 = 1,
                H腾讯云图地址 = "https://example.invalid/image.png",
                H使用状态 = true,
                H是否特殊商品 = false,
                H进货单主表GUID = $"order-{productCode}",
                H进货单详情GUID = $"order-detail-{productCode}",
                CBP商品中文名称 = name,
                CBP供应商编码 = "SUP",
                CBP商品分类码GUID = "WAREHOUSE",
                FGC_Creator = "HQ",
                FGC_CreateDate = lastModifyDate.AddDays(-1),
                FGC_LastModifier = "HQ",
                FGC_LastModifyDate = lastModifyDate,
                FGC_UpdateHelp = "test",
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalStoreAsync(string storeCode)
    {
        await _localDb.Insertable(
            new Store
            {
                StoreGUID = $"store-{storeCode}",
                StoreCode = storeCode,
                StoreName = storeCode,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqRetailPriceAsync(
        string hguid,
        string storeCode,
        string productCode,
        string supplierCode,
        decimal purchasePrice,
        decimal retailPrice,
        DateTime lastModifyDate
    )
    {
        await _hqDb.Insertable(
            new DIC_商品零售价表
            {
                HGUID = hguid,
                H分店代码 = storeCode,
                H商品编码 = productCode,
                H分店商品编码 = storeCode + productCode,
                H供应商编码 = supplierCode,
                H分店供应商编码 = storeCode + supplierCode,
                H进货价 = purchasePrice,
                H分店零售价 = retailPrice,
                H使用状态 = true,
                H是否自动定价 = true,
                H是否特殊商品 = false,
                FGC_Creator = "HQ",
                FGC_CreateDate = lastModifyDate.AddDays(-1),
                FGC_LastModifier = "HQ",
                FGC_LastModifyDate = lastModifyDate,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqProductSetCodeAsync(
        string hguid,
        string productCode,
        string setProductCode,
        DateTime lastModifyDate
    )
    {
        await _hqDb.Insertable(
            new DIC_一品多码表
            {
                HGUID = hguid,
                H商品编码 = productCode,
                H多码商品编号 = setProductCode,
                H供应商编码 = "SUP",
                H主条形码 = $"BAR-{productCode}",
                H多条形码 = $"BAR-{setProductCode}",
                H进货价 = 2.1m,
                H一品多码零售价 = 4.2m,
                H使用状态 = true,
                H是否自动定价 = false,
                FGC_Creator = "HQ",
                FGC_CreateDate = lastModifyDate.AddDays(-1),
                FGC_LastModifier = "HQ",
                FGC_LastModifyDate = lastModifyDate,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqStoreMultiCodeAsync(
        string hguid,
        string storeCode,
        string productCode,
        string multiCode,
        DateTime lastModifyDate
    )
    {
        await _hqDb.Insertable(
            new DIC_分店一品多码表
            {
                HGUID = hguid,
                H分店代码 = storeCode,
                H商品编码 = productCode,
                H分店商品编码 = storeCode + productCode,
                H多码商品编码 = multiCode,
                H分店多码商品编码 = storeCode + multiCode,
                H供应商编码 = "SUP",
                H主条形码 = $"BAR-{productCode}",
                H多条形码 = $"BAR-{multiCode}",
                H进货价 = 2.2m,
                H折扣率 = 0.9m,
                H一品多码零售价 = 4.4m,
                H是否自动定价 = false,
                H是否特殊商品 = true,
                H使用状态 = true,
                FGC_Creator = "HQ",
                FGC_CreateDate = lastModifyDate.AddDays(-1),
                FGC_LastModifier = "HQ",
                FGC_LastModifyDate = lastModifyDate,
            }
        ).ExecuteCommandAsync();
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
                cfg.AddProfile<ReactProductMappingProfile>();
                cfg.AddProfile<ReactProductSetCodeMappingProfile>();
            },
            NullLoggerFactory.Instance
        );
        return configuration.CreateMapper();
    }

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
}
