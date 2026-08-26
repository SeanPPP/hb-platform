using System.Globalization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

/// <summary>
/// 冻结促销规则持久化聚焦测试：挂单冻结规则经仓储落库后，在“重启 + 目录变化”场景下
/// 仍能精确恢复金额与促销 id（映射只使用冻结规则，不重算当前目录规则）。
/// 同时覆盖 schema 列缺失时的 fail-closed 降级（旧记录 null 语义）。
/// </summary>
public sealed class SuspendedOrderFrozenPromotionPersistenceTests
{
    [Fact]
    public async Task Frozen_rules_survive_restart_and_map_exactly_without_current_catalog_rules()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-frozen-persist-{Guid.NewGuid():N}.db");
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            await EnsureFrozenRuleColumnsAsync(store);

            var order = PromotionOrder();
            var repository = new SuspendedOrderRepository(store);
            await repository.SaveAsync(order);

            // 模拟重启：同一数据库文件上重建 store/schema/repository。
            SqliteConnection.ClearAllPools();
            var restartedStore = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(restartedStore).InitializeAsync();
            var restartedRepository = new SuspendedOrderRepository(restartedStore);
            var saved = await restartedRepository.GetAsync(order.SuspendedOrderGuid);

            Assert.NotNull(saved);
            var frozen = Assert.Single(saved!.FrozenPromotionRules ?? []);
            Assert.Equal("PROMO-X", frozen.PromotionId);
            Assert.Equal(20m, frozen.FixedPrice);
            Assert.Equal("P-1", Assert.Single(frozen.Products).ProductCode);
            Assert.Equal(1, Assert.Single(frozen.Products).UnitWeight);

            // 目录规则已经变化也不影响：mapper 只接收挂单冻结规则，绝不回退当前目录规则重算。
            var mapper = new SharedHeldOrderMapper();
            var result = mapper.Map(saved, saved.FrozenPromotionRules, revision: 9);

            Assert.False(result.IsBlocked);
            var payload = result.Payload!;
            Assert.Equal(9, payload.PricingState.Revision);
            var promotion = Assert.Single(payload.PricingState.Promotions);
            Assert.Equal("PROMO-X", promotion.Id);
            Assert.Equal(2000L, promotion.FixedPriceCents);
            foreach (var line in payload.PricingState.Lines)
            {
                Assert.Equal("promotion", line.DiscountState.Mode);
                Assert.Equal(100L, line.DiscountState.Cents);
                Assert.Equal(["PROMO-X"], line.DiscountState.PromotionIds);
            }
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task GetAsync_returns_null_frozen_rules_when_schema_column_missing_and_promotion_hold_blocks()
    {
        // 旧库（无 FrozenPromotionRulesJson/IsManualPrice 列）：Save 不写缺失列，
        // Get 按 null/0 恢复——等价于旧挂单语义；带自动促销折扣的挂单 fail-closed Blocked。
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-frozen-legacy-{Guid.NewGuid():N}.db");
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            await using (var connection = await store.OpenConnectionAsync())
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    ALTER TABLE SuspendedOrders DROP COLUMN FrozenPromotionRulesJson;
                    ALTER TABLE SuspendedOrderLines DROP COLUMN IsManualPrice;
                    ALTER TABLE SuspendedOrderLines DROP COLUMN CatalogDiscountBasisPoints;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SuspendedOrderRepository(store);
            var order = PromotionOrder();

            await repository.SaveAsync(order);

            var saved = await repository.GetAsync(order.SuspendedOrderGuid);
            Assert.NotNull(saved);
            Assert.Null(saved!.FrozenPromotionRules);
            Assert.Equal(2, saved.Lines.Count);
            Assert.All(saved.Lines, line => Assert.False(line.IsManualPrice));

            var result = new SharedHeldOrderMapper().Map(saved, saved.FrozenPromotionRules, revision: 1);
            Assert.True(result.IsBlocked);
            Assert.Equal(SharedHeldOrderMappingReasons.PromotionRulesMissing, result.Block!.Reason);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static SuspendedOrder PromotionOrder()
    {
        var orderGuid = Guid.NewGuid();
        return new SuspendedOrder(
            orderGuid,
            "S001",
            "POS-01",
            "cashier-1",
            "Cashier One",
            new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
            22m,
            2m,
            20m,
            SuspendedOrderStatus.Pending,
            [
                new SuspendedOrderLine(
                    Guid.NewGuid(),
                    orderGuid,
                    "S001",
                    "P-1",
                    "REF-1",
                    "Product 1",
                    "CODE-1",
                    "ITEM-1",
                    null,
                    1m,
                    11m,
                    1m,
                    null,
                    10m,
                    PriceSourceKind.ProductBase,
                    "Product Base",
                    PosCartLineDiscountSource.Promotion),
                new SuspendedOrderLine(
                    Guid.NewGuid(),
                    orderGuid,
                    "S001",
                    "P-1",
                    "REF-1",
                    "Product 1",
                    "CODE-1",
                    "ITEM-1",
                    null,
                    1m,
                    11m,
                    1m,
                    null,
                    10m,
                    PriceSourceKind.ProductBase,
                    "Product Base",
                    PosCartLineDiscountSource.Promotion)
            ])
        {
            FrozenPromotionRules =
            [
                new CatalogPromotionRuleDto(
                    "PROMO-X",
                    "Buy 2 save 10",
                    IsExclusive: true,
                    Priority: 100,
                    ApplyQuantity: 2,
                    FixedPrice: 20m,
                    MaxApplicationsPerOrder: null,
                    EffectiveStart: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                    EffectiveEnd: new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero),
                    UpdatedAt: null,
                    Products: [new CatalogPromotionProductDto("P-1", 1)])
            ]
        };
    }

    private static async Task EnsureFrozenRuleColumnsAsync(LocalSqliteStore store)
    {
        await using var connection = await store.OpenConnectionAsync();
        if (!await HasColumnAsync(connection, "SuspendedOrders", "FrozenPromotionRulesJson"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE SuspendedOrders ADD COLUMN FrozenPromotionRulesJson TEXT NULL;";
            await command.ExecuteNonQueryAsync();
        }

        if (!await HasColumnAsync(connection, "SuspendedOrderLines", "IsManualPrice"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE SuspendedOrderLines ADD COLUMN IsManualPrice INTEGER NOT NULL DEFAULT 0;";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info($TableName) WHERE name = $ColumnName;";
        command.Parameters.AddWithValue("$TableName", tableName);
        command.Parameters.AddWithValue("$ColumnName", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    private static void DeleteTempDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
