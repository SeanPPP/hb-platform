using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Pricing;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 自动定价匹配规则测试：同一策略的不同 target 类型必须跨类型 AND、同类 target 在类型内 OR。
/// </summary>
public sealed class AutoPricingServiceTests : IDisposable
{
    private const string Supplier = "SP2502260001";
    private const string Supplier2 = "SP2502260002";
    private const string Bankstown = "1024";
    private const string Bankstown2 = "1025";
    private const string Campbelltown = "1004";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly AutoPricingService _service;

    public AutoPricingServiceTests()
    {
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
            typeof(PricingStrategy),
            typeof(PricingStrategyDetail),
            typeof(PricingStrategyTarget)
        );

        SeedStrategy(
            "mixed-high",
            "W&S HOUSEWARES CO 0-2.5",
            "Store",
            20,
            new[] { ("Store", Bankstown), ("Store", Bankstown2), ("Supplier", Supplier), ("Supplier", Supplier2) },
            3.5m,
            0m,
            2.5m
        );
        SeedStrategy(
            "mixed-low",
            "W&S HOUSEWARES CO 0-2.5 (low)",
            "Store",
            10,
            new[] { ("Store", Bankstown), ("Supplier", Supplier) },
            3.25m,
            0m,
            2.5m
        );
        SeedStrategy(
            "supplier-only",
            "W&S supplier only",
            "Supplier",
            100,
            new[] { ("Supplier", Supplier) },
            3m,
            0m,
            2.5m
        );
        SeedStrategy(
            "store-only",
            "Bankstown store only",
            "Store",
            100,
            new[] { ("Store", Bankstown) },
            4m,
            0m,
            2.5m
        );
        SeedStrategy(
            "global-with-target",
            "Global with an unrelated target",
            "Global",
            200,
            new[] { ("Store", "9999") },
            9m,
            0m,
            2.5m
        );
        SeedStrategy(
            "global-default",
            "Default global",
            "Global",
            0,
            Array.Empty<(string TargetType, string TargetCode)>(),
            2.5m,
            0m,
            5m
        );

        // 保留旧数据模型中使用 Level + TargetCode、没有 target 行的兼容形式。
        SeedStrategy(
            "legacy-supplier-only",
            "Legacy supplier",
            "Supplier",
            30,
            Array.Empty<(string TargetType, string TargetCode)>(),
            3.1m,
            0m,
            2.5m,
            targetCode: "SP-LEGACY"
        );
        SeedStrategy(
            "legacy-store-only",
            "Legacy store",
            "Store",
            30,
            Array.Empty<(string TargetType, string TargetCode)>(),
            4.1m,
            0m,
            2.5m,
            targetCode: "STORE-LEGACY"
        );
        SeedStrategy(
            "legacy-mixed",
            "Legacy mixed target",
            "Store",
            50,
            new[] { ("Store", Bankstown), ("Supplier", "SP-LEGACY") },
            8m,
            0m,
            2.5m
        );

