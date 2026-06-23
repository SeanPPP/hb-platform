using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderImportPriceVarianceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public StoreOrderImportPriceVarianceTests()
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
            typeof(Container),
            typeof(ContainerDetail),
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(Product),
            typeof(Store)
        );
    }

    [Fact]
    public async Task GetImportPriceVarianceAsync_AppliesBaselineRulesAndDirectionFilters()
    {
        await SeedProductsAndStoreAsync();
        await SeedContainersAsync();
        await SeedOrdersAsync();

        var service = CreateService();

        var all = await service.GetImportPriceVarianceAsync(new StoreOrderImportPriceVarianceQueryDto
        {
            PageNumber = 1,
            PageSize = 20,
        });

        Assert.True(all.Success, all.Message);
        Assert.NotNull(all.Data);
        Assert.Equal(5, all.Data!.Total);
        Assert.Equal(5, all.Data.Summary.TotalRows);
        Assert.Equal(71.2m, all.Data.Summary.OriginalImportAmountTotal);
        Assert.Equal(69.2m, all.Data.Summary.BaselineImportAmountTotal);
        Assert.Equal(2m, all.Data.Summary.VarianceAmountTotal);

        var decrease = Assert.Single(all.Data.Items, item => item.ProductCode == "P2");
        Assert.Equal("C-P2-FIRST", decrease.FirstContainerCode);
        Assert.Equal(10m, decrease.FirstContainerImportPrice);
        Assert.Equal(8m, decrease.OrderImportPrice);
        Assert.Equal(4m, decrease.AllocQuantity);
        Assert.Equal(32m, decrease.OriginalImportAmount);
        Assert.Equal(40m, decrease.BaselineImportAmount);
        Assert.Equal(-8m, decrease.VarianceAmount);

        var increase = Assert.Single(all.Data.Items, item => item.ProductCode == "P1");
        Assert.Equal("C-P1-A", increase.FirstContainerCode);
        Assert.Equal("HG-P1-A-VALID", increase.FirstContainerNumber);
        Assert.Equal(new DateTime(2024, 1, 1), increase.FirstContainerDate);
        Assert.Equal(5m, increase.FirstContainerImportPrice);
        Assert.Equal(7m, increase.OrderImportPrice);
        Assert.Equal(3m, increase.AllocQuantity);
        Assert.Equal(21m, increase.OriginalImportAmount);
        Assert.Equal(15m, increase.BaselineImportAmount);
        Assert.Equal(6m, increase.VarianceAmount);

        var zeroFirst = Assert.Single(all.Data.Items, item => item.ProductCode == "P-ZERO-FIRST");
        Assert.Equal("C-ZERO-FIRST-VALID", zeroFirst.FirstContainerCode);
        Assert.Equal(6m, zeroFirst.FirstContainerImportPrice);
        Assert.Equal(8m, zeroFirst.OrderImportPrice);
        Assert.Equal(2m, zeroFirst.AllocQuantity);
        Assert.Equal(16m, zeroFirst.OriginalImportAmount);
        Assert.Equal(12m, zeroFirst.BaselineImportAmount);
        Assert.Equal(4m, zeroFirst.VarianceAmount);

        var exactHigh = Assert.Single(all.Data.Items, item => item.ProductCode == "P-EXACT-HIGH");
        Assert.Equal(0.2m, exactHigh.FirstContainerImportPrice);
        Assert.Equal(2m, exactHigh.OrderImportPrice);
        Assert.Equal(2m, exactHigh.OriginalImportAmount);
        Assert.Equal(0.2m, exactHigh.BaselineImportAmount);
        Assert.Equal(1.8m, exactHigh.VarianceAmount);

        var exactLow = Assert.Single(all.Data.Items, item => item.ProductCode == "P-EXACT-LOW");
        Assert.Equal(2m, exactLow.FirstContainerImportPrice);
        Assert.Equal(0.2m, exactLow.OrderImportPrice);
        Assert.Equal(0.2m, exactLow.OriginalImportAmount);
        Assert.Equal(2m, exactLow.BaselineImportAmount);
        Assert.Equal(-1.8m, exactLow.VarianceAmount);

        Assert.Equal("P2", all.Data.Items[0].ProductCode);
        Assert.DoesNotContain(all.Data.Items, item => item.OrderNo == "O-BEFORE");
        Assert.DoesNotContain(all.Data.Items, item => item.OrderNo == "O-EQUAL-DATE");
        Assert.DoesNotContain(all.Data.Items, item => item.OrderNo == "O-SAME-PRICE");
        Assert.DoesNotContain(all.Data.Items, item => item.OrderNo == "O-DELETED-ORDER");
        Assert.DoesNotContain(all.Data.Items, item => item.DetailGUID == "D-DELETED");
        Assert.DoesNotContain(all.Data.Items, item => item.DetailGUID == "D-NULL-IMPORT-PRICE");
        Assert.DoesNotContain(all.Data.Items, item => item.DetailGUID == "D-ZERO-IMPORT-PRICE");
        Assert.DoesNotContain(all.Data.Items, item => item.DetailGUID == "D-EXTREME-HIGH-PRICE");
        Assert.DoesNotContain(all.Data.Items, item => item.DetailGUID == "D-EXTREME-LOW-PRICE");
        Assert.DoesNotContain(all.Data.Items, item => string.IsNullOrWhiteSpace(item.ProductCode));
        Assert.DoesNotContain(all.Data.Items, item => item.ProductCode == "P-NO-BASELINE");

        var increaseOnly = await service.GetImportPriceVarianceAsync(new StoreOrderImportPriceVarianceQueryDto
        {
            VarianceDirection = "increase",
        });
        Assert.Equal(3, increaseOnly.Data!.Total);
        Assert.Contains(increaseOnly.Data.Items, item => item.ProductCode == "P1");
        Assert.Contains(increaseOnly.Data.Items, item => item.ProductCode == "P-ZERO-FIRST");
        Assert.Contains(increaseOnly.Data.Items, item => item.ProductCode == "P-EXACT-HIGH");
        Assert.Equal(11.8m, increaseOnly.Data.Summary.VarianceAmountTotal);

        var decreaseOnly = await service.GetImportPriceVarianceAsync(new StoreOrderImportPriceVarianceQueryDto
        {
            VarianceDirection = "decrease",
        });
        Assert.Equal(2, decreaseOnly.Data!.Total);
        Assert.Contains(decreaseOnly.Data.Items, item => item.ProductCode == "P2");
        Assert.Contains(decreaseOnly.Data.Items, item => item.ProductCode == "P-EXACT-LOW");
        Assert.Equal(-9.8m, decreaseOnly.Data.Summary.VarianceAmountTotal);
    }

    private async Task SeedProductsAndStoreAsync()
    {
        await _db.Insertable(new Store
        {
            StoreGUID = "STORE-GUID-1",
            StoreCode = "S1",
            StoreName = "Main Store",
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            new Product { UUID = "UUID-P1", ProductCode = "P1", ItemNumber = "ITEM-P1", ProductName = "Product One" },
            new Product { UUID = "UUID-P2", ProductCode = "P2", ItemNumber = "ITEM-P2", ProductName = "Product Two" },
            new Product { UUID = "UUID-P3", ProductCode = "P-NO-BASELINE", ItemNumber = "ITEM-P3", ProductName = "No Baseline" },
            new Product { UUID = "UUID-P4", ProductCode = "P-EXTREME-HIGH", ItemNumber = "ITEM-P4", ProductName = "Extreme High" },
            new Product { UUID = "UUID-P5", ProductCode = "P-EXTREME-LOW", ItemNumber = "ITEM-P5", ProductName = "Extreme Low" },
            new Product { UUID = "UUID-P6", ProductCode = "P-ZERO-FIRST", ItemNumber = "ITEM-P6", ProductName = "Zero First Baseline" },
            new Product { UUID = "UUID-P7", ProductCode = "P-EXACT-HIGH", ItemNumber = "ITEM-P7", ProductName = "Exact High" },
            new Product { UUID = "UUID-P8", ProductCode = "P-EXACT-LOW", ItemNumber = "ITEM-P8", ProductName = "Exact Low" },
        }).ExecuteCommandAsync();
    }

    private async Task SeedContainersAsync()
    {
        await _db.Insertable(new[]
        {
            new Container { ContainerCode = "C-P1-SHORT-NUMBER", ContainerNumber = "1234567890", LoadingDate = new DateTime(2023, 12, 20) },
            new Container { ContainerCode = "C-P1-B", ContainerNumber = "HG-P1-B-VALID", LoadingDate = new DateTime(2024, 1, 1) },
            new Container { ContainerCode = "C-P1-A", ContainerNumber = "HG-P1-A-VALID", LoadingDate = new DateTime(2024, 1, 1) },
            new Container { ContainerCode = "C-P1-DELETED-DETAIL", ContainerNumber = "HG-P1-DEL-DETAIL", LoadingDate = new DateTime(2023, 12, 15) },
            new Container { ContainerCode = "C-P1-LATE", ContainerNumber = "HG-P1-LATE-VALID", LoadingDate = new DateTime(2024, 1, 10) },
            new Container { ContainerCode = "C-P2-FIRST", ContainerNumber = "HG-P2-FIRST-VALID", LoadingDate = new DateTime(2024, 1, 2) },
            new Container { ContainerCode = "C-EXTREME-HIGH", ContainerNumber = "HG-EXTREME-HIGH", LoadingDate = new DateTime(2024, 1, 2) },
            new Container { ContainerCode = "C-EXTREME-LOW", ContainerNumber = "HG-EXTREME-LOW", LoadingDate = new DateTime(2024, 1, 2) },
            new Container { ContainerCode = "C-ZERO-FIRST-ZERO", ContainerNumber = "HG-ZERO-FIRST-ZERO", LoadingDate = new DateTime(2023, 12, 1) },
            new Container { ContainerCode = "C-ZERO-FIRST-VALID", ContainerNumber = "HG-ZERO-FIRST-VALID", LoadingDate = new DateTime(2024, 1, 2) },
            new Container { ContainerCode = "C-EXACT-HIGH", ContainerNumber = "HG-EXACT-HIGH", LoadingDate = new DateTime(2024, 1, 2) },
            new Container { ContainerCode = "C-EXACT-LOW", ContainerNumber = "HG-EXACT-LOW", LoadingDate = new DateTime(2024, 1, 2) },
            new Container { ContainerCode = "C-DELETED", ContainerNumber = "HG-DEL", LoadingDate = new DateTime(2023, 12, 1), IsDeleted = true },
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            new ContainerDetail { DetailCode = "CD-P1-SHORT-NUMBER", ContainerCode = "C-P1-SHORT-NUMBER", ProductCode = "P1", ImportPrice = 2m },
            new ContainerDetail { DetailCode = "CD-P1-B", ContainerCode = "C-P1-B", ProductCode = "P1", ImportPrice = 9m },
            new ContainerDetail { DetailCode = "CD-P1-A", ContainerCode = "C-P1-A", ProductCode = "P1", ImportPrice = 5m },
            new ContainerDetail { DetailCode = "CD-P1-DELETED-DETAIL", ContainerCode = "C-P1-DELETED-DETAIL", ProductCode = "P1", ImportPrice = 2m, IsDeleted = true },
            new ContainerDetail { DetailCode = "CD-P1-LATE", ContainerCode = "C-P1-LATE", ProductCode = "P1", ImportPrice = 4m },
            new ContainerDetail { DetailCode = "CD-P2-FIRST", ContainerCode = "C-P2-FIRST", ProductCode = "P2", ImportPrice = 10m },
            new ContainerDetail { DetailCode = "CD-EXTREME-HIGH", ContainerCode = "C-EXTREME-HIGH", ProductCode = "P-EXTREME-HIGH", ImportPrice = 1m },
            new ContainerDetail { DetailCode = "CD-EXTREME-LOW", ContainerCode = "C-EXTREME-LOW", ProductCode = "P-EXTREME-LOW", ImportPrice = 100m },
            new ContainerDetail { DetailCode = "CD-ZERO-FIRST-ZERO", ContainerCode = "C-ZERO-FIRST-ZERO", ProductCode = "P-ZERO-FIRST", ImportPrice = 0m },
            new ContainerDetail { DetailCode = "CD-ZERO-FIRST-VALID", ContainerCode = "C-ZERO-FIRST-VALID", ProductCode = "P-ZERO-FIRST", ImportPrice = 6m },
            new ContainerDetail { DetailCode = "CD-EXACT-HIGH", ContainerCode = "C-EXACT-HIGH", ProductCode = "P-EXACT-HIGH", ImportPrice = 0.2m },
            new ContainerDetail { DetailCode = "CD-EXACT-LOW", ContainerCode = "C-EXACT-LOW", ProductCode = "P-EXACT-LOW", ImportPrice = 2m },
            new ContainerDetail { DetailCode = "CD-DELETED-CONTAINER", ContainerCode = "C-DELETED", ProductCode = "P-NO-BASELINE", ImportPrice = 1m },
            new ContainerDetail { DetailCode = "CD-ZERO", ContainerCode = "C-P1-A", ProductCode = "P-NO-BASELINE", ImportPrice = 0m },
        }).ExecuteCommandAsync();
    }

    private async Task SeedOrdersAsync()
    {
        await InsertOrderAsync("O1", "O-INCREASE", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-INCREASE", "O1", "P1", 7m, null, 3m);

        await InsertOrderAsync("O2", "O-DECREASE", new DateTime(2024, 1, 6));
        await InsertDetailAsync("D-DECREASE", "O2", "P2", 8m, 30m, 4m);

        await InsertOrderAsync("O3", "O-BEFORE", new DateTime(2023, 12, 31));
        await InsertDetailAsync("D-BEFORE", "O3", "P1", 7m, null, 2m);

        await InsertOrderAsync("O4", "O-EQUAL-DATE", new DateTime(2024, 1, 1));
        await InsertDetailAsync("D-EQUAL-DATE", "O4", "P1", 7m, null, 2m);

        await InsertOrderAsync("O5", "O-SAME-PRICE", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-SAME-PRICE", "O5", "P1", 5m, null, 2m);

        await InsertOrderAsync("O6", "O-DELETED-ORDER", new DateTime(2024, 1, 5), isDeleted: true);
        await InsertDetailAsync("D-DELETED-ORDER", "O6", "P1", 7m, null, 2m);

        await InsertOrderAsync("O7", "O-DELETED-DETAIL", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-DELETED", "O7", "P1", 7m, null, 2m, isDeleted: true);

        await InsertOrderAsync("O10", "O-NULL-IMPORT-PRICE", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-NULL-IMPORT-PRICE", "O10", "P1", null, null, 2m);

        await InsertOrderAsync("O11", "O-ZERO-IMPORT-PRICE", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-ZERO-IMPORT-PRICE", "O11", "P1", 0m, null, 2m);

        await InsertOrderAsync("O12", "O-EXTREME-HIGH-PRICE", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-EXTREME-HIGH-PRICE", "O12", "P-EXTREME-HIGH", 11m, null, 2m);

        await InsertOrderAsync("O13", "O-EXTREME-LOW-PRICE", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-EXTREME-LOW-PRICE", "O13", "P-EXTREME-LOW", 9m, null, 2m);

        await InsertOrderAsync("O14", "O-ZERO-FIRST", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-ZERO-FIRST", "O14", "P-ZERO-FIRST", 8m, null, 2m);

        await InsertOrderAsync("O15", "O-EXACT-HIGH", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-EXACT-HIGH", "O15", "P-EXACT-HIGH", 2m, null, 1m);

        await InsertOrderAsync("O16", "O-EXACT-LOW", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-EXACT-LOW", "O16", "P-EXACT-LOW", 0.2m, null, 1m);

        await InsertOrderAsync("O8", "O-EMPTY-PRODUCT", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-EMPTY-PRODUCT", "O8", string.Empty, 7m, null, 2m);

        await InsertOrderAsync("O9", "O-NO-BASELINE", new DateTime(2024, 1, 5));
        await InsertDetailAsync("D-NO-BASELINE", "O9", "P-NO-BASELINE", 7m, null, 2m);
    }

    private async Task InsertOrderAsync(
        string orderGuid,
        string orderNo,
        DateTime orderDate,
        bool isDeleted = false
    )
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            StoreCode = "S1",
            OrderNo = orderNo,
            OrderDate = orderDate,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task InsertDetailAsync(
        string detailGuid,
        string orderGuid,
        string productCode,
        decimal? importPrice,
        decimal? importAmount,
        decimal allocQuantity,
        bool isDeleted = false
    )
    {
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = detailGuid,
            OrderGUID = orderGuid,
            StoreCode = "S1",
            ProductCode = productCode,
            ImportPrice = importPrice,
            ImportAmount = importAmount,
            AllocQuantity = allocQuantity,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private StoreOrderReactService CreateService()
    {
        return new StoreOrderReactService(
            CreateSqlSugarContext(_db),
            NullLogger<StoreOrderReactService>.Instance,
            new HttpContextAccessor(),
            Mock.Of<IOrderNumberGenerator>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            Mock.Of<IInvoiceEmailService>()
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
