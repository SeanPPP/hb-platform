using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerDetailFieldConcurrencyGuardTests
{
    [Fact]
    public void CreateToken_相同业务值应产生稳定令牌_且不同字段隔离()
    {
        var first = ContainerDetailFieldConcurrencyGuard.CreateToken(
            "DETAIL-1",
            "国内价格",
            1.2300m,
            null
        );
        var second = ContainerDetailFieldConcurrencyGuard.CreateToken(
            "DETAIL-1",
            "国内价格",
            1.23m,
            null
        );
        var otherField = ContainerDetailFieldConcurrencyGuard.CreateToken(
            "DETAIL-1",
            "进口价格",
            1.23m,
            null
        );

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherField);
    }

    [Fact]
    public void Resolve_同字段已被服务器修改时应返回冲突_提交值等于当前值则幂等成功()
    {
        var baseline = ContainerDetailFieldConcurrencyGuard.CreateToken(
            "DETAIL-1",
            "国内价格",
            10m,
            null
        );
        var current = ContainerDetailFieldConcurrencyGuard.CreateToken(
            "DETAIL-1",
            "国内价格",
            11m,
            null
        );

        var conflict = ContainerDetailFieldConcurrencyGuard.Resolve(
            "DETAIL-1",
            "国内价格",
            baseline,
            null,
            current,
            11m,
            12m
        );
        var idempotent = ContainerDetailFieldConcurrencyGuard.Resolve(
            "DETAIL-1",
            "国内价格",
            baseline,
            null,
            current,
            11m,
            11m
        );

        Assert.False(conflict.Allowed);
        Assert.Equal("CONCURRENT_FIELD_UPDATE", conflict.Conflict?.Code);
        Assert.True(idempotent.Allowed);
    }

    [Fact]
    public void Resolve_确认覆盖必须针对刚看到的当前令牌()
    {
        var baseline = ContainerDetailFieldConcurrencyGuard.CreateToken(
            "DETAIL-1",
            "备注",
            "旧备注",
            null
        );
        var current = ContainerDetailFieldConcurrencyGuard.CreateToken(
            "DETAIL-1",
            "备注",
            "他人修改",
            null
        );

        var accepted = ContainerDetailFieldConcurrencyGuard.Resolve(
            "DETAIL-1",
            "备注",
            baseline,
            current,
            current,
            "他人修改",
            "我的备注"
        );
        var staleAcknowledgement = ContainerDetailFieldConcurrencyGuard.Resolve(
            "DETAIL-1",
            "备注",
            baseline,
            baseline,
            current,
            "他人修改",
            "我的备注"
        );

        Assert.True(accepted.Allowed);
        Assert.False(staleAcknowledgement.Allowed);
    }

    [Fact]
    public void CreateToken_null布尔字符串应有确定边界_空串与空白不等同()
    {
        var nullToken = ContainerDetailFieldConcurrencyGuard.CreateToken("D-1", "备注", null, null);
        var emptyToken = ContainerDetailFieldConcurrencyGuard.CreateToken("D-1", "备注", string.Empty, null);
        var whitespaceToken = ContainerDetailFieldConcurrencyGuard.CreateToken("D-1", "备注", " ", null);
        var enabledToken = ContainerDetailFieldConcurrencyGuard.CreateToken("D-1", "IsActive", true, null);
        var disabledToken = ContainerDetailFieldConcurrencyGuard.CreateToken("D-1", "IsActive", false, null);

        Assert.NotEqual(nullToken, emptyToken);
        Assert.NotEqual(emptyToken, whitespaceToken);
        Assert.NotEqual(enabledToken, disabledToken);
    }

    [Fact]
    public void CreateSnapshots_关联商品值变化必须改变对应字段令牌_其它字段不受影响()
    {
        var detail = new BlazorApp.Shared.Models.ContainerDetail
        {
            DetailCode = "D-RELATED",
            DomesticPrice = 10m,
            OEMPrice = 11m,
            ProductCode = "P-1",
        };
        var first = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail,
                new BlazorApp.Shared.Models.WarehouseProduct { ProductCode = "P-1", OEMPrice = 12m },
                null,
                null
            )
        );
        var second = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail,
                new BlazorApp.Shared.Models.WarehouseProduct { ProductCode = "P-1", OEMPrice = 13m },
                null,
                null
            )
        );

        Assert.NotEqual(first["贴牌价格"], second["贴牌价格"]);
        Assert.Equal(first["国内价格"], second["国内价格"]);
    }

    [Fact]
    public void 商品名称令牌_本地主档显示名变化不应影响_英文名称仍应受影响()
    {
        var detail = new ContainerDetail { DetailCode = "D-NAME", ProductCode = "P-NAME" };
        var domestic = new DomesticProduct { ProductCode = "P-NAME", ProductName = "中文名称" };
        var before = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail,
                null,
                domestic,
                new Product { ProductCode = "P-NAME", ProductName = "Old English", EnglishName = "Old English" }
            )
        );
        var after = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail,
                null,
                domestic,
                new Product { ProductCode = "P-NAME", ProductName = "New English", EnglishName = "New English" }
            )
        );

        Assert.Equal(before["商品名称"], after["商品名称"]);
        Assert.NotEqual(before["英文名称"], after["英文名称"]);
    }

    [Fact]
    public void CreateSnapshots_进口价同步目标含分店行变化必须冲突()
    {
        var detail = new ContainerDetail { DetailCode = "D-STORE", ProductCode = "P-STORE", ImportPrice = 10m };
        var warehouse = new WarehouseProduct { ProductCode = "P-STORE", ImportPrice = 10m };
        var local = new Product { ProductCode = "P-STORE", PurchasePrice = 10m };
        var before = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail, warehouse, null, local,
                new[] { new StoreRetailPrice { UUID = "S-1", ProductCode = "P-STORE", PurchasePrice = 10m } }
            )
        );
        var after = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail, warehouse, null, local,
                new[] { new StoreRetailPrice { UUID = "S-1", ProductCode = "P-STORE", PurchasePrice = 11m } }
            )
        );

        Assert.NotEqual(before["进口价格"], after["进口价格"]);
        Assert.Equal(before["国内价格"], after["国内价格"]);
    }

    [Fact]
    public void CreateSnapshots_进口价同步目标含套装和多码关系变化必须冲突()
    {
        var detail = new ContainerDetail { DetailCode = "D-SET-MULTI", ProductCode = "P-SET-MULTI", ImportPrice = 10m };
        var warehouse = new WarehouseProduct { ProductCode = detail.ProductCode, ImportPrice = 10m };
        var local = new Product { ProductCode = detail.ProductCode, PurchasePrice = 10m };
        var setRows = new[]
        {
            new ProductSetCode
            {
                SetCodeId = "SET-1", ProductCode = detail.ProductCode!, SetProductCode = "CHILD-1",
                SetType = 1, SetRetailPrice = 12m, SetPurchasePrice = 10m, IsActive = true,
            },
        };
        var multiRows = new[]
        {
            new StoreMultiCodeProduct
            {
                UUID = "MULTI-1", ProductCode = detail.ProductCode, StoreCode = "001",
                MultiCodeProductCode = "CHILD-1", MultiCodeRetailPrice = 12m,
                PurchasePrice = 10m, IsActive = true,
            },
        };
        var before = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail, warehouse, null, local, null, setRows, multiRows
            )
        );
        multiRows[0].MultiCodeRetailPrice = 13m;
        var afterMultiRetail = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail, warehouse, null, local, null, setRows, multiRows
            )
        );
        multiRows[0].MultiCodeRetailPrice = 12m;
        setRows[0].SetPurchasePrice = 11m;
        var afterSetPurchase = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(
                detail, warehouse, null, local, null, setRows, multiRows
            )
        );

        Assert.NotEqual(before["进口价格"], afterMultiRetail["进口价格"]);
        Assert.NotEqual(before["进口价格"], afterSetPurchase["进口价格"]);
        Assert.Equal(before["国内价格"], afterMultiRetail["国内价格"]);
    }

    [Fact]
    public void Resolve_关联同步值已变化时展示值相同也必须冲突()
    {
        var baseline = ContainerDetailFieldConcurrencyGuard.CreateToken("D-1", "贴牌价格", 10m, "warehouse:10");
        var current = ContainerDetailFieldConcurrencyGuard.CreateToken("D-1", "贴牌价格", 10m, "warehouse:11");

        var resolution = ContainerDetailFieldConcurrencyGuard.Resolve(
            "D-1", "贴牌价格", baseline, null, current, 10m, 10m, hasRelatedSyncValue: true
        );

        Assert.False(resolution.Allowed);
        Assert.Equal("CONCURRENT_FIELD_UPDATE", resolution.Conflict?.Code);
    }

    [Fact]
    public void Resolve_关联同步全部已达提交值时旧令牌重试仍应幂等成功()
    {
        var baseline = ContainerDetailFieldConcurrencyGuard.CreateToken("D-RETRY", "进口价格", 9m, "related:9");
        var current = ContainerDetailFieldConcurrencyGuard.CreateToken("D-RETRY", "进口价格", 10m, "related:10");

        var resolution = ContainerDetailFieldConcurrencyGuard.Resolve(
            "D-RETRY", "进口价格", baseline, null, current, 10m, 10m,
            hasRelatedSyncValue: true,
            relatedTargetsAlreadyAtSubmittedValue: true
        );

        Assert.True(resolution.Allowed);
        Assert.False(resolution.Overridden);
    }

    [Theory]
    [InlineData("普通商品")]
    [InlineData("套装商品")]
    [InlineData("多码商品")]
    public void 查询签发与事务锁内复算_同一快照必须生成相同令牌(string productType)
    {
        var detail = new ContainerDetail
        {
            DetailCode = $"D-{productType}",
            ProductCode = $"P-{productType}",
            ProductType = productType,
            AdjustmentRate = 1.3m,
            DomesticPrice = 9.6m,
            ImportPrice = 4.59m,
            TransportCost = 1.84m,
            OEMPrice = 8.99m,
            PackingQuantity = 12m,
            UnitVolume = 0.148m,
            LoadingQuantity = 20m,
            TotalVolume = 2.96m,
            TotalAmount = 191.8m,
            TargetWarehouseCategoryGUID = "TARGET",
            Remarks = "备注",
        };
        var warehouse = new WarehouseProduct
        {
            ProductCode = detail.ProductCode,
            ImportPrice = detail.ImportPrice,
            OEMPrice = 9.01m,
            MinOrderQuantity = 12,
            IsActive = true,
        };
        var domestic = new DomesticProduct
        {
            ProductCode = detail.ProductCode,
            ProductName = "中文商品",
            EnglishProductName = "Domestic English",
            MiddlePackQuantity = 10,
        };
        var local = new Product
        {
            ProductCode = detail.ProductCode,
            ProductName = "Local English",
            EnglishName = "Local English Name",
            WarehouseCategoryGUID = "LOCAL",
            PurchasePrice = detail.ImportPrice,
            RetailPrice = warehouse.OEMPrice,
        };
        var queryDto = new ContainerDetailDto
        {
            HGUID = detail.DetailCode,
            商品编码 = detail.ProductCode,
            调整浮率 = detail.AdjustmentRate,
            国内价格 = detail.DomesticPrice,
            进口价格 = detail.ImportPrice,
            运输成本 = detail.TransportCost,
            贴牌价格 = detail.OEMPrice,
            单件装箱数 = detail.PackingQuantity,
            中包数 = warehouse.MinOrderQuantity,
            单件体积 = detail.UnitVolume,
            装柜数量 = detail.LoadingQuantity,
            合计装柜体积 = detail.TotalVolume,
            合计装柜金额 = detail.TotalAmount,
            WarehouseOEMPrice = warehouse.OEMPrice,
            WarehouseImportPrice = warehouse.ImportPrice,
            WarehouseIsActive = warehouse.IsActive,
            ServerTokenLocalPurchasePrice = local.PurchasePrice,
            ServerTokenLocalRetailPrice = local.RetailPrice,
            ServerTokenDetailIsActive = detail.IsActive,
            ProductCategoryGUID = detail.TargetWarehouseCategoryGUID,
            备注 = detail.Remarks,
            ServerTokenDomesticMiddlePackQuantity = domestic.MiddlePackQuantity,
            ServerTokenTargetCategoryGuid = detail.TargetWarehouseCategoryGUID,
            ServerTokenLocalCategoryGuid = local.WarehouseCategoryGUID,
            ServerTokenLocalProductName = local.ProductName,
            ServerTokenLocalEnglishName = local.EnglishName,
            ServerTokenDomesticEnglishName = domestic.EnglishProductName,
            商品信息 = new ContainerProductInfoDto
            {
                商品名称 = domestic.ProductName,
                英文名称 = local.ProductName,
            },
        };

        var storeRows = new[]
        {
            new StoreRetailPrice { UUID = $"S-{productType}", ProductCode = detail.ProductCode, PurchasePrice = detail.ImportPrice },
        };
        var issued = ContainerDetailFieldConcurrencyGuard.CreateDetailTokens(queryDto, storeRows);
        var recomputed = ContainerDetailFieldConcurrencyGuard.CreateTokens(
            detail.DetailCode,
            ContainerDetailFieldConcurrencyGuard.CreateSnapshots(detail, warehouse, domestic, local, storeRows)
        );

        var differentFields = issued
            .Where(entry => !recomputed.TryGetValue(entry.Key, out var token) || token != entry.Value)
            .Select(entry => entry.Key)
            .ToList();
        Assert.True(differentFields.Count == 0, string.Join(", ", differentFields));
    }
}
