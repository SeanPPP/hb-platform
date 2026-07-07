using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles;
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
            typeof(WarehouseCategory),
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
    public async Task GetContainersAsync_列头过滤应作用于全列表并更新总数()
    {
        await SeedContainerAsync(
            "C-LIST-1",
            "FFAU 7818368",
            loadingDate: new DateTime(2026, 6, 17),
            estimatedArrivalDate: new DateTime(2026, 7, 15),
            totalPieces: 909m,
            totalAmount: 339615m,
            totalVolume: 67.04m,
            status: 0
        );
        await SeedContainerAsync(
            "C-LIST-2",
            "CSGU7035442",
            loadingDate: new DateTime(2026, 6, 16),
            estimatedArrivalDate: new DateTime(2026, 7, 14),
            totalPieces: 1185m,
            totalAmount: 300397.50m,
            totalVolume: 68m,
            status: 1
        );
        await SeedContainerAsync(
            "C-LIST-3",
            "CSGU7030456",
            loadingDate: new DateTime(2026, 6, 16),
            estimatedArrivalDate: new DateTime(2026, 7, 14),
            totalPieces: 756m,
            totalAmount: 301886.40m,
            totalVolume: 63.56m,
            status: 1
        );
        var service = CreateService(CreateContainerListMapper());

        var result = await service.GetContainersAsync(
            new ContainerQueryRequest
            {
                Page = 1,
                PageSize = 20,
                ContainerNumberFilter = "CSGU",
                LoadingDateStart = new DateTime(2026, 6, 16),
                LoadingDateEnd = new DateTime(2026, 6, 16),
                EstimatedArrivalDateStart = new DateTime(2026, 7, 14),
                EstimatedArrivalDateEnd = new DateTime(2026, 7, 14),
                TotalPiecesMin = 1000m,
                TotalAmountMax = 310000m,
                TotalVolumeMin = 60m,
                TotalVolumeMax = 70m,
                Statuses = new List<int> { 1 },
            }
        );

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("CSGU7035442", Assert.Single(result.Containers).货柜编号);
    }

    [Fact]
    public async Task GetContainersAsync_实际到货日期结束日应包含整天()
    {
        await SeedContainerAsync(
            "C-ACTUAL-1",
            "OOLU9955404",
            actualArrivalDate: new DateTime(2026, 6, 8, 16, 30, 0),
            status: 2
        );
        await SeedContainerAsync(
            "C-ACTUAL-2",
            "FFAU2703638",
            actualArrivalDate: new DateTime(2026, 6, 9),
            status: 2
        );
        var service = CreateService(CreateContainerListMapper());

        var result = await service.GetContainersAsync(
            new ContainerQueryRequest
            {
                Page = 1,
                PageSize = 20,
                ActualArrivalDateStart = new DateTime(2026, 6, 8),
                ActualArrivalDateEnd = new DateTime(2026, 6, 8),
            }
        );

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("OOLU9955404", Assert.Single(result.Containers).货柜编号);
    }

    [Fact]
    public async Task GetContainersAsync_空列头过滤应保持原分页总数()
    {
        await SeedContainerAsync("C-EMPTY-1", "CSGU7035442", status: 1);
        await SeedContainerAsync("C-EMPTY-2", "CSGU7030456", status: 1);
        await SeedContainerAsync("C-EMPTY-3", "FFAU 7818368", status: 0);
        var service = CreateService(CreateContainerListMapper());

        var result = await service.GetContainersAsync(
            new ContainerQueryRequest
            {
                Page = 1,
                PageSize = 2,
                ContainerNumberFilter = " ",
                Statuses = new List<int>(),
            }
        );

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Containers.Count);
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
        Assert.True(result.TotalComputed);
        Assert.True(result.StatsComputed);
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
    public async Task QueryContainerDetailsAsync_禁用标签统计时应保留总数但标记未计算统计()
    {
        await SeedContainerAsync("C-NO-STATS", "CSLU6099488");
        await SeedDetailAsync("D-NO-STATS-1", "C-NO-STATS", "P-NO-STATS-1", "HB201", localExists: true);
        await SeedDetailAsync("D-NO-STATS-2", "C-NO-STATS", "P-NO-STATS-2", "HB202", localExists: false);
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-NO-STATS",
                PageNumber = 1,
                PageSize = 50,
                IncludeStats = false,
            }
        );

        Assert.Equal(2, result.ItemsTotal);
        Assert.True(result.TotalComputed);
        Assert.False(result.StatsComputed);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(0, result.TagStats.All);
        Assert.Equal(0, result.TagStats.New);
        Assert.Equal(0, result.TagStats.Existing);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_禁用总数时应多取一条判断是否还有下一页()
    {
        await SeedContainerAsync("C-NO-TOTAL", "CSLU6099489");
        await SeedDetailAsync("D-NO-TOTAL-1", "C-NO-TOTAL", "P-NO-TOTAL-1", "HB301");
        await SeedDetailAsync("D-NO-TOTAL-2", "C-NO-TOTAL", "P-NO-TOTAL-2", "HB302");
        await SeedDetailAsync("D-NO-TOTAL-3", "C-NO-TOTAL", "P-NO-TOTAL-3", "HB303");
        var service = CreateService();

        var firstPage = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-NO-TOTAL",
                PageNumber = 1,
                PageSize = 2,
                IncludeTotal = false,
                IncludeStats = false,
                SortBy = "itemNumber",
                SortOrder = "ascend",
            }
        );
        var lastPage = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-NO-TOTAL",
                PageNumber = 2,
                PageSize = 2,
                IncludeTotal = false,
                IncludeStats = false,
                SortBy = "itemNumber",
                SortOrder = "ascend",
            }
        );

        Assert.False(firstPage.TotalComputed);
        Assert.False(firstPage.StatsComputed);
        Assert.Equal(0, firstPage.ItemsTotal);
        Assert.True(firstPage.HasMore);
        Assert.Equal(new[] { "HB301", "HB302" }, firstPage.Items.Select(x => x.商品信息?.货号).ToArray());
        Assert.False(lastPage.TotalComputed);
        Assert.False(lastPage.StatsComputed);
        Assert.Equal(0, lastPage.ItemsTotal);
        Assert.False(lastPage.HasMore);
        Assert.Equal(new[] { "HB303" }, lastPage.Items.Select(x => x.商品信息?.货号).ToArray());
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_请求标签统计时应同步返回统计内总数()
    {
        await SeedContainerAsync("C-STATS-TOTAL", "CSLU6099490");
        await SeedDetailAsync("D-STATS-TOTAL-1", "C-STATS-TOTAL", "P-STATS-TOTAL-1", "HB401", localExists: true);
        await SeedDetailAsync("D-STATS-TOTAL-2", "C-STATS-TOTAL", "P-STATS-TOTAL-2", "HB402", localExists: false);
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-STATS-TOTAL",
                PageNumber = 1,
                PageSize = 1,
                IncludeTotal = false,
                IncludeStats = true,
                SortBy = "itemNumber",
                SortOrder = "ascend",
            }
        );

        Assert.True(result.TotalComputed);
        Assert.True(result.StatsComputed);
        Assert.Equal(2, result.ItemsTotal);
        Assert.Equal(2, result.TagStats.All);
        Assert.True(result.HasMore);
        Assert.Equal(new[] { "HB401" }, result.Items.Select(x => x.商品信息?.货号).ToArray());
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_实时仓库价列取仓库商品且保留明细快照()
    {
        await SeedContainerAsync("C-LAST-PRICE", "CSLU6099501");
        await SeedDetailAsync(
            "D-LAST-PRICE",
            "C-LAST-PRICE",
            "P-LAST-PRICE",
            "HB-LAST",
            oemPrice: 3m,
            importPrice: 2m,
            lastImportPrice: 1.25m,
            lastOemPrice: 2.75m
        );
        await _localDb.Updateable<WarehouseProduct>()
            .SetColumns(x => x.ImportPrice == 9.99m)
            .SetColumns(x => x.OEMPrice == 19.99m)
            .Where(x => x.ProductCode == "P-LAST-PRICE")
            .ExecuteCommandAsync();
        var service = CreateService(CreateContainerDetailMapper());

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-LAST-PRICE",
                PageNumber = 1,
                PageSize = 50,
            }
        );

        var item = Assert.Single(result.Items);
        Assert.Equal(1.25m, item.LastImportPrice);
        Assert.Equal(2.75m, item.LastOEMPrice);
        Assert.Equal(9.99m, item.WarehouseImportPrice);
        Assert.Equal(19.99m, item.WarehouseOEMPrice);

        var legacyItem = Assert.Single(await service.GetContainerProductsAsync("C-LAST-PRICE"));
        Assert.Equal(1.25m, legacyItem.LastImportPrice);
        Assert.Equal(2.75m, legacyItem.LastOEMPrice);
        Assert.Equal(9.99m, legacyItem.WarehouseImportPrice);
        Assert.Equal(19.99m, legacyItem.WarehouseOEMPrice);

        var filteredItem = Assert.Single(await service.GetFilteredContainerProductsAsync(new ContainerQueryRequest
        {
            SortBy = "ProductCode",
        }));
        Assert.Equal(1.25m, filteredItem.LastImportPrice);
        Assert.Equal(2.75m, filteredItem.LastOEMPrice);
        Assert.Equal(9.99m, filteredItem.WarehouseImportPrice);
        Assert.Equal(19.99m, filteredItem.WarehouseOEMPrice);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_实时仓库价筛选排序使用仓库商品表()
    {
        await SeedContainerAsync("C-REALTIME-PRICE", "CMAU0000002");
        await SeedDetailAsync(
            "D-REALTIME-A",
            "C-REALTIME-PRICE",
            "P-REALTIME-A",
            "HB-REAL-A",
            importPrice: 5m,
            oemPrice: 1m,
            lastImportPrice: 99m,
            lastOemPrice: 99m,
            warehouseImportPrice: 2.22m,
            warehouseOemPrice: 8.88m
        );
        await SeedDetailAsync(
            "D-REALTIME-B",
            "C-REALTIME-PRICE",
            "P-REALTIME-B",
            "HB-REAL-B",
            importPrice: 6m,
            oemPrice: 2m,
            lastImportPrice: 1m,
            lastOemPrice: 1m,
            warehouseImportPrice: 4.44m,
            warehouseOemPrice: 3.33m
        );
        var service = CreateService();

        var importFiltered = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-REALTIME-PRICE",
                PageNumber = 1,
                PageSize = 50,
                WarehouseImportPriceMin = 4m,
            }
        );
        var retailFiltered = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-REALTIME-PRICE",
                PageNumber = 1,
                PageSize = 50,
                LastOEMPriceMax = 5m,
            }
        );
        var importSorted = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-REALTIME-PRICE",
                PageNumber = 1,
                PageSize = 50,
                SortBy = "warehouseImportPrice",
                SortOrder = "ascend",
            }
        );
        var retailSorted = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-REALTIME-PRICE",
                PageNumber = 1,
                PageSize = 50,
                SortBy = "lastOEMPrice",
                SortOrder = "descend",
            }
        );

        Assert.Equal(new[] { "D-REALTIME-B" }, importFiltered.Items.Select(x => x.HGUID).ToArray());
        Assert.Equal(new[] { "D-REALTIME-B" }, retailFiltered.Items.Select(x => x.HGUID).ToArray());
        Assert.Equal(new[] { "D-REALTIME-A", "D-REALTIME-B" }, importSorted.Items.Select(x => x.HGUID).ToArray());
        Assert.Equal(new[] { "D-REALTIME-A", "D-REALTIME-B" }, retailSorted.Items.Select(x => x.HGUID).ToArray());
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_零售价筛选排序新商品用明细已有商品用仓库()
    {
        await SeedContainerAsync("C-OEM-VISIBLE", "CMAU0000003");
        await SeedDetailAsync(
            "D-OEM-VISIBLE-NEW",
            "C-OEM-VISIBLE",
            "P-OEM-VISIBLE-NEW",
            "HB-OEM-NEW",
            oemPrice: 7m,
            warehouseOemPrice: 1m,
            localExists: false
        );
        await SeedDetailAsync(
            "D-OEM-VISIBLE-EXISTING-HIGH",
            "C-OEM-VISIBLE",
            "P-OEM-VISIBLE-EXISTING-HIGH",
            "HB-OEM-HIGH",
            oemPrice: 1m,
            warehouseOemPrice: 9m,
            localExists: true
        );
        await SeedDetailAsync(
            "D-OEM-VISIBLE-EXISTING-LOW",
            "C-OEM-VISIBLE",
            "P-OEM-VISIBLE-EXISTING-LOW",
            "HB-OEM-LOW",
            oemPrice: 9m,
            warehouseOemPrice: 2m,
            localExists: true
        );
        var service = CreateService();

        var filtered = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-OEM-VISIBLE",
                PageNumber = 1,
                PageSize = 50,
                OemPriceMin = 6m,
            }
        );
        var maxFiltered = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-OEM-VISIBLE",
                PageNumber = 1,
                PageSize = 50,
                OemPriceMax = 6m,
            }
        );
        var sorted = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-OEM-VISIBLE",
                PageNumber = 1,
                PageSize = 50,
                SortBy = "oemPrice",
                SortOrder = "ascend",
            }
        );

        Assert.Equal(
            new[] { "D-OEM-VISIBLE-EXISTING-HIGH", "D-OEM-VISIBLE-NEW" },
            filtered.Items.Select(x => x.HGUID).OrderBy(x => x).ToArray()
        );
        Assert.Equal(new[] { "D-OEM-VISIBLE-EXISTING-LOW" }, maxFiltered.Items.Select(x => x.HGUID).ToArray());
        Assert.Equal(
            new[] { "D-OEM-VISIBLE-EXISTING-LOW", "D-OEM-VISIBLE-NEW", "D-OEM-VISIBLE-EXISTING-HIGH" },
            sorted.Items.Select(x => x.HGUID).ToArray()
        );
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_只读贴牌价新商品取国内已有商品取仓库()
    {
        await SeedContainerAsync("C-READONLY-OEM", "CMAU0000001");
        await SeedDetailAsync(
            "D-READONLY-NEW",
            "C-READONLY-OEM",
            "P-READONLY-NEW",
            "HB-RO-NEW",
            oemPrice: 3m,
            domesticOemPrice: 6.66m,
            warehouseOemPrice: 9.99m,
            localExists: false
        );
        await SeedDetailAsync(
            "D-READONLY-EXISTING",
            "C-READONLY-OEM",
            "P-READONLY-EXISTING",
            "HB-RO-EXISTING",
            oemPrice: 4m,
            domesticOemPrice: 7.77m,
            warehouseOemPrice: 8.88m,
            localExists: true
        );
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-READONLY-OEM",
                PageNumber = 1,
                PageSize = 50,
                SortBy = "itemNumber",
                SortOrder = "ascend",
            }
        );
        var legacyList = await service.GetContainerProductsAsync("C-READONLY-OEM");

        var newItem = result.Items.Single(x => x.HGUID == "D-READONLY-NEW");
        var existingItem = result.Items.Single(x => x.HGUID == "D-READONLY-EXISTING");
        Assert.Equal(6.66m, newItem.ReadonlyOemPrice);
        Assert.Equal(8.88m, existingItem.ReadonlyOemPrice);
        Assert.Equal(3m, newItem.贴牌价格);
        Assert.Equal(4m, existingItem.贴牌价格);
        Assert.Equal(6.66m, legacyList.Single(x => x.HGUID == "D-READONLY-NEW").ReadonlyOemPrice);
        Assert.Equal(8.88m, legacyList.Single(x => x.HGUID == "D-READONLY-EXISTING").ReadonlyOemPrice);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_已有商品英文名称应优先读取本地主档商品名称()
    {
        await SeedContainerAsync("C-LOCAL-ENGLISH", "CBHU8299137");
        await SeedDetailAsync(
            "D-LOCAL-ENGLISH",
            "C-LOCAL-ENGLISH",
            "P-LOCAL-ENGLISH",
            "8107779",
            localExists: true
        );
        await SeedDetailAsync(
            "D-DOMESTIC-ENGLISH",
            "C-LOCAL-ENGLISH",
            "P-DOMESTIC-ENGLISH",
            "8107780",
            localExists: false
        );
        await _localDb.Updateable<DomesticProduct>()
            .SetColumns(x => x.EnglishProductName == null)
            .Where(x => x.ProductCode == "P-LOCAL-ENGLISH")
            .ExecuteCommandAsync();
        await _localDb.Updateable<Product>()
            .SetColumns(x => x.ProductName == "Alpha Local Master Name")
            .Where(x => x.ProductCode == "P-LOCAL-ENGLISH")
            .ExecuteCommandAsync();
        await _localDb.Updateable<DomesticProduct>()
            .SetColumns(x => x.EnglishProductName == "Zulu Domestic English Name")
            .Where(x => x.ProductCode == "P-DOMESTIC-ENGLISH")
            .ExecuteCommandAsync();
        var service = CreateService();

        var sorted = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-LOCAL-ENGLISH",
                PageNumber = 1,
                PageSize = 50,
                SortBy = "englishName",
                SortOrder = "ascend",
            }
        );
        var filtered = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-LOCAL-ENGLISH",
                PageNumber = 1,
                PageSize = 50,
                EnglishName = "Alpha Local",
            }
        );
        var legacyList = await service.GetContainerProductsAsync("C-LOCAL-ENGLISH");

        Assert.Equal(new[] { "D-LOCAL-ENGLISH", "D-DOMESTIC-ENGLISH" }, sorted.Items.Select(x => x.HGUID).ToArray());
        Assert.Equal("Alpha Local Master Name", sorted.Items[0].商品信息?.英文名称);
        Assert.Equal("Zulu Domestic English Name", sorted.Items[1].商品信息?.英文名称);
        Assert.Equal("D-LOCAL-ENGLISH", Assert.Single(filtered.Items).HGUID);
        Assert.Equal(
            "Alpha Local Master Name",
            legacyList.Single(item => item.HGUID == "D-LOCAL-ENGLISH").商品信息?.英文名称
        );
    }

    [Fact]
    public async Task BackfillLastPricesByScopeAsync_只填空快照且不覆盖已有值()
    {
        await SeedContainerAsync("C-BACKFILL", "CSLU6099502");
        await SeedContainerAsync("C-BACKFILL-OTHER", "CSLU6099504");
        await SeedDetailAsync("D-BACKFILL-EMPTY", "C-BACKFILL", "P-BACKFILL-EMPTY", "HB-BE", oemPrice: 4m, importPrice: 3m);
        await SeedDetailAsync(
            "D-BACKFILL-EXISTING",
            "C-BACKFILL",
            "P-BACKFILL-EXISTING",
            "HB-BX",
            oemPrice: 8m,
            importPrice: 7m,
            lastImportPrice: 1.11m,
            lastOemPrice: 2.22m
        );
        await SeedDetailAsync("D-BACKFILL-OTHER", "C-BACKFILL-OTHER", "P-BACKFILL-OTHER", "HB-BO", oemPrice: 6m, importPrice: 5m);
        var service = CreateService();

        var updated = await service.BackfillLastPricesByScopeAsync(
            "C-BACKFILL",
            new ContainerDetailBatchScopeDto
            {
                SelectedHguids = new List<string> { "D-BACKFILL-EMPTY", "D-BACKFILL-EXISTING", "D-BACKFILL-OTHER" },
            }
        );

        var empty = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-BACKFILL-EMPTY");
        var existing = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-BACKFILL-EXISTING");
        var otherContainerDetail = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-BACKFILL-OTHER");
        Assert.Equal(1, updated);
        Assert.Equal(3m, empty.LastImportPrice);
        Assert.Equal(4m, empty.LastOEMPrice);
        Assert.Equal(1.11m, existing.LastImportPrice);
        Assert.Equal(2.22m, existing.LastOEMPrice);
        Assert.Null(otherContainerDetail.LastImportPrice);
        Assert.Null(otherContainerDetail.LastOEMPrice);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_单件运输成本筛选应按展示值两位小数比较()
    {
        await SeedContainerAsync("C-UNIT-TRANSPORT", "CSLU6099505");
        await SeedDetailAsync("D-UNIT-ROUND-UP", "C-UNIT-TRANSPORT", "P-UNIT-ROUND-UP", "HB-UR", oemPrice: 4m, importPrice: 3m);
        await SeedDetailAsync("D-UNIT-ROUND-DOWN", "C-UNIT-TRANSPORT", "P-UNIT-ROUND-DOWN", "HB-UD", oemPrice: 4m, importPrice: 3m);
        await _localDb.Updateable<ContainerDetail>()
            .SetColumns(x => x.TransportCost == 0.08m)
            .SetColumns(x => x.PackingQuantity == 6.20m)
            .Where(x => x.DetailCode == "D-UNIT-ROUND-UP")
            .ExecuteCommandAsync();
        await _localDb.Updateable<ContainerDetail>()
            .SetColumns(x => x.TransportCost == 0.08m)
            .SetColumns(x => x.PackingQuantity == 6.18m)
            .Where(x => x.DetailCode == "D-UNIT-ROUND-DOWN")
            .ExecuteCommandAsync();
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-UNIT-TRANSPORT",
                UnitTransportCostMin = 0.50m,
                UnitTransportCostMax = 0.50m,
                PageNumber = 1,
                PageSize = 50,
            }
        );

        var item = Assert.Single(result.Items);
        Assert.Equal("D-UNIT-ROUND-UP", item.HGUID);
    }

    [Fact]
    public async Task AssignProductsAsync_新建明细应写入上次价格快照且更新已有明细不覆盖()
    {
        await SeedContainerAsync("C-ASSIGN-LAST", "CSLU6099503");
        await _localDb.Insertable(new WarehouseProduct { ProductCode = "P-ASSIGN-NEW", ImportPrice = 6.6m, OEMPrice = 9.9m, IsActive = true }).ExecuteCommandAsync();
        await _localDb.Insertable(new DomesticProduct { ProductCode = "P-ASSIGN-NEW", HBProductNo = "HB-AN", IsDeleted = false }).ExecuteCommandAsync();
        await SeedDetailAsync(
            "D-ASSIGN-EXISTING",
            "C-ASSIGN-LAST",
            "P-ASSIGN-EXISTING",
            "HB-AE",
            oemPrice: 4m,
            importPrice: 3m,
            lastImportPrice: 1.23m,
            lastOemPrice: 4.56m
        );
        var service = CreateService();

        var result = await service.AssignProductsAsync(
            "C-ASSIGN-LAST",
            new List<AssignProductItemDto>
            {
                new() { ProductCode = "P-ASSIGN-NEW", Quantity = 1m, PackingQuantity = 12m, UnitVolume = 0.1m },
                new() { ProductCode = "P-ASSIGN-EXISTING", Quantity = 1m, PackingQuantity = 24m, UnitVolume = 0.2m },
            },
            "increase",
            null
        );

        var created = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.ContainerCode == "C-ASSIGN-LAST" && x.ProductCode == "P-ASSIGN-NEW");
        var existing = await _localDb.Queryable<ContainerDetail>()
            .SingleAsync(x => x.DetailCode == "D-ASSIGN-EXISTING");
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(6.6m, created.LastImportPrice);
        Assert.Equal(9.9m, created.LastOEMPrice);
        Assert.Equal(1.23m, existing.LastImportPrice);
        Assert.Equal(4.56m, existing.LastOEMPrice);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_应返回仓库分类到明细和商品信息()
    {
        await SeedContainerAsync("C-CATEGORY", "CSLU6099490");
        await SeedWarehouseCategoryAsync("CAT-LAUNDRY", "Laundry");
        await SeedDetailAsync(
            "D-CATEGORY",
            "C-CATEGORY",
            "P-CATEGORY",
            "HB-CATEGORY",
            warehouseCategoryGuid: " cat-laundry "
        );
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-CATEGORY",
                PageNumber = 1,
                PageSize = 50,
            }
        );

        var detail = Assert.Single(result.Items);
        Assert.Equal("CAT-LAUNDRY", detail.ProductCategoryGUID);
        Assert.Equal("Laundry", detail.ProductCategoryName);
        Assert.NotNull(detail.商品信息);
        Assert.Equal("CAT-LAUNDRY", detail.商品信息!.ProductCategoryGUID);
        Assert.Equal("Laundry", detail.商品信息.ProductCategoryName);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_目标分类应优先于本地商品分类展示()
    {
        await SeedContainerAsync("C-TARGET-CATEGORY", "CSLU6099491");
        await SeedWarehouseCategoryAsync("CAT-OLD", "Old Category");
        await SeedWarehouseCategoryAsync("CAT-TARGET", "Target Category");
        await SeedDetailAsync(
            "D-TARGET-CATEGORY",
            "C-TARGET-CATEGORY",
            "P-TARGET-CATEGORY",
            "HB-TARGET-CATEGORY",
            warehouseCategoryGuid: "CAT-OLD",
            targetWarehouseCategoryGuid: " cat-target "
        );
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-TARGET-CATEGORY",
                PageNumber = 1,
                PageSize = 50,
            }
        );

        var detail = Assert.Single(result.Items);
        Assert.Equal("CAT-TARGET", detail.ProductCategoryGUID);
        Assert.Equal("Target Category", detail.ProductCategoryName);
        Assert.NotNull(detail.商品信息);
        Assert.Equal("CAT-TARGET", detail.商品信息!.ProductCategoryGUID);
        Assert.Equal("Target Category", detail.商品信息.ProductCategoryName);
    }

    [Fact]
    public async Task QueryContainerDetailsAsync_应按国内商品页规则补齐商品图片()
    {
        await SeedContainerAsync("C-IMAGE", "OOLU9955404");
        await SeedDetailAsync(
            "D-IMAGE-GENERATED",
            "C-IMAGE",
            "P-IMAGE-GENERATED",
            "HB249-HM-001",
            productImage: null
        );
        await SeedDetailAsync(
            "D-IMAGE-EXISTING",
            "C-IMAGE",
            "P-IMAGE-EXISTING",
            "HB249-HM-002",
            productImage: "https://cdn.example.test/HB249-HM-002.jpg"
        );
        var service = CreateService();

        var result = await service.QueryContainerDetailsAsync(
            new ContainerDetailQueryDto
            {
                ContainerGuid = "C-IMAGE",
                PageNumber = 1,
                PageSize = 50,
                SortBy = "itemNumber",
                SortOrder = "ascend",
            }
        );

        Assert.Equal(
            "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/HB249-HM-001.jpg",
            result.Items.Single(item => item.HGUID == "D-IMAGE-GENERATED").商品信息?.商品图片
        );
        Assert.Equal(
            "https://cdn.example.test/HB249-HM-002.jpg",
            result.Items.Single(item => item.HGUID == "D-IMAGE-EXISTING").商品信息?.商品图片
        );
    }

    [Fact]
    public async Task GetContainerProductsAsync_应按国内商品页规则补齐商品图片()
    {
        await SeedContainerAsync("C-LIST-IMAGE", "OOLU9955404");
        await SeedDetailAsync(
            "D-LIST-IMAGE",
            "C-LIST-IMAGE",
            "P-LIST-IMAGE",
            "HB249-HM-001",
            productImage: null
        );
        var service = CreateService();

        var result = await service.GetContainerProductsAsync("C-LIST-IMAGE");

        var detail = Assert.Single(result);
        Assert.Equal(
            "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/HB249-HM-001.jpg",
            detail.商品信息?.商品图片
        );
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

    private async Task SeedContainerAsync(
        string containerCode,
        string containerNumber,
        DateTime? loadingDate = null,
        DateTime? estimatedArrivalDate = null,
        DateTime? actualArrivalDate = null,
        decimal? totalPieces = null,
        decimal? totalAmount = null,
        decimal? totalVolume = null,
        int? status = null
    )
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = containerCode,
                ContainerNumber = containerNumber,
                LoadingDate = loadingDate ?? new DateTime(2026, 5, 12),
                EstimatedArrivalDate = estimatedArrivalDate ?? new DateTime(2026, 6, 2),
                ActualArrivalDate = actualArrivalDate ?? new DateTime(2026, 6, 8),
                ExchangeRate = 4.5m,
                ShippingFee = 12000m,
                TotalPieces = totalPieces,
                TotalAmount = totalAmount,
                TotalVolume = totalVolume ?? 69.868m,
                Status = status ?? 2,
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
        string detailProductType = "普通商品",
        string? warehouseCategoryGuid = null,
        string? targetWarehouseCategoryGuid = null,
        string? productImage = "__DEFAULT__",
        decimal? lastImportPrice = null,
        decimal? lastOemPrice = null,
        decimal? domesticOemPrice = null,
        decimal? warehouseImportPrice = null,
        decimal? warehouseOemPrice = null
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
                LastImportPrice = lastImportPrice,
                LastOEMPrice = lastOemPrice,
                TransportCost = 0.5m,
                Remarks = $"备注 {itemNumber}",
                TargetWarehouseCategoryGUID = targetWarehouseCategoryGuid,
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
                ProductImage = productImage == "__DEFAULT__" ? $"https://example.test/{itemNumber}.jpg" : productImage,
                MiddlePackQuantity = middlePackQuantity,
                ProductType = domesticProductType,
                OEMPrice = domesticOemPrice ?? oemPrice,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new WarehouseProduct
            {
                ProductCode = productCode,
                ImportPrice = warehouseImportPrice ?? importPrice,
                OEMPrice = warehouseOemPrice ?? oemPrice,
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
                    WarehouseCategoryGUID = warehouseCategoryGuid,
                    IsActive = isActive,
                }
            ).ExecuteCommandAsync();
        }
    }

    private async Task SeedWarehouseCategoryAsync(string categoryGuid, string categoryName)
    {
        await _localDb.Insertable(
            new WarehouseCategory
            {
                CategoryGUID = categoryGuid,
                CategoryName = categoryName,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
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

    private static IMapper CreateContainerListMapper()
    {
        var mapper = new Mock<IMapper>();
        mapper
            .Setup(x => x.Map<List<ContainerMainDto>>(It.IsAny<List<Container>>()))
            .Returns((List<Container> containers) =>
                containers
                    .Select(container => new ContainerMainDto
                    {
                        HGUID = container.ContainerCode,
                        货柜编号 = container.ContainerNumber,
                        装柜日期 = container.LoadingDate,
                        预计到岸日期 = container.EstimatedArrivalDate,
                        实际到货日期 = container.ActualArrivalDate,
                        合计件数 = container.TotalPieces,
                        合计金额 = container.TotalAmount,
                        总体积 = container.TotalVolume,
                        状态 = container.Status,
                    })
                    .ToList()
            );
        return mapper.Object;
    }

    private static IMapper CreateContainerDetailMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<ContainerMappingProfile>(),
            NullLoggerFactory.Instance
        ).CreateMapper();

    private ContainerReactService CreateService(IMapper? mapper = null)
    {
        return new ContainerReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(),
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            new ConfigurationBuilder().Build(),
            mapper ?? Mock.Of<IMapper>(),
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
        var dbField = typeof(HBSalesSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }
}
