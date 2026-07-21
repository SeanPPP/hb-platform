using Hbpos.Client.Wpf.Services;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class LocalSchemaServiceTests
{
    [Fact]
    public async Task InitializeAsync_recovers_only_interrupted_order_uploads()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-local-schema-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            var schema = new LocalSchemaService(store);
            var interruptedOrderGuid = Guid.NewGuid().ToString("D");
            var unrelatedOrderGuid = Guid.NewGuid().ToString("D");

            await schema.InitializeAsync();
            await using (var connection = await store.OpenConnectionAsync())
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO LocalOrders (
                        OrderGuid, StoreCode, DeviceCode, CashierId, CashierName, SoldAt,
                        TotalAmount, DiscountAmount, ActualAmount, SyncStatus)
                    VALUES
                        ($InterruptedOrderGuid, 'S001', 'POS-01', 'C001', 'Alice', '2026-07-21T10:00:00+10:00', '1.00', '0.00', '1.00', 'Syncing'),
                        ($UnrelatedOrderGuid, 'S001', 'POS-01', 'C001', 'Alice', '2026-07-21T10:01:00+10:00', '2.00', '0.00', '2.00', 'Syncing');

                    INSERT INTO SyncQueue (EntityId, EntityType, Status, CreatedAt)
                    VALUES
                        ($InterruptedOrderGuid, 'Order', 'Syncing', '2026-07-21T10:00:00+10:00'),
                        ($UnrelatedOrderGuid, 'Catalog', 'Syncing', '2026-07-21T10:01:00+10:00');
                    """;
                command.Parameters.AddWithValue("$InterruptedOrderGuid", interruptedOrderGuid);
                command.Parameters.AddWithValue("$UnrelatedOrderGuid", unrelatedOrderGuid);
                await command.ExecuteNonQueryAsync();
            }

            await schema.InitializeAsync();

            await using var verificationConnection = await store.OpenConnectionAsync();
            await using var verificationCommand = verificationConnection.CreateCommand();
            verificationCommand.CommandText =
                """
                SELECT
                    (SELECT SyncStatus FROM LocalOrders WHERE OrderGuid = $InterruptedOrderGuid),
                    (SELECT Status FROM SyncQueue WHERE EntityId = $InterruptedOrderGuid AND EntityType = 'Order'),
                    (SELECT SyncStatus FROM LocalOrders WHERE OrderGuid = $UnrelatedOrderGuid),
                    (SELECT Status FROM SyncQueue WHERE EntityId = $UnrelatedOrderGuid AND EntityType = 'Catalog');
                """;
            verificationCommand.Parameters.AddWithValue("$InterruptedOrderGuid", interruptedOrderGuid);
            verificationCommand.Parameters.AddWithValue("$UnrelatedOrderGuid", unrelatedOrderGuid);

            await using var reader = await verificationCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Pending", reader.GetString(0));
            Assert.Equal("Pending", reader.GetString(1));
            Assert.Equal("Syncing", reader.GetString(2));
            Assert.Equal("Syncing", reader.GetString(3));
        }
        finally
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
}
