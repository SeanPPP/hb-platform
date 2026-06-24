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
            typeof(WarehouseProduct),
            typeof(DomesticProduct),
            typeof(ChinaSupplier),
            typeof(Store)
        );
    }

    [Theory]
    [InlineData(
        "orderDate",
        true,
        "OrderDate DESC, OrderNo DESC, DetailGUID ASC",
        "OrderDate DESC, OrderDate DESC"
    )]
    [InlineData(
        "orderNo",
        false,
        "OrderNo ASC, OrderDate DESC, DetailGUID ASC",
        "OrderNo ASC, OrderNo DESC"
    )]
    [InlineData(
        null,
        true,
        "ABS(VarianceAmount) DESC, OrderDate DESC, OrderNo DESC, DetailGUID ASC",
        "ABS(VarianceAmount) DESC, ABS(VarianceAmount) DESC"
    )]
    public void BuildStoreOrderImportPriceVarianceDetailOrderBy_RemovesDuplicateSortColumns(
        string? sortBy,
        bool sortDescending,
        string expected,
        string duplicatedFragment
    )
    {
        var orderBy = StoreOrderReactService.BuildStoreOrderImportPriceVarianceDetailOrderBy(
            sortBy,
            sortDescending
        );

        Assert.Equal(expected, orderBy);
        Assert.DoesNotContain(duplicatedFragment, orderBy);
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
        Assert.Equal(79.2m, all.Data.Summary.OriginalImportAmountTotal);
        Assert.Equal(79.2m, all.Data.Summary.BaselineImportAmountTotal);
        Assert.Equal(0m, all.Data.Summary.VarianceAmountTotal);

        var decrease = Assert.Single(all.Data.Items, item => item.ProductCode == "P2");
        Assert.Equal("C-P2-FIRST", decrease.FirstContainerCode);
        Assert.Equal(10m, decrease.FirstContainerImportPrice);
        Assert.Equal("CN2", decrease.SupplierCode);
        Assert.Equal("供应商二", decrease.SupplierName);
        Assert.Equal("domestic-p2.jpg", decrease.ProductImage);
        Assert.Equal(55m, decrease.DomesticPrice);
        Assert.Equal(0.55m, decrease.UnitVolume);
        Assert.Equal(30, decrease.PackingQuantity);
        Assert.Equal(4m, decrease.AllocQuantityTotal);
        Assert.Equal(32m, decrease.OriginalImportAmountTotal);
        Assert.Equal(40m, decrease.BaselineImportAmountTotal);
        Assert.Equal(-8m, decrease.VarianceAmountTotal);
        Assert.Equal(1, decrease.DetailCount);

        var increase = Assert.Single(all.Data.Items, item => item.ProductCode == "P1");
        Assert.Equal("C-P1-A", increase.FirstContainerCode);
        Assert.Equal("HG-P1-A-VALID", increase.FirstContainerNumber);
        Assert.Equal(new DateTime(2024, 1, 1), increase.FirstContainerDate);
        Assert.Equal(5m, increase.FirstContainerImportPrice);
        Assert.Equal("CN1", increase.SupplierCode);
        Assert.Equal("供应商一", increase.SupplierName);
        Assert.Equal("product-p1.jpg", increase.ProductImage);
        Assert.Equal(88m, increase.DomesticPrice);
        Assert.Equal(0.88m, increase.UnitVolume);
        Assert.Equal(12, increase.PackingQuantity);
        Assert.Equal(5m, increase.AllocQuantityTotal);
        Assert.Equal(29m, increase.OriginalImportAmountTotal);
        Assert.Equal(25m, increase.BaselineImportAmountTotal);
        Assert.Equal(4m, increase.VarianceAmountTotal);
        Assert.Equal(2, increase.DetailCount);

        var zeroFirst = Assert.Single(all.Data.Items, item => item.ProductCode == "P-ZERO-FIRST");
        Assert.Equal("C-ZERO-FIRST-VALID", zeroFirst.FirstContainerCode);
        Assert.Equal(6m, zeroFirst.FirstContainerImportPrice);
        Assert.Equal(2m, zeroFirst.AllocQuantityTotal);
        Assert.Equal(16m, zeroFirst.OriginalImportAmountTotal);
        Assert.Equal(12m, zeroFirst.BaselineImportAmountTotal);
        Assert.Equal(4m, zeroFirst.VarianceAmountTotal);

        var exactHigh = Assert.Single(all.Data.Items, item => item.ProductCode == "P-EXACT-HIGH");
        Assert.Equal(0.2m, exactHigh.FirstContainerImportPrice);
        Assert.Equal(2m, exactHigh.OriginalImportAmountTotal);
        Assert.Equal(0.2m, exactHigh.BaselineImportAmountTotal);
        Assert.Equal(1.8m, exactHigh.VarianceAmountTotal);

        var exactLow = Assert.Single(all.Data.Items, item => item.ProductCode == "P-EXACT-LOW");
        Assert.Equal(2m, exactLow.FirstContainerImportPrice);
        Assert.Equal(0.2m, exactLow.OriginalImportAmountTotal);
        Assert.Equal(2m, exactLow.BaselineImportAmountTotal);
        Assert.Equal(-1.8m, exactLow.VarianceAmountTotal);

        Assert.Equal("P2", all.Data.Items[0].ProductCode);
        Assert.DoesNotContain(all.Data.Items, item => string.IsNullOrWhiteSpace(item.ProductCode));
        Assert.DoesNotContain(all.Data.Items, item => item.ProductCode == "P-NO-BASELINE");
        Assert.DoesNotContain(all.Data.Items, item => item.ProductCode == "P-EXTREME-HIGH");
        Assert.DoesNotContain(all.Data.Items, item => item.ProductCode == "P-EXTREME-LOW");

        var increaseOnly = await service.GetImportPriceVarianceAsync(new StoreOrderImportPriceVarianceQueryDto
        {
            VarianceDirection = "increase",
        });
        Assert.Equal(3, increaseOnly.Data!.Total);
        Assert.Contains(increaseOnly.Data.Items, item => item.ProductCode == "P1");
        Assert.Contains(increaseOnly.Data.Items, item => item.ProductCode == "P-ZERO-FIRST");
        Assert.Contains(increaseOnly.Data.Items, item => item.ProductCode == "P-EXACT-HIGH");
        Assert.Equal(11.8m, increaseOnly.Data.Summary.VarianceAmountTotal);
        Assert.Equal(6m, Assert.Single(increaseOnly.Data.Items, item => item.ProductCode == "P1").VarianceAmountTotal);

        var decreaseOnly = await service.GetImportPriceVarianceAsync(new StoreOrderImportPriceVarianceQueryDto
        {
            VarianceDirection = "decrease",
        });
        Assert.Equal(3, decreaseOnly.Data!.Total);
        Assert.Contains(decreaseOnly.Data.Items, item => item.ProductCode == "P1");
        Assert.Contains(decreaseOnly.Data.Items, item => item.ProductCode == "P2");
        Assert.Contains(decreaseOnly.Data.Items, item => item.ProductCode == "P-EXACT-LOW");
        Assert.Equal(-11.8m, decreaseOnly.Data.Summary.VarianceAmountTotal);

        var supplierOnly = await service.GetImportPriceVarianceAsync(new StoreOrderImportPriceVarianceQueryDto
        {
            SupplierCode = "CN1",
        });
        Assert.Equal(3, supplierOnly.Data!.Total);
        Assert.Contains(supplierOnly.Data.Items, item => item.ProductCode == "P1");
        Assert.Contains(supplierOnly.Data.Items, item => item.ProductCode == "P-ZERO-FIRST");
        Assert.Contains(supplierOnly.Data.Items, item => item.ProductCode == "P-EXACT-HIGH");
        Assert.Equal(9.8m, supplierOnly.Data.Summary.VarianceAmountTotal);

        var localSupplierMustNotMatch = await service.GetImportPriceVarianceAsync(new StoreOrderImportPriceVarianceQueryDto
        {
            SupplierCode = "LOCAL-P1",
        });
        Assert.Empty(localSupplierMustNotMatch.Data!.Items);
        Assert.Equal(0, localSupplierMustNotMatch.Data.Total);

        var p1Details = await service.GetImportPriceVarianceDetailsAsync(new StoreOrderImportPriceVarianceDetailQueryDto
        {
            ProductCode = "P1",
            PageNumber = 1,
            PageSize = 20,
        });
        Assert.True(p1Details.Success, p1Details.Message);
        Assert.Equal(2, p1Details.Data!.Total);
        Assert.Equal(29m, p1Details.Data.Summary.OriginalImportAmountTotal);
        Assert.Equal(25m, p1Details.Data.Summary.BaselineImportAmountTotal);
        Assert.Equal(4m, p1Details.Data.Summary.VarianceAmountTotal);
        Assert.Contains(p1Details.Data.Items, item => item.DetailGUID == "D-INCREASE");
        Assert.Contains(p1Details.Data.Items, item => item.DetailGUID == "D-P1-DECREASE");
        Assert.DoesNotContain(p1Details.Data.Items, item => item.OrderNo == "O-BEFORE");
        Assert.DoesNotContain(p1Details.Data.Items, item => item.OrderNo == "O-EQUAL-DATE");
        Assert.DoesNotContain(p1Details.Data.Items, item => item.OrderNo == "O-SAME-PRICE");
        Assert.DoesNotContain(p1Details.Data.Items, item => item.OrderNo == "O-DELETED-ORDER");
        Assert.DoesNotContain(p1Details.Data.Items, item => item.DetailGUID == "D-DELETED");
        Assert.DoesNotContain(p1Details.Data.Items, item => item.DetailGUID == "D-NULL-IMPORT-PRICE");
        Assert.DoesNotContain(p1Details.Data.Items, item => item.DetailGUID == "D-ZERO-IMPORT-PRICE");

        var p1DetailsByOrderDate = await service.GetImportPriceVarianceDetailsAsync(new StoreOrderImportPriceVarianceDetailQueryDto
        {
            ProductCode = "P1",
            SortBy = "orderDate",
            SortDescending = true,
        });
        Assert.True(p1DetailsByOrderDate.Success, p1DetailsByOrderDate.Message);
        Assert.Equal(2, p1DetailsByOrderDate.Data!.Total);

        var p1IncreaseDetails = await service.GetImportPriceVarianceDetailsAsync(new StoreOrderImportPriceVarianceDetailQueryDto
        {
            ProductCode = "P1",
            SupplierCode = "CN1",
            VarianceDirection = "increase",
        });
        var p1IncreaseDetail = Assert.Single(p1IncreaseDetails.Data!.Items);
        Assert.Equal("D-INCREASE", p1IncreaseDetail.DetailGUID);
        Assert.Equal(6m, p1IncreaseDetails.Data.Summary.VarianceAmountTotal);
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
            new Product { UUID = "UUID-P1", ProductCode = "P1", ItemNumber = "ITEM-P1", ProductName = "Product One", ProductImage = "product-p1.jpg", LocalSupplierCode = "LOCAL-P1" },
            new Product { UUID = "UUID-P2", ProductCode = "P2", ItemNumber = "ITEM-P2", ProductName = "Product Two", ProductImage = "" },
            new Product { UUID = "UUID-P3", ProductCode = "P-NO-BASELINE", ItemNumber = "ITEM-P3", ProductName = "No Baseline" },
            new Product { UUID = "UUID-P4", ProductCode = "P-EXTREME-HIGH", ItemNumber = "ITEM-P4", ProductName = "Extreme High" },
            new Product { UUID = "UUID-P5", ProductCode = "P-EXTREME-LOW", ItemNumber = "ITEM-P5", ProductName = "Extreme Low" },
            new Product { UUID = "UUID-P6", ProductCode = "P-ZERO-FIRST", ItemNumber = "ITEM-P6", ProductName = "Zero First Baseline" },
            new Product { UUID = "UUID-P7", ProductCode = "P-EXACT-HIGH", ItemNumber = "ITEM-P7", ProductName = "Exact High" },
            new Product { UUID = "UUID-P8", ProductCode = "P-EXACT-LOW", ItemNumber = "ITEM-P8", ProductName = "Exact Low" },
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            new ChinaSupplier { Guid = "CS1", SupplierCode = "CN1", SupplierName = "供应商一" },
            new ChinaSupplier { Guid = "CS2", SupplierCode = "CN2", SupplierName = "供应商二" },
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            new DomesticProduct { ProductCode = "P1", SupplierCode = "CN1", ProductImage = "domestic-p1.jpg", DomesticPrice = 99m, UnitVolume = 1.25m, PackingQuantity = 24 },
            new DomesticProduct { ProductCode = "P2", SupplierCode = "CN2", ProductImage = "domestic-p2.jpg", DomesticPrice = 55m, UnitVolume = 0.55m, PackingQuantity = 30 },
            new DomesticProduct { ProductCode = "P-ZERO-FIRST", SupplierCode = "CN1", DomesticPrice = 6m, UnitVolume = 0.66m, PackingQuantity = 18 },
            new DomesticProduct { ProductCode = "P-EXACT-HIGH", SupplierCode = "CN1", DomesticPrice = 2m, UnitVolume = 0.2m, PackingQuantity = 10 },
            new DomesticProduct { ProductCode = "P-EXACT-LOW", SupplierCode = "CN2", DomesticPrice = 2m, UnitVolume = 0.3m, PackingQuantity = 20 },
        }).ExecuteCommandAsync();

        await _db.Insertable(new[]
        {
            new WarehouseProduct { ProductCode = "P1", DomesticPrice = 88m, Volume = 0.88m, PackingQuantity = 12 },
            new WarehouseProduct { ProductCode = "P-ZERO-FIRST", DomesticPrice = 7m, Volume = 0.77m, PackingQuantity = 16 },
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

        await InsertOrderAsync("O17", "O-P1-DECREASE", new DateTime(2024, 1, 7));
        await InsertDetailAsync("D-P1-DECREASE", "O17", "P1", 4m, null, 2m);

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
