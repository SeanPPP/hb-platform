using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("ProductHqSyncServiceTests")]
public sealed class ProductPushToHqServiceTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;

    public ProductPushToHqServiceTests()
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
            typeof(WarehouseProduct),
            typeof(DomesticProduct),
            typeof(ProductSetCode),
            typeof(StoreRetailPrice),
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
    public async Task SyncProductImagesAsync_只覆盖唯一匹配的HQ图片字段并报告逐项失败()
    {
        await _hqDb.Insertable(new[]
        {
            CreateHqImageRow(1, "HB-IMAGE-OK", "https://old/ok.jpg", "https://keep/tencent-ok.jpg"),
            CreateHqImageRow(2, "HB-IMAGE-DUP", "https://old/dup-1.jpg", "https://keep/tencent-dup-1.jpg"),
            CreateHqImageRow(3, "HB-IMAGE-DUP", "https://old/dup-2.jpg", "https://keep/tencent-dup-2.jpg"),
        }).ExecuteCommandAsync();

        var items = new List<ProductHqImageUpdateItemDto>();
        foreach (var (productCode, imageUrl) in new[]
        {
            ("HB-IMAGE-OK", "https://new/ok.jpg"),
            ("HB-IMAGE-DUP", "https://new/dup.jpg"),
            ("HB-IMAGE-MISSING", "https://new/missing.jpg"),
        })
        {
            items.Add(new ProductHqImageUpdateItemDto
            {
                ProductCode = productCode,
                ImageUrl = imageUrl,
            });
        }

        var result = await CreateService().SyncProductImagesAsync(items, "仓库经理A");
        var unique = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(item => item.H商品编码 == "HB-IMAGE-OK");
        var duplicates = await _hqDb.Queryable<DIC_商品信息字典表>()
            .Where(item => item.H商品编码 == "HB-IMAGE-DUP")
            .OrderBy(item => item.ID)
            .ToListAsync();

        Assert.False(result.Success);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal("HQ_IMAGE_SYNC_PARTIAL", result.ErrorCode);
        Assert.Equal("https://new/ok.jpg", unique.H商品图片);
        Assert.Equal("https://keep/tencent-ok.jpg", unique.H腾讯云图地址);
        Assert.Equal("仓库经理A", unique.FGC_LastModifier);
        Assert.Collection(
            duplicates,
            first => Assert.Equal("https://old/dup-1.jpg", first.H商品图片),
            second => Assert.Equal("https://old/dup-2.jpg", second.H商品图片)
        );
    }

    [Fact]
    public async Task SyncProductImagesAsync_同步锁占用时立即返回忙碌且不写HQ()
    {
        await _hqDb.Insertable(
            CreateHqImageRow(
                11,
                "HB-IMAGE-BUSY",
                "https://old/busy.jpg",
                "https://keep/tencent-busy.jpg"
            )
        ).ExecuteCommandAsync();
        var syncLock = Assert.IsType<SemaphoreSlim>(
            typeof(ProductHqSyncService)
                .GetField("SyncLock", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)
        );
        await syncLock.WaitAsync();
        try
        {
            var result = await CreateService().SyncProductImagesAsync(
                new List<ProductHqImageUpdateItemDto>
                {
                    new()
                    {
                        ProductCode = "HB-IMAGE-BUSY",
                        ImageUrl = "https://new/busy.jpg",
                    },
                },
                "仓库经理A"
            );

            var row = await _hqDb.Queryable<DIC_商品信息字典表>()
                .SingleAsync(item => item.H商品编码 == "HB-IMAGE-BUSY");
            Assert.False(result.Success);
            Assert.Equal("HQ_IMAGE_SYNC_BUSY", result.ErrorCode);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal("https://old/busy.jpg", row.H商品图片);
        }
        finally
        {
            syncLock.Release();
        }
    }

    [Fact]
    public async Task SyncProductImagesAsync_HQ写入异常时回滚本批全部图片()
    {
        await _hqDb.Insertable(new[]
        {
            CreateHqImageRow(21, "HB-IMAGE-ROLLBACK-1", "https://old/rollback-1.jpg", string.Empty),
            CreateHqImageRow(22, "HB-IMAGE-ROLLBACK-2", "https://old/rollback-2.jpg", string.Empty),
        }).ExecuteCommandAsync();
        _hqDb.Ado.ExecuteCommand(
            """
            CREATE TRIGGER fail_hq_image_update
            BEFORE UPDATE OF "H商品图片" ON "DIC_商品信息字典表"
            WHEN NEW."H商品编码" = 'HB-IMAGE-ROLLBACK-2'
            BEGIN
                SELECT RAISE(ABORT, 'forced hq image failure');
            END
            """
        );

        var result = await CreateService().SyncProductImagesAsync(
            new List<ProductHqImageUpdateItemDto>
            {
                new()
                {
                    ProductCode = "HB-IMAGE-ROLLBACK-1",
                    ImageUrl = "https://new/rollback-1.jpg",
                },
                new()
                {
                    ProductCode = "HB-IMAGE-ROLLBACK-2",
                    ImageUrl = "https://new/rollback-2.jpg",
                },
            },
            "仓库经理A"
        );

        var rows = await _hqDb.Queryable<DIC_商品信息字典表>()
            .Where(item => item.H商品编码 != null && item.H商品编码.StartsWith("HB-IMAGE-ROLLBACK"))
            .OrderBy(item => item.ID)
            .ToListAsync();
        Assert.False(result.Success);
        Assert.Equal("HQ_IMAGE_SYNC_ERROR", result.ErrorCode);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Collection(
            rows,
            first => Assert.Equal("https://old/rollback-1.jpg", first.H商品图片),
            second => Assert.Equal("https://old/rollback-2.jpg", second.H商品图片)
        );
    }

    [Fact]
    public async Task PushToHqAsync_空商品编码_返回验证错误()
    {
        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest());

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_EMPTY_CODES", response.ErrorCode);
    }

    [Fact]
    public async Task PushToHqAsync_首次推送_创建商品分店价格多码分店多码和库存状态()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { " HB001 " },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(0, response.Data?.FailedCount);
        Assert.Equal(1, response.Data?.TotalCount);
        Assert.Equal(1, response.Data?.ProductsAdded);
        Assert.Equal(2, response.Data?.StoreRetailPricesCreated);
        Assert.Equal(1, response.Data?.ProductSetCodesCreated);
        Assert.Equal(2, response.Data?.StoreMultiCodesCreated);
        Assert.Equal(1, response.Data?.WarehouseInventoriesCreated);
        Assert.Equal(7, response.Data?.AffectedRowCount);

        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("HB001-ITEM", product.H货号);
        Assert.Equal("测试商品英文", product.H商品名称);
        Assert.Equal("SUP01", product.CBP供应商编码);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, prices.Count);
        Assert.Contains(prices, row => row.H分店代码 == "S01" && row.H分店零售价 == 4.5m);
        Assert.Contains(prices, row => row.H分店代码 == "S02" && row.H分店商品编码 == "S02HB001");

        var setCode = await _hqDb.Queryable<DIC_一品多码表>()
            .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1");
        Assert.Equal("set-1", setCode.HGUID);
        Assert.Equal("952700000002", setCode.H多条形码);
        Assert.Equal(2.5m, setCode.H进货价);
        Assert.Equal(4.0m, setCode.H一品多码零售价);

        var storeMultiCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1")
            .ToListAsync();
        Assert.Equal(2, storeMultiCodes.Count);
        Assert.Contains(storeMultiCodes, row =>
            row.H分店代码 == "S01"
            && row.H进货价 == 2.5m
            && row.H一品多码零售价 == 3.9m
        );
        Assert.Contains(storeMultiCodes, row =>
            row.H分店代码 == "S02"
            && row.H进货价 == 2.5m
            && row.H一品多码零售价 == 4.0m
        );

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(1, inventory.H使用状态);
    }

    [Fact]
    public async Task PushToHqAsync_带货柜候选_仅业务字段以货柜明细为准且不改HQ启用状态()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product
            {
                Barcode = "LOCAL-BARCODE",
                ProductName = "本地商品名",
                EnglishName = "Local English",
                ProductImage = "local-image.jpg",
                PurchasePrice = 2.5m,
                RetailPrice = null,
            })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    localSupplierCode: "CARGO-SUP",
                    itemNumber: "CARGO-ITEM",
                    productName: "货柜商品名",
                    englishName: "Cargo English",
                    barcode: "CARGO-BARCODE",
                    imageUrl: "cargo-image.jpg",
                    domesticPrice: 8.88m,
                    importPrice: 1.23m,
                    oemPrice: 4.99m,
                    warehouseIsActive: false
                ),
            },
        });

        Assert.True(response.Success, response.Message);

        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("CARGO-ITEM", product.H货号);
        Assert.Equal("CARGO-BARCODE", product.H主条形码);
        Assert.Equal("Cargo English", product.H商品名称);
        Assert.Equal("货柜商品名", product.H大写名称);
        Assert.Equal(1.23m, product.H进货价);
        Assert.Equal(4.99m, product.H零售价);
        Assert.Equal("cargo-image.jpg", product.H商品图片);
        Assert.True(product.H使用状态);
        Assert.Equal("SUP01", product.CBP供应商编码);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, prices.Count);
        Assert.All(prices, row =>
        {
            Assert.Equal(1.23m, row.H进货价);
            Assert.Equal(4.99m, row.H分店零售价);
            Assert.True(row.H使用状态);
            Assert.EndsWith("CARGO-SUP", row.H分店供应商编码);
        });

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(8.88m, inventory.H国内价格);
        Assert.Equal(1.23m, inventory.H进口价格);
        Assert.Equal(4.99m, inventory.H贴牌价格);
        Assert.Equal(1, inventory.H使用状态);
    }

    [Fact]
    public async Task PushToHqAsync_只选分店零售价_不改商品字典和库存价格()
    {
        await SeedProductGraphAsync();
        var service = CreateService();
        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    itemNumber: "CARGO-ITEM",
                    productName: "首次商品名",
                    englishName: "First English",
                    barcode: "FIRST-BARCODE",
                    domesticPrice: 8.88m,
                    importPrice: 1.23m,
                    oemPrice: 4.99m
                ),
            },
        });
        Assert.True(first.Success, first.Message);

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            UpdateFields = new List<string> { "storeRetailPrice" },
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    itemNumber: "NEW-ITEM",
                    productName: "不应写入商品名",
                    englishName: "Should Not Update",
                    barcode: "NEW-BARCODE",
                    domesticPrice: 6.66m,
                    importPrice: 9.99m,
                    oemPrice: 7.77m
                ),
            },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(0, second.Data?.ProductsUpdated);
        Assert.Equal(2, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(0, second.Data?.WarehouseInventoriesUpdated);

        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("CARGO-ITEM", product.H货号);
        Assert.Equal("FIRST-BARCODE", product.H主条形码);
        Assert.Equal("First English", product.H商品名称);
        Assert.Equal(1.23m, product.H进货价);
        Assert.Equal(4.99m, product.H零售价);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.All(prices, row =>
        {
            Assert.Equal(1.23m, row.H进货价);
            Assert.Equal(7.77m, row.H分店零售价);
        });

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(8.88m, inventory.H国内价格);
        Assert.Equal(1.23m, inventory.H进口价格);
        Assert.Equal(4.99m, inventory.H贴牌价格);
    }

    [Fact]
    public async Task PushToHqAsync_只选库存进口价_不改分店价格()
    {
        await SeedProductGraphAsync();
        var service = CreateService();
        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    domesticPrice: 8.88m,
                    importPrice: 1.23m,
                    oemPrice: 4.99m
                ),
            },
        });
        Assert.True(first.Success, first.Message);

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            UpdateFields = new List<string> { "inventoryImportPrice" },
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    domesticPrice: 6.66m,
                    importPrice: 9.99m,
                    oemPrice: 7.77m
                ),
            },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(0, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(1, second.Data?.WarehouseInventoriesUpdated);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.All(prices, row =>
        {
            Assert.Equal(1.23m, row.H进货价);
            Assert.Equal(4.99m, row.H分店零售价);
        });

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(8.88m, inventory.H国内价格);
        Assert.Equal(9.99m, inventory.H进口价格);
        Assert.Equal(4.99m, inventory.H贴牌价格);
    }

    [Fact]
    public async Task PushToHqAsync_货柜候选无图且商品主档无图_回退国内商品图片()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { ProductImage = null })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(productCode: "HB001", itemNumber: "HB001-ITEM"),
            },
        });

        Assert.True(response.Success, response.Message);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("HB001-domestic.jpg", product.H商品图片);
    }

    [Fact]
    public async Task PushToHqAsync_重复推送_更新既有记录且不重复创建()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);
        await _hqDb.Updateable<DIC_商品信息字典表>()
            .SetColumns(row => new DIC_商品信息字典表
            {
                CBP供应商编码 = "OLD-SUP",
            })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetPurchasePrice = 2.6m,
                SetRetailPrice = 4.6m,
                IsActive = false,
            })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                PurchasePrice = 2.4m,
                MultiCodeRetailPrice = 4.4m,
                IsActive = false,
            })
            .Where(row => row.StoreCode == "S01" && row.ProductCode == "HB001" && row.MultiCodeProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                PurchasePrice = 2.6m,
                MultiCodeRetailPrice = 4.6m,
                IsActive = false,
            })
            .Where(row => row.StoreCode == "S02" && row.ProductCode == "HB001" && row.MultiCodeProductCode == "HB001-M1")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data?.ProductsUpdated);
        Assert.Equal(2, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(1, second.Data?.ProductSetCodesUpdated);
        Assert.Equal(2, second.Data?.StoreMultiCodesUpdated);
        Assert.Equal(1, second.Data?.WarehouseInventoriesUpdated);
        Assert.Equal(7, second.Data?.AffectedRowCount);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("SUP01", product.CBP供应商编码);
        Assert.Equal(1, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(1, await _hqDb.Queryable<DIC_一品多码表>().CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());

        var setCode = await _hqDb.Queryable<DIC_一品多码表>()
            .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1");
        Assert.Equal(2.6m, setCode.H进货价);
        Assert.Equal(4.6m, setCode.H一品多码零售价);
        Assert.False(setCode.H使用状态);

        var storeMultiCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1")
            .ToListAsync();
        Assert.Contains(storeMultiCodes, row =>
            row.H分店代码 == "S01"
            && row.H进货价 == 2.4m
            && row.H一品多码零售价 == 4.4m
            && row.H使用状态 == false
        );
        Assert.Contains(storeMultiCodes, row =>
            row.H分店代码 == "S02"
            && row.H进货价 == 2.6m
            && row.H一品多码零售价 == 4.6m
            && row.H使用状态 == false
        );
    }

    [Fact]
    public async Task PushToHqAsync_重复推送_按主键ID批量更新而非逐行()
    {
        await SeedProductGraphAsync();
        await _hqDb.Insertable(
            Enumerable.Range(3, 39)
                .Select(index => new HqBranch
                {
                    BranchCode = $"S{index:00}",
                    BranchName = $"测试分店{index:00}",
                })
                .ToList()
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            Enumerable.Range(3, 39)
                .Select(index => new StoreMultiCodeProduct
                {
                    UUID = $"store-multi-s{index:00}",
                    StoreCode = $"S{index:00}",
                    ProductCode = "HB001",
                    MultiCodeProductCode = "HB001-M1",
                    StoreMultiCodeProductCode = $"S{index:00}HB001-M1",
                    MultiBarcode = "952700000002",
                    PurchasePrice = 9m,
                    MultiCodeRetailPrice = 4m,
                    IsActive = true,
                    IsDeleted = false,
                })
                .ToList()
        ).ExecuteCommandAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        var hqUpdateCommands = 0;
        _hqDb.Aop.OnLogExecuting = (sql, _) =>
        {
            if (IsHqUpdateCommand(sql))
            {
                hqUpdateCommands++;
            }
        };

        ApiResponse<PushProductsToHqResult> second;
        try
        {
            second = await service.PushToHqAsync(new PushProductsToHqRequest
            {
                ProductCodes = new List<string> { "HB001" },
            });
        }
        finally
        {
            _hqDb.Aop.OnLogExecuting = null;
        }

        Assert.True(second.Success, second.Message);
        var updatedRows =
            (second.Data?.ProductsUpdated ?? 0)
            + (second.Data?.StoreRetailPricesUpdated ?? 0)
            + (second.Data?.ProductSetCodesUpdated ?? 0)
            + (second.Data?.StoreMultiCodesUpdated ?? 0)
            + (second.Data?.WarehouseInventoriesUpdated ?? 0);
        Assert.Equal(85, updatedRows);
        Assert.Equal(41, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(41, second.Data?.StoreMultiCodesUpdated);
        Assert.Equal(7, hqUpdateCommands);
    }

    [Fact]
    public async Task PushToHqAsync_Hq重复业务键_更新全部物理记录且逻辑计数不膨胀()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        var duplicateProduct = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        duplicateProduct.ID = 0;
        duplicateProduct.HGUID = "duplicate-product";
        duplicateProduct.H商品名称 = "旧英文名";
        duplicateProduct.CBP供应商编码 = "OLD-SUP";
        await _hqDb.Insertable(duplicateProduct)
            .IgnoreColumns(row => row.ID)
            .ExecuteCommandAsync();

        var duplicateRetail = await _hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(row => row.H分店代码 == "S01" && row.H商品编码 == "HB001");
        duplicateRetail.ID = 0;
        duplicateRetail.HGUID = "duplicate-retail";
        duplicateRetail.H进货价 = -1m;
        duplicateRetail.H分店零售价 = -1m;
        await _hqDb.Insertable(duplicateRetail)
            .IgnoreColumns(row => row.ID)
            .ExecuteCommandAsync();

        var duplicateSetCode = await _hqDb.Queryable<DIC_一品多码表>()
            .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1");
        duplicateSetCode.ID = 0;
        duplicateSetCode.HGUID = "duplicate-set-code";
        duplicateSetCode.H进货价 = -1m;
        duplicateSetCode.H一品多码零售价 = -1m;
        await _hqDb.Insertable(duplicateSetCode)
            .IgnoreColumns(row => row.ID)
            .ExecuteCommandAsync();

        var duplicateStoreMultiCode = await _hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(row =>
                row.H分店代码 == "S01"
                && row.H商品编码 == "HB001"
                && row.H多码商品编码 == "HB001-M1"
            );
        duplicateStoreMultiCode.ID = 0;
        duplicateStoreMultiCode.HGUID = "duplicate-store-multi-code";
        duplicateStoreMultiCode.H分店多码商品编码 = "S01-HISTORICAL-DUPLICATE";
        duplicateStoreMultiCode.H进货价 = -1m;
        duplicateStoreMultiCode.H一品多码零售价 = -1m;
        await _hqDb.Insertable(duplicateStoreMultiCode)
            .IgnoreColumns(row => row.ID)
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data?.ProductsUpdated);
        Assert.Equal(2, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(1, second.Data?.ProductSetCodesUpdated);
        Assert.Equal(2, second.Data?.StoreMultiCodesUpdated);
        Assert.Equal(1, second.Data?.WarehouseInventoriesUpdated);
        Assert.Equal(7, second.Data?.AffectedRowCount);

        var products = await _hqDb.Queryable<DIC_商品信息字典表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, products.Count);
        Assert.All(products, row =>
        {
            Assert.Equal("测试商品英文", row.H商品名称);
            Assert.Equal("SUP01", row.CBP供应商编码);
        });

        var retailPrices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H分店代码 == "S01" && row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, retailPrices.Count);
        Assert.All(retailPrices, row =>
        {
            Assert.Equal(2.5m, row.H进货价);
            Assert.Equal(4.5m, row.H分店零售价);
        });

        var setCodes = await _hqDb.Queryable<DIC_一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1")
            .ToListAsync();
        Assert.Equal(2, setCodes.Count);
        Assert.All(setCodes, row =>
        {
            Assert.Equal(2.5m, row.H进货价);
            Assert.Equal(4.0m, row.H一品多码零售价);
        });

        var storeMultiCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row =>
                row.H分店代码 == "S01"
                && row.H商品编码 == "HB001"
                && row.H多码商品编码 == "HB001-M1"
            )
            .ToListAsync();
        Assert.Equal(2, storeMultiCodes.Count);
        Assert.All(storeMultiCodes, row =>
        {
            Assert.Equal(2.5m, row.H进货价);
            Assert.Equal(3.9m, row.H一品多码零售价);
        });
        Assert.Contains(storeMultiCodes, row => row.H分店多码商品编码 == "S01-HISTORICAL-DUPLICATE");
    }

    [Fact]
    public async Task PushToHqAsync_批量更新中途失败_回滚此前全部Hq写入()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product
            {
                RetailPrice = 9.9m,
                ProductName = "事务回滚后的新名称",
            })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        _hqDb.Ado.ExecuteCommand(
            """
            CREATE TRIGGER fail_hq_retail_batch_update
            BEFORE UPDATE OF "H分店零售价" ON "DIC_商品零售价表"
            WHEN NEW."H分店代码" = 'S02'
            BEGIN
                SELECT RAISE(ABORT, 'forced hq retail batch failure');
            END
            """
        );

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.False(second.Success);
        Assert.Equal(0, second.Data?.AffectedRowCount);
        Assert.Equal(0, second.Data?.ProductsAdded);
        Assert.Equal(0, second.Data?.ProductsUpdated);
        Assert.Equal(0, second.Data?.StoreRetailPricesCreated);
        Assert.Equal(0, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(0, second.Data?.ProductSetCodesCreated);
        Assert.Equal(0, second.Data?.ProductSetCodesUpdated);
        Assert.Equal(0, second.Data?.StoreMultiCodesCreated);
        Assert.Equal(0, second.Data?.StoreMultiCodesUpdated);
        Assert.Equal(0, second.Data?.WarehouseInventoriesCreated);
        Assert.Equal(0, second.Data?.WarehouseInventoriesUpdated);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(4.5m, product.H零售价);
        Assert.Equal("测试商品中文", product.H大写名称);
        var retailPrices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, retailPrices.Count);
        Assert.All(retailPrices, row => Assert.Equal(4.5m, row.H分店零售价));
    }

    [Fact]
    public void RollbackPushTransaction_回滚动作失败_仍归零计数并记录原始异常类型()
    {
        var logger = new Mock<ILogger<ProductHqSyncService>>();
        var service = CreateService(logger.Object);
        var result = new PushProductsToHqResult
        {
            ProductsAdded = 1,
            ProductsUpdated = 1,
            StoreRetailPricesCreated = 1,
            StoreRetailPricesUpdated = 1,
            ProductSetCodesCreated = 1,
            ProductSetCodesUpdated = 1,
            StoreMultiCodesCreated = 1,
            StoreMultiCodesUpdated = 1,
            WarehouseInventoriesCreated = 1,
            WarehouseInventoriesUpdated = 1,
        };
        var operationException = new InvalidOperationException("original write failure");
        var invocationException = Record.Exception(() =>
            service.RollbackPushTransaction(
                () => throw new InvalidOperationException("rollback failure"),
                result,
                operationException
            )
        );

        Assert.Null(invocationException);
        Assert.Equal(0, result.AffectedRowCount);
        Assert.Equal(0, result.ProductsAdded);
        Assert.Equal(0, result.ProductsUpdated);
        Assert.Equal(0, result.StoreRetailPricesCreated);
        Assert.Equal(0, result.StoreRetailPricesUpdated);
        Assert.Equal(0, result.ProductSetCodesCreated);
        Assert.Equal(0, result.ProductSetCodesUpdated);
        Assert.Equal(0, result.StoreMultiCodesCreated);
        Assert.Equal(0, result.StoreMultiCodesUpdated);
        Assert.Equal(0, result.WarehouseInventoriesCreated);
        Assert.Equal(0, result.WarehouseInventoriesUpdated);
        VerifyLogWritten(
            logger,
            LogLevel.Error,
            state =>
                state.Any(pair =>
                    pair.Key == "ErrorCode"
                    && Equals(pair.Value, "PRODUCT_HQ_PUSH_ROLLBACK_ERROR")
                )
                && state.Any(pair =>
                    pair.Key == "OriginalExceptionType"
                    && Equals(pair.Value, nameof(InvalidOperationException))
                )
        );
    }

    [Fact]
    public async Task PushToHqAsync_Hq库存重复商品编码_保持失败而不静默选择其中一条()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        var duplicateInventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        duplicateInventory.ID = 0;
        duplicateInventory.HGUID = "duplicate-inventory";
        duplicateInventory.H国内价格 = -1m;
        await _hqDb.Insertable(duplicateInventory)
            .IgnoreColumns(row => row.ID)
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.False(second.Success);
        var inventories = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, inventories.Count);
        Assert.Contains(inventories, row => row.H国内价格 == -1m);
    }

    [Fact]
    public async Task PushToHqAsync_Hq已有小货号本地历史错用Hguid时_更新既有子码且不重复创建()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetProductCode = "set-1",
                SetItemNumber = "HB001-M1",
                SetPurchasePrice = 2.7m,
                SetRetailPrice = 4.7m,
            })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                MultiCodeProductCode = "set-1",
                StoreMultiCodeProductCode = "S01set-1",
                PurchasePrice = 2.6m,
                MultiCodeRetailPrice = 4.6m,
            })
            .Where(row => row.StoreCode == "S01" && row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                MultiCodeProductCode = "set-1",
                StoreMultiCodeProductCode = "S02set-1",
                PurchasePrice = 2.6m,
                MultiCodeRetailPrice = 4.6m,
            })
            .Where(row => row.StoreCode == "S02" && row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _hqDb.Insertable(new DIC_一品多码表
        {
            HGUID = "set-1",
            H商品编码 = "HB001",
            H多码商品编号 = "HB001-M1",
            H多条形码 = "952700000002",
            H进货价 = 2.0m,
            H一品多码零售价 = 4.0m,
            H使用状态 = true,
        }).ExecuteCommandAsync();
        await _hqDb.Insertable(new[]
        {
            new DIC_分店一品多码表
            {
                HGUID = "store-multi-1",
                H分店代码 = "S01",
                H商品编码 = "HB001",
                H分店商品编码 = "S01HB001",
                H多码商品编码 = "HB001-M1",
                H分店多码商品编码 = "S01HB001-M1",
                H多条形码 = "952700000003",
                H进货价 = 1.9m,
                H一品多码零售价 = 3.9m,
                H使用状态 = true,
            },
            new DIC_分店一品多码表
            {
                HGUID = "store-multi-s02",
                H分店代码 = "S02",
                H商品编码 = "HB001",
                H分店商品编码 = "S02HB001",
                H多码商品编码 = "HB001-M1",
                H分店多码商品编码 = "S02HB001-M1",
                H多条形码 = "952700000002",
                H进货价 = 2.0m,
                H一品多码零售价 = 4.0m,
                H使用状态 = true,
            },
        }).ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(0, response.Data?.ProductSetCodesCreated);
        Assert.Equal(1, response.Data?.ProductSetCodesUpdated);
        Assert.Equal(0, response.Data?.StoreMultiCodesCreated);
        Assert.Equal(2, response.Data?.StoreMultiCodesUpdated);
        Assert.Equal(1, await _hqDb.Queryable<DIC_一品多码表>().Where(row => row.H商品编码 == "HB001").CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_分店一品多码表>().Where(row => row.H商品编码 == "HB001").CountAsync());

        var setCode = await _hqDb.Queryable<DIC_一品多码表>().SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("HB001-M1", setCode.H多码商品编号);
        Assert.Equal(2.5m, setCode.H进货价);
        Assert.Equal(4.7m, setCode.H一品多码零售价);

        var s01StoreMulti = await _hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(row => row.H商品编码 == "HB001" && row.H分店代码 == "S01");
        Assert.Equal("HB001-M1", s01StoreMulti.H多码商品编码);
        Assert.Equal(2.5m, s01StoreMulti.H进货价);
        Assert.Equal(4.6m, s01StoreMulti.H一品多码零售价);
    }

    [Fact]
    public async Task PushToHqAsync_SetType二忽略人工子码成本并同步主商品成本()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { PurchasePrice = 10m, RetailPrice = 20m })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetPurchasePrice = 99m,
                SetRetailPrice = 4m,
            })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = "set-2",
            ProductCode = "HB001",
            SetProductCode = "HB001-M2",
            SetItemNumber = "HB001-M2",
            SetBarcode = "952700000004",
            SetPurchasePrice = 88m,
            SetRetailPrice = 6m,
            SetQuantity = 1,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                PurchasePrice = 77m,
                MultiCodeRetailPrice = 4m,
            })
            .Where(row => row.StoreCode == "S01" && row.ProductCode == "HB001" && row.MultiCodeProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-2",
            StoreCode = "S01",
            ProductCode = "HB001",
            MultiCodeProductCode = "HB001-M2",
            StoreMultiCodeProductCode = "S01HB001-M2",
            MultiBarcode = "952700000005",
            PurchasePrice = 66m,
            MultiCodeRetailPrice = 6m,
            DiscountRate = 0.95m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-s02-m2",
            StoreCode = "S02",
            ProductCode = "HB001",
            MultiCodeProductCode = "HB001-M2",
            StoreMultiCodeProductCode = "S02HB001-M2",
            MultiBarcode = "952700000005",
            PurchasePrice = 55m,
            MultiCodeRetailPrice = 6m,
            DiscountRate = 1m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(response.Success, response.Message);
        var hqSetCodes = await _hqDb.Queryable<DIC_一品多码表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Contains(hqSetCodes, row =>
            row.H多码商品编号 == "HB001-M1"
            && row.H进货价 == 10m
            && row.H一品多码零售价 == 4m
        );
        Assert.Contains(hqSetCodes, row =>
            row.H多码商品编号 == "HB001-M2"
            && row.H进货价 == 10m
            && row.H一品多码零售价 == 6m
        );

        var hqStoreMultiCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H分店代码 == "S01")
            .ToListAsync();
        Assert.Contains(hqStoreMultiCodes, row =>
            row.H多码商品编码 == "HB001-M1"
            && row.H进货价 == 10m
            && row.H一品多码零售价 == 4m
        );
        Assert.Contains(hqStoreMultiCodes, row =>
            row.H多码商品编码 == "HB001-M2"
            && row.H进货价 == 10m
            && row.H一品多码零售价 == 6m
        );
    }

    [Fact]
    public async Task PushToHqAsync_Item带供应商且本地商品为空_仍写入国内供应商编码()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { LocalSupplierCode = null })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(productCode: "HB001", localSupplierCode: " SUP-ITEM "),
            },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.ProductsAdded);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("SUP01", product.CBP供应商编码);
    }

    [Fact]
    public async Task PushToHqAsync_Hq已有商品时_Item供应商不覆盖Cbp国内供应商编码()
    {
        await SeedProductGraphAsync();
        var service = CreateService();
        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { LocalSupplierCode = null })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _hqDb.Updateable<DIC_商品信息字典表>()
            .SetColumns(row => new DIC_商品信息字典表 { CBP供应商编码 = "OLD-SUP" })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(productCode: "HB001", localSupplierCode: "SUP-ITEM"),
            },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data?.ProductsUpdated);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("SUP01", product.CBP供应商编码);
    }

    [Fact]
    public async Task PushToHqAsync_全字段更新但无国内供应商编码_保留Hq既有Cbp编码()
    {
        await SeedProductGraphAsync();
        var service = CreateService();
        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { LocalSupplierCode = null })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<DomesticProduct>()
            .SetColumns(row => new DomesticProduct { SupplierCode = string.Empty })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _hqDb.Updateable<DIC_商品信息字典表>()
            .SetColumns(row => new DIC_商品信息字典表 { CBP供应商编码 = "KEEP-CBP" })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data?.ProductsUpdated);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("KEEP-CBP", product.CBP供应商编码);
    }

    [Fact]
    public async Task PushToHqAsync_旧ProductCodes入口_写入国内供应商编码()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(response.Success, response.Message);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("SUP01", product.CBP供应商编码);
    }

    [Fact]
    public async Task PushToHqAsync_本地供应商为200时_Cbp写入国内供应商编码()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { LocalSupplierCode = "200" })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<DomesticProduct>()
            .SetColumns(row => new DomesticProduct { SupplierCode = "SUP-CN" })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(response.Success, response.Message);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("SUP-CN", product.CBP供应商编码);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.All(prices, row =>
        {
            Assert.Equal("200", row.H供应商编码);
            Assert.EndsWith("200", row.H分店供应商编码);
        });
    }

    [Fact]
    public async Task PushToHqAsync_Hq已有Cbp为200时_纠正为国内供应商编码()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { LocalSupplierCode = "200" })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<DomesticProduct>()
            .SetColumns(row => new DomesticProduct { SupplierCode = "SUP-CN" })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var service = CreateService();
        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);
        await _hqDb.Updateable<DIC_商品信息字典表>()
            .SetColumns(row => new DIC_商品信息字典表 { CBP供应商编码 = "200" })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(second.Success, second.Message);
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("SUP-CN", product.CBP供应商编码);
    }

    [Fact]
    public async Task PushToHqAsync_首次发送_库存状态按本地商品启用状态初始化()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: " HB001 ",
                    domesticPrice: 11.1m,
                    importPrice: 22.2m,
                    oemPrice: 33.3m,
                    warehouseIsActive: true
                ),
            },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(0, response.Data?.FailedCount);
        Assert.Equal(1, response.Data?.WarehouseInventoriesCreated);
        Assert.Equal(0, response.Data?.WarehouseInventoriesUpdated);
        Assert.Equal(7, response.Data?.AffectedRowCount);

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(11.1m, inventory.H国内价格);
        Assert.Equal(22.2m, inventory.H进口价格);
        Assert.Equal(33.3m, inventory.H贴牌价格);
        Assert.Equal(1, inventory.H使用状态);
        Assert.Equal("HBweb", inventory.FGC_Creator);
        Assert.Equal("HBweb", inventory.FGC_LastModifier);
        Assert.False(string.IsNullOrWhiteSpace(inventory.HGUID));
    }

    [Fact]
    public async Task PushToHqAsync_重复发送_库存只更新价格和修改信息且不覆盖状态与动态字段()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    domesticPrice: 10.0m,
                    importPrice: 20.0m,
                    oemPrice: 30.0m,
                    warehouseIsActive: true
                ),
            },
        });
        Assert.True(first.Success, first.Message);

        await _hqDb.Updateable<CBP_DIC_商品库存表>()
            .SetColumns(row => new CBP_DIC_商品库存表
            {
                H库存 = 99,
                H库存金额 = 199,
                H最小订货量 = 9,
                H库存预警数 = 8,
            })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    domesticPrice: 12.0m,
                    importPrice: 23.0m,
                    oemPrice: 34.0m,
                    warehouseIsActive: false
                ),
            },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(0, second.Data?.WarehouseInventoriesCreated);
        Assert.Equal(1, second.Data?.WarehouseInventoriesUpdated);
        Assert.Equal(7, second.Data?.AffectedRowCount);
        Assert.Equal(1, await _hqDb.Queryable<CBP_DIC_商品库存表>().CountAsync());

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(12.0m, inventory.H国内价格);
        Assert.Equal(23.0m, inventory.H进口价格);
        Assert.Equal(34.0m, inventory.H贴牌价格);
        Assert.Equal(99, inventory.H库存);
        Assert.Equal(199, inventory.H库存金额);
        Assert.Equal(9, inventory.H最小订货量);
        Assert.Equal(8, inventory.H库存预警数);
        Assert.Equal(1, inventory.H使用状态);
    }

    [Fact]
    public async Task PushToHqAsync_旧ProductCodes入口_库存新增状态按本地商品启用状态初始化()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product
            {
                IsActive = false,
            })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.WarehouseInventoriesCreated);

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(0, inventory.H使用状态);
    }

    [Fact]
    public async Task PushToHqAsync_同一商品多个候选_库存仅沿用首个有效候选价格()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    localSupplierCode: "SUP01",
                    itemNumber: "HB001-ITEM",
                    domesticPrice: 15.5m,
                    importPrice: 25.5m,
                    oemPrice: 35.5m,
                    warehouseIsActive: false
                ),
                CreatePushItem(
                    productCode: "HB001",
                    domesticPrice: 99.9m,
                    importPrice: 88.8m,
                    oemPrice: 77.7m,
                    warehouseIsActive: true
                ),
            },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(1, response.Data?.WarehouseInventoriesCreated);

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(15.5m, inventory.H国内价格);
        Assert.Equal(25.5m, inventory.H进口价格);
        Assert.Equal(35.5m, inventory.H贴牌价格);
        Assert.Equal(1, inventory.H使用状态);

    }

    [Fact]
    public async Task PushToHqAsync_重复发送_商品和分店价格更新不覆盖既有HQ启用状态()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        await _hqDb.Updateable<DIC_商品信息字典表>()
            .SetColumns(row => new DIC_商品信息字典表
            {
                H使用状态 = false,
            })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();
        await _hqDb.Updateable<DIC_商品零售价表>()
            .SetColumns(row => new DIC_商品零售价表
            {
                H使用状态 = false,
            })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    localSupplierCode: "CARGO-SUP",
                    itemNumber: "CARGO-ITEM",
                    productName: "货柜商品名",
                    englishName: "Cargo English",
                    barcode: "CARGO-BARCODE",
                    imageUrl: "cargo-image.jpg",
                    domesticPrice: 8.88m,
                    importPrice: 1.23m,
                    oemPrice: 4.99m,
                    warehouseIsActive: true
                ),
            },
        });

        Assert.True(second.Success, second.Message);

        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.False(product.H使用状态);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, prices.Count);
        Assert.All(prices, row => Assert.False(row.H使用状态));
    }

    [Fact]
    public async Task PushToHqAsync_重复发送_分店价格更新不覆盖既有HQ特殊商品标记()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        await _hqDb.Updateable<DIC_商品零售价表>()
            .SetColumns(row => new DIC_商品零售价表
            {
                H是否特殊商品 = true,
                H进货价 = 11.11m,
                H分店零售价 = 22.22m,
            })
            .Where(row => row.H商品编码 == "HB001")
            .ExecuteCommandAsync();

        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product
            {
                IsSpecialProduct = false,
                PurchasePrice = 9.99m,
                RetailPrice = 19.99m,
            })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(2, second.Data?.StoreRetailPricesUpdated);

        var prices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(row => row.H商品编码 == "HB001")
            .ToListAsync();
        Assert.Equal(2, prices.Count);
        Assert.All(prices, row =>
        {
            Assert.Equal(9.99m, row.H进货价);
            Assert.Equal(19.99m, row.H分店零售价);
            Assert.True(row.H是否特殊商品);
        });
    }

    [Fact]
    public async Task PushToHqAsync_套装价格缺失时_回退主商品价格()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetPurchasePrice = null,
                SetRetailPrice = null,
            })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                PurchasePrice = null,
                MultiCodeRetailPrice = null,
            })
            .Where(row => row.ProductCode == "HB001" && row.MultiCodeProductCode == "HB001-M1")
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(response.Success, response.Message);

        var setCode = await _hqDb.Queryable<DIC_一品多码表>()
            .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1");
        Assert.Equal(2.5m, setCode.H进货价);
        Assert.Equal(4.5m, setCode.H一品多码零售价);

        var storeMultiCodes = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1")
            .ToListAsync();
        Assert.Equal(2, storeMultiCodes.Count);
        Assert.All(storeMultiCodes, row =>
        {
            Assert.Equal(2.5m, row.H进货价);
            Assert.Equal(4.5m, row.H一品多码零售价);
        });
    }

    [Fact]
    public async Task PushToHqAsync_SetType一门店投影不完整时拒绝推送并回滚Hq写入()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetType = 1,
                SetPurchasePrice = null,
            })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct { PurchasePrice = null })
            .Where(row => row.ProductCode == "HB001" && row.MultiCodeProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => row.IsDeleted == true)
            .Where(row =>
                row.StoreCode == "S02"
                && row.ProductCode == "HB001"
                && row.MultiCodeProductCode == "HB001-M1"
            )
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.False(response.Success);
        Assert.Contains("无法完整重算", response.Message);
        Assert.Contains("门店子项不完整", response.Message);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_一品多码表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_只推全局套装字段时不受无关门店投影缺失阻断()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetType = 1,
                SetPurchasePrice = 99m,
            })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            UpdateFields = new List<string> { "productSetCodes" },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(
            2.5m,
            (await _hqDb.Queryable<DIC_一品多码表>()
                .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1"))
                .H进货价
        );
        Assert.Equal(0, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_已有商品只校正并推送所选门店套装子项()
    {
        await SeedProductGraphAsync();
        var initial = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(initial.Success, initial.Message);
        await _hqDb.Updateable<DIC_分店一品多码表>()
            .SetColumns(row => row.H进货价 == 7m)
            .Where(row =>
                row.H分店代码 == "S02"
                && row.H商品编码 == "HB001"
                && row.H多码商品编码 == "HB001-M1"
            )
            .ExecuteCommandAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode { SetType = 1, SetPurchasePrice = 99m })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct { PurchasePrice = 99m })
            .Where(row =>
                row.StoreCode == "S01"
                && row.ProductCode == "HB001"
                && row.MultiCodeProductCode == "HB001-M1"
            )
            .ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            UpdateFields = new List<string> { "storeMultiCodes" },
            TargetStoreCodes = new List<string> { "S01" },
        });

        Assert.True(response.Success, response.Message);
        var rows = await _hqDb.Queryable<DIC_分店一品多码表>()
            .Where(row => row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1")
            .ToListAsync();
        Assert.Equal(2.5m, rows.Single(row => row.H分店代码 == "S01").H进货价);
        Assert.Equal(7m, rows.Single(row => row.H分店代码 == "S02").H进货价);
    }

    [Fact]
    public async Task PushToHqAsync_SetType一合法零成本可以推送()
    {
        await SeedProductGraphAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product { PurchasePrice = 0.01m })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetType = 1,
                SetPurchasePrice = 99m,
                SetRetailPrice = 1m,
            })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                PurchasePrice = 99m,
                MultiCodeRetailPrice = 1m,
            })
            .Where(row => row.ProductCode == "HB001" && row.MultiCodeProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = "set-zero-second",
            ProductCode = "HB001",
            SetProductCode = "HB001-M2",
            SetItemNumber = "HB001-M2",
            SetBarcode = "952700000004",
            SetPurchasePrice = 99m,
            SetRetailPrice = 100m,
            SetQuantity = 1,
            SetType = 1,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new[]
        {
            new StoreMultiCodeProduct
            {
                UUID = "store-multi-zero-s01-second",
                StoreCode = "S01",
                ProductCode = "HB001",
                MultiCodeProductCode = "HB001-M2",
                StoreMultiCodeProductCode = "S01HB001-M2",
                PurchasePrice = 99m,
                MultiCodeRetailPrice = 100m,
                IsActive = true,
                IsDeleted = false,
            },
            new StoreMultiCodeProduct
            {
                UUID = "store-multi-zero-s02-second",
                StoreCode = "S02",
                ProductCode = "HB001",
                MultiCodeProductCode = "HB001-M2",
                StoreMultiCodeProductCode = "S02HB001-M2",
                PurchasePrice = 99m,
                MultiCodeRetailPrice = 100m,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(
            0m,
            (await _hqDb.Queryable<DIC_一品多码表>()
                .SingleAsync(row => row.H商品编码 == "HB001" && row.H多码商品编号 == "HB001-M1"))
                .H进货价
        );
        Assert.All(
            await _hqDb.Queryable<DIC_分店一品多码表>()
                .Where(row => row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1")
                .ToListAsync(),
            row => Assert.Equal(0m, row.H进货价)
        );
    }

    [Fact]
    public async Task PushToHqAsync_重复发送_更新分店一品多码价格状态且不覆盖动态字段()
    {
        await SeedProductGraphAsync();
        var service = CreateService();

        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        var activityStart = new DateTime(2026, 6, 1, 9, 0, 0);
        var activityEnd = new DateTime(2026, 6, 30, 21, 0, 0);
        await _hqDb.Updateable<DIC_分店一品多码表>()
            .SetColumns(row => new DIC_分店一品多码表
            {
                H库存 = 88,
                H库存金额 = 188,
                H自动新价格 = 28,
                H库存预警数 = 18,
                H活动类型 = "PROMO",
                H满减活动代码 = "PROMO-001",
                H活动开始日期 = activityStart,
                H活动结束日期 = activityEnd,
                H动态销售数量 = 66,
                H动态销售额 = 166,
                H动态成本 = 86,
                H动态毛利 = 80,
                H动态毛利率 = 0.4812m,
                H动态销售占比 = 0.2188m,
            })
            .Where(row =>
                row.H分店代码 == "S01"
                && row.H商品编码 == "HB001"
                && row.H多码商品编码 == "HB001-M1"
            )
            .ExecuteCommandAsync();
        await _localDb.Updateable<ProductSetCode>()
            .SetColumns(row => new ProductSetCode
            {
                SetPurchasePrice = 2.8m,
                SetRetailPrice = 4.8m,
                IsActive = false,
            })
            .Where(row => row.ProductCode == "HB001" && row.SetProductCode == "HB001-M1")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                PurchasePrice = 2.3m,
                MultiCodeRetailPrice = 4.3m,
                IsActive = false,
            })
            .Where(row => row.StoreCode == "S01" && row.ProductCode == "HB001" && row.MultiCodeProductCode == "HB001-M1")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.True(second.Success, second.Message);

        var storeMulti = await _hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(row =>
                row.H分店代码 == "S01"
                && row.H商品编码 == "HB001"
                && row.H多码商品编码 == "HB001-M1"
            );
        Assert.Equal(2.3m, storeMulti.H进货价);
        Assert.Equal(4.3m, storeMulti.H一品多码零售价);
        Assert.False(storeMulti.H使用状态);
        Assert.Equal(88, storeMulti.H库存);
        Assert.Equal(188, storeMulti.H库存金额);
        Assert.Equal(28, storeMulti.H自动新价格);
        Assert.Equal(18, storeMulti.H库存预警数);
        Assert.Equal("PROMO", storeMulti.H活动类型);
        Assert.Equal("PROMO-001", storeMulti.H满减活动代码);
        Assert.Equal(activityStart, storeMulti.H活动开始日期);
        Assert.Equal(activityEnd, storeMulti.H活动结束日期);
        Assert.Equal(66, storeMulti.H动态销售数量);
        Assert.Equal(166, storeMulti.H动态销售额);
        Assert.Equal(86, storeMulti.H动态成本);
        Assert.Equal(80, storeMulti.H动态毛利);
        Assert.Equal(0.4812m, storeMulti.H动态毛利率);
        Assert.Equal(0.2188m, storeMulti.H动态销售占比);
    }

    [Fact]
    public async Task PushToHqAsync_部分商品不存在_返回失败统计和错误明细()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001", "HB404" },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(1, response.Data?.FailedCount);
        Assert.Equal(2, response.Data?.TotalCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), item => item.Contains("HB404"));
    }

    [Fact]
    public async Task PushToHqAsync_本地重复业务键_校正失败且不写HQ()
    {
        await SeedProductGraphAsync();
        await SeedDuplicateProductGraphRowsAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        Assert.False(response.Success);
        Assert.Contains("重复子项业务键", response.Message);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_一品多码表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_商品编码缺失时_按供应商和货号唯一命中成功()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    LocalSupplierCode = " SUP01 ",
                    ItemNumber = " HB001-ITEM ",
                    DomesticPrice = 15.5m,
                    ImportPrice = 25.5m,
                    OemPrice = 35.5m,
                },
            },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(0, response.Data?.FailedCount);
        Assert.Equal(1, response.Data?.WarehouseInventoriesCreated);

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(15.5m, inventory.H国内价格);
    }

    [Fact]
    public async Task PushToHqAsync_新商品页面状态且缺商品编码时_按供应商和货号唯一命中成功()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    LocalSupplierCode = " SUP01 ",
                    ItemNumber = " HB001-ITEM ",
                    DomesticPrice = 16.5m,
                    ImportPrice = 26.5m,
                    OemPrice = 36.5m,
                    IsNewProduct = true,
                },
            },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(0, response.Data?.FailedCount);
        Assert.Equal(1, response.Data?.WarehouseInventoriesCreated);

        var inventory = await _hqDb.Queryable<CBP_DIC_商品库存表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal(16.5m, inventory.H国内价格);
    }

    [Fact]
    public async Task PushToHqAsync_Items混合有效和无效候选_整体失败且不写Hq()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    domesticPrice: 15.5m,
                    importPrice: 25.5m,
                    oemPrice: 35.5m,
                    warehouseIsActive: true
                ),
                CreatePushItem(
                    localSupplierCode: "SUP404",
                    itemNumber: "ITEM404",
                    domesticPrice: 1.1m
                ),
            },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_ITEM_ERRORS", response.ErrorCode);
        Assert.Equal(0, response.Data?.SuccessCount);
        Assert.Equal(2, response.Data?.FailedCount);
        Assert.Equal(2, response.Data?.TotalCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), item => item.Contains("SUP404"));
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<CBP_DIC_商品库存表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_一品多码表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_业务失败时_记录结构化失败日志()
    {
        await SeedProductGraphAsync();
        var logger = new Mock<ILogger<ProductHqSyncService>>();

        var response = await CreateService(logger.Object).PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                CreatePushItem(
                    productCode: "HB001",
                    domesticPrice: 15.5m,
                    importPrice: 25.5m,
                    oemPrice: 35.5m,
                    warehouseIsActive: true
                ),
                CreatePushItem(
                    localSupplierCode: "SUP404",
                    itemNumber: "ITEM404",
                    domesticPrice: 1.1m
                ),
            },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_ITEM_ERRORS", response.ErrorCode);
        VerifyLogWritten(
            logger,
            LogLevel.Warning,
            state =>
                HasStateValue(state, "ErrorCode", "PRODUCT_HQ_PUSH_ITEM_ERRORS")
                && HasStateValue(state, "FailedCount", 2)
                && HasStateValue(state, "FirstFailureReason", "商品不存在")
                && HasNumericStateAtLeast(state, "DurationMs", 0)
        );
    }

    [Fact]
    public async Task PushToHqAsync_商品编码缺失且无匹配_返回错误明细()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    LocalSupplierCode = "SUP404",
                    ItemNumber = "ITEM404",
                    DomesticPrice = 1.1m,
                },
            },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_NO_PRODUCTS", response.ErrorCode);
        Assert.Equal(0, response.Data?.SuccessCount);
        Assert.Equal(1, response.Data?.FailedCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), item => item.Contains("SUP404"));
    }

    [Fact]
    public async Task PushToHqAsync_商品编码缺失且多匹配_返回错误明细()
    {
        await SeedProductGraphAsync();
        await SeedAmbiguousSupplierItemProductsAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    LocalSupplierCode = "SUP-MULTI",
                    ItemNumber = "ITEM-MULTI",
                    DomesticPrice = 2.2m,
                },
            },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_NO_PRODUCTS", response.ErrorCode);
        Assert.Equal(1, response.Data?.FailedCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), item => item.Contains("多条本地商品"));
    }

    [Fact]
    public async Task PushToHqAsync_候选商品编码为空_返回错误明细()
    {
        await SeedProductGraphAsync();
        await SeedMissingProductCodeFallbackAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    LocalSupplierCode = "SUP-NULL",
                    ItemNumber = "ITEM-NULL",
                    DomesticPrice = 3.3m,
                },
            },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_NO_PRODUCTS", response.ErrorCode);
        Assert.Equal(1, response.Data?.FailedCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), item => item.Contains("商品编码为空"));
    }

    [Fact]
    public async Task PushToHqAsync_新商品页面状态但本地商品存在_允许发送()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    ProductCode = "HB001",
                    DomesticPrice = 8.8m,
                    IsNewProduct = true,
                },
            },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.SuccessCount);
        Assert.Equal(0, response.Data?.FailedCount);
        Assert.Equal(1, response.Data?.TotalCount);
        Assert.Equal(1, await _hqDb.Queryable<DIC_商品信息字典表>().Where(row => row.H商品编码 == "HB001").CountAsync());
        Assert.Equal(1, await _hqDb.Queryable<CBP_DIC_商品库存表>().Where(row => row.H商品编码 == "HB001").CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_新商品页面状态且本地商品不存在_后端兜底拒绝()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    ProductCode = "HB404",
                    DomesticPrice = 8.8m,
                    IsNewProduct = true,
                },
            },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_NO_PRODUCTS", response.ErrorCode);
        Assert.Equal(0, response.Data?.SuccessCount);
        Assert.Equal(1, response.Data?.FailedCount);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), item => item.Contains("HB404"));
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<CBP_DIC_商品库存表>().CountAsync());
    }

    [Fact]
    public async Task PushToHq_控制器使用Pos商品管理权限并委托服务()
    {
        var authorize = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.PushToHq))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Permissions.PosProducts.Manage, authorize?.Policy);

        var service = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        service
            .Setup(item => item.PushToHqAsync(It.Is<PushProductsToHqRequest>(payload =>
                payload.ProductCodes.SequenceEqual(new[] { " HB001 ", "HB001" })
                && payload.Items.Count == 1
                && payload.Items[0].ProductCode == "HB001"
            )))
            .ReturnsAsync(ApiResponse<PushProductsToHqResult>.OK(new PushProductsToHqResult(), "ok"));
        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            service.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>(),
            Mock.Of<ICurrentUserService>()
        );

        var response = await controller.PushToHq(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { " HB001 ", "HB001" },
            Items = new List<PushProductsToHqItem>
            {
                new() { ProductCode = "HB001" },
            },
        });

        Assert.IsType<OkObjectResult>(response);
        service.VerifyAll();
    }

    [Fact]
    public async Task PushToHq_控制器允许只传Items()
    {
        var service = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        service
            .Setup(item => item.PushToHqAsync(It.Is<PushProductsToHqRequest>(payload =>
                payload.ProductCodes == null
                && payload.Items.Count == 1
                && payload.Items[0].LocalSupplierCode == "SUP01"
                && payload.Items[0].ItemNumber == "HB001-ITEM"
            )))
            .ReturnsAsync(ApiResponse<PushProductsToHqResult>.OK(new PushProductsToHqResult(), "ok"));
        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            service.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>(),
            Mock.Of<ICurrentUserService>()
        );

        var response = await controller.PushToHq(new PushProductsToHqRequest
        {
            ProductCodes = null!,
            Items = new List<PushProductsToHqItem>
            {
                new() { LocalSupplierCode = "SUP01", ItemNumber = "HB001-ITEM" },
            },
        });

        Assert.IsType<OkObjectResult>(response);
        service.VerifyAll();
    }

    [Fact]
    public async Task PushToHq_零成功套装成本锁冲突时返回409()
    {
        var service = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        service
            .Setup(item => item.PushToHqAsync(It.IsAny<PushProductsToHqRequest>()))
            .ReturnsAsync(
                ApiResponse<PushProductsToHqResult>.Error(
                    "套装子项成本正在被其他操作更新，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode,
                    new PushProductsToHqResult { SuccessCount = 0, FailedCount = 1 }
                )
            );
        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            service.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>(),
            Mock.Of<ICurrentUserService>()
        );

        var response = await controller.PushToHq(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });

        var conflict = Assert.IsType<ConflictObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<PushProductsToHqResult>>(conflict.Value);
        Assert.Equal(SetChildPurchasePriceMutationLock.BusyErrorCode, payload.ErrorCode);
        service.VerifyAll();
    }

    [Fact]
    public async Task StartPushToHqJob_控制器使用Pos商品管理权限并委托Job服务()
    {
        var authorize = typeof(ReactProductController)
            .GetMethod(nameof(ReactProductController.StartPushToHqJob))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(Permissions.PosProducts.Manage, authorize?.Policy);
        var jobService = new Mock<IProductPushToHqJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.StartJobAsync(
                It.Is<PushProductsToHqRequest>(payload =>
                    payload.Items.Count == 1 && payload.Items[0].ProductCode == "HB001"
                ),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new PushProductsToHqJobDto
            {
                JobId = "job-001",
                Status = ProductPushToHqJobStatusConstants.Running,
            });
        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            Mock.Of<IProductHqSyncService>(),
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>(),
            Mock.Of<ICurrentUserService>(),
            productPushToHqJobService: jobService.Object
        );

        var response = await controller.StartPushToHqJob(
            new PushProductsToHqRequest
            {
                Items = new List<PushProductsToHqItem> { new() { ProductCode = "HB001" } },
            },
            CancellationToken.None
        );

        Assert.IsType<OkObjectResult>(response);
        jobService.VerifyAll();
    }

    [Fact]
    public async Task GetPushToHqJob_控制器查询Job服务并返回快照()
    {
        var jobService = new Mock<IProductPushToHqJobService>(MockBehavior.Strict);
        jobService
            .Setup(service => service.GetJobAsync("job-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushProductsToHqJobDto
            {
                JobId = "job-001",
                Status = ProductPushToHqJobStatusConstants.Succeeded,
                Result = new PushProductsToHqResult { ProductsUpdated = 1 },
            });
        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            Mock.Of<IProductHqSyncService>(),
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<ILogger<ReactProductController>>(),
            Mock.Of<ICurrentUserService>(),
            productPushToHqJobService: jobService.Object
        );

        var response = await controller.GetPushToHqJob("job-001", CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        jobService.VerifyAll();
    }

    [Fact]
    public async Task PushToHq_控制器业务失败时_记录RequestPath和TraceId且不泄露请求明细()
    {
        var service = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        service
            .Setup(item => item.PushToHqAsync(It.IsAny<PushProductsToHqRequest>()))
            .ReturnsAsync(
                ApiResponse<PushProductsToHqResult>.Error(
                    "推送候选包含错误，未写入HQ",
                    "PRODUCT_HQ_PUSH_ITEM_ERRORS",
                    new PushProductsToHqResult
                    {
                        FailedCount = 2,
                        TotalCount = 2,
                        DurationMs = 123,
                        Errors = new List<string>
                        {
                            "商品不存在或已删除: HB001",
                            "商品不存在或已删除: HB404",
                        },
                    }
                )
            );
        var logger = new Mock<ILogger<ReactProductController>>();
        var controller = new ReactProductController(
            Mock.Of<IProductReactService>(),
            Mock.Of<IProductStoreSyncService>(),
            service.Object,
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            logger.Object,
            Mock.Of<ICurrentUserService>()
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        controller.HttpContext.TraceIdentifier = "trace-push-001";
        controller.HttpContext.Request.Path = "/api/react/v1/products/push-to-hq";

        var response = await controller.PushToHq(new PushProductsToHqRequest
        {
            Items = new List<PushProductsToHqItem>
            {
                new()
                {
                    ProductCode = "HB001",
                    LocalSupplierCode = "SUP01",
                    ItemNumber = "ITEM-001",
                },
            },
        });

        Assert.IsType<BadRequestObjectResult>(response);
        VerifyLogWritten(
            logger,
            LogLevel.Warning,
            state =>
                HasStateValue(
                    state,
                    "RequestPath",
                    "/api/react/v1/products/push-to-hq"
                )
                && HasStateValue(state, "TraceId", "trace-push-001")
                && HasStateValue(state, "ErrorCode", "PRODUCT_HQ_PUSH_ITEM_ERRORS")
                && HasStateValue(state, "FailedCount", 2)
                && HasNumericStateAtLeast(state, "DurationMs", 123)
                && !RenderState(state).Contains("HB001", StringComparison.Ordinal)
                && !RenderState(state).Contains("SUP01", StringComparison.Ordinal)
                && !RenderState(state).Contains("ITEM-001", StringComparison.Ordinal)
        );
        service.VerifyAll();
    }

    [Fact]
    public async Task PushToHqAsync_新商品指定目标分店_仍为全部分店创建必要记录()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            TargetStoreCodes = new List<string> { "S01" },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.ProductsAdded);
        Assert.Equal(2, response.Data?.StoreRetailPricesCreated);
        Assert.Equal(2, response.Data?.StoreMultiCodesCreated);
        Assert.Equal(2, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(2, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_已有商品指定目标分店_只写目标分店价格和分店多码()
    {
        await SeedProductGraphAsync();
        var service = CreateService();
        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product
            {
                PurchasePrice = 9.99m,
                RetailPrice = 19.99m,
            })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();
        await _localDb.Updateable<StoreMultiCodeProduct>()
            .SetColumns(row => new StoreMultiCodeProduct
            {
                PurchasePrice = 1.2m,
                MultiCodeRetailPrice = 2.2m,
            })
            .Where(row =>
                row.StoreCode == "S01"
                && row.ProductCode == "HB001"
                && row.MultiCodeProductCode == "HB001-M1"
            )
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            // trim + 大小写去重后只应命中 S01。
            TargetStoreCodes = new List<string> { " s01 ", "S01", "s01" },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data?.ProductsUpdated);
        Assert.Equal(1, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(1, second.Data?.StoreMultiCodesUpdated);

        var s01Price = await _hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(row => row.H分店代码 == "S01" && row.H商品编码 == "HB001");
        Assert.Equal(19.99m, s01Price.H分店零售价);
        var s02Price = await _hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(row => row.H分店代码 == "S02" && row.H商品编码 == "HB001");
        Assert.Equal(4.5m, s02Price.H分店零售价);

        var s01Multi = await _hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(row => row.H分店代码 == "S01" && row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1");
        Assert.Equal(2.2m, s01Multi.H一品多码零售价);
        var s02Multi = await _hqDb.Queryable<DIC_分店一品多码表>()
            .SingleAsync(row => row.H分店代码 == "S02" && row.H商品编码 == "HB001" && row.H多码商品编码 == "HB001-M1");
        Assert.Equal(4.0m, s02Multi.H一品多码零售价);
    }

    [Fact]
    public async Task PushToHqAsync_混合批次_新商品全部分店且已有商品仅目标分店()
    {
        await SeedProductGraphAsync();
        await SeedSecondProductGraphAsync();
        var service = CreateService();
        var first = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
        });
        Assert.True(first.Success, first.Message);

        await _localDb.Updateable<Product>()
            .SetColumns(row => new Product
            {
                PurchasePrice = 9.99m,
                RetailPrice = 19.99m,
            })
            .Where(row => row.ProductCode == "HB001")
            .ExecuteCommandAsync();

        var second = await service.PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001", "HB002" },
            TargetStoreCodes = new List<string> { "S01" },
        });

        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data?.ProductsUpdated);
        Assert.Equal(1, second.Data?.ProductsAdded);
        Assert.Equal(1, second.Data?.StoreRetailPricesUpdated);
        Assert.Equal(2, second.Data?.StoreRetailPricesCreated);
        Assert.Equal(1, second.Data?.StoreMultiCodesUpdated);
        Assert.Equal(2, second.Data?.StoreMultiCodesCreated);

        var s01HqPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(row => row.H分店代码 == "S01" && row.H商品编码 == "HB001");
        Assert.Equal(19.99m, s01HqPrice.H分店零售价);
        var s02HqPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .SingleAsync(row => row.H分店代码 == "S02" && row.H商品编码 == "HB001");
        Assert.Equal(4.5m, s02HqPrice.H分店零售价);

        Assert.Equal(2, await _hqDb.Queryable<DIC_商品零售价表>()
            .CountAsync(row => row.H商品编码 == "HB002"));
        Assert.Equal(2, await _hqDb.Queryable<DIC_分店一品多码表>()
            .CountAsync(row => row.H商品编码 == "HB002"));
    }

    [Fact]
    public async Task PushToHqAsync_显式空分店数组带全字段_整批拒绝且不写入()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            TargetStoreCodes = new List<string>(),
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_EMPTY_TARGET_STORES", response.ErrorCode);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_显式空分店数组带分店字段_整批拒绝且不写入()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            TargetStoreCodes = new List<string>(),
            UpdateFields = new List<string> { "supplierCode" },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_EMPTY_TARGET_STORES", response.ErrorCode);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_显式空分店数组仅全局字段_允许且只写全局()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            TargetStoreCodes = new List<string>(),
            UpdateFields = new List<string> { "itemNumber" },
        });

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.ProductsAdded);
        Assert.Equal(0, response.Data?.StoreRetailPricesCreated);
        Assert.Equal(0, response.Data?.StoreMultiCodesCreated);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_分店一品多码表>().CountAsync());
        var product = await _hqDb.Queryable<DIC_商品信息字典表>()
            .SingleAsync(row => row.H商品编码 == "HB001");
        Assert.Equal("HB001-ITEM", product.H货号);
    }

    [Fact]
    public async Task PushToHqAsync_未知分店编码_整批拒绝且不写入()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            TargetStoreCodes = new List<string> { "S99" },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_UNKNOWN_STORE_CODES", response.ErrorCode);
        Assert.Contains(response.Data?.Errors ?? new List<string>(), error => error.Contains("S99"));
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
    }

    [Fact]
    public async Task PushToHqAsync_混合已知未知分店编码_整批拒绝不部分写入()
    {
        await SeedProductGraphAsync();

        var response = await CreateService().PushToHqAsync(new PushProductsToHqRequest
        {
            ProductCodes = new List<string> { "HB001" },
            TargetStoreCodes = new List<string> { "S01", "S99" },
        });

        Assert.False(response.Success);
        Assert.Equal("PRODUCT_HQ_PUSH_UNKNOWN_STORE_CODES", response.ErrorCode);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品零售价表>().CountAsync());
    }

    [Fact]
    public async Task GetHqStoreOptionsAsync_返回非空大小写去重并按编码排序的分店选项()
    {
        await _hqDb.Insertable(new[]
        {
            new HqBranch { BranchCode = "S02", BranchName = "二店" },
            new HqBranch { BranchCode = "S01", BranchName = "一店" },
            new HqBranch { BranchCode = " s01 ", BranchName = "一店重复" },
            new HqBranch { BranchCode = "", BranchName = "空编码店" },
            new HqBranch { BranchCode = "S03", BranchName = "" },
        }).ExecuteCommandAsync();

        var options = await CreateService().GetHqStoreOptionsAsync();

        Assert.Equal(new[] { "S01", "S02", "S03" }, options.Select(option => option.StoreCode));
        Assert.Contains(options, option => option.StoreCode == "S01" && option.StoreName == "一店");
        Assert.Contains(options, option => option.StoreCode == "S03" && option.StoreName == "");
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

    private async Task SeedProductGraphAsync()
    {
        await _hqDb.Insertable(new[]
        {
            new HqBranch { BranchCode = "S01", BranchName = "一店" },
            new HqBranch { BranchCode = "S02", BranchName = "二店" },
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new Product
        {
            UUID = "product-1",
            ProductCode = "HB001",
            LocalSupplierCode = "SUP01",
            ItemNumber = "HB001-ITEM",
            Barcode = "952700000001",
            ProductName = "测试商品中文",
            EnglishName = "测试商品英文",
            ProductType = 2,
            PurchasePrice = 2.5m,
            RetailPrice = 4.5m,
            MiddlePackageQuantity = 6,
            ProductImage = "HB001.jpg",
            IsActive = true,
            IsAutoPricing = true,
            IsSpecialProduct = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "HB001",
            HBProductNo = "HB001-ITEM",
            ProductName = "测试国内商品中文",
            EnglishProductName = "测试国内商品英文",
            SupplierCode = "SUP01",
            ProductImage = "HB001-domestic.jpg",
            Barcode = "952700000001",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = "set-1",
            ProductCode = "HB001",
            SetProductCode = "HB001-M1",
            SetItemNumber = "HB001-M1",
            SetBarcode = "952700000002",
            SetPurchasePrice = 2.0m,
            SetRetailPrice = 4.0m,
            SetQuantity = 1,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-1",
            StoreCode = "S01",
            ProductCode = "HB001",
            MultiCodeProductCode = "HB001-M1",
            StoreMultiCodeProductCode = "S01HB001-M1",
            MultiBarcode = "952700000003",
            PurchasePrice = 1.9m,
            MultiCodeRetailPrice = 3.9m,
            DiscountRate = 0.95m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-s02-default",
            StoreCode = "S02",
            ProductCode = "HB001",
            MultiCodeProductCode = "HB001-M1",
            StoreMultiCodeProductCode = "S02HB001-M1",
            MultiBarcode = "952700000002",
            PurchasePrice = 8m,
            MultiCodeRetailPrice = 4m,
            DiscountRate = 1m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDuplicateProductGraphRowsAsync()
    {
        var latest = DateTime.UtcNow.AddDays(1);
        await _localDb.Insertable(new Product
        {
            UUID = "product-duplicate-latest",
            ProductCode = "HB001",
            LocalSupplierCode = "SUP01",
            ItemNumber = "HB001-ITEM-LATEST",
            Barcode = "952700000009",
            ProductName = "测试商品中文新版",
            EnglishName = "测试商品英文新版",
            ProductType = 2,
            PurchasePrice = 2.8m,
            RetailPrice = 5.5m,
            MiddlePackageQuantity = 8,
            ProductImage = "HB001-latest.jpg",
            IsActive = true,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsDeleted = false,
            CreatedAt = latest,
            UpdatedAt = latest,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = "set-duplicate-latest",
            ProductCode = "HB001",
            SetProductCode = "HB001-M1",
            SetItemNumber = "HB001-M1-LATEST",
            SetBarcode = "952700000010",
            SetPurchasePrice = 2.2m,
            SetRetailPrice = 4.4m,
            SetQuantity = 1,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = latest,
            UpdatedAt = latest,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-duplicate-latest",
            StoreCode = "S01",
            ProductCode = "HB001",
            MultiCodeProductCode = "HB001-M1",
            StoreMultiCodeProductCode = "S01HB001-M1-LATEST",
            MultiBarcode = "952700000011",
            PurchasePrice = 2.1m,
            MultiCodeRetailPrice = 4.2m,
            DiscountRate = 0.9m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = latest,
            UpdatedAt = latest,
        }).ExecuteCommandAsync();
    }

    private async Task SeedAmbiguousSupplierItemProductsAsync()
    {
        await _localDb.Insertable(new[]
        {
            new Product
            {
                UUID = "product-multi-1",
                ProductCode = "HBM01",
                LocalSupplierCode = "SUP-MULTI",
                ItemNumber = "ITEM-MULTI",
                ProductName = "多命中商品1",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            new Product
            {
                UUID = "product-multi-2",
                ProductCode = "HBM02",
                LocalSupplierCode = "SUP-MULTI",
                ItemNumber = "ITEM-MULTI",
                ProductName = "多命中商品2",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(1),
                UpdatedAt = DateTime.UtcNow.AddMinutes(1),
            },
        }).ExecuteCommandAsync();
    }

    private async Task SeedMissingProductCodeFallbackAsync()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "product-null-code",
            ProductCode = null,
            LocalSupplierCode = "SUP-NULL",
            ItemNumber = "ITEM-NULL",
            ProductName = "空编码商品",
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSecondProductGraphAsync()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "product-2",
            ProductCode = "HB002",
            LocalSupplierCode = "SUP02",
            ItemNumber = "HB002-ITEM",
            Barcode = "952700000021",
            ProductName = "测试商品2中文",
            EnglishName = "测试商品2英文",
            ProductType = 2,
            PurchasePrice = 3.5m,
            RetailPrice = 6.5m,
            MiddlePackageQuantity = 4,
            ProductImage = "HB002.jpg",
            IsActive = true,
            IsAutoPricing = true,
            IsSpecialProduct = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = "set-2",
            ProductCode = "HB002",
            SetProductCode = "HB002-M1",
            SetItemNumber = "HB002-M1",
            SetBarcode = "952700000022",
            SetPurchasePrice = 3.0m,
            SetRetailPrice = 5.5m,
            SetQuantity = 1,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-2",
            StoreCode = "S01",
            ProductCode = "HB002",
            MultiCodeProductCode = "HB002-M1",
            StoreMultiCodeProductCode = "S01HB002-M1",
            MultiBarcode = "952700000023",
            PurchasePrice = 2.9m,
            MultiCodeRetailPrice = 5.9m,
            DiscountRate = 0.9m,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new StoreMultiCodeProduct
        {
            UUID = "store-multi-2-s02",
            StoreCode = "S02",
            ProductCode = "HB002",
            MultiCodeProductCode = "HB002-M1",
            StoreMultiCodeProductCode = "S02HB002-M1",
            MultiBarcode = "952700000022",
            PurchasePrice = 8m,
            MultiCodeRetailPrice = 5.5m,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private static DIC_商品信息字典表 CreateHqImageRow(
        int id,
        string productCode,
        string imageUrl,
        string tencentImageUrl
    ) => new()
    {
        ID = id,
        HGUID = $"hq-image-{id}",
        H商品标签GUID = string.Empty,
        H商品分类码GUID = string.Empty,
        H供货商编码 = "200",
        H商品编码 = productCode,
        H货号 = productCode,
        H主条形码 = string.Empty,
        H商品名称 = productCode,
        H大写名称 = productCode,
        H规格 = string.Empty,
        H单位 = "个",
        H商品图片 = imageUrl,
        H腾讯云图地址 = tencentImageUrl,
        H进货单主表GUID = string.Empty,
        H进货单详情GUID = string.Empty,
        CBP商品中文名称 = productCode,
        CBP供应商编码 = string.Empty,
        CBP商品分类码GUID = string.Empty,
        FGC_Creator = "seed",
        FGC_CreateDate = DateTime.UtcNow,
        FGC_LastModifier = "seed",
        FGC_LastModifyDate = DateTime.UtcNow,
        FGC_UpdateHelp = string.Empty,
    };

    private ProductHqSyncService CreateService(ILogger<ProductHqSyncService>? logger = null)
    {
        return new ProductHqSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            Mock.Of<IMapper>(),
            logger ?? Mock.Of<ILogger<ProductHqSyncService>>(),
            new ProductAuditNoopHistoryService(),
            new ProductAuditSystemCurrentUserService()
        );
    }

    /// <summary>
    /// 用反射桥接新契约字段，先让测试能在 DTO 还没补上时红起来。
    /// </summary>
    private static PushProductsToHqItem CreatePushItem(
        string? productCode = null,
        string? localSupplierCode = null,
        string? itemNumber = null,
        string? productName = null,
        string? englishName = null,
        string? barcode = null,
        string? imageUrl = null,
        decimal? domesticPrice = null,
        decimal? importPrice = null,
        decimal? oemPrice = null,
        bool isNewProduct = false,
        bool? warehouseIsActive = null
    )
    {
        var item = new PushProductsToHqItem
        {
            ProductCode = productCode,
            LocalSupplierCode = localSupplierCode,
            ItemNumber = itemNumber,
            ProductName = productName,
            EnglishName = englishName,
            Barcode = barcode,
            ImageUrl = imageUrl,
            DomesticPrice = domesticPrice,
            ImportPrice = importPrice,
            OemPrice = oemPrice,
            IsNewProduct = isNewProduct,
        };

        if (!warehouseIsActive.HasValue)
        {
            return item;
        }

        var warehouseIsActiveProperty = typeof(PushProductsToHqItem).GetProperty("WarehouseIsActive");
        Assert.NotNull(warehouseIsActiveProperty);
        warehouseIsActiveProperty!.SetValue(item, warehouseIsActive.Value);
        return item;
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

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

    private static void VerifyLogWritten<TLogger>(
        Mock<ILogger<TLogger>> logger,
        LogLevel expectedLevel,
        Func<IReadOnlyList<KeyValuePair<string, object?>>, bool> predicate
    )
    {
        logger.Verify(
            item => item.Log(
                expectedLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => MatchLogState(state, predicate)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ),
            Times.AtLeastOnce
        );
    }

    private static bool IsHqUpdateCommand(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        return sql.AsSpan().TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchLogState(
        object state,
        Func<IReadOnlyList<KeyValuePair<string, object?>>, bool> predicate
    )
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return predicate(pairs.ToList());
        }

        return false;
    }

    private static bool HasStateValue(
        IReadOnlyList<KeyValuePair<string, object?>> state,
        string key,
        string expectedValue
    )
    {
        var pair = state.FirstOrDefault(item => item.Key == key);
        return string.Equals(pair.Value?.ToString(), expectedValue, StringComparison.Ordinal);
    }

    private static bool HasStateValue(
        IReadOnlyList<KeyValuePair<string, object?>> state,
        string key,
        int expectedValue
    )
    {
        var pair = state.FirstOrDefault(item => item.Key == key);
        return TryConvertToLong(pair.Value, out var actual) && actual == expectedValue;
    }

    private static bool HasNumericStateAtLeast(
        IReadOnlyList<KeyValuePair<string, object?>> state,
        string key,
        long minimumValue
    )
    {
        var pair = state.FirstOrDefault(item => item.Key == key);
        return TryConvertToLong(pair.Value, out var actual) && actual >= minimumValue;
    }

    private static bool TryConvertToLong(object? value, out long number)
    {
        switch (value)
        {
            case byte byteValue:
                number = byteValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static string RenderState(IReadOnlyList<KeyValuePair<string, object?>> state)
    {
        var originalFormat = state.FirstOrDefault(item => item.Key == "{OriginalFormat}").Value?.ToString();
        if (string.IsNullOrWhiteSpace(originalFormat))
        {
            return string.Join(
                ", ",
                state
                    .Where(item => item.Key != "{OriginalFormat}")
                    .Select(item => $"{item.Key}={item.Value}")
            );
        }

        var rendered = originalFormat;
        foreach (var pair in state.Where(item => item.Key != "{OriginalFormat}"))
        {
            rendered = rendered.Replace(
                "{" + pair.Key + "}",
                pair.Value?.ToString(),
                StringComparison.Ordinal
            );
        }

        return rendered;
    }
}
