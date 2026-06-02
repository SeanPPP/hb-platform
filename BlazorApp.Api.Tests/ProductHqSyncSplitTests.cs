using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("ProductHqSyncServiceTests")]
public sealed class ProductHqSyncSplitTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;
    private readonly IMapper _mapper;

    public ProductHqSyncSplitTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
        _mapper = CreateMapper();

        // 商品 HQ 解耦同步只需要最小表集合，关联表用来验证全量同步不会误触碰。
        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(ProductSetCode),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct)
        );
        _hqDb.CodeFirst.InitTables(typeof(DIC_商品信息字典表), typeof(DIC_一品多码表));
    }

    [Fact]
    public async Task SyncFullAsync_只处理Product主表_不触碰价格和多码关联表()
    {
        var now = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqProductAsync("P-HQ-001", now, "HQ商品");
        await _localDb.Insertable(
            new StoreRetailPrice
            {
                UUID = "retail-keep",
                StoreCode = "S01",
                ProductCode = "P-LOCAL-ONLY",
                SupplierCode = "SUP",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new StoreMultiCodeProduct
            {
                UUID = "multi-keep",
                StoreCode = "S01",
                ProductCode = "P-LOCAL-ONLY",
                MultiCodeProductCode = "M01",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _localDb.Insertable(
            new ProductSetCode
            {
                SetCodeId = "set-keep",
                ProductCode = "P-LOCAL-ONLY",
                SetProductCode = "M01",
                SetItemNumber = "M01",
                SetType = 2,
                SetQuantity = 1,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var result = await CreateService().SyncFullAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ProductsAdded);
        Assert.True(result.Data.ProductsSwapped);
        Assert.NotNull(await _localDb.Queryable<Product>().SingleAsync(x => x.ProductCode == "P-HQ-001"));
        Assert.False((await _localDb.Queryable<StoreRetailPrice>().SingleAsync(x => x.UUID == "retail-keep")).IsDeleted);
        Assert.False((await _localDb.Queryable<StoreMultiCodeProduct>().SingleAsync(x => x.UUID == "multi-keep")).IsDeleted);
        Assert.False((await _localDb.Queryable<ProductSetCode>().SingleAsync(x => x.SetCodeId == "set-keep")).IsDeleted);
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hqConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
    }

    private ProductHqSyncService CreateService()
    {
        return new ProductHqSyncService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            _mapper,
            NullLogger<ProductHqSyncService>.Instance
        );
    }

    private async Task SeedHqProductAsync(string productCode, DateTime lastModifyDate, string name)
    {
        await _hqDb.Insertable(
            new DIC_商品信息字典表
            {
                ID = Math.Abs(productCode.GetHashCode(StringComparison.Ordinal)) % 100000 + 1,
                HGUID = $"hq-{productCode}",
                H商品标签GUID = $"tag-{productCode}",
                H商品分类码GUID = "CAT",
                H供货商编码 = "SUP",
                H商品编码 = productCode,
                H货号 = $"ITEM-{productCode}",
                H主条形码 = $"BAR-{productCode}",
                H商品名称 = name,
                H商品类型 = 1,
                H大写名称 = name.ToUpperInvariant(),
                H规格 = "默认规格",
                H单位 = "EA",
                H进货价 = 1.2m,
                H零售价 = 2.3m,
                H是否自动定价 = false,
                H商品图片 = "image.png",
                中包数量 = 1,
                H腾讯云图地址 = "https://example.invalid/image.png",
                H使用状态 = true,
                H是否特殊商品 = false,
                H进货单主表GUID = $"order-{productCode}",
                H进货单详情GUID = $"order-detail-{productCode}",
                CBP商品中文名称 = name,
                CBP供应商编码 = "SUP",
                CBP商品分类码GUID = "WAREHOUSE",
                FGC_Creator = "HQ",
                FGC_CreateDate = lastModifyDate.AddDays(-1),
                FGC_LastModifier = "HQ",
                FGC_LastModifyDate = lastModifyDate,
                FGC_UpdateHelp = "test",
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
        };

    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(
            cfg =>
            {
                cfg.AddProfile<ReactProductMappingProfile>();
                cfg.AddProfile<ReactProductSetCodeMappingProfile>();
            },
            NullLoggerFactory.Instance
        );
        return configuration.CreateMapper();
    }

    private static IConfiguration CreateHqConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StoreHzgHQConnection"] = connectionString,
                }
            )
            .Build();

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(
        ISqlSugarClient db,
        IConfiguration configuration
    )
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        typeof(HqSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        typeof(HqSqlSugarContext)
            .GetField("<Configuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, configuration);
        return context;
    }
}
