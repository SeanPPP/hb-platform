using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
            typeof(WarehouseProduct),
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
    public async Task SyncFullAsync_主成本变化后同事务校正两张套装子项成本表()
    {
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-SET-FULL", now, "全量套装商品");
        await _localDb.Insertable(new Product
        {
            UUID = "local-set-full",
            ProductCode = "P-SET-FULL",
            ProductName = "旧套装商品",
            PurchasePrice = 9m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new[]
        {
            new ProductSetCode
            {
                SetCodeId = "set-full-a",
                ProductCode = "P-SET-FULL",
                SetProductCode = "CHILD-A",
                SetItemNumber = "CHILD-A",
                SetBarcode = "LOCAL-SET-A",
                SetPurchasePrice = 99m,
                SetRetailPrice = 1m,
                SetQuantity = 1,
                SetType = 1,
                IsActive = true,
                IsDeleted = false,
            },
            new ProductSetCode
            {
                SetCodeId = "set-full-b",
                ProductCode = "P-SET-FULL",
                SetProductCode = "CHILD-B",
                SetItemNumber = "CHILD-B",
                SetBarcode = "LOCAL-SET-B",
                SetPurchasePrice = 99m,
                SetRetailPrice = 3m,
                SetQuantity = 1,
                SetType = 1,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new[]
        {
            new StoreMultiCodeProduct
            {
                UUID = "store-full-a",
                StoreCode = "S01",
                ProductCode = "P-SET-FULL",
                MultiCodeProductCode = "CHILD-A",
                StoreMultiCodeProductCode = "S01-CHILD-A",
                MultiBarcode = "LOCAL-STORE-A",
                PurchasePrice = 99m,
                MultiCodeRetailPrice = 1m,
                IsActive = true,
                IsDeleted = false,
            },
            new StoreMultiCodeProduct
            {
                UUID = "store-full-b",
                StoreCode = "S01",
                ProductCode = "P-SET-FULL",
                MultiCodeProductCode = "CHILD-B",
                StoreMultiCodeProductCode = "S01-CHILD-B",
                MultiBarcode = "LOCAL-STORE-B",
                PurchasePrice = 99m,
                MultiCodeRetailPrice = 3m,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();

        var result = await CreateService().SyncFullAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(
            1.2m,
            (await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-SET-FULL"))
                .PurchasePrice
        );
        var setRows = await _localDb.Queryable<ProductSetCode>()
            .Where(x => x.ProductCode == "P-SET-FULL")
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 0.3m, 0.9m }, setRows.Select(x => x.SetPurchasePrice));
        Assert.Equal(new[] { "LOCAL-SET-A", "LOCAL-SET-B" }, setRows.Select(x => x.SetBarcode));
        var storeRows = await _localDb.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.ProductCode == "P-SET-FULL")
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 0.3m, 0.9m }, storeRows.Select(x => x.PurchasePrice));
        Assert.Equal(new[] { "LOCAL-STORE-A", "LOCAL-STORE-B" }, storeRows.Select(x => x.MultiBarcode));
    }

    [Fact]
    public async Task SyncFullAsync_Type2Only主成本变化后_同事务校正全局和相关门店且不影响无关系与软删除行()
    {
        var now = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-TYPE2-FULL", now, "全量Type2商品");
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-type2-full",
                ProductCode = "P-TYPE2-FULL",
                ProductName = "旧Type2商品",
                PurchasePrice = 9m,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(new[]
        {
            new ProductSetCode
            {
                SetCodeId = "type2-full-active",
                ProductCode = "P-TYPE2-FULL",
                SetProductCode = "TYPE2-CHILD",
                SetItemNumber = "TYPE2-CHILD",
                SetPurchasePrice = 99m,
                SetQuantity = 1,
                SetType = 2,
                IsActive = true,
                IsDeleted = false,
            },
            new ProductSetCode
            {
                SetCodeId = "type2-full-deleted",
                ProductCode = "P-TYPE2-FULL",
                SetProductCode = "TYPE2-DELETED",
                SetItemNumber = "TYPE2-DELETED",
                SetPurchasePrice = 88m,
                SetQuantity = 1,
                SetType = 2,
                IsActive = true,
                IsDeleted = true,
            },
        }).ExecuteCommandAsync();
        await _localDb.Insertable(
            new StoreRetailPrice
            {
                UUID = "type2-full-store-parent",
                StoreCode = "S01",
                ProductCode = "P-TYPE2-FULL",
                SupplierCode = "SUP",
                PurchasePrice = 1.1m,
                StoreRetailPriceValue = 2.3m,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(new[]
        {
            new StoreMultiCodeProduct
            {
                UUID = "type2-full-store-active",
                StoreCode = "S01",
                ProductCode = "P-TYPE2-FULL",
                MultiCodeProductCode = "TYPE2-CHILD",
                StoreMultiCodeProductCode = "S01-TYPE2-CHILD",
                PurchasePrice = 99m,
                IsActive = true,
                IsDeleted = false,
            },
            new StoreMultiCodeProduct
            {
                UUID = "type2-full-store-deleted",
                StoreCode = "S01",
                ProductCode = "P-TYPE2-FULL",
                MultiCodeProductCode = "TYPE2-DELETED",
                StoreMultiCodeProductCode = "S01-TYPE2-DELETED",
                PurchasePrice = 77m,
                IsActive = true,
                IsDeleted = true,
            },
            new StoreMultiCodeProduct
            {
                UUID = "type2-full-store-unrelated",
                StoreCode = "S01",
                ProductCode = "P-UNRELATED",
                MultiCodeProductCode = "UNRELATED-CHILD",
                StoreMultiCodeProductCode = "S01-UNRELATED-CHILD",
                PurchasePrice = 66m,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();

        var result = await CreateService().SyncFullAsync();

        Assert.True(result.Success, result.Message);
        var activeGlobalChild = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(x => x.SetCodeId == "type2-full-active");
        Assert.Equal(1.2m, activeGlobalChild.SetPurchasePrice);
        var activeStoreChild = await _localDb.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(x => x.UUID == "type2-full-store-active");
        Assert.Equal(1.1m, activeStoreChild.PurchasePrice);

        var deletedGlobalChild = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(x => x.SetCodeId == "type2-full-deleted");
        Assert.True(deletedGlobalChild.IsDeleted);
        Assert.Equal(88m, deletedGlobalChild.SetPurchasePrice);
        var deletedStoreChild = await _localDb.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(x => x.UUID == "type2-full-store-deleted");
        Assert.True(deletedStoreChild.IsDeleted);
        Assert.Equal(77m, deletedStoreChild.PurchasePrice);
        Assert.Equal(
            66m,
            (await _localDb.Queryable<StoreMultiCodeProduct>()
                .SingleAsync(x => x.UUID == "type2-full-store-unrelated"))
                .PurchasePrice
        );
    }

    [Fact]
    public async Task SyncFullAsync_商品写入与统一审计共享同一批次()
    {
        var now = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-AUDIT-FULL", now, "全量审计商品");

        var historyService = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        var captureCount = 0;
        historyService
            .Setup(service => service.CaptureSnapshotsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns((IEnumerable<string> codes, CancellationToken _) =>
            {
                captureCount++;
                IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> snapshots =
                    codes.ToDictionary(
                        code => code,
                        code => new WarehouseProductChangeSnapshotDto
                        {
                            ProductCode = code,
                            ProductName = captureCount == 1 ? "before" : "after",
                        },
                        StringComparer.OrdinalIgnoreCase
                    );
                return Task.FromResult(snapshots);
            });
        historyService
            .Setup(service => service.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.Is<WarehouseProductChangeHistoryContextDto>(context =>
                    context.BatchGuid.HasValue
                    && context.Source == "ProductHqSync.Full"
                    && context.ActorName == "System"
                    && context.ActorType == "System"
                ),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(1);

        var result = await CreateServiceWithHistory(historyService.Object).SyncFullAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, captureCount);
        historyService.Verify(service => service.RecordChangesAsync(
            It.Is<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(snapshots =>
                snapshots.Count == 1 && snapshots.ContainsKey("P-AUDIT-FULL")
            ),
            It.Is<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(snapshots =>
                snapshots.Count == 1 && snapshots.ContainsKey("P-AUDIT-FULL")
            ),
            It.IsAny<WarehouseProductChangeHistoryContextDto>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task SyncFullAsync_审计集合包含将被HQ停用的本地商品()
    {
        var now = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-HQ-ACTIVE", now, "HQ商品");
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-stale-full",
                ProductCode = "P-LOCAL-STALE",
                ProductName = "将被停用",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var capturedCodeSets = new List<HashSet<string>>();
        var historyService = CreateCapturingHistoryService(capturedCodeSets);

        var result = await CreateServiceWithHistory(historyService).SyncFullAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, capturedCodeSets.Count);
        Assert.All(capturedCodeSets, codes =>
        {
            Assert.Contains("P-HQ-ACTIVE", codes);
            Assert.Contains("P-LOCAL-STALE", codes);
        });
        var stale = await _localDb.Queryable<Product>()
            .SingleAsync(item => item.ProductCode == "P-LOCAL-STALE");
        Assert.True(stale.IsDeleted);
        Assert.False(stale.IsActive);
    }

    [Fact]
    public async Task SyncIncrementalAsync_审计集合包含将被HQ停用的本地商品()
    {
        var now = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-HQ-INCREMENTAL", now, "HQ增量商品");
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-stale-incremental",
                ProductCode = "P-LOCAL-STALE-INCREMENTAL",
                ProductName = "增量时将被停用",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var capturedCodeSets = new List<HashSet<string>>();
        var historyService = CreateCapturingHistoryService(capturedCodeSets);

        var result = await CreateServiceWithHistory(historyService)
            .SyncIncrementalAsync(now.AddDays(-1));

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, capturedCodeSets.Count);
        Assert.All(capturedCodeSets, codes =>
        {
            Assert.Contains("P-HQ-INCREMENTAL", codes);
            Assert.Contains("P-LOCAL-STALE-INCREMENTAL", codes);
        });
        var stale = await _localDb.Queryable<Product>()
            .SingleAsync(item => item.ProductCode == "P-LOCAL-STALE-INCREMENTAL");
        Assert.True(stale.IsDeleted);
        Assert.False(stale.IsActive);
    }

    [Fact]
    public async Task SyncFullAsync_默认使用当前请求用户历史上下文()
    {
        var now = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-ACTOR-FULL", now, "当前用户商品");

        WarehouseProductChangeHistoryContextDto? capturedContext = null;
        var historyService = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        historyService
            .Setup(service => service.CaptureSnapshotsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new Dictionary<string, WarehouseProductChangeSnapshotDto>());
        historyService
            .Setup(service => service.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                It.IsAny<CancellationToken>()
            ))
            .Callback<
                IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>,
                IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>,
                WarehouseProductChangeHistoryContextDto,
                CancellationToken
            >((_, _, context, _) => capturedContext = context)
            .ReturnsAsync(1);

        var currentUserService = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUserService.Setup(service => service.GetCurrentUserGuid()).Returns("user-guid-hq");
        currentUserService.Setup(service => service.GetCurrentUsername()).Returns("HQ操作员");

        var result = await CreateServiceWithHistory(
            historyService.Object,
            currentUserService.Object
        ).SyncFullAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(capturedContext);
        Assert.Equal("user-guid-hq", capturedContext!.ActorUserGuid);
        Assert.Equal("HQ操作员", capturedContext.ActorName);
        Assert.Equal("User", capturedContext.ActorType);
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
    public async Task SyncSelectedFromHqAsync_HQ同键多码不得覆盖本地套装及门店派生成本()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalStoreAsync("S01");
        await SeedHqProductAsync("P-SET-PROTECTED", now, "HQ商品");
        await SeedHqProductSetCodeAsync(
            "set-protected-local",
            "P-SET-PROTECTED",
            "M-PROTECTED",
            now
        );
        await SeedHqStoreMultiCodeAsync(
            "multi-protected-local",
            "S01",
            "P-SET-PROTECTED",
            "M-PROTECTED",
            now
        );
        await _localDb.Insertable(
            new Product
            {
                UUID = "product-protected-local",
                ProductCode = "P-SET-PROTECTED",
                ProductName = "本地套装",
                PurchasePrice = 1.2m,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new StoreRetailPrice
            {
                UUID = "retail-protected-local",
                StoreCode = "S01",
                ProductCode = "P-SET-PROTECTED",
                SupplierCode = "SUP",
                PurchasePrice = 1.11m,
                StoreRetailPriceValue = 8m,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = "set-protected-local",
                ProductCode = "P-SET-PROTECTED",
                SetProductCode = "M-PROTECTED",
                SetItemNumber = "M-PROTECTED",
                SetBarcode = "LOCAL-SET-BARCODE",
                SetPurchasePrice = 1.2m,
                SetRetailPrice = 4.56m,
                SetType = 1,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new StoreMultiCodeProduct
            {
                UUID = "multi-protected-local",
                StoreCode = "S01",
                ProductCode = "P-SET-PROTECTED",
                MultiCodeProductCode = "M-PROTECTED",
                StoreMultiCodeProductCode = "S01-M-PROTECTED",
                MultiBarcode = "LOCAL-STORE-BARCODE",
                PurchasePrice = 1.11m,
                MultiCodeRetailPrice = 4.44m,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(
            new List<string> { "P-SET-PROTECTED" }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesUpdated);
        Assert.Equal(0, result.Data.StoreMultiCodesUpdated);
        var protectedChild = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(x => x.SetCodeId == "set-protected-local");
        Assert.Equal(1, protectedChild.SetType);
        Assert.Equal(1.2m, protectedChild.SetPurchasePrice);
        Assert.Equal(4.56m, protectedChild.SetRetailPrice);
        Assert.Equal("LOCAL-SET-BARCODE", protectedChild.SetBarcode);
        Assert.False(protectedChild.IsDeleted);
        var protectedStoreChild = await _localDb.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(x => x.UUID == "multi-protected-local");
        Assert.Equal(1.11m, protectedStoreChild.PurchasePrice);
        Assert.Equal(4.44m, protectedStoreChild.MultiCodeRetailPrice);
        Assert.Equal("LOCAL-STORE-BARCODE", protectedStoreChild.MultiBarcode);
        Assert.False(protectedStoreChild.IsDeleted);
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_GUID与业务键命中不同Type2时拒绝并保留两行()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-SELECTED-CROSS";
        await SeedHqProductAsync(productCode, now, "HQ商品");
        await SeedHqProductSetCodeAsync(
            "hq-selected-cross-guid",
            productCode,
            "CHILD-TARGET",
            now
        );
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-selected-cross-product",
                ProductCode = productCode,
                ProductName = "本地商品",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(new[]
        {
            new ProductSetCode
            {
                SetCodeId = "hq-selected-cross-guid",
                ProductCode = productCode,
                SetProductCode = "CHILD-GUID-OWNER",
                SetItemNumber = "CHILD-GUID-OWNER",
                SetBarcode = "LOCAL-GUID-OWNER",
                SetType = 2,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            },
            new ProductSetCode
            {
                SetCodeId = "local-selected-key-owner",
                ProductCode = productCode,
                SetProductCode = "CHILD-TARGET",
                SetItemNumber = "CHILD-TARGET",
                SetBarcode = "LOCAL-KEY-OWNER",
                SetType = 2,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(
            new List<string> { productCode }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesUpdated);
        Assert.Contains(result.Data.Errors, error =>
            error.Contains("本地 ProductSetCode 身份冲突", StringComparison.Ordinal)
            && error.Contains("hq-selected-cross-guid", StringComparison.Ordinal)
            && error.Contains("P-SELECTED-CROSS/CHILD-TARGET", StringComparison.Ordinal)
            && error.Contains("本地记录=", StringComparison.Ordinal)
        );
        var guidOwner = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == "hq-selected-cross-guid");
        var keyOwner = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == "local-selected-key-owner");
        Assert.Equal("CHILD-GUID-OWNER", guidOwner.SetProductCode);
        Assert.Equal("LOCAL-GUID-OWNER", guidOwner.SetBarcode);
        Assert.False(guidOwner.IsDeleted);
        Assert.Equal("CHILD-TARGET", keyOwner.SetProductCode);
        Assert.Equal("LOCAL-KEY-OWNER", keyOwner.SetBarcode);
        Assert.False(keyOwner.IsDeleted);
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_仅GUID命中其他父商品Type2且目标键空闲时安全迁移()
    {
        var now = new DateTime(2026, 6, 4, 1, 0, 0, DateTimeKind.Utc);
        const string sourceProductCode = "P-SELECTED-GUID-SOURCE";
        const string targetProductCode = "P-SELECTED-GUID-TARGET";
        const string targetChildCode = "CHILD-TARGET";
        const string sharedGuid = "hq-selected-guid-migration";
        await SeedHqProductAsync(targetProductCode, now, "HQ目标商品");
        await SeedHqProductSetCodeAsync(
            sharedGuid,
            targetProductCode,
            targetChildCode,
            now
        );
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-selected-guid-target-product",
                ProductCode = targetProductCode,
                ProductName = "本地目标商品",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = sharedGuid,
                ProductCode = sourceProductCode,
                SetProductCode = "CHILD-SOURCE",
                SetItemNumber = "CHILD-SOURCE",
                SetBarcode = "LOCAL-SOURCE-BARCODE",
                SetType = 2,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(
            new List<string> { targetProductCode }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ProductSetCodesUpdated);
        var migrated = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == sharedGuid);
        Assert.Equal(targetProductCode, migrated.ProductCode);
        Assert.Equal(targetChildCode, migrated.SetProductCode);
        Assert.Equal(1, await _localDb.Queryable<ProductSetCode>().CountAsync());
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_跨父商品GUID命中Type1时保持保护关系不变()
    {
        var now = new DateTime(2026, 6, 4, 2, 0, 0, DateTimeKind.Utc);
        const string sourceProductCode = "P-SELECTED-TYPE1-SOURCE";
        const string targetProductCode = "P-SELECTED-TYPE1-TARGET";
        const string sharedGuid = "hq-selected-type1-protected";
        await SeedHqProductAsync(targetProductCode, now, "HQ目标商品");
        await SeedHqProductSetCodeAsync(sharedGuid, targetProductCode, "CHILD-TARGET", now);
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-selected-type1-target-product",
                ProductCode = targetProductCode,
                ProductName = "本地目标商品",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = sharedGuid,
                ProductCode = sourceProductCode,
                SetProductCode = "CHILD-PROTECTED",
                SetItemNumber = "CHILD-PROTECTED",
                SetBarcode = "LOCAL-TYPE1-BARCODE",
                SetPurchasePrice = 3.21m,
                SetRetailPrice = 6.54m,
                SetType = 1,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(
            new List<string> { targetProductCode }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesUpdated);
        var protectedRow = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == sharedGuid);
        Assert.Equal(sourceProductCode, protectedRow.ProductCode);
        Assert.Equal("CHILD-PROTECTED", protectedRow.SetProductCode);
        Assert.Equal("LOCAL-TYPE1-BARCODE", protectedRow.SetBarcode);
        Assert.Equal(1, protectedRow.SetType);
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_HQ同业务键多非空GUID时整组拒绝()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-SELECTED-SOURCE-CONFLICT";
        const string childCode = "CHILD-CONFLICT";
        await SeedHqProductAsync(productCode, now, "HQ商品");
        await SeedHqProductSetCodeAsync("hq-key-guid-a", productCode, childCode, now);
        await SeedHqProductSetCodeAsync(
            "hq-key-guid-b",
            productCode,
            childCode,
            now.AddMinutes(1)
        );
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-selected-source-conflict",
                ProductCode = productCode,
                ProductName = "本地商品",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncSelectedFromHqAsync(
            new List<string> { productCode }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesAdded);
        Assert.Equal(
            0,
            await _localDb.Queryable<ProductSetCode>()
                .Where(row => row.ProductCode == productCode)
                .CountAsync()
        );
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

    [Fact]
    public async Task SyncSelectedFromHqAsync_同一批次每个商品只写一次服务端审计()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalStoreAsync("S01");
        await SeedHqProductAsync("P-AUDIT-SELECTED", now, "选中审计商品");
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-audit-selected",
                ProductCode = "P-AUDIT-SELECTED",
                ProductName = "旧选中审计商品",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var historyService = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        var captureCount = 0;
        historyService
            .Setup(service => service.CaptureSnapshotsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns((IEnumerable<string> codes, CancellationToken _) =>
            {
                captureCount++;
                IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> snapshots =
                    codes.ToDictionary(
                        code => code,
                        code => new WarehouseProductChangeSnapshotDto
                        {
                            ProductCode = code,
                            ProductName = captureCount == 1 ? "before" : "after",
                        },
                        StringComparer.OrdinalIgnoreCase
                    );
                return Task.FromResult(snapshots);
            });
        historyService
            .Setup(service => service.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.Is<WarehouseProductChangeHistoryContextDto>(context =>
                    context.BatchGuid.HasValue
                    && context.Source == "ProductHqSync.Selected"
                    && context.Action == "Update"
                    && context.ActorName == "System"
                    && context.ActorType == "System"
                ),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(1);

        var result = await CreateServiceWithHistory(historyService.Object)
            .SyncSelectedFromHqAsync(new List<string> { "P-AUDIT-SELECTED" });

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, captureCount);
        historyService.Verify(service => service.RecordChangesAsync(
            It.Is<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(snapshots =>
                snapshots.Count == 1 && snapshots.ContainsKey("P-AUDIT-SELECTED")
            ),
            It.Is<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(snapshots =>
                snapshots.Count == 1 && snapshots.ContainsKey("P-AUDIT-SELECTED")
            ),
            It.IsAny<WarehouseProductChangeHistoryContextDto>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task SyncSelectedFromHqAsync_历史写入失败时_本地主档和镜像应一起回滚()
    {
        var now = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalStoreAsync("S01");
        await SeedHqProductAsync("P-AUDIT-SELECTED-ROLLBACK", now, "新商品名称");
        await _localDb.Insertable(
            new Product
            {
                UUID = "local-audit-selected-rollback",
                ProductCode = "P-AUDIT-SELECTED-ROLLBACK",
                ProductName = "旧商品名称",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var historyService = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        historyService
            .Setup(service => service.CaptureSnapshotsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(new Dictionary<string, WarehouseProductChangeSnapshotDto>());
        historyService
            .Setup(service => service.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new InvalidOperationException("history insert failed"));

        var result = await CreateServiceWithHistory(historyService.Object)
            .SyncSelectedFromHqAsync(new List<string> { "P-AUDIT-SELECTED-ROLLBACK" });

        Assert.False(result.Success);
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(item => item.ProductCode == "P-AUDIT-SELECTED-ROLLBACK");
        Assert.Equal("旧商品名称", product.ProductName);
        Assert.Equal(
            0,
            await _localDb.Queryable<ProductSetCode>()
                .Where(item => item.ProductCode == "P-AUDIT-SELECTED-ROLLBACK")
                .CountAsync()
        );
        historyService.VerifyAll();
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
            NullLogger<ProductHqSyncService>.Instance,
            new ProductAuditNoopHistoryService(),
            new ProductAuditSystemCurrentUserService()
        );
    }

    private ProductHqSyncService CreateServiceWithHistory(
        IWarehouseProductChangeHistoryService historyService,
        ICurrentUserService? currentUserService = null
    )
    {
        return new ProductHqSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            _mapper,
            NullLogger<ProductHqSyncService>.Instance,
            historyService,
            currentUserService ?? new ProductAuditSystemCurrentUserService()
        );
    }

    private static IWarehouseProductChangeHistoryService CreateCapturingHistoryService(
        List<HashSet<string>> capturedCodeSets
    )
    {
        var historyService = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        historyService
            .Setup(service => service.CaptureSnapshotsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns((IEnumerable<string> codes, CancellationToken _) =>
            {
                var captured = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                capturedCodeSets.Add(captured);
                return Task.FromResult<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(
                    captured.ToDictionary(
                        code => code,
                        code => new WarehouseProductChangeSnapshotDto { ProductCode = code },
                        StringComparer.OrdinalIgnoreCase
                    )
                );
            });
        historyService
            .Setup(service => service.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(1);
        return historyService.Object;
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
