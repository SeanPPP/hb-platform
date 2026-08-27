using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("ProductHqSyncServiceTests")]
public sealed class WarehouseStorePriceHqSyncTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;

    public WarehouseStorePriceHqSyncTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();
        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(DomesticProduct),
            typeof(ProductSetCode),
            typeof(StoreMultiCodeProduct)
        );
        _hqDb.CodeFirst.InitTables(
            typeof(HqBranch),
            typeof(DIC_商品信息字典表),
            typeof(DIC_商品零售价表),
            typeof(DIC_一品多码表),
            typeof(DIC_分店一品多码表),
            typeof(CBP_DIC_商品库存表)
        );
    }

    [Fact]
    public async Task 专用HQ同步_已有主商品时只更新目标分店四字段()
    {
        await SeedBranchesAsync();
        await SeedLocalProductGraphAsync("P01", includeSetCode: false);
        await _hqDb.Insertable(new DIC_商品信息字典表
        {
            HGUID = "hq-product-existing",
            H商品标签GUID = string.Empty,
            H商品分类码GUID = string.Empty,
            H供货商编码 = "200",
            H商品编码 = "P01",
            H货号 = "P01-ITEM",
            H主条形码 = "P01-BARCODE",
            H商品名称 = "HQ旧名称",
            H大写名称 = string.Empty,
            H规格 = string.Empty,
            H单位 = string.Empty,
            H零售价 = 77m,
            H商品图片 = string.Empty,
            H腾讯云图地址 = string.Empty,
            H使用状态 = true,
            H进货单主表GUID = string.Empty,
            H进货单详情GUID = string.Empty,
            CBP商品中文名称 = string.Empty,
            CBP供应商编码 = string.Empty,
            CBP商品分类码GUID = string.Empty,
            FGC_Creator = "old-admin",
            FGC_CreateDate = DateTime.Now.AddDays(-10),
            FGC_LastModifier = "old-admin",
            FGC_LastModifyDate = DateTime.Now.AddDays(-10),
            FGC_UpdateHelp = string.Empty,
        }).ExecuteCommandAsync();
        var target = BuildHqRetail(1, "S01", "P01", 1m, 2m, 0.5m, true);
        target.H库存 = 123m;
        target.H活动类型 = "KEEP";
        target.H使用状态 = false;
        var untouched = BuildHqRetail(2, "S02", "P01", 3m, 4m, 0.6m, true);
        await _hqDb.Insertable(new[] { target, untouched }).ExecuteCommandAsync();

        var response = await CreateService().SyncWarehouseStorePricesAsync(
            new WarehouseStorePriceHqSyncRequestDto
            {
                TargetStoreCodes = ["S01"],
                UpdatedBy = "price-admin",
                Products =
                [
                    new WarehouseStorePriceHqProductDto
                    {
                        ProductCode = "P01",
                        ImportPrice = 11m,
                        OemPrice = 22m,
                    },
                ],
            }
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(0, response.Data?.HqCreatedCount);
        Assert.Equal(1, response.Data?.HqUpdatedCount);
        Assert.Equal(0, response.Data?.HqProvisionedProductCount);

        var updated = await _hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(row => row.H分店代码 == "S01" && row.H商品编码 == "P01");
        Assert.Equal(11m, updated.H进货价);
        Assert.Equal(22m, updated.H分店零售价);
        Assert.Equal(0m, updated.H折扣率);
        Assert.False(updated.H是否自动定价);
        Assert.Equal("price-admin", updated.FGC_LastModifier);
        Assert.Equal(123m, updated.H库存);
        Assert.Equal("KEEP", updated.H活动类型);
        Assert.False(updated.H使用状态);

        var otherStore = await _hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(row => row.H分店代码 == "S02" && row.H商品编码 == "P01");
        Assert.Equal(3m, otherStore.H进货价);
        Assert.Equal(4m, otherStore.H分店零售价);
        Assert.Equal(0.6m, otherStore.H折扣率);
        Assert.True(otherStore.H是否自动定价);

        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "P01");
        Assert.Equal("HQ旧名称", hqProduct.H商品名称);
        Assert.Equal(77m, hqProduct.H零售价);
        Assert.Equal(0, await _hqDb.Queryable<CBP_DIC_商品库存表>().CountAsync());
    }

    [Fact]
    public async Task 专用HQ同步_已有主商品但本地Product缺失时仍更新并补目标分店价格()
    {
        await SeedBranchesAsync();
        await _hqDb.Insertable(new DIC_商品信息字典表
        {
            HGUID = "hq-product-existing-without-local",
            H商品标签GUID = string.Empty,
            H商品分类码GUID = string.Empty,
            H商品编码 = "P03",
            H货号 = "P03-ITEM",
            H主条形码 = "P03-BARCODE",
            H商品名称 = "HQ已有商品",
            H大写名称 = string.Empty,
            H规格 = string.Empty,
            H单位 = string.Empty,
            H商品图片 = string.Empty,
            H腾讯云图地址 = string.Empty,
            H供货商编码 = "SUP03",
            H使用状态 = false,
            H是否特殊商品 = true,
            H进货单主表GUID = string.Empty,
            H进货单详情GUID = string.Empty,
            CBP商品中文名称 = string.Empty,
            CBP供应商编码 = string.Empty,
            CBP商品分类码GUID = string.Empty,
            FGC_Creator = "old-admin",
            FGC_CreateDate = DateTime.Now.AddDays(-10),
            FGC_LastModifier = "old-admin",
            FGC_LastModifyDate = DateTime.Now.AddDays(-10),
            FGC_UpdateHelp = string.Empty,
        }).ExecuteCommandAsync();
        await _hqDb.Insertable(BuildHqRetail(1, "S01", "P03", 1m, 2m, 0.5m, true))
            .ExecuteCommandAsync();

        var response = await CreateService().SyncWarehouseStorePricesAsync(
            new WarehouseStorePriceHqSyncRequestDto
            {
                TargetStoreCodes = ["S01", "S02"],
                UpdatedBy = "price-admin",
                Products =
                [
                    new WarehouseStorePriceHqProductDto
                    {
                        ProductCode = "P03",
                        ImportPrice = 13m,
                        OemPrice = 26m,
                    },
                ],
            }
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.HqCreatedCount);
        Assert.Equal(1, response.Data?.HqUpdatedCount);
        Assert.Equal(0, response.Data?.HqProvisionedProductCount);
        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "P03")
            .OrderBy(row => row.H分店代码)
            .ToListAsync();
        Assert.Equal(new[] { "S01", "S02" }, prices.Select(row => row.H分店代码));
        Assert.All(prices, row =>
        {
            Assert.Equal(13m, row.H进货价);
            Assert.Equal(26m, row.H分店零售价);
            Assert.Equal(0m, row.H折扣率);
            Assert.False(row.H是否自动定价);
            Assert.Equal("price-admin", row.FGC_LastModifier);
        });
        var created = prices.Single(row => row.H分店代码 == "S02");
        Assert.False(created.H使用状态);
        Assert.True(created.H是否特殊商品);
        Assert.Equal("S02SUP03", created.H分店供应商编码);
    }

    [Fact]
    public async Task 专用HQ同步_缺主商品且本地资料缺失时返回逐商品错误并零写入()
    {
        await SeedBranchesAsync();

        var response = await CreateService().SyncWarehouseStorePricesAsync(
            new WarehouseStorePriceHqSyncRequestDto
            {
                TargetStoreCodes = ["S01"],
                UpdatedBy = "price-admin",
                Products =
                [
                    new WarehouseStorePriceHqProductDto
                    {
                        ProductCode = "P404",
                        ImportPrice = 13m,
                        OemPrice = 26m,
                    },
                ],
            }
        );

        Assert.False(response.Success);
        Assert.Equal("HQ_PRODUCT_RESOLUTION_FAILED", response.ErrorCode);
        var result = Assert.IsType<WarehouseStorePriceHqSyncResultDto>(response.Details);
        Assert.Contains(
            result.Errors,
            error => error.ProductCode == "P404" && error.Code == "HQ_PRODUCT_RESOLUTION_FAILED"
        );
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<CBP_DIC_商品库存表>().CountAsync());
    }

    [Fact]
    public async Task 专用HQ同步_缺主商品时复用完整建档并为全部分店创建必要记录()
    {
        await SeedBranchesAsync();
        await SeedLocalProductGraphAsync("P02", includeSetCode: true);

        var response = await CreateService().SyncWarehouseStorePricesAsync(
            new WarehouseStorePriceHqSyncRequestDto
            {
                TargetStoreCodes = ["S01"],
                UpdatedBy = "price-admin",
                Products =
                [
                    new WarehouseStorePriceHqProductDto
                    {
                        ProductCode = "P02",
                        ImportPrice = 12m,
                        OemPrice = 24m,
                    },
                ],
            }
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.HqProvisionedProductCount);
        Assert.Equal(2, response.Data?.HqCreatedCount);
        Assert.Equal(0, response.Data?.HqUpdatedCount);
        Assert.Equal(1, await _hqDb.Queryable<DIC_商品信息字典表>()
            .Where(row => row.H商品编码 == "P02")
            .CountAsync());
        Assert.Equal(1, await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .Where(row => row.H商品编码 == "P02")
            .CountAsync());
        Assert.Equal(1, await _hqDb.Queryable<DIC_一品多码表>()
            .Where(row => row.H商品编码 == "P02")
            .CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "P02")
            .CountAsync());

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "P02")
            .OrderBy(row => row.H分店代码)
            .ToListAsync();
        Assert.Equal(new[] { "S01", "S02" }, prices.Select(row => row.H分店代码));
        Assert.All(prices, row =>
        {
            Assert.Equal(12m, row.H进货价);
            Assert.Equal(24m, row.H分店零售价);
            Assert.Equal(0m, row.H折扣率);
            Assert.False(row.H是否自动定价);
            Assert.Equal("price-admin", row.FGC_Creator);
            Assert.Equal("price-admin", row.FGC_LastModifier);
        });

        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "P02");
        Assert.Equal("price-admin", hqProduct.FGC_Creator);
        Assert.Equal("price-admin", hqProduct.FGC_LastModifier);
        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "P02");
        Assert.Equal("price-admin", inventory.FGC_Creator);
        Assert.Equal("price-admin", inventory.FGC_LastModifier);
        var productSetCode = await _hqDb.Queryable<DIC_一品多码表>()
            .SingleAsync(row => row.H商品编码 == "P02");
        Assert.Equal("price-admin", productSetCode.FGC_Creator);
        Assert.Equal("price-admin", productSetCode.FGC_LastModifier);
        var storeSetCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "P02")
            .ToListAsync();
        Assert.All(storeSetCodes, row =>
        {
            Assert.Equal("price-admin", row.FGC_Creator);
            Assert.Equal("price-admin", row.FGC_LastModifier);
        });
    }

    [Fact]
    public async Task 专用HQ同步_缺主商品建档时普通供应商统一使用本地商品供应商()
    {
        await SeedBranchesAsync();
        await SeedLocalProductGraphAsync("P04", includeSetCode: true);
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { LocalSupplierCode = "WAREHOUSE-SUP" })
            .Where(row => row.ProductCode == "P04")
            .ExecuteCommandAsync();

        var response = await CreateService().SyncWarehouseStorePricesAsync(
            new WarehouseStorePriceHqSyncRequestDto
            {
                TargetStoreCodes = ["S01"],
                UpdatedBy = "price-admin",
                Products =
                [
                    new WarehouseStorePriceHqProductDto
                    {
                        ProductCode = "P04",
                        ImportPrice = 12m,
                        OemPrice = 24m,
                    },
                ],
            }
        );

        Assert.True(response.Success, response.Message);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "P04");
        Assert.Equal("WAREHOUSE-SUP", product.H供货商编码);
        Assert.Equal("CN-SUP", product.CBP供应商编码);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "P04")
            .ToListAsync();
        Assert.All(prices, row =>
        {
            Assert.Equal("WAREHOUSE-SUP", row.H供应商编码);
            Assert.Equal(row.H分店代码 + "WAREHOUSE-SUP", row.H分店供应商编码);
        });
        Assert.Equal(
            "WAREHOUSE-SUP",
            (await _hqDb.Queryable<DIC_一品多码表>()
                .SingleAsync(row => row.H商品编码 == "P04")).H供应商编码
        );
        Assert.All(
            await _hqDb.Queryable<DIC_分店一品多码表>()
                .Where(row => row.H商品编码 == "P04")
                .ToListAsync(),
            row => Assert.Equal("WAREHOUSE-SUP", row.H供应商编码)
        );
    }

    [Fact]
    public async Task 专用HQ预校验_目标分店大小写去重且任一缺失时整体失败()
    {
        await SeedBranchesAsync();

        var response = await CreateService().ValidateWarehouseStorePriceTargetsAsync(
            [" s01 ", "S01", "missing"]
        );

        Assert.False(response.Success);
        Assert.Equal("HQ_TARGET_STORE_NOT_FOUND", response.ErrorCode);
        var result = Assert.IsType<WarehouseStorePriceHqValidationResultDto>(response.Details);
        Assert.Equal(["S01"], result.CanonicalTargetStoreCodes);
        var error = Assert.Single(result.Errors);
        Assert.Equal("missing", error.StoreCode);
        Assert.Equal("HqValidation", error.Stage);
    }

    [Fact]
    public async Task 专用HQ同步_数据库失败时不向客户端泄露原始异常()
    {
        await SeedBranchesAsync();
        await SeedLocalProductGraphAsync("P03", includeSetCode: false);
        await _hqDb.Ado.ExecuteCommandAsync(
            """
            CREATE TRIGGER fail_hq_price_insert
            BEFORE INSERT ON "DIC_商品零售价表"
            BEGIN
                SELECT RAISE(ABORT, 'sensitive hq sql detail');
            END;
            """
        );

        var response = await CreateService().SyncWarehouseStorePricesAsync(
            new WarehouseStorePriceHqSyncRequestDto
            {
                TargetStoreCodes = ["S01"],
                UpdatedBy = "price-admin",
                Products =
                [
                    new WarehouseStorePriceHqProductDto
                    {
                        ProductCode = "P03",
                        ImportPrice = 12m,
                        OemPrice = 24m,
                    },
                ],
            }
        );

        Assert.False(response.Success);
        Assert.Equal("WAREHOUSE_STORE_PRICE_HQ_SYNC_FAILED", response.ErrorCode);
        var result = Assert.IsType<WarehouseStorePriceHqSyncResultDto>(response.Details);
        var error = Assert.Single(result.Errors);
        Assert.Equal("仓库商品价格 HQ 同步失败", error.Message);
        Assert.DoesNotContain("sensitive hq sql detail", error.Message);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>()
            .Where(row => row.H商品编码 == "P03")
            .CountAsync());
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

    private async Task SeedBranchesAsync()
    {
        await _hqDb.Insertable(new[]
        {
            new HqBranch { BranchCode = "S01", BranchName = "一店" },
            new HqBranch { BranchCode = "S02", BranchName = "二店" },
        }).ExecuteCommandAsync();
    }

    private async Task SeedLocalProductGraphAsync(string productCode, bool includeSetCode)
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"product-{productCode}",
            ProductCode = productCode,
            LocalSupplierCode = "200",
            ItemNumber = $"{productCode}-ITEM",
            Barcode = $"{productCode}-BARCODE",
            ProductName = $"{productCode}中文",
            EnglishName = $"{productCode} English",
            ProductType = includeSetCode ? 2 : 0,
            PurchasePrice = 1m,
            RetailPrice = 2m,
            IsAutoPricing = true,
            IsSpecialProduct = true,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            SupplierCode = "CN-SUP",
            HBProductNo = $"{productCode}-ITEM",
            ProductName = $"{productCode}国内商品",
            IsDeleted = false,
        }).ExecuteCommandAsync();

        if (!includeSetCode)
        {
            return;
        }

        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = $"set-{productCode}",
            ProductCode = productCode,
            SetProductCode = $"{productCode}-M1",
            SetItemNumber = $"{productCode}-M1",
            SetBarcode = $"{productCode}-M1-BARCODE",
            SetPurchasePrice = 6m,
            SetRetailPrice = 12m,
            SetQuantity = 1,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = $"store-multi-{productCode}",
            StoreCode = "S01",
            ProductCode = productCode,
            MultiCodeProductCode = $"{productCode}-M1",
            StoreMultiCodeProductCode = $"S01{productCode}-M1",
            MultiBarcode = $"{productCode}-M1-BARCODE",
            PurchasePrice = 6m,
            MultiCodeRetailPrice = 12m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private ProductHqSyncService CreateService()
    {
        return new ProductHqSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, _hqConnection.ConnectionString),
            Mock.Of<IMapper>(),
            Mock.Of<ILogger<ProductHqSyncService>>(),
            new ProductAuditNoopHistoryService(),
            new ProductAuditSystemCurrentUserService()
        );
    }

    private static DIC_商品零售价表 BuildHqRetail(
        int id,
        string storeCode,
        string productCode,
        decimal purchasePrice,
        decimal retailPrice,
        decimal discountRate,
        bool autoPricing
    )
    {
        return new DIC_商品零售价表
        {
            ID = id,
            HGUID = $"price-{storeCode}-{productCode}",
            H分店代码 = storeCode,
            H商品编码 = productCode,
            H分店商品编码 = storeCode + productCode,
            H供应商编码 = "200",
            H分店供应商编码 = storeCode + "200",
            H进货价 = purchasePrice,
            H分店零售价 = retailPrice,
            H折扣率 = discountRate,
            H是否自动定价 = autoPricing,
            H使用状态 = true,
            FGC_Creator = "old-admin",
            FGC_CreateDate = DateTime.Now.AddDays(-10),
            FGC_LastModifier = "old-admin",
            FGC_LastModifyDate = DateTime.Now.AddDays(-10),
        };
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

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
        string connectionString
    )
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HqSqlSugarContext)
        );
        typeof(HqSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StoreHzgHQConnection"] = connectionString,
            })
            .Build();
        typeof(HqSqlSugarContext)
            .GetField("<Configuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, configuration);
        return context;
    }
}
