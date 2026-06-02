using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("ProductHqSyncServiceTests")]
public sealed class ProductSetCodeHqIncrementalSyncTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;
    private readonly IMapper _mapper;

    public ProductSetCodeHqIncrementalSyncTests()
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

        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(ProductSetCode),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct)
        );
        _hqDb.CodeFirst.InitTables(typeof(DIC_商品信息字典表), typeof(DIC_一品多码表));
    }

    [Fact]
    public async Task SyncIncrementalAsync_先同步Product再同步全局ProductSetCode并软删HQ缺失行()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalProductAsync("P-DELETE", isDeleted: false);
        await SeedLocalSetCodeAsync("set-physical-delete", "P-DELETE", "M-DELETE", false);
        await SeedLocalStoreRetailPriceAsync("retail-physical-delete", "P-DELETE");
        await SeedLocalStoreMultiCodeAsync("multi-physical-delete", "P-DELETE", "M-DELETE");

        await SeedHqProductAsync("P-NEW", start.AddDays(1), true);
        await SeedHqProductAsync("P-KEEP", start.AddDays(1), true);
        await SeedLocalSetCodeAsync("set-fallback-local", "P-KEEP", "M-FALLBACK", true);

        await SeedHqSetCodeAsync("hq-set-new", "P-NEW", "M-NEW", true, start.AddDays(1), "BAR-NEW");
        await SeedHqSetCodeAsync(null, "P-KEEP", "M-FALLBACK", true, start.AddDays(1), "BAR-FALLBACK-HQ");

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Data!.ProductsAdded);
        Assert.Equal(1, result.Data.ProductsSoftDeleted);
        Assert.Equal(1, result.Data.StoreRetailPricesDeleted);
        Assert.Equal(1, result.Data.StoreMultiCodesDeleted);
        Assert.Equal(1, result.Data.ProductSetCodesAdded);
        Assert.Equal(1, result.Data.ProductSetCodesUpdated);
        Assert.Equal(1, result.Data.ProductSetCodesSoftDeleted);

        Assert.False((await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-NEW")).IsDeleted);
        Assert.True((await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-DELETE")).IsDeleted);
        Assert.True((await _localDb.Queryable<StoreRetailPrice>().SingleAsync(x => x.UUID == "retail-physical-delete")).IsDeleted);
        Assert.True((await _localDb.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "multi-physical-delete")).IsDeleted);
        Assert.True((await _localDb.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-physical-delete")).IsDeleted);
        Assert.Equal("BAR-FALLBACK-HQ", (await _localDb.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-fallback-local")).SetBarcode);
        Assert.Equal(2, await _localDb.Queryable<ProductSetCode>().Where(x => !x.IsDeleted).CountAsync());
    }

    [Fact]
    public async Task SyncIncrementalAsync_HQ停用ProductSetCode时_本地应该软删()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-SET", start.AddDays(1), true);
        await SeedLocalSetCodeAsync("hq-set-disabled", "P-SET", "M-DISABLED", false);
        await SeedHqSetCodeAsync("hq-set-disabled", "P-SET", "M-DISABLED", false, start.AddDays(1), "BAR-DISABLED");

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ProductSetCodesSoftDeleted);
        Assert.True((await _localDb.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "hq-set-disabled")).IsDeleted);
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

    private async Task SeedLocalProductAsync(string productCode, bool isDeleted)
    {
        await _localDb.Insertable(
            new Product
            {
                UUID = $"local-{productCode}",
                ProductCode = productCode,
                ProductName = productCode,
                IsActive = true,
                IsDeleted = isDeleted,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalSetCodeAsync(
        string setCodeId,
        string productCode,
        string setProductCode,
        bool preserveCreatedBy
    )
    {
        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = setCodeId,
                ProductCode = productCode,
                SetProductCode = setProductCode,
                SetItemNumber = setProductCode,
                SetBarcode = $"LOCAL-{setProductCode}",
                SetPurchasePrice = 1,
                SetRetailPrice = 2,
                SetType = 2,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
                CreatedBy = preserveCreatedBy ? "local-user" : null,
                CreatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalStoreRetailPriceAsync(string uuid, string productCode)
    {
        await _localDb.Insertable(
            new StoreRetailPrice
            {
                UUID = uuid,
                StoreCode = "S01",
                ProductCode = productCode,
                SupplierCode = "SUP",
                PurchasePrice = 1,
                StoreRetailPriceValue = 2,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalStoreMultiCodeAsync(
        string uuid,
        string productCode,
        string multiCodeProductCode
    )
    {
        await _localDb.Insertable(
            new StoreMultiCodeProduct
            {
                UUID = uuid,
                StoreCode = "S01",
                ProductCode = productCode,
                MultiCodeProductCode = multiCodeProductCode,
                StoreMultiCodeProductCode = $"S01-{multiCodeProductCode}",
                MultiBarcode = $"LOCAL-{multiCodeProductCode}",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqProductAsync(string productCode, DateTime lastModifyDate, bool isActive)
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
                H商品名称 = productCode,
                H商品类型 = 1,
                H大写名称 = productCode.ToUpperInvariant(),
                H规格 = "默认规格",
                H单位 = "EA",
                H进货价 = 1.2m,
                H零售价 = 2.3m,
                H是否自动定价 = false,
                H商品图片 = "image.png",
                中包数量 = 1,
                H腾讯云图地址 = "https://example.invalid/image.png",
                H使用状态 = isActive,
                H是否特殊商品 = false,
                H进货单主表GUID = $"order-{productCode}",
                H进货单详情GUID = $"order-detail-{productCode}",
                CBP商品中文名称 = productCode,
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

    private async Task SeedHqSetCodeAsync(
        string? hguid,
        string productCode,
        string setProductCode,
        bool isActive,
        DateTime lastModifyDate,
        string barcode
    )
    {
        await _hqDb.Insertable(
            new DIC_一品多码表
            {
                HGUID = hguid,
                H商品编码 = productCode,
                H多码商品编号 = setProductCode,
                H主条形码 = barcode,
                H多条形码 = barcode,
                H进货价 = 3.4m,
                H一品多码零售价 = 5.6m,
                H使用状态 = isActive,
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
