using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class SuspendedOrderServiceTests
{
    [Fact]
    public async Task SuspendCurrentOrderAsync_saves_snapshot_clears_cart_and_keeps_local_orders_and_sync_queue_empty()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();
            var line = cart.AddItem(CreateItem(
                productCode: "SKU-HOLD-01",
                lookupCode: "hold-01",
                itemNumber: "ITEM-HOLD-01",
                price: 13.5m,
                priceSource: PriceSourceKind.StoreClearancePrice,
                productImage: "https://images.example/hold-01.jpg"));
            Assert.True(cart.SetLineQuantity(line, 2m));
            Assert.True(cart.SetLineDiscountPercent(line, 10m));

            await schema.InitializeAsync();

            var suspended = await service.SuspendCurrentOrderAsync(session);
            var pending = await service.GetPendingOrdersAsync(session.StoreCode);

            Assert.True(cart.IsEmpty);
            var summary = Assert.Single(pending);
            Assert.Equal(suspended.SuspendedOrderGuid, summary.SuspendedOrderGuid);
            Assert.Equal(27m, summary.TotalAmount);
            Assert.Equal(2.70m, summary.DiscountAmount);
            Assert.Equal(24.30m, summary.ActualAmount);

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(0, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM LocalOrders;"));
            Assert.Equal(0, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM SyncQueue;"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RecallOrderAsync_rejects_non_empty_cart()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);

            await schema.InitializeAsync();
            cart.AddItem(CreateItem(productCode: "SKU-HOLD-02", lookupCode: "hold-02", price: 8m));
            var suspended = await service.SuspendCurrentOrderAsync(CreateSession());
            cart.AddItem(CreateItem(productCode: "SKU-LIVE-01", lookupCode: "live-01", price: 5m));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecallOrderAsync(suspended.SuspendedOrderGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task GetPendingOrdersAsync_filters_by_device_when_terminal_is_selected()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();

            await schema.InitializeAsync();

            cart.AddItem(CreateItem(productCode: "SKU-POS-01", lookupCode: "pos-01", price: 6m));
            var pos01Order = await service.SuspendCurrentOrderAsync(session);

            cart.AddItem(CreateItem(productCode: "SKU-POS-02", lookupCode: "pos-02", price: 9m));
            var pos02Order = await service.SuspendCurrentOrderAsync(session with { DeviceCode = "POS-02" });

            var allTerminals = await service.GetPendingOrdersAsync(session.StoreCode, deviceCode: null);
            var pos01Only = await service.GetPendingOrdersAsync(session.StoreCode, deviceCode: "POS-01");
            var pos02Only = await service.GetPendingOrdersAsync(session.StoreCode, deviceCode: "POS-02");

            Assert.Equal(2, allTerminals.Count);
            Assert.Contains(allTerminals, order => order.SuspendedOrderGuid == pos01Order.SuspendedOrderGuid);
            Assert.Contains(allTerminals, order => order.SuspendedOrderGuid == pos02Order.SuspendedOrderGuid);
            Assert.Equal(pos01Order.SuspendedOrderGuid, Assert.Single(pos01Only).SuspendedOrderGuid);
            Assert.Equal(pos02Order.SuspendedOrderGuid, Assert.Single(pos02Only).SuspendedOrderGuid);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RecallOrderAsync_restores_snapshot_marks_recalled_and_hides_order_from_pending_list()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();
            var line = cart.AddItem(CreateItem(
                productCode: "SKU-HOLD-03",
                lookupCode: " hold-03 ",
                itemNumber: "ITEM-HOLD-03",
                price: 15m,
                priceSource: PriceSourceKind.StoreMultiCodeProduct,
                productImage: "https://images.example/hold-03.jpg"));
            Assert.True(cart.SetLineQuantity(line, 3m));
            Assert.True(cart.SetLineDiscountPercent(line, 12.5m));

            await schema.InitializeAsync();

            var suspended = await service.SuspendCurrentOrderAsync(session);
            var recalled = await service.RecallOrderAsync(suspended.SuspendedOrderGuid);
            var pending = await service.GetPendingOrdersAsync(session.StoreCode);
            var saved = await repository.GetAsync(suspended.SuspendedOrderGuid);

            Assert.Equal(suspended.SuspendedOrderGuid, recalled.SuspendedOrderGuid);
            line = Assert.Single(cart.Lines);
            Assert.Equal(3m, line.Quantity);
            Assert.Equal(15m, line.UnitPrice);
            Assert.Equal(5.63m, line.DiscountAmount);
            Assert.Equal("ITEM-HOLD-03", line.ItemNumber);
            Assert.Equal(" hold-03 ", line.LookupCode);
            Assert.Equal("https://images.example/hold-03.jpg", line.ProductImage);
            Assert.Equal(PriceSourceKind.StoreMultiCodeProduct, line.PriceSource);

            Assert.True(cart.SetLineQuantity(line, 4m));
            Assert.Equal(7.50m, line.DiscountAmount);

            Assert.Empty(pending);
            Assert.NotNull(saved);
            Assert.Equal(SuspendedOrderStatus.Recalled, saved.Status);

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(0, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM LocalOrders;"));
            Assert.Equal(0, await ReadScalarIntAsync(connection, "SELECT COUNT(*) FROM SyncQueue;"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RecallOrderAsync_finishes_status_commit_when_caller_cancels_after_cart_restore()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);

            await schema.InitializeAsync();
            cart.AddItem(CreateItem(productCode: "SKU-CANCEL-RECALL", lookupCode: "cancel-recall", price: 8m));
            var suspended = await service.SuspendCurrentOrderAsync(CreateSession());
            using var callerCancellation = new CancellationTokenSource();
            var interceptingRepository = new InterceptingSuspendedOrderRepository(repository)
            {
                BeforeMarkStatus = callerCancellation.Cancel
            };

            var recalled = await new SuspendedOrderService(interceptingRepository, cart)
                .RecallOrderAsync(suspended.SuspendedOrderGuid, callerCancellation.Token);

            Assert.True(callerCancellation.IsCancellationRequested);
            Assert.False(interceptingRepository.MarkStatusTokenCanBeCanceled);
            Assert.Equal(SuspendedOrderStatus.Recalled, recalled.Status);
            Assert.Single(cart.Lines);
            Assert.Equal(
                SuspendedOrderStatus.Recalled,
                (await repository.GetAsync(suspended.SuspendedOrderGuid))!.Status);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RecallOrderAsync_clears_restored_cart_when_status_commit_fails()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);

            await schema.InitializeAsync();
            cart.AddItem(CreateItem(productCode: "SKU-FAILED-RECALL", lookupCode: "failed-recall", price: 8m));
            var suspended = await service.SuspendCurrentOrderAsync(CreateSession());
            var interceptingRepository = new InterceptingSuspendedOrderRepository(repository)
            {
                MarkStatusException = new SqliteException("simulated status write failure", 5)
            };

            await Assert.ThrowsAsync<SqliteException>(() =>
                new SuspendedOrderService(interceptingRepository, cart)
                    .RecallOrderAsync(suspended.SuspendedOrderGuid));

            Assert.True(cart.IsEmpty);
            Assert.Equal(
                SuspendedOrderStatus.Pending,
                (await repository.GetAsync(suspended.SuspendedOrderGuid))!.Status);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RecallOrderAsync_preserves_automatic_promotion_discount_and_recalculates_after_edit()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();
            cart.SetAutomaticPromotionRules(
            [
                CreatePromotionRule(
                    applyQuantity: 2,
                    fixedPrice: 15m,
                    products:
                    [
                        new CatalogPromotionProductDto("SKU-PROMO-01", 1)
                    ])
            ]);

            await schema.InitializeAsync();
            var line = cart.AddItem(CreateItem(productCode: "SKU-PROMO-01", lookupCode: "promo-01", price: 10m));
            cart.AddItem(CreateItem(productCode: "SKU-PROMO-01", lookupCode: "promo-01", price: 10m));
            Assert.True(line.IsAutomaticPromotionDiscount);
            Assert.Equal(5m, line.DiscountAmount);

            var suspended = await service.SuspendCurrentOrderAsync(session);
            var saved = await repository.GetAsync(suspended.SuspendedOrderGuid);
            var savedLine = Assert.Single(saved?.Lines ?? []);
            Assert.True(savedLine.IsAutomaticPromotionDiscount);

            await service.RecallOrderAsync(suspended.SuspendedOrderGuid);

            line = Assert.Single(cart.Lines);
            Assert.True(line.IsAutomaticPromotionDiscount);
            Assert.Equal(5m, line.DiscountAmount);
            Assert.Equal(15m, cart.ActualAmount);

            // 取单后继续编辑必须重新套用自动满减，不能把旧满减当成人工折扣保留。
            Assert.True(cart.DecreaseLine(line));

            line = Assert.Single(cart.Lines);
            Assert.False(line.IsAutomaticPromotionDiscount);
            Assert.Equal(0m, line.DiscountAmount);
            Assert.Equal(10m, cart.ActualAmount);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RecallOrderAsync_preserves_promotion_discount_source_so_future_promotions_replace_it()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();

            await schema.InitializeAsync();
            var line = cart.AddItem(CreateItem(productCode: "SKU-PROMO-HOLD-01", lookupCode: "promo-hold-01", price: 10m));
            cart.ApplyPromotionDiscounts([new PromotionLineDiscount(line, 3m)]);

            var suspended = await service.SuspendCurrentOrderAsync(session);
            var saved = await repository.GetAsync(suspended.SuspendedOrderGuid);
            Assert.Equal(PosCartLineDiscountSource.Promotion, Assert.Single(saved!.Lines).DiscountSource);

            await service.RecallOrderAsync(suspended.SuspendedOrderGuid);

            var recalledLine = Assert.Single(cart.Lines);
            Assert.Equal(3m, recalledLine.DiscountAmount);

            // 中文注释：召回后的自动促销折扣仍应保持 Promotion 来源，后续重算才能替换旧金额。
            cart.ApplyPromotionDiscounts([new PromotionLineDiscount(recalledLine, 5m)]);

            Assert.Equal(5m, recalledLine.DiscountAmount);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RecallOrderAsync_restores_return_line_context_and_card_refund_capacity()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();
            var originalOrderGuid = Guid.NewGuid();
            var originalLineGuid = Guid.NewGuid();
            const string returnSourceKey = "S001:ORIGINAL-ORDER-01:LINE-01";
            const string returnReason = "Damaged packaging";

            await schema.InitializeAsync();
            cart.AddReturnLine(new ReturnCartLineRequest(
                "S001",
                "SKU-RETURN-01",
                "REF-RETURN-01",
                "Returned item",
                "return-01",
                "ITEM-RETURN-01",
                "https://images.example/return-01.jpg",
                1m,
                12m,
                PriceSourceKind.StoreRetailPrice,
                PriceSourceKind.StoreRetailPrice.ToString(),
                returnSourceKey,
                originalOrderGuid,
                originalLineGuid,
                ReturnReason: returnReason));
            cart.AddReturnPaymentCapacities(
            [
                new OrderReturnPaymentCapacityDto(
                    PaymentMethodKind.Card,
                    OriginalAmount: 12m,
                    RefundedAmount: 3m,
                    RemainingAmount: 9m,
                    Reference: "SQ:original-card-payment",
                    OriginalOrderGuid: originalOrderGuid)
            ]);

            var suspended = await service.SuspendCurrentOrderAsync(session);
            await service.RecallOrderAsync(suspended.SuspendedOrderGuid);

            var recalledLine = Assert.Single(cart.Lines);
            Assert.Equal(CartLineKind.Return, recalledLine.Kind);
            Assert.True(recalledLine.IsReturnLine);
            Assert.Equal(returnSourceKey, recalledLine.ReturnSourceKey);
            Assert.Equal(originalOrderGuid, recalledLine.OriginalOrderGuid);
            Assert.Equal(originalLineGuid, recalledLine.OriginalOrderLineGuid);
            Assert.Equal(returnReason, recalledLine.ReturnReason);
            Assert.Equal(-12m, cart.ActualAmount);

            var cardCapacity = Assert.Single(cart.ReturnPaymentCapacities);
            Assert.Equal(PaymentMethodKind.Card, cardCapacity.Method);
            Assert.Equal(12m, cardCapacity.OriginalAmount);
            Assert.Equal(3m, cardCapacity.RefundedAmount);
            Assert.Equal(9m, cardCapacity.RemainingAmount);
            Assert.Equal("SQ:original-card-payment", cardCapacity.Reference);
            Assert.Equal(originalOrderGuid, cardCapacity.OriginalOrderGuid);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public void Return_cart_snapshot_exposes_return_reason_context()
    {
        Assert.Contains(
            typeof(ReturnCartLineRequest).GetConstructors().Single().GetParameters(),
            parameter => string.Equals(parameter.Name, "ReturnReason", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(typeof(CartLine).GetProperty("ReturnReason"));
        Assert.NotNull(typeof(PosCartLineSnapshot).GetProperty("ReturnReason"));
    }

    [Fact]
    public async Task SuspendCurrentOrderAsync_freezes_deep_copy_of_automatic_promotion_rules()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();
            var rules = new List<CatalogPromotionRuleDto>
            {
                CreatePromotionRule(
                    applyQuantity: 2,
                    fixedPrice: 15m,
                    products:
                    [
                        new CatalogPromotionProductDto("SKU-FREEZE-01", 1)
                    ])
            };
            cart.SetAutomaticPromotionRules(rules);
            cart.AddItem(CreateItem(productCode: "SKU-FREEZE-01", lookupCode: "freeze-01", price: 10m));
            cart.AddItem(CreateItem(productCode: "SKU-FREEZE-01", lookupCode: "freeze-01", price: 10m));

            await schema.InitializeAsync();
            await EnsureFrozenRuleColumnsAsync(store);

            var suspended = await service.SuspendCurrentOrderAsync(session);

            // 挂单后修改当前目录规则，不得影响已冻结的规则副本。
            rules[0] = rules[0] with { FixedPrice = 0m, PromotionId = "PROMO-CHANGED" };
            rules.Add(CreatePromotionRule(
                applyQuantity: 1,
                fixedPrice: 1m,
                products: [new CatalogPromotionProductDto("SKU-OTHER", 1)]));

            var frozen = Assert.Single(suspended.FrozenPromotionRules ?? []);
            Assert.Equal("PROMO-HOLD-01", frozen.PromotionId);
            Assert.Equal(15m, frozen.FixedPrice);
            Assert.Equal("SKU-FREEZE-01", Assert.Single(frozen.Products).ProductCode);

            // 从仓储读回仍是同一份冻结规则（重启/目录刷新后的映射依据）。
            var saved = await repository.GetAsync(suspended.SuspendedOrderGuid);
            var savedFrozen = Assert.Single(saved?.FrozenPromotionRules ?? []);
            Assert.Equal("PROMO-HOLD-01", savedFrozen.PromotionId);
            Assert.Equal(15m, savedFrozen.FixedPrice);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Suspend_and_recall_preserve_manual_price_provenance()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var session = CreateSession();
            var line = cart.AddItem(CreateItem(productCode: "SKU-MANUAL-HOLD", lookupCode: "manual-hold", price: 10m));
            Assert.True(cart.SetLineUnitPrice(line, 6.6m));

            await schema.InitializeAsync();
            await EnsureFrozenRuleColumnsAsync(store);

            var suspended = await service.SuspendCurrentOrderAsync(session);
            var saved = await repository.GetAsync(suspended.SuspendedOrderGuid);

            Assert.True(Assert.Single(saved!.Lines).IsManualPrice);
            Assert.Equal(6.6m, Assert.Single(saved.Lines).UnitPrice);

            await service.RecallOrderAsync(suspended.SuspendedOrderGuid);

            var recalled = Assert.Single(cart.Lines);
            Assert.True(recalled.IsManualPrice);
            Assert.Equal(6.6m, recalled.UnitPrice);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Suspend_and_recall_preserve_catalog_discount_baseline_and_source()
    {
        var databasePath = CreateTempDatabasePath();

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var cart = new PosCartService();
            var repository = new SuspendedOrderRepository(store);
            var service = new SuspendedOrderService(repository, cart);
            var line = cart.AddItem(CreateItem(
                productCode: "SKU-CATALOG-HOLD",
                lookupCode: "catalog-hold",
                price: 6.99m,
                discountRate: 0.2m));

            await schema.InitializeAsync();
            var suspended = await service.SuspendCurrentOrderAsync(CreateSession());
            var saved = await repository.GetAsync(suspended.SuspendedOrderGuid);

            Assert.Equal(3, (int)Assert.Single(saved!.Lines).DiscountSource);
            Assert.Equal(2000, (int?)typeof(SuspendedOrderLine)
                .GetProperty("CatalogDiscountBasisPoints")
                ?.GetValue(Assert.Single(saved.Lines)));

            await service.RecallOrderAsync(suspended.SuspendedOrderGuid);

            line = Assert.Single(cart.Lines);
            Assert.Equal(2000, (int?)typeof(CartLine)
                .GetProperty("CatalogDiscountBasisPoints")
                ?.GetValue(line));
            Assert.Equal(3, (int)line.DiscountSource);
            Assert.Equal(1.40m, line.DiscountAmount);
            Assert.Equal(5.59m, line.ActualAmount);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static PosSessionState CreateSession()
    {
        return new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);
    }

    /// <summary>
    /// 测试专用：模拟主代理即将补齐的 SuspendedOrders.FrozenPromotionRulesJson 与
    /// SuspendedOrderLines.IsManualPrice 列，验证仓储往返；不修改 LocalSchemaService。
    /// </summary>
    private static async Task EnsureFrozenRuleColumnsAsync(LocalSqliteStore store)
    {
        await using var connection = await store.OpenConnectionAsync();
        var hasFrozenColumn = await HasColumnAsync(connection, "SuspendedOrders", "FrozenPromotionRulesJson");
        if (!hasFrozenColumn)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE SuspendedOrders ADD COLUMN FrozenPromotionRulesJson TEXT NULL;";
            await command.ExecuteNonQueryAsync();
        }

        var hasManualColumn = await HasColumnAsync(connection, "SuspendedOrderLines", "IsManualPrice");
        if (!hasManualColumn)
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
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static SellableItemDto CreateItem(
        string storeCode = "S001",
        string productCode = "SKU-001",
        string lookupCode = "690001",
        string displayName = "Milk 1L",
        string? itemNumber = null,
        decimal price = 10m,
        PriceSourceKind priceSource = PriceSourceKind.StoreRetailPrice,
        string? productImage = null,
        decimal quantityFactor = 1m,
        decimal? discountRate = null)
    {
        return new SellableItemDto(
            StoreCode: storeCode,
            ProductCode: productCode,
            ReferenceCode: null,
            DisplayName: displayName,
            LookupCode: lookupCode,
            ItemNumber: itemNumber ?? productCode,
            Barcode: lookupCode.Trim(),
            RetailPrice: price,
            PriceSource: priceSource,
            PriceSourceLabel: priceSource.ToString(),
            QuantityFactor: quantityFactor,
            UpdatedAt: DateTimeOffset.UtcNow,
            ProductImage: productImage,
            DiscountRate: discountRate);
    }

    private static CatalogPromotionRuleDto CreatePromotionRule(
        int applyQuantity,
        decimal fixedPrice,
        IReadOnlyList<CatalogPromotionProductDto> products,
        string promotionId = "PROMO-HOLD-01",
        bool isExclusive = true,
        int priority = 0,
        int? maxApplicationsPerOrder = null)
    {
        return new CatalogPromotionRuleDto(
            promotionId,
            "Quantity discount",
            isExclusive,
            priority,
            applyQuantity,
            fixedPrice,
            maxApplicationsPerOrder,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow,
            products);
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"hbpos-suspended-{Guid.NewGuid():N}.db");
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

    private sealed class InterceptingSuspendedOrderRepository(ISuspendedOrderRepository inner)
        : ISuspendedOrderRepository
    {
        public Action? BeforeMarkStatus { get; init; }

        public Exception? MarkStatusException { get; init; }

        public bool MarkStatusTokenCanBeCanceled { get; private set; }

        public Task SaveAsync(SuspendedOrder order, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(order, cancellationToken);

        public Task<IReadOnlyList<SuspendedOrderSummary>> GetPendingAsync(
            string storeCode,
            string? deviceCode = null,
            string? keyword = null,
            int take = 100,
            CancellationToken cancellationToken = default) =>
            inner.GetPendingAsync(storeCode, deviceCode, keyword, take, cancellationToken);

        public Task<SuspendedOrder?> GetAsync(
            Guid suspendedOrderGuid,
            CancellationToken cancellationToken = default) =>
            inner.GetAsync(suspendedOrderGuid, cancellationToken);

        public Task MarkStatusAsync(
            Guid suspendedOrderGuid,
            SuspendedOrderStatus status,
            CancellationToken cancellationToken = default)
        {
            MarkStatusTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            BeforeMarkStatus?.Invoke();
            return MarkStatusException is null
                ? inner.MarkStatusAsync(suspendedOrderGuid, status, cancellationToken)
                : Task.FromException(MarkStatusException);
        }
    }

    private static async Task<int> ReadScalarIntAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
