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

public sealed class ContainerReactServiceDetailQueryTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hbSalesDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hbSalesDb;

    public ContainerReactServiceDetailQueryTests()
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
            typeof(WarehouseProduct),
            typeof(Product),
            typeof(StoreRetailPrice)
        );
    }

    [Fact]
    public async Task GetContainerDetailAsync_应只返回货柜头且不预加载明细()
    {
        await SeedContainerAsync("C-HEAD", "CSLU6099486");
        await SeedDetailAsync("D-HEAD-1", "C-HEAD", "P-HEAD-1", "HB001");
        var service = CreateService();

        var detail = await service.GetContainerDetailAsync("C-HEAD");

        Assert.NotNull(detail);
        Assert.Equal("C-HEAD", detail!.HGUID);
        Assert.Equal("CSLU6099486", detail.货柜编号);
        Assert.Empty(detail.Details);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_应服务端筛选排序分页并返回全量标签统计()
    {
        await SeedContainerAsync("C-QUERY", "CSLU6099486");
        await SeedDetailAsync("D-1", "C-QUERY", "P-1", "HB010", isActive: true, oemPrice: 3m, importPrice: 2m, localExists: true);
        await SeedDetailAsync("D-2", "C-QUERY", "P-2", "HB002", isActive: false, oemPrice: 0m, importPrice: 0m, localExists: false, minOrderQuantity: 12);
        await SeedDetailAsync("D-3", "C-QUERY", "P-3", "HB001", isActive: true, oemPrice: 4m, importPrice: 5m, localExists: true);
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-QUERY",
                PageNumber = 1,
                PageSize = 2,
                ItemNumber = "HB0",
                SortBy = "itemNumber",
                SortOrder = "ascend",
            }
        );

        Assert.Equal(3, result.ItemsTotal);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(2, result.PageSize);
        Assert.True(result.HasMore);
        Assert.Equal(new[] { "HB001", "HB002" }, result.Items.Select(x => x.商品信息?.货号).ToArray());
        Assert.Equal(12m, result.Items.Single(x => x.HGUID == "D-2").中包数);
        Assert.Equal(3, result.TagStats.All);
        Assert.Equal(1, result.TagStats.New);
        Assert.Equal(2, result.TagStats.Existing);
        Assert.Equal(1, result.TagStats.NoOemPrice);
        Assert.Equal(1, result.TagStats.AbnormalImport);
        Assert.Equal(2, result.TagStats.Active);
        Assert.Equal(1, result.TagStats.Inactive);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_中包数应仓库优先并用国内中包数兜底筛选排序()
    {
        await SeedContainerAsync("C-MIDDLE-PACK", "CSLU6099488");
        await SeedDetailAsync("D-MIDDLE-WAREHOUSE", "C-MIDDLE-PACK", "P-MIDDLE-WAREHOUSE", "HB300", minOrderQuantity: 8, middlePackQuantity: 4);
        await SeedDetailAsync("D-MIDDLE-DOMESTIC", "C-MIDDLE-PACK", "P-MIDDLE-DOMESTIC", "HB100", minOrderQuantity: null, middlePackQuantity: 12);
        await SeedDetailAsync("D-MIDDLE-LOW", "C-MIDDLE-PACK", "P-MIDDLE-LOW", "HB200", minOrderQuantity: null, middlePackQuantity: 2);
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-MIDDLE-PACK",
                PageNumber = 1,
                PageSize = 50,
                MiddlePackQuantityMin = 5,
                SortBy = "middlePackQuantity",
                SortOrder = "descend",
            }
        );

        Assert.Equal(new[] { "D-MIDDLE-DOMESTIC", "D-MIDDLE-WAREHOUSE" }, result.Items.Select(x => x.HGUID).ToArray());
        Assert.Equal(12m, result.Items[0].中包数);
        Assert.Equal(8m, result.Items[1].中包数);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_商品类型应以国内商品表为准展示筛选排序()
    {
        await SeedContainerAsync("C-PRODUCT-TYPE", "CSLU6099489");
        await SeedDetailAsync("D-TYPE-SET", "C-PRODUCT-TYPE", "P-TYPE-SET", "HB137-480", domesticProductType: 1);
        await SeedDetailAsync("D-TYPE-NORMAL", "C-PRODUCT-TYPE", "P-TYPE-NORMAL", "HB137-470", domesticProductType: 0);
        await SeedDetailAsync("D-TYPE-MULTI", "C-PRODUCT-TYPE", "P-TYPE-MULTI", "HB137-481", domesticProductType: 2);
        await SeedDetailAsync(
            "D-TYPE-SET-CHILD",
            "C-PRODUCT-TYPE",
            "P-TYPE-SET-CHILD",
            "HB137-482",
            detailProductType: "套装子商品"
        );
        var service = CreateService();

        var displayResult = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-PRODUCT-TYPE",
                PageNumber = 1,
                PageSize = 50,
                ItemNumber = "HB137-480",
            }
        );

        var displayItem = Assert.Single(displayResult.Items);
        Assert.Equal("普通商品", displayItem.商品类型);
        Assert.Equal("套装商品", displayItem.商品信息?.商品类型);

        var setResult = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-PRODUCT-TYPE",
                PageNumber = 1,
                PageSize = 50,
                ProductTypes = new List<string> { "set" },
            }
        );

        Assert.Equal(new[] { "HB137-480" }, setResult.Items.Select(x => x.商品信息?.货号).ToArray());

        var multiResult = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-PRODUCT-TYPE",
                PageNumber = 1,
                PageSize = 50,
                ProductTypes = new List<string> { "multi" },
            }
        );

        Assert.Equal(new[] { "HB137-481" }, multiResult.Items.Select(x => x.商品信息?.货号).ToArray());

        var setChildResult = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-PRODUCT-TYPE",
                PageNumber = 1,
                PageSize = 50,
                ProductTypes = new List<string> { "setChild" },
            }
        );

        Assert.Equal(new[] { "HB137-482" }, setChildResult.Items.Select(x => x.商品信息?.货号).ToArray());

        var normalForSetItemResult = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-PRODUCT-TYPE",
                PageNumber = 1,
                PageSize = 50,
                ItemNumber = "HB137-480",
                ProductTypes = new List<string> { "normal" },
            }
        );

        Assert.Empty(normalForSetItemResult.Items);

        var sortedResult = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-PRODUCT-TYPE",
                PageNumber = 1,
                PageSize = 50,
                SortBy = "productType",
                SortOrder = "ascend",
            }
        );

        Assert.Equal(
            new[] { "D-TYPE-NORMAL", "D-TYPE-SET-CHILD", "D-TYPE-SET", "D-TYPE-MULTI" },
            sortedResult.Items.Select(x => x.HGUID).ToArray()
        );
    }

    [Fact]
    public async Task GetDomesticSetCodesAsync_应从国内套装表返回条码价格和进货价()
    {
        await SeedDetailAsync("D-SET-CODES", "C-SET-CODES", "P-SET-CODES", "HB500", domesticProductType: 1);
        await SeedDomesticSetProductAsync("SET-2", "P-SET-CODES", "HB500", "HB500-02", "952700000002", domesticPrice: 12m, oemPrice: null, importPrice: 5m);
        await SeedDomesticSetProductAsync("SET-1", "P-SET-CODES", "HB500", "HB500-01", "952700000001", domesticPrice: 10m, oemPrice: 8.8m, importPrice: 4.5m);
        var service = CreateService();

        var result = await service.GetDomesticSetCodesAsync("P-SET-CODES");

        Assert.Equal(new[] { "HB500-01", "HB500-02" }, result.Select(x => x.SetItemNumber).ToArray());
        Assert.Equal("P-SET-CODES", result[0].ProductCode);
        Assert.Equal("HB500", result[0].ItemNumber);
        Assert.Equal(1, result[0].ProductType);
        Assert.Equal("952700000001", result[0].Barcode);
        Assert.Equal(8.8m, result[0].RetailPrice);
        Assert.Equal(4.5m, result[0].PurchasePrice);
        Assert.Equal(12m, result[1].RetailPrice);
    }

    [Fact]
    public async Task GetDomesticSetCodesAsync_商品不存在或无套装明细时返回空列表()
    {
        await SeedDetailAsync("D-NO-SET-CODES", "C-NO-SET-CODES", "P-NO-SET-CODES", "HB501", domesticProductType: 1);
        var service = CreateService();

        Assert.Empty(await service.GetDomesticSetCodesAsync("P-NO-SET-CODES"));
        Assert.Empty(await service.GetDomesticSetCodesAsync("P-MISSING"));
    }

    [Fact]
    public async Task UpdateDomesticSetCodePricesAsync_只回写国内套装价格字段且限制商品归属()
    {
        await SeedDetailAsync("D-SET-PRICE", "C-SET-PRICE", "P-SET-PRICE", "HB502", domesticProductType: 1);
        await SeedDetailAsync("D-OTHER-SET-PRICE", "C-SET-PRICE", "P-OTHER-SET-PRICE", "HB503", domesticProductType: 1);
        await SeedDomesticSetProductAsync("SET-PRICE-1", "P-SET-PRICE", "HB502", "HB502-01", "952700000101", domesticPrice: 13m, oemPrice: 9.9m, importPrice: 5.5m);
        await SeedDomesticSetProductAsync("SET-PRICE-OTHER", "P-OTHER-SET-PRICE", "HB503", "HB503-01", "952700000201", domesticPrice: 23m, oemPrice: 19.9m, importPrice: 15.5m);
        var service = CreateService();

        var updated = await service.UpdateDomesticSetCodePricesAsync(
            "P-SET-PRICE",
            new UpdateContainerDomesticSetCodePricesRequestDto
            {
                Items = new List<UpdateContainerDomesticSetCodePriceItemDto>
                {
                    new() { SetProductCode = "SET-PRICE-1", RetailPrice = 11.1m, PurchasePrice = 6.6m },
                    new() { SetProductCode = "SET-PRICE-OTHER", RetailPrice = 99m, PurchasePrice = 88m },
                },
            },
            "tester"
        );

        Assert.Equal(1, updated);
        var changed = await _localDb.Queryable<DomesticSetProduct>().FirstAsync(x => x.SetProductCode == "SET-PRICE-1");
        Assert.Equal(11.1m, changed.OEMPrice);
        Assert.Equal(6.6m, changed.ImportPrice);
        Assert.Equal(13m, changed.DomesticPrice);
        Assert.Equal("HB502-01", changed.SetProductNo);
        Assert.Equal("952700000101", changed.SetBarcode);
        Assert.Equal("tester", changed.UpdatedBy);

        var unchanged = await _localDb.Queryable<DomesticSetProduct>().FirstAsync(x => x.SetProductCode == "SET-PRICE-OTHER");
        Assert.Equal(19.9m, unchanged.OEMPrice);
        Assert.Equal(15.5m, unchanged.ImportPrice);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_标签筛选应同组取并集跨组取交集()
    {
        await SeedContainerAsync("C-TAGS", "CSLU6099487");
        await SeedDetailAsync("D-TAG-1", "C-TAGS", "P-TAG-1", "HB101", isActive: true, localExists: false);
        await SeedDetailAsync("D-TAG-2", "C-TAGS", "P-TAG-2", "HB102", isActive: false, localExists: false);
        await SeedDetailAsync("D-TAG-3", "C-TAGS", "P-TAG-3", "HB103", isActive: false, localExists: true);
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-TAGS",
                PageNumber = 1,
                PageSize = 50,
                SelectedTags = new List<string> { "new", "inactive" },
            }
        );

        Assert.Equal(new[] { "HB102" }, result.Items.Select(x => x.商品信息?.货号).ToArray());
        Assert.Equal(1, result.ItemsTotal);
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

    private async Task SeedContainerAsync(string containerCode, string containerNumber)
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = containerCode,
                ContainerNumber = containerNumber,
                LoadingDate = new DateTime(2026, 5, 12),
                EstimatedArrivalDate = new DateTime(2026, 6, 2),
                ActualArrivalDate = new DateTime(2026, 6, 8),
                ExchangeRate = 4.5m,
                ShippingFee = 12000m,
                TotalVolume = 69.868m,
                Status = 2,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedDetailAsync(
        string detailCode,
        string containerCode,
        string productCode,
        string itemNumber,
        bool isActive = true,
        decimal? oemPrice = 1m,
        decimal? importPrice = 1m,
        bool localExists = true,
        int? minOrderQuantity = null,
        int? middlePackQuantity = null,
        int domesticProductType = 0,
        string detailProductType = "普通商品"
    )
    {
        await _localDb.Insertable(
            new ContainerDetail
            {
                DetailCode = detailCode,
                ContainerCode = containerCode,
                ProductCode = productCode,
                ProductType = detailProductType,
                LoadingPieces = 1m,
                LoadingQuantity = 10m,
                DomesticPrice = 8m,
                AdjustmentRate = 1.1m,
                ImportPrice = importPrice,
                OEMPrice = oemPrice,
                TransportCost = 0.5m,
                Remarks = $"备注 {itemNumber}",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new DomesticProduct
            {
                ProductCode = productCode,
                HBProductNo = itemNumber,
                Barcode = $"9300000000{itemNumber}",
                ProductName = $"商品 {itemNumber}",
                EnglishProductName = $"Product {itemNumber}",
                ProductImage = $"https://example.test/{itemNumber}.jpg",
                MiddlePackQuantity = middlePackQuantity,
                ProductType = domesticProductType,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                ImportPrice = importPrice,
                OEMPrice = oemPrice,
                MinOrderQuantity = minOrderQuantity,
                IsActive = isActive,
            }
        ).ExecuteCommandAsync();

        if (localExists)
        {
            await _localDb.Insertable(
                new Product
                {
                    UUID = $"LOCAL-{productCode}",
                    ProductCode = productCode,
                    ProductName = $"本地商品 {itemNumber}",
                    IsActive = isActive,
                }
            ).ExecuteCommandAsync();
        }
    }

    private async Task SeedDomesticSetProductAsync(
        string setProductCode,
        string productCode,
        string productNo,
        string setProductNo,
        string setBarcode,
        decimal? domesticPrice,
        decimal? oemPrice,
        decimal? importPrice
    )
    {
        await _localDb.Insertable(
            new DomesticSetProduct
            {
                SetProductCode = setProductCode,
                ProductCode = productCode,
                ProductNo = productNo,
                SetProductNo = setProductNo,
                SetBarcode = setBarcode,
                DomesticPrice = domesticPrice,
                OEMPrice = oemPrice,
                ImportPrice = importPrice,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
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
            .ReturnsAsync((List<string> texts) => texts.ToDictionary(text => text, text => text));
        return translationService.Object;
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
        var dbField = typeof(HBSalesSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }
}
