using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreProductMaintenanceLookupTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lookup-{Guid.NewGuid():N}.db");
    private readonly SqlSugarClient _db;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly StoreProductMaintenanceReactService _service;

    public StoreProductMaintenanceLookupTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"Data Source={_dbPath}",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(typeof(Product), typeof(ProductGrade), typeof(ProductSetCode), typeof(StoreClearancePrice));
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, _db);
        _service = new StoreProductMaintenanceReactService(
            context, NullLogger<StoreProductMaintenanceReactService>.Instance,
            Mock.Of<IAutoPricingService>(), _cache, Mock.Of<IWarehouseProductChangeHistoryService>(),
            Mock.Of<ICurrentUserService>(), Mock.Of<IProductMaintenanceHqProjectionWriter>());
    }

    public static IEnumerable<object[]> SpacePairs()
    {
        foreach (var space in "\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u202f\u205f\u3000")
        {
            yield return new object[] { $"ttete{space}stest", "ttete stest" };
            yield return new object[] { "ttete stest", $"ttete{space}stest" };
        }
    }

    [Theory]
    [MemberData(nameof(SpacePairs))]
    [InlineData("ttete\u2006\u00a0stest", "ttete  stest")]
    [InlineData("ttete\u2006middle stest", "ttete middle\u3000stest")]
    public async Task Lookup_不同空格双向匹配且保留原货号(string stored, string query)
    {
        await SeedProduct("product", stored);
        var item = Assert.Single(await Lookup(query));
        Assert.Equal("product", item.ProductCode);
        Assert.Equal("ItemNumber", item.MatchSource);
        Assert.Equal(stored, item.ItemNumber);
        Assert.Equal(stored, (await _db.Queryable<Product>().SingleAsync()).ItemNumber);
    }

    [Fact]
    public async Task Lookup_返回全部等价货号且排除删除商品()
    {
        await SeedProduct("ordinary", "ttete stest");
        await SeedProduct("unicode", "ttete\u2006stest");
        await SeedProduct("deleted", "ttete\u2006stest", deleted: true);
        var matches = await Lookup("ttete stest");
        Assert.Equal(new[] { "ordinary", "unicode" }, matches.Select(x => x.ProductCode).OrderBy(x => x));
    }

    [Theory]
    [InlineData("ttetestest")]
    [InlineData("ttete  stest")]
    [InlineData("ttete\tstest")]
    [InlineData("ttete\nstest")]
    [InlineData("ttete\u200bstest")]
    public async Task Lookup_不合并空格或放宽其他字符(string query)
    {
        await SeedProduct("product", "ttete\u2006stest");
        Assert.Empty(await Lookup(query));
    }

    [Theory]
    [InlineData("A%_[\\'", "Aother")]
    [InlineData("ABC", "ABCD")]
    public async Task Lookup_货号特殊字符按字面值匹配(string prefix, string otherPrefix)
    {
        await SeedProduct("expected", prefix + "\u2006B");
        await SeedProduct("other", otherPrefix + " B");
        Assert.Equal("expected", Assert.Single(await Lookup(prefix + " B")).ProductCode);
    }

    [Fact]
    public async Task Lookup_条码保持精确匹配并保留清货门店范围()
    {
        await SeedProduct("product", "item", "bar\u2006code");
        await _db.Insertable(new ProductSetCode { ProductCode = "product", SetBarcode = "set\u2006code" }).ExecuteCommandAsync();
        await _db.Insertable(new StoreClearancePrice { ProductCode = "product", StoreCode = "allowed", ClearanceBarcode = "clear\u2006code" }).ExecuteCommandAsync();
        Assert.Equal("ProductBarcode", Assert.Single(await Lookup("bar\u2006code")).MatchSource);
        Assert.Equal("SetBarcode", Assert.Single(await Lookup("set\u2006code")).MatchSource);
        Assert.Equal("ClearanceBarcode", Assert.Single(await Lookup("clear\u2006code")).MatchSource);
        Assert.Empty(await Lookup("bar code"));
        Assert.Empty(await Lookup("set code"));
        Assert.Empty(await Lookup("clear code"));
        Assert.Empty(await Lookup("clear\u2006code", new List<string> { "other" }));
        Assert.Empty(await Lookup("item", new List<string>()));
    }

    [Fact]
    public async Task Lookup_普通无空格货号和条码保持可查询()
    {
        await SeedProduct("product", "ITEM123", "9529260910023");
        Assert.Equal("ItemNumber", Assert.Single(await Lookup("  ITEM123  ")).MatchSource);
        Assert.Equal("ProductBarcode", Assert.Single(await Lookup("9529260910023")).MatchSource);
    }

    private Task<int> SeedProduct(string code, string itemNumber, string? barcode = null, bool deleted = false) =>
        _db.Insertable(new Product { ProductCode = code, ProductName = code, ItemNumber = itemNumber, Barcode = barcode, IsDeleted = deleted }).ExecuteCommandAsync();

    private async Task<List<StoreProductLookupItemDto>> Lookup(string query, List<string>? scope = null)
    {
        var response = await _service.LookupAsync(new StoreProductLookupRequestDto { Keyword = query }, scope ?? new List<string> { "allowed" });
        Assert.True(response.Success, response.Message);
        return Assert.IsType<List<StoreProductLookupItemDto>>(response.Data);
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
