using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SetChildPurchasePriceServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public SetChildPurchasePriceServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(ProductSetCode),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct)
        );
    }

    [Fact]
    public async Task PreviewAsync_覆盖错误正数并按门店成本独立分摊但不写库()
    {
        await SeedCompleteSetAsync();
        var service = new SetChildPurchasePriceService(_db);

        var result = await service.PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.True(result.IsDryRun);
        Assert.Equal(2, result.ProductSetCode.PendingUpdateCount);
        Assert.Equal(2, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Equal(0, result.ProductSetCode.UpdatedCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.UpdatedCount);
        Assert.Equal(1, result.ProductSetCode.ScannedGroupCount);
        Assert.Equal(1, result.StoreMultiCodeProduct.ScannedGroupCount);

        var globalRows = await _db.Queryable<ProductSetCode>()
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 99m, 99m }, globalRows.Select(x => x.SetPurchasePrice));

        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 50m, 50m }, storeRows.Select(x => x.PurchasePrice));

        Assert.Contains(result.Samples, x =>
            x.TableName == "ProductSetCode"
            && x.ChildProductCode == "CHILD-A"
            && x.ExpectedPurchasePrice == 4m);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.StoreCode == "S01"
            && x.ChildProductCode == "CHILD-A"
            && x.ExpectedPurchasePrice == 8m);
    }

    [Fact]
    public async Task WritebackAsync_更新两表成本并保留其他字段()
    {
        await SeedCompleteSetAsync();
        var service = new SetChildPurchasePriceService(_db);

        var result = await service.WritebackAsync(
            new SetChildPurchasePriceWritebackRequestDto(),
            "测试管理员"
        );

        Assert.False(result.IsDryRun);
        Assert.Equal(2, result.ProductSetCode.UpdatedCount);
        Assert.Equal(2, result.StoreMultiCodeProduct.UpdatedCount);

        var globalRows = await _db.Queryable<ProductSetCode>()
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 4m, 6m }, globalRows.Select(x => x.SetPurchasePrice));
        Assert.All(globalRows, x => Assert.Equal("测试管理员", x.UpdatedBy));
        Assert.Equal(new decimal?[] { 20m, 30m }, globalRows.Select(x => x.SetRetailPrice));

        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 8m, 12m }, storeRows.Select(x => x.PurchasePrice));
        Assert.All(storeRows, x => Assert.Equal("测试管理员", x.UpdatedBy));
        Assert.Equal(new decimal?[] { 20m, 30m }, storeRows.Select(x => x.MultiCodeRetailPrice));
    }

    [Fact]
    public async Task PreviewAsync_空值和0均按公式列为待更新()
    {
        await SeedCompleteSetAsync();
        var globalRows = await _db.Queryable<ProductSetCode>()
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        globalRows[0].SetPurchasePrice = null;
        globalRows[1].SetPurchasePrice = 0m;
        await _db.Updateable(globalRows)
            .UpdateColumns(x => x.SetPurchasePrice)
            .ExecuteCommandAsync();

        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        storeRows[0].PurchasePrice = null;
        storeRows[1].PurchasePrice = 0m;
        await _db.Updateable(storeRows)
            .UpdateColumns(x => x.PurchasePrice)
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(2, result.ProductSetCode.PendingUpdateCount);
        Assert.Equal(2, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Samples, x =>
            x.TableName == "ProductSetCode" && x.CurrentPurchasePrice == null);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct" && x.CurrentPurchasePrice == 0m);
    }

    [Fact]
    public async Task PreviewAsync_门店子项零售价为空或0时回退总部子项零售价()
    {
        await SeedCompleteSetAsync();
        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        storeRows[0].MultiCodeRetailPrice = null;
        storeRows[1].MultiCodeRetailPrice = 0m;
        await _db.Updateable(storeRows)
            .UpdateColumns(x => x.MultiCodeRetailPrice)
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(2, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.ChildProductCode == "CHILD-A"
            && x.ChildRetailPrice == 20m
            && x.ExpectedPurchasePrice == 8m);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.ChildProductCode == "CHILD-B"
            && x.ChildRetailPrice == 30m
            && x.ExpectedPurchasePrice == 12m);
    }

    [Fact]
    public async Task PreviewAsync_全局主成本无效但门店主成本有效时仍重算门店组()
    {
        await SeedCompleteSetAsync();
        await _db.Updateable<Product>()
            .SetColumns(x => x.PurchasePrice == 0m)
            .Where(x => x.ProductCode == "P-SET")
            .ExecuteCommandAsync();
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(x => x.ImportPrice == 0m)
            .Where(x => x.ProductCode == "P-SET")
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.ProductSetCode.SkippedGroupCount);
        Assert.Equal(2, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.ChildProductCode == "CHILD-A"
            && x.ExpectedPurchasePrice == 8m);
    }

    [Fact]
    public async Task PreviewAsync_全局子项零售价无效但门店子项零售价完整时仍重算门店组()
    {
        await SeedCompleteSetAsync();
        await _db.Updateable<ProductSetCode>()
            .SetColumns(x => x.SetRetailPrice == 0m)
            .Where(x => x.SetProductCode == "CHILD-B")
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.ProductSetCode.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(2, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.ChildProductCode == "CHILD-B"
            && x.ChildRetailPrice == 30m
            && x.ExpectedPurchasePrice == 12m);
    }

    [Fact]
    public async Task RecalculateStoresAsync_只统计门店表且不污染全局报告()
    {
        await SeedCompleteSetAsync();

        var result = await new SetChildPurchasePriceService(_db).RecalculateStoresAsync(
            new[] { "P-SET" },
            new[] { "S01" },
            "测试管理员"
        );

        Assert.Equal(0, result.ProductSetCode.ScannedGroupCount);
        Assert.Equal(0, result.ProductSetCode.EligibleGroupCount);
        Assert.Equal(0, result.ProductSetCode.SkippedGroupCount);
        Assert.DoesNotContain(result.Errors, x => x.TableName == "ProductSetCode");
        Assert.Equal(1, result.StoreMultiCodeProduct.ScannedGroupCount);
        Assert.Equal(2, result.StoreMultiCodeProduct.UpdatedCount);
    }

    [Fact]
    public async Task RecalculateStoreGroupsLockedAsync_只计算明确业务键而不扩展笛卡尔积()
    {
        await SeedCompleteSetAsync();
        await _db.Insertable(new Product
        {
            UUID = "P-SET-2-UUID",
            ProductCode = "P-SET-2",
            ProductName = "第二个测试套装",
            PurchasePrice = 30m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("SET-2-A", "CHILD-2-A", 10m, 99m, productCode: "P-SET-2"),
            BuildSetCode("SET-2-B", "CHILD-2-B", 20m, 99m, productCode: "P-SET-2"),
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "STORE-PRICE-2",
            StoreCode = "S02",
            ProductCode = "P-SET-2",
            StoreProductCode = "S02-P-SET-2",
            PurchasePrice = 60m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreRow("STORE-2-A", "CHILD-2-A", 10m, 50m, "S02", "P-SET-2"),
            BuildStoreRow("STORE-2-B", "CHILD-2-B", 20m, 50m, "S02", "P-SET-2"),
        }).ExecuteCommandAsync();

        await _db.Ado.BeginTranAsync();
        try
        {
            var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                _db,
                new[] { "P-SET", "P-SET-2" }
            );
            var result = await new SetChildPurchasePriceService(_db)
                .RecalculateStoreGroupsLockedAsync(
                    lockScope,
                    new (string? StoreCode, string? ProductCode)[]
                    {
                        (StoreCode: "S01", ProductCode: "P-SET"),
                        (StoreCode: "S02", ProductCode: "P-SET-2"),
                    },
                    "测试管理员"
                );

            Assert.Equal(2, result.StoreMultiCodeProduct.ScannedGroupCount);
            Assert.Equal(0, result.StoreMultiCodeProduct.SkippedGroupCount);
            Assert.Equal(4, result.StoreMultiCodeProduct.UpdatedCount);
            Assert.DoesNotContain(result.Errors, x =>
                (x.StoreCode == "S01" && x.ProductCode == "P-SET-2")
                || (x.StoreCode == "S02" && x.ProductCode == "P-SET")
            );
            await _db.Ado.CommitTranAsync();
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    [Fact]
    public async Task RecalculateStoresAsync_门店组不完整时抛错并回滚已计算成本()
    {
        await SeedCompleteSetAsync();
        await _db.Updateable<StoreMultiCodeProduct>()
            .SetColumns(x => x.IsActive == false)
            .Where(x => x.MultiCodeProductCode == "CHILD-B")
            .ExecuteCommandAsync();
        var originalCost = (await _db.Queryable<StoreMultiCodeProduct>()
                .SingleAsync(x => x.MultiCodeProductCode == "CHILD-A"))
            .PurchasePrice;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SetChildPurchasePriceService(_db).RecalculateStoresAsync(
                new[] { "P-SET" },
                new[] { "S01" },
                "测试管理员"
            )
        );

        Assert.Contains("门店子项不完整", exception.Message);
        Assert.Equal(
            originalCost,
            (await _db.Queryable<StoreMultiCodeProduct>()
                    .SingleAsync(x => x.MultiCodeProductCode == "CHILD-A"))
                .PurchasePrice
        );
    }

    [Fact]
    public async Task RecalculateGlobalLockedAsync_只更新全局表且不扫描门店投影()
    {
        await SeedCompleteSetAsync();
        await _db.Ado.BeginTranAsync();
        try
        {
            var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                _db,
                new[] { "P-SET" }
            );
            var originalStoreCost = (await _db.Queryable<StoreMultiCodeProduct>()
                    .SingleAsync(x => x.MultiCodeProductCode == "CHILD-A"))
                .PurchasePrice;

            var result = await new SetChildPurchasePriceService(_db)
                .RecalculateGlobalLockedAsync(lockScope, new[] { "P-SET" }, "测试管理员");

            Assert.Equal(1, result.ProductSetCode.ScannedGroupCount);
            Assert.Equal(2, result.ProductSetCode.UpdatedCount);
            Assert.Equal(0, result.StoreMultiCodeProduct.ScannedGroupCount);
            Assert.Equal(
                originalStoreCost,
                (await _db.Queryable<StoreMultiCodeProduct>()
                        .SingleAsync(x => x.MultiCodeProductCode == "CHILD-A"))
                    .PurchasePrice
            );
            await _db.Ado.CommitTranAsync();
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }
    }

    [Fact]
    public async Task PreviewAsync_门店停用子项不参与计算并导致该门店组不完整()
    {
        await SeedCompleteSetAsync();
        await _db.Updateable<StoreMultiCodeProduct>()
            .SetColumns(x => x.IsActive == false)
            .Where(x => x.MultiCodeProductCode == "CHILD-B")
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Errors, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.Reason.Contains("门店子项不完整"));
    }

    [Fact]
    public async Task WritebackAsync_门店表更新失败时回滚总部表更新()
    {
        await SeedCompleteSetAsync();
        await _db.Ado.ExecuteCommandAsync(
            """
            CREATE TRIGGER RejectStorePurchasePriceUpdate
            BEFORE UPDATE OF PurchasePrice ON StoreMultiCodeProduct
            BEGIN
                SELECT RAISE(ABORT, 'reject store purchase price update');
            END;
            """
        );
        var service = new SetChildPurchasePriceService(_db);

        await Assert.ThrowsAnyAsync<Exception>(() => service.WritebackAsync(
            new SetChildPurchasePriceWritebackRequestDto(),
            "测试管理员"
        ));

        var globalRows = await _db.Queryable<ProductSetCode>()
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 99m, 99m }, globalRows.Select(x => x.SetPurchasePrice));
    }

    [Fact]
    public async Task PreviewAsync_门店子项不完整时整组跳过且不部分更新()
    {
        await SeedCompleteSetAsync();
        await _db.Deleteable<StoreMultiCodeProduct>()
            .Where(x => x.MultiCodeProductCode == "CHILD-B")
            .ExecuteCommandAsync();
        var service = new SetChildPurchasePriceService(_db);

        var result = await service.PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Errors, x =>
            x.Reason.Contains("门店子项不完整")
            && x.Reason.Contains("缺少子项: CHILD-B"));
    }

    [Fact]
    public async Task WritebackAsync_门店额外子项时管理员模式报告并整组跳过()
    {
        await SeedCompleteSetAsync();
        await _db.Insertable(BuildStoreRow("STORE-EXTRA", "CHILD-EXTRA", 40m, 77m))
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db).WritebackAsync(
            new SetChildPurchasePriceWritebackRequestDto(),
            "测试管理员"
        );

        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.UpdatedCount);
        Assert.Contains(result.Errors, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.Reason.Contains("额外子项: CHILD-EXTRA"));
        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-SET")
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 50m, 50m, 77m }, storeRows.Select(x => x.PurchasePrice));
    }

    [Fact]
    public async Task PreviewAsync_门店结构差异明细应限制子项数量()
    {
        await SeedCompleteSetAsync();
        var extraRows = Enumerable.Range(1, 25)
            .Select(index => BuildStoreRow(
                $"STORE-EXTRA-{index:D2}",
                $"CHILD-EXTRA-{index:D2}",
                40m,
                77m
            ))
            .ToList();
        await _db.Insertable(extraRows).ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        var error = Assert.Single(result.Errors, item =>
            item.TableName == "StoreMultiCodeProduct"
            && item.StoreCode == "S01"
            && item.ProductCode == "P-SET"
        );
        Assert.Contains("额外子项: CHILD-EXTRA-01", error.Reason);
        Assert.Contains("另有 5 项未展开", error.Reason);
        Assert.DoesNotContain("CHILD-EXTRA-21", error.Reason);
    }

    [Fact]
    public async Task PreviewAsync_门店空子项时报告并整组跳过()
    {
        await SeedCompleteSetAsync();
        var emptyChild = BuildStoreRow("STORE-EMPTY", "PLACEHOLDER", 40m, 77m);
        emptyChild.MultiCodeProductCode = "   ";
        await _db.Insertable(emptyChild).ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Errors, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.Reason.Contains("门店子项业务键为空"));
    }

    [Fact]
    public async Task PreviewAsync_门店子项规范化后重复时报告并整组跳过()
    {
        await SeedCompleteSetAsync();
        await _db.Insertable(BuildStoreRow("STORE-DUP-NORMALIZED", " child-a ", 40m, 77m))
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Errors, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.Reason.Contains("规范化后重复子项业务键: CHILD-A"));
    }

    [Fact]
    public async Task PreviewAsync_总部无有效关系但门店仍活跃时报告孤儿父项()
    {
        await _db.Insertable(new Product
        {
            UUID = "P-ORPHAN-UUID",
            ProductCode = "P-ORPHAN",
            ProductName = "孤儿套装",
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode(
            "SET-ORPHAN-INACTIVE",
            "CHILD-ORPHAN",
            20m,
            10m,
            productCode: "P-ORPHAN",
            isActive: false
        )).ExecuteCommandAsync();
        await _db.Insertable(BuildStoreRow(
            "STORE-ORPHAN",
            "CHILD-ORPHAN",
            20m,
            10m,
            productCode: "P-ORPHAN"
        )).ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db).PreviewAsync(
            new SetChildPurchasePriceWritebackRequestDto
            {
                ProductCodes = new List<string> { "P-ORPHAN" },
            }
        );

        Assert.Equal(1, result.StoreMultiCodeProduct.ScannedGroupCount);
        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Contains(result.Errors, x =>
            x.StoreCode == "S01"
            && x.ProductCode == "P-ORPHAN"
            && x.Reason.Contains("总部无有效关系但门店仍有活跃子项"));
    }

    [Fact]
    public async Task PreviewAsync_总部仅有停用Type2且门店仍活跃时不误报套装孤儿()
    {
        await _db.Insertable(new Product
        {
            UUID = "P-TYPE2-INACTIVE-UUID",
            ProductCode = "P-TYPE2-INACTIVE",
            ProductName = "停用普通多码",
            ProductType = 2,
            PurchasePrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode(
            "TYPE2-INACTIVE-ACTIVE-STORE",
            "CHILD-TYPE2-INACTIVE",
            20m,
            10m,
            productCode: "P-TYPE2-INACTIVE",
            isActive: false,
            setType: 2
        )).ExecuteCommandAsync();
        await _db.Insertable(BuildStoreRow(
            "STORE-TYPE2-INACTIVE-ACTIVE",
            "CHILD-TYPE2-INACTIVE",
            20m,
            77m,
            productCode: "P-TYPE2-INACTIVE"
        )).ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db).PreviewAsync(
            new SetChildPurchasePriceWritebackRequestDto
            {
                ProductCodes = new List<string> { "P-TYPE2-INACTIVE" },
            }
        );

        Assert.Equal(1, result.StoreMultiCodeProduct.ScannedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.DoesNotContain(result.Errors, x =>
            x.Reason.Contains("总部无有效关系但门店仍有活跃子项"));
    }

    [Fact]
    public async Task RecalculateStoresAsync_门店额外子项时抛错并回滚整组成本()
    {
        await SeedCompleteSetAsync();
        await _db.Insertable(BuildStoreRow("STORE-EXTRA", "CHILD-EXTRA", 40m, 77m))
            .ExecuteCommandAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SetChildPurchasePriceService(_db).RecalculateStoresAsync(
                new[] { "P-SET" },
                new[] { "S01" },
                "业务操作"
            )
        );

        Assert.Contains("额外子项: CHILD-EXTRA", exception.Message);
        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S01" && x.ProductCode == "P-SET")
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 50m, 50m, 77m }, storeRows.Select(x => x.PurchasePrice));
    }

    [Fact]
    public async Task PreviewAsync_门店整组子项缺失时仍报告不完整()
    {
        await SeedCompleteSetAsync();
        await _db.Deleteable<StoreMultiCodeProduct>().ExecuteCommandAsync();
        var service = new SetChildPurchasePriceService(_db);

        var result = await service.PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.StoreMultiCodeProduct.ScannedGroupCount);
        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Errors, x =>
            x.StoreCode == "S01"
            && x.ProductCode == "P-SET"
            && x.Reason.Contains("期望 2 条，实际 0 条"));
    }

    [Fact]
    public async Task PreviewAsync_子项零售价无效或业务键重复时整组跳过()
    {
        await SeedCompleteSetAsync();
        await _db.Updateable<ProductSetCode>()
            .SetColumns(x => x.SetRetailPrice == 0m)
            .Where(x => x.SetProductCode == "CHILD-B")
            .ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("SET-DUP", "CHILD-A", 10m, 7m)).ExecuteCommandAsync();
        var service = new SetChildPurchasePriceService(_db);

        var result = await service.PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.ProductSetCode.SkippedGroupCount);
        Assert.Equal(0, result.ProductSetCode.PendingUpdateCount);
        Assert.Contains(result.Errors, x => x.Reason.Contains("重复子项业务键"));
    }

    [Fact]
    public async Task PreviewAsync_只处理有效未删除套装子项并使用仓库成本回退()
    {
        await _db.Insertable(new Product
        {
            UUID = "P-FALLBACK-UUID",
            ProductCode = "P-FALLBACK",
            ProductName = "仓库成本回退套装",
            PurchasePrice = 0m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = "P-FALLBACK",
            ImportPrice = 12m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("SET-A", "A", 10m, 1m, productCode: "P-FALLBACK"),
            BuildSetCode("SET-B", "B", 20m, 1m, productCode: "P-FALLBACK"),
            BuildSetCode("SET-INACTIVE", "C", 100m, 77m, productCode: "P-FALLBACK", isActive: false),
            BuildSetCode("SET-MULTI", "D", 100m, 88m, productCode: "P-FALLBACK", setType: 2),
        }).ExecuteCommandAsync();
        var service = new SetChildPurchasePriceService(_db);

        var result = await service.PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(3, result.ProductSetCode.PendingUpdateCount);
        Assert.Contains(result.Samples, x => x.ChildProductCode == "A" && x.ExpectedPurchasePrice == 4m);
        Assert.Contains(result.Samples, x => x.ChildProductCode == "B" && x.ExpectedPurchasePrice == 8m);
        Assert.Contains(result.Samples, x => x.ChildProductCode == "D" && x.ExpectedPurchasePrice == 12m);
        Assert.DoesNotContain(result.Samples, x => x.ChildProductCode == "C");
    }

    [Fact]
    public async Task WritebackAsync_Type2按主成本写回且正确值不修改审计()
    {
        await SeedCompleteSetAsync();
        var unchangedAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var unchangedGlobal = BuildSetCode("TYPE2-UNCHANGED", "CHILD-TYPE2-UNCHANGED", 0m, 10m, setType: 2);
        unchangedGlobal.UpdatedAt = unchangedAt;
        unchangedGlobal.UpdatedBy = "原管理员";
        var unchangedStore = BuildStoreRow(
            "STORE-TYPE2-UNCHANGED",
            "CHILD-TYPE2-UNCHANGED",
            0m,
            20m
        );
        unchangedStore.UpdatedAt = unchangedAt;
        unchangedStore.UpdatedBy = "原管理员";
        await _db.Insertable(new[]
        {
            BuildSetCode("TYPE2-WRONG", "CHILD-TYPE2-WRONG", 0m, 99m, setType: 2),
            unchangedGlobal,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreRow("STORE-TYPE2-WRONG", "CHILD-TYPE2-WRONG", 0m, 99m),
            unchangedStore,
        }).ExecuteCommandAsync();

        await new SetChildPurchasePriceService(_db).WritebackAsync(
            new SetChildPurchasePriceWritebackRequestDto(),
            "测试管理员"
        );

        var globalRows = await _db.Queryable<ProductSetCode>()
            .Where(x => x.SetType == 2)
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 10m, 10m }, globalRows.Select(x => x.SetPurchasePrice));
        Assert.Equal("原管理员", globalRows[0].UpdatedBy);
        Assert.Equal(unchangedAt, globalRows[0].UpdatedAt);
        Assert.Equal("测试管理员", globalRows[1].UpdatedBy);

        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.MultiCodeProductCode!.Contains("TYPE2"))
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 20m, 20m }, storeRows.Select(x => x.PurchasePrice));
        Assert.Equal("原管理员", storeRows[0].UpdatedBy);
        Assert.Equal(unchangedAt, storeRows[0].UpdatedAt);
        Assert.Equal("测试管理员", storeRows[1].UpdatedBy);
    }

    [Fact]
    public async Task WritebackAsync_停用或软删除Type2及其门店投影保持不变()
    {
        await SeedCompleteSetAsync();
        var originalUpdatedAt = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var inactive = BuildSetCode(
            "TYPE2-INACTIVE",
            "CHILD-TYPE2-INACTIVE",
            0m,
            77m,
            setType: 2,
            isActive: false
        );
        inactive.UpdatedAt = originalUpdatedAt;
        inactive.UpdatedBy = "原管理员";
        var deleted = BuildSetCode(
            "TYPE2-DELETED",
            "CHILD-TYPE2-DELETED",
            0m,
            88m,
            setType: 2
        );
        deleted.IsDeleted = true;
        deleted.UpdatedAt = originalUpdatedAt;
        deleted.UpdatedBy = "原管理员";
        var inactiveStore = BuildStoreRow(
            "STORE-TYPE2-INACTIVE",
            "CHILD-TYPE2-INACTIVE",
            0m,
            77m
        );
        inactiveStore.IsActive = false;
        inactiveStore.UpdatedAt = originalUpdatedAt;
        inactiveStore.UpdatedBy = "原管理员";
        var deletedStore = BuildStoreRow(
            "STORE-TYPE2-DELETED",
            "CHILD-TYPE2-DELETED",
            0m,
            88m
        );
        deletedStore.IsDeleted = true;
        deletedStore.UpdatedAt = originalUpdatedAt;
        deletedStore.UpdatedBy = "原管理员";
        await _db.Insertable(new[] { inactive, deleted }).ExecuteCommandAsync();
        await _db.Insertable(new[] { inactiveStore, deletedStore }).ExecuteCommandAsync();

        await new SetChildPurchasePriceService(_db).WritebackAsync(
            new SetChildPurchasePriceWritebackRequestDto(),
            "测试管理员"
        );

        var globalRows = await _db.Queryable<ProductSetCode>()
            .Where(x => x.SetCodeId == "TYPE2-INACTIVE" || x.SetCodeId == "TYPE2-DELETED")
            .OrderBy(x => x.SetCodeId)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 88m, 77m }, globalRows.Select(x => x.SetPurchasePrice));
        Assert.All(globalRows, row =>
        {
            Assert.Equal(originalUpdatedAt, row.UpdatedAt);
            Assert.Equal("原管理员", row.UpdatedBy);
        });

        var storeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.UUID == "STORE-TYPE2-INACTIVE" || x.UUID == "STORE-TYPE2-DELETED")
            .OrderBy(x => x.UUID)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 88m, 77m }, storeRows.Select(x => x.PurchasePrice));
        Assert.All(storeRows, row =>
        {
            Assert.Equal(originalUpdatedAt, row.UpdatedAt);
            Assert.Equal("原管理员", row.UpdatedBy);
        });
    }

    [Fact]
    public async Task PreviewAsync_Type2全局和门店均按规定顺序回退主成本()
    {
        await _db.Insertable(new[]
        {
            new Product
            {
                UUID = "P-TYPE2-PRODUCT-UUID",
                ProductCode = "P-TYPE2-PRODUCT",
                PurchasePrice = 10m,
                IsActive = true,
                IsDeleted = false,
            },
            new Product
            {
                UUID = "P-TYPE2-WAREHOUSE-UUID",
                ProductCode = "P-TYPE2-WAREHOUSE",
                PurchasePrice = 0m,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new WarehouseProduct
            {
                ProductCode = "P-TYPE2-PRODUCT",
                ImportPrice = 9m,
                IsActive = true,
                IsDeleted = false,
            },
            new WarehouseProduct
            {
                ProductCode = "P-TYPE2-WAREHOUSE",
                ImportPrice = 9m,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("TYPE2-PRODUCT", "CHILD-TYPE2-PRODUCT", 0m, 99m, "P-TYPE2-PRODUCT", setType: 2),
            BuildSetCode("TYPE2-WAREHOUSE", "CHILD-TYPE2-WAREHOUSE", 0m, 99m, "P-TYPE2-WAREHOUSE", setType: 2),
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new StoreRetailPrice
            {
                UUID = "STORE-PRICE-TYPE2-PRODUCT",
                StoreCode = "S01",
                ProductCode = "P-TYPE2-PRODUCT",
                PurchasePrice = 0m,
                IsActive = true,
                IsDeleted = false,
            },
            new StoreRetailPrice
            {
                UUID = "STORE-PRICE-TYPE2-WAREHOUSE",
                StoreCode = "S01",
                ProductCode = "P-TYPE2-WAREHOUSE",
                PurchasePrice = 0m,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreRow("STORE-TYPE2-PRODUCT", "CHILD-TYPE2-PRODUCT", 0m, 99m, "S01", "P-TYPE2-PRODUCT"),
            BuildStoreRow("STORE-TYPE2-WAREHOUSE", "CHILD-TYPE2-WAREHOUSE", 0m, 99m, "S01", "P-TYPE2-WAREHOUSE"),
        }).ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Contains(result.Samples, x =>
            x.TableName == "ProductSetCode"
            && x.ChildProductCode == "CHILD-TYPE2-PRODUCT"
            && x.ExpectedPurchasePrice == 10m);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.ChildProductCode == "CHILD-TYPE2-PRODUCT"
            && x.ExpectedPurchasePrice == 10m);
        Assert.Contains(result.Samples, x =>
            x.TableName == "ProductSetCode"
            && x.ChildProductCode == "CHILD-TYPE2-WAREHOUSE"
            && x.ExpectedPurchasePrice == 9m);
        Assert.Contains(result.Samples, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.ChildProductCode == "CHILD-TYPE2-WAREHOUSE"
            && x.ExpectedPurchasePrice == 9m);
    }

    [Fact]
    public async Task PreviewAsync_门店必须完整匹配所有有效Type1和Type2关系()
    {
        await SeedCompleteSetAsync();
        await _db.Insertable(BuildSetCode("TYPE2-MISSING-STORE", "CHILD-TYPE2", 0m, 99m, setType: 2))
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(3, result.ProductSetCode.PendingUpdateCount);
        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Errors, x =>
            x.TableName == "StoreMultiCodeProduct"
            && x.Reason.Contains("期望 3 条，实际 2 条"));
    }

    [Fact]
    public async Task PreviewAsync_同主商品子项的活跃Type1和Type2冲突时整组跳过()
    {
        await SeedCompleteSetAsync();
        await _db.Insertable(BuildSetCode("TYPE2-CONFLICT", "CHILD-A", 0m, 99m, setType: 2))
            .ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db)
            .PreviewAsync(new SetChildPurchasePriceWritebackRequestDto());

        Assert.Equal(1, result.ProductSetCode.SkippedGroupCount);
        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.ProductSetCode.PendingUpdateCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.PendingUpdateCount);
        Assert.Contains(result.Errors, x =>
            x.TableName == "ProductSetCode"
            && x.Reason.Contains("Type1/Type2冲突"));
    }

    [Fact]
    public async Task WritebackAsync_Type2缺主成本时管理员模式整组跳过()
    {
        await _db.Insertable(new Product
        {
            UUID = "P-TYPE2-NO-COST-UUID",
            ProductCode = "P-TYPE2-NO-COST",
            PurchasePrice = 0m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = "P-TYPE2-NO-COST",
            ImportPrice = 0m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildSetCode("TYPE2-NO-COST", "CHILD-TYPE2-NO-COST", 0m, 99m, "P-TYPE2-NO-COST", setType: 2))
            .ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "STORE-PRICE-TYPE2-NO-COST",
            StoreCode = "S01",
            ProductCode = "P-TYPE2-NO-COST",
            PurchasePrice = 0m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(BuildStoreRow(
            "STORE-TYPE2-NO-COST",
            "CHILD-TYPE2-NO-COST",
            0m,
            99m,
            "S01",
            "P-TYPE2-NO-COST"
        )).ExecuteCommandAsync();

        var result = await new SetChildPurchasePriceService(_db).WritebackAsync(
            new SetChildPurchasePriceWritebackRequestDto(),
            "测试管理员"
        );

        Assert.Equal(1, result.ProductSetCode.SkippedGroupCount);
        Assert.Equal(1, result.StoreMultiCodeProduct.SkippedGroupCount);
        Assert.Equal(0, result.ProductSetCode.UpdatedCount);
        Assert.Equal(0, result.StoreMultiCodeProduct.UpdatedCount);
        Assert.Contains(result.Errors, x => x.Reason.Contains("套装总进货价为空或0"));
    }

    [Fact]
    public async Task RepairMissingStoreRelationsLockedAsync_指定精确组时只补齐目标门店()
    {
        await SeedMissingStoreRelationsForTwoStoresAsync();

        await _db.Ado.BeginTranAsync();
        SetChildStoreRelationRepairResult result;
        try
        {
            var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                _db,
                new[] { "P-SET" }
            );
            result = await new SetChildPurchasePriceService(_db)
                .RepairMissingStoreRelationsLockedAsync(
                    lockScope,
                    new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["P-SET"] = 10m,
                    },
                    "测试管理员",
                    new (string? StoreCode, string? ProductCode)[]
                    {
                        (StoreCode: "S01", ProductCode: "P-SET"),
                    }
                );
            await _db.Ado.CommitTranAsync();
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }

        var targetRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(row => row.StoreCode == "S01" && row.ProductCode == "P-SET")
            .ToListAsync();
        var otherStoreRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(row => row.StoreCode == "S02" && row.ProductCode == "P-SET")
            .ToListAsync();

        Assert.Empty(result.Failures);
        Assert.Equal(1, result.AutoRepairedStoreGroupCount);
        Assert.Equal(2, result.AutoRepairedRelationCount);
        Assert.Equal(2, targetRows.Count);
        Assert.Empty(otherStoreRows);
    }

    [Fact]
    public async Task RepairMissingStoreRelationsLockedAsync_未指定精确组时保留全部候选门店行为()
    {
        await SeedMissingStoreRelationsForTwoStoresAsync();

        await _db.Ado.BeginTranAsync();
        SetChildStoreRelationRepairResult result;
        try
        {
            var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                _db,
                new[] { "P-SET" }
            );
            result = await new SetChildPurchasePriceService(_db)
                .RepairMissingStoreRelationsLockedAsync(
                    lockScope,
                    new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["P-SET"] = 10m,
                    },
                    "测试管理员"
                );
            await _db.Ado.CommitTranAsync();
        }
        catch
        {
            await _db.Ado.RollbackTranAsync();
            throw;
        }

        var rows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(row => row.ProductCode == "P-SET")
            .ToListAsync();
        var orderedStoreCodes = rows
            .OrderBy(row => row.StoreCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.MultiCodeProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(row => row.StoreCode)
            .ToArray();

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.AutoRepairedStoreGroupCount);
        Assert.Equal(4, result.AutoRepairedRelationCount);
        Assert.Equal(new[] { "S01", "S01", "S02", "S02" }, orderedStoreCodes);
    }

    [Fact]
    public void ResolveFailedRepairProductCodes_超过错误明细上限时保守拒绝全部计划商品()
    {
        var validation = new SetChildPurchasePriceWritebackResultDto();
        validation.StoreMultiCodeProduct.SkippedGroupCount = 101;
        validation.Errors = Enumerable.Range(0, 100)
            .Select(index => new SetChildPurchasePriceWritebackError
            {
                TableName = "StoreMultiCodeProduct",
                StoreCode = $"S{index:000}",
                ProductCode = "P-FIRST",
                Reason = "门店子项结构异常",
            })
            .ToList();

        // 第 101 条错误属于 P-AFTER-LIMIT，但展示明细已达到上限，无法从 Errors 归因。
        var failedProductCodes = SetChildPurchasePriceService.ResolveFailedRepairProductCodes(
            validation,
            new[] { "P-FIRST", "P-AFTER-LIMIT" }
        );

        Assert.Equal(2, failedProductCodes.Count);
        Assert.Contains("P-FIRST", failedProductCodes);
        Assert.Contains("P-AFTER-LIMIT", failedProductCodes);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private async Task SeedCompleteSetAsync()
    {
        await _db.Insertable(new Product
        {
            UUID = "P-SET-UUID",
            ProductCode = "P-SET",
            ProductName = "测试套装",
            PurchasePrice = 10m,
            RetailPrice = 50m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = "P-SET",
            ImportPrice = 9m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildSetCode("SET-A", "CHILD-A", 20m, 99m),
            BuildSetCode("SET-B", "CHILD-B", 30m, 99m),
        }).ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "STORE-PRICE",
            StoreCode = "S01",
            ProductCode = "P-SET",
            StoreProductCode = "S01-P-SET",
            PurchasePrice = 20m,
            StoreRetailPriceValue = 80m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreRow("STORE-A", "CHILD-A", 20m, 50m),
            BuildStoreRow("STORE-B", "CHILD-B", 30m, 50m),
        }).ExecuteCommandAsync();
    }

    private async Task SeedMissingStoreRelationsForTwoStoresAsync()
    {
        await SeedCompleteSetAsync();
        await _db.Deleteable<StoreMultiCodeProduct>().ExecuteCommandAsync();
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "STORE-PRICE-S02",
            StoreCode = "S02",
            ProductCode = "P-SET",
            StoreProductCode = "S02-P-SET",
            PurchasePrice = 30m,
            StoreRetailPriceValue = 90m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private static ProductSetCode BuildSetCode(
        string id,
        string childCode,
        decimal retailPrice,
        decimal purchasePrice,
        string productCode = "P-SET",
        bool isActive = true,
        int setType = 1
    ) => new()
    {
        SetCodeId = id,
        ProductCode = productCode,
        SetProductCode = childCode,
        SetItemNumber = $"ITEM-{childCode}",
        SetBarcode = $"BAR-{childCode}",
        SetPurchasePrice = purchasePrice,
        SetRetailPrice = retailPrice,
        SetQuantity = 99,
        SetType = setType,
        IsActive = isActive,
        IsDeleted = false,
        CreatedAt = DateTime.UtcNow,
    };

    private static StoreMultiCodeProduct BuildStoreRow(
        string uuid,
        string childCode,
        decimal retailPrice,
        decimal purchasePrice,
        string storeCode = "S01",
        string productCode = "P-SET"
    ) => new()
    {
        UUID = uuid,
        StoreCode = storeCode,
        ProductCode = productCode,
        MultiCodeProductCode = childCode,
        StoreMultiCodeProductCode = $"{storeCode}{childCode}",
        MultiBarcode = $"BAR-{childCode}",
        PurchasePrice = purchasePrice,
        MultiCodeRetailPrice = retailPrice,
        IsActive = true,
        IsDeleted = false,
        CreatedAt = DateTime.UtcNow,
    };
}
