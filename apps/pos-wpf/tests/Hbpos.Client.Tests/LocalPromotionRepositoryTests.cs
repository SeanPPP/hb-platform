using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Promotions;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class LocalPromotionRepositoryTests
{
    [Fact]
    public async Task Local_schema_service_creates_local_promotion_tables_and_indexes()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);

            await schema.InitializeAsync();

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LocalPromotions';"));
            Assert.Equal(1, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LocalPromotionProducts';"));
            Assert.Equal(1, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_LocalPromotions_Store_EffectiveRange';"));
            Assert.Equal(1, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_LocalPromotionProducts_Store_ProductCode';"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ReplaceStoreRulesAsync_replaces_only_selected_store_rules()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var current = DateTimeOffset.Parse("2026-06-13T12:00:00Z");

            await repository.ReplaceStoreRulesAsync("S01", CreateRulesResponse("S01", current, CreateRule("PROMO-OLD-S01", current, ["SKU-001"])));
            await repository.ReplaceStoreRulesAsync("S02", CreateRulesResponse("S02", current, CreateRule("PROMO-KEEP-S02", current, ["SKU-002"])));

            await repository.ReplaceStoreRulesAsync("S01", CreateRulesResponse("S01", current, CreateRule("PROMO-NEW-S01", current, ["SKU-003", "SKU-004"])));

            var s01Rules = await repository.GetActiveRulesAsync("S01", current);
            var s02Rules = await repository.GetActiveRulesAsync("S02", current);

            var s01Rule = Assert.Single(s01Rules);
            Assert.Equal("PROMO-NEW-S01", s01Rule.Id);
            Assert.Equal(["SKU-003", "SKU-004"], s01Rule.Products.Select(item => item.ProductCode).ToArray());

            var s02Rule = Assert.Single(s02Rules);
            Assert.Equal("PROMO-KEEP-S02", s02Rule.Id);
            Assert.Equal(["SKU-002"], s02Rule.Products.Select(item => item.ProductCode).ToArray());
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task GetActiveRulesAsync_returns_only_rules_effective_at_requested_time()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var asOf = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
            var response = new PromotionRulesResponse(
                "S01",
                asOf,
                [
                    CreateRule(
                        "PROMO-ACTIVE-HIGH",
                        asOf,
                        ["SKU-001"],
                        priority: 20,
                        effectiveStart: asOf.AddDays(-1).UtcDateTime,
                        effectiveEnd: asOf.AddDays(1).UtcDateTime),
                    CreateRule(
                        "PROMO-EXPIRED",
                        asOf,
                        ["SKU-002"],
                        priority: 10,
                        effectiveStart: asOf.AddDays(-5).UtcDateTime,
                        effectiveEnd: asOf.AddSeconds(-1).UtcDateTime),
                    CreateRule(
                        "PROMO-FUTURE",
                        asOf,
                        ["SKU-003"],
                        priority: 30,
                        effectiveStart: asOf.AddSeconds(1).UtcDateTime,
                        effectiveEnd: asOf.AddDays(2).UtcDateTime)
                ]);

            await repository.ReplaceStoreRulesAsync("S01", response);

            var activeRules = await repository.GetActiveRulesAsync("S01", asOf);

            var activeRule = Assert.Single(activeRules);
            Assert.Equal("PROMO-ACTIVE-HIGH", activeRule.Id);
            Assert.Equal(["SKU-001"], activeRule.Products.Select(item => item.ProductCode).ToArray());
            Assert.Equal(20, activeRule.Priority);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ReplaceStoreRulesAsync_when_product_insert_fails_rolls_back_and_keeps_previous_rules()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var current = DateTimeOffset.Parse("2026-06-13T12:00:00Z");
            await repository.ReplaceStoreRulesAsync(
                "S01",
                CreateRulesResponse("S01", current, CreateRule("PROMO-OLD", current, ["SKU-OLD-1", "SKU-OLD-2"])));

            var duplicateProductRule = CreateRule("PROMO-NEW", current, ["SKU-DUP", "SKU-DUP"]);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                repository.ReplaceStoreRulesAsync(
                    "S01",
                    CreateRulesResponse("S01", current, duplicateProductRule)));

            var activeRules = await repository.GetActiveRulesAsync("S01", current);

            var activeRule = Assert.Single(activeRules);
            Assert.Equal("PROMO-OLD", activeRule.Id);
            Assert.Equal(["SKU-OLD-1", "SKU-OLD-2"], activeRule.Products.Select(item => item.ProductCode).ToArray());
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static async Task<LocalPromotionRepository> CreateRepositoryAsync(string databasePath)
    {
        var store = new LocalSqliteStore(databasePath);
        var schema = new LocalSchemaService(store);
        await schema.InitializeAsync();
        return new LocalPromotionRepository(store);
    }

    private static PromotionRulesResponse CreateRulesResponse(
        string storeCode,
        DateTimeOffset generatedAt,
        params PromotionRuleDto[] rules)
    {
        return new PromotionRulesResponse(storeCode, generatedAt, rules);
    }

    private static PromotionRuleDto CreateRule(
        string id,
        DateTimeOffset generatedAt,
        IEnumerable<string> productCodes,
        int priority = 10,
        bool isExclusive = false,
        int applyQuantity = 2,
        decimal fixedPrice = 9.99m,
        int? maxApplicationsPerOrder = 1,
        DateTime? effectiveStart = null,
        DateTime? effectiveEnd = null)
    {
        return new PromotionRuleDto(
            id,
            $"Rule {id}",
            effectiveStart ?? generatedAt.AddDays(-1).UtcDateTime,
            effectiveEnd ?? generatedAt.AddDays(1).UtcDateTime,
            isExclusive,
            priority,
            applyQuantity,
            fixedPrice,
            maxApplicationsPerOrder,
            productCodes.Select((productCode, index) => new PromotionRuleProductDto(productCode, index + 1)).ToArray());
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"hbpos-promotions-{Guid.NewGuid():N}.db");
    }

    private static void DeleteTempDatabase(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task<int> ReadScalarIntAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
