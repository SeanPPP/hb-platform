using Hbpos.Client.Wpf.Services;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class ClientLogOutboxTests
{
    [Fact]
    public async Task Initialize_creates_independent_dual_outbox_schema_with_required_pragmas()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var store = new ClientLogOutboxStore(databasePath);

            await store.InitializeAsync(CancellationToken.None);

            await using (var connection = await store.OpenConnectionAsync(CancellationToken.None))
            {
                Assert.Equal("wal", await ExecuteScalarAsync<string>(connection, "PRAGMA journal_mode;"));
                Assert.Equal(5000L, await ExecuteScalarAsync<long>(connection, "PRAGMA busy_timeout;"));
                Assert.Equal(1L, await ExecuteScalarAsync<long>(connection, "PRAGMA user_version;"));
                Assert.True(await TableExistsAsync(connection, "RuntimeLogOutbox"));
                Assert.True(await TableExistsAsync(connection, "OperationAuditOutbox"));
            }
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Pending_records_are_idempotent_and_read_oldest_first()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new ClientLogOutboxStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);
            var now = DateTimeOffset.Parse("2026-07-10T01:00:00Z");
            var olderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var newerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

            await store.EnqueueAsync(ClientLogOutboxKind.Runtime, newerId, now, "{\"message\":\"new\"}", now, CancellationToken.None);
            await store.EnqueueAsync(ClientLogOutboxKind.Runtime, olderId, now.AddMinutes(-1), "{\"message\":\"old\"}", now, CancellationToken.None);
            await store.EnqueueAsync(ClientLogOutboxKind.Runtime, olderId, now.AddMinutes(-1), "{\"message\":\"duplicate\"}", now, CancellationToken.None);

            var pending = await store.ReadPendingAsync(ClientLogOutboxKind.Runtime, now, 100, CancellationToken.None);

            Assert.Equal([olderId, newerId], pending.Select(item => item.EventId));
            Assert.Equal("{\"message\":\"old\"}", pending[0].PayloadJson);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Apply_results_deletes_completed_and_quarantines_permanent_rejection()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new ClientLogOutboxStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);
            var now = DateTimeOffset.Parse("2026-07-10T01:00:00Z");
            var acceptedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var duplicateId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var rejectedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            foreach (var eventId in new[] { acceptedId, duplicateId, rejectedId })
            {
                await store.EnqueueAsync(ClientLogOutboxKind.OperationAudit, eventId, now, "{}", now, CancellationToken.None);
            }

            await store.ApplyResultsAsync(
                ClientLogOutboxKind.OperationAudit,
                [acceptedId, duplicateId],
                [new ClientLogRejection(rejectedId, "INVALID_EVENT", "invalid payload")],
                now,
                CancellationToken.None);

            Assert.Empty(await store.ReadPendingAsync(ClientLogOutboxKind.OperationAudit, now, 100, CancellationToken.None));
            var rejected = await store.ReadRejectedAsync(ClientLogOutboxKind.OperationAudit, 100, CancellationToken.None);
            var item = Assert.Single(rejected);
            Assert.Equal(rejectedId, item.EventId);
            Assert.Equal("INVALID_EVENT", item.LastErrorCode);
            Assert.Equal("invalid payload", item.LastErrorMessage);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Retry_is_not_due_until_next_attempt_and_increments_attempt_count()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new ClientLogOutboxStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);
            var now = DateTimeOffset.Parse("2026-07-10T01:00:00Z");
            var eventId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
            await store.EnqueueAsync(ClientLogOutboxKind.Runtime, eventId, now, "{}", now, CancellationToken.None);

            await store.ScheduleRetryAsync(
                ClientLogOutboxKind.Runtime,
                [eventId],
                now.AddMinutes(5),
                "HTTP_503",
                "temporary failure",
                CancellationToken.None);

            Assert.Empty(await store.ReadPendingAsync(ClientLogOutboxKind.Runtime, now, 100, CancellationToken.None));
            var due = Assert.Single(await store.ReadPendingAsync(
                ClientLogOutboxKind.Runtime,
                now.AddMinutes(5),
                100,
                CancellationToken.None));
            Assert.Equal(1, due.AttemptCount);
            Assert.Equal("HTTP_503", due.LastErrorCode);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Runtime_cap_drops_oldest_but_operation_audit_is_never_capacity_trimmed()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new ClientLogOutboxStore(databasePath, runtimePendingLimit: 2);
            await store.InitializeAsync(CancellationToken.None);
            var now = DateTimeOffset.Parse("2026-07-10T01:00:00Z");
            var ids = new[]
            {
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Guid.Parse("10000000-0000-0000-0000-000000000003")
            };

            for (var index = 0; index < ids.Length; index++)
            {
                var occurredAt = now.AddSeconds(index);
                await store.EnqueueAsync(ClientLogOutboxKind.Runtime, ids[index], occurredAt, "{}", occurredAt, CancellationToken.None);
                await store.EnqueueAsync(ClientLogOutboxKind.OperationAudit, ids[index], occurredAt, "{}", occurredAt, CancellationToken.None);
            }

            var runtime = await store.ReadPendingAsync(ClientLogOutboxKind.Runtime, now.AddMinutes(1), 100, CancellationToken.None);
            var audits = await store.ReadPendingAsync(ClientLogOutboxKind.OperationAudit, now.AddMinutes(1), 100, CancellationToken.None);
            Assert.Equal(ids[1..], runtime.Select(item => item.EventId));
            Assert.Equal(ids, audits.Select(item => item.EventId));
            Assert.Equal(1, store.RuntimeDroppedCount);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Cleanup_removes_rejected_records_older_than_thirty_days()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new ClientLogOutboxStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);
            var eventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
            var rejectedAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
            await store.EnqueueAsync(ClientLogOutboxKind.OperationAudit, eventId, rejectedAt, "{}", rejectedAt, CancellationToken.None);
            await store.ApplyResultsAsync(
                ClientLogOutboxKind.OperationAudit,
                [],
                [new ClientLogRejection(eventId, "INVALID", null)],
                rejectedAt,
                CancellationToken.None);

            await store.DeleteExpiredRejectedAsync(rejectedAt.AddDays(31), CancellationToken.None);

            Assert.Empty(await store.ReadRejectedAsync(ClientLogOutboxKind.OperationAudit, 100, CancellationToken.None));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"hbpos-logs-test-{Guid.NewGuid():N}.db");
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
