using System.Reflection;
using System.Runtime.CompilerServices;
using System.ComponentModel.DataAnnotations;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierInvoiceHqProductSyncTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;

    public LocalSupplierInvoiceHqProductSyncTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = CreateSqlSugarClient(_localConnection.ConnectionString);
        _hqDb = CreateSqlSugarClient(_hqConnection.ConnectionString);

        _localDb.CodeFirst.InitTables(
            typeof(Store),
            typeof(Product),
            typeof(DomesticProduct),
            typeof(StoreRetailPrice),
            typeof(StoreLocalSupplierInvoice),
            typeof(StoreLocalSupplierInvoiceDetails)
        );
        _hqDb.CodeFirst.InitTables(
            typeof(DIC_商品信息字典表),
            typeof(DIC_商品零售价表)
        );
    }

    [Fact]
    public void EnsureHqProducts_后端契约与前端一致()
    {
        Assert.Null(typeof(UpdateToStorePricesRequest).GetProperty("UpdateHqProduct"));
        Assert.True(typeof(UpdateToStorePricesResultDto).IsSubclassOf(typeof(BatchResultDto)));
        Assert.NotNull(typeof(EnsureHqProductsRequest).GetProperty("DetailGuids"));
        Assert.NotNull(typeof(EnsureHqProductsRequest).GetProperty("TargetStoreCodes"));
        Assert.NotNull(typeof(EnsureHqProductsResult).GetProperty("HqPurchasePricesUpdated"));
        AssertRequiredProperty<UpdateHqProductsRequest>("DetailGuids");
        AssertRequiredProperty<UpdateHqProductsRequest>("TargetStoreCodes");
        AssertRequiredProperty<UpdateHqProductsRequest>("UpdateFields");

        var method = typeof(ReactLocalSupplierInvoicesController).GetMethod("EnsureHqProducts");
        Assert.NotNull(method);

        var route = Assert.Single(method!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Equal("{invoiceGuid}/details/ensure-hq-products", route.Template);

        var auth = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(Permissions.LocalPurchase.Edit, auth.Policy);

        var updateMethod = typeof(ReactLocalSupplierInvoicesController).GetMethod("UpdateHqProducts");
        Assert.NotNull(updateMethod);

        var updateRoute = Assert.Single(updateMethod!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Equal("{invoiceGuid}/details/update-hq-products", updateRoute.Template);

        var updatePolicies = updateMethod
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(x => x.Policy)
            .ToArray();
        Assert.Contains(Permissions.LocalPurchase.Edit, updatePolicies);
        Assert.Contains(Permissions.LocalPurchase.PushToHq, updatePolicies);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_空请求字段_返回稳定校验错误()
    {
        var service = CreateSyncService();

        var nullRequestResult = await service.UpdateHqProductsAsync("invoice-1", null, "tester");
        Assert.False(nullRequestResult.Success);
        Assert.Equal("VALIDATION_ERROR", nullRequestResult.Code);

        var nullDetailResult = await service.UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = null!,
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields { UpdateRetailPrice = true },
            },
            "tester"
        );
        Assert.False(nullDetailResult.Success);
        Assert.Equal("VALIDATION_ERROR", nullDetailResult.Code);

        var nullFieldsResult = await service.UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = null!,
            },
            "tester"
        );
        Assert.False(nullFieldsResult.Success);
        Assert.Equal("VALIDATION_ERROR", nullFieldsResult.Code);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_只更新勾选字段_未勾选字段保持不变()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedHqPriceAsync("S02", "P-001", 6m, 12m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-1",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            PurchasePrice = 8m,
            RetailPrice = 18m,
            AutoPricing = true,
            IsSpecialProduct = true,
            DiscountRate = 0.25m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdateIsAutoPricing = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.Updated);
        Assert.Equal(1, result.Data.HqAutoPricingUpdated);
        Assert.Equal(0, result.Data.HqPurchasePricesUpdated);
        Assert.Equal(0, result.Data.HqRetailPricesUpdated);
        Assert.Equal(0, result.Data.HqSpecialProductsUpdated);
        Assert.Equal(0, result.Data.HqDiscountRatesUpdated);

        var hqS01 = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001");
        var hqS02 = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S02" && x.H商品编码 == "P-001");

        Assert.True(hqS01.H是否自动定价);
        Assert.Equal(5m, hqS01.H进货价);
        Assert.Equal(10m, hqS01.H分店零售价);
        Assert.False(hqS01.H是否特殊商品);
        Assert.Equal(0m, hqS01.H折扣率);
        Assert.False(hqS02.H是否自动定价);
        Assert.Equal(6m, hqS02.H进货价);
        Assert.Equal(12m, hqS02.H分店零售价);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_更新HQ价格但不回写本单明细上次进货价()
    {
        await SeedStoreAsync("S01", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-no-writeback",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            PurchasePrice = 8m,
            RetailPrice = 18m,
            LastPurchasePrice = 3.21m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-no-writeback" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.HqPurchasePricesUpdated);

        var hqPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001");
        Assert.Equal(8m, hqPrice.H进货价);

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .FirstAsync(x => x.DetailGUID == "detail-no-writeback");
        Assert.Equal(3.21m, detail.LastPurchasePrice);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_目标分店HQ价格不存在_插入时使用更新字段完整建价()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-missing-price",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            PurchasePrice = 8m,
            RetailPrice = 18m,
            AutoPricing = true,
            IsSpecialProduct = false,
            DiscountRate = 0.10m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-missing-price" },
                TargetStoreCodes = new List<string> { "S01", "S02" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                    UpdateRetailPrice = true,
                    UpdateIsAutoPricing = true,
                    UpdateIsSpecialProduct = true,
                    UpdateDiscountRate = true,
                    PurchasePrice = 9m,
                    RetailPrice = 19m,
                    IsAutoPricing = false,
                    IsSpecialProduct = true,
                    DiscountRate = 0.30m,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(2, result.Data!.Updated);

        var existingPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001");
        Assert.Equal(9m, existingPrice.H进货价);
        Assert.Equal(19m, existingPrice.H分店零售价);
        Assert.False(existingPrice.H是否自动定价);
        Assert.True(existingPrice.H是否特殊商品);
        Assert.Equal(0.30m, existingPrice.H折扣率);

        var insertedPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S02" && x.H商品编码 == "P-001");
        Assert.Equal(9m, insertedPrice.H进货价);
        Assert.Equal(19m, insertedPrice.H分店零售价);
        Assert.False(insertedPrice.H是否自动定价);
        Assert.True(insertedPrice.H是否特殊商品);
        Assert.Equal(0.30m, insertedPrice.H折扣率);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_数值为0时跳过且布尔False仍然写入()
    {
        await SeedStoreAsync("S01", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-zero-skip",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            PurchasePrice = 0m,
            RetailPrice = 0m,
            AutoPricing = false,
            IsSpecialProduct = false,
            DiscountRate = 0m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-zero-skip" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                    UpdateRetailPrice = true,
                    UpdateIsAutoPricing = true,
                    UpdateIsSpecialProduct = true,
                    UpdateDiscountRate = true,
                },
            },
            "tester"
        );

        var hqPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001");

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.Updated);
        Assert.Equal(0, result.Data.HqPurchasePricesUpdated);
        Assert.Equal(0, result.Data.HqRetailPricesUpdated);
        Assert.Equal(1, result.Data.HqAutoPricingUpdated);
        Assert.Equal(1, result.Data.HqSpecialProductsUpdated);
        Assert.Equal(0, result.Data.HqDiscountRatesUpdated);
        Assert.Equal(5m, hqPrice.H进货价);
        Assert.Equal(10m, hqPrice.H分店零售价);
        Assert.False(hqPrice.H是否自动定价);
        Assert.False(hqPrice.H是否特殊商品);
        Assert.Equal(0m, hqPrice.H折扣率);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_自动定价为空时按否写入HQ价格()
    {
        await SeedStoreAsync("S01", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-null-auto-pricing",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            PurchasePrice = 8m,
            RetailPrice = 18m,
            AutoPricing = null,
            IsDeleted = false,
        });

        await _hqDb.Updateable<DIC_商品零售价表>()
            .SetColumns(x => x.H是否自动定价 == true)
            .Where(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001")
            .ExecuteCommandAsync();

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-null-auto-pricing" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdateIsAutoPricing = true,
                },
            },
            "tester"
        );

        var hqPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001");

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.Updated);
        Assert.Equal(0, result.Data.Skipped);
        Assert.Equal(1, result.Data.HqAutoPricingUpdated);
        Assert.False(hqPrice.H是否自动定价);
        Assert.DoesNotContain(result.Data.Errors, error => error.Message.Contains("自动定价为空"));
    }

    [Fact]
    public async Task UpdateHqProductsAsync_未选择字段_拒绝写入()
    {
        await SeedStoreAsync("S01", true);

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields(),
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Equal(0, result.Data?.Updated ?? 0);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_货号仅大小写不同且已存在时_绑定已有商品不重复创建()
    {
        await SeedStoreAsync("S01", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedLocalPriceAsync("S01", "P-001", 5m, 10m);
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-case",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "item-old",
            Barcode = "930000001111",
            ProductName = "大小写绑定测试",
            PurchasePrice = 7m,
            RetailPrice = 17m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-case" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(0, result.Data!.HbwebCreated);
        Assert.Equal(1, await _localDb.Queryable<Product>().CountAsync());

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .FirstAsync(x => x.DetailGUID == "detail-case");
        Assert.True(string.IsNullOrWhiteSpace(detail.ProductCode));

        var hqPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001");
        Assert.Equal(7m, hqPrice.H进货价);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_明细商品编码失效但货号已存在_绑定已有本地商品不重复创建()
    {
        await SeedStoreAsync("S01", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedLocalPriceAsync("S01", "P-001", 5m, 10m);
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-stale-code",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "STALE-PRODUCT",
            ItemNumber = "item-old",
            Barcode = "930000001111",
            ProductName = "失效编码绑定测试",
            PurchasePrice = 7m,
            RetailPrice = 17m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-stale-code" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(0, result.Data!.HbwebCreated);
        Assert.Equal(1, await _localDb.Queryable<Product>().CountAsync());

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .FirstAsync(x => x.DetailGUID == "detail-stale-code");
        Assert.Equal("STALE-PRODUCT", detail.ProductCode);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_Hq已有大小写货号但编码不同_复用Hq商品不重复创建()
    {
        await SeedStoreAsync("S01", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedLocalPriceAsync("S01", "P-001", 5m, 10m);
        await SeedHqProductAsync("P-HQ-OLD", 5m, 10m, itemNumber: "ITEM-OLD", barcode: "930000000000");
        await SeedHqPriceAsync("S01", "P-HQ-OLD", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-hq-case",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            ItemNumber = "item-old",
            Barcode = "930000001111",
            ProductName = "HQ大小写绑定测试",
            PurchasePrice = 7m,
            RetailPrice = 17m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-hq-case" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(0, result.Data!.HqCreated);
        Assert.Equal(1, result.Data.HqExisting);
        Assert.Equal(1, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());

        var oldHqPrice = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-HQ-OLD");
        Assert.Equal(7m, oldHqPrice.H进货价);

        var newCodePriceCount = await _hqDb.Queryable<DIC_商品零售价表>()
            .CountAsync(x => x.H商品编码 == "P-001");
        Assert.Equal(0, newCodePriceCount);
    }

    [Fact]
    public async Task UpdateHqProductsAsync_HQ商品不存在_新建商品并为所有启用分店处理价格()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedStoreAsync("S03", false);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedDomesticProductAsync("P-001", "CN-SUP01");
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-new-hq",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            ItemNumber = "ITEM-NEW-HQ",
            Barcode = "930000001234",
            ProductName = "HQ不存在测试",
            PurchasePrice = 7.50m,
            RetailPrice = 15.00m,
            AutoPricing = true,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-new-hq" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.HqCreated);
        Assert.Equal(2, result.Data.Updated);

        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>()
            .FirstAsync(x => x.H商品编码 == "P-001");
        Assert.Equal("P-001", hqProduct.H商品编码);
        Assert.Equal("CN-SUP01", hqProduct.CBP供应商编码);

        var hqPrices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(x => x.H商品编码 == "P-001")
            .OrderBy(x => x.H分店代码)
            .ToListAsync();
        Assert.Equal(new[] { "S01", "S02" }, hqPrices.Select(x => x.H分店代码).ToArray());
        Assert.All(hqPrices, price => Assert.Equal(7.50m, price.H进货价));
        Assert.DoesNotContain(hqPrices, price => price.H分店代码 == "S03");
    }

    [Fact]
    public async Task UpdateHqProductsAsync_本地商品不存在_新建本地商品并为所有启用分店创建本地价格()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedStoreAsync("S03", false);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-new-local-product",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-NEW-LOCAL",
            Barcode = "930000009999",
            ProductName = "本地不存在测试",
            PurchasePrice = 4.20m,
            RetailPrice = 9.90m,
            AutoPricing = false,
            IsSpecialProduct = true,
            DiscountRate = 0.15m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-new-local-product" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.HbwebCreated);

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .FirstAsync(x => x.DetailGUID == "detail-new-local-product");
        Assert.True(string.IsNullOrWhiteSpace(detail.ProductCode));

        var product = await _localDb.Queryable<Product>()
            .FirstAsync(x => x.ItemNumber == "ITEM-NEW-LOCAL");
        Assert.Equal("ITEM-NEW-LOCAL", product.ItemNumber);
        Assert.Equal("930000009999", product.Barcode);

        var localPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == product.ProductCode)
            .OrderBy(x => x.StoreCode)
            .ToListAsync();
        Assert.Equal(new[] { "S01", "S02" }, localPrices.Select(x => x.StoreCode).ToArray());
        Assert.DoesNotContain(localPrices, price => price.StoreCode == "S03");
        Assert.All(localPrices, price =>
        {
            Assert.Equal(4.20m, price.PurchasePrice);
            Assert.Equal(9.90m, price.StoreRetailPriceValue);
            Assert.Equal(0.15m, price.DiscountRate);
            Assert.False(price.IsAutoPricing);
            Assert.True(price.IsSpecialProduct);
            Assert.Equal($"{price.StoreCode}{product.ProductCode}", price.StoreProductCode);
        });
    }

    [Fact]
    public async Task UpdateHqProductsAsync_本地商品已存在_不更新本地分店价格()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedLocalPriceAsync("S01", "P-001", 5m, 10m);
        await SeedLocalPriceAsync("S02", "P-001", 6m, 12m);
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-existing-local-product",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            ItemNumber = "ITEM-NEW",
            Barcode = "930000000002",
            ProductName = "已有本地商品",
            PurchasePrice = 8m,
            RetailPrice = 18m,
            AutoPricing = true,
            IsSpecialProduct = true,
            DiscountRate = 0.05m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().UpdateHqProductsAsync(
            "invoice-1",
            new UpdateHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-existing-local-product" },
                TargetStoreCodes = new List<string> { "S01" },
                UpdateFields = new UpdateToStorePricesFields
                {
                    UpdatePurchasePrice = true,
                    UpdateRetailPrice = true,
                    UpdateDiscountRate = true,
                    UpdateIsAutoPricing = true,
                    UpdateIsSpecialProduct = true,
                },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(0, result.Data!.HbwebCreated);

        var localS01 = await _localDb.Queryable<StoreRetailPrice>()
            .FirstAsync(x => x.StoreCode == "S01" && x.ProductCode == "P-001");
        var localS02 = await _localDb.Queryable<StoreRetailPrice>()
            .FirstAsync(x => x.StoreCode == "S02" && x.ProductCode == "P-001");
        Assert.Equal(5m, localS01.PurchasePrice);
        Assert.Equal(10m, localS01.StoreRetailPriceValue);
        Assert.Equal(6m, localS02.PurchasePrice);
        Assert.Equal(12m, localS02.StoreRetailPriceValue);
    }

    [Fact]
    public async Task EnsureHqProductsAsync_缺本地和HQ商品_新建商品并为所有启用分店创建价格()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedStoreAsync("S03", false);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-1",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-1",
            Barcode = "930000000001",
            ProductName = "测试商品",
            PurchasePrice = 4.20m,
            RetailPrice = 9.90m,
            AutoPricing = false,
            IsSpecialProduct = true,
            DiscountRate = 0.15m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().EnsureHqProductsAsync(
            "invoice-1",
            new EnsureHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S01" },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.HbwebCreated);
        Assert.Equal(1, result.Data.HqCreated);
        Assert.Equal(0, result.Data.Failed);

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .FirstAsync(x => x.DetailGUID == "detail-1");
        Assert.False(string.IsNullOrWhiteSpace(detail.ProductCode));

        var localPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == detail.ProductCode)
            .OrderBy(x => x.StoreCode)
            .ToListAsync();
        Assert.Equal(new[] { "S01", "S02" }, localPrices.Select(x => x.StoreCode).ToArray());
        Assert.All(localPrices, price => Assert.Equal(4.20m, price.PurchasePrice));
        Assert.DoesNotContain(localPrices, price => price.StoreCode == "S03");

        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>()
            .FirstAsync(x => x.H商品编码 == detail.ProductCode);
        Assert.Equal("ITEM-1", hqProduct.H货号);
        Assert.Equal("930000000001", hqProduct.H主条形码);
        Assert.Equal("detail-1", hqProduct.H进货单详情GUID);
        Assert.Equal(string.Empty, hqProduct.CBP供应商编码);

        var hqPrices = await _hqDb.Queryable<DIC_商品零售价表>()
            .Where(x => x.H商品编码 == detail.ProductCode)
            .OrderBy(x => x.H分店代码)
            .ToListAsync();
        Assert.Equal(new[] { "S01", "S02" }, hqPrices.Select(x => x.H分店代码).ToArray());
        Assert.All(hqPrices, price => Assert.Equal(9.90m, price.H分店零售价));
    }

    [Fact]
    public async Task EnsureHqProductsAsync_已有商品_只更新请求目标分店()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedDomesticProductAsync("P-001", "CN-SUP01");
        await SeedLocalPriceAsync("S01", "P-001", 5m, 10m);
        await SeedLocalPriceAsync("S02", "P-001", 6m, 12m);
        await SeedHqProductAsync("P-001", 5m, 10m);
        await _hqDb.Updateable<DIC_商品信息字典表>()
            .SetColumns(row => new DIC_商品信息字典表 { CBP供应商编码 = "200" })
            .Where(row => row.H商品编码 == "P-001")
            .ExecuteCommandAsync();
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedHqPriceAsync("S02", "P-001", 6m, 12m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-1",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ProductCode = "P-001",
            ItemNumber = "ITEM-NEW",
            Barcode = "930000000002",
            ProductName = "已存在商品",
            PurchasePrice = 8m,
            RetailPrice = 18m,
            AutoPricing = true,
            IsSpecialProduct = false,
            DiscountRate = 0.05m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().EnsureHqProductsAsync(
            "invoice-1",
            new EnsureHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S01" },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(1, result.Data!.HqExisting);
        Assert.Equal(1, result.Data.HqPurchasePricesUpdated);

        var localS01 = await _localDb.Queryable<StoreRetailPrice>()
            .FirstAsync(x => x.StoreCode == "S01" && x.ProductCode == "P-001");
        var localS02 = await _localDb.Queryable<StoreRetailPrice>()
            .FirstAsync(x => x.StoreCode == "S02" && x.ProductCode == "P-001");
        Assert.Equal(8m, localS01.PurchasePrice);
        Assert.Equal(18m, localS01.StoreRetailPriceValue);
        Assert.Equal(6m, localS02.PurchasePrice);
        Assert.Equal(12m, localS02.StoreRetailPriceValue);

        var product = await _localDb.Queryable<Product>()
            .FirstAsync(x => x.ProductCode == "P-001");
        Assert.Equal("ITEM-OLD", product.ItemNumber);
        Assert.Equal("930000000000", product.Barcode);
        Assert.Equal(5m, product.PurchasePrice);
        Assert.Equal(10m, product.RetailPrice);

        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>()
            .FirstAsync(x => x.H商品编码 == "P-001");
        Assert.Equal("ITEM-OLD", hqProduct.H货号);
        Assert.Equal("930000000000", hqProduct.H主条形码);
        Assert.Equal(5m, hqProduct.H进货价);
        Assert.Equal(10m, hqProduct.H零售价);

        var hqS01 = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S01" && x.H商品编码 == "P-001");
        var hqS02 = await _hqDb.Queryable<DIC_商品零售价表>()
            .FirstAsync(x => x.H分店代码 == "S02" && x.H商品编码 == "P-001");
        Assert.Equal(8m, hqS01.H进货价);
        Assert.Equal(18m, hqS01.H分店零售价);
        Assert.Equal(6m, hqS02.H进货价);
        Assert.Equal(12m, hqS02.H分店零售价);
        Assert.Equal("CN-SUP01", hqProduct.CBP供应商编码);
    }

    [Fact]
    public async Task EnsureHqProductsAsync_ProductCode为空但货号已存在_绑定已有商品不重复创建()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedExistingProductAsync("P-001", "SUP01");
        await SeedLocalPriceAsync("S01", "P-001", 5m, 10m);
        await SeedLocalPriceAsync("S02", "P-001", 6m, 12m);
        await SeedHqProductAsync("P-001", 5m, 10m);
        await SeedHqPriceAsync("S01", "P-001", 5m, 10m);
        await SeedHqPriceAsync("S02", "P-001", 6m, 12m);
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-1",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-OLD",
            Barcode = "930000009999",
            ProductName = "重复货号明细",
            PurchasePrice = 7m,
            RetailPrice = 17m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().EnsureHqProductsAsync(
            "invoice-1",
            new EnsureHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S01" },
            },
            "tester"
        );

        Assert.True(result.Success, BuildFailureMessage(result));
        Assert.Equal(0, result.Data!.HbwebCreated);
        Assert.Equal(1, await _localDb.Queryable<Product>().CountAsync());

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .FirstAsync(x => x.DetailGUID == "detail-1");
        Assert.Equal("P-001", detail.ProductCode);

        var localS01 = await _localDb.Queryable<StoreRetailPrice>()
            .FirstAsync(x => x.StoreCode == "S01" && x.ProductCode == "P-001");
        var localS02 = await _localDb.Queryable<StoreRetailPrice>()
            .FirstAsync(x => x.StoreCode == "S02" && x.ProductCode == "P-001");
        Assert.Equal(7m, localS01.PurchasePrice);
        Assert.Equal(17m, localS01.StoreRetailPriceValue);
        Assert.Equal(6m, localS02.PurchasePrice);
        Assert.Equal(12m, localS02.StoreRetailPriceValue);
    }

    [Fact]
    public async Task EnsureHqProductsAsync_目标分店不存在或停用_拒绝写入()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", false);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-1",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-1",
            Barcode = "930000000004",
            ProductName = "停用分店测试",
            PurchasePrice = 4m,
            RetailPrice = 8m,
            IsDeleted = false,
        });

        var result = await CreateSyncService().EnsureHqProductsAsync(
            "invoice-1",
            new EnsureHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S02" },
            },
            "tester"
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_TARGET_STORE", result.ErrorCode);
        Assert.Equal(0, await _localDb.Queryable<Product>().CountAsync());
        Assert.Equal(0, await _localDb.Queryable<StoreRetailPrice>().CountAsync());
    }

    [Fact]
    public async Task EnsureHqProductsAsync_HQ失败时_本地写入保留并返回错误()
    {
        await SeedStoreAsync("S01", true);
        await SeedStoreAsync("S02", true);
        await SeedInvoiceAsync("invoice-1", "S01", "SUP01");
        await SeedDetailAsync(new StoreLocalSupplierInvoiceDetails
        {
            DetailGUID = "detail-1",
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            ItemNumber = "ITEM-FAIL",
            Barcode = "930000000003",
            ProductName = "HQ失败本地保留",
            PurchasePrice = 3.30m,
            RetailPrice = 6.60m,
            IsDeleted = false,
        });
        await _hqDb.Ado.ExecuteCommandAsync("DROP TABLE \"DIC_商品信息字典表\"");

        var result = await CreateSyncService().EnsureHqProductsAsync(
            "invoice-1",
            new EnsureHqProductsRequest
            {
                DetailGuids = new List<string> { "detail-1" },
                TargetStoreCodes = new List<string> { "S01" },
            },
            "tester"
        );

        var failedResult = Assert.IsType<EnsureHqProductsResult>(result.Details);
        Assert.False(result.Success);
        Assert.Equal("HQ_SYNC_PARTIAL_FAILED", result.ErrorCode);
        Assert.Equal(1, failedResult.HbwebCreated);
        Assert.Equal(1, failedResult.Failed);
        Assert.NotEmpty(failedResult.Errors);

        var detail = await _localDb.Queryable<StoreLocalSupplierInvoiceDetails>()
            .FirstAsync(x => x.DetailGUID == "detail-1");
        var localPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == detail.ProductCode)
            .OrderBy(x => x.StoreCode)
            .ToListAsync();
        Assert.Equal(new[] { "S01", "S02" }, localPrices.Select(x => x.StoreCode).ToArray());
    }

    private LocalSupplierInvoiceHqProductSyncService CreateSyncService()
    {
        return new LocalSupplierInvoiceHqProductSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb),
            NullLogger<LocalSupplierInvoiceHqProductSyncService>.Instance
        );
    }

    private static string BuildFailureMessage(ApiResponse<EnsureHqProductsResult> result)
    {
        var errors = result.Data?.Errors ?? (result.Details as EnsureHqProductsResult)?.Errors;
        return errors == null
            ? result.Message
            : $"{result.Message}: {string.Join("; ", errors.Select(x => $"{x.DetailGuid}/{x.StoreCode}: {x.Message}"))}";
    }

    private static string BuildFailureMessage(ApiResponse<UpdateHqProductsResult> result)
    {
        var errors = result.Data?.Errors ?? (result.Details as UpdateHqProductsResult)?.Errors;
        return errors == null
            ? result.Message
            : $"{result.Message}: {string.Join("; ", errors.Select(x => $"{x.DetailGuid}/{x.StoreCode}: {x.Message}"))}";
    }

    private async Task SeedStoreAsync(string storeCode, bool active)
    {
        await _localDb.Insertable(new Store
        {
            StoreGUID = $"store-{storeCode}",
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = active,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedInvoiceAsync(string invoiceGuid, string storeCode, string supplierCode)
    {
        await _localDb.Insertable(new StoreLocalSupplierInvoice
        {
            InvoiceGUID = invoiceGuid,
            StoreCode = storeCode,
            SupplierCode = supplierCode,
            InvoiceNo = "INV-001",
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDetailAsync(StoreLocalSupplierInvoiceDetails detail)
    {
        await _localDb.Insertable(detail).ExecuteCommandAsync();
    }

    private async Task SeedExistingProductAsync(
        string productCode,
        string supplierCode,
        string itemNumber = "ITEM-OLD",
        string barcode = "930000000000"
    )
    {
        await _localDb.Insertable(new Product
        {
            UUID = productCode,
            ProductCode = productCode,
            LocalSupplierCode = supplierCode,
            ItemNumber = itemNumber,
            Barcode = barcode,
            ProductName = "旧商品",
            PurchasePrice = 5m,
            RetailPrice = 10m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDomesticProductAsync(string productCode, string supplierCode)
    {
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            SupplierCode = supplierCode,
            ProductName = $"国内商品-{productCode}",
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedLocalPriceAsync(string storeCode, string productCode, decimal purchasePrice, decimal retailPrice)
    {
        await _localDb.Insertable(new StoreRetailPrice
        {
            UUID = $"{storeCode}-{productCode}",
            StoreCode = storeCode,
            ProductCode = productCode,
            StoreProductCode = $"{storeCode}{productCode}",
            SupplierCode = "SUP01",
            PurchasePrice = purchasePrice,
            StoreRetailPriceValue = retailPrice,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHqProductAsync(
        string productCode,
        decimal purchasePrice,
        decimal retailPrice,
        string itemNumber = "ITEM-OLD",
        string barcode = "930000000000"
    )
    {
        await _hqDb.Insertable(new DIC_商品信息字典表
        {
            HGUID = $"hq-product-{productCode}",
            H商品标签GUID = string.Empty,
            H商品分类码GUID = string.Empty,
            H供货商编码 = "SUP01",
            H商品编码 = productCode,
            H货号 = itemNumber,
            H主条形码 = barcode,
            H商品名称 = "旧商品",
            H大写名称 = "旧商品",
            H规格 = string.Empty,
            H单位 = string.Empty,
            H进货价 = purchasePrice,
            H零售价 = retailPrice,
            H商品图片 = string.Empty,
            H腾讯云图地址 = string.Empty,
            H使用状态 = true,
            H进货单主表GUID = string.Empty,
            H进货单详情GUID = string.Empty,
            CBP商品中文名称 = string.Empty,
            CBP供应商编码 = string.Empty,
            CBP商品分类码GUID = string.Empty,
            FGC_Creator = "seed",
            FGC_CreateDate = DateTime.UtcNow,
            FGC_LastModifier = "seed",
            FGC_LastModifyDate = DateTime.UtcNow,
            FGC_UpdateHelp = string.Empty,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHqPriceAsync(string storeCode, string productCode, decimal purchasePrice, decimal retailPrice)
    {
        await _hqDb.Insertable(new DIC_商品零售价表
        {
            ID = storeCode == "S01" ? 1 : 2,
            HGUID = $"hq-price-{storeCode}-{productCode}",
            H分店代码 = storeCode,
            H商品编码 = productCode,
            H分店商品编码 = $"{storeCode}{productCode}",
            H供应商编码 = "SUP01",
            H分店供应商编码 = $"{storeCode}SUP01",
            H进货价 = purchasePrice,
            H分店零售价 = retailPrice,
            H使用状态 = true,
            FGC_Creator = "seed",
            FGC_CreateDate = DateTime.UtcNow,
            FGC_LastModifier = "seed",
            FGC_LastModifyDate = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private static SqlSugarClient CreateSqlSugarClient(string connectionString)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(ISqlSugarClient db)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        typeof(HqSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static void AssertRequiredProperty<T>(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredAttribute>());
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hqConnection.Dispose();
        if (File.Exists(_localDbPath)) File.Delete(_localDbPath);
        if (File.Exists(_hqDbPath)) File.Delete(_hqDbPath);
    }
}
