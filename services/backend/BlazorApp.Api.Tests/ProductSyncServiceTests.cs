using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductSyncServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public ProductSyncServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables(
            typeof(WarehouseProduct),
            typeof(Product),
            typeof(StoreRetailPrice),
            typeof(Store),
            typeof(DomesticSetProduct),
            typeof(ProductSetCode),
            typeof(StoreMultiCodeProduct)
        );
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_只改零售价时应保留原上下架状态()
    {
        // ProductSync 的锁内身份确认以有效 Product 主档为前提。
        await SeedProductAsync("P-PRICE-ONLY", "ITEM-PRICE-ONLY");
        await SeedWarehouseProductAsync("P-PRICE-ONLY", oemPrice: 2m, isActive: false);
        var service = CreateService();

        var priceOnlyResult = await service.BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-PRICE-ONLY", OEMPrice = 3.5m },
                },
            }
        );
        var afterPriceOnly = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-PRICE-ONLY");

        Assert.True(priceOnlyResult.Success, priceOnlyResult.Message);
        Assert.Equal(3.5m, afterPriceOnly.OEMPrice);
        Assert.False(afterPriceOnly.IsActive);

        var statusResult = await service.BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-PRICE-ONLY", IsActive = true },
                },
            }
        );
        var afterStatus = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-PRICE-ONLY");

        Assert.True(statusResult.Success, statusResult.Message);
        Assert.True(afterStatus.IsActive);
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_商品编码未命中时应按货号匹配()
    {
        await SeedProductAsync("P-BY-ITEM", "ITEM-BY-ITEM");
        await SeedWarehouseProductAsync("P-BY-ITEM", oemPrice: 2m, isActive: false);
        var service = CreateService();

        var result = await service.BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-MISSING", ItemNumber = "ITEM-BY-ITEM", OEMPrice = 4m },
                },
            }
        );
        var warehouseProduct = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-BY-ITEM");

        Assert.True(result.Success, result.Message);
        Assert.Equal(4m, warehouseProduct.OEMPrice);
        Assert.False(warehouseProduct.IsActive);
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_锁内货号映射变化时应失败且不写入原商品()
    {
        await SeedProductAsync("P-ITEM-CHANGED", "ITEM-BEFORE");
        await SeedWarehouseProductAsync("P-ITEM-CHANGED", oemPrice: 2m, isActive: true);
        var changeHistory = new Mock<IWarehouseProductChangeHistoryService>();
        var captureCount = 0;
        changeHistory
            .Setup(service => service.CaptureSnapshotsAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(async () =>
            {
                if (captureCount++ == 0)
                {
                    await _db.Updateable<Product>()
                        .SetColumns(product => product.ItemNumber == "ITEM-AFTER")
                        .Where(product => product.ProductCode == "P-ITEM-CHANGED")
                        .ExecuteCommandAsync();
                }

                return new Dictionary<string, WarehouseProductChangeSnapshotDto>();
            });
        changeHistory
            .Setup(service =>
                service.RecordChangesAsync(
                    It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                    It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                    It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                    default
                )
            )
            .ReturnsAsync(0);

        var result = await CreateService(changeHistory.Object).BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new()
                    {
                        ProductCode = "P-NOT-FOUND",
                        ItemNumber = "ITEM-BEFORE",
                        OEMPrice = 4m,
                    },
                },
            }
        );
        var warehouse = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(product => product.ProductCode == "P-ITEM-CHANGED");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("映射已变化", StringComparison.Ordinal));
        Assert.Equal(2m, warehouse.OEMPrice);
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_直连商品编码的锁内货号映射变化时应失败且不追加锁()
    {
        await SeedProductAsync("P-DIRECT-ITEM-CHANGED", "ITEM-BEFORE");
        await SeedWarehouseProductAsync("P-DIRECT-ITEM-CHANGED", oemPrice: 2m, isActive: true);
        var changeHistory = new Mock<IWarehouseProductChangeHistoryService>();
        var captureCount = 0;
        changeHistory
            .Setup(service => service.CaptureSnapshotsAsync(It.IsAny<IEnumerable<string>>(), default))
            .Returns(async () =>
            {
                if (captureCount++ == 0)
                {
                    await _db.Updateable<Product>()
                        .SetColumns(product => product.ItemNumber == "ITEM-AFTER")
                        .Where(product => product.ProductCode == "P-DIRECT-ITEM-CHANGED")
                        .ExecuteCommandAsync();
                }

                return new Dictionary<string, WarehouseProductChangeSnapshotDto>();
            });
        changeHistory
            .Setup(service => service.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                default
            ))
            .ReturnsAsync(0);

        var result = await CreateService(changeHistory.Object).BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new()
                    {
                        ProductCode = "P-DIRECT-ITEM-CHANGED",
                        ItemNumber = "ITEM-BEFORE",
                        OEMPrice = 4m,
                    },
                },
            }
        );

        var warehouse = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(product => product.ProductCode == "P-DIRECT-ITEM-CHANGED");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("映射已变化", StringComparison.Ordinal));
        Assert.Equal(2m, warehouse.OEMPrice);
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_软删除主档与价格投影不得被复活或改写()
    {
        await SeedProductAsync("P-SOFT-DELETED", "ITEM-SOFT-DELETED", purchasePrice: 2m);
        await SeedWarehouseProductAsync(
            "P-SOFT-DELETED",
            oemPrice: 2m,
            isActive: true,
            importPrice: 2m
        );
        await SeedStoreRetailPriceAsync("S01", "P-SOFT-DELETED", purchasePrice: 2m, retailPrice: 2m);
        await _db.Updateable<Product>()
            .SetColumns(product => product.IsDeleted == true)
            .Where(product => product.ProductCode == "P-SOFT-DELETED")
            .ExecuteCommandAsync();
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(product => product.IsDeleted == true)
            .Where(product => product.ProductCode == "P-SOFT-DELETED")
            .ExecuteCommandAsync();
        await _db.Updateable<StoreRetailPrice>()
            .SetColumns(product => product.IsDeleted == true)
            .Where(product => product.ProductCode == "P-SOFT-DELETED")
            .ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-SOFT-DELETED", ItemNumber = "ITEM-SOFT-DELETED", ImportPrice = 4m },
                },
            }
        );

        var product = await _db.Queryable<Product>()
            .SingleAsync(row => row.ProductCode == "P-SOFT-DELETED");
        var warehouse = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-SOFT-DELETED");
        var storeRetail = await _db.Queryable<StoreRetailPrice>()
            .SingleAsync(row => row.ProductCode == "P-SOFT-DELETED");

        Assert.False(result.Success);
        Assert.True(product.IsDeleted);
        Assert.True(warehouse.IsDeleted);
        Assert.True(storeRetail.IsDeleted);
        Assert.Equal(2m, product.PurchasePrice);
        Assert.Equal(2m, warehouse.ImportPrice);
        Assert.Equal(2m, storeRetail.PurchasePrice);
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_更新套装主成本时应在同一事务内重算子项成本()
    {
        await SeedProductAsync("P-SET-UPDATE", "ITEM-SET-UPDATE", purchasePrice: 10m);
        await SeedWarehouseProductAsync("P-SET-UPDATE", oemPrice: 30m, isActive: true, importPrice: 10m);
        await SeedStoreRetailPriceAsync("S01", "P-SET-UPDATE", purchasePrice: 10m, retailPrice: 30m);
        await SeedProductSetCodeAsync("P-SET-UPDATE", "CHILD-A", retailPrice: 10m, purchasePrice: null);
        await SeedProductSetCodeAsync("P-SET-UPDATE", "CHILD-B", retailPrice: 20m, purchasePrice: null);
        await SeedStoreMultiCodeAsync("S01", "P-SET-UPDATE", "CHILD-A", retailPrice: 10m, purchasePrice: null);
        await SeedStoreMultiCodeAsync("S01", "P-SET-UPDATE", "CHILD-B", retailPrice: 20m, purchasePrice: null);

        var result = await CreateService().BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-SET-UPDATE", ImportPrice = 12m },
                },
            }
        );

        var setCosts = await _db.Queryable<ProductSetCode>()
            .Where(row => row.ProductCode == "P-SET-UPDATE")
            .OrderBy(row => row.SetProductCode)
            .Select(row => row.SetPurchasePrice)
            .ToListAsync();
        var storeCosts = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(row => row.ProductCode == "P-SET-UPDATE")
            .OrderBy(row => row.MultiCodeProductCode)
            .Select(row => row.PurchasePrice)
            .ToListAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(new decimal?[] { 4m, 8m }, setCosts);
        Assert.Equal(new decimal?[] { 4m, 8m }, storeCosts);
    }

    [Fact]
    public async Task BatchUpdateWarehouseProductsAsync_套装子项无法重算时应回滚本次成本更新()
    {
        await SeedProductAsync("P-SET-ROLLBACK", "ITEM-SET-ROLLBACK", purchasePrice: 10m);
        await SeedWarehouseProductAsync("P-SET-ROLLBACK", oemPrice: 30m, isActive: true, importPrice: 10m);
        await SeedStoreRetailPriceAsync("S01", "P-SET-ROLLBACK", purchasePrice: 10m, retailPrice: 30m);
        await SeedProductSetCodeAsync("P-SET-ROLLBACK", "CHILD-BAD", retailPrice: 0m, purchasePrice: null);

        var result = await CreateService().BatchUpdateWarehouseProductsAsync(
            new BatchProductUpdateRequest
            {
                Items = new List<ProductUpdateItem>
                {
                    new() { ProductCode = "P-SET-ROLLBACK", ImportPrice = 12m },
                },
            }
        );

        var product = await _db.Queryable<Product>()
            .SingleAsync(row => row.ProductCode == "P-SET-ROLLBACK");
        var warehouse = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(row => row.ProductCode == "P-SET-ROLLBACK");
        var storePrice = await _db.Queryable<StoreRetailPrice>()
            .SingleAsync(row => row.ProductCode == "P-SET-ROLLBACK");

        Assert.False(result.Success);
        Assert.Equal(10m, product.PurchasePrice);
        Assert.Equal(10m, warehouse.ImportPrice);
        Assert.Equal(10m, storePrice.PurchasePrice);
    }

    [Fact]
    public async Task BatchCreateProductsAsync_套装子项应按ProductCode分组并以国内套装子项键创建后重算成本()
    {
        await SeedActiveStoreAsync("S01");
        await SeedDomesticSetProductAsync("P-SET-CREATE", "CHILD-A", "SET-ITEM-A", retailPrice: 10m);
        await SeedDomesticSetProductAsync("P-SET-CREATE", "CHILD-B", "SET-ITEM-B", retailPrice: 20m);

        var result = await CreateService().BatchCreateProductsAsync(
            new BatchProductCreateRequest
            {
                Items = new List<ProductCreateItem>
                {
                    new()
                    {
                        ProductCode = "P-SET-CREATE",
                        ItemNumber = "PARENT-ITEM",
                        Barcode = "PARENT-BARCODE",
                        ImportPrice = 12m,
                        OEMPrice = 30m,
                    },
                },
            }
        );

        var setCodes = await _db.Queryable<ProductSetCode>()
            .Where(row => row.ProductCode == "P-SET-CREATE")
            .OrderBy(row => row.SetProductCode)
            .ToListAsync();
        var storeMultiCodes = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(row => row.ProductCode == "P-SET-CREATE")
            .OrderBy(row => row.MultiCodeProductCode)
            .ToListAsync();

        Assert.True(result.Success, result.Message);
        Assert.Collection(
            setCodes,
            row =>
            {
                Assert.Equal("CHILD-A", row.SetCodeId);
                Assert.Equal("CHILD-A", row.SetProductCode);
                Assert.Equal("SET-ITEM-A", row.SetItemNumber);
                Assert.Equal(10m, row.SetRetailPrice);
                Assert.Equal(4m, row.SetPurchasePrice);
            },
            row =>
            {
                Assert.Equal("CHILD-B", row.SetCodeId);
                Assert.Equal("CHILD-B", row.SetProductCode);
                Assert.Equal("SET-ITEM-B", row.SetItemNumber);
                Assert.Equal(20m, row.SetRetailPrice);
                Assert.Equal(8m, row.SetPurchasePrice);
            }
        );
        Assert.Equal(new[] { "CHILD-A", "CHILD-B" }, storeMultiCodes.Select(row => row.MultiCodeProductCode));
        Assert.All(storeMultiCodes, row => Assert.Equal(row.MultiCodeProductCode, row.StoreMultiCodeProductCode));
        Assert.Equal(new decimal?[] { 4m, 8m }, storeMultiCodes.Select(row => row.PurchasePrice));
    }

    [Fact]
    public async Task BatchCreateProductsAsync_套装子项键为空时应拒绝整组并回滚()
    {
        await SeedDomesticSetProductAsync("P-SET-INVALID", string.Empty, "SET-ITEM-A", retailPrice: 10m);

        var result = await CreateService().BatchCreateProductsAsync(
            new BatchProductCreateRequest
            {
                Items = new List<ProductCreateItem>
                {
                    new()
                    {
                        ProductCode = "P-SET-INVALID",
                        ItemNumber = "PARENT-ITEM",
                        ImportPrice = 12m,
                        OEMPrice = 30m,
                    },
                },
            }
        );

        Assert.False(result.Success);
        Assert.Equal(0, await _db.Queryable<Product>().Where(row => row.ProductCode == "P-SET-INVALID").CountAsync());
        Assert.Equal(0, await _db.Queryable<ProductSetCode>().Where(row => row.ProductCode == "P-SET-INVALID").CountAsync());
    }

    [Fact]
    public async Task BatchCreateProductsAsync_套装子项键忽略大小写重复时应拒绝整组并回滚()
    {
        await SeedDomesticSetProductAsync("P-SET-DUPLICATE", "CHILD-DUP", "SET-ITEM-A", retailPrice: 10m);
        await SeedDomesticSetProductAsync("P-SET-DUPLICATE", "child-dup", "SET-ITEM-B", retailPrice: 20m);

        var result = await CreateService().BatchCreateProductsAsync(
            new BatchProductCreateRequest
            {
                Items = new List<ProductCreateItem>
                {
                    new()
                    {
                        ProductCode = "P-SET-DUPLICATE",
                        ItemNumber = "PARENT-ITEM",
                        ImportPrice = 12m,
                        OEMPrice = 30m,
                    },
                },
            }
        );

        Assert.False(result.Success);
        Assert.Equal(0, await _db.Queryable<Product>().Where(row => row.ProductCode == "P-SET-DUPLICATE").CountAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task SeedWarehouseProductAsync(
        string productCode,
        decimal oemPrice,
        bool isActive,
        decimal? importPrice = null
    )
    {
        await _db.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                OEMPrice = oemPrice,
                ImportPrice = importPrice,
                IsActive = isActive,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedProductAsync(string productCode, string itemNumber, decimal? purchasePrice = null)
    {
        await _db.Insertable(
            new Product
            {
                UUID = $"UUID-{productCode}",
                ProductCode = productCode,
                ItemNumber = itemNumber,
                PurchasePrice = purchasePrice,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedActiveStoreAsync(string storeCode)
    {
        await _db.Insertable(new Store
        {
            StoreGUID = $"GUID-{storeCode}",
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDomesticSetProductAsync(
        string productCode,
        string setProductCode,
        string setProductNo,
        decimal retailPrice
    )
    {
        await _db.Insertable(new DomesticSetProduct
        {
            SetProductCode = setProductCode,
            ProductCode = productCode,
            SetProductNo = setProductNo,
            SetBarcode = $"BAR-{setProductNo}",
            OEMPrice = retailPrice,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductSetCodeAsync(
        string productCode,
        string childCode,
        decimal retailPrice,
        decimal? purchasePrice
    )
    {
        await _db.Insertable(new ProductSetCode
        {
            SetCodeId = $"SET-{childCode}",
            ProductCode = productCode,
            SetProductCode = childCode,
            SetItemNumber = childCode,
            SetRetailPrice = retailPrice,
            SetPurchasePrice = purchasePrice,
            SetType = 1,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreRetailPriceAsync(
        string storeCode,
        string productCode,
        decimal purchasePrice,
        decimal retailPrice
    )
    {
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = $"SRP-{storeCode}-{productCode}",
            StoreCode = storeCode,
            ProductCode = productCode,
            PurchasePrice = purchasePrice,
            StoreRetailPriceValue = retailPrice,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreMultiCodeAsync(
        string storeCode,
        string productCode,
        string childCode,
        decimal retailPrice,
        decimal? purchasePrice
    )
    {
        await _db.Insertable(new StoreMultiCodeProduct
        {
            UUID = $"SMC-{storeCode}-{childCode}",
            StoreCode = storeCode,
            ProductCode = productCode,
            MultiCodeProductCode = childCode,
            StoreMultiCodeProductCode = childCode,
            MultiCodeRetailPrice = retailPrice,
            PurchasePrice = purchasePrice,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private ProductSyncService CreateService(
        IWarehouseProductChangeHistoryService? changeHistoryService = null
    ) =>
        new(
            CreateSqlSugarContext(_db),
            NullLogger<ProductSyncService>.Instance,
            changeHistoryService ?? Mock.Of<IWarehouseProductChangeHistoryService>(),
            Mock.Of<ICurrentUserService>()
        );

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}
