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
        await SeedDetailAsync("D-2", "C-QUERY", "P-2", "HB002", isActive: false, oemPrice: 0m, importPrice: 0m, localExists: false);
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
        Assert.Equal(3, result.TagStats.All);
        Assert.Equal(1, result.TagStats.New);
        Assert.Equal(2, result.TagStats.Existing);
        Assert.Equal(1, result.TagStats.NoOemPrice);
        Assert.Equal(1, result.TagStats.AbnormalImport);
        Assert.Equal(2, result.TagStats.Active);
        Assert.Equal(1, result.TagStats.Inactive);
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
        bool localExists = true
    )
    {
        await _localDb.Insertable(
            new ContainerDetail
            {
                DetailCode = detailCode,
                ContainerCode = containerCode,
                ProductCode = productCode,
                ProductType = "普通商品",
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
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                ImportPrice = importPrice,
                OEMPrice = oemPrice,
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
