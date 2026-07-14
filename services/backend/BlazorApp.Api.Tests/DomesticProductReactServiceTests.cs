using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class DomesticProductReactServiceTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hbSalesDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hbSalesDb;
    private readonly SqlSugarClient _hqDb;

    public DomesticProductReactServiceTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hbSalesConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hbSalesConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hbSalesDb = new SqlSugarScope(CreateConnectionConfig(_hbSalesConnection.ConnectionString));
        _hqDb = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(
            typeof(DomesticProduct),
            typeof(DomesticSetProduct),
            typeof(Product),
            typeof(ChinaSupplier)
        );
        _hbSalesDb.CodeFirst.InitTables(
            typeof(CPT_DIC_商品信息字典表),
            typeof(CPT_DIC_商品套装信息表)
        );
        _hqDb.CodeFirst.InitTables(typeof(DIC_商品信息字典表));
    }

    [Fact]
    public async Task HbwebProductNames_同货号不同供应商只更新请求供应商()
    {
        await _localDb.Insertable(new[]
        {
            new Product { UUID = "product-sup-a", ProductCode = "PC-SUP-A", LocalSupplierCode = "SUP-A", ItemNumber = "MK029", ProductName = "A 原名称", IsDeleted = false },
            new Product { UUID = "product-sup-b", ProductCode = "PC-SUP-B", LocalSupplierCode = "SUP-B", ItemNumber = "MK029", ProductName = "B 原名称", IsDeleted = false },
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "MK029", ProductName = "A 新名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.UpdatedCount);
        var products = await _localDb.Queryable<Product>().OrderBy(product => product.LocalSupplierCode).ToListAsync();
        Assert.Equal("A 新名称", products[0].ProductName);
        Assert.Equal("B 原名称", products[1].ProductName);
    }

    [Fact]
    public async Task HbwebProductNames_同一供应商复合键重复仍跳过()
    {
        await _localDb.Insertable(new[]
        {
            new Product { UUID = "product-dup-a-1", ProductCode = "PC-DUP-A-1", LocalSupplierCode = "SUP-A", ItemNumber = "MK032", ProductName = "原名称1", IsDeleted = false },
            new Product { UUID = "product-dup-a-2", ProductCode = "PC-DUP-A-2", LocalSupplierCode = "SUP-A", ItemNumber = "MK032", ProductName = "原名称2", IsDeleted = false },
            new Product { UUID = "product-dup-b", ProductCode = "PC-DUP-B", LocalSupplierCode = "SUP-B", ItemNumber = "MK032", ProductName = "B 原名称", IsDeleted = false },
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "MK032", ProductName = "新名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.UpdatedCount);
        Assert.Contains(result.Data.Errors, error => error.Contains("SUP-A/MK032"));
        var products = await _localDb.Queryable<Product>().OrderBy(product => product.ProductCode).ToListAsync();
        Assert.All(products, product => Assert.Contains("原名称", product.ProductName));
    }

    [Fact]
    public async Task HbwebProductNames_供应商代码不能为空()
    {
        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = " ", ItemNumber = "MK029", ProductName = "新名称" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("INVALID_HBWEB_PRODUCT_NAMES", result.ErrorCode);
        var details = Assert.IsType<BatchUpdateHbwebProductNamesResultDto>(result.Details);
        Assert.Contains(details.Errors, error => error.Contains("供应商代码不能为空"));
    }

    [Fact]
    public async Task HbwebProductNames_复合键按Trim和OrdinalIgnoreCase规范化()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "product-normalized",
            ProductCode = "PC-NORMALIZED",
            LocalSupplierCode = "sup-a",
            ItemNumber = "mk029",
            ProductName = "原名称",
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = " sup-a ", ItemNumber = " mk029 ", ProductName = "统一名称" },
                new() { SupplierCode = "SUP-A", ItemNumber = "MK029", ProductName = "统一名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.UpdatedCount);
        var product = await _localDb.Queryable<Product>().SingleAsync();
        Assert.Equal("统一名称", product.ProductName);
    }

    [Fact]
    public async Task HbwebProductNames_SyncToHq跨供应商同货号不同名称时两库写前失败()
    {
        await _localDb.Insertable(new[]
        {
            new Product { UUID = "product-hq-conflict-a", ProductCode = "PC-HQ-CONFLICT-A", LocalSupplierCode = "SUP-A", ItemNumber = "MK029", ProductName = "A 原名称", IsDeleted = false },
            new Product { UUID = "product-hq-conflict-b", ProductCode = "PC-HQ-CONFLICT-B", LocalSupplierCode = "SUP-B", ItemNumber = "MK029", ProductName = "B 原名称", IsDeleted = false },
        }).ExecuteCommandAsync();
        await SeedHqProductAsync(1, "MK029", "HQ 原名称");

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "MK029", ProductName = "名称 A" },
                new() { SupplierCode = "SUP-B", ItemNumber = "MK029", ProductName = "名称 B" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("DUPLICATE_HQ_ITEM_NUMBER_NAMES", result.ErrorCode);
        var hbwebProducts = await _localDb.Queryable<Product>().OrderBy(product => product.LocalSupplierCode).ToListAsync();
        Assert.Equal("A 原名称", hbwebProducts[0].ProductName);
        Assert.Equal("B 原名称", hbwebProducts[1].ProductName);
        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync();
        Assert.Equal("HQ 原名称", hqProduct.H商品名称);
    }

    [Fact]
    public async Task HbwebProductNames_SyncToHq跨供应商同货号同名称时HQ只处理一次()
    {
        await _localDb.Insertable(new[]
        {
            new Product { UUID = "product-hq-same-a", ProductCode = "PC-HQ-SAME-A", LocalSupplierCode = "SUP-A", ItemNumber = "MK040", ProductName = "A 原名称", IsDeleted = false },
            new Product { UUID = "product-hq-same-b", ProductCode = "PC-HQ-SAME-B", LocalSupplierCode = "SUP-B", ItemNumber = "MK040", ProductName = "B 原名称", IsDeleted = false },
        }).ExecuteCommandAsync();
        await SeedHqProductAsync(1, "MK040", "HQ 原名称");

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "MK040", ProductName = "统一名称" },
                new() { SupplierCode = "SUP-B", ItemNumber = "MK040", ProductName = "统一名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.UpdatedCount);
        Assert.Equal(1, result.Data.HqSyncResult!.UpdatedCount);
        Assert.All(await _localDb.Queryable<Product>().ToListAsync(), product => Assert.Equal("统一名称", product.ProductName));
        Assert.Equal("统一名称", (await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync()).H商品名称);
    }

    [Fact]
    public async Task HbwebProductNames_按货号更新ProductName且不改其它字段()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "product-hb001",
            ProductCode = "PC-HB001",
            LocalSupplierCode = "SUP-A",
            ItemNumber = "HB001",
            ProductName = "旧商品名",
            EnglishName = "Existing English",
            Barcode = "9300000000011",
            PurchasePrice = 1.23m,
            RetailPrice = 4.56m,
            ProductImage = "old.jpg",
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB001", ProductName = "NEW MASTER NAME" },
                new() { SupplierCode = "SUP-A", ItemNumber = "MISSING", ProductName = "MISSING NAME" },
            },
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data!.UpdatedCount);
        Assert.Equal(new[] { "MISSING" }, result.Data.MissingItemNumbers);

        var product = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "HB001");
        Assert.Equal("NEW MASTER NAME", product.ProductName);
        Assert.Equal("Existing English", product.EnglishName);
        Assert.Equal("9300000000011", product.Barcode);
        Assert.Equal(1.23m, product.PurchasePrice);
        Assert.Equal(4.56m, product.RetailPrice);
        Assert.Equal("old.jpg", product.ProductImage);
        Assert.Equal("System", product.UpdatedBy);
    }

    [Fact]
    public async Task HbwebProductNames_请求内同货号不同名称时不写库()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "product-conflict",
            ProductCode = "PC-CONFLICT",
            LocalSupplierCode = "SUP-A",
            ItemNumber = "HB-CONFLICT",
            ProductName = "原名称",
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-CONFLICT", ProductName = "NAME A" },
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-CONFLICT", ProductName = "NAME B" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("DUPLICATE_ITEM_NUMBER_NAMES", result.ErrorCode);

        var product = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "HB-CONFLICT");
        Assert.Equal("原名称", product.ProductName);
    }

    [Fact]
    public async Task HbwebProductNames_请求内大小写等价货号不同名称时两库都不写()
    {
        await SeedHbwebProductAsync("hb-case-conflict", "HBweb 原名称");
        await SeedHqProductAsync(1, "hb-case-conflict", "HQ 原名称");

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "hb-case-conflict", ProductName = "名称 A" },
                new() { SupplierCode = "sup-a", ItemNumber = "HB-CASE-CONFLICT", ProductName = "名称 B" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("DUPLICATE_ITEM_NUMBER_NAMES", result.ErrorCode);
        var hbwebProduct = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "hb-case-conflict");
        Assert.Equal("HBweb 原名称", hbwebProduct.ProductName);
        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync();
        Assert.Equal("HQ 原名称", hqProduct.H商品名称);
        Assert.Equal("seed", hqProduct.FGC_LastModifier);
    }

    [Fact]
    public async Task HbwebProductNames_请求内大小写等价货号同名称时只更新一次()
    {
        await SeedHbwebProductAsync("hb-case-same", "HBweb 原名称");
        await SeedHqProductAsync(1, "hb-case-same", "HQ 原名称");

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "hb-case-same", ProductName = "统一新名称" },
                new() { SupplierCode = "sup-a", ItemNumber = "HB-CASE-SAME", ProductName = "统一新名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.UpdatedCount);
        Assert.Empty(result.Data.MissingItemNumbers);
        Assert.Equal(1, result.Data.HqSyncResult!.UpdatedCount);
        Assert.Empty(result.Data.HqSyncResult.MissingItemNumbers);
        var hbwebProduct = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "hb-case-same");
        Assert.Equal("统一新名称", hbwebProduct.ProductName);
        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync();
        Assert.Equal("统一新名称", hqProduct.H商品名称);
    }

    [Fact]
    public async Task HbwebProductNames_请求内空货号或空名称时不写库()
    {
        await _localDb.Insertable(new Product
        {
            UUID = "product-invalid",
            ProductCode = "PC-INVALID",
            LocalSupplierCode = "SUP-A",
            ItemNumber = "HB-INVALID",
            ProductName = "原名称",
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-INVALID", ProductName = "NEW NAME" },
                new() { SupplierCode = "SUP-A", ItemNumber = " ", ProductName = "HAS NAME" },
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-NO-NAME", ProductName = " " },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("INVALID_HBWEB_PRODUCT_NAMES", result.ErrorCode);
        Assert.NotNull(result.Details);
        var details = Assert.IsType<BatchUpdateHbwebProductNamesResultDto>(result.Details);
        Assert.Contains(details.Errors, error => error.Contains("货号不能为空"));
        Assert.Contains(details.Errors, error => error.Contains("商品名称不能为空"));

        var product = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "HB-INVALID");
        Assert.Equal("原名称", product.ProductName);
    }

    [Fact]
    public async Task HbwebProductNames_Hbweb主表货号重复时跳过该货号()
    {
        await _localDb.Insertable(new[]
        {
            new Product { UUID = "product-dup-1", ProductCode = "PC-DUP-1", LocalSupplierCode = "SUP-A", ItemNumber = "HB-DUP", ProductName = "原名称1", IsDeleted = false },
            new Product { UUID = "product-dup-2", ProductCode = "PC-DUP-2", LocalSupplierCode = "SUP-A", ItemNumber = "HB-DUP", ProductName = "原名称2", IsDeleted = false },
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-DUP", ProductName = "NEW NAME" },
            },
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data!.UpdatedCount);
        Assert.Contains(result.Data.Errors, error => error.Contains("货号重复"));

        var products = await _localDb.Queryable<Product>().Where(p => p.ItemNumber == "HB-DUP").OrderBy(p => p.ProductCode).ToListAsync();
        Assert.Equal("原名称1", products[0].ProductName);
        Assert.Equal("原名称2", products[1].ProductName);
    }

    [Fact]
    public async Task HbwebProductNames_未开启HQ同步时不访问HQ数据库()
    {
        await SeedHbwebProductAsync("HB-NO-HQ", "原名称");
        var inaccessibleHqDb = new Mock<ISqlSugarClient>(MockBehavior.Strict).Object;

        var result = await CreateService(CreateHqSqlSugarContext(inaccessibleHqDb))
            .BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
            {
                SyncToHq = false,
                Products = new List<HbwebProductNameUpdateItemDto>
                {
                    new() { SupplierCode = "SUP-A", ItemNumber = "HB-NO-HQ", ProductName = "仅更新 HBweb" },
                },
            });

        Assert.True(result.Success);
        Assert.Null(result.Data!.HqSyncResult);
        var product = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "HB-NO-HQ");
        Assert.Equal("仅更新 HBweb", product.ProductName);
    }

    [Fact]
    public void HbwebProductNames_HQ查询使用可索引的货号裸列比较()
    {
        var source = File.ReadAllText(ResolveDomesticProductReactServicePath());

        var methodStart = source.IndexOf("BatchUpdateHbwebProductNamesAsync", StringComparison.Ordinal);
        var hbwebQueryStart = source.IndexOf("db.Queryable<Product>()", methodStart, StringComparison.Ordinal);
        var hbwebQueryEnd = source.IndexOf(".ToListAsync()", hbwebQueryStart, StringComparison.Ordinal);
        var hbwebQuery = source[hbwebQueryStart..hbwebQueryEnd];
        Assert.Contains("supplierCodes.Contains(product.LocalSupplierCode)", hbwebQuery);
        Assert.Contains("itemNumbers.Contains(product.ItemNumber)", hbwebQuery);
        Assert.DoesNotContain(".Trim()", hbwebQuery);

        Assert.Contains("private const int HqProductNameQueryChunkSize = 1000;", source);
        Assert.Contains("private const int HqProductNameUpdateChunkSize = 200;", source);
        Assert.Contains("hqItemNumbers.Chunk(HqProductNameQueryChunkSize)", source);
        Assert.Contains("hqProductsToUpdate.Chunk(HqProductNameUpdateChunkSize)", source);
        Assert.Contains("queryItemNumbers.Contains(product.H货号)", source);
        Assert.Contains("product.H供货商编码 == HqProductNameSupplierCode", source);
        Assert.Contains(".Select(product => new DIC_商品信息字典表", source);
        Assert.Contains("ID = product.ID", source);
        Assert.Contains("H供货商编码 = product.H供货商编码", source);
        Assert.Contains("H货号 = product.H货号", source);
        Assert.Contains("H商品名称 = product.H商品名称", source);
        Assert.DoesNotContain("product.H货号.Trim().ToUpper()", source);
        Assert.DoesNotContain("normalizedHqItemNumbers", source);

        var projectionStart = source.IndexOf(
            ".Select(product => new DIC_商品信息字典表",
            StringComparison.Ordinal
        );
        var projectionEnd = source.IndexOf(".ToListAsync()", projectionStart, StringComparison.Ordinal);
        var projection = source[projectionStart..projectionEnd];
        Assert.Equal(4, projection.Split("= product.", StringSplitOptions.None).Length - 1);

        var transactionStart = source.IndexOf(
            "var hqTransactionResult = await hqDb.Ado.UseTranAsync",
            StringComparison.Ordinal
        );
        var updateChunk = source.IndexOf(
            "hqProductsToUpdate.Chunk(HqProductNameUpdateChunkSize)",
            transactionStart,
            StringComparison.Ordinal
        );
        var transactionCheck = source.IndexOf(
            "if (!hqTransactionResult.IsSuccess)",
            updateChunk,
            StringComparison.Ordinal
        );
        Assert.True(transactionStart >= 0 && updateChunk > transactionStart && transactionCheck > updateChunk);
    }

    [Fact]
    public async Task HbwebProductNames_开启HQ同步时唯一匹配只更新名称和审计字段()
    {
        await SeedHbwebProductAsync("HB-HQ-001", "HBweb 原名称");
        var originalModifyDate = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var seededHqProduct = CreateHqProduct(1, "HB-HQ-001", "HQ 原名称");
        seededHqProduct.H大写名称 = "HQ UPPER NAME";
        seededHqProduct.H主条形码 = "9300000000001";
        seededHqProduct.H进货价 = 12.34m;
        seededHqProduct.H零售价 = 56.78m;
        seededHqProduct.H商品图片 = "hq-old.jpg";
        seededHqProduct.FGC_LastModifyDate = originalModifyDate;
        await _hqDb.Insertable(seededHqProduct).ExecuteCommandAsync();

        var before = DateTime.Now;
        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = " SUP-A ", ItemNumber = " HB-HQ-001 ", ProductName = " HQ 新名称 " },
            },
        });

        Assert.True(result.Success);
        var hqResult = Assert.IsType<HqProductNameSyncResultDto>(result.Data!.HqSyncResult);
        Assert.True(hqResult.Success);
        Assert.Equal(1, hqResult.UpdatedCount);
        Assert.Equal(0, hqResult.UnchangedCount);

        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync();
        Assert.Equal("HQ 新名称", hqProduct.H商品名称);
        Assert.Equal("HBweb", hqProduct.FGC_LastModifier);
        Assert.InRange(hqProduct.FGC_LastModifyDate, before, DateTime.Now);
        Assert.Equal("HQ UPPER NAME", hqProduct.H大写名称);
        Assert.Equal("9300000000001", hqProduct.H主条形码);
        Assert.Equal(12.34m, hqProduct.H进货价);
        Assert.Equal(56.78m, hqProduct.H零售价);
        Assert.Equal("hq-old.jpg", hqProduct.H商品图片);
        Assert.Equal("seed", hqProduct.FGC_Creator);
        Assert.Equal(new DateTime(2023, 1, 1), hqProduct.FGC_CreateDate);
    }

    [Fact]
    public async Task HbwebProductNames_HQ名称相同时计入无变化()
    {
        await SeedHbwebProductAsync("HB-HQ-SAME", "原名称");
        await SeedHqProductAsync(1, "HB-HQ-SAME", "相同名称");

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-HQ-SAME", ProductName = "相同名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.HqSyncResult!.UpdatedCount);
        Assert.Equal(1, result.Data.HqSyncResult.UnchangedCount);
        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync();
        Assert.Equal("seed", hqProduct.FGC_LastModifier);
    }

    [Fact]
    public async Task HbwebProductNames_HQ缺失货号时记录但不创建()
    {
        await SeedHbwebProductAsync("HB-HQ-MISSING", "原名称");

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-HQ-MISSING", ProductName = "新名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(new[] { "HB-HQ-MISSING" }, result.Data!.HqSyncResult!.MissingItemNumbers);
        Assert.Equal(0, await _hqDb.Queryable<DIC_商品信息字典表>().CountAsync());
    }

    [Fact]
    public async Task HbwebProductNames_HQ同货号重复时跳过并记录错误()
    {
        await SeedHbwebProductAsync("HB-HQ-DUP", "原名称");
        await SeedHqProductAsync(1, "HB-HQ-DUP", "HQ 原名称1");
        await SeedHqProductAsync(2, "HB-HQ-DUP", "HQ 原名称2");

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-HQ-DUP", ProductName = "新名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Contains(result.Data!.HqSyncResult!.Errors, error => error.Contains("货号重复") && error.Contains("HB-HQ-DUP"));
        var hqProducts = await _hqDb.Queryable<DIC_商品信息字典表>().OrderBy(p => p.ID).ToListAsync();
        Assert.Equal("HQ 原名称1", hqProducts[0].H商品名称);
        Assert.Equal("HQ 原名称2", hqProducts[1].H商品名称);
    }

    [Fact]
    public async Task HbwebProductNames_HQ同货号不同供应商只更新固定供应商200()
    {
        await SeedHbwebProductAsync("HB-HQ-SUPPLIER", "原名称");
        var supplier200 = CreateHqProduct(1, "HB-HQ-SUPPLIER", "200 原名称");
        supplier200.H供货商编码 = "200";
        var supplier201 = CreateHqProduct(2, "HB-HQ-SUPPLIER", "201 原名称");
        supplier201.H供货商编码 = "201";
        await _hqDb.Insertable(new[] { supplier200, supplier201 }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-HQ-SUPPLIER", ProductName = "HQ 新名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.HqSyncResult!.UpdatedCount);
        var hqProducts = await _hqDb.Queryable<DIC_商品信息字典表>().OrderBy(product => product.ID).ToListAsync();
        Assert.Equal("HQ 新名称", hqProducts[0].H商品名称);
        Assert.Equal("201 原名称", hqProducts[1].H商品名称);
    }

    [Fact]
    public async Task HbwebProductNames_HQ固定供应商200内部同货号重复仍跳过()
    {
        await SeedHbwebProductAsync("HB-HQ-200-DUP", "原名称");
        var supplier200First = CreateHqProduct(1, "HB-HQ-200-DUP", "200 原名称1");
        supplier200First.H供货商编码 = "200";
        var supplier200Second = CreateHqProduct(2, "HB-HQ-200-DUP", "200 原名称2");
        supplier200Second.H供货商编码 = "200";
        var supplier201 = CreateHqProduct(3, "HB-HQ-200-DUP", "201 原名称");
        supplier201.H供货商编码 = "201";
        await _hqDb.Insertable(new[] { supplier200First, supplier200Second, supplier201 }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-HQ-200-DUP", ProductName = "HQ 新名称" },
            },
        });

        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.HqSyncResult!.UpdatedCount);
        Assert.Contains(result.Data.HqSyncResult.Errors, error => error.Contains("200/HB-HQ-200-DUP"));
        var hqProducts = await _hqDb.Queryable<DIC_商品信息字典表>().OrderBy(product => product.ID).ToListAsync();
        Assert.Equal("200 原名称1", hqProducts[0].H商品名称);
        Assert.Equal("200 原名称2", hqProducts[1].H商品名称);
        Assert.Equal("201 原名称", hqProducts[2].H商品名称);
    }

    [Fact]
    public async Task HbwebProductNames_HQ数据库异常时保留Hbweb更新并返回部分失败()
    {
        await SeedHbwebProductAsync("HB-HQ-ERROR", "原名称");
        _hqDb.DbMaintenance.DropTable<DIC_商品信息字典表>();

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-HQ-ERROR", ProductName = "HBweb 已更新" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("HQ_PRODUCT_NAME_SYNC_FAILED", result.ErrorCode);
        var data = Assert.IsType<BatchUpdateHbwebProductNamesResultDto>(result.Data);
        Assert.Same(data, result.Details);
        Assert.Equal(1, data.UpdatedCount);
        Assert.False(data.HqSyncResult!.Success);
        Assert.NotEmpty(data.HqSyncResult.Errors);
        var product = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "HB-HQ-ERROR");
        Assert.Equal("HBweb 已更新", product.ProductName);
    }

    [Fact]
    public async Task HbwebProductNames_HQ更新事务失败时返回部分失败()
    {
        await SeedHbwebProductAsync("HB-HQ-TRAN", "原名称");
        await SeedHqProductAsync(1, "HB-HQ-TRAN", "HQ 原名称");
        _hqDb.Ado.ExecuteCommand(
            "CREATE TRIGGER fail_hq_product_name_update BEFORE UPDATE ON \"DIC_商品信息字典表\" "
            + "BEGIN SELECT RAISE(ABORT, 'forced HQ update failure'); END;"
        );

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-HQ-TRAN", ProductName = "HBweb 已更新" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("HQ_PRODUCT_NAME_SYNC_FAILED", result.ErrorCode);
        Assert.False(result.Data!.HqSyncResult!.Success);
        Assert.Equal(new[] { "HQ 商品名称同步失败，请稍后重试" }, result.Data.HqSyncResult.Errors);
        Assert.DoesNotContain(result.Data.HqSyncResult.Errors, error => error.Contains("forced HQ update failure"));
        var product = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "HB-HQ-TRAN");
        Assert.Equal("HBweb 已更新", product.ProductName);
        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync();
        Assert.Equal("HQ 原名称", hqProduct.H商品名称);
    }

    [Fact]
    public async Task HbwebProductNames_Hbweb更新事务失败时不进入HQ同步()
    {
        await SeedHbwebProductAsync("HB-LOCAL-TRAN", "HBweb 原名称");
        await SeedHqProductAsync(1, "HB-LOCAL-TRAN", "HQ 原名称");
        _localDb.Ado.ExecuteCommand(
            "CREATE TRIGGER fail_hbweb_product_name_update BEFORE UPDATE ON \"Product\" "
            + "BEGIN SELECT RAISE(ABORT, 'forced HBweb update failure'); END;"
        );

        var result = await CreateService().BatchUpdateHbwebProductNamesAsync(new BatchUpdateHbwebProductNamesDto
        {
            SyncToHq = true,
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-LOCAL-TRAN", ProductName = "不应写入的名称" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("BATCH_UPDATE_HBWEB_PRODUCT_NAMES_ERROR", result.ErrorCode);
        Assert.Null(result.Data);
        var details = Assert.IsType<BatchUpdateHbwebProductNamesResultDto>(result.Details);
        Assert.Equal(0, details.UpdatedCount);
        Assert.Null(details.HqSyncResult);

        var hbwebProduct = await _localDb.Queryable<Product>().SingleAsync(p => p.ItemNumber == "HB-LOCAL-TRAN");
        Assert.Equal("HBweb 原名称", hbwebProduct.ProductName);
        var hqProduct = await _hqDb.Queryable<DIC_商品信息字典表>().SingleAsync();
        Assert.Equal("HQ 原名称", hqProduct.H商品名称);
        Assert.Equal("seed", hqProduct.FGC_LastModifier);
    }

    [Fact]
    public void 单条创建_控制器使用根POST和商品创建权限()
    {
        var method = typeof(ReactDomesticProductsController).GetMethod("CreateDomesticProduct");

        Assert.NotNull(method);
        var route = method!.GetCustomAttribute<HttpPostAttribute>();
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(route);
        Assert.Null(route!.Template);
        Assert.Equal(Permissions.Products.Create, authorize?.Policy);
    }

    [Fact]
    public async Task 单条创建_控制器成功返回201和统一响应契约()
    {
        var response = ApiResponse<DomesticProductDto>.OK(new DomesticProductDto
        {
            ProductCode = "DP-CREATED",
            SupplierCode = "HB001",
            HBProductNo = "HB001-EXPLICIT",
            Barcode = "9527999900001",
        });
        var service = new Mock<IDomesticProductService>(MockBehavior.Strict);
        service
            .Setup(item => item.CreateDomesticProductAsync(It.IsAny<CreateDomesticProductDto>()))
            .ReturnsAsync(response);
        var controller = CreateReactController(service.Object);

        var actionResult = await InvokeCreateDomesticProductAsync(
            controller,
            CreateDomesticProductRequest()
        );

        var created = Assert.IsAssignableFrom<ObjectResult>(actionResult);
        Assert.Equal(201, created.StatusCode);
        Assert.Same(response, created.Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("SUPPLIER_NOT_FOUND", "供应商不存在")]
    [InlineData("CREATE_PRODUCT_ERROR", "创建国内商品失败")]
    public async Task 单条创建_控制器供应商或普通业务错误返回400(
        string errorCode,
        string message
    )
    {
        var response = ApiResponse<DomesticProductDto>.Error(message, errorCode);
        var service = new Mock<IDomesticProductService>(MockBehavior.Strict);
        service
            .Setup(item => item.CreateDomesticProductAsync(It.IsAny<CreateDomesticProductDto>()))
            .ReturnsAsync(response);
        var controller = CreateReactController(service.Object);

        var actionResult = await InvokeCreateDomesticProductAsync(
            controller,
            CreateDomesticProductRequest()
        );

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Same(response, badRequest.Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("HB_PRODUCT_NO_EXISTS", "HB货号已存在")]
    [InlineData("BARCODE_EXISTS", "条形码已存在")]
    public async Task 单条创建_控制器重复货号或条码返回409(
        string errorCode,
        string message
    )
    {
        var response = ApiResponse<DomesticProductDto>.Error(message, errorCode);
        var service = new Mock<IDomesticProductService>(MockBehavior.Strict);
        service
            .Setup(item => item.CreateDomesticProductAsync(It.IsAny<CreateDomesticProductDto>()))
            .ReturnsAsync(response);
        var controller = CreateReactController(service.Object);

        var actionResult = await InvokeCreateDomesticProductAsync(
            controller,
            CreateDomesticProductRequest()
        );

        var conflict = Assert.IsType<ConflictObjectResult>(actionResult);
        Assert.Same(response, conflict.Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task 单条创建_控制器模型验证失败返回400且不调用服务()
    {
        var service = new Mock<IDomesticProductService>(MockBehavior.Strict);
        var controller = CreateReactController(service.Object);
        controller.ModelState.AddModelError(nameof(CreateDomesticProductDto.SupplierCode), "供应商编码不能为空");

        var actionResult = await InvokeCreateDomesticProductAsync(
            controller,
            CreateDomesticProductRequest()
        );

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var response = Assert.IsType<ApiResponse<object>>(badRequest.Value);
        Assert.Equal("VALIDATION_ERROR", response.ErrorCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 单条创建_控制器未预期异常返回500()
    {
        var service = new Mock<IDomesticProductService>(MockBehavior.Strict);
        service
            .Setup(item => item.CreateDomesticProductAsync(It.IsAny<CreateDomesticProductDto>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));
        var controller = CreateReactController(service.Object);

        var actionResult = await InvokeCreateDomesticProductAsync(
            controller,
            CreateDomesticProductRequest()
        );

        var serverError = Assert.IsAssignableFrom<ObjectResult>(actionResult);
        Assert.Equal(500, serverError.StatusCode);
        var response = Assert.IsType<ApiResponse<object>>(serverError.Value);
        Assert.Equal("INTERNAL_SERVER_ERROR", response.ErrorCode);
        service.VerifyAll();
    }

    [Fact]
    public async Task 单条创建_真实服务保留显式货号和条码并去除首尾空白()
    {
        await SeedChinaSupplierAsync();
        var service = CreateDomesticProductService();
        var request = CreateDomesticProductRequest();
        request.HBProductNo = "  HB001-EXPLICIT  ";
        request.Barcode = "  9527999900001  ";

        var result = await service.CreateDomesticProductAsync(request);

        Assert.True(result.Success);
        Assert.Equal("HB001-EXPLICIT", result.Data!.HBProductNo);
        Assert.Equal("9527999900001", result.Data.Barcode);
        var product = await _localDb.Queryable<DomesticProduct>().SingleAsync();
        Assert.Equal("HB001-EXPLICIT", product.HBProductNo);
        Assert.Equal("9527999900001", product.Barcode);
    }

    [Theory]
    [InlineData("HB001-EXISTS", "9527999900002", "HB_PRODUCT_NO_EXISTS", "HB货号已存在")]
    [InlineData("HB001-NEW", "9527999900001", "BARCODE_EXISTS", "条形码已存在")]
    public async Task 单条创建_真实服务重复货号或条码时不新增记录(
        string hbProductNo,
        string barcode,
        string expectedErrorCode,
        string expectedMessage
    )
    {
        await SeedChinaSupplierAsync();
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DP-EXISTING",
            SupplierCode = "HB001",
            ProductName = "已存在商品",
            HBProductNo = "HB001-EXISTS",
            Barcode = "9527999900001",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        var request = CreateDomesticProductRequest();
        request.HBProductNo = hbProductNo;
        request.Barcode = barcode;

        var result = await CreateDomesticProductService().CreateDomesticProductAsync(request);

        Assert.False(result.Success);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Equal(1, await _localDb.Queryable<DomesticProduct>().CountAsync());
    }

    [Fact]
    public void HbwebProductNames_控制器使用国内采购商品管理权限()
    {
        var method = typeof(ReactDomesticProductsController).GetMethod(
            nameof(ReactDomesticProductsController.BatchUpdateHbwebProductNames)
        );

        Assert.NotNull(method);
        var route = method!.GetCustomAttribute<HttpPutAttribute>();
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("product-master-names", route?.Template);
        Assert.Equal(Permissions.DomesticPurchase.ManageProducts, authorize?.Policy);
    }

    [Fact]
    public async Task HbwebProductNames_控制器失败响应透传错误码和结果()
    {
        var data = new BatchUpdateHbwebProductNamesResultDto();
        data.Errors.Add("同一货号存在多个商品名称: HB-CONFLICT");
        var service = new Mock<IDomesticProductReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.BatchUpdateHbwebProductNamesAsync(It.IsAny<BatchUpdateHbwebProductNamesDto>()))
            .ReturnsAsync(ApiResponse<BatchUpdateHbwebProductNamesResultDto>.Error(
                "同一货号存在多个商品名称，请先修正后再更新",
                "DUPLICATE_ITEM_NUMBER_NAMES",
                data
            ));
        var controller = new ReactDomesticProductsController(
            service.Object,
            Mock.Of<IDomesticProductService>(),
            NullLogger<ReactDomesticProductsController>.Instance
        );

        var actionResult = await controller.BatchUpdateHbwebProductNames(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-CONFLICT", ProductName = "NAME A" },
            },
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var response = badRequest.Value!;
        Assert.Equal("DUPLICATE_ITEM_NUMBER_NAMES", response.GetType().GetProperty("errorCode")?.GetValue(response));
        var responseData = response.GetType().GetProperty("data")?.GetValue(response);
        var result = Assert.IsType<BatchUpdateHbwebProductNamesResultDto>(responseData);
        Assert.Contains(result.Errors, error => error.Contains("HB-CONFLICT"));
        service.VerifyAll();
    }

    [Fact]
    public async Task HbwebProductNames_控制器空货号空名称走服务层统一错误()
    {
        var data = new BatchUpdateHbwebProductNamesResultDto();
        data.Errors.Add("商品名称不能为空: HB-NO-NAME");
        var service = new Mock<IDomesticProductReactService>(MockBehavior.Strict);
        service
            .Setup(item => item.BatchUpdateHbwebProductNamesAsync(It.IsAny<BatchUpdateHbwebProductNamesDto>()))
            .ReturnsAsync(ApiResponse<BatchUpdateHbwebProductNamesResultDto>.Error(
                "存在无效货号或商品名称，请先修正后再更新",
                "INVALID_HBWEB_PRODUCT_NAMES",
                data
            ));
        var controller = new ReactDomesticProductsController(
            service.Object,
            Mock.Of<IDomesticProductService>(),
            NullLogger<ReactDomesticProductsController>.Instance
        );

        var actionResult = await controller.BatchUpdateHbwebProductNames(new BatchUpdateHbwebProductNamesDto
        {
            Products = new List<HbwebProductNameUpdateItemDto>
            {
                new() { SupplierCode = "SUP-A", ItemNumber = "HB-NO-NAME", ProductName = string.Empty },
            },
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var response = badRequest.Value!;
        Assert.Equal("INVALID_HBWEB_PRODUCT_NAMES", response.GetType().GetProperty("errorCode")?.GetValue(response));
        var responseData = response.GetType().GetProperty("data")?.GetValue(response);
        var result = Assert.IsType<BatchUpdateHbwebProductNamesResultDto>(responseData);
        Assert.Contains(result.Errors, error => error.Contains("HB-NO-NAME"));
        service.VerifyAll();
    }

    [Fact]
    public async Task SyncSelectedToHBSalesAsync_InsertsSetItemsForSetProduct()
    {
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DP-SET-001",
            SupplierCode = "HB001",
            ProductName = "套装商品",
            HBProductNo = "SET-001",
            Barcode = "PARENT-BAR",
            ProductType = 1,
            DomesticPrice = 10m,
            ImportPrice = 6m,
            OEMPrice = 8m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _localDb.Insertable(new[]
        {
            new DomesticSetProduct
            {
                SetProductCode = "set-guid-1",
                ProductCode = "DP-SET-001",
                ProductNo = "SET-001",
                SetProductNo = "SET-001-01",
                SetBarcode = "SET-BAR-01",
                DomesticPrice = 3m,
                ImportPrice = 2m,
                OEMPrice = 2.5m,
                Remarks = "第一件",
                IsDeleted = false,
            },
            new DomesticSetProduct
            {
                SetProductCode = "set-guid-2",
                ProductCode = "DP-SET-001",
                ProductNo = "SET-001",
                SetProductNo = "SET-001-02",
                SetBarcode = "SET-BAR-02",
                DomesticPrice = 4m,
                ImportPrice = 3m,
                OEMPrice = 3.5m,
                Remarks = "第二件",
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();

        var service = CreateService();

        var result = await service.SyncSelectedToHBSalesAsync(new List<string> { "DP-SET-001" });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("套装明细新增 2", result.Data!.Details);

        var setRows = await _hbSalesDb.Queryable<CPT_DIC_商品套装信息表>()
            .OrderBy(x => x.商品小货号)
            .ToListAsync();

        Assert.Equal(2, setRows.Count);
        Assert.Equal("DP-SET-001", setRows[0].商品编码);
        Assert.Equal("SET-001-01", setRows[0].商品小货号);
        Assert.Equal("SET-BAR-01", setRows[0].条形码);
        Assert.Equal(3m, setRows[0].国内价格);
        Assert.Equal(2m, setRows[0].进口价格);
        Assert.Equal(2.5m, setRows[0].贴牌价格);
        Assert.Equal("set-guid-1", setRows[0].HGUID);
        Assert.Equal(1, setRows[0].使用状态);
    }

    [Fact]
    public async Task SyncSelectedToHBSalesAsync_UpdatesSetItemsByProductCodeAndBarcodeWithoutDeletingExtraRows()
    {
        await SeedSetProductAsync("DP-SET-002");

        await _localDb.Insertable(new DomesticSetProduct
        {
            SetProductCode = "local-set-guid",
            ProductCode = "DP-SET-002",
            ProductNo = "SET-002",
            SetProductNo = "SET-002-NEW",
            SetBarcode = "SET-BAR-SAME",
            DomesticPrice = 9m,
            ImportPrice = 7m,
            OEMPrice = 8m,
            Remarks = "已更新",
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _hbSalesDb.Insertable(new[]
        {
            new CPT_DIC_商品套装信息表
            {
                HGUID = "existing-hguid",
                商品编码 = "DP-SET-002",
                商品小货号 = "SET-002-OLD",
                条形码 = "SET-BAR-SAME",
                国内价格 = 1m,
                进口价格 = 1m,
                贴牌价格 = 1m,
                备注 = "旧数据",
                使用状态 = 0,
            },
            new CPT_DIC_商品套装信息表
            {
                HGUID = "extra-hguid",
                商品编码 = "DP-SET-002",
                商品小货号 = "SET-002-EXTRA",
                条形码 = "SET-BAR-EXTRA",
                国内价格 = 2m,
                使用状态 = 1,
            },
        }).ExecuteCommandAsync();

        var service = CreateService();

        var result = await service.SyncSelectedToHBSalesAsync(new List<string> { "DP-SET-002" });

        Assert.True(result.Success);
        Assert.Contains("套装明细新增 0 条，更新 1 条，跳过 0 条", result.Data!.Details);

        var rows = await _hbSalesDb.Queryable<CPT_DIC_商品套装信息表>()
            .OrderBy(x => x.条形码)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        var updated = Assert.Single(rows, x => x.条形码 == "SET-BAR-SAME");
        Assert.Equal("existing-hguid", updated.HGUID);
        Assert.Equal("SET-002-NEW", updated.商品小货号);
        Assert.Equal(9m, updated.国内价格);
        Assert.Equal(7m, updated.进口价格);
        Assert.Equal(8m, updated.贴牌价格);
        Assert.Equal("已更新", updated.备注);
        Assert.Equal(1, updated.使用状态);
        Assert.Contains(rows, x => x.条形码 == "SET-BAR-EXTRA");
    }

    [Fact]
    public async Task SyncSelectedToHBSalesAsync_SkipsSetItemsWithoutBarcodeAndReportsMissingSetItems()
    {
        await SeedSetProductAsync("DP-SET-003");
        await SeedSetProductAsync("DP-SET-004");

        await _localDb.Insertable(new DomesticSetProduct
        {
            SetProductCode = "missing-barcode-guid",
            ProductCode = "DP-SET-003",
            ProductNo = "SET-003",
            SetProductNo = "SET-003-01",
            SetBarcode = null,
            DomesticPrice = 5m,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var service = CreateService();

        var result = await service.SyncSelectedToHBSalesAsync(
            new List<string> { "DP-SET-003", "DP-SET-004" }
        );

        Assert.True(result.Success);
        Assert.Contains("套装明细新增 0 条，更新 0 条，跳过 2 条，失败 0 条", result.Data!.Details);
        Assert.Contains("缺少套装条码", result.Data!.Details);
        Assert.Contains("没有本地套装明细", result.Data!.Details);

        var setRows = await _hbSalesDb.Queryable<CPT_DIC_商品套装信息表>().ToListAsync();
        Assert.Empty(setRows);
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hbSalesDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hbSalesConnection.Dispose();
        _hqConnection.Dispose();

        if (File.Exists(_localDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        if (File.Exists(_hbSalesDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_hbSalesDbPath);
        if (File.Exists(_hqDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
    }

    private DomesticProductReactService CreateService(HqSqlSugarContext? hqContext = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        var localContext = CreateSqlSugarContext(_localDb);
        var itemBarcodeService = new ItemBarcodeService(
            localContext,
            NullLogger<ItemBarcodeService>.Instance,
            configuration
        );

        return new DomesticProductReactService(
            localContext,
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            Mock.Of<AutoMapper.IMapper>(),
            NullLogger<DomesticProductReactService>.Instance,
            itemBarcodeService,
            hqContext ?? CreateHqSqlSugarContext(_hqDb)
        );
    }

    private DomesticProductService CreateDomesticProductService()
    {
        var configuration = new ConfigurationBuilder().Build();
        var localContext = CreateSqlSugarContext(_localDb);
        var itemBarcodeService = new ItemBarcodeService(
            localContext,
            NullLogger<ItemBarcodeService>.Instance,
            configuration
        );

        return new DomesticProductService(
            localContext,
            CreateDomesticProductMapper(),
            NullLogger<DomesticProductService>.Instance,
            itemBarcodeService
        );
    }

    private static IMapper CreateDomesticProductMapper()
    {
        return new MapperConfiguration(
            cfg => cfg.AddProfile<DomesticProductMappingProfile>(),
            NullLoggerFactory.Instance
        ).CreateMapper();
    }

    private static ReactDomesticProductsController CreateReactController(
        IDomesticProductService domesticProductService
    )
    {
        return new ReactDomesticProductsController(
            Mock.Of<IDomesticProductReactService>(),
            domesticProductService,
            NullLogger<ReactDomesticProductsController>.Instance
        );
    }

    private static async Task<IActionResult> InvokeCreateDomesticProductAsync(
        ReactDomesticProductsController controller,
        CreateDomesticProductDto dto
    )
    {
        return await controller.CreateDomesticProduct(dto);
    }

    private static CreateDomesticProductDto CreateDomesticProductRequest()
    {
        return new CreateDomesticProductDto
        {
            SupplierCode = "HB001",
            ProductName = "单条创建测试商品",
            HBProductNo = "HB001-EXPLICIT",
            Barcode = "9527999900001",
            ProductType = 0,
            IsActive = true,
        };
    }

    private async Task SeedChinaSupplierAsync()
    {
        await _localDb.Insertable(new ChinaSupplier
        {
            Guid = "supplier-hb001",
            SupplierCode = "HB001",
            SupplierName = "测试供应商",
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHbwebProductAsync(string itemNumber, string productName)
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"product-{itemNumber}",
            ProductCode = $"PC-{itemNumber}",
            LocalSupplierCode = "SUP-A",
            ItemNumber = itemNumber,
            ProductName = productName,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHqProductAsync(int id, string itemNumber, string productName)
    {
        await _hqDb.Insertable(CreateHqProduct(id, itemNumber, productName)).ExecuteCommandAsync();
    }

    private static DIC_商品信息字典表 CreateHqProduct(int id, string itemNumber, string productName)
    {
        return new DIC_商品信息字典表
        {
            ID = id,
            HGUID = $"hq-{id}",
            H商品标签GUID = string.Empty,
            H商品分类码GUID = string.Empty,
            H供货商编码 = "200",
            H商品编码 = $"CODE-{id}",
            H货号 = itemNumber,
            H主条形码 = $"BAR-{id}",
            H商品名称 = productName,
            H商品类型 = 0,
            H大写名称 = productName.ToUpperInvariant(),
            H规格 = string.Empty,
            H单位 = string.Empty,
            H商品图片 = string.Empty,
            H腾讯云图地址 = string.Empty,
            H进货单主表GUID = string.Empty,
            H进货单详情GUID = string.Empty,
            CBP商品中文名称 = string.Empty,
            CBP供应商编码 = string.Empty,
            CBP商品分类码GUID = string.Empty,
            FGC_Creator = "seed",
            FGC_CreateDate = new DateTime(2023, 1, 1),
            FGC_LastModifier = "seed",
            FGC_LastModifyDate = new DateTime(2024, 1, 1),
            FGC_UpdateHelp = string.Empty,
        };
    }

    private async Task SeedSetProductAsync(string productCode)
    {
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            SupplierCode = "HB001",
            ProductName = $"套装商品 {productCode}",
            HBProductNo = productCode.Replace("DP-", string.Empty),
            Barcode = $"{productCode}-PARENT",
            ProductType = 1,
            DomesticPrice = 10m,
            ImportPrice = 6m,
            OEMPrice = 8m,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
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
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static HBSalesSqlSugarContext CreateHBSalesSqlSugarContext(SqlSugarScope db)
    {
        var context = (HBSalesSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HBSalesSqlSugarContext)
        );
        var dbField = typeof(HBSalesSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(ISqlSugarClient db)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static string ResolveDomesticProductReactServicePath(
        [CallerFilePath] string testFilePath = ""
    )
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException("无法解析测试文件目录");
        return Path.GetFullPath(
            Path.Combine(
                testDirectory,
                "..",
                "BlazorApp.Api",
                "Services",
                "React",
                "DomesticProductReactService.cs"
            )
        );
    }
}
