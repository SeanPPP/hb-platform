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

public sealed class ContainerAllocationSalesReportServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public ContainerAllocationSalesReportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(ProductStoreDailySalesStatistic),
            typeof(SalesStatisticRefreshState),
            typeof(Store)
        );
    }

    [Fact]
    public async Task QueryAsync_实际到货日优先并按含边界口径汇总全部商品()
    {
        var arrival = DateTime.Today.AddDays(-10);
        var service = CreateService(
            new ContainerMainDto
            {
                HGUID = "C-1",
                货柜编号 = "柜-1",
                实际到货日期 = arrival,
                预计到岸日期 = arrival.AddDays(-5),
            },
            Detail(" p-1 ", 10, "A-1", "商品一"),
            Detail("P-1", 5, "A-1", "商品一"),
            Detail("P-2", 8, "A-2", "商品二")
        );
        await SeedFreshStatesAsync(arrival, arrival.AddDays(2));

        await SeedOrderAsync("O-START", arrival.AddDays(-20), arrival, false,
            OrderDetail("D-1", "O-START", "P-1", "S1", 2, 3));
        await SeedOrderAsync("O-END", arrival.AddDays(2), null, false,
            OrderDetail("D-2", "O-END", "P-1", "S1", 1, 4));
        await SeedOrderAsync("O-OUT", arrival.AddDays(3), null, false,
            OrderDetail("D-3", "O-OUT", "P-1", "S1", 99, 99));
        await SeedOrderAsync("O-DELETED", arrival, null, true,
            OrderDetail("D-4", "O-DELETED", "P-1", "S1", 99, 99));
        await SeedOrderAsync("O-DETAIL-DELETED", arrival, null, false,
            OrderDetail("D-5", "O-DETAIL-DELETED", "P-1", "S1", 99, 99, true));

        await SeedSaleAsync(arrival, "S1", "P-1", 2, 20, 12, 8, "ProductSnapshot");
        await SeedSaleAsync(arrival.AddDays(1), "S2", "P-1", 1, 15, 9, 6, "ProductSnapshot");
        await SeedSaleAsync(arrival, "S1", "P-1", 100, 999, 1, 998, "ProductSnapshot", "999");
        await SeedSaleAsync(arrival, "S1", "P-2", 0, 0, 0, 0, "ProductSnapshot");

        var result = await service.QueryAsync("C-1", new ContainerAllocationSalesQueryRequest
        {
            StartDate = arrival,
            EndDate = arrival.AddDays(2),
            PageNumber = 1,
            PageSize = 20,
        });

        Assert.True(result.CanQuery);
        Assert.Equal(ContainerArrivalDateBasis.Actual, result.ArrivalDateBasis);
        Assert.False(result.IsEstimatedArrivalDate);
        Assert.Equal(3, result.DayCount);
        Assert.Equal(2, result.Total);
        Assert.Equal("P-1", result.Items[0].ProductCode);
        var product = Assert.Single(result.Items, item => item.ProductCode == "P-1");
        Assert.Equal(15, product.LoadingQuantity);
        Assert.Equal(3, product.AllocationQuantity);
        Assert.Equal(10, product.AllocationImportAmount);
        Assert.Equal(3, product.SalesQuantity);
        Assert.Equal(35, product.SalesAmount);
        Assert.Equal(35m / 3m, product.AverageSalesPrice);
        Assert.Equal(14, product.GrossProfit);
        Assert.Equal(0.4m, product.GrossMarginRate);
        Assert.True(product.IsGrossMarginComplete);
        var zeroSales = Assert.Single(result.Items, item => item.ProductCode == "P-2");
        Assert.Null(zeroSales.AverageSalesPrice);
        Assert.Null(zeroSales.GrossMarginRate);
        Assert.Equal(23, result.Totals.LoadingQuantity);
        Assert.Equal(3, result.Totals.AllocationQuantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-99)]
    [InlineData(0)]
    [InlineData(999)]
    public async Task QueryAsync_任意订单流程状态均参与配货汇总(int? flowStatus)
    {
        var arrival = DateTime.Today.AddDays(-1);
        var service = CreateService(new ContainerMainDto { HGUID = "C-FLOW", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await SeedFreshStatesAsync(arrival, arrival);
        await SeedOrderWithFlowStatusAsync(
            $"O-{flowStatus}",
            arrival,
            flowStatus,
            OrderDetail($"D-{flowStatus}", $"O-{flowStatus}", "P-1", "S1", 3, 2)
        );

        var result = await service.QueryAsync("C-FLOW", new ContainerAllocationSalesQueryRequest
        {
            StartDate = arrival,
            EndDate = arrival,
        });

        Assert.Equal(3, Assert.Single(result.Items).AllocationQuantity);
    }

    [Fact]
    public async Task QueryAsync_数据库商品编码含空格和小写时仍命中配货与销售()
    {
        var arrival = DateTime.Today.AddDays(-1);
        var service = CreateService(new ContainerMainDto { HGUID = "C-NORMALIZE", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await SeedFreshStatesAsync(arrival, arrival);
        await SeedOrderAsync("O-NORMALIZE", arrival, null, false,
            OrderDetail("D-NORMALIZE", "O-NORMALIZE", " p-1 ", "S1", 3, 2));
        await SeedSaleAsync(arrival, "S1", " p-1 ", 4, 20, 12, 8, "ProductSnapshot");

        var result = await service.QueryAsync("C-NORMALIZE", new ContainerAllocationSalesQueryRequest
        {
            StartDate = arrival,
            EndDate = arrival,
        });

        var product = Assert.Single(result.Items);
        Assert.Equal(3, product.AllocationQuantity);
        Assert.Equal(6, product.AllocationImportAmount);
        Assert.Equal(4, product.SalesQuantity);
        Assert.Equal(20, product.SalesAmount);
    }

    [Fact]
    public async Task QueryAsync_同商品分店跨日期销售聚合且任一行缺成本时标记不完整()
    {
        var arrival = DateTime.Today.AddDays(-2);
        var salesSql = new List<string>();
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (
                sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("ProductStoreDailySalesStatistic", StringComparison.OrdinalIgnoreCase)
            )
                salesSql.Add(sql);
        };
        var service = CreateService(new ContainerMainDto { HGUID = "C-SALES-GROUP", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await SeedFreshStatesAsync(arrival, arrival.AddDays(1));
        await SeedSaleAsync(arrival, "S1", "P-1", 2, 20, 12, 8, "ProductSnapshot");
        await SeedSaleAsync(arrival.AddDays(1), "S1", " p-1 ", 3, 30, null, null, "missing");

        var result = await service.QueryAsync("C-SALES-GROUP", new ContainerAllocationSalesQueryRequest
        {
            StartDate = arrival,
            EndDate = arrival.AddDays(1),
        });

        var product = Assert.Single(result.Items);
        Assert.Equal(5, product.SalesQuantity);
        Assert.Equal(50, product.SalesAmount);
        Assert.Equal(10, product.AverageSalesPrice);
        Assert.False(product.IsGrossMarginComplete);
        Assert.Null(product.GrossProfit);
        Assert.Null(product.GrossMarginRate);
        Assert.NotEmpty(salesSql);
        Assert.All(salesSql, sql => Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QueryAsync_货柜不存在时抛出明确异常()
    {
        var containerService = new Mock<IContainerReactService>();
        containerService.Setup(x => x.GetContainerDetailAsync("MISSING")).ReturnsAsync((ContainerMainDto?)null);
        var service = new ContainerAllocationSalesReportService(
            CreateContext(_db),
            containerService.Object,
            NullLogger<ContainerAllocationSalesReportService>.Instance
        );

        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.QueryAsync(
            "MISSING",
            new ContainerAllocationSalesQueryRequest()
        ));

        Assert.Equal("货柜不存在。", error.Message);
    }

    [Fact]
    public async Task QueryAsync_到货超过四周时默认范围精确包含二十八天()
    {
        var arrival = DateTime.Today.AddDays(-60);
        var expectedEnd = arrival.AddDays(27);
        var service = CreateService(new ContainerMainDto { HGUID = "C-28", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await SeedFreshStatesAsync(arrival, expectedEnd);

        var result = await service.QueryAsync("C-28", new ContainerAllocationSalesQueryRequest());

        Assert.Equal(arrival, result.StartDate);
        Assert.Equal(expectedEnd, result.EndDate);
        Assert.Equal(28, result.DayCount);
        Assert.Equal(4, result.EndWeek);
    }

    [Theory]
    [InlineData(-1, -2)]
    [InlineData(0, 1)]
    [InlineData(-6, -5)]
    public async Task QueryAsync_拒绝超出到货日至今天范围或结束日早于开始日(int startOffset, int endOffset)
    {
        var arrival = DateTime.Today.AddDays(-5);
        var service = CreateService(new ContainerMainDto { HGUID = "C-DATE", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueryAsync(
            "C-DATE",
            new ContainerAllocationSalesQueryRequest
            {
                StartDate = DateTime.Today.AddDays(startOffset),
                EndDate = DateTime.Today.AddDays(endOffset),
            }
        ));
    }

    [Fact]
    public async Task QueryAsync_预计日期回退_默认四周并限制到今天()
    {
        var expected = DateTime.Today.AddDays(-10);
        var service = CreateService(new ContainerMainDto
        {
            HGUID = "C-2",
            货柜编号 = "柜-2",
            预计到岸日期 = expected,
        }, Detail("P-1", 1, "A-1", "商品一"));
        await SeedFreshStatesAsync(expected, DateTime.Today);

        var result = await service.QueryAsync("C-2", new ContainerAllocationSalesQueryRequest());

        Assert.True(result.CanQuery);
        Assert.Equal(ContainerArrivalDateBasis.Expected, result.ArrivalDateBasis);
        Assert.True(result.IsEstimatedArrivalDate);
        Assert.Equal(expected, result.StartDate);
        Assert.Equal(DateTime.Today, result.EndDate);
        Assert.Equal(11, result.DayCount);
        Assert.Contains("第 1-2 周", result.RangeLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueryAsync_缺日期或尚未到货时禁止查询(bool futureArrival)
    {
        var service = CreateService(new ContainerMainDto
        {
            HGUID = "C-3",
            实际到货日期 = futureArrival ? DateTime.Today.AddDays(1) : null,
        }, Detail("P-1", 1, "A-1", "商品一"));

        var result = await service.QueryAsync("C-3", new ContainerAllocationSalesQueryRequest());

        Assert.False(result.CanQuery);
        Assert.Empty(result.Items);
        Assert.NotNull(result.QueryMessage);
    }

    [Fact]
    public async Task QueryAsync_统计未就绪仍返回配货且销售字段为空()
    {
        var arrival = DateTime.Today.AddDays(-2);
        var service = CreateService(new ContainerMainDto { HGUID = "C-4", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await SeedOrderAsync("O-1", arrival, null, false,
            OrderDetail("D-1", "O-1", "P-1", "S1", 4, 2));
        await SeedSaleAsync(arrival, "S1", "P-1", 10, 100, 60, 40, "ProductSnapshot");

        var result = await service.QueryAsync("C-4", new ContainerAllocationSalesQueryRequest
        {
            StartDate = arrival,
            EndDate = arrival,
        });

        var product = Assert.Single(result.Items);
        Assert.Equal(4, product.AllocationQuantity);
        Assert.Null(product.SalesQuantity);
        Assert.Null(product.SalesAmount);
        Assert.Equal(SalesStatisticRefreshStatus.Pending, result.StatisticStatus);
        Assert.NotNull(result.StatisticMessage);
    }

    [Fact]
    public async Task QueryAsync_缺成本时不显示毛利率并支持搜索排序分页()
    {
        var arrival = DateTime.Today.AddDays(-1);
        var service = CreateService(new ContainerMainDto { HGUID = "C-5", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "苹果"),
            Detail("P-2", 1, "A-2", "香蕉"),
            Detail("P-3", 1, "A-3", "香蕉特价"));
        await SeedFreshStatesAsync(arrival, arrival);
        await SeedOrderAsync("O-1", arrival, null, false,
            OrderDetail("D-1", "O-1", "P-2", "S1", 2, 5),
            OrderDetail("D-2", "O-1", "P-3", "S1", 8, 5));
        await SeedSaleAsync(arrival, "S1", "P-3", 2, 20, null, null, "Missing");

        var result = await service.QueryAsync("C-5", new ContainerAllocationSalesQueryRequest
        {
            StartDate = arrival,
            EndDate = arrival,
            Search = "香蕉",
            SortBy = "allocationQuantity",
            SortDirection = "desc",
            PageNumber = 1,
            PageSize = 1,
        });

        Assert.Equal(2, result.Total);
        var product = Assert.Single(result.Items);
        Assert.Equal("P-3", product.ProductCode);
        Assert.False(product.IsGrossMarginComplete);
        Assert.Null(product.GrossMarginRate);
    }

    [Fact]
    public async Task QueryBranchesAsync_返回有效分店和有历史数据的停用分店且不串店()
    {
        var arrival = DateTime.Today.AddDays(-1);
        var service = CreateService(new ContainerMainDto { HGUID = "C-6", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await _db.Insertable(new[]
        {
            new Store { StoreGUID = "G1", StoreCode = "S1", StoreName = "有效零数据", IsActive = true },
            new Store { StoreGUID = "G2", StoreCode = "S2", StoreName = "有效有数据", IsActive = true },
            new Store { StoreGUID = "G3", StoreCode = "S3", StoreName = "停用有历史", IsActive = false },
            new Store { StoreGUID = "G4", StoreCode = "S4", StoreName = "停用无历史", IsActive = false },
        }).ExecuteCommandAsync();
        await SeedFreshStatesAsync(arrival, arrival);
        await SeedOrderAsync("O-1", arrival, null, false,
            OrderDetail("D-1", "O-1", "P-1", "S2", 2, 5));
        await SeedSaleAsync(arrival, "S3", "P-1", 3, 30, 18, 12, "ProductSnapshot");

        var result = await service.QueryBranchesAsync("C-6", new ContainerAllocationSalesBranchesQueryRequest
        {
            ProductCode = "p-1",
            StartDate = arrival,
            EndDate = arrival,
        });

        Assert.Equal(3, result.Items.Count);
        var zero = Assert.Single(result.Items, item => item.BranchCode == "S1");
        Assert.Equal(0, zero.AllocationQuantity);
        Assert.Equal(0, zero.SalesQuantity);
        var allocated = Assert.Single(result.Items, item => item.BranchCode == "S2");
        Assert.Equal(2, allocated.AllocationQuantity);
        Assert.Equal(0, allocated.SalesQuantity);
        var inactive = Assert.Single(result.Items, item => item.BranchCode == "S3");
        Assert.False(inactive.IsActive);
        Assert.Equal(3, inactive.SalesQuantity);
        Assert.DoesNotContain(result.Items, item => item.BranchCode == "S4");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QueryBranchesAsync_明细分店为空时回退订单分店(string? detailStoreCode)
    {
        var arrival = DateTime.Today.AddDays(-1);
        var service = CreateService(new ContainerMainDto { HGUID = "C-STORE", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await _db.Insertable(new Store
        {
            StoreGUID = "G-S1",
            StoreCode = "S1",
            StoreName = "订单分店",
            IsActive = true,
        }).ExecuteCommandAsync();
        await SeedFreshStatesAsync(arrival, arrival);
        await SeedOrderWithStoreAsync(
            "O-STORE",
            arrival,
            "S1",
            OrderDetail("D-STORE", "O-STORE", "P-1", detailStoreCode!, 2, 3)
        );

        var result = await service.QueryBranchesAsync("C-STORE", new ContainerAllocationSalesBranchesQueryRequest
        {
            ProductCode = "P-1",
            StartDate = arrival,
            EndDate = arrival,
        });

        var branch = Assert.Single(result.Items);
        Assert.Equal("S1", branch.BranchCode);
        Assert.Equal(2, branch.AllocationQuantity);
    }

    [Fact]
    public async Task QueryBranchesAsync_统计Pending时仍显示仅有历史销售的停用分店且不泄漏指标()
    {
        var arrival = DateTime.Today.AddDays(-1);
        var service = CreateService(new ContainerMainDto { HGUID = "C-PENDING-BRANCH", 实际到货日期 = arrival },
            Detail("P-1", 1, "A-1", "商品一"));
        await _db.Insertable(new Store
        {
            StoreGUID = "G-INACTIVE",
            StoreCode = "S-INACTIVE",
            StoreName = "停用历史分店",
            IsActive = false,
        }).ExecuteCommandAsync();
        await SeedSaleAsync(arrival, "S-INACTIVE", " p-1 ", 5, 50, 30, 20, "ProductSnapshot");

        var result = await service.QueryBranchesAsync(
            "C-PENDING-BRANCH",
            new ContainerAllocationSalesBranchesQueryRequest
            {
                ProductCode = "P-1",
                StartDate = arrival,
                EndDate = arrival,
            }
        );

        Assert.Equal(SalesStatisticRefreshStatus.Pending, result.StatisticStatus);
        var branch = Assert.Single(result.Items);
        Assert.Equal("S-INACTIVE", branch.BranchCode);
        Assert.False(branch.IsActive);
        Assert.Equal(0, branch.AllocationQuantity);
        Assert.Null(branch.SalesQuantity);
        Assert.Null(branch.SalesAmount);
        Assert.Null(branch.AverageSalesPrice);
        Assert.Null(branch.GrossProfit);
        Assert.Null(branch.GrossMarginRate);
        Assert.Null(branch.IsGrossMarginComplete);
    }

    private ContainerAllocationSalesReportService CreateService(ContainerMainDto container, params ContainerDetailDto[] details)
    {
        var containerService = new Mock<IContainerReactService>();
        containerService.Setup(x => x.GetContainerDetailAsync(container.HGUID!)).ReturnsAsync(container);
        containerService.Setup(x => x.GetContainerProductsAsync(container.HGUID!)).ReturnsAsync(details.ToList());
        return new ContainerAllocationSalesReportService(
            CreateContext(_db),
            containerService.Object,
            NullLogger<ContainerAllocationSalesReportService>.Instance
        );
    }

    private static ContainerDetailDto Detail(string productCode, decimal quantity, string itemNumber, string name) => new()
    {
        商品编码 = productCode,
        装柜数量 = quantity,
        商品信息 = new ContainerProductInfoDto { 货号 = itemNumber, 商品名称 = name },
    };

    private static WareHouseOrderDetails OrderDetail(
        string detailGuid, string orderGuid, string productCode, string storeCode,
        decimal quantity, decimal importPrice, bool isDeleted = false) => new()
        {
            DetailGUID = detailGuid,
            OrderGUID = orderGuid,
            ProductCode = productCode,
            StoreCode = storeCode,
            AllocQuantity = quantity,
            ImportPrice = importPrice,
            IsDeleted = isDeleted,
        };

    private async Task SeedOrderAsync(
        string orderGuid, DateTime? orderDate, DateTime? outboundDate, bool isDeleted,
        params WareHouseOrderDetails[] details)
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            OrderDate = orderDate,
            OutboundDate = outboundDate,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
        await _db.Insertable(details).ExecuteCommandAsync();
    }

    private async Task SeedOrderWithFlowStatusAsync(
        string orderGuid,
        DateTime orderDate,
        int? flowStatus,
        params WareHouseOrderDetails[] details
    )
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            OrderDate = orderDate,
            FlowStatus = flowStatus,
        }).ExecuteCommandAsync();
        await _db.Insertable(details).ExecuteCommandAsync();
    }

    private async Task SeedOrderWithStoreAsync(
        string orderGuid,
        DateTime orderDate,
        string storeCode,
        params WareHouseOrderDetails[] details
    )
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            OrderDate = orderDate,
            StoreCode = storeCode,
        }).ExecuteCommandAsync();
        await _db.Insertable(details).ExecuteCommandAsync();
    }

    private async Task SeedSaleAsync(
        DateTime date, string branch, string productCode, int quantity, decimal amount,
        decimal? cost, decimal? grossProfit, string costSource, string supplierCode = "200")
    {
        await _db.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date,
            BranchCode = branch,
            SupplierCode = supplierCode,
            ProductCode = productCode,
            TotalQuantity = quantity,
            TotalAmount = amount,
            TotalCost = cost,
            GrossProfit = grossProfit,
            CostSource = costSource,
        }).ExecuteCommandAsync();
    }

    private async Task SeedFreshStatesAsync(DateTime startDate, DateTime endDate)
    {
        var states = Enumerable.Range(0, (endDate.Date - startDate.Date).Days + 1)
            .Select(offset => new SalesStatisticRefreshState
            {
                StatisticType = SalesStatisticType.ProductStoreDaily,
                Date = startDate.Date.AddDays(offset),
                Status = SalesStatisticRefreshStatus.Fresh,
            }).ToList();
        await _db.Insertable(states).ExecuteCommandAsync();
    }

    private static SqlSugarContext CreateContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (File.Exists(_dbPath)) SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
