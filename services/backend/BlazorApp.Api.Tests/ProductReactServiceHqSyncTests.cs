using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Mappings;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductReactServiceHqSyncTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hqDb;
    private readonly IMapper _mapper;

    public ProductReactServiceHqSyncTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarScope(CreateConnectionConfig(_hqConnection.ConnectionString));
        _mapper = CreateMapper();

        // 只初始化本次同步链路依赖的最小表集合，避免测试基建过重。
        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(Store),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct),
            typeof(ProductSetCode)
        );
        _hqDb.CodeFirst.InitTables(
            typeof(DIC_商品信息字典表),
            typeof(DIC_商品零售价表),
            typeof(DIC_分店一品多码表)
        );
    }

    [Fact]
    public async Task SyncProductsFromHqAsync_本地存在同编码软删商品时_应该恢复原记录且不新增重复商品和关联表()
    {
        const string productCode = "P-RESTORE-001";
        var localUpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hqUpdatedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await SeedActiveStoreAsync("S01");
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-product-restore",
                ProductCode = productCode,
                LocalSupplierCode = "SUP-OLD",
                ItemNumber = "ITEM-OLD",
                Barcode = "BAR-OLD",
                ProductName = "旧商品",
                ProductType = 1,
                PurchasePrice = 1.2m,
                RetailPrice = 2.4m,
                IsActive = false,
                IsAutoPricing = false,
                IsSpecialProduct = false,
                CreatedAt = localUpdatedAt,
                UpdatedAt = localUpdatedAt,
                IsDeleted = true,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new StoreRetailPrice
            {
                UUID = "retail-restore-1",
                StoreCode = "S01",
                ProductCode = productCode,
                SupplierCode = "SUP-OLD",
                PurchasePrice = 1.2m,
                StoreRetailPriceValue = 2.4m,
                IsActive = false,
                IsAutoPricing = false,
                IsSpecialProduct = false,
                CreatedAt = localUpdatedAt,
                UpdatedAt = localUpdatedAt,
                IsDeleted = true,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new StoreMultiCodeProduct
            {
                UUID = "multi-restore-1",
                StoreCode = "S01",
                ProductCode = productCode,
                MultiCodeProductCode = "MULTI-OLD",
                StoreMultiCodeProductCode = "S01-MULTI-OLD",
                MultiBarcode = "MULTI-BAR-OLD",
                PurchasePrice = 1.1m,
                MultiCodeRetailPrice = 2.3m,
                IsActive = false,
                IsAutoPricing = false,
                IsSpecialProduct = false,
                CreatedAt = localUpdatedAt,
                UpdatedAt = localUpdatedAt,
                IsDeleted = true,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = "set-restore-1",
                ProductCode = productCode,
                SetProductCode = "MULTI-OLD",
                SetItemNumber = "MULTI-OLD",
                SetBarcode = "MULTI-BAR-OLD",
                SetPurchasePrice = 1.1m,
                SetRetailPrice = 2.3m,
                SetType = 2,
                SetQuantity = 1,
                IsActive = false,
                CreatedAt = localUpdatedAt,
                UpdatedAt = localUpdatedAt,
                IsDeleted = true,
            }
        ).ExecuteCommandAsync();

        await SeedHqProductAsync(productCode, hqUpdatedAt, "SUP-NEW", "ITEM-NEW", "商品新名称", "BAR-NEW");
        await _hqDb.Insertable(
            new DIC_商品零售价表
            {
                HGUID = "hq-retail-restore-1",
                H分店代码 = "S01",
                H商品编码 = productCode,
                H分店商品编码 = "S01-P-RESTORE-001",
                H供应商编码 = "SUP-NEW",
                H分店供应商编码 = "S01-SUP-NEW",
                H进货价 = 8.8m,
                H分店零售价 = 12.8m,
                H折扣率 = 0.9m,
                H使用状态 = true,
                H是否自动定价 = true,
                H是否特殊商品 = true,
                FGC_CreateDate = hqUpdatedAt,
                FGC_LastModifyDate = hqUpdatedAt,
            }
        ).ExecuteCommandAsync();
        await _hqDb.Insertable(
            new DIC_分店一品多码表
            {
                HGUID = "hq-multi-restore-1",
                H分店代码 = "S01",
                H商品编码 = productCode,
                H分店商品编码 = "S01-P-RESTORE-001",
                H多码商品编码 = "MULTI-NEW",
                H分店多码商品编码 = "S01-MULTI-NEW",
                H供应商编码 = "SUP-NEW",
                H多条形码 = "MULTI-BAR-NEW",
                H进货价 = 8.3m,
                H折扣率 = 0.88m,
                H一品多码零售价 = 12.6m,
                H使用状态 = true,
                H是否自动定价 = true,
                H是否特殊商品 = true,
                FGC_CreateDate = hqUpdatedAt,
                FGC_LastModifyDate = hqUpdatedAt,
            }
        ).ExecuteCommandAsync();

        var service = CreateService();

        var response = await service.SyncProductsFromHqAsync();

        Assert.True(response.Success, response.Message);
        Assert.Empty(response.Data?.Errors ?? new List<string>());

        var products = await _localDb.Queryable<Product>()
            .Where(x => x.ProductCode == productCode)
            .ToListAsync();
        var retailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == productCode)
            .ToListAsync();
        var multiCodes = await _localDb.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.ProductCode == productCode)
            .ToListAsync();
        var setCodes = await _localDb.Queryable<ProductSetCode>()
            .Where(x => x.ProductCode == productCode)
            .ToListAsync();

        var product = Assert.Single(products);
        Assert.Equal("local-product-restore", product.UUID);
        Assert.False(product.IsDeleted);
        Assert.Single(retailPrices);
        Assert.Equal("hq-retail-restore-1", retailPrices[0].UUID);
        Assert.False(retailPrices[0].IsDeleted);
        Assert.Single(multiCodes);
        Assert.Equal("hq-multi-restore-1", multiCodes[0].UUID);
        Assert.False(multiCodes[0].IsDeleted);
        Assert.Single(setCodes);
        Assert.Equal("hq-multi-restore-1", setCodes[0].SetCodeId);
        Assert.False(setCodes[0].IsDeleted);
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hqConnection.Dispose();

        if (File.Exists(_localDbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        }

        if (File.Exists(_hqDbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
        }
    }

    private ProductReactService CreateService()
    {
        return new ProductReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            _mapper,
            NullLogger<ProductReactService>.Instance,
            new HttpContextAccessor()
        );
    }

    private async Task SeedActiveStoreAsync(string storeCode)
    {
        await _localDb.Insertable(
            new Store
            {
                StoreGUID = $"store-{storeCode}",
                StoreCode = storeCode,
                StoreName = $"门店{storeCode}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqProductAsync(
        string productCode,
        DateTime lastModifyDate,
        string supplierCode,
        string itemNumber,
        string productName,
        string barcode
    )
    {
        await _hqDb.Insertable(
            new DIC_商品信息字典表
            {
                HGUID = $"hq-product-{productCode}",
                H商品标签GUID = $"tag-{productCode}",
                H商品分类码GUID = "CATEGORY-NEW",
                H供货商编码 = supplierCode,
                H商品编码 = productCode,
                H货号 = itemNumber,
                H主条形码 = barcode,
                H商品名称 = productName,
                H商品类型 = 2,
                H大写名称 = productName.ToUpperInvariant(),
                H规格 = "默认规格",
                H单位 = "EA",
                H进货价 = 8.8m,
                H零售价 = 12.8m,
                H是否自动定价 = true,
                H商品图片 = "hq-image.png",
                中包数量 = 6,
                H腾讯云图地址 = "https://example.invalid/image.png",
                H使用状态 = true,
                H是否特殊商品 = true,
                H进货单主表GUID = $"order-{productCode}",
                H进货单详情GUID = $"order-detail-{productCode}",
                CBP商品中文名称 = productName,
                CBP供应商编码 = supplierCode,
                CBP商品分类码GUID = "WAREHOUSE-NEW",
                FGC_Creator = "HQ",
                FGC_LastModifier = "HQ",
                FGC_CreateDate = lastModifyDate.AddMinutes(-10),
                FGC_LastModifyDate = lastModifyDate,
                FGC_UpdateHelp = "test",
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
            cfg => cfg.AddProfile<ReactProductMappingProfile>(),
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
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HqSqlSugarContext)
        );

        // 这里显式注入 SqlSugar 与配置，确保测试能稳定命中真实同步逻辑。
        var dbField = typeof(HqSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);

        var configurationField = typeof(HqSqlSugarContext).GetField(
            "<Configuration>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        configurationField!.SetValue(context, configuration);

        return context;
    }
}
