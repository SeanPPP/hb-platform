using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Linkly;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class LinklySettlementStorageTests
{
    [Fact]
    public async Task TryCreatePendingAsync_allows_only_one_unresolved_record_for_the_same_business_date()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-linkly-settlement-pending-race-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var firstRepository = new LocalLinklySettlementRepository(store);
            var secondRepository = new LocalLinklySettlementRepository(store);
            var requestedAt = DateTimeOffset.UtcNow;
            var first = CreatePendingSettlement(Guid.NewGuid(), requestedAt);
            var second = CreatePendingSettlement(Guid.NewGuid(), requestedAt.AddMilliseconds(1));

            var created = await Task.WhenAll(
                Task.Run(() => firstRepository.TryCreatePendingAsync(first)),
                Task.Run(() => secondRepository.TryCreatePendingAsync(second)));
            var settlements = await firstRepository.GetByBusinessDateAsync("S001", "POS-01", DateTime.Today);

            Assert.Equal(1, created.Count(result => result));
            var stored = Assert.Single(settlements);
            Assert.Equal(LocalLinklySettlementStatus.Pending, stored.Status);
            Assert.Contains(stored.SettlementGuid, new[] { first.SettlementGuid, second.SettlementGuid });
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

    [Fact]
    public async Task Repository_round_trips_sanitized_receipts_and_print_state()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-linkly-settlement-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalLinklySettlementRepository(store);
            var settlementGuid = Guid.NewGuid();
            var requestedAt = new DateTimeOffset(2026, 8, 1, 7, 30, 0, TimeSpan.Zero);

            await repository.CreatePendingAsync(new LocalLinklySettlementRecord(
                settlementGuid,
                "S001",
                "POS-01",
                new DateTime(2026, 8, 1),
                "LocalIp",
                "Production",
                ProviderSessionId: null,
                LocalLinklySettlementStatus.Pending,
                ResponseCode: null,
                ResponseText: null,
                SettlementData: null,
                ReceiptTexts: [],
                requestedAt,
                CompletedAt: null,
                FirstPrintedAt: null,
                LastPrintedAt: null,
                PrintCount: 0,
                LastPrintError: null));
            await repository.BindProviderSessionAsync(settlementGuid, "settlement-001");
            await repository.CompleteAsync(settlementGuid, new LocalLinklySettlementCompletion(
                LocalLinklySettlementStatus.Succeeded,
                "00",
                "Settlement complete CardNumber: 4111 1111 1111 1111",
                "{\"cardNumber\":\"4111111111111111\",\"track2\":\"4111111111111111=2912\"}",
                [
                    "TXN REF: 123456\nCARD 4111 1111 1111 1111\nTrack2: 4111111111111111=2912",
                    "TXN REF: 123456\nCARD 4111 1111 1111 1111\nTrack2: 4111111111111111=2912"
                ],
                requestedAt.AddMinutes(1),
                ProviderSubmissionState.Submitted));
            await repository.MarkPrintedAsync(settlementGuid, requestedAt.AddMinutes(2));
            await repository.MarkPrintedAsync(settlementGuid, requestedAt.AddMinutes(3));
            await repository.MarkPrintFailedAsync(
                settlementGuid,
                "paper out 4111111111111111",
                requestedAt.AddMinutes(4));

            var stored = Assert.Single(await repository.GetByBusinessDateAsync("S001", "POS-01", new DateTime(2026, 8, 1)));

            Assert.Equal(settlementGuid, stored.SettlementGuid);
            Assert.Equal("settlement-001", stored.ProviderSessionId);
            Assert.Equal(LocalLinklySettlementStatus.Succeeded, stored.Status);
            Assert.Equal(requestedAt, stored.RequestedAt);
            Assert.Equal(2, stored.PrintCount);
            Assert.Equal(requestedAt.AddMinutes(3), stored.LastPrintedAt);
            Assert.Equal("paper out ****1111", stored.LastPrintError);
            Assert.Equal(ProviderSubmissionState.Submitted, stored.ProviderSubmissionState);
            Assert.Equal(LocalLinklySettlementUploadStatus.Pending, stored.UploadStatus);
            Assert.Equal(6, stored.PayloadRevision);
            Assert.Equal(requestedAt.AddMinutes(4), stored.NextUploadAt);
            Assert.Equal(2, stored.ReceiptTexts.Count);
            Assert.Equal(stored.ReceiptTexts[0], stored.ReceiptTexts[1]);
            Assert.Contains("CARD ****1111", stored.ReceiptTexts[0], StringComparison.Ordinal);
            Assert.DoesNotContain("4111 1111", stored.ReceiptTexts[0], StringComparison.Ordinal);
            Assert.DoesNotContain("Track2", stored.ReceiptTexts[0], StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Settlement complete CardNumber: ****1111", stored.ResponseText);
            Assert.Contains("****1111", stored.SettlementData, StringComparison.Ordinal);
            Assert.DoesNotContain("track2", stored.SettlementData, StringComparison.OrdinalIgnoreCase);
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

    [Theory]
    [InlineData(LocalLinklySettlementManualResolution.ConfirmedSucceeded, LocalLinklySettlementStatus.Succeeded, ProviderSubmissionState.Submitted)]
    [InlineData(LocalLinklySettlementManualResolution.ConfirmedFailed, LocalLinklySettlementStatus.Failed, ProviderSubmissionState.Submitted)]
    [InlineData(LocalLinklySettlementManualResolution.ConfirmedNotSubmitted, LocalLinklySettlementStatus.Failed, ProviderSubmissionState.NotSubmitted)]
    public async Task TryResolveUncertainAsync_uses_revision_and_status_as_a_compare_and_swap(
        LocalLinklySettlementManualResolution resolution,
        LocalLinklySettlementStatus expectedStatus,
        ProviderSubmissionState expectedSubmissionState)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-linkly-settlement-resolve-{Guid.NewGuid():N}.db");

        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalLinklySettlementRepository(store);
            var settlement = CreatePendingSettlement(Guid.NewGuid(), DateTimeOffset.UtcNow);
            await repository.CreatePendingAsync(settlement);

            var resolvedAt = DateTimeOffset.UtcNow.AddMinutes(1);
            Assert.True(await repository.TryResolveUncertainAsync(
                settlement.SettlementGuid,
                expectedPayloadRevision: 1,
                resolution,
                resolvedAt));
            Assert.False(await repository.TryResolveUncertainAsync(
                settlement.SettlementGuid,
                expectedPayloadRevision: 1,
                resolution,
                resolvedAt.AddSeconds(1)));
            Assert.False(await repository.TryResolveUncertainAsync(
                settlement.SettlementGuid,
                expectedPayloadRevision: 2,
                resolution,
                resolvedAt.AddSeconds(1)));

            var stored = Assert.Single(await repository.GetByBusinessDateAsync("S001", "POS-01", DateTime.Today));
            Assert.Equal(expectedStatus, stored.Status);
            Assert.Equal(expectedSubmissionState, stored.ProviderSubmissionState);
            Assert.Equal(2, stored.PayloadRevision);
            Assert.Equal(LocalLinklySettlementUploadStatus.Pending, stored.UploadStatus);
            Assert.Equal(resolvedAt, stored.CompletedAt);
            Assert.DoesNotContain("4111", stored.ResponseText ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Schema_migrates_submission_state_and_requeues_only_the_known_legacy_rejection()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-linkly-settlement-migration-{Guid.NewGuid():N}.db");
        var pendingGuid = Guid.NewGuid();
        var rejectedGuid = Guid.NewGuid();
        var localGuid = Guid.NewGuid();
        var unknownGuid = Guid.NewGuid();
        var sessionGuid = Guid.NewGuid();

        try
        {
            await CreateLegacySettlementDatabaseAsync(databasePath);
            await InsertLegacySettlementAsync(databasePath, pendingGuid, "CloudBackendAsync", "Failed", null, "Pending", 4, null);
            await InsertLegacySettlementAsync(databasePath, rejectedGuid, "CloudBackendAsync", "Failed", null, "Rejected", 4, "PROVIDER_SESSION_REQUIRED");
            await InsertLegacySettlementAsync(databasePath, localGuid, "LocalIp", "Failed", null, "Rejected", 4, "PROVIDER_SESSION_REQUIRED");
            await InsertLegacySettlementAsync(databasePath, unknownGuid, "CloudBackendAsync", "Unknown", null, "Rejected", 4, "PROVIDER_SESSION_REQUIRED");
            await InsertLegacySettlementAsync(databasePath, sessionGuid, "CloudBackendAsync", "Failed", "session-001", "Rejected", 4, "PROVIDER_SESSION_REQUIRED");

            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            await new LocalSchemaService(store).InitializeAsync();

            var pending = await ReadMigrationResultAsync(databasePath, pendingGuid);
            Assert.Equal("NotSubmitted", pending.SubmissionState);
            Assert.Equal("Pending", pending.UploadStatus);
            Assert.Equal(4, pending.PayloadRevision);

            var rejected = await ReadMigrationResultAsync(databasePath, rejectedGuid);
            Assert.Equal("NotSubmitted", rejected.SubmissionState);
            Assert.Equal("Pending", rejected.UploadStatus);
            Assert.Equal(5, rejected.PayloadRevision);
            Assert.Null(rejected.UploadErrorCode);

            var local = await ReadMigrationResultAsync(databasePath, localGuid);
            Assert.Equal("Unknown", local.SubmissionState);
            Assert.Equal("Rejected", local.UploadStatus);
            Assert.Equal(4, local.PayloadRevision);

            var unknown = await ReadMigrationResultAsync(databasePath, unknownGuid);
            Assert.Equal("Unknown", unknown.SubmissionState);
            Assert.Equal("Rejected", unknown.UploadStatus);
            Assert.Equal(4, unknown.PayloadRevision);

            var session = await ReadMigrationResultAsync(databasePath, sessionGuid);
            Assert.Equal("Submitted", session.SubmissionState);
            Assert.Equal("Rejected", session.UploadStatus);
            Assert.Equal(4, session.PayloadRevision);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static LocalLinklySettlementRecord CreatePendingSettlement(Guid settlementGuid, DateTimeOffset requestedAt)
    {
        return new LocalLinklySettlementRecord(
            settlementGuid,
            "S001",
            "POS-01",
            DateTime.Today,
            "LocalIp",
            "Production",
            ProviderSessionId: null,
            LocalLinklySettlementStatus.Pending,
            ResponseCode: null,
            ResponseText: null,
            SettlementData: null,
            ReceiptTexts: [],
            requestedAt,
            CompletedAt: null,
            FirstPrintedAt: null,
            LastPrintedAt: null,
            PrintCount: 0,
            LastPrintError: null);
    }

    private static async Task CreateLegacySettlementDatabaseAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE LinklySettlementRecords (
                SettlementGuid TEXT PRIMARY KEY,
                StoreCode TEXT NOT NULL,
                DeviceCode TEXT NOT NULL,
                BusinessDate TEXT NOT NULL,
                ConnectionMode TEXT NOT NULL,
                Environment TEXT NOT NULL,
                ProviderSessionId TEXT NULL,
                Status TEXT NOT NULL,
                ResponseCode TEXT NULL,
                ResponseText TEXT NULL,
                SettlementData TEXT NULL,
                ReceiptTextsJson TEXT NOT NULL,
                RequestedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                FirstPrintedAt TEXT NULL,
                LastPrintedAt TEXT NULL,
                PrintCount INTEGER NOT NULL DEFAULT 0,
                LastPrintError TEXT NULL,
                UploadStatus TEXT NOT NULL DEFAULT 'Pending',
                PayloadRevision INTEGER NOT NULL DEFAULT 1,
                UploadedRevision INTEGER NOT NULL DEFAULT 0,
                UploadAttemptCount INTEGER NOT NULL DEFAULT 0,
                NextUploadAt TEXT NULL,
                LastUploadAttemptAt TEXT NULL,
                UploadErrorCode TEXT NULL,
                UploadErrorMessage TEXT NULL,
                UploadedAt TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacySettlementAsync(
        string databasePath,
        Guid settlementGuid,
        string connectionMode,
        string status,
        string? providerSessionId,
        string uploadStatus,
        long payloadRevision,
        string? uploadErrorCode)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LinklySettlementRecords
            (SettlementGuid, StoreCode, DeviceCode, BusinessDate, ConnectionMode, Environment, ProviderSessionId,
             Status, ReceiptTextsJson, RequestedAt, UploadStatus, PayloadRevision, UploadErrorCode)
            VALUES
            ($SettlementGuid, $StoreCode, 'POS-01', '2026-08-01', $ConnectionMode, 'Production', $ProviderSessionId,
             $Status, '[]', '2026-08-01T07:00:00.0000000+00:00', $UploadStatus, $PayloadRevision, $UploadErrorCode);
            """;
        command.Parameters.AddWithValue("$SettlementGuid", settlementGuid.ToString("D"));
        command.Parameters.AddWithValue("$StoreCode", $"S-{settlementGuid:N}");
        command.Parameters.AddWithValue("$ConnectionMode", connectionMode);
        command.Parameters.AddWithValue("$ProviderSessionId", (object?)providerSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$Status", status);
        command.Parameters.AddWithValue("$UploadStatus", uploadStatus);
        command.Parameters.AddWithValue("$PayloadRevision", payloadRevision);
        command.Parameters.AddWithValue("$UploadErrorCode", (object?)uploadErrorCode ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(string SubmissionState, string UploadStatus, long PayloadRevision, string? UploadErrorCode)>
        ReadMigrationResultAsync(string databasePath, Guid settlementGuid)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProviderSubmissionState, UploadStatus, PayloadRevision, UploadErrorCode
            FROM LinklySettlementRecords
            WHERE SettlementGuid = $SettlementGuid;
            """;
        command.Parameters.AddWithValue("$SettlementGuid", settlementGuid.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static void DeleteDatabase(string databasePath)
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
