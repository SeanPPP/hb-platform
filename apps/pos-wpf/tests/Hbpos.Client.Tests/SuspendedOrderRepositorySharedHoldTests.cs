using System.Globalization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Microsoft.Data.Sqlite;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// 本地先存原子性：SuspendedOrderRepository.SaveAsync 的现有事务必须同时插入
/// SharedHeldOrderPublications NeedsEvaluation；任一插入失败整体回滚，绝不留孤儿行。
/// </summary>
public sealed class SuspendedOrderRepositorySharedHoldTests
{
    [Fact]
    public async Task SaveAsync_commits_hold_and_needs_evaluation_publication_atomically()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new SuspendedOrderRepository(store);
            var order = SampleOrder();

            await repository.SaveAsync(order);

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrders;"));
            Assert.Equal(2, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrderLines;"));

            var publication = await ReadPublicationAsync(connection, order.SuspendedOrderGuid);
            Assert.NotNull(publication);
            Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation, publication!.Status);
            Assert.Equal(1, publication.Revision);
            Assert.Equal(0, publication.RetryCount);
            Assert.Null(publication.PayloadCiphertext);
            Assert.Null(publication.ErrorCode);
            Assert.Null(publication.ErrorMessage);
            Assert.Equal(
                order.SuspendedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                publication.HeldAtIso);
            Assert.Equal(publication.CreatedAtIso, publication.UpdatedAtIso);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_creates_publication_with_null_share_requested_at()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new SuspendedOrderRepository(store);
            var order = SampleOrder();

            await repository.SaveAsync(order);

