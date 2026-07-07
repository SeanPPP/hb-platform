using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerReactServiceBatchUpdateDetailsTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hbSalesDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hbSalesDb;

    public ContainerReactServiceBatchUpdateDetailsTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hbSalesConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _localConnection.Open();
        _hbSalesConnection.Open();
        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hbSalesDb = new SqlSugarScope(CreateConnectionConfig(_hbSalesConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(
            typeof(Container),
            typeof(ContainerDetail),
            typeof(DomesticProduct),
            typeof(DomesticSetProduct),
            typeof(ProductGrade),
            typeof(DomesticProductCreationLog),
            typeof(WarehouseProduct),
            typeof(Product),
            typeof(StoreRetailPrice),
            typeof(WarehouseCategory)
        );
    }

    [Fact]
    public async Task ContainerReactServiceUpdateContainerAsync_状态变化_应更新货柜主表状态并保留头部字段更新()
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = "OOCU5568972",
                ContainerNumber = "OOCU5568972",
                LoadingDate = new DateTime(2026, 5, 26),
                EstimatedArrivalDate = new DateTime(2026, 6, 16),
                ActualArrivalDate = new DateTime(2026, 6, 15),
                ExchangeRate = 4.5m,
                ShippingFee = 100m,
                Status = 0,
                Remarks = "旧备注",
            }
        ).ExecuteCommandAsync();
        var service = CreateService();

        var success = await service.UpdateContainerAsync(
            "OOCU5568972",
            new UpdateContainerDto
            {
                货柜编号 = " OOCU5568973 ",
                装柜日期 = new DateTime(2026, 5, 27),
                预计到岸日期 = new DateTime(2026, 6, 17),
                实际到货日期 = new DateTime(2026, 6, 16),
                汇率 = 4.6m,
                运费 = 1280m,
                备注 = "运输中",
                状态 = 1,
            }
        );

        var container = await _localDb.Queryable<Container>()
            .SingleAsync(x => x.ContainerCode == "OOCU5568972");
        Assert.True(success);
        Assert.Equal("OOCU5568973", container.ContainerNumber);
        Assert.Equal(new DateTime(2026, 5, 27), container.LoadingDate);
        Assert.Equal(new DateTime(2026, 6, 17), container.EstimatedArrivalDate);
        Assert.Equal(1, container.Status);
        Assert.Equal(new DateTime(2026, 6, 16), container.ActualArrivalDate);
        Assert.Equal(4.6m, container.ExchangeRate);
        Assert.Equal(1280m, container.ShippingFee);
        Assert.Equal("运输中", container.Remarks);
    }

    [Fact]
    public async Task ContainerReactServiceUpdateContainerAsync_更新成同编号同装柜日期_应拒绝保存()
    {
        await _localDb.Insertable(
            new List<Container>
            {
                new()
                {
                    ContainerCode = "C-EXISTING",
                    ContainerNumber = "CSNU6209359",
                    LoadingDate = new DateTime(2026, 5, 29, 8, 30, 0),
                    Status = 1,
                },
                new()
                {
                    ContainerCode = "C-TARGET",
                    ContainerNumber = "CSNU6209360",
                    LoadingDate = new DateTime(2026, 5, 30),
                    Status = 1,
                },
            }
        ).ExecuteCommandAsync();
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateContainerAsync(
                "C-TARGET",
                new UpdateContainerDto
                {
                    货柜编号 = " CSNU6209359 ",
                    装柜日期 = new DateTime(2026, 5, 29, 15, 45, 0),
                }
            )
        );

        Assert.Equal("货柜编号 CSNU6209359 在装柜日期 2026-05-29 已存在", ex.Message);
    }

    [Fact]
    public async Task ContainerReactServiceUpdateContainerAsync_历史重复数据只改状态备注_应允许保存()
    {
        await _localDb.Insertable(
            new List<Container>
            {
                new()
                {
                    ContainerCode = "C-DUPLICATE-1",
                    ContainerNumber = "CSNU6209359",
                    LoadingDate = new DateTime(2026, 5, 29, 8, 30, 0),
                    Status = 1,
                },
                new()
                {
                    ContainerCode = "C-DUPLICATE-2",
                    ContainerNumber = "CSNU6209359",
                    LoadingDate = new DateTime(2026, 5, 29, 15, 45, 0),
                    Status = 1,
                    Remarks = "旧备注",
                },
            }
        ).ExecuteCommandAsync();
        var service = CreateService();

        var success = await service.UpdateContainerAsync(
            "C-DUPLICATE-2",
            new UpdateContainerDto
            {
                货柜编号 = " CSNU6209359 ",
                装柜日期 = new DateTime(2026, 5, 29),
                状态 = 2,
                备注 = "只改状态备注",
            }
        );

        var container = await _localDb.Queryable<Container>()
            .SingleAsync(x => x.ContainerCode == "C-DUPLICATE-2");
        Assert.True(success);
        Assert.Equal(2, container.Status);
        Assert.Equal("只改状态备注", container.Remarks);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_确认后应反向更新国内编码并级联引用()
    {
        await SeedDetailAsync("D-ALIGN-1", "DOM-OLD");
        await SeedDetailAsync("D-ALIGN-2", "DOM-OLD");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-OLD",
            HBProductNo = "ITEM-ALIGN",
            SupplierCode = "200",
            ProductName = "国内旧编码商品",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-NEW", "本地主档商品", null, "ITEM-ALIGN", "200");
        await _localDb.Insertable(new DomesticSetProduct
        {
            ProductCode = "DOM-OLD",
            ProductNo = "ITEM-ALIGN",
            SetProductNo = "SET-ALIGN",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new ProductGrade
        {
            Id = "GRADE-ALIGN",
            ProductCode = "DOM-OLD",
            Grade = "A",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new DomesticProductCreationLog
        {
            LogId = "LOG-ALIGN",
            ProductCode = "DOM-OLD",
            SupplierCode = "200",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        var service = CreateService();

        var result = await service.AlignDomesticProductCodeAsync(
            new AlignDomesticProductCodeRequestDto
            {
                DetailHguid = "D-ALIGN-1",
                ExpectedDomesticProductCode = "DOM-OLD",
                TargetProductCode = "LOCAL-NEW",
                SupplierCode = "200",
            }
        );

        Assert.Equal("DOM-OLD", result.OldProductCode);
        Assert.Equal("LOCAL-NEW", result.NewProductCode);
        Assert.Equal(1, result.UpdatedDomesticProducts);
        Assert.Equal(2, result.UpdatedContainerDetails);
        Assert.Equal(1, result.UpdatedDomesticSetProducts);
        Assert.Equal(1, result.UpdatedProductGrades);
        Assert.Equal(1, result.UpdatedDomesticProductCreationLogs);
        Assert.False(await _localDb.Queryable<DomesticProduct>().AnyAsync(x => x.ProductCode == "DOM-OLD"));
        Assert.True(await _localDb.Queryable<DomesticProduct>().AnyAsync(x => x.ProductCode == "LOCAL-NEW"));
        Assert.Equal(2, await _localDb.Queryable<ContainerDetail>().CountAsync(x => x.ProductCode == "LOCAL-NEW"));
        Assert.True(await _localDb.Queryable<DomesticSetProduct>().AnyAsync(x => x.ProductCode == "LOCAL-NEW"));
        Assert.True(await _localDb.Queryable<ProductGrade>().AnyAsync(x => x.ProductCode == "LOCAL-NEW"));
        Assert.True(await _localDb.Queryable<DomesticProductCreationLog>().AnyAsync(x => x.ProductCode == "LOCAL-NEW"));
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_缺少供应商代码_应拒绝()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-NO-SUPPLIER",
                    ExpectedDomesticProductCode = "DOM-NO-SUPPLIER",
                    TargetProductCode = "LOCAL-NO-SUPPLIER",
                }
            )
        );

        Assert.Equal("供应商代码不能为空", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_目标国内编码已存在_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-DUP", "DOM-DUP-OLD");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-DUP-OLD",
            HBProductNo = "ITEM-DUP",
            SupplierCode = "200",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "LOCAL-DUP",
            HBProductNo = "ITEM-DUP",
            SupplierCode = "200",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-DUP", "本地主档商品", null, "ITEM-DUP", "200");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-DUP",
                    ExpectedDomesticProductCode = "DOM-DUP-OLD",
                    TargetProductCode = "LOCAL-DUP",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("目标国内商品编码已存在，不能自动合并", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_货号不一致_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-ITEM", "DOM-ITEM-OLD");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-ITEM-OLD",
            HBProductNo = "ITEM-OLD",
            SupplierCode = "200",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-ITEM-NEW", "本地主档商品", null, "ITEM-NEW", "200");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-ITEM",
                    ExpectedDomesticProductCode = "DOM-ITEM-OLD",
                    TargetProductCode = "LOCAL-ITEM-NEW",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("国内商品货号与本地主档货号不一致，不能对齐编码", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_国内商品供应商不一致_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-DOM-SUPPLIER", "DOM-DOM-SUPPLIER-OLD");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-DOM-SUPPLIER-OLD",
            HBProductNo = "ITEM-DOM-SUPPLIER",
            SupplierCode = "999",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-DOM-SUPPLIER-NEW", "本地主档商品", null, "ITEM-DOM-SUPPLIER", "200");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-DOM-SUPPLIER",
                    ExpectedDomesticProductCode = "DOM-DOM-SUPPLIER-OLD",
                    TargetProductCode = "LOCAL-DOM-SUPPLIER-NEW",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("国内商品供应商代码与候选供应商不一致，不能对齐编码", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_本地主档供应商不一致_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-SUPPLIER", "DOM-SUPPLIER-OLD");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-SUPPLIER-OLD",
            HBProductNo = "ITEM-SUPPLIER",
            SupplierCode = "200",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-SUPPLIER-NEW", "本地主档商品", null, "ITEM-SUPPLIER", "999");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-SUPPLIER",
                    ExpectedDomesticProductCode = "DOM-SUPPLIER-OLD",
                    TargetProductCode = "LOCAL-SUPPLIER-NEW",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("供应商代码与本地主档不一致，不能对齐编码", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_套装子商品_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-SET-CHILD", "DOM-SET-CHILD-OLD", "套装子商品");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-SET-CHILD",
                    ExpectedDomesticProductCode = "DOM-SET-CHILD-OLD",
                    TargetProductCode = "LOCAL-SET-CHILD-NEW",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("套装子商品关联套装结构，暂不支持单独对齐编码", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_明细旧编码已变化_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-STALE", "DOM-CHANGED");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-STALE",
                    ExpectedDomesticProductCode = "DOM-STALE",
                    TargetProductCode = "LOCAL-STALE",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("明细商品编码已变化，请刷新后重试", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_原国内编码已存在本地主档_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-OLD-LOCAL", "DOM-OLD-LOCAL");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-OLD-LOCAL",
            HBProductNo = "ITEM-OLD-LOCAL",
            SupplierCode = "200",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-OLD-LOCAL", "目标本地主档商品", null, "ITEM-OLD-LOCAL", "200");
        await SeedLocalProductAsync("DOM-OLD-LOCAL", "旧码本地主档商品", null, "ITEM-OLD-LOCAL", "200");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-OLD-LOCAL",
                    ExpectedDomesticProductCode = "DOM-OLD-LOCAL",
                    TargetProductCode = "LOCAL-OLD-LOCAL",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("原国内商品编码已存在本地主档或仓库商品，不能自动改码", ex.Message);
    }

    [Fact]
    public async Task AlignDomesticProductCodeAsync_原国内编码已存在仓库商品_应拒绝()
    {
        await SeedDetailAsync("D-ALIGN-OLD-WAREHOUSE", "DOM-OLD-WAREHOUSE");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-OLD-WAREHOUSE",
            HBProductNo = "ITEM-OLD-WAREHOUSE",
            SupplierCode = "200",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-OLD-WAREHOUSE", "目标本地主档商品", null, "ITEM-OLD-WAREHOUSE", "200");
        await _localDb.Insertable(new WarehouseProduct
        {
            ProductCode = "DOM-OLD-WAREHOUSE",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AlignDomesticProductCodeAsync(
                new AlignDomesticProductCodeRequestDto
                {
                    DetailHguid = "D-ALIGN-OLD-WAREHOUSE",
                    ExpectedDomesticProductCode = "DOM-OLD-WAREHOUSE",
                    TargetProductCode = "LOCAL-OLD-WAREHOUSE",
                    SupplierCode = "200",
                }
            )
        );

        Assert.Equal("原国内商品编码已存在本地主档或仓库商品，不能自动改码", ex.Message);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_普通保存不应反向更新国内商品编码()
    {
        await SeedDetailAsync("D-NO-ALIGN", "DOM-NO-ALIGN");
        await _localDb.Insertable(new DomesticProduct
        {
            ProductCode = "DOM-NO-ALIGN",
            HBProductNo = "ITEM-NO-ALIGN",
            DomesticPrice = 1m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await SeedLocalProductAsync("LOCAL-NO-ALIGN", "本地主档商品", null, "ITEM-NO-ALIGN");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-NO-ALIGN", 国内价格 = 2m },
            }
        );

        Assert.Equal(1, totalUpdated);
        Assert.True(await _localDb.Queryable<DomesticProduct>().AnyAsync(x => x.ProductCode == "DOM-NO-ALIGN"));
        Assert.False(await _localDb.Queryable<DomesticProduct>().AnyAsync(x => x.ProductCode == "LOCAL-NO-ALIGN"));
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_未匹配明细目标分类_应保存到货柜明细()
    {
        await SeedDetailAsync("D-TARGET-CATEGORY-NEW", "P-TARGET-CATEGORY-NEW");
        await SeedWarehouseCategoryAsync("CAT-TARGET-NEW");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-TARGET-CATEGORY-NEW", ProductCategoryGUID = "CAT-TARGET-NEW" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-TARGET-CATEGORY-NEW");
        Assert.Equal(1, totalUpdated);
        Assert.Equal("CAT-TARGET-NEW", detail.TargetWarehouseCategoryGUID);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_已有商品目标分类_应同步本地商品仓库分类()
    {
        await SeedDetailAsync("D-TARGET-CATEGORY-EXISTING", "P-TARGET-CATEGORY-EXISTING");
        await SeedRelatedPriceRowsAsync("P-TARGET-CATEGORY-EXISTING");
        await SeedWarehouseCategoryAsync("CAT-TARGET-EXISTING");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-TARGET-CATEGORY-EXISTING", ProductCategoryGUID = "CAT-TARGET-EXISTING" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-TARGET-CATEGORY-EXISTING");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-TARGET-CATEGORY-EXISTING");
        Assert.Equal(1, totalUpdated);
        Assert.Equal("CAT-TARGET-EXISTING", detail.TargetWarehouseCategoryGUID);
        Assert.Equal("CAT-TARGET-EXISTING", product.WarehouseCategoryGUID);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_空白目标分类_应拒绝并不清空明细分类()
    {
        await SeedDetailAsync("D-TARGET-CATEGORY-BLANK", "P-TARGET-CATEGORY-BLANK");
        await _localDb.Updateable<ContainerDetail>()
            .SetColumns(x => x.TargetWarehouseCategoryGUID == "CAT-EXISTING")
            .Where(x => x.DetailCode == "D-TARGET-CATEGORY-BLANK")
            .ExecuteCommandAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BatchUpdateDetailsAsync(
                new List<UpdateContainerDetailDto>
                {
                    new() { HGUID = "D-TARGET-CATEGORY-BLANK", ProductCategoryGUID = "   " },
                }
            )
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-TARGET-CATEGORY-BLANK");
        Assert.Equal("CAT-EXISTING", detail.TargetWarehouseCategoryGUID);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_不存在或已删除目标分类_应拒绝并不写入分类()
    {
        await SeedDetailAsync("D-TARGET-CATEGORY-MISSING", "P-TARGET-CATEGORY-MISSING");
        await SeedWarehouseCategoryAsync("CAT-DELETED", isDeleted: true);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BatchUpdateDetailsAsync(
                new List<UpdateContainerDetailDto>
                {
                    new() { HGUID = "D-TARGET-CATEGORY-MISSING", ProductCategoryGUID = "CAT-MISSING" },
                    new() { HGUID = "D-TARGET-CATEGORY-MISSING", ProductCategoryGUID = "CAT-DELETED" },
                }
            )
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-TARGET-CATEGORY-MISSING");
        Assert.Null(detail.TargetWarehouseCategoryGUID);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_仅英文名称变化_应回写DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-EN-ONLY", "P-EN-ONLY", englishName: null);
        await SeedLocalProductAsync("P-EN-ONLY", productName: "旧本地商品名", englishName: "Old Local English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-EN-ONLY", 英文名称 = "Large Strawberry" },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-EN-ONLY");
        var localProduct = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-EN-ONLY");
        Assert.Equal(1, totalUpdated);
        Assert.Equal("Large Strawberry", product.EnglishProductName);
        Assert.Equal("Large Strawberry", localProduct.ProductName);
        Assert.Equal("Large Strawberry", localProduct.EnglishName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_空白英文名称_不覆盖DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-BLANK-EN", "P-BLANK-EN", englishName: "Existing English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-BLANK-EN", 英文名称 = "   " },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-BLANK-EN");
        Assert.Equal(0, totalUpdated);
        Assert.Equal("Existing English", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_清空英文名称_应清空DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-CLEAR-EN", "P-CLEAR-EN", englishName: "Existing English");
        await SeedLocalProductAsync("P-CLEAR-EN", productName: "保留本地商品名", englishName: "Existing Local English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-CLEAR-EN", ClearEnglishName = true },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-CLEAR-EN");
        var localProduct = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-CLEAR-EN");
        Assert.Equal(1, totalUpdated);
        Assert.Null(product.EnglishProductName);
        Assert.Equal("保留本地商品名", localProduct.ProductName);
        Assert.Null(localProduct.EnglishName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_同一商品多条明细_应聚合回写名称并统计请求行()
    {
        await SeedDetailAndProductAsync("D-SAME-1", "P-SAME", englishName: "Old English");
        await SeedDetailAsync("D-SAME-2", "P-SAME");
        await SeedDetailAsync("D-SAME-3", "P-SAME");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-SAME-1", 商品名称 = "聚合中文名" },
                new() { HGUID = "D-SAME-2", 英文名称 = "First English" },
                new() { HGUID = "D-SAME-3", 英文名称 = "Last English" },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-SAME");
        Assert.Equal(3, totalUpdated);
        Assert.Equal("聚合中文名", product.ProductName);
        Assert.Equal("Last English", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_同一商品多条明细最后清空英文名称_应清空并统计请求行()
    {
        await SeedDetailAndProductAsync("D-SAME-CLEAR-1", "P-SAME-CLEAR", englishName: "Old English");
        await SeedDetailAsync("D-SAME-CLEAR-2", "P-SAME-CLEAR");
        await SeedDetailAsync("D-SAME-CLEAR-3", "P-SAME-CLEAR");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-SAME-CLEAR-1", 英文名称 = "First English" },
                new() { HGUID = "D-SAME-CLEAR-2", 英文名称 = "Last English" },
                new() { HGUID = "D-SAME-CLEAR-3", ClearEnglishName = true },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-SAME-CLEAR");
        Assert.Equal(3, totalUpdated);
        Assert.Null(product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_价格和英文名称同时变化_应同时更新明细和DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-PRICE-EN", "P-PRICE-EN", englishName: "Old English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-PRICE-EN", 进口价格 = 3.45m, 英文名称 = "Translated Name" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-PRICE-EN");
        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-PRICE-EN");
        Assert.Equal(1, totalUpdated);
        Assert.Equal(3.45m, detail.ImportPrice);
        Assert.Equal("Translated Name", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_英文名称仍含中文_不覆盖DomesticProduct但保留其它明细更新()
    {
        await SeedDetailAndProductAsync("D-MIXED-EN", "P-MIXED-EN", englishName: "Old English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-MIXED-EN", 进口价格 = 4.56m, 英文名称 = "Large 草莓" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-MIXED-EN");
        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-MIXED-EN");
        Assert.Equal(1, totalUpdated);
        Assert.Equal(4.56m, detail.ImportPrice);
        Assert.Equal("Old English", product.EnglishProductName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_英文名称为中文_应翻译后回写DomesticProduct()
    {
        await SeedDetailAndProductAsync("D-ZH-EN", "P-ZH-EN", englishName: "Old English");
        await SeedLocalProductAsync("P-ZH-EN", productName: "旧中文名", englishName: "Old Local English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-ZH-EN", 英文名称 = "草莓玩具" },
            }
        );

        var product = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-ZH-EN");
        var localProduct = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-ZH-EN");
        Assert.Equal(1, totalUpdated);
        Assert.Equal("Strawberry Toy", product.EnglishProductName);
        Assert.Equal("Strawberry Toy", localProduct.ProductName);
        Assert.Equal("Strawberry Toy", localProduct.EnglishName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_国内价格和贴牌价格变化_应更新货柜明细()
    {
        await SeedDetailAndProductAsync("D-DOMESTIC-OEM", "P-DOMESTIC-OEM", englishName: "Old English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-DOMESTIC-OEM",
                    国内价格 = 11.60m,
                    贴牌价格 = 6.99m,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-DOMESTIC-OEM");

        Assert.Equal(1, totalUpdated);
        Assert.Equal(11.60m, detail.DomesticPrice);
        Assert.Equal(6.99m, detail.OEMPrice);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_装箱体积和统计字段变化_应更新货柜明细()
    {
        await SeedDetailAndProductAsync("D-PACKING-VOLUME", "P-PACKING-VOLUME", englishName: "Old English");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-PACKING-VOLUME",
                    单件装箱数 = 48m,
                    单件体积 = 0.118m,
                    装柜数量 = 96m,
                    合计装柜体积 = 0.236m,
                    合计装柜金额 = 1336.32m,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-PACKING-VOLUME");

        Assert.Equal(1, totalUpdated);
        Assert.Equal(48m, detail.PackingQuantity);
        Assert.Equal(0.118m, detail.UnitVolume);
        Assert.Equal(96m, detail.LoadingQuantity);
        Assert.Equal(0.236m, detail.TotalVolume);
        Assert.Equal(1336.32m, detail.TotalAmount);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_已匹配商品中包数变化_应同步仓库和国内中包数且不改明细装箱数()
    {
        await SeedDetailAndProductAsync("D-MIN-ORDER", "P-MIN-ORDER", englishName: "Old English", middlePackQuantity: 6);
        await SeedRelatedPriceRowsAsync("P-MIN-ORDER", minOrderQuantity: 6, packingQuantity: 24);
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-MIN-ORDER", 中包数 = 12m },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-MIN-ORDER");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-MIN-ORDER");
        var domesticProduct = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-MIN-ORDER");

        Assert.Equal(1, totalUpdated);
        Assert.Null(detail.PackingQuantity);
        Assert.Equal(12, warehouseProduct.MinOrderQuantity);
        Assert.Equal(24, warehouseProduct.PackingQuantity);
        Assert.Equal(12, domesticProduct.MiddlePackQuantity);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_未匹配商品中包数变化_应只更新国内中包数且不创建仓库商品()
    {
        await SeedDetailAndProductAsync("D-MIN-UNMATCHED", "P-MIN-UNMATCHED", englishName: "Old English", middlePackQuantity: 6);
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-MIN-UNMATCHED", 中包数 = 14m },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-MIN-UNMATCHED");
        var domesticProduct = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-MIN-UNMATCHED");
        var warehouseProductCount = await _localDb.Queryable<WarehouseProduct>()
            .Where(x => x.ProductCode == "P-MIN-UNMATCHED")
            .CountAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Null(detail.PackingQuantity);
        Assert.Equal(14, domesticProduct.MiddlePackQuantity);
        Assert.Equal(0, warehouseProductCount);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_统计字段变化_应同步刷新货柜主表汇总()
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = "C-SUMMARY",
                ContainerNumber = "C-SUMMARY",
                TotalPieces = 99m,
                TotalQuantity = 99m,
                TotalAmount = 99m,
                TotalVolume = 99m,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new List<ContainerDetail>
            {
                new()
                {
                    DetailCode = "D-SUMMARY-1",
                    ContainerCode = "C-SUMMARY",
                    ProductCode = "P-SUMMARY-1",
                    LoadingPieces = 2m,
                    LoadingQuantity = 20m,
                    TotalAmount = 100m,
                    TotalVolume = 0.5m,
                    IsDeleted = false,
                },
                new()
                {
                    DetailCode = "D-SUMMARY-2",
                    ContainerCode = "C-SUMMARY",
                    ProductCode = "P-SUMMARY-2",
                    LoadingPieces = 3m,
                    LoadingQuantity = 30m,
                    TotalAmount = 150m,
                    TotalVolume = 0.75m,
                    IsDeleted = false,
                },
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new DomesticProduct
            {
                ProductCode = "P-SUMMARY-1",
                HBProductNo = "P-SUMMARY-1",
                ProductName = "汇总商品",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await SeedRelatedPriceRowsAsync("P-SUMMARY-1");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SUMMARY-1",
                    装柜数量 = 48m,
                    合计装柜体积 = 0.66m,
                    合计装柜金额 = 464.64m,
                    进口价格 = 2.10m,
                    SkipRelatedProductSync = true,
                },
            }
        );

        var container = await _localDb.Queryable<Container>()
            .SingleAsync(x => x.ContainerCode == "C-SUMMARY");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SUMMARY-1");

        Assert.Equal(1, totalUpdated);
        Assert.Equal(5m, container.TotalPieces);
        Assert.Equal(78m, container.TotalQuantity);
        Assert.Equal(614.64m, container.TotalAmount);
        Assert.Equal(1.41m, container.TotalVolume);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_价格贴牌和上下架变化_应同步进货价但不写分店零售价()
    {
        await SeedDetailAndProductAsync("D-SYNC-PRICE", "P-SYNC-PRICE", englishName: "Old English");
        await SeedRelatedPriceRowsAsync("P-SYNC-PRICE");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SYNC-PRICE",
                    进口价格 = 8.88m,
                    贴牌价格 = 9.99m,
                    IsActive = false,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-SYNC-PRICE");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SYNC-PRICE");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-SYNC-PRICE");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-SYNC-PRICE")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(8.88m, detail.ImportPrice);
        Assert.Equal(9.99m, detail.OEMPrice);
        Assert.False(detail.IsActive);
        Assert.Equal(8.88m, warehouseProduct.ImportPrice);
        Assert.Equal(9.99m, warehouseProduct.OEMPrice);
        Assert.False(warehouseProduct.IsActive);
        Assert.Equal(8.88m, product.PurchasePrice);
        Assert.Equal(9.99m, product.RetailPrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(8.88m, row.PurchasePrice));
        Assert.All(storeRetailPrices, row => Assert.Equal(2.22m, row.StoreRetailPriceValue));
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_明细价格未变化_仍应同步已有商品关联价格()
    {
        await SeedDetailAndProductAsync("D-SYNC-SAME-PRICE", "P-SYNC-SAME-PRICE", englishName: "Old English");
        await SeedRelatedPriceRowsAsync("P-SYNC-SAME-PRICE");
        await _localDb.Updateable<ContainerDetail>()
            .SetColumns(x => x.ImportPrice == 8.88m)
            .SetColumns(x => x.OEMPrice == 9.99m)
            .SetColumns(x => x.IsActive == false)
            .Where(x => x.DetailCode == "D-SYNC-SAME-PRICE")
            .ExecuteCommandAsync();
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SYNC-SAME-PRICE",
                    进口价格 = 8.88m,
                    贴牌价格 = 9.99m,
                    IsActive = false,
                },
            }
        );

        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SYNC-SAME-PRICE");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-SYNC-SAME-PRICE");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-SYNC-SAME-PRICE")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(8.88m, warehouseProduct.ImportPrice);
        Assert.Equal(9.99m, warehouseProduct.OEMPrice);
        Assert.False(warehouseProduct.IsActive);
        Assert.Equal(8.88m, product.PurchasePrice);
        Assert.Equal(9.99m, product.RetailPrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(8.88m, row.PurchasePrice));
        Assert.All(storeRetailPrices, row => Assert.Equal(2.22m, row.StoreRetailPriceValue));
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_本地Product不存在_应只更新货柜明细且不回填仓库价格()
    {
        await SeedDetailAndProductAsync("D-NEW-PRICE", "P-NEW-PRICE", englishName: "New English");
        await _localDb.Insertable(
            new WarehouseProduct
            {
                ProductCode = "P-NEW-PRICE",
                ImportPrice = 1.11m,
                OEMPrice = 2.22m,
                IsActive = true,
            }
        ).ExecuteCommandAsync();
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-NEW-PRICE",
                    进口价格 = 8.88m,
                    贴牌价格 = 9.99m,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-NEW-PRICE");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-NEW-PRICE");
        var localProductCount = await _localDb.Queryable<Product>()
            .Where(x => x.ProductCode == "P-NEW-PRICE")
            .CountAsync();
        var storeRetailPriceCount = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-NEW-PRICE")
            .CountAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(8.88m, detail.ImportPrice);
        Assert.Equal(9.99m, detail.OEMPrice);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
        Assert.Equal(2.22m, warehouseProduct.OEMPrice);
        Assert.Equal(0, localProductCount);
        Assert.Equal(0, storeRetailPriceCount);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_跳过关联同步_应阻止名称中包数和分类回填()
    {
        await SeedDetailAndProductAsync(
            "D-SKIP-MASTER-DATA",
            "P-SKIP-MASTER-DATA",
            englishName: "Old English",
            middlePackQuantity: 12
        );
        await SeedRelatedPriceRowsAsync("P-SKIP-MASTER-DATA", minOrderQuantity: 12);
        await SeedWarehouseCategoryAsync("CAT-SKIP-MASTER-DATA");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SKIP-MASTER-DATA",
                    商品名称 = "新商品名",
                    英文名称 = "New English",
                    中包数 = 24,
                    ProductCategoryGUID = "CAT-SKIP-MASTER-DATA",
                    SkipRelatedProductSync = true,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-SKIP-MASTER-DATA");
        var domesticProduct = await _localDb.Queryable<DomesticProduct>()
            .SingleAsync(x => x.ProductCode == "P-SKIP-MASTER-DATA");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SKIP-MASTER-DATA");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-SKIP-MASTER-DATA");

        Assert.Equal(1, totalUpdated);
        Assert.Equal("CAT-SKIP-MASTER-DATA", detail.TargetWarehouseCategoryGUID);
        Assert.Equal("商品 P-SKIP-MASTER-DATA", domesticProduct.ProductName);
        Assert.Equal("Old English", domesticProduct.EnglishProductName);
        Assert.Equal(12, domesticProduct.MiddlePackQuantity);
        Assert.Equal(12, warehouseProduct.MinOrderQuantity);
        Assert.Null(product.WarehouseCategoryGUID);
        Assert.Equal("本地商品 P-SKIP-MASTER-DATA", product.ProductName);
        Assert.Null(product.EnglishName);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_跳过关联同步_应只更新货柜明细()
    {
        await SeedDetailAndProductAsync("D-SKIP-SYNC", "P-SKIP-SYNC", englishName: "Old English");
        await SeedRelatedPriceRowsAsync("P-SKIP-SYNC");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new()
                {
                    HGUID = "D-SKIP-SYNC",
                    进口价格 = 8.88m,
                    贴牌价格 = 9.99m,
                    IsActive = false,
                    SkipRelatedProductSync = true,
                },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-SKIP-SYNC");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-SKIP-SYNC");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-SKIP-SYNC");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-SKIP-SYNC")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(8.88m, detail.ImportPrice);
        Assert.Equal(9.99m, detail.OEMPrice);
        Assert.False(detail.IsActive);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
        Assert.Equal(2.22m, warehouseProduct.OEMPrice);
        Assert.True(warehouseProduct.IsActive);
        Assert.Equal(1.11m, product.PurchasePrice);
        Assert.Equal(2.22m, product.RetailPrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(1.11m, row.PurchasePrice));
        Assert.All(storeRetailPrices, row => Assert.Equal(2.22m, row.StoreRetailPriceValue));
    }

    [Fact]
    public async Task ApplyPricesByScopeAsync_仅修改进货价_不应同步旧零售价()
    {
        await SeedDetailAndProductAsync(
            "D-APPLY-IMPORT-ONLY",
            "P-APPLY-IMPORT-ONLY",
            englishName: "Old English"
        );
        await _localDb.Updateable<ContainerDetail>()
            .SetColumns(x => x.OEMPrice == 7.77m)
            .Where(x => x.DetailCode == "D-APPLY-IMPORT-ONLY")
            .ExecuteCommandAsync();
        await SeedRelatedPriceRowsAsync("P-APPLY-IMPORT-ONLY");
        var service = CreateService();

        var totalUpdated = await service.ApplyPricesByScopeAsync(
            "C-TEST",
            new ContainerDetailApplyPricesRequestDto
            {
                ImportPrice = 8.88m,
                SelectedHguids = new List<string> { "D-APPLY-IMPORT-ONLY" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-APPLY-IMPORT-ONLY");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-APPLY-IMPORT-ONLY");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-APPLY-IMPORT-ONLY");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-APPLY-IMPORT-ONLY")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(8.88m, detail.ImportPrice);
        Assert.Equal(7.77m, detail.OEMPrice);
        Assert.Equal(8.88m, warehouseProduct.ImportPrice);
        Assert.Equal(2.22m, warehouseProduct.OEMPrice);
        Assert.Equal(8.88m, product.PurchasePrice);
        Assert.Equal(2.22m, product.RetailPrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(8.88m, row.PurchasePrice));
        Assert.All(storeRetailPrices, row => Assert.Equal(2.22m, row.StoreRetailPriceValue));
    }

    [Fact]
    public async Task ApplyPricesByScopeAsync_仅修改零售价_不应同步旧进货价()
    {
        await SeedDetailAndProductAsync(
            "D-APPLY-OEM-ONLY",
            "P-APPLY-OEM-ONLY",
            englishName: "Old English"
        );
        await _localDb.Updateable<ContainerDetail>()
            .SetColumns(x => x.ImportPrice == 4.44m)
            .Where(x => x.DetailCode == "D-APPLY-OEM-ONLY")
            .ExecuteCommandAsync();
        await SeedRelatedPriceRowsAsync("P-APPLY-OEM-ONLY");
        var service = CreateService();

        var totalUpdated = await service.ApplyPricesByScopeAsync(
            "C-TEST",
            new ContainerDetailApplyPricesRequestDto
            {
                OemPrice = 9.99m,
                SelectedHguids = new List<string> { "D-APPLY-OEM-ONLY" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-APPLY-OEM-ONLY");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-APPLY-OEM-ONLY");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-APPLY-OEM-ONLY");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-APPLY-OEM-ONLY")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(4.44m, detail.ImportPrice);
        Assert.Equal(9.99m, detail.OEMPrice);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
        Assert.Equal(9.99m, warehouseProduct.OEMPrice);
        Assert.Equal(1.11m, product.PurchasePrice);
        Assert.Equal(9.99m, product.RetailPrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(1.11m, row.PurchasePrice));
        Assert.All(storeRetailPrices, row => Assert.Equal(2.22m, row.StoreRetailPriceValue));
    }

    [Fact]
    public async Task ApplyFloatRateByScopeAsync_系统重算进货价_应只更新货柜明细()
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = "C-FLOAT-SKIP",
                ContainerNumber = "C-FLOAT-SKIP",
                ExchangeRate = 5m,
                ShippingFee = 100m,
                TotalVolume = 10m,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new ContainerDetail
            {
                DetailCode = "D-FLOAT-SKIP",
                ContainerCode = "C-FLOAT-SKIP",
                ProductCode = "P-FLOAT-SKIP",
                DomesticPrice = 10m,
                TotalVolume = 2m,
                LoadingQuantity = 5m,
                AdjustmentRate = 1.10m,
                TransportCost = 0m,
                ImportPrice = 0m,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await SeedRelatedPriceRowsAsync("P-FLOAT-SKIP");
        var service = CreateService();

        var totalUpdated = await service.ApplyFloatRateByScopeAsync(
            "C-FLOAT-SKIP",
            new ContainerDetailApplyFloatRateRequestDto
            {
                FloatRate = 1.50m,
                SelectedHguids = new List<string> { "D-FLOAT-SKIP" },
            }
        );

        var detail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-FLOAT-SKIP");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-FLOAT-SKIP");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-FLOAT-SKIP");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-FLOAT-SKIP")
            .ToListAsync();

        Assert.Equal(1, totalUpdated);
        Assert.Equal(1.50m, detail.AdjustmentRate);
        Assert.Equal(4m, detail.TransportCost);
        Assert.Equal(8.18m, detail.ImportPrice);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
        Assert.Equal(1.11m, product.PurchasePrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(1.11m, row.PurchasePrice));
    }

    [Fact]
    public async Task RecalculateCostsByScopeAsync_空或低浮率_应托底到1点30并写回()
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = "C-RECALC-FLOAT",
                ContainerNumber = "C-RECALC-FLOAT",
                ExchangeRate = 5m,
                ShippingFee = 100m,
                TotalVolume = 10m,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new List<ContainerDetail>
            {
                new()
                {
                    DetailCode = "D-RECALC-NULL",
                    ContainerCode = "C-RECALC-FLOAT",
                    ProductCode = "P-RECALC-NULL",
                    DomesticPrice = 10m,
                    TotalVolume = 2m,
                    LoadingQuantity = 5m,
                    AdjustmentRate = null,
                    TransportCost = 0m,
                    ImportPrice = 0m,
                    IsDeleted = false,
                },
                new()
                {
                    DetailCode = "D-RECALC-LOW",
                    ContainerCode = "C-RECALC-FLOAT",
                    ProductCode = "P-RECALC-LOW",
                    DomesticPrice = 10m,
                    TotalVolume = 2m,
                    LoadingQuantity = 5m,
                    AdjustmentRate = 1.29m,
                    TransportCost = 0m,
                    ImportPrice = 0m,
                    IsDeleted = false,
                },
                new()
                {
                    DetailCode = "D-RECALC-VALID",
                    ContainerCode = "C-RECALC-FLOAT",
                    ProductCode = "P-RECALC-VALID",
                    DomesticPrice = 10m,
                    TotalVolume = 2m,
                    LoadingQuantity = 5m,
                    AdjustmentRate = 1.50m,
                    TransportCost = 0m,
                    ImportPrice = 0m,
                    IsDeleted = false,
                },
            }
        ).ExecuteCommandAsync();
        await SeedRelatedPriceRowsAsync("P-RECALC-VALID");
        var service = CreateService();

        var totalUpdated = await service.RecalculateCostsByScopeAsync(
            "C-RECALC-FLOAT",
            new ContainerDetailBatchScopeDto
            {
                SelectedHguids = new List<string>
                {
                    "D-RECALC-NULL",
                    "D-RECALC-LOW",
                    "D-RECALC-VALID",
                },
            }
        );

        var details = await _localDb.Queryable<ContainerDetail>()
            .Where(x => x.ContainerCode == "C-RECALC-FLOAT")
            .OrderBy(x => x.DetailCode)
            .ToListAsync();
        var nullRateDetail = details.Single(x => x.DetailCode == "D-RECALC-NULL");
        var lowRateDetail = details.Single(x => x.DetailCode == "D-RECALC-LOW");
        var validRateDetail = details.Single(x => x.DetailCode == "D-RECALC-VALID");
        var warehouseProduct = await _localDb.Queryable<WarehouseProduct>()
            .SingleAsync(x => x.ProductCode == "P-RECALC-VALID");
        var product = await _localDb.Queryable<Product>()
            .SingleAsync(x => x.ProductCode == "P-RECALC-VALID");
        var storeRetailPrices = await _localDb.Queryable<StoreRetailPrice>()
            .Where(x => x.ProductCode == "P-RECALC-VALID")
            .ToListAsync();
        Assert.Equal(3, totalUpdated);
        Assert.Equal(1.30m, nullRateDetail.AdjustmentRate);
        Assert.Equal(1.30m, lowRateDetail.AdjustmentRate);
        Assert.Equal(1.50m, validRateDetail.AdjustmentRate);
        Assert.All(details, detail => Assert.Equal(4m, detail.TransportCost));
        Assert.Equal(7.09m, nullRateDetail.ImportPrice);
        Assert.Equal(7.09m, lowRateDetail.ImportPrice);
        Assert.Equal(8.18m, validRateDetail.ImportPrice);
        Assert.Equal(1.11m, warehouseProduct.ImportPrice);
        Assert.Equal(1.11m, product.PurchasePrice);
        Assert.All(storeRetailPrices, row => Assert.Equal(1.11m, row.PurchasePrice));
    }

    [Fact]
    public async Task RecalculateCostsByScopeAsync_缺少汇率或运费_应阻止成本重算()
    {
        await _localDb.Insertable(
            new List<Container>
            {
                new()
                {
                    ContainerCode = "C-RECALC-NO-RATE",
                    ContainerNumber = "C-RECALC-NO-RATE",
                    ExchangeRate = null,
                    ShippingFee = 100m,
                    TotalVolume = 10m,
                },
                new()
                {
                    ContainerCode = "C-RECALC-NO-FREIGHT",
                    ContainerNumber = "C-RECALC-NO-FREIGHT",
                    ExchangeRate = 5m,
                    ShippingFee = null,
                    TotalVolume = 10m,
                },
            }
        ).ExecuteCommandAsync();
        var service = CreateService();

        var missingRateError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecalculateCostsByScopeAsync(
                "C-RECALC-NO-RATE",
                new ContainerDetailBatchScopeDto()
            )
        );
        var missingFreightError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyFloatRateByScopeAsync(
                "C-RECALC-NO-FREIGHT",
                new ContainerDetailApplyFloatRateRequestDto { FloatRate = 1.3m }
            )
        );

        Assert.Equal("缺少汇率，无法重算成本", missingRateError.Message);
        Assert.Equal("缺少运费，无法重算成本", missingFreightError.Message);
    }

    [Fact]
    public async Task BatchUpdateDetailsAsync_明细或商品不存在_不抛异常()
    {
        await SeedDetailAsync("D-NO-PRODUCT", productCode: "P-MISSING");
        var service = CreateService();

        var totalUpdated = await service.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-MISSING", 英文名称 = "Missing Detail" },
                new() { HGUID = "D-NO-PRODUCT", 英文名称 = "Missing Product" },
            }
        );

        Assert.Equal(0, totalUpdated);
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hbSalesDb.Dispose();
        _localConnection.Dispose();
        _hbSalesConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hbSalesDbPath);
    }

    private ContainerReactService CreateService()
    {
        return new ContainerReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(),
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<ContainerReactService>.Instance,
            Mock.Of<IContainerHqSyncService>(),
            CreateTranslationServiceMock()
        );
    }

    private static ITranslationService CreateTranslationServiceMock()
    {
        var translationService = new Mock<ITranslationService>();
        translationService
            .Setup(x => x.ContainsChinese(It.IsAny<string>()))
            .Returns<string>(value => value.Any(c => c >= '\u4e00' && c <= '\u9fff'));
        translationService
            .Setup(x => x.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((List<string> texts) =>
                texts.ToDictionary(
                    text => text,
                    text => text == "草莓玩具" ? "Strawberry Toy" : text
                )
            );
        return translationService.Object;
    }

    private async Task SeedDetailAndProductAsync(
        string detailCode,
        string productCode,
        string? englishName,
        int? middlePackQuantity = null
    )
    {
        await SeedDetailAsync(detailCode, productCode);
        await _localDb.Insertable(
            new DomesticProduct
            {
                ProductCode = productCode,
                HBProductNo = productCode,
                ProductName = $"商品 {productCode}",
                EnglishProductName = englishName,
                MiddlePackQuantity = middlePackQuantity,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalProductAsync(
        string productCode,
        string productName,
        string? englishName,
        string? itemNumber = null,
        string? localSupplierCode = null
    )
    {
        await _localDb.Insertable(
            new Product
            {
                ProductCode = productCode,
                ProductName = productName,
                EnglishName = englishName,
                ItemNumber = itemNumber,
                LocalSupplierCode = localSupplierCode,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedRelatedPriceRowsAsync(
        string productCode,
        int? minOrderQuantity = null,
        int? packingQuantity = null
    )
    {
        await _localDb.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                ImportPrice = 1.11m,
                OEMPrice = 2.22m,
                MinOrderQuantity = minOrderQuantity,
                PackingQuantity = packingQuantity,
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new Product
            {
                ProductCode = productCode,
                ProductName = $"本地商品 {productCode}",
                PurchasePrice = 1.11m,
                RetailPrice = 2.22m,
                IsActive = true,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new List<StoreRetailPrice>
            {
                new()
                {
                    StoreCode = "001",
                    ProductCode = productCode,
                    PurchasePrice = 1.11m,
                    StoreRetailPriceValue = 2.22m,
                    IsActive = true,
                },
                new()
                {
                    StoreCode = "002",
                    ProductCode = productCode,
                    PurchasePrice = 1.11m,
                    StoreRetailPriceValue = 2.22m,
                    IsActive = true,
                },
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedDetailAsync(string detailCode, string? productCode, string? productType = null)
    {
        await _localDb.Insertable(
            new ContainerDetail
            {
                DetailCode = detailCode,
                ContainerCode = "C-TEST",
                ProductCode = productCode,
                ProductType = productType,
                ImportPrice = 1.23m,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedWarehouseCategoryAsync(string categoryGuid, bool isDeleted = false)
    {
        await _localDb.Insertable(
            new WarehouseCategory
            {
                CategoryGUID = categoryGuid,
                CategoryName = $"分类 {categoryGuid}",
                IsActive = true,
                IsDeleted = isDeleted,
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
            MoreSettings = new ConnMoreSettings { IsNoReadXmlDescription = true },
        };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext()
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);
        return context;
    }

    private static HBSalesSqlSugarContext CreateHBSalesSqlSugarContext(SqlSugarScope db)
    {
        var context = (HBSalesSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HBSalesSqlSugarContext));
        var dbField = typeof(HBSalesSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }
}
