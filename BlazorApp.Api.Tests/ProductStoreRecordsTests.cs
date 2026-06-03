using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductStoreRecordsTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hqDb;
    private readonly IMapper _mapper;

    public ProductStoreRecordsTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarScope(CreateConnectionConfig(_hqConnection.ConnectionString));
        _mapper = CreateMapper();

        _localDb.CodeFirst.InitTables(
            typeof(Product),
            typeof(Store),
            typeof(StoreRetailPrice)
        );
    }

    [Fact]
    public async Task GetPagedListAsync_返回当前页商品已有分店价格记录数量且排除软删记录()
    {
        await SeedProductAsync("P001", "A001");
        await SeedProductAsync("P002", "A002");
        await SeedStoreAsync("S01", "分店一");
        await SeedStoreAsync("S02", "分店二");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false);
        await SeedStoreRetailPriceAsync("price-2", "P001", "S02", false);
        await SeedStoreRetailPriceAsync("price-deleted", "P001", "S03", true);

        var result = await CreateService().GetPagedListAsync(new ProductReactFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            SortBy = "productcode",
            SortOrder = "asc"
        });

        Assert.Equal(2, result.Items.Single(item => item.ProductCode == "P001").StoreRecordCount);
        Assert.Equal(0, result.Items.Single(item => item.ProductCode == "P002").StoreRecordCount);
    }

    [Fact]
    public async Task GetStoreRecordsAsync_只返回指定商品当前用户可访问的未删除分店记录并补充分店名称()
    {
        await SeedProductAsync("P001", "A001");
        await SeedStoreAsync("S01", "分店一");
        await SeedStoreAsync("S02", "分店二");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S02", false, 1.2m, 2.5m);
        await SeedStoreRetailPriceAsync("price-2", "P001", "S01", false, 1.1m, 2.4m);
        await SeedStoreRetailPriceAsync("price-deleted", "P001", "S03", true);
        await SeedStoreRetailPriceAsync("price-other", "P002", "S01", false);

        var response = await CreateService().GetStoreRecordsAsync("P001", new[] { "S01" });

        Assert.True(response.Success, response.Message);
        var records = response.Data ?? new List<ProductStoreRecordDto>();
        Assert.Equal(new[] { "S01" }, records.Select(item => item.StoreCode).ToArray());
        Assert.Equal("分店一", records[0].StoreName);
        Assert.Equal("S01-P001", records[0].StoreProductCode);
        Assert.Equal(1.1m, records[0].PurchasePrice);
        Assert.Equal(2.4m, records[0].StoreRetailPriceValue);
    }

    [Fact]
    public async Task GetStoreRecordsAsync_按分店名称升序返回且空名称按分店代码兜底排序()
    {
        await SeedProductAsync("P001", "A001");
        await SeedStoreAsync("S01", "Beta");
        await SeedStoreAsync("S02", "Gamma");
        await SeedStoreAsync("S03", "Alpha");
        await SeedStoreAsync("S04", "Alpha");
        await SeedStoreAsync("S99", "");
        await SeedStoreRetailPriceAsync("price-beta", "P001", "S01", false);
        await SeedStoreRetailPriceAsync("price-gamma", "P001", "S02", false);
        await SeedStoreRetailPriceAsync("price-alpha-3", "P001", "S03", false);
        await SeedStoreRetailPriceAsync("price-alpha-4", "P001", "S04", false);
        await SeedStoreRetailPriceAsync("price-empty", "P001", "S99", false);

        var response = await CreateService().GetStoreRecordsAsync("P001", null);

        Assert.True(response.Success, response.Message);
        var records = response.Data ?? new List<ProductStoreRecordDto>();
        Assert.Equal(new[] { "S03", "S04", "S01", "S02", "S99" }, records.Select(item => item.StoreCode).ToArray());
    }

    [Fact]
    public async Task GetStoreRecordsAsync_当前用户没有可访问分店时返回空列表()
    {
        await SeedProductAsync("P001", "A001");
        await SeedStoreAsync("S01", "分店一");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false);

        var response = await CreateService().GetStoreRecordsAsync("P001", Array.Empty<string>());

        Assert.True(response.Success, response.Message);
        Assert.Empty(response.Data ?? new List<ProductStoreRecordDto>());
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

    private ProductReactService CreateService()
    {
        return new ProductReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            _mapper,
            NullLogger<ProductReactService>.Instance,
            new HttpContextAccessor()
        );
    }

    private async Task SeedProductAsync(string productCode, string itemNumber)
    {
        await _localDb.Insertable(new Product
        {
            UUID = $"product-{productCode}",
            ProductCode = productCode,
            ItemNumber = itemNumber,
            Barcode = $"barcode-{productCode}",
            ProductName = $"商品{productCode}",
            PurchasePrice = 1,
            RetailPrice = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeCode, string storeName)
    {
        await _localDb.Insertable(new Store
        {
            StoreGUID = $"store-{storeCode}",
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreRetailPriceAsync(
        string uuid,
        string productCode,
        string storeCode,
        bool isDeleted,
        decimal purchasePrice = 1,
        decimal retailPrice = 2)
    {
        await _localDb.Insertable(new StoreRetailPrice
        {
            UUID = uuid,
            StoreCode = storeCode,
            ProductCode = productCode,
            StoreProductCode = $"{storeCode}-{productCode}",
            PurchasePrice = purchasePrice,
            StoreRetailPriceValue = retailPrice,
            DiscountRate = 0.9m,
            IsActive = true,
            IsAutoPricing = false,
            IsSpecialProduct = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "tester",
            IsDeleted = isDeleted,
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

    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<ReactProductMappingProfile>(),
            NullLoggerFactory.Instance
        );
        return configuration.CreateMapper();
    }

    private static IConfiguration CreateHqConfiguration(string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StoreHzgHQConnection"] = connectionString,
            })
            .Build();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(ISqlSugarClient db, IConfiguration configuration)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        var configurationField = typeof(HqSqlSugarContext).GetField(
            "<Configuration>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        configurationField!.SetValue(context, configuration);
        return context;
    }
}