        _service = new AutoPricingService(CreateSqlSugarContext(_db));
    }

    [Fact]
    public async Task FindStrategyForPriceAsync_同时命中混合策略_返回35倍率和699零售价()
    {
        // 复现用户场景时只保留混合策略和默认全局策略，避免纯供应商策略掩盖跨店泄漏。
        DeleteStrategiesExcept("mixed-high", "global-default");
        var strategy = await _service.FindStrategyForPriceAsync(2m, Supplier, Bankstown);

        Assert.Equal("mixed-high", strategy?.Id);
        Assert.Equal(3.5m, _service.CalculateRate(2m, strategy));
        Assert.Equal(6.99m, _service.CalculateRetailPrice(2m, strategy));

        var otherStoreStrategy = await _service.FindStrategyForPriceAsync(2m, Supplier, Campbelltown);
        Assert.Equal("global-default", otherStoreStrategy?.Id);
        Assert.Equal(2.5m, _service.CalculateRate(2m, otherStoreStrategy));
        Assert.Equal(4.99m, _service.CalculateRetailPrice(2m, otherStoreStrategy));
    }

    [Theory]
    [InlineData(Campbelltown, Supplier, "supplier-only")]
    [InlineData(Bankstown, "UNKNOWN-SUPPLIER", "store-only")]
    [InlineData(null, Supplier, "supplier-only")]
    [InlineData(Bankstown, null, "store-only")]
    [InlineData(Campbelltown, "UNKNOWN-SUPPLIER", "global-default")]
    public async Task FindStrategyForPriceAsync_混合策略必须同时满足供应商和分店(
        string? storeCode,
        string? supplierCode,
        string expectedStrategyId
    )
    {
        var strategy = await _service.FindStrategyForPriceAsync(2m, supplierCode, storeCode);

        Assert.Equal(expectedStrategyId, strategy?.Id);
    }

    [Theory]
    [InlineData(Supplier2, Bankstown2, "mixed-high")]
    [InlineData(Supplier2, Campbelltown, "global-default")]
    [InlineData("UNKNOWN-SUPPLIER", Bankstown2, "global-default")]
    public async Task FindStrategyForPriceAsync_同类targets为OR跨类型仍为AND(
        string? supplierCode,
        string? storeCode,
        string expectedStrategyId
    )
    {
        var strategy = await _service.FindStrategyForPriceAsync(2m, supplierCode, storeCode);

        Assert.Equal(expectedStrategyId, strategy?.Id);
    }

    [Fact]
    public async Task FindStrategyForPriceAsync_不匹配的Global级别带target不能绕过target限制且区间未命中继续默认规则()
    {
        var targetRestricted = await _service.FindStrategyForPriceAsync(2m, "UNKNOWN-SUPPLIER", Campbelltown);

        Assert.Equal("global-default", targetRestricted?.Id);
        Assert.Equal(2.5m, _service.CalculateRate(2m, targetRestricted));

        var strategy = await _service.FindStrategyForPriceAsync(3m, "UNKNOWN-SUPPLIER", Campbelltown);

        Assert.Equal("global-default", strategy?.Id);
        Assert.Equal(2.5m, _service.CalculateRate(3m, strategy));
    }

    [Fact]
    public async Task FindStrategyForPriceAsync_混合策略优先于纯策略且同层按Priority排序()
    {
        var strategy = await _service.FindStrategyForPriceAsync(2m, Supplier, Bankstown);

        Assert.Equal("mixed-high", strategy?.Id);
        Assert.NotEqual("supplier-only", strategy?.Id);
        Assert.NotEqual("store-only", strategy?.Id);
    }

    [Theory]
    [InlineData(Campbelltown, Supplier, "supplier-only")]
    [InlineData(Bankstown, "UNKNOWN-SUPPLIER", "store-only")]
    [InlineData(Bankstown, Supplier, "mixed-high")]
    [InlineData(Bankstown2, Supplier2, "mixed-high")]
    [InlineData(Campbelltown, Supplier2, "global-default")]
    [InlineData(Bankstown2, "UNKNOWN-SUPPLIER", "global-default")]
    [InlineData(null, Supplier, "supplier-only")]
    [InlineData(Bankstown, null, "store-only")]
    [InlineData(null, "UNKNOWN-SUPPLIER", "global-default")]
    public void FindBestStrategyForPrice_使用调用方按targets分组的数据时也必须跨类型AND(
        string? storeCode,
        string? supplierCode,
        string expectedStrategyId
    )
    {
        var strategies = LoadStrategies();
        var strategy = _service.FindBestStrategyForPrice(
            2m,
            strategies
                .Where(s => supplierCode != null && s.Targets.Any(t => t.TargetType == "Supplier" && t.TargetCode == supplierCode))
                .ToList(),
            strategies
                .Where(s => storeCode != null && s.Targets.Any(t => t.TargetType == "Store" && t.TargetCode == storeCode))
                .ToList(),
            strategies.Where(s => s.Level == "Global" || s.Targets.Count == 0).ToList()
        );

        Assert.Equal(expectedStrategyId, strategy?.Id);
    }

    [Fact]
    public void FindBestStrategyForPrice_Global带target不能绕过限制()
    {
        var strategies = LoadStrategies();
        var strategy = _service.FindBestStrategyForPrice(
            2m,
            strategies.Where(s => s.Targets.Any(t => t.TargetType == "Supplier" && t.TargetCode == "UNKNOWN-SUPPLIER")).ToList(),
            strategies.Where(s => s.Targets.Any(t => t.TargetType == "Store" && t.TargetCode == Campbelltown)).ToList(),
            strategies.Where(s => s.Level == "Global" || s.Targets.Count == 0).ToList()
        );

        Assert.Equal("global-default", strategy?.Id);
    }

    [Fact]
    public void FindBestStrategyForPrice_价格区间未命中时继续全局fallback()
    {
        var strategies = LoadStrategies();
        var strategy = _service.FindBestStrategyForPrice(
            3m,
            strategies.Where(s => s.Targets.Any(t => t.TargetType == "Supplier" && t.TargetCode == Supplier)).ToList(),
            strategies.Where(s => s.Targets.Any(t => t.TargetType == "Store" && t.TargetCode == Bankstown)).ToList(),
            strategies.Where(s => s.Level == "Global" || s.Targets.Count == 0).ToList()
        );

        Assert.Equal("global-default", strategy?.Id);
    }

    [Fact]
    public async Task FindStrategyAsync_混合target跨店隔离并保留LegacyLevelTargetCode兼容()
    {
        var mismatched = await _service.FindStrategyAsync("SP-LEGACY", Campbelltown);
        var legacySupplier = await _service.FindStrategyAsync("SP-LEGACY", null);
        var legacyStore = await _service.FindStrategyAsync(null, "STORE-LEGACY");

        Assert.NotEqual("legacy-mixed", mismatched?.Id);
        Assert.Equal("legacy-supplier-only", legacySupplier?.Id);
        Assert.Equal("legacy-store-only", legacyStore?.Id);
    }

    [Theory]
    [InlineData("SP-LEGACY", Campbelltown, "legacy-supplier-only")]
    [InlineData("UNKNOWN-SUPPLIER", "STORE-LEGACY", "legacy-store-only")]
    [InlineData("SP-LEGACY", null, "legacy-supplier-only")]
    [InlineData(null, "STORE-LEGACY", "legacy-store-only")]
    [InlineData("UNKNOWN-SUPPLIER", Campbelltown, "global-default")]
    [InlineData(null, null, "global-default")]
    public async Task 旧策略预加载分组后与直接计算保持一致(
        string? supplierCode,
        string? storeCode,
        string expectedStrategyId
    )
    {
        var direct = await _service.FindStrategyForPriceAsync(2m, supplierCode, storeCode);
        var strategies = await _service.GetAllActiveStrategiesAsync();

        // 手机详情和进货单检查都按预加载的 Targets 分组，旧数据也必须遵守相同范围。
        var preloaded = _service.FindBestStrategyForPrice(
            2m,
            strategies.Where(s => s.Targets.Any(t => t.TargetType == "Supplier" && t.TargetCode == supplierCode)).ToList(),
            strategies.Where(s => s.Targets.Any(t => t.TargetType == "Store" && t.TargetCode == storeCode)).ToList(),
            strategies.Where(s => s.Level == "Global" || s.Targets.Count == 0).ToList()
        );

        Assert.Equal(expectedStrategyId, direct?.Id);
        Assert.Equal(expectedStrategyId, preloaded?.Id);
        Assert.Equal(_service.CalculateRate(2m, direct), _service.CalculateRate(2m, preloaded));
        // 兼容处理只影响内存中的查询结果，不迁移或写回策略目标表。
        Assert.Equal(0, _db.Queryable<PricingStrategyTarget>().Count(t => t.StrategyId == "legacy-supplier-only" || t.StrategyId == "legacy-store-only"));
    }

    [Fact]
    public async Task 新Targets优先于陈旧的TargetCode且不扩大分店范围()
    {
        DeleteStrategiesExcept("global-default");
        SeedStrategy(
            "new-targets",
            "已改为Campbelltown的混合策略",
            "Store",
            20,
            new[] { ("Store", Campbelltown), ("Supplier", Supplier) },
            3.5m,
            0m,
            2.5m,
            targetCode: Bankstown
        );

        var strategies = await _service.GetAllActiveStrategiesAsync();
        Assert.DoesNotContain(strategies.Single(s => s.Id == "new-targets").Targets,
            t => t.TargetType == "Store" && t.TargetCode == Bankstown);

        foreach (var storeCode in new[] { Campbelltown, Bankstown })
        {
            var expectedId = storeCode == Campbelltown ? "new-targets" : "global-default";
            var direct = await _service.FindStrategyForPriceAsync(2m, Supplier, storeCode);
            var preloaded = _service.FindBestStrategyForPrice(
                2m,
                strategies.Where(s => s.Targets.Any(t => t.TargetType == "Supplier" && t.TargetCode == Supplier)).ToList(),
                strategies.Where(s => s.Targets.Any(t => t.TargetType == "Store" && t.TargetCode == storeCode)).ToList(),
                strategies.Where(s => s.Level == "Global" || s.Targets.Count == 0).ToList()
            );
            Assert.Equal(expectedId, direct?.Id);
            Assert.Equal(expectedId, preloaded?.Id);
            Assert.Equal(expectedId, (await _service.FindStrategyAsync(Supplier, storeCode))?.Id);
        }
    }

    [Fact]
    public async Task 曲线保存回读且非法更新不改变数据库()
    {
        var service = new BlazorApp.Api.Services.React.PricingStrategyReactService(CreateSqlSugarContext(_db));
        var dto = new BlazorApp.Shared.DTOs.CreatePricingStrategyDto
        {
            Name = "曲线测试", Details = new()
            {
                new() { MinPrice = 10, MaxPrice = 20, StartRate = 4, EndRate = 2.5m,
                    StartRetailPrice = 39.99m, EndRetailPrice = 49.99m, Algorithm = "ArcUp", CurveBend = .1m }
            }
        };
        var saved = await service.CreateAsync(dto);
        Assert.True(saved.Success, saved.Message);
        var id = saved.Data!.Id;
        Assert.Equal(39.99m, saved.Data.Details.Single().StartRetailPrice);
        Assert.Equal(.1m, saved.Data.Details.Single().CurveBend);
        var loaded = (await _service.GetAllActiveStrategiesAsync()).Single(s => s.Id == id);
        Assert.Equal(49.99m, loaded.Details.Single().EndRetailPrice);
        dto.Details[0].EndRetailPrice = 9.99m;
        var invalid = await service.UpdateAsync(id, new() { Name = "不得保存", Details = dto.Details });
        Assert.False(invalid.Success);
        Assert.Equal("曲线测试", (await service.GetByIdAsync(id)).Data!.Name);
        Assert.Equal(49.99m, (await service.GetByIdAsync(id)).Data!.Details.Single().EndRetailPrice);
    }

    [Fact]
    public async Task 弧度先向零截断再校验确保保存回读安全()
    {
        var service = new BlazorApp.Api.Services.React.PricingStrategyReactService(CreateSqlSugarContext(_db));
        var result = await service.CreateAsync(new()
        {
            Name = "弧度精度", Details = new()
            { new() { MinPrice = 10, MaxPrice = 20, StartRate = 3.999m, EndRate = 3.4495m,
                StartRetailPrice = 39.99m, EndRetailPrice = 68.99m, Algorithm = "ArcUp", CurveBend = .37896551m } }
        });
        Assert.True(result.Success, result.Message);
        Assert.Equal(.378965m, result.Data!.Details.Single().CurveBend);
        var loaded = (await _service.GetAllActiveStrategiesAsync()).Single(s => s.Id == result.Data.Id);
        Assert.Equal(39.99m, _service.CalculateRetailPrice(10, loaded));
    }

    [Fact]
    public async Task 保存明细失败时策略主表也回滚()
    {
        var service = new BlazorApp.Api.Services.React.PricingStrategyReactService(CreateSqlSugarContext(_db));
        var count = _db.Queryable<PricingStrategy>().Count();
        _db.Ado.ExecuteCommand("CREATE TRIGGER reject_pricing_detail BEFORE INSERT ON PricingStrategyDetail BEGIN SELECT RAISE(ABORT, 'test rejection'); END;");
        var result = await service.CreateAsync(new()
        {
            Name = "必须回滚", Details = new()
            { new() { MinPrice = 10, MaxPrice = 20, StartRate = 4, EndRate = 2.5m, Algorithm = "Linear" } }
        });
        Assert.False(result.Success);
        Assert.Equal(count, _db.Queryable<PricingStrategy>().Count());
    }

    private List<PricingStrategy> LoadStrategies()
    {
        var strategies = _db.Queryable<PricingStrategy>().ToList();
        var details = _db.Queryable<PricingStrategyDetail>().ToList();
        var targets = _db.Queryable<PricingStrategyTarget>().ToList();
        foreach (var strategy in strategies)
        {
            strategy.Details = details.Where(d => d.StrategyId == strategy.Id).ToList();
            strategy.Targets = targets.Where(t => t.StrategyId == strategy.Id).ToList();
        }
        return strategies;
    }

    private void DeleteStrategiesExcept(params string[] retainedIds)
    {
        var retained = retainedIds.ToHashSet(StringComparer.Ordinal);
        var ids = _db.Queryable<PricingStrategy>()
            .Where(s => !retained.Contains(s.Id))
            .Select(s => s.Id)
            .ToList();
        if (ids.Count == 0)
            return;

        _db.Deleteable<PricingStrategyTarget>().Where(t => ids.Contains(t.StrategyId)).ExecuteCommand();
        _db.Deleteable<PricingStrategyDetail>().Where(d => ids.Contains(d.StrategyId)).ExecuteCommand();
        _db.Deleteable<PricingStrategy>().Where(s => ids.Contains(s.Id)).ExecuteCommand();
    }

    private void SeedStrategy(
        string id,
        string name,
        string level,
        int priority,
        IReadOnlyCollection<(string TargetType, string TargetCode)> targets,
        decimal rate,
        decimal minPrice,
        decimal maxPrice,
        string? targetCode = null
    )
    {
        _db.Insertable(new PricingStrategy
        {
            Id = id,
            Name = name,
            Level = level,
            TargetCode = targetCode,
            Priority = priority,
            IsEnabled = true,
        }).ExecuteCommand();
        _db.Insertable(new PricingStrategyDetail
        {
            Id = $"{id}-detail",
            StrategyId = id,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            StartRate = rate,
            EndRate = rate,
            Algorithm = "Step",
        }).ExecuteCommand();
        foreach (var target in targets)
        {
            _db.Insertable(new PricingStrategyTarget
            {
                Id = $"{id}-{target.TargetType}-{target.TargetCode}",
                StrategyId = id,
                TargetType = target.TargetType,
                TargetCode = target.TargetCode,
            }).ExecuteCommand();
        }
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
