using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
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
    public async Task GetPagedListAsync_StoreRecordCountMin为1时仅返回有未删除分店记录的商品()
    {
        await SeedProductAsync("P001", "A001");
        await SeedProductAsync("P002", "A002");
        await SeedProductAsync("P003", "A003");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false);
        await SeedStoreRetailPriceAsync("price-2", "P003", "S03", true);

        var result = await CreateService().GetPagedListAsync(new ProductReactFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StoreRecordCountMin = 1,
        });

        Assert.Equal(
            new[] { "P001" },
            result.Items.Select(item => item.ProductCode).OrderBy(code => code).ToArray()
        );
        Assert.Equal(1, result.Items.Single().StoreRecordCount);
    }

    [Fact]
    public async Task GetPagedListAsync_StoreRecordCountMinMax为0时仅返回无未删除分店记录的商品()
    {
        await SeedProductAsync("P001", "A001");
        await SeedProductAsync("P002", "A002");
        await SeedProductAsync("P003", "A003");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false);
        await SeedStoreRetailPriceAsync("price-2", "P003", "S03", true);

        var result = await CreateService().GetPagedListAsync(new ProductReactFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StoreRecordCountMin = 0,
            StoreRecordCountMax = 0,
        });

        Assert.Equal(
            new[] { "P002", "P003" },
            result.Items.Select(item => item.ProductCode).OrderBy(code => code).ToArray()
        );
        Assert.All(result.Items, item => Assert.Equal(0, item.StoreRecordCount));
    }

    [Fact]
    public async Task GetPagedListAsync_按StoreRecordCount范围筛选时只返回命中区间的商品()
    {
        await SeedProductAsync("P001", "A001");
        await SeedProductAsync("P002", "A002");
        await SeedProductAsync("P003", "A003");
        await SeedProductAsync("P004", "A004");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false);
        await SeedStoreRetailPriceAsync("price-2", "P002", "S01", false);
        await SeedStoreRetailPriceAsync("price-3", "P002", "S02", false);
        await SeedStoreRetailPriceAsync("price-4", "P003", "S01", false);
        await SeedStoreRetailPriceAsync("price-5", "P003", "S02", false);
        await SeedStoreRetailPriceAsync("price-6", "P003", "S03", false);
        await SeedStoreRetailPriceAsync("price-7", "P004", "S04", true);

        var result = await CreateService().GetPagedListAsync(new ProductReactFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StoreRecordCountMin = 2,
            StoreRecordCountMax = 3,
        });

        Assert.Equal(
            new[] { "P002", "P003" },
            result.Items.Select(item => item.ProductCode).OrderBy(code => code).ToArray()
        );
        Assert.Equal(
            new[] { 2, 3 },
            result.Items.OrderBy(item => item.ProductCode).Select(item => item.StoreRecordCount).ToArray()
        );
    }

    [Fact]
    public async Task GetPagedListAsync_按StoreRecordCount升降序排序时在分页前生效()
    {
        await SeedProductAsync("P001", "A001");
        await SeedProductAsync("P002", "A002");
        await SeedProductAsync("P003", "A003");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false);
        await SeedStoreRetailPriceAsync("price-2", "P001", "S02", false);
        await SeedStoreRetailPriceAsync("price-3", "P002", "S01", false);
        await SeedStoreRetailPriceAsync("price-4", "P003", "S03", true);

        var ascResult = await CreateService().GetPagedListAsync(new ProductReactFilterDto
        {
            PageNumber = 1,
            PageSize = 1,
            SortBy = "storerecordcount",
            SortOrder = "asc",
        });

        var descResult = await CreateService().GetPagedListAsync(new ProductReactFilterDto
        {
            PageNumber = 1,
            PageSize = 1,
            SortBy = "storerecordcount",
            SortOrder = "desc",
        });

        Assert.Equal("P003", ascResult.Items.Single().ProductCode);
        Assert.Equal(0, ascResult.Items.Single().StoreRecordCount);
        Assert.Equal("P001", descResult.Items.Single().ProductCode);
        Assert.Equal(2, descResult.Items.Single().StoreRecordCount);
    }

    [Fact]
    public async Task GetPagedListAsync_分店记录数量使用预聚合查询避免逐行相关计数()
    {
        await SeedProductAsync("P001", "A001");
        await SeedProductAsync("P002", "A002");
        await SeedStoreRetailPriceAsync("price-active-1", "P001", "S01", false);
        await SeedStoreRetailPriceAsync("price-active-2", "P001", "S02", false);
        await SeedStoreRetailPriceAsync("price-deleted-1", "P002", "S03", true);
        await SeedStoreRetailPriceAsync("price-deleted-2", "P002", "S04", true);

        var executedSql = new List<string>();
        _localDb.Aop.OnLogExecuting = (sql, _) => executedSql.Add(sql);

        try
        {
            var result = await CreateService().GetPagedListAsync(new ProductReactFilterDto
            {
                PageNumber = 1,
                PageSize = 20,
                StoreRecordCountMin = 0,
                StoreRecordCountMax = 0,
            });

            Assert.Equal(new[] { "P002" }, result.Items.Select(item => item.ProductCode).ToArray());
            Assert.Equal(0, result.Items.Single().StoreRecordCount);
        }
        finally
        {
            _localDb.Aop.OnLogExecuting = null;
        }

        var storeRecordSql = string.Join(
            "\n",
            executedSql.Where(sql => sql.Contains("StoreRetailPrice", StringComparison.OrdinalIgnoreCase))
        );
        Assert.Contains("JOIN", storeRecordSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", storeRecordSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "`record`.`ProductCode` = `p`.`ProductCode`",
            storeRecordSql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            "[record].[ProductCode] = [p].[ProductCode]",
            storeRecordSql,
            StringComparison.OrdinalIgnoreCase
        );
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

    [Fact]
    public async Task BatchUpdateStoreRecordsAsync_只更新勾选分店()
    {
        await SeedProductAsync("P001", "A001");
        await SeedStoreAsync("S01", "分店一");
        await SeedStoreAsync("S02", "分店二");
        await SeedStoreAsync("S03", "分店三");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false, 1.1m, 2.1m, discountRate: 0.91m);
        await SeedStoreRetailPriceAsync("price-2", "P001", "S02", false, 1.2m, 2.2m, discountRate: 0.92m);
        await SeedStoreRetailPriceAsync("price-3", "P001", "S03", false, 1.3m, 2.3m, discountRate: 0.93m);
        var beforeS01 = await GetStoreRetailPriceAsync("P001", "S01");
        var beforeS02 = await GetStoreRetailPriceAsync("P001", "S02");
        var beforeS03 = await GetStoreRetailPriceAsync("P001", "S03");

        var response = await CreateService("batch-editor").BatchUpdateStoreRecordsAsync(
            "P001",
            new BatchUpdateProductStoreRecordsRequest
            {
                StoreCodes = new[] { "S01", "S03" },
                Changes = new BatchUpdateProductStoreRecordChangesDto
                {
                    PurchasePrice = 5.5m,
                    StoreRetailPriceValue = 8.8m,
                    DiscountRate = 0.77m,
                    IsAutoPricing = true,
                    IsSpecialProduct = true,
                    IsActive = false,
                },
            },
            null
        );

        var afterS01 = await GetStoreRetailPriceAsync("P001", "S01");
        var afterS02 = await GetStoreRetailPriceAsync("P001", "S02");
        var afterS03 = await GetStoreRetailPriceAsync("P001", "S03");

        Assert.True(response.Success, response.Message);
        Assert.NotNull(response.Data);
        Assert.Equal(2, response.Data!.SuccessCount);
        Assert.Equal(0, response.Data.FailedCount);
        Assert.Empty(response.Data.Errors);

        Assert.Equal(5.5m, afterS01!.PurchasePrice);
        Assert.Equal(8.8m, afterS01.StoreRetailPriceValue);
        Assert.Equal(0.77m, afterS01.DiscountRate);
        Assert.True(afterS01.IsAutoPricing);
        Assert.True(afterS01.IsSpecialProduct);
        Assert.False(afterS01.IsActive);
        Assert.Equal("batch-editor", afterS01.UpdatedBy);
        Assert.True(afterS01.UpdatedAt >= beforeS01!.UpdatedAt);

        Assert.Equal(beforeS02!.PurchasePrice, afterS02!.PurchasePrice);
        Assert.Equal(beforeS02.StoreRetailPriceValue, afterS02.StoreRetailPriceValue);
        Assert.Equal(beforeS02.DiscountRate, afterS02.DiscountRate);
        Assert.Equal(beforeS02.IsAutoPricing, afterS02.IsAutoPricing);
        Assert.Equal(beforeS02.IsSpecialProduct, afterS02.IsSpecialProduct);
        Assert.Equal(beforeS02.IsActive, afterS02.IsActive);
        Assert.Equal(beforeS02.UpdatedBy, afterS02.UpdatedBy);

        Assert.Equal(5.5m, afterS03!.PurchasePrice);
        Assert.Equal(8.8m, afterS03.StoreRetailPriceValue);
        Assert.Equal(0.77m, afterS03.DiscountRate);
        Assert.True(afterS03.IsAutoPricing);
        Assert.True(afterS03.IsSpecialProduct);
        Assert.False(afterS03.IsActive);
        Assert.Equal("batch-editor", afterS03.UpdatedBy);
        Assert.True(afterS03.UpdatedAt >= beforeS03!.UpdatedAt);
    }

    [Fact]
    public async Task BatchUpdateStoreRecordsAsync_字段缺省时不修改未提供字段()
    {
        await SeedProductAsync("P001", "A001");
        await SeedStoreAsync("S01", "分店一");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false, 1.2m, 2.4m, discountRate: 0.85m, isAutoPricing: false, isSpecialProduct: true, isActive: true);
        var before = await GetStoreRetailPriceAsync("P001", "S01");

        var response = await CreateService("partial-editor").BatchUpdateStoreRecordsAsync(
            "P001",
            new BatchUpdateProductStoreRecordsRequest
            {
                StoreCodes = new[] { "S01" },
                Changes = new BatchUpdateProductStoreRecordChangesDto
                {
                    DiscountRate = 0.66m,
                },
            },
            null
        );

        var after = await GetStoreRetailPriceAsync("P001", "S01");

        Assert.True(response.Success, response.Message);
        Assert.NotNull(response.Data);
        Assert.Equal(1, response.Data!.SuccessCount);
        Assert.Equal(0, response.Data.FailedCount);
        Assert.Equal(before!.PurchasePrice, after!.PurchasePrice);
        Assert.Equal(before.StoreRetailPriceValue, after.StoreRetailPriceValue);
        Assert.Equal(0.66m, after.DiscountRate);
        Assert.Equal(before.IsAutoPricing, after.IsAutoPricing);
        Assert.Equal(before.IsSpecialProduct, after.IsSpecialProduct);
        Assert.Equal(before.IsActive, after.IsActive);
        Assert.Equal("partial-editor", after.UpdatedBy);
    }

    [Fact]
    public async Task BatchUpdateStoreRecordsAsync_不可访问分店不更新并记录失败()
    {
        await SeedProductAsync("P001", "A001");
        await SeedStoreAsync("S01", "分店一");
        await SeedStoreAsync("S02", "分店二");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false, 1.1m, 2.1m);
        await SeedStoreRetailPriceAsync("price-2", "P001", "S02", false, 1.2m, 2.2m);
        var beforeDenied = await GetStoreRetailPriceAsync("P001", "S02");

        var response = await CreateService("scope-editor").BatchUpdateStoreRecordsAsync(
            "P001",
            new BatchUpdateProductStoreRecordsRequest
            {
                StoreCodes = new[] { "S01", "S02" },
                Changes = new BatchUpdateProductStoreRecordChangesDto
                {
                    PurchasePrice = 9.9m,
                },
            },
            new[] { "S01" }
        );

        var allowed = await GetStoreRetailPriceAsync("P001", "S01");
        var denied = await GetStoreRetailPriceAsync("P001", "S02");

        Assert.True(response.Success, response.Message);
        Assert.NotNull(response.Data);
        Assert.Equal(1, response.Data!.SuccessCount);
        Assert.Equal(1, response.Data.FailedCount);
        Assert.Contains(response.Data.Errors, error => error.Contains("S02"));
        Assert.Equal(9.9m, allowed!.PurchasePrice);
        Assert.Equal(beforeDenied!.PurchasePrice, denied!.PurchasePrice);
        Assert.Equal(beforeDenied.StoreRetailPriceValue, denied.StoreRetailPriceValue);
        Assert.Equal(beforeDenied.UpdatedBy, denied.UpdatedBy);
    }

    [Fact]
    public async Task BatchUpdateStoreRecordsAsync_软删记录不更新并记录失败()
    {
        await SeedProductAsync("P001", "A001");
        await SeedStoreAsync("S01", "分店一");
        await SeedStoreAsync("S02", "分店二");
        await SeedStoreRetailPriceAsync("price-1", "P001", "S01", false, 1.1m, 2.1m);
        await SeedStoreRetailPriceAsync("price-2", "P001", "S02", true, 1.2m, 2.2m);
        var beforeDeleted = await GetStoreRetailPriceAsync("P001", "S02");

        var response = await CreateService("delete-editor").BatchUpdateStoreRecordsAsync(
            "P001",
            new BatchUpdateProductStoreRecordsRequest
            {
                StoreCodes = new[] { "S01", "S02" },
                Changes = new BatchUpdateProductStoreRecordChangesDto
                {
                    IsActive = false,
                },
            },
            null
        );

        var active = await GetStoreRetailPriceAsync("P001", "S01");
        var deleted = await GetStoreRetailPriceAsync("P001", "S02");

        Assert.True(response.Success, response.Message);
        Assert.NotNull(response.Data);
        Assert.Equal(1, response.Data!.SuccessCount);
        Assert.Equal(1, response.Data.FailedCount);
        Assert.Contains(response.Data.Errors, error => error.Contains("S02"));
        Assert.False(active!.IsActive);
        Assert.True(beforeDeleted!.IsDeleted);
        Assert.Equal(beforeDeleted.PurchasePrice, deleted!.PurchasePrice);
        Assert.Equal(beforeDeleted.UpdatedBy, deleted.UpdatedBy);
    }

    [Fact]
    public void BatchUpdateStoreRecordsAsync_应只写入显式字段避免整实体覆盖未传字段()
    {
        var source = File.ReadAllText(ResolveProductReactServicePath());

        Assert.Contains("ExecuteStoreRecordPartialUpdateAsync", source);
        Assert.Contains("只写入请求显式勾选的业务字段", source);
        Assert.DoesNotContain("_db.Updateable(record).ExecuteCommandAsync()", source);
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

    private ProductReactService CreateService(string? identityName = null)
    {
        return new ProductReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(_hqDb, CreateHqConfiguration(_hqConnection.ConnectionString)),
            _mapper,
            NullLogger<ProductReactService>.Instance,
            CreateHttpContextAccessor(identityName)
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
        decimal retailPrice = 2,
        decimal discountRate = 0.9m,
        bool isAutoPricing = false,
        bool isSpecialProduct = false,
        bool isActive = true)
    {
        await _localDb.Insertable(new StoreRetailPrice
        {
            UUID = uuid,
            StoreCode = storeCode,
            ProductCode = productCode,
            StoreProductCode = $"{storeCode}-{productCode}",
            PurchasePrice = purchasePrice,
            StoreRetailPriceValue = retailPrice,
            DiscountRate = discountRate,
            IsActive = isActive,
            IsAutoPricing = isAutoPricing,
            IsSpecialProduct = isSpecialProduct,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "tester",
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task<StoreRetailPrice?> GetStoreRetailPriceAsync(string productCode, string storeCode)
    {
        return await _localDb.Queryable<StoreRetailPrice>()
            .Where(item => item.ProductCode == productCode && item.StoreCode == storeCode)
            .FirstAsync();
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

    private static HttpContextAccessor CreateHttpContextAccessor(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
        {
            return new HttpContextAccessor();
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, identityName),
        }, "TestAuth");

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };
    }

    private static string ResolveProductReactServicePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException("无法解析测试文件目录");
        return Path.GetFullPath(
            Path.Combine(testDirectory, "..", "BlazorApp.Api", "Services", "React", "ProductReactService.cs")
        );
    }
}
