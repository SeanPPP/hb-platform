using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductSetCodeReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public ProductSetCodeReactServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(ProductSetCode),
            typeof(Store),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct),
            typeof(HBLocalSupplier)
        );
    }

    [Fact]
    public async Task GetGridDataAsync_按ProductCode只返回当前商品多码并同步Total()
    {
        await SeedProductSetCodesAsync();
        var service = CreateService();

        var result = await service.GetGridDataAsync(new ProductSetCodeGridRequestDto
        {
            ProductCode = "P-A",
            StartRow = 0,
            PageSize = 20,
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Total);
        Assert.Equal(new[] { "set-a-2", "set-a-1" }, result.Items!.Select(item => item.SetCodeId));
        Assert.All(result.Items!, item => Assert.Equal("P-A", item.ProductCode));
    }

    [Fact]
    public async Task GetGridDataAsync_兼容FilterModelProductCode筛选()
    {
        await SeedProductSetCodesAsync();
        var service = CreateService();

        var result = await service.GetGridDataAsync(new ProductSetCodeGridRequestDto
        {
            StartRow = 0,
            PageSize = 20,
            FilterModel = new Dictionary<string, FilterModelDto>
            {
                ["productCode"] = new()
                {
                    FilterType = "text",
                    Type = "equals",
                    Filter = "P-B",
                },
            },
        });

        Assert.True(result.Success);
        var item = Assert.Single(result.Items!);
        Assert.Equal(1, result.Total);
        Assert.Equal("set-b-1", item.SetCodeId);
        Assert.Equal("P-B", item.ProductCode);
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_套装忽略提交成本并按全部兄弟零售价重算()
    {
        var product = BuildProduct("P-SET", "ITEM-SET", "BAR-SET", "200");
        product.PurchasePrice = 10m;
        product.IsActive = true;
        await _db.Insertable(product).ExecuteCommandAsync();
        var first = BuildSetCode("set-1", "P-SET", "CHILD-A", DateTime.UtcNow);
        first.SetProductCode = "CHILD-A";
        first.SetPurchasePrice = 1m;
        first.SetRetailPrice = 20m;
        first.SetType = 1;
        var second = BuildSetCode("set-2", "P-SET", "CHILD-B", DateTime.UtcNow);
        second.SetProductCode = "CHILD-B";
        second.SetPurchasePrice = 1m;
        second.SetRetailPrice = 30m;
        second.SetType = 1;
        await _db.Insertable(new[] { first, second }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new()
                {
                    Id = "set-1",
                    SetPurchasePrice = 99m,
                    SetRetailPrice = 25m,
                },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var rows = await _db.Queryable<ProductSetCode>()
            .Where(x => x.ProductCode == "P-SET")
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 4.55m, 5.45m }, rows.Select(x => x.SetPurchasePrice));
        Assert.Equal(new decimal?[] { 25m, 30m }, rows.Select(x => x.SetRetailPrice));
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_目标套装组无法重算时回滚零售价修改()
    {
        var product = BuildProduct("P-SET", "ITEM-SET", "BAR-SET", "200");
        product.PurchasePrice = 10m;
        product.IsActive = true;
        await _db.Insertable(product).ExecuteCommandAsync();
        var first = BuildSetCode("set-1", "P-SET", "CHILD-A", DateTime.UtcNow);
        first.SetProductCode = "CHILD-A";
        first.SetPurchasePrice = 4m;
        first.SetRetailPrice = 20m;
        first.SetType = 1;
        var second = BuildSetCode("set-2", "P-SET", "CHILD-B", DateTime.UtcNow);
        second.SetProductCode = "CHILD-B";
        second.SetPurchasePrice = 6m;
        second.SetRetailPrice = 30m;
        second.SetType = 1;
        await _db.Insertable(new[] { first, second }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new() { Id = "set-1", SetRetailPrice = 0m },
            },
            "测试管理员"
        );

        Assert.False(result.Success);
        var stored = await _db.Queryable<ProductSetCode>()
            .SingleAsync(x => x.SetCodeId == "set-1");
        Assert.Equal(20m, stored.SetRetailPrice);
        Assert.Equal(4m, stored.SetPurchasePrice);
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_Type1总部售价变化自动重算非请求回退门店()
    {
        await SeedType1FallbackStoreScenarioAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new() { Id = "type1-a", SetRetailPrice = 25m },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var globalRows = await _db.Queryable<ProductSetCode>()
            .Where(x => x.ProductCode == "P-TYPE1-FALLBACK")
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 4.55m, 5.45m }, globalRows.Select(x => x.SetPurchasePrice));
        Assert.Equal(new decimal?[] { 25m, 30m }, globalRows.Select(x => x.SetRetailPrice));

        var fallbackRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.ProductCode == "P-TYPE1-FALLBACK" && x.StoreCode == "S-FALLBACK")
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 4.55m, 5.45m }, fallbackRows.Select(x => x.PurchasePrice));
        Assert.Equal(new decimal?[] { 0m, 30m }, fallbackRows.Select(x => x.MultiCodeRetailPrice));

        var nullFallbackRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x =>
                x.ProductCode == "P-TYPE1-FALLBACK"
                && x.StoreCode == "S-FALLBACK-NULL"
            )
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(
            new decimal?[] { 4.55m, 5.45m },
            nullFallbackRows.Select(x => x.PurchasePrice)
        );
        Assert.Equal(
            new decimal?[] { null, 30m },
            nullFallbackRows.Select(x => x.MultiCodeRetailPrice)
        );

        var negativeFallbackRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x =>
                x.ProductCode == "P-TYPE1-FALLBACK"
                && x.StoreCode == "S-FALLBACK-NEGATIVE"
            )
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(
            new decimal?[] { 4.55m, 5.45m },
            negativeFallbackRows.Select(x => x.PurchasePrice)
        );
        Assert.Equal(
            new decimal?[] { -1m, 30m },
            negativeFallbackRows.Select(x => x.MultiCodeRetailPrice)
        );

        var customRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.ProductCode == "P-TYPE1-FALLBACK" && x.StoreCode == "S-CUSTOM")
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 80m, 81m }, customRows.Select(x => x.PurchasePrice));
        Assert.Equal(new decimal?[] { 22m, 30m }, customRows.Select(x => x.MultiCodeRetailPrice));
        Assert.Equal(
            66m,
            (await _db.Queryable<StoreMultiCodeProduct>()
                    .SingleAsync(x => x.UUID == "STORE-UNRELATED"))
                .PurchasePrice
        );
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_相同Type1售价及Type2或停用Type1变化不扩展门店组()
    {
        await SeedType1FallbackStoreScenarioAsync();
        var type2Product = BuildProduct("P-TYPE2-NO-AUTO", "ITEM-TYPE2", "BAR-TYPE2", "200");
        type2Product.PurchasePrice = 7m;
        var inactiveProduct = BuildProduct("P-INACTIVE-NO-AUTO", "ITEM-INACTIVE", "BAR-INACTIVE", "200");
        inactiveProduct.PurchasePrice = 6m;
        await _db.Insertable(new[] { type2Product, inactiveProduct }).ExecuteCommandAsync();

        var type2 = BuildSetCode("type2-no-auto", "P-TYPE2-NO-AUTO", "BAR-TYPE2", DateTime.UtcNow);
        type2.SetProductCode = "CHILD-TYPE2";
        type2.SetPurchasePrice = 7m;
        type2.SetRetailPrice = 5m;
        type2.SetType = 2;
        var inactiveType1 = BuildSetCode(
            "inactive-type1-no-auto",
            "P-INACTIVE-NO-AUTO",
            "BAR-INACTIVE",
            DateTime.UtcNow
        );
        inactiveType1.SetProductCode = "CHILD-INACTIVE";
        inactiveType1.SetPurchasePrice = 6m;
        inactiveType1.SetRetailPrice = 11m;
        inactiveType1.SetType = 1;
        inactiveType1.IsActive = false;
        await _db.Insertable(new[] { type2, inactiveType1 }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreProjectionRow(
                "STORE-TYPE2-NO-AUTO",
                "P-TYPE2-NO-AUTO",
                "CHILD-TYPE2",
                "S-TYPE2",
                77m,
                0m
            ),
            BuildStoreProjectionRow(
                "STORE-INACTIVE-NO-AUTO",
                "P-INACTIVE-NO-AUTO",
                "CHILD-INACTIVE",
                "S-INACTIVE",
                55m,
                0m
            ),
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new() { Id = "type1-a", SetRetailPrice = 20m },
                new() { Id = "type2-no-auto", SetRetailPrice = 9m },
                new() { Id = "inactive-type1-no-auto", SetRetailPrice = 13m },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var untouchedCosts = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x =>
                x.UUID == "STORE-FALLBACK-A"
                || x.UUID == "STORE-FALLBACK-B"
                || x.UUID == "STORE-FALLBACK-NULL-A"
                || x.UUID == "STORE-FALLBACK-NULL-B"
                || x.UUID == "STORE-TYPE2-NO-AUTO"
                || x.UUID == "STORE-INACTIVE-NO-AUTO"
            )
            .OrderBy(x => x.UUID)
            .ToListAsync();
        Assert.Equal(
            new decimal?[] { 90m, 91m, 92m, 93m, 55m, 77m },
            untouchedCosts.Select(x => x.PurchasePrice)
        );
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_自动纳入的回退门店结构失败时整事务回滚()
    {
        await SeedType1FallbackStoreScenarioAsync(includeFallbackSecondChild: false);

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new() { Id = "type1-a", SetRetailPrice = 25m },
            },
            "测试管理员"
        );

        Assert.False(result.Success);
        Assert.Contains("缺少子项: CHILD-B", result.Message);
        var globalRows = await _db.Queryable<ProductSetCode>()
            .Where(x => x.ProductCode == "P-TYPE1-FALLBACK")
            .OrderBy(x => x.SetProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 4m, 6m }, globalRows.Select(x => x.SetPurchasePrice));
        Assert.Equal(new decimal?[] { 20m, 30m }, globalRows.Select(x => x.SetRetailPrice));
        Assert.Equal(
            90m,
            (await _db.Queryable<StoreMultiCodeProduct>()
                    .SingleAsync(x => x.UUID == "STORE-FALLBACK-A"))
                .PurchasePrice
        );
        var nullFallbackRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == "S-FALLBACK-NULL")
            .OrderBy(x => x.MultiCodeProductCode)
            .ToListAsync();
        Assert.Equal(new decimal?[] { 92m, 93m }, nullFallbackRows.Select(x => x.PurchasePrice));
    }

    [Fact]
    public async Task BatchUpdateStatusAsync_仅同步相同父商品和子项的指定门店投影()
    {
        await SeedProjectionCollisionAsync();
        await _db.Updateable<ProductSetCode>()
            .SetColumns(x => x.IsActive == false)
            .Where(x => x.SetCodeId == "set-a")
            .ExecuteCommandAsync();
        await _db.Updateable<StoreMultiCodeProduct>()
            .SetColumns(x => x.IsActive == false)
            .Where(x => x.UUID == "P-A-S-1")
            .ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateStatusAsync(
            new List<string> { "set-a" },
            true,
            "测试管理员",
            new List<string> { "S-1" }
        );

        Assert.True(result.Success, result.Message);
        var projections = await GetProjectionMapAsync();
        Assert.True(projections["P-A|CHILD-COLLISION|S-1"].IsActive);
        Assert.True(projections["P-A|CHILD-COLLISION|S-2"].IsActive);
        Assert.True(projections["P-B|CHILD-COLLISION|S-1"].IsActive);
    }

    [Fact]
    public async Task BatchUpdateStatusAsync_非请求门店投影与新总部结构不兼容时整事务回滚()
    {
        await SeedProjectionCollisionAsync();
        await _db.Updateable<ProductSetCode>()
            .SetColumns(x => x.SetType == 1)
            .Where(x => x.SetCodeId == "set-a")
            .ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateStatusAsync(
            new List<string> { "set-a" },
            false,
            "测试管理员",
            new List<string> { "S-1" }
        );

        Assert.False(result.Success);
        Assert.Contains("总部无有效关系但门店仍有活跃子项", result.Message);
        Assert.True(
            (await _db.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-a"))
                .IsActive
        );
        var projections = await GetProjectionMapAsync();
        Assert.True(projections["P-A|CHILD-COLLISION|S-1"].IsActive);
        Assert.True(projections["P-A|CHILD-COLLISION|S-2"].IsActive);
        Assert.True(projections["P-B|CHILD-COLLISION|S-1"].IsActive);
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_仅同步相同父商品子项和指定门店投影()
    {
        await SeedProjectionCollisionAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new()
                {
                    Id = "set-a",
                    SetRetailPrice = 29m,
                    StoreCodes = new List<string> { "S-1" },
                },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var projections = await GetProjectionMapAsync();
        Assert.Equal(29m, projections["P-A|CHILD-COLLISION|S-1"].MultiCodeRetailPrice);
        Assert.Equal(10m, projections["P-A|CHILD-COLLISION|S-2"].MultiCodeRetailPrice);
        Assert.Equal(20m, projections["P-B|CHILD-COLLISION|S-1"].MultiCodeRetailPrice);
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_按每项精确门店映射且忽略软删除投影()
    {
        await SeedProjectionCollisionAsync();
        var deletedProjection = BuildStoreProjection(
            "P-A",
            "CHILD-COLLISION",
            "S-3",
            "BAR-A",
            8m,
            10m
        );
        deletedProjection.IsDeleted = true;
        await _db.Insertable(deletedProjection).ExecuteCommandAsync();
        // 非本项门店即使历史成本错误，也不能被本批请求顺带重算。
        await _db.Updateable<StoreMultiCodeProduct>()
            .SetColumns(x => x.PurchasePrice == 91m)
            .Where(x => x.UUID == "P-A-S-2")
            .ExecuteCommandAsync();
        await _db.Updateable<StoreMultiCodeProduct>()
            .SetColumns(x => x.PurchasePrice == 92m)
            .Where(x => x.UUID == "P-B-S-1")
            .ExecuteCommandAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new() { Id = "set-a", SetRetailPrice = 29m, StoreCodes = new List<string> { "S-1" } },
                // 该项仅用于验证不会把另一项的 S-1 扩展为全批次门店集合。
                new() { Id = "set-b", SetRetailPrice = 39m, StoreCodes = new List<string> { "S-2" } },
                new() { Id = "set-a", SetRetailPrice = 29m, StoreCodes = new List<string> { "S-3" } },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var projections = await GetProjectionMapAsync();
        Assert.Equal(29m, projections["P-A|CHILD-COLLISION|S-1"].MultiCodeRetailPrice);
        Assert.Equal(10m, projections["P-A|CHILD-COLLISION|S-2"].MultiCodeRetailPrice);
        Assert.Equal(10m, projections["P-A|CHILD-COLLISION|S-3"].MultiCodeRetailPrice);
        Assert.Equal(91m, projections["P-A|CHILD-COLLISION|S-2"].PurchasePrice);
        Assert.Equal(92m, projections["P-B|CHILD-COLLISION|S-1"].PurchasePrice);
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_重复UUID价格冲突时拒绝整批()
    {
        await SeedProjectionCollisionAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new() { Id = "set-a", SetRetailPrice = 29m },
                new() { Id = "set-a", SetRetailPrice = 30m },
            },
            "测试管理员"
        );

        Assert.False(result.Success);
        var stored = await _db.Queryable<ProductSetCode>()
            .SingleAsync(x => x.SetCodeId == "set-a");
        Assert.Equal(10m, stored.SetRetailPrice);
    }

    [Fact]
    public async Task BatchUpdatePricesAsync_Type二同样忽略客户端成本()
    {
        await SeedProjectionCollisionAsync();

        var result = await CreateService().BatchUpdatePricesAsync(
            new List<BatchUpdatePricesItemDto>
            {
                new()
                {
                    Id = "set-a",
                    SetPurchasePrice = 999m,
                    SetRetailPrice = 29m,
                    StoreCodes = new List<string> { "S-1" },
                },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var stored = await _db.Queryable<ProductSetCode>()
            .SingleAsync(x => x.SetCodeId == "set-a");
        Assert.Equal(8m, stored.SetPurchasePrice);
        var projection = await _db.Queryable<StoreMultiCodeProduct>()
            .SingleAsync(x => x.UUID == "P-A-S-1");
        Assert.Equal(8m, projection.PurchasePrice);
    }

    [Fact]
    public async Task BatchDeleteAsync_仅删除相同父商品和子项的全部门店投影()
    {
        await SeedProjectionCollisionAsync();

        var result = await CreateService().BatchDeleteAsync(new List<string> { "set-a" }, "测试管理员");

        Assert.True(result.Success, result.Message);
        var projections = await GetProjectionMapAsync();
        Assert.DoesNotContain("P-A|CHILD-COLLISION|S-1", projections.Keys);
        Assert.DoesNotContain("P-A|CHILD-COLLISION|S-2", projections.Keys);
        Assert.Contains("P-B|CHILD-COLLISION|S-1", projections.Keys);
    }

    [Fact]
    public async Task BatchUpdateBarcodesAsync_仅窄列更新相同父商品子项的全部门店且不覆盖成本()
    {
        await SeedProjectionCollisionAsync();

        var result = await CreateService().BatchUpdateBarcodesAsync(
            new List<BatchUpdateBarcodesItemDto>
            {
                new() { Id = "set-a", SetBarcode = "NEW-BARCODE" },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var projections = await GetProjectionMapAsync();
        Assert.Equal("NEW-BARCODE", projections["P-A|CHILD-COLLISION|S-1"].MultiBarcode);
        Assert.Equal("NEW-BARCODE", projections["P-A|CHILD-COLLISION|S-2"].MultiBarcode);
        Assert.Equal("BAR-B", projections["P-B|CHILD-COLLISION|S-1"].MultiBarcode);
        Assert.Equal(8m, projections["P-A|CHILD-COLLISION|S-1"].PurchasePrice);
        Assert.Equal(9m, projections["P-A|CHILD-COLLISION|S-2"].PurchasePrice);
    }

    [Fact]
    public async Task BatchUpdateBarcodesAsync_Type一成本来源无效时回滚条码更新()
    {
        var product = BuildProduct("P-BARCODE-ONLY", "ITEM-BARCODE-ONLY", "OLD", "200");
        product.PurchasePrice = 0m;
        await _db.Insertable(product).ExecuteCommandAsync();
        var relation = BuildSetCode(
            "set-barcode-only",
            "P-BARCODE-ONLY",
            "CHILD-BARCODE-ONLY",
            DateTime.UtcNow
        );
        relation.SetType = 1;
        relation.SetBarcode = "OLD";
        relation.SetRetailPrice = 0m;
        relation.SetPurchasePrice = 3m;
        await _db.Insertable(relation).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateBarcodesAsync(
            new List<BatchUpdateBarcodesItemDto>
            {
                new() { Id = relation.SetCodeId, SetBarcode = "NEW" },
            },
            "测试管理员"
        );

        Assert.False(result.Success);
        var persisted = await _db.Queryable<ProductSetCode>()
            .SingleAsync(row => row.SetCodeId == relation.SetCodeId);
        Assert.Equal("OLD", persisted.SetBarcode);
        Assert.Equal(3m, persisted.SetPurchasePrice);
    }

    [Fact]
    public async Task BatchCreateAsync_Type2忽略客户端成本并按请求顺序生成唯一货号()
    {
        await SeedType2CreationParentAsync("P-CREATE", "ITEM-CREATE", 8m);

        var result = await CreateService().BatchCreateAsync(
                new List<CreateSetCodeItemDto>
                {
                    new() { ProductCode = "P-CREATE", SetBarcode = "SET-1", SetPurchasePrice = 999m },
                    new() { ProductCode = "P-CREATE", SetBarcode = "SET-2", SetPurchasePrice = 888m },
                },
                "测试管理员"
            );

        Assert.True(result.Success, result.Message);
        var rows = await _db.Queryable<ProductSetCode>()
            .Where(x => x.ProductCode == "P-CREATE")
            .OrderBy(x => x.SetItemNumber)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(x => x.SetItemNumber).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.StartsWith("ITEM-CREATE", row.SetItemNumber);
            Assert.Equal(8m, row.SetPurchasePrice);
            Assert.False(string.IsNullOrWhiteSpace(row.SetProductCode));
        });
        var projections = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.ProductCode == "P-CREATE")
            .ToListAsync();
        Assert.Equal(4, projections.Count);
        Assert.All(projections.Where(x => x.StoreCode == "S01"), row => Assert.Equal(8m, row.PurchasePrice));
        Assert.All(projections.Where(x => x.StoreCode == "S02"), row => Assert.Equal(9m, row.PurchasePrice));
    }

    [Fact]
    public async Task BatchCreateWithStoreSyncAsync_Type2只投影请求门店且使用目标门店主成本()
    {
        await SeedType2CreationParentAsync("P-CREATE-SCOPE", "ITEM-CREATE-SCOPE", 8m);

        var result = await CreateService().BatchCreateWithStoreSyncAsync(
            new List<CreateSetCodeWithStoreSyncDto>
            {
                new()
                {
                    ProductCode = "P-CREATE-SCOPE",
                    SetBarcode = "SET-SCOPE",
                    SetPurchasePrice = 999m,
                    SetRetailPrice = 12m,
                    StoreCodes = new List<string> { "S01" },
                },
            },
            "测试管理员"
        );

        Assert.True(result.Success, result.Message);
        var relation = await _db.Queryable<ProductSetCode>()
            .SingleAsync(x => x.ProductCode == "P-CREATE-SCOPE");
        Assert.Equal(8m, relation.SetPurchasePrice);
        Assert.False(string.IsNullOrWhiteSpace(relation.SetProductCode));
        var projection = Assert.Single(await _db.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.ProductCode == "P-CREATE-SCOPE")
            .ToListAsync());
        Assert.Equal("S01", projection.StoreCode);
        Assert.Equal(8m, projection.PurchasePrice);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private ProductSetCodeReactService CreateService(
        IStoreRetailPriceReactService? storeRetailPriceService = null
    )
    {
        return new ProductSetCodeReactService(
            CreateSqlSugarContext(_db),
            storeRetailPriceService ?? Mock.Of<IStoreRetailPriceReactService>(),
            NullLogger<ProductSetCodeReactService>.Instance
        );
    }

    private async Task SeedType2CreationParentAsync(
        string productCode,
        string itemNumber,
        decimal purchasePrice
    )
    {
        var product = BuildProduct(productCode, itemNumber, $"BAR-{productCode}", "200");
        product.PurchasePrice = purchasePrice;
        await _db.Insertable(product).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new Store
            {
                StoreGUID = "STORE-GUID-S01",
                StoreCode = "S01",
                StoreName = "S01",
                IsActive = true,
                IsDeleted = false,
            },
            new Store
            {
                StoreGUID = "STORE-GUID-S02",
                StoreCode = "S02",
                StoreName = "S02",
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new StoreRetailPrice
            {
                UUID = $"SRP-S01-{productCode}",
                StoreCode = "S01",
                ProductCode = productCode,
                PurchasePrice = purchasePrice,
                IsActive = true,
                IsDeleted = false,
            },
            new StoreRetailPrice
            {
                UUID = $"SRP-S02-{productCode}",
                StoreCode = "S02",
                ProductCode = productCode,
                PurchasePrice = purchasePrice + 1m,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductSetCodesAsync()
    {
        await _db.Insertable(new[]
        {
            new HBLocalSupplier { Guid = "supplier-200", LocalSupplierCode = "200", Name = "Hot Bargain" },
            new HBLocalSupplier { Guid = "supplier-225", LocalSupplierCode = "225", Name = "MNB" },
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            BuildProduct("P-A", "ITEM-A", "BAR-A", "200"),
            BuildProduct("P-B", "ITEM-B", "BAR-B", "225"),
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            BuildSetCode("set-a-1", "P-A", "A-1", DateTime.UtcNow.AddMinutes(-2)),
            BuildSetCode("set-a-2", "P-A", "A-2", DateTime.UtcNow.AddMinutes(-1)),
            BuildSetCode("set-b-1", "P-B", "B-1", DateTime.UtcNow),
        }).ExecuteCommandAsync();
    }

    private async Task SeedType1FallbackStoreScenarioAsync(
        bool includeFallbackSecondChild = true
    )
    {
        var parent = BuildProduct(
            "P-TYPE1-FALLBACK",
            "ITEM-TYPE1-FALLBACK",
            "BAR-TYPE1-FALLBACK",
            "200"
        );
        parent.PurchasePrice = 10m;
        var unrelated = BuildProduct(
            "P-UNRELATED",
            "ITEM-UNRELATED",
            "BAR-UNRELATED",
            "200"
        );
        unrelated.PurchasePrice = 9m;
        await _db.Insertable(new[] { parent, unrelated }).ExecuteCommandAsync();

        var first = BuildSetCode("type1-a", "P-TYPE1-FALLBACK", "BAR-A", DateTime.UtcNow);
        first.SetProductCode = "CHILD-A";
        first.SetPurchasePrice = 4m;
        first.SetRetailPrice = 20m;
        first.SetType = 1;
        var second = BuildSetCode("type1-b", "P-TYPE1-FALLBACK", "BAR-B", DateTime.UtcNow);
        second.SetProductCode = "CHILD-B";
        second.SetPurchasePrice = 6m;
        second.SetRetailPrice = 30m;
        second.SetType = 1;
        await _db.Insertable(new[] { first, second }).ExecuteCommandAsync();

        var storeRows = new List<StoreMultiCodeProduct>
        {
            BuildStoreProjectionRow(
                "STORE-FALLBACK-A",
                "P-TYPE1-FALLBACK",
                "CHILD-A",
                "S-FALLBACK",
                90m,
                0m
            ),
            BuildStoreProjectionRow(
                "STORE-CUSTOM-A",
                "P-TYPE1-FALLBACK",
                "CHILD-A",
                "S-CUSTOM",
                80m,
                22m
            ),
            BuildStoreProjectionRow(
                "STORE-CUSTOM-B",
                "P-TYPE1-FALLBACK",
                "CHILD-B",
                "S-CUSTOM",
                81m,
                30m
            ),
            BuildStoreProjectionRow(
                "STORE-FALLBACK-NULL-A",
                "P-TYPE1-FALLBACK",
                "CHILD-A",
                "S-FALLBACK-NULL",
                92m,
                null
            ),
            BuildStoreProjectionRow(
                "STORE-FALLBACK-NULL-B",
                "P-TYPE1-FALLBACK",
                "CHILD-B",
                "S-FALLBACK-NULL",
                93m,
                30m
            ),
            BuildStoreProjectionRow(
                "STORE-FALLBACK-NEGATIVE-A",
                "P-TYPE1-FALLBACK",
                "CHILD-A",
                "S-FALLBACK-NEGATIVE",
                94m,
                -1m
            ),
            BuildStoreProjectionRow(
                "STORE-FALLBACK-NEGATIVE-B",
                "P-TYPE1-FALLBACK",
                "CHILD-B",
                "S-FALLBACK-NEGATIVE",
                95m,
                30m
            ),
            BuildStoreProjectionRow(
                "STORE-UNRELATED",
                "P-UNRELATED",
                "CHILD-UNRELATED",
                "S-UNRELATED",
                66m,
                0m
            ),
        };
        if (includeFallbackSecondChild)
        {
            storeRows.Add(BuildStoreProjectionRow(
                "STORE-FALLBACK-B",
                "P-TYPE1-FALLBACK",
                "CHILD-B",
                "S-FALLBACK",
                91m,
                30m
            ));
        }
        await _db.Insertable(storeRows).ExecuteCommandAsync();
    }

    private async Task SeedProjectionCollisionAsync()
    {
        var productA = BuildProduct("P-A", "ITEM-A", "BAR-A", "200");
        productA.PurchasePrice = 8m;
        var productB = BuildProduct("P-B", "ITEM-B", "BAR-B", "200");
        productB.PurchasePrice = 7m;
        await _db.Insertable(new[]
        {
            productA,
            productB,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new StoreRetailPrice
            {
                UUID = "retail-p-a-s-1",
                ProductCode = "P-A",
                StoreCode = "S-1",
                PurchasePrice = 8m,
                IsDeleted = false,
            },
            new StoreRetailPrice
            {
                UUID = "retail-p-a-s-2",
                ProductCode = "P-A",
                StoreCode = "S-2",
                PurchasePrice = 9m,
                IsDeleted = false,
            },
            new StoreRetailPrice
            {
                UUID = "retail-p-b-s-1",
                ProductCode = "P-B",
                StoreCode = "S-1",
                PurchasePrice = 7m,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new ProductSetCode
            {
                SetCodeId = "set-a",
                ProductCode = "P-A",
                SetProductCode = "CHILD-COLLISION",
                SetItemNumber = "ITEM-A-01",
                SetBarcode = "BAR-A",
                SetPurchasePrice = 8m,
                SetRetailPrice = 10m,
                SetType = 2,
                IsActive = true,
                IsDeleted = false,
            },
            new ProductSetCode
            {
                SetCodeId = "set-b",
                ProductCode = "P-B",
                SetProductCode = "CHILD-COLLISION",
                SetItemNumber = "ITEM-B-01",
                SetBarcode = "BAR-B",
                SetPurchasePrice = 7m,
                SetRetailPrice = 20m,
                SetType = 2,
                IsActive = true,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            BuildStoreProjection("P-A", "CHILD-COLLISION", "S-1", "BAR-A", 8m, 10m),
            BuildStoreProjection("P-A", "CHILD-COLLISION", "S-2", "BAR-A", 9m, 10m),
            BuildStoreProjection("P-B", "CHILD-COLLISION", "S-1", "BAR-B", 7m, 20m),
        }).ExecuteCommandAsync();
    }

    private async Task<Dictionary<string, StoreMultiCodeProduct>> GetProjectionMapAsync() => (
        await _db.Queryable<StoreMultiCodeProduct>().ToListAsync()
    ).ToDictionary(
        x => $"{x.ProductCode}|{x.MultiCodeProductCode}|{x.StoreCode}",
        x => x
    );

    private static StoreMultiCodeProduct BuildStoreProjectionRow(
        string uuid,
        string productCode,
        string childProductCode,
        string storeCode,
        decimal purchasePrice,
        decimal? retailPrice
    ) => new()
    {
        UUID = uuid,
        ProductCode = productCode,
        MultiCodeProductCode = childProductCode,
        StoreCode = storeCode,
        MultiBarcode = $"BAR-{childProductCode}",
        PurchasePrice = purchasePrice,
        MultiCodeRetailPrice = retailPrice,
        IsActive = true,
        IsDeleted = false,
    };

    private static StoreMultiCodeProduct BuildStoreProjection(
        string productCode,
        string childProductCode,
        string storeCode,
        string barcode,
        decimal purchasePrice,
        decimal retailPrice
    ) => new()
    {
        UUID = $"{productCode}-{storeCode}",
        ProductCode = productCode,
        MultiCodeProductCode = childProductCode,
        StoreCode = storeCode,
        MultiBarcode = barcode,
        PurchasePrice = purchasePrice,
        MultiCodeRetailPrice = retailPrice,
        IsActive = true,
        IsDeleted = false,
    };

    private static Product BuildProduct(
        string productCode,
        string itemNumber,
        string barcode,
        string supplierCode
    ) => new()
    {
        ProductCode = productCode,
        ProductName = productCode,
        ItemNumber = itemNumber,
        Barcode = barcode,
        LocalSupplierCode = supplierCode,
        IsDeleted = false,
    };

    private static ProductSetCode BuildSetCode(
        string setCodeId,
        string productCode,
        string setBarcode,
        DateTime updatedAt
    ) => new()
    {
        SetCodeId = setCodeId,
        ProductCode = productCode,
        SetProductCode = $"{setCodeId}-product",
        SetItemNumber = $"{setCodeId}-item",
        SetBarcode = setBarcode,
        SetPurchasePrice = 1.23m,
        SetRetailPrice = 2.99m,
        IsActive = true,
        IsDeleted = false,
        UpdatedAt = updatedAt,
        UpdatedBy = "test",
    };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }
}

public sealed class StorePriceSetChildCostConsistencyContractTests
{
    private static readonly string ApiRoot = Path.Combine(
        Environment.GetEnvironmentVariable("HB_PLATFORM_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."),
        "BlazorApp.Api",
        "Services",
        "React"
    );

    [Fact]
    public void 门店价格写入入口_在业务锁内重读窄列写入并重算()
    {
        var retail = File.ReadAllText(Path.Combine(ApiRoot, "StoreRetailPriceReactService.cs"));
        var product = File.ReadAllText(Path.Combine(ApiRoot, "StoreProductPriceReactService.cs"));
        var multiCode = File.ReadAllText(Path.Combine(ApiRoot, "StoreMultiCodePricesReactService.cs"));

        Assert.Contains("SetChildPurchasePriceMutationLock.AcquireProductsAsync", retail);
        Assert.Contains("RecalculateStoreGroupsLockedAsync", retail);
        Assert.Contains("SkippedGroupCount > 0", retail);
        Assert.DoesNotContain("Updateable(entity).ExecuteCommandAsync", retail);

        Assert.Contains("SetChildPurchasePriceMutationLock.AcquireProductsAsync", product);
        Assert.Contains("RecalculateStoresLockedAsync", product);
        Assert.Contains("SkippedGroupCount > 0", product);
        Assert.DoesNotContain("BulkMergeAsync", product);

        Assert.Contains("SetChildPurchasePriceMutationLock.AcquireProductsAsync", multiCode);
        Assert.DoesNotContain("SetChildPurchasePriceMutationLock.AcquireAllAsync", multiCode);
        Assert.Contains("RecalculateStoreGroupsLockedAsync", multiCode);
        Assert.Contains("SkippedGroupCount > 0", multiCode);
        Assert.Contains("UpdateColumns", multiCode);
    }

    [Fact]
    public async Task CopyStoreData_已有已提交目标时返回200部分成功和失败详情()
    {
        var service = new Mock<IStoreProductPriceReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.CopyStoreDataAsync(It.IsAny<CopyStoreDataDto>(), "system"))
            .ReturnsAsync(new ApiResponse<CopyStoreDataResultDto>
            {
                Success = true,
                ErrorCode = "PARTIAL_SUCCESS",
                Data = new CopyStoreDataResultDto { StoreRetailPriceCopied = 3 },
                Details = new
                {
                    FailureDetails = new[]
                    {
                        new { TargetStoreCode = "1002", ErrorCode = "COPY_STORE_DATA_FAILED" },
                    },
                },
            });
        var controller = CreateCopyStoreDataController(service.Object);

        var response = await controller.CopyStoreData(new CopyStoreDataDto
        {
            SourceStoreCode = "1001",
            TargetStoreCodes = new List<string> { "1002" },
        });

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<CopyStoreDataResultDto>>(ok.Value);
        Assert.True(payload.Success);
        Assert.Equal("PARTIAL_SUCCESS", payload.ErrorCode);
        Assert.NotNull(payload.Details);
        service.VerifyAll();
    }

    [Fact]
    public async Task CopyStoreData_零成功且锁忙时返回409()
    {
        var service = new Mock<IStoreProductPriceReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.CopyStoreDataAsync(It.IsAny<CopyStoreDataDto>(), "system"))
            .ReturnsAsync(
                ApiResponse<CopyStoreDataResultDto>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    "SET_CHILD_PURCHASE_PRICE_BUSY",
                    new { FailureDetails = Array.Empty<object>() }
                )
            );
        var controller = CreateCopyStoreDataController(service.Object);

        var response = await controller.CopyStoreData(new CopyStoreDataDto
        {
            SourceStoreCode = "1001",
            TargetStoreCodes = new List<string> { "1002" },
        });

        var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(response);
        var payload = Assert.IsType<ApiResponse<CopyStoreDataResultDto>>(conflict.Value);
        Assert.Equal("SET_CHILD_PURCHASE_PRICE_BUSY", payload.ErrorCode);
        Assert.NotNull(payload.Details);
        service.VerifyAll();
    }

    [Fact]
    public void CopyStoreData服务_保留已提交批次并在SSE异常后完成Channel()
    {
        var product = File.ReadAllText(Path.Combine(ApiRoot, "StoreProductPriceReactService.cs"));

        Assert.Contains("FailureDetails = failureDetails", product);
        Assert.Contains("ErrorCode = \"PARTIAL_SUCCESS\"", product);
        Assert.Contains("var allBusy = failures.All(result => result.IsBusy);", product);
        Assert.Contains("EventType = \"error\"", product);
        Assert.Contains("progressChannel.Writer.TryComplete();", product);
        Assert.Contains("}, CancellationToken.None);", product);
    }

    private static BlazorApp.Api.Controllers.React.ReactStoreProductPricesController CreateCopyStoreDataController(
        IStoreProductPriceReactService service
    )
    {
        var controller = new BlazorApp.Api.Controllers.React.ReactStoreProductPricesController(
            service,
            Mock.Of<IStoreRetailPriceReactService>(),
            Mock.Of<BlazorApp.Api.Interfaces.IUserService>(),
            NullLogger<BlazorApp.Api.Controllers.React.ReactStoreProductPricesController>.Instance,
            Mock.Of<IStorePriceTransferJobService>()
        );
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        };
        return controller;
    }
}
