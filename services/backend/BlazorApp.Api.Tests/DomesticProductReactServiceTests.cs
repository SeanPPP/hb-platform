using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
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
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hbSalesDb;

    public DomesticProductReactServiceTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hbSalesConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _localConnection.Open();
        _hbSalesConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hbSalesDb = new SqlSugarScope(CreateConnectionConfig(_hbSalesConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(typeof(DomesticProduct), typeof(DomesticSetProduct));
        _hbSalesDb.CodeFirst.InitTables(
            typeof(CPT_DIC_商品信息字典表),
            typeof(CPT_DIC_商品套装信息表)
        );
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
        _localConnection.Dispose();
        _hbSalesConnection.Dispose();

        if (File.Exists(_localDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        if (File.Exists(_hbSalesDbPath))
            SqliteTempFileCleanup.DeleteIfExists(_hbSalesDbPath);
    }

    private DomesticProductReactService CreateService()
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
            CreateHqSqlSugarContext()
        );
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

    private static HqSqlSugarContext CreateHqSqlSugarContext()
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);
        return context;
    }
}
