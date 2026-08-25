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
            typeof(WarehouseProduct),
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

    [Fact]
    public async Task SyncIncrementalAsync_HQ同键多码不得覆盖或软删本地套装子项()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalProductAsync("P-SET-PROTECTED", isDeleted: false);
        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = "set-protected-local",
                ProductCode = "P-SET-PROTECTED",
                SetProductCode = "M-PROTECTED",
                SetItemNumber = "M-PROTECTED",
                SetBarcode = "LOCAL-PROTECTED",
                SetPurchasePrice = 1.23m,
                SetRetailPrice = 4.56m,
                SetType = 1,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await SeedHqProductAsync("P-SET-PROTECTED", start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            "set-protected-local",
            "P-SET-PROTECTED",
            "M-PROTECTED",
            true,
            start.AddDays(1),
            "HQ-SHOULD-NOT-APPLY"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesUpdated);
        Assert.Equal(0, result.Data.ProductSetCodesSoftDeleted);
        var protectedChild = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(x => x.SetCodeId == "set-protected-local");
        Assert.Equal(1, protectedChild.SetType);
        Assert.Equal(1.2m, protectedChild.SetPurchasePrice);
        Assert.Equal(4.56m, protectedChild.SetRetailPrice);
        Assert.Equal("LOCAL-PROTECTED", protectedChild.SetBarcode);
        Assert.True(protectedChild.IsActive);
        Assert.False(protectedChild.IsDeleted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task SyncIncrementalAsync_HQ同GUID和父子键不得复用任意状态本地Type1(
        bool isActive,
        bool isDeleted
    )
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-SET-ALL-STATE";
        const string childCode = "M-SET-ALL-STATE";
        const string localGuid = "set-protected-all-state";
        await SeedLocalProductAsync(productCode, isDeleted: false);
        await _localDb.Insertable(new ProductSetCode
        {
            SetCodeId = localGuid,
            ProductCode = productCode,
            SetProductCode = childCode,
            SetItemNumber = childCode,
            SetBarcode = "LOCAL-MUST-STAY",
            SetPurchasePrice = 1.23m,
            SetRetailPrice = 4.56m,
            SetType = 1,
            SetQuantity = 1,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            localGuid,
            productCode,
            childCode,
            true,
            start.AddDays(1),
            "HQ-MUST-NOT-RESTORE"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        var protectedChild = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == localGuid);
        // 本地 Type1 是人工维护的套装关系，HQ 普通多码不得借 GUID 或父子键改变其任何状态。
        Assert.Equal(1, protectedChild.SetType);
        Assert.Equal(isActive, protectedChild.IsActive);
        Assert.Equal(isDeleted, protectedChild.IsDeleted);
        Assert.Equal("LOCAL-MUST-STAY", protectedChild.SetBarcode);
        Assert.Equal(4.56m, protectedChild.SetRetailPrice);
    }

    [Fact]
    public async Task SyncIncrementalAsync_GUID与业务键命中不同Type2时拒绝并保留两行()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-IDENTITY-CROSS";
        await SeedLocalProductAsync(productCode, isDeleted: false);
        await _localDb.Insertable(new[]
        {
            new ProductSetCode
            {
                SetCodeId = "hq-cross-guid",
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
                SetCodeId = "local-key-owner",
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
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            "hq-cross-guid",
            productCode,
            "CHILD-TARGET",
            true,
            start.AddDays(1),
            "HQ-MUST-NOT-WIN"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesUpdated);
        Assert.Contains(result.Data.Errors, error =>
            error.Contains("本地 ProductSetCode 身份冲突", StringComparison.Ordinal)
            && error.Contains("hq-cross-guid", StringComparison.Ordinal)
            && error.Contains("P-IDENTITY-CROSS/CHILD-TARGET", StringComparison.Ordinal)
            && error.Contains("本地记录=", StringComparison.Ordinal)
        );
        var guidOwner = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == "hq-cross-guid");
        var keyOwner = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == "local-key-owner");
        Assert.Equal("CHILD-GUID-OWNER", guidOwner.SetProductCode);
        Assert.Equal("LOCAL-GUID-OWNER", guidOwner.SetBarcode);
        Assert.False(guidOwner.IsDeleted);
        Assert.Equal("CHILD-TARGET", keyOwner.SetProductCode);
        Assert.Equal("LOCAL-KEY-OWNER", keyOwner.SetBarcode);
        Assert.False(keyOwner.IsDeleted);
    }

    [Fact]
    public async Task SyncIncrementalAsync_交叉身份包含Type1时仍先报告冲突再保留双方()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-IDENTITY-TYPE1-CROSS";
        await SeedLocalProductAsync(productCode, isDeleted: false);
        await _localDb.Insertable(new[]
        {
            new ProductSetCode
            {
                SetCodeId = "hq-type1-cross-guid",
                ProductCode = productCode,
                SetProductCode = "CHILD-TYPE1",
                SetItemNumber = "CHILD-TYPE1",
                SetBarcode = "LOCAL-TYPE1",
                SetPurchasePrice = 1m,
                SetRetailPrice = 1m,
                SetType = 1,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            },
            new ProductSetCode
            {
                SetCodeId = "local-type2-key-owner",
                ProductCode = productCode,
                SetProductCode = "CHILD-TARGET",
                SetItemNumber = "CHILD-TARGET",
                SetBarcode = "LOCAL-TYPE2",
                SetPurchasePrice = 1m,
                SetRetailPrice = 1m,
                SetType = 2,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            "hq-type1-cross-guid",
            productCode,
            "CHILD-TARGET",
            true,
            start.AddDays(1),
            "HQ-MUST-NOT-WIN"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Contains(
            result.Data!.Errors,
            error => error.Contains("本地 ProductSetCode 身份冲突", StringComparison.Ordinal)
        );
        Assert.Equal(
            "CHILD-TYPE1",
            (await _localDb.Queryable<ProductSetCode>()
                    .SingleAsync(row => row.SetCodeId == "hq-type1-cross-guid"))
                .SetProductCode
        );
        var keyOwner = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == "local-type2-key-owner");
        Assert.Equal("LOCAL-TYPE2", keyOwner.SetBarcode);
        Assert.False(keyOwner.IsDeleted);
    }

    [Fact]
    public async Task SyncIncrementalAsync_HQ同GUID多业务键时整组拒绝()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-SOURCE-GUID-CONFLICT";
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            "hq-duplicate-guid",
            productCode,
            "CHILD-A",
            true,
            start.AddDays(1),
            "BAR-A"
        );
        await SeedHqSetCodeAsync(
            "hq-duplicate-guid",
            productCode,
            "CHILD-B",
            true,
            start.AddDays(2),
            "BAR-B"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

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
    public async Task SyncIncrementalAsync_身份冲突跨增量时间边界时仍拒绝当前成员()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-SOURCE-CROSS-WINDOW";
        const string sharedGuid = "hq-cross-window-guid";
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            sharedGuid,
            productCode,
            "CHILD-OLD-OUTSIDE-WINDOW",
            true,
            start.AddDays(-1),
            "BAR-OLD"
        );
        await SeedHqSetCodeAsync(
            sharedGuid,
            productCode,
            "CHILD-NEW-IN-WINDOW",
            true,
            start.AddDays(1),
            "BAR-NEW"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesAdded);
        Assert.Contains(result.Data.Errors, error =>
            error.Contains("同一 GUID 对应多个父子业务键", StringComparison.Ordinal)
        );
        Assert.Equal(
            0,
            await _localDb.Queryable<ProductSetCode>()
                .Where(row => row.ProductCode == productCode)
                .CountAsync()
        );
    }

    [Fact]
    public async Task SyncIncrementalAsync_停用成员与窗口外活跃成员同GUID时不得误删本地关系()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-SOURCE-ACTIVE-INACTIVE-CROSS-WINDOW";
        const string sharedGuid = "hq-active-inactive-cross-window-guid";
        const string activeChildCode = "CHILD-ACTIVE-OUTSIDE-WINDOW";
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            sharedGuid,
            productCode,
            activeChildCode,
            true,
            start.AddDays(-1),
            "BAR-ACTIVE"
        );
        await SeedHqSetCodeAsync(
            sharedGuid,
            productCode,
            "CHILD-INACTIVE-IN-WINDOW",
            false,
            start.AddDays(1),
            "BAR-INACTIVE"
        );
        await SeedLocalSetCodeAsync(sharedGuid, productCode, activeChildCode, false);

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.ProductSetCodesSoftDeleted);
        Assert.Contains(result.Data.Errors, error =>
            error.Contains("同一 GUID 对应多个父子业务键", StringComparison.Ordinal)
        );
        var local = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == sharedGuid);
        Assert.Equal(activeChildCode, local.SetProductCode);
        Assert.True(local.IsActive);
        Assert.False(local.IsDeleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataSyncIncrementalAsync_同页GUID迁移后旧键可由另一GUID安全接管(
        bool reverseSourceOrder
    )
    {
        const string productCode = "P-DATASYNC-SAME-PAGE-MIGRATION";
        const string migratedGuid = "hq-migrated-guid";
        const string replacementGuid = "hq-replacement-guid";
        const string oldChildCode = "CHILD-OLD-KEY";
        const string newChildCode = "CHILD-NEW-KEY";
        var local = new ProductSetCode
        {
            SetCodeId = migratedGuid,
            ProductCode = productCode,
            SetProductCode = oldChildCode,
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
        };
        var migrated = new ProductSetCode
        {
            SetCodeId = migratedGuid,
            ProductCode = productCode,
            SetProductCode = newChildCode,
            SetBarcode = "BAR-MIGRATED",
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
        };
        var replacement = new ProductSetCode
        {
            SetCodeId = replacementGuid,
            ProductCode = productCode,
            SetProductCode = oldChildCode,
            SetBarcode = "BAR-REPLACEMENT",
            SetType = 2,
            IsActive = true,
            IsDeleted = false,
        };
        var sourceRows = reverseSourceOrder
            ? new[] { replacement, migrated }
            : new[] { migrated, replacement };
        var identityIndex = ProductSetCodeIdentityResolver.CreateIndex(new[] { local });

        var plan = DataSyncIncrementalService.BuildProductSetCodePageMutationPlan(
            sourceRows,
            identityIndex
        );

        var updated = Assert.Single(plan.ToUpdate);
        Assert.Same(local, updated);
        Assert.Equal(newChildCode, updated.SetProductCode);
        Assert.Equal("BAR-MIGRATED", updated.SetBarcode);
        var inserted = Assert.Single(plan.ToInsert);
        Assert.Same(replacement, inserted);
        Assert.Equal(oldChildCode, inserted.SetProductCode);
    }

    [Fact]
    public void DataSyncIncrementalAsync_同修改时间超过五万行时按ID稳定分页且不重不漏()
    {
        var modifiedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var sourceRows = Enumerable.Range(1, 50001)
            .Reverse()
            .Select(id => new DIC_一品多码表
            {
                ID = id,
                HGUID = $"GUID-{id}",
                H商品编码 = $"P-{id}",
                H多码商品编号 = $"CHILD-{id}",
                FGC_LastModifyDate = modifiedAt,
            })
            .ToList();

        var pages = DataSyncIncrementalService.BuildProductSetCodeSourcePages(
            sourceRows,
            50000
        );

        Assert.Equal(2, pages.Count);
        Assert.Equal(50000, pages[0].Count);
        Assert.Single(pages[1]);
        var ids = pages.SelectMany(page => page).Select(row => row.ID).ToList();
        Assert.Equal(Enumerable.Range(1, 50001), ids);
        Assert.Equal(50001, ids.Distinct().Count());
    }

    [Fact]
    public void DataSyncIncrementalAsync_单一父商品超过页大小时整组保留在同一事务页()
    {
        var modifiedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var sourceRows = Enumerable.Range(1, 50001)
            .Select(id => new DIC_一品多码表
            {
                ID = id,
                HGUID = $"GUID-LARGE-{id}",
                H商品编码 = "P-LARGE-GROUP",
                H多码商品编号 = $"CHILD-{id}",
                FGC_LastModifyDate = modifiedAt,
            })
            .Append(new DIC_一品多码表
            {
                ID = 50002,
                HGUID = "GUID-OTHER",
                H商品编码 = "P-OTHER-GROUP",
                H多码商品编号 = "CHILD-OTHER",
                FGC_LastModifyDate = modifiedAt,
            })
            .ToList();

        var pages = DataSyncIncrementalService.BuildProductSetCodeSourcePages(
            sourceRows,
            50000
        );

        Assert.Equal(2, pages.Count);
        Assert.Equal(50001, pages[0].Count);
        Assert.All(pages[0], row => Assert.Equal("P-LARGE-GROUP", row.H商品编码));
        var other = Assert.Single(pages[1]);
        Assert.Equal("P-OTHER-GROUP", other.H商品编码);
    }

    [Fact]
    public void DataSyncIncrementalAsync_分页读取身份较预检快照变化时拒绝整个父商品组()
    {
        var expectedRows = new[]
        {
            new DIC_一品多码表
            {
                ID = 1,
                HGUID = "GUID-CHANGED",
                H商品编码 = "P-SNAPSHOT",
                H多码商品编号 = "CHILD-BEFORE",
            },
            new DIC_一品多码表
            {
                ID = 2,
                HGUID = "GUID-UNCHANGED",
                H商品编码 = "P-SNAPSHOT",
                H多码商品编号 = "CHILD-STABLE",
            },
        };
        var actualRows = new[]
        {
            new DIC_一品多码表
            {
                ID = 1,
                HGUID = "GUID-CHANGED",
                H商品编码 = "P-SNAPSHOT",
                H多码商品编号 = "CHILD-AFTER",
            },
            expectedRows[1],
        };

        var validation = DataSyncIncrementalService.ValidateProductSetCodeSourcePageSnapshot(
            expectedRows,
            actualRows
        );

        Assert.Empty(validation.AcceptedRows);
        Assert.Contains("P-SNAPSHOT", validation.ConflictProductCodes);
        Assert.Contains(validation.Errors, error => error.Contains("身份在预检后改变"));
    }

    [Fact]
    public async Task SyncIncrementalAsync_HQ完全同身份重复按修改时间再按ID取胜()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-SOURCE-DUPLICATE";
        const string childCode = "CHILD-DUPLICATE";
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        // 故意先插入最新记录，再插入旧记录；同步结果不能依赖数据库返回顺序的 Last-wins。
        await SeedHqSetCodeAsync(
            "hq-same-identity",
            productCode,
            childCode,
            true,
            start.AddDays(3),
            "BAR-NEWEST"
        );
        await SeedHqSetCodeAsync(
            "hq-same-identity",
            productCode,
            childCode,
            true,
            start.AddDays(2),
            "BAR-OLDER"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ProductSetCodesAdded);
        Assert.Equal(0, result.Data.ProductSetCodesUpdated);
        var row = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(item => item.ProductCode == productCode);
        Assert.Equal("BAR-NEWEST", row.SetBarcode);
    }

    [Fact]
    public async Task SyncIncrementalAsync_空GUID多业务键不构成GUID冲突()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-EMPTY-GUID";
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            null,
            productCode,
            "CHILD-A",
            true,
            start.AddDays(1),
            "BAR-A"
        );
        await SeedHqSetCodeAsync(
            "   ",
            productCode,
            "CHILD-B",
            true,
            start.AddDays(2),
            "BAR-B"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Data!.ProductSetCodesAdded);
        Assert.Equal(
            new[] { "CHILD-A", "CHILD-B" },
            (await _localDb.Queryable<ProductSetCode>()
                    .Where(row => row.ProductCode == productCode)
                    .OrderBy(row => row.SetProductCode)
                    .ToListAsync())
                .Select(row => row.SetProductCode)
        );
    }

    [Fact]
    public async Task SyncIncrementalAsync_Type2身份安全时允许HQ权威重键迁移与复活()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        const string productCode = "P-TYPE2-SAFE";
        await SeedLocalProductAsync(productCode, isDeleted: false);
        await _localDb.Insertable(new[]
        {
            new ProductSetCode
            {
                SetCodeId = "hq-guid-migration",
                ProductCode = productCode,
                SetProductCode = "CHILD-OLD",
                SetItemNumber = "CHILD-OLD",
                SetBarcode = "LOCAL-OLD-KEY",
                SetType = 2,
                SetQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            },
            new ProductSetCode
            {
                SetCodeId = "local-key-migration",
                ProductCode = productCode,
                SetProductCode = "CHILD-STABLE",
                SetItemNumber = "CHILD-STABLE",
                SetBarcode = "LOCAL-DELETED",
                SetType = 2,
                SetQuantity = 1,
                IsActive = false,
                IsDeleted = true,
            },
        }).ExecuteCommandAsync();
        await SeedHqProductAsync(productCode, start.AddDays(1), true);
        await SeedHqSetCodeAsync(
            "hq-guid-migration",
            productCode,
            "CHILD-NEW",
            true,
            start.AddDays(1),
            "HQ-GUID-WINS"
        );
        await SeedHqSetCodeAsync(
            "hq-key-migration",
            productCode,
            "CHILD-STABLE",
            true,
            start.AddDays(2),
            "HQ-KEY-WINS"
        );

        var result = await CreateService().SyncIncrementalAsync(start);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Data!.ProductSetCodesUpdated);
        var guidMigration = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == "hq-guid-migration");
        Assert.Equal("CHILD-NEW", guidMigration.SetProductCode);
        Assert.Equal("HQ-GUID-WINS", guidMigration.SetBarcode);
        Assert.False(guidMigration.IsDeleted);
        Assert.Null(
            await _localDb.Queryable<ProductSetCode>()
                .SingleAsync(row => row.SetCodeId == "local-key-migration")
        );
        var keyMigration = await _localDb.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == "hq-key-migration");
        Assert.Equal("CHILD-STABLE", keyMigration.SetProductCode);
        Assert.Equal("HQ-KEY-WINS", keyMigration.SetBarcode);
        Assert.True(keyMigration.IsActive);
        Assert.False(keyMigration.IsDeleted);
    }

    [Fact]
    public void ProductSetCodeIdentityResolver_区分五种本地命中结果()
    {
        var guidOwner = new ProductSetCode
        {
            SetCodeId = "guid-a",
            ProductCode = " P-IDENTITY ",
            SetProductCode = "CHILD-A",
        };
        var keyOwner = new ProductSetCode
        {
            SetCodeId = "guid-b",
            ProductCode = "P-IDENTITY",
            SetProductCode = " child-b ",
        };
        var index = ProductSetCodeIdentityResolver.CreateIndex(new[] { guidOwner, keyOwner });

        Assert.Equal(
            ProductSetCodeIdentityMatchKind.None,
            index.Resolve("guid-none", "P-IDENTITY", "CHILD-NONE").Kind
        );
        Assert.Equal(
            ProductSetCodeIdentityMatchKind.GuidOnly,
            index.Resolve(" GUID-A ", "P-IDENTITY", "CHILD-NONE").Kind
        );
        Assert.Equal(
            ProductSetCodeIdentityMatchKind.KeyOnly,
            index.Resolve("guid-none", " p-identity ", "CHILD-A").Kind
        );
        Assert.Equal(
            ProductSetCodeIdentityMatchKind.SameRecord,
            index.Resolve("guid-a", "P-IDENTITY", " child-a ").Kind
        );
        Assert.Equal(
            ProductSetCodeIdentityMatchKind.Conflict,
            index.Resolve("guid-a", "P-IDENTITY", "CHILD-B").Kind
        );
    }

    [Fact]
    public void ProductSetCodeIdentityResolver_完全相同源身份按修改时间再按HQ_ID取胜()
    {
        var modifiedAt = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new DIC_一品多码表
            {
                ID = 10,
                HGUID = "same-guid",
                H商品编码 = " P-SAME ",
                H多码商品编号 = "CHILD-SAME",
                H多条形码 = "LOW-ID",
                FGC_LastModifyDate = modifiedAt,
            },
            new DIC_一品多码表
            {
                ID = 20,
                HGUID = " SAME-GUID ",
                H商品编码 = "P-SAME",
                H多码商品编号 = " child-same ",
                H多条形码 = "HIGH-ID",
                FGC_LastModifyDate = modifiedAt,
            },
            new DIC_一品多码表
            {
                ID = 30,
                HGUID = "same-guid",
                H商品编码 = "P-SAME",
                H多码商品编号 = "CHILD-SAME",
                H多条形码 = "NEWER-ID-BUT-OLDER-TIME",
                FGC_LastModifyDate = modifiedAt.AddMinutes(-1),
            },
        };

        var preflight = ProductSetCodeIdentityResolver.PreflightSource(
            rows,
            row => row.HGUID,
            row => row.H商品编码,
            row => row.H多码商品编号,
            row => row.FGC_LastModifyDate,
            row => row.ID
        );

        Assert.Empty(preflight.Conflicts);
        Assert.Equal("HIGH-ID", Assert.Single(preflight.Rows).H多条形码);
    }

    [Fact]
    public void ProductSetCodeIdentityResolver_同业务键混合空与非空GUID时拒绝避免重复关系()
    {
        var rows = new[]
        {
            new DIC_一品多码表
            {
                ID = 10,
                HGUID = null,
                H商品编码 = "P-MIXED-GUID",
                H多码商品编号 = "CHILD-MIXED-GUID",
                FGC_LastModifyDate = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc),
            },
            new DIC_一品多码表
            {
                ID = 20,
                HGUID = "hq-mixed-guid",
                H商品编码 = "P-MIXED-GUID",
                H多码商品编号 = "CHILD-MIXED-GUID",
                FGC_LastModifyDate = new DateTime(2026, 5, 8, 1, 0, 0, DateTimeKind.Utc),
            },
        };

        var preflight = ProductSetCodeIdentityResolver.PreflightSource(
            rows,
            row => row.HGUID,
            row => row.H商品编码,
            row => row.H多码商品编号,
            row => row.FGC_LastModifyDate,
            row => row.ID
        );

        var conflict = Assert.Single(preflight.Conflicts);
        Assert.Equal(
            ProductSetCodeSourceConflictKind.KeyHasMixedGuidPresence,
            conflict.Kind
        );
        Assert.Empty(preflight.Rows);
        Assert.Contains("同时存在空 GUID 与非空 GUID", conflict.ToErrorMessage());
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
