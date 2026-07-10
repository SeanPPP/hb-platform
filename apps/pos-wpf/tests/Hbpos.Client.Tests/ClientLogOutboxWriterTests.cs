using System.Text.Json;
using System.Reflection;
using System.Threading.Channels;
using BlazorApp.Shared.DTOs;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Client.Tests;

[Collection(GlobalLoggingTestCollection.Name)]
public sealed class ClientLogOutboxWriterTests
{
    [Fact]
    public async Task Temp_database_cleanup_is_best_effort_while_windows_handle_is_still_open()
    {
        var databasePath = CreateDatabasePath();
        FileStream? lockedHandle = null;
        try
        {
            lockedHandle = new FileStream(
                databasePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);

            var exception = await Record.ExceptionAsync(() => DeleteDatabaseFilesAsync(databasePath));

            Assert.Null(exception);
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            lockedHandle?.Dispose();
            await DeleteDatabaseFilesAsync(databasePath);
        }
    }

    [Fact]
    public void Runtime_drop_counter_changes_only_when_channel_really_drops_oldest_item()
    {
        var writer = new ClientLogOutboxWriter(
            new ClientLogOutboxStore(CreateDatabasePath()),
            new DeviceAuthorizationState(),
            new CashierSessionContext(),
            new ClientLogIdentity("drop-counter-instance", "1.0.0"),
            runtimeQueueCapacity: 10);

        try
        {
            for (var index = 0; index < 10; index++)
            {
                writer.Enqueue(CreateRuntimeEntry($"initial-{index}"));
            }

            var channelField = typeof(ClientLogOutboxWriter)
                .GetField("_runtimeChannel", BindingFlags.Instance | BindingFlags.NonPublic);
            var channel = Assert.IsAssignableFrom<Channel<QueuedClientLog>>(channelField!.GetValue(writer));
            Assert.True(channel.Reader.TryRead(out _));

            // 这里模拟 reader 已取走元素、旧人工计数尚未递减的真实线程交错；槽位空闲时不能误报丢弃。
            writer.Enqueue(CreateRuntimeEntry("fills-real-free-slot"));
            Assert.Equal(0, writer.RuntimeQueueDroppedCount);

            writer.Enqueue(CreateRuntimeEntry("really-drops-oldest"));
            Assert.Equal(1, writer.RuntimeQueueDroppedCount);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public async Task Stop_retries_inflight_event_with_shared_shutdown_token_instead_of_losing_it()
    {
        var databasePath = CreateDatabasePath();
        var store = new ClientLogOutboxStore(databasePath);
        await store.InitializeAsync(CancellationToken.None);
        var writer = new ClientLogOutboxWriter(
            store,
            new DeviceAuthorizationState(),
            CreateCashierContext(),
            new ClientLogIdentity("inflight-instance", "1.0.0"),
            runtimeQueueCapacity: 20);
        var writeGateField = typeof(ClientLogOutboxStore)
            .GetField("_writeGate", BindingFlags.Instance | BindingFlags.NonPublic);
        var writeGate = Assert.IsType<SemaphoreSlim>(writeGateField!.GetValue(store));

        try
        {
            await writer.StartAsync(CancellationToken.None);
            var warmupEventId = Guid.Parse("dededede-dede-dede-dede-dededededede");
            writer.Record(new OperationAuditEventDto
            {
                EventId = warmupEventId,
                OperationType = "CASHIER_LOGIN",
                Outcome = "Succeeded"
            });
            _ = await WaitForSinglePendingAsync(store, ClientLogOutboxKind.OperationAudit);
            await store.ApplyResultsAsync(
                ClientLogOutboxKind.OperationAudit,
                [warmupEventId],
                [],
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            await writeGate.WaitAsync(CancellationToken.None);
            writer.Record(new OperationAuditEventDto
            {
                EventId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd"),
                OperationType = "CASHIER_LOGOUT",
                Outcome = "Succeeded"
            });
            var channelField = typeof(ClientLogOutboxWriter)
                .GetField("_operationChannel", BindingFlags.Instance | BindingFlags.NonPublic);
            var channel = Assert.IsAssignableFrom<Channel<QueuedClientLog>>(channelField!.GetValue(writer));
            await WaitUntilAsync(() => !channel.Reader.TryPeek(out _));

            using var shutdownBudget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var stopTask = writer.StopAsync(shutdownBudget.Token);
            await Task.Delay(50);
            writeGate.Release();
            await stopTask;

            Assert.Single(await store.ReadPendingAsync(
                ClientLogOutboxKind.OperationAudit,
                DateTimeOffset.UtcNow.AddMinutes(1),
                100,
                CancellationToken.None));
        }
        finally
        {
            if (writeGate.CurrentCount == 0)
            {
                writeGate.Release();
            }

            if (writer.ExecuteTask is { IsCompleted: false })
            {
                using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await writer.StopAsync(cleanupBudget.Token);
            }

            writer.Dispose();
            await DeleteDatabaseFilesAsync(databasePath);
        }
    }

    [Fact]
    public async Task Stop_flushes_last_runtime_and_operation_events_to_local_outbox_within_shared_budget()
    {
        var databasePath = CreateDatabasePath();
        var store = new ClientLogOutboxStore(databasePath);
        var writer = new ClientLogOutboxWriter(
            store,
            new DeviceAuthorizationState(),
            CreateCashierContext(),
            new ClientLogIdentity("shutdown-instance", "1.0.0"),
            runtimeQueueCapacity: 20);

        try
        {
            await writer.StartAsync(CancellationToken.None);
            writer.Enqueue(new ApplicationLogEntry(
                "Information",
                "last runtime event",
                DateTimeOffset.UtcNow,
                "hbpos_win",
                "test",
                "POS"));
            writer.Record(new OperationAuditEventDto
            {
                EventId = Guid.Parse("abababab-abab-abab-abab-abababababab"),
                OperationType = "CASHIER_LOGOUT",
                Outcome = "Succeeded"
            });

            using var shutdownBudget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await writer.StopAsync(shutdownBudget.Token);

            Assert.Single(await store.ReadPendingAsync(
                ClientLogOutboxKind.Runtime,
                DateTimeOffset.UtcNow.AddMinutes(1),
                100,
                CancellationToken.None));
            Assert.Single(await store.ReadPendingAsync(
                ClientLogOutboxKind.OperationAudit,
                DateTimeOffset.UtcNow.AddMinutes(1),
                100,
                CancellationToken.None));
        }
        finally
        {
            writer.Dispose();
            await DeleteDatabaseFilesAsync(databasePath);
        }
    }

    [Fact]
    public void Runtime_burst_coalesces_wakeup_signal_instead_of_accumulating_empty_work()
    {
        var writer = new ClientLogOutboxWriter(
            new ClientLogOutboxStore(CreateDatabasePath()),
            new DeviceAuthorizationState(),
            new CashierSessionContext(),
            new ClientLogIdentity("instance", "1.0.0"),
            runtimeQueueCapacity: 200);
        try
        {
            for (var index = 0; index < 100; index++)
            {
                writer.Enqueue(new ApplicationLogEntry(
                    "Information",
                    $"message-{index}",
                    DateTimeOffset.UtcNow,
                    "hbpos_win",
                    "test",
                    "POS"));
            }

            var field = typeof(ClientLogOutboxWriter).GetField("_signal", BindingFlags.Instance | BindingFlags.NonPublic);
            var signal = Assert.IsType<SemaphoreSlim>(field!.GetValue(writer));
            Assert.Equal(1, signal.CurrentCount);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Fact]
    public async Task Runtime_entry_is_persisted_with_device_cashier_and_process_identity()
    {
        var databasePath = CreateDatabasePath();
        var store = new ClientLogOutboxStore(databasePath);
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-01", "device-secret"));
        var cashierContext = CreateCashierContext();
        var writer = new ClientLogOutboxWriter(
            store,
            authorizationState,
            cashierContext,
            new ClientLogIdentity("instance-1", "1.2.3"),
            runtimeQueueCapacity: 20);

        try
        {
            await store.InitializeAsync(CancellationToken.None);
            await writer.StartAsync(CancellationToken.None);
            writer.Enqueue(new ApplicationLogEntry(
                "Information",
                "sale completed",
                DateTimeOffset.Parse("2026-07-10T01:00:00Z"),
                "hbpos_win",
                "test",
                "POS",
                Category: "Sale",
                Properties: new Dictionary<string, object?>
                {
                    ["category"] = "Sale",
                    ["storeCode"] = "S001",
                    ["deviceCode"] = "POS-01",
                    ["errorCode"] = "NONE",
                    ["elapsedMs"] = 25L,
                    ["source"] = "checkout",
                    ["action"] = "complete",
                    ["status"] = "succeeded",
                    ["screen"] = "payment",
                    ["mode"] = "online",
                    ["reason"] = "sale",
                    ["result"] = "accepted",
                    ["itemCount"] = 2,
                    ["customerEmail"] = "alice@example.com",
                    ["arbitraryPayload"] = "must-not-be-stored"
                }));

            var record = await WaitForSinglePendingAsync(store, ClientLogOutboxKind.Runtime);
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            Assert.Equal(record.EventId, root.GetProperty("clientEventId").GetGuid());
            Assert.Equal("S001", root.GetProperty("storeCode").GetString());
            Assert.Equal("POS-01", root.GetProperty("deviceCode").GetString());
            Assert.Equal("C001", root.GetProperty("userId").GetString());
            Assert.Equal("Alice", root.GetProperty("userName").GetString());
            Assert.Equal("instance-1", root.GetProperty("instanceId").GetString());
            Assert.Equal("1.2.3", root.GetProperty("appVersion").GetString());
            var properties = root.GetProperty("properties");
            foreach (var allowedProperty in new[]
                     {
                         "category", "storeCode", "deviceCode", "errorCode", "elapsedMs", "source", "action",
                         "status", "screen", "mode", "reason", "result", "itemCount"
                     })
            {
                Assert.True(properties.TryGetProperty(allowedProperty, out _), $"缺少允许的属性：{allowedProperty}");
            }

            Assert.False(properties.TryGetProperty("customerEmail", out _));
            Assert.False(properties.TryGetProperty("arbitraryPayload", out _));
        }
        finally
        {
            await writer.StopAsync(CancellationToken.None);
            writer.Dispose();
            await DeleteDatabaseFilesAsync(databasePath);
        }
    }

    [Fact]
    public async Task Operation_record_captures_sanitized_immutable_snapshot_without_throwing()
    {
        var databasePath = CreateDatabasePath();
        var store = new ClientLogOutboxStore(databasePath);
        var authorizationState = new DeviceAuthorizationState();
        authorizationState.Set(new DeviceAuthorizationContext("POS-01", "S001", "HW-01", "device-secret"));
        var writer = new ClientLogOutboxWriter(
            store,
            authorizationState,
            CreateCashierContext(),
            new ClientLogIdentity("instance-2", "2.0.0"),
            runtimeQueueCapacity: 20);
        var audit = new OperationAuditEventDto
        {
            EventId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            OccurredAtUtc = DateTimeOffset.Parse("2026-07-10T01:00:00Z"),
            OperationType = "CART_ITEM_ADD",
            Outcome = "Succeeded",
            CurrencyCode = "USD",
            StoreCode = "WRONG-STORE",
            DeviceCode = "WRONG-DEVICE",
            Properties = new Dictionary<string, string?>
            {
                ["authorization"] = "Bearer secret-token",
                ["safe"] = "unknown-value",
                ["customerEmail"] = "alice@example.com",
                ["previousValue"] = "raw-voucher-secret",
                ["reason"] = "manual-add"
            },
            Items =
            [
                new OperationAuditItemDto { ProductCode = "P001", DisplayName = "Original name" }
            ]
        };

        try
        {
            await store.InitializeAsync(CancellationToken.None);
            await writer.StartAsync(CancellationToken.None);
            var exception = Record.Exception(() => writer.Record(audit));
            audit.Items[0].DisplayName = "Mutated name";

            Assert.Null(exception);
            var record = await WaitForSinglePendingAsync(store, ClientLogOutboxKind.OperationAudit);
            Assert.DoesNotContain("secret-token", record.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("unknown-value", record.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("alice@example.com", record.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-voucher-secret", record.PayloadJson, StringComparison.Ordinal);
            Assert.Contains("manual-add", record.PayloadJson, StringComparison.Ordinal);
            Assert.Contains("Original name", record.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("Mutated name", record.PayloadJson, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            Assert.Equal("S001", root.GetProperty("storeCode").GetString());
            Assert.Equal("POS-01", root.GetProperty("deviceCode").GetString());
            Assert.Equal("C001", root.GetProperty("cashierId").GetString());
            Assert.Equal("instance-2", root.GetProperty("instanceId").GetString());
            Assert.Equal("AUD", root.GetProperty("currencyCode").GetString());
        }
        finally
        {
            await writer.StopAsync(CancellationToken.None);
            writer.Dispose();
            await DeleteDatabaseFilesAsync(databasePath);
        }
    }

    private static CashierSessionContext CreateCashierContext()
    {
        var context = new CashierSessionContext();
        context.SetCurrent(new CashierSessionDto(
            "C001",
            "user-1",
            "Alice",
            "S001",
            "POS-01",
            [],
            [],
            ["S001"],
            false,
            false,
            false));
        return context;
    }

    private static ApplicationLogEntry CreateRuntimeEntry(string message) => new(
        "Information",
        message,
        DateTimeOffset.UtcNow,
        "hbpos_win",
        "test",
        "POS");

    private static async Task<ClientLogOutboxRecord> WaitForSinglePendingAsync(
        ClientLogOutboxStore store,
        ClientLogOutboxKind kind)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var records = await store.ReadPendingAsync(kind, DateTimeOffset.UtcNow.AddDays(1), 100, CancellationToken.None);
            if (records.Count == 1)
            {
                return records[0];
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("日志未在预期时间内落入本地 outbox。");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("未在预期时间内进入受控并发状态。");
    }

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"hbpos-logs-writer-test-{Guid.NewGuid():N}.db");

    private static async Task DeleteDatabaseFilesAsync(string databasePath)
    {
        // 与仓库其他 SQLite 测试一致，先清理 Microsoft.Data.Sqlite 的全局句柄，再删除 Windows 临时文件。
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            for (var attempt = 0; attempt < 20 && File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) when (attempt < 19)
                {
                    // Windows 可能在 SQLite 连接 Dispose 后极短时间仍持有句柄，按文件状态重试测试清理。
                    await Task.Delay(25);
                }
                catch (IOException)
                {
                    // 临时库最终仍被系统持有时采用 best-effort，不能覆盖已经通过的行为断言。
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    // 对齐仓库 SqliteTempFileCleanup：测试清理权限竞态不作为业务失败。
                    break;
                }
            }
        }
    }
}