            await using var connection = await store.OpenConnectionAsync();
            var publication = await ReadPublicationAsync(connection, order.SuspendedOrderGuid);
            Assert.NotNull(publication);
            // 本地先存只写 NeedsEvaluation；显式共享请求前请求时间必须为空。
            Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation, publication!.Status);
            Assert.Null(publication.ShareRequestedAtIso);
            // 未请求的挂单默认不进入发布评估。
            Assert.Empty(await new SharedHeldOrderRepository(
                store,
                new SharedHeldOrderClientTestSupport.TestPayloadProtector(),
                new SharedHeldOrderClientTestSupport.TestPayloadSerializer())
                .ListLegacyOrdersNeedingEvaluationAsync("S001"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_publication_insert_failure_rolls_back_entire_save()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new SuspendedOrderRepository(store);
            var order = SampleOrder();

            // 预先占用同一 LocalHoldGuid 的 publication 主键，使 SaveAsync 内的
            // publication INSERT 必然冲突；若 publication 不在同一事务内，
            // SuspendedOrders 头/行会在冲突前被提交，形成孤儿数据。
            await using (var setupConnection = await store.OpenConnectionAsync())
            {
                await using var command = setupConnection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO SharedHeldOrderPublications (
                        LocalHoldGuid, StoreCode, DeviceCode, Status, Revision, RetryCount,
                        ErrorCode, ErrorMessage, PayloadCiphertext, HeldAtIso, CreatedAtIso, UpdatedAtIso,
                        ShareRequestedAtIso)
                    VALUES ($Guid, 'S001', 'POS-01', 'Blocked', 1, 0,
                            'ReturnLineNotSupported', 'pre-existing', NULL, $Now, $Now, $Now, $Now);
                    """;
                command.Parameters.AddWithValue("$Guid", order.SuspendedOrderGuid.ToString("D"));
                command.Parameters.AddWithValue("$Now", "2026-07-28T00:00:00.000Z");
                await command.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<SqliteException>(() => repository.SaveAsync(order));

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(0, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrders;"));
            Assert.Equal(0, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrderLines;"));
            Assert.Equal(0, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrderReturnPaymentCapacities;"));
            var existing = await ReadPublicationAsync(connection, order.SuspendedOrderGuid);
            Assert.NotNull(existing);
            Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, existing!.Status);
            Assert.Equal(1, existing.Revision);
            Assert.Equal("ReturnLineNotSupported", existing.ErrorCode);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_fails_before_transaction_when_catalog_baseline_column_is_missing()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new SuspendedOrderRepository(store);
            var order = SampleOrder();
            order = order with
            {
                Lines =
                [
                    order.Lines[0] with { CatalogDiscountBasisPoints = 2_000 }
                ]
            };

            await using (var setupConnection = await store.OpenConnectionAsync())
            {
                await using var command = setupConnection.CreateCommand();
                command.CommandText = "ALTER TABLE SuspendedOrderLines DROP COLUMN CatalogDiscountBasisPoints;";
                await command.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(order));

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(0, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrders;"));
            Assert.Equal(0, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrderLines;"));
            Assert.Equal(0, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SharedHeldOrderPublications;"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_keeps_legacy_schema_compatibility_when_no_catalog_baseline_is_present()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new SuspendedOrderRepository(store);
            var order = SampleOrder();

            await using (var setupConnection = await store.OpenConnectionAsync())
            {
                await using var command = setupConnection.CreateCommand();
                command.CommandText = "ALTER TABLE SuspendedOrderLines DROP COLUMN CatalogDiscountBasisPoints;";
                await command.ExecuteNonQueryAsync();
            }

            await repository.SaveAsync(order);

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrders;"));
            Assert.Equal(2, await ReadIntAsync(connection, "SELECT COUNT(*) FROM SuspendedOrderLines;"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Recall_and_cancel_status_changes_do_not_touch_publication()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new SuspendedOrderRepository(store);
            var order = SampleOrder();
            await repository.SaveAsync(order);

            await repository.MarkStatusAsync(order.SuspendedOrderGuid, SuspendedOrderStatus.Recalled);

            await using var connection = await store.OpenConnectionAsync();
            var publication = await ReadPublicationAsync(connection, order.SuspendedOrderGuid);
            Assert.NotNull(publication);
            Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation, publication!.Status);
            Assert.Equal(1, await ReadIntAsync(
                connection,
                $"SELECT COUNT(*) FROM SharedHeldOrderPublications WHERE LocalHoldGuid = '{order.SuspendedOrderGuid:D}';"));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static SuspendedOrder SampleOrder()
    {
        var orderGuid = Guid.NewGuid();
        var suspendedAt = new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero);
        return new SuspendedOrder(
            orderGuid,
            "S001",
            "POS-01",
            "cashier-1",
            "Cashier One",
            suspendedAt,
            21m,
            1m,
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
                    0m,
                    null,
                    11m,
                    PriceSourceKind.StoreRetailPrice,
                    "Store Retail Price"),
                new SuspendedOrderLine(
                    Guid.NewGuid(),
                    orderGuid,
                    "S001",
                    "P-2",
                    null,
                    "Product 2",
                    "CODE-2",
                    null,
                    null,
                    1m,
                    10m,
                    1m,
                    null,
                    9m,
                    PriceSourceKind.ProductBase,
                    "Product Base",
                    PosCartLineDiscountSource.Manual)
            ]);
    }

    private static async Task<SharedHeldOrderPublication?> ReadPublicationAsync(
        SqliteConnection connection,
        Guid localHoldGuid)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT LocalHoldGuid, StoreCode, DeviceCode, Status, Revision, RetryCount,
                   ErrorCode, ErrorMessage, PayloadCiphertext, HeldAtIso, CreatedAtIso, UpdatedAtIso,
                   LastAttemptAtIso, NextAttemptAtIso, RemoteRevision, RemoteUpdatedAtIso, ShareRequestedAtIso
            FROM SharedHeldOrderPublications
            WHERE LocalHoldGuid = $Guid;
            """;
        command.Parameters.AddWithValue("$Guid", localHoldGuid.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var statusText = reader.GetString(reader.GetOrdinal("Status"));
        return new SharedHeldOrderPublication(
            Guid.ParseExact(reader.GetString(reader.GetOrdinal("LocalHoldGuid")), "D"),
            reader.GetString(reader.GetOrdinal("StoreCode")),
            reader.GetString(reader.GetOrdinal("DeviceCode")),
            statusText switch
            {
                "NeedsEvaluation" => SharedHeldOrderPublicationStatus.NeedsEvaluation,
                "PendingPublish" => SharedHeldOrderPublicationStatus.PendingPublish,
                "Published" => SharedHeldOrderPublicationStatus.Published,
                "Blocked" => SharedHeldOrderPublicationStatus.Blocked,
                _ => throw new InvalidDataException(statusText)
            },
            reader.GetInt32(reader.GetOrdinal("Revision")),
            reader.GetInt32(reader.GetOrdinal("RetryCount")),
            ReadNullableString(reader, "ErrorCode"),
            ReadNullableString(reader, "ErrorMessage"),
            ReadNullableBlob(reader, "PayloadCiphertext"),
            reader.GetString(reader.GetOrdinal("HeldAtIso")),
            reader.GetString(reader.GetOrdinal("CreatedAtIso")),
            reader.GetString(reader.GetOrdinal("UpdatedAtIso")),
            ReadNullableString(reader, "LastAttemptAtIso"),
            ReadNullableString(reader, "NextAttemptAtIso"),
            ReadNullableInt64(reader, "RemoteRevision"),
            ReadNullableString(reader, "RemoteUpdatedAtIso"),
            ReadNullableString(reader, "ShareRequestedAtIso"));
    }

    private static string? ReadNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static byte[]? ReadNullableBlob(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (byte[])reader.GetValue(ordinal);
    }

    private static long? ReadNullableInt64(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static async Task<int> ReadIntAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"hbpos-suspended-shared-{Guid.NewGuid():N}.db");
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
