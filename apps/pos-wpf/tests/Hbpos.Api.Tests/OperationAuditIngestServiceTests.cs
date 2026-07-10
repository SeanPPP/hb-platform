using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class OperationAuditIngestServiceTests
{
    [Fact]
    public async Task InitializeAsync_creates_POSM_audit_tables_and_is_idempotent()
    {
        await using var fixture = OperationAuditFixture.Create();
        var initializer = new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext);

        Assert.False(await fixture.TableExistsAsync("pos_operation_audit"));
        Assert.False(await fixture.TableExistsAsync("pos_operation_audit_item"));

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        Assert.True(await fixture.TableExistsAsync("pos_operation_audit"));
        Assert.True(await fixture.TableExistsAsync("pos_operation_audit_item"));
        Assert.True(await fixture.IndexExistsAsync("IX_pos_operation_audit_store_time"));
        Assert.Equal(0, await fixture.PosmDb.Queryable<PosOperationAudit>().CountAsync());
        Assert.Equal(0, await fixture.PosmDb.Queryable<PosOperationAuditItem>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_persists_one_event_with_items_and_returns_duplicate_on_retry()
    {
        await using var fixture = OperationAuditFixture.Create();
        await new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext).InitializeAsync();
        var receivedAt = new DateTimeOffset(2026, 7, 10, 1, 2, 3, TimeSpan.Zero);
        var service = new SqlSugarOperationAuditIngestService(
            fixture.DbContext,
            new StubTimeProvider(receivedAt));
        var request = CreateRequest();
        request.Events[0].OperationType = "cart_item_price_change";
        request.Events[0].CurrencyCode = "aud";
        request.Events[0].SafeMessage =
            "failed https://example.test/pay?token=secret /api/pay?token=relative-secret " +
            "Bearer topsecret 4111111111111111 voucherCode=full-voucher " +
            "employeeBarcode=staff-secret api-key=key-secret";
        request.Events[0].Properties = new Dictionary<string, string?>
        {
            ["mode"] = "manual",
            ["previousValue"] = "full-voucher-value",
            ["authorizationCode"] = "must-not-persist",
            ["customerEmail"] = "private@example.test"
        };

        var first = await service.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None);
        var second = await service.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None);

        Assert.Equal(1, first.AcceptedCount);
        Assert.Equal("accepted", Assert.Single(first.Results).Status);
        Assert.Equal(1, second.DuplicateCount);
        Assert.Equal("duplicate", Assert.Single(second.Results).Status);

        var parent = Assert.Single(await fixture.PosmDb.Queryable<PosOperationAudit>().ToListAsync());
        Assert.Equal("STORE-1", parent.StoreCode);
        Assert.Equal("POS-1", parent.DeviceCode);
        Assert.Equal("CART_ITEM_PRICE_CHANGE", parent.OperationType);
        Assert.Equal("AUD", parent.CurrencyCode);
        Assert.Equal(receivedAt.UtcDateTime, parent.ReceivedAtUtc);
        Assert.DoesNotContain("topsecret", parent.SafeMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4111111111111111", parent.SafeMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", parent.SafeMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("relative-secret", parent.SafeMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("full-voucher", parent.SafeMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("staff-secret", parent.SafeMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("key-secret", parent.SafeMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("manual", parent.PropertiesJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("authorizationCode", parent.PropertiesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customerEmail", parent.PropertiesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("full-voucher-value", parent.PropertiesJson ?? string.Empty, StringComparison.Ordinal);

        var item = Assert.Single(await fixture.PosmDb.Queryable<PosOperationAuditItem>().ToListAsync());
        Assert.Equal(request.Events[0].EventId, item.EventId);
        Assert.Equal("SKU-1", item.ProductCode);
        Assert.Equal("9300000000001", item.LookupCode);
        Assert.Equal(19.95m, item.AfterActualAmount);
    }

    [Fact]
    public async Task IngestAsync_returns_duplicate_for_repeated_event_in_same_batch()
    {
        await using var fixture = OperationAuditFixture.Create();
        await new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext).InitializeAsync();
        var service = new SqlSugarOperationAuditIngestService(fixture.DbContext);
        var request = CreateRequest();
        request.Events.Add(request.Events[0]);

        var result = await service.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(["accepted", "duplicate"], result.Results.Select(item => item.Status).ToArray());
        Assert.Equal(1, await fixture.PosmDb.Queryable<PosOperationAudit>().CountAsync());
        Assert.Equal(1, await fixture.PosmDb.Queryable<PosOperationAuditItem>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_two_independent_contexts_concurrently_persist_event_once()
    {
        await using var fixture = OperationAuditFixture.Create();
        await new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext).InitializeAsync();
        using var peer = fixture.CreatePeerContext();
        var firstService = new SqlSugarOperationAuditIngestService(fixture.DbContext);
        var secondService = new SqlSugarOperationAuditIngestService(peer.DbContext);
        var request = CreateRequest();

        var results = await Task.WhenAll(
            firstService.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None),
            secondService.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None));

        Assert.Equal(1, results.Sum(result => result.AcceptedCount));
        Assert.Equal(1, results.Sum(result => result.DuplicateCount));
        Assert.Equal(1, await fixture.PosmDb.Queryable<PosOperationAudit>().CountAsync());
        Assert.Equal(1, await fixture.PosmDb.Queryable<PosOperationAuditItem>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_rejects_invalid_event_but_persists_valid_sibling()
    {
        await using var fixture = OperationAuditFixture.Create();
        await new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext).InitializeAsync();
        var service = new SqlSugarOperationAuditIngestService(fixture.DbContext);
        var request = CreateRequest();
        request.Events.Add(new OperationAuditEventDto
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            OperationType = "PAYMENT_CANCEL",
            Outcome = "Unknown",
            StoreCode = "STORE-1",
            DeviceCode = "POS-1"
        });

        var result = await service.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal("INVALID_OUTCOME", result.Results.Single(x => x.Status == "rejected").ErrorCode);
        Assert.Equal(1, await fixture.PosmDb.Queryable<PosOperationAudit>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_treats_null_items_as_empty_collection()
    {
        await using var fixture = OperationAuditFixture.Create();
        await new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext).InitializeAsync();
        var service = new SqlSugarOperationAuditIngestService(fixture.DbContext);
        var request = CreateRequest();
        request.Events[0].Items = null!;

        var result = await service.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None);

        Assert.Equal(1, result.AcceptedCount);
        var parent = Assert.Single(await fixture.PosmDb.Queryable<PosOperationAudit>().ToListAsync());
        Assert.Equal(0, parent.ProductCount);
        Assert.Equal(0, await fixture.PosmDb.Queryable<PosOperationAuditItem>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_rejects_event_containing_null_item_without_throwing()
    {
        await using var fixture = OperationAuditFixture.Create();
        await new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext).InitializeAsync();
        var service = new SqlSugarOperationAuditIngestService(fixture.DbContext);
        var request = CreateRequest();
        request.Events[0].Items.Add(null!);

        var result = await service.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None);

        Assert.Equal(1, result.RejectedCount);
        Assert.Equal("INVALID_ITEM", Assert.Single(result.Results).ErrorCode);
        Assert.Equal(0, await fixture.PosmDb.Queryable<PosOperationAudit>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_rejects_non_AUD_currency()
    {
        await using var fixture = OperationAuditFixture.Create();
        await new SqlSugarOperationAuditSchemaInitializer(fixture.DbContext).InitializeAsync();
        var service = new SqlSugarOperationAuditIngestService(fixture.DbContext);
        var request = CreateRequest();
        request.Events[0].CurrencyCode = "USD";

        var result = await service.IngestAsync(request, "STORE-1", "POS-1", CancellationToken.None);

        Assert.Equal(1, result.RejectedCount);
        Assert.Equal("INVALID_CURRENCY", Assert.Single(result.Results).ErrorCode);
        Assert.Equal(0, await fixture.PosmDb.Queryable<PosOperationAudit>().CountAsync());
    }

    private static OperationAuditBatchRequestDto CreateRequest()
    {
        return new OperationAuditBatchRequestDto
        {
            Events =
            [
                new OperationAuditEventDto
                {
                    EventId = Guid.NewGuid(),
                    SchemaVersion = 1,
                    OccurredAtUtc = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
                    OperationType = "CART_ITEM_PRICE_CHANGE",
                    Outcome = "Succeeded",
                    CashierId = "C-1",
                    UserGuid = "U-1",
                    CashierName = "Alice",
                    StoreCode = "STORE-1",
                    DeviceCode = "POS-1",
                    AppVersion = "1.2.3",
                    InstanceId = "instance-1",
                    OrderGuid = "ORDER-1",
                    CurrencyCode = "AUD",
                    BeforeActual = 10m,
                    AfterActual = 19.95m,
                    AmountDelta = 9.95m,
                    Items =
                    [
                        new OperationAuditItemDto
                        {
                            ProductCode = "SKU-1",
                            LookupCode = "9300000000001",
                            DisplayName = "Test product",
                            BeforeQuantity = 1m,
                            AfterQuantity = 1m,
                            BeforeUnitPrice = 10m,
                            AfterUnitPrice = 19.95m,
                            BeforeActualAmount = 10m,
                            AfterActualAmount = 19.95m,
                            ActualAmountDelta = 9.95m
                        }
                    ]
                }
            ]
        };
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class OperationAuditFixture : IAsyncDisposable
    {
        private readonly string mainDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-audit-main-{Guid.NewGuid():N}.db");
        private readonly string posmDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-audit-posm-{Guid.NewGuid():N}.db");

        private OperationAuditFixture()
        {
            MainDb = CreateClient(mainDatabasePath);
            PosmDb = CreateClient(posmDatabasePath);
            DbContext = CreateDbContext(MainDb, PosmDb);
        }

        public ISqlSugarClient MainDb { get; }

        public ISqlSugarClient PosmDb { get; }

        public HbposSqlSugarContext DbContext { get; }

        public static OperationAuditFixture Create() => new();

        public PeerOperationAuditContext CreatePeerContext()
        {
            var mainDb = CreateClient(mainDatabasePath);
            var posmDb = CreateClient(posmDatabasePath);
            return new PeerOperationAuditContext(mainDb, posmDb, CreateDbContext(mainDb, posmDb));
        }

        public async Task<bool> TableExistsAsync(string tableName)
        {
            var count = await PosmDb.Ado.GetIntAsync(
                "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @tableName",
                new SugarParameter("@tableName", tableName));
            return count > 0;
        }

        public async Task<bool> IndexExistsAsync(string indexName)
        {
            var count = await PosmDb.Ado.GetIntAsync(
                "SELECT COUNT(1) FROM sqlite_master WHERE type = 'index' AND name = @indexName",
                new SugarParameter("@indexName", indexName));
            return count > 0;
        }

        public ValueTask DisposeAsync()
        {
            MainDb.Dispose();
            PosmDb.Dispose();
            DeleteIfExists(mainDatabasePath);
            DeleteIfExists(posmDatabasePath);
            return ValueTask.CompletedTask;
        }

        private static ISqlSugarClient CreateClient(string databasePath)
        {
            return new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"Data Source={databasePath}",
                DbType = DbType.Sqlite,
                InitKeyType = InitKeyType.Attribute,
                IsAutoCloseConnection = true
            });
        }

        private static HbposSqlSugarContext CreateDbContext(ISqlSugarClient mainDb, ISqlSugarClient posmDb)
        {
            var context = (HbposSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HbposSqlSugarContext));
            SetAutoProperty(context, nameof(HbposSqlSugarContext.MainDb), mainDb);
            SetAutoProperty(context, nameof(HbposSqlSugarContext.PosmDb), posmDb);
            return context;
        }

        private static void SetAutoProperty(HbposSqlSugarContext context, string propertyName, ISqlSugarClient value)
        {
            var backingField = typeof(HbposSqlSugarContext).GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(backingField);
            backingField!.SetValue(context, value);
        }

        private static void DeleteIfExists(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // SQLite 可能短暂占用测试库文件，不影响断言结果。
            }
        }

        public sealed class PeerOperationAuditContext(
            ISqlSugarClient mainDb,
            ISqlSugarClient posmDb,
            HbposSqlSugarContext dbContext) : IDisposable
        {
            public HbposSqlSugarContext DbContext { get; } = dbContext;

            public void Dispose()
            {
                mainDb.Dispose();
                posmDb.Dispose();
            }
        }
    }
}
