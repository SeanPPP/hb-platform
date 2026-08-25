using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Orders;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class LocalFinancialSupervisorResolutionRepositoryTests
{
    [Fact]
    public async Task Legacy_schema_upgrade_is_repeatable_and_preserves_sale_default()
    {
        var path = CreateTempDatabasePath();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE LocalCardPaymentAttempts (
                        AttemptGuid TEXT PRIMARY KEY,
                        SessionId TEXT NULL,
                        TxnRef TEXT NULL,
                        Processor TEXT NOT NULL,
                        Environment TEXT NOT NULL,
                        ConnectionMode TEXT NOT NULL,
                        TxnType TEXT NOT NULL,
                        Amount TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        OrderDraftJson TEXT NOT NULL,
                        StoreCode TEXT NOT NULL,
                        DeviceCode TEXT NOT NULL,
                        CashierId TEXT NOT NULL,
                        ResponseCode TEXT NULL,
                        ResponseText TEXT NULL,
                        PaymentReference TEXT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL,
                        CompletedAt TEXT NULL,
                        AcknowledgedAt TEXT NULL
                    );

                    INSERT INTO LocalCardPaymentAttempts
                    (
                        AttemptGuid, Processor, Environment, ConnectionMode, TxnType, Amount,
                        Status, OrderDraftJson, StoreCode, DeviceCode, CashierId, CreatedAt, UpdatedAt
                    )
                    VALUES
                    (
                        '10000000-0000-0000-0000-000000000001', 'Linkly', 'Production',
                        'CloudBackendAsync', 'P', '10.00', 'Pending', '{}', 'S001', 'POS-01',
                        'C001', '2026-07-28T00:00:00.0000000+00:00',
                        '2026-07-28T00:00:00.0000000+00:00'
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var store = new LocalSqliteStore(path);
            var schema = new LocalSchemaService(store);
            await schema.InitializeAsync();
            await schema.InitializeAsync();

            await using var upgraded = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadLongAsync(
                upgraded,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='LocalFinancialSupervisorResolutions';"));
            Assert.Equal(1L, await ReadLongAsync(
                upgraded,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_LocalCardPaymentAttempts_ActiveSession';"));
            Assert.Equal("Sale", await ReadStringAsync(
                upgraded,
                "SELECT OperationKind FROM LocalCardPaymentAttempts WHERE AttemptGuid='10000000-0000-0000-0000-000000000001';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Active_session_rows_are_unique_per_terminal_and_environment()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var first = CreateLinklyAttempt(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                "SESSION-001",
                "ActiveSession");
            var second = CreateLinklyAttempt(
                Guid.Parse("20000000-0000-0000-0000-000000000002"),
                "SESSION-001",
                "ActiveSession");

            var persistedFirst = await repository.CreateOrGetActiveSessionAsync(first);
            var persistedSecond = await repository.CreateOrGetActiveSessionAsync(second);

            Assert.Equal(first.AttemptGuid, persistedFirst.AttemptGuid);
            Assert.Equal(first.AttemptGuid, persistedSecond.AttemptGuid);
            await repository.CreateAsync(CreateLinklyAttempt(
                Guid.Parse("20000000-0000-0000-0000-000000000003"),
                "SESSION-001",
                "Sale"));

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadLongAsync(
                connection,
                "SELECT COUNT(*) FROM LocalCardPaymentAttempts WHERE OperationKind='ActiveSession';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Active_session_resolution_uses_status_and_timestamp_cas()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository1 = new LocalCardPaymentAttemptRepository(store);
            var repository2 = new LocalCardPaymentAttemptRepository(store);
            var now = DateTimeOffset.Parse("2026-07-28T00:30:00+00:00");
            var attempt = CreateLinklyAttempt(
                Guid.Parse("21000000-0000-0000-0000-000000000001"),
                "SESSION-CAS-001",
                "ActiveSession") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = now
            };
            await repository1.CreateOrGetActiveSessionAsync(attempt);
            var resolution = new ActiveSessionResolution(
                attempt.AttemptGuid,
                Assert.IsType<string>(attempt.SessionId),
                ActiveSessionSupervisorDecision.ConfirmNotPaid,
                LocalCardPaymentAttemptStatus.Recovering,
                now,
                string.Empty,
                "bank case 123",
                PaymentReference: null,
                now.AddMinutes(1));

            var results = await Task.WhenAll(
                repository1.ResolveActiveSessionWithJournalAsync(
                    resolution,
                    CreateActiveSessionJournal(
                        Guid.Parse("21000000-0000-0000-0000-000000000101"),
                        Guid.Parse("21000000-0000-0000-0000-000000000201"),
                        attempt,
                        now.AddMinutes(1),
                        "manager-1") with
                    {
                        Reason = string.Empty
                    }),
                repository2.ResolveActiveSessionWithJournalAsync(
                    resolution with { ResolvedAt = now.AddMinutes(2) },
                    CreateActiveSessionJournal(
                        Guid.Parse("21000000-0000-0000-0000-000000000102"),
                        Guid.Parse("21000000-0000-0000-0000-000000000202"),
                        attempt,
                        now.AddMinutes(2),
                        "manager-2") with
                    {
                        Reason = string.Empty
                    }));

            Assert.Equal(1, results.Count(result => result));
            var persisted = Assert.IsType<LocalCardPaymentAttempt>(
                await repository1.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, persisted.Status);
            Assert.Equal(CardRecoveryPhases.FinalizePending, persisted.RecoveryPhase);
            Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), persisted.RecoveryTargetStatus);
            Assert.Equal("Evidence: bank case 123", persisted.ResponseText);
            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadLongAsync(
                connection,
                $"SELECT COUNT(*) FROM LocalFinancialSupervisorResolutions WHERE AttemptGuid='{attempt.AttemptGuid:D}';"));
            Assert.Equal(string.Empty, await ReadStringAsync(
                connection,
                $"SELECT Reason FROM LocalFinancialSupervisorResolutions WHERE AttemptGuid='{attempt.AttemptGuid:D}';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Payment_resolution_cas_allows_only_one_supervisor_journal()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository1 = new LocalCardPaymentAttemptRepository(store);
            var repository2 = new LocalCardPaymentAttemptRepository(store);
            var now = DateTimeOffset.Parse("2026-07-28T00:35:00+00:00");
            var attempt = CreateLinklyAttempt(
                Guid.Parse("21200000-0000-0000-0000-000000000001"),
                "SESSION-PAYMENT-CAS-001",
                "Sale") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = now
            };
            await repository1.CreateAsync(attempt);

            var paidResolution = new ActiveSessionResolution(
                attempt.AttemptGuid,
                Assert.IsType<string>(attempt.SessionId),
                ActiveSessionSupervisorDecision.ConfirmPaid,
                LocalCardPaymentAttemptStatus.Recovering,
                now,
                string.Empty,
                "bank confirms charge",
                "PAYMENT-CAS-WINNER",
                now.AddMinutes(1));
            var notPaidResolution = new ActiveSessionResolution(
                attempt.AttemptGuid,
                Assert.IsType<string>(attempt.SessionId),
                ActiveSessionSupervisorDecision.ConfirmNotPaid,
                LocalCardPaymentAttemptStatus.Recovering,
                now,
                string.Empty,
                "bank confirms no charge",
                PaymentReference: null,
                now.AddMinutes(2));
            var paidJournal = CreateActiveSessionJournal(
                Guid.Parse("21200000-0000-0000-0000-000000000101"),
                Guid.Parse("21200000-0000-0000-0000-000000000201"),
                attempt,
                paidResolution.ResolvedAt,
                "manager-paid") with
            {
                Decision = ActiveSessionSupervisorDecision.ConfirmPaid.ToString(),
                Reason = string.Empty,
                Evidence = paidResolution.Evidence,
                FinancialReference = paidResolution.PaymentReference
            };
            var notPaidJournal = CreateActiveSessionJournal(
                Guid.Parse("21200000-0000-0000-0000-000000000102"),
                Guid.Parse("21200000-0000-0000-0000-000000000202"),
                attempt,
                notPaidResolution.ResolvedAt,
                "manager-not-paid") with
            {
                Decision = ActiveSessionSupervisorDecision.ConfirmNotPaid.ToString(),
                Reason = string.Empty,
                Evidence = notPaidResolution.Evidence,
                FinancialReference = null
            };

            var results = await Task.WhenAll(
                repository1.ResolvePaymentWithJournalAsync(paidResolution, paidJournal),
                repository2.ResolvePaymentWithJournalAsync(notPaidResolution, notPaidJournal));

            Assert.Equal(1, results.Count(result => result));
            var paidWon = results[0];
            var persisted = Assert.IsType<LocalCardPaymentAttempt>(
                await repository1.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(
                paidWon
                    ? ActiveSessionSupervisorResolutionCodes.ConfirmedPaid
                    : ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
                persisted.ResponseCode);
            Assert.Equal(paidWon ? "PAYMENT-CAS-WINNER" : null, persisted.PaymentReference);

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadLongAsync(
                connection,
                $"SELECT COUNT(*) FROM LocalFinancialSupervisorResolutions WHERE AttemptGuid='{attempt.AttemptGuid:D}';"));
            Assert.Equal(
                paidWon
                    ? ActiveSessionSupervisorDecision.ConfirmPaid.ToString()
                    : ActiveSessionSupervisorDecision.ConfirmNotPaid.ToString(),
                await ReadStringAsync(
                    connection,
                    $"SELECT Decision FROM LocalFinancialSupervisorResolutions WHERE AttemptGuid='{attempt.AttemptGuid:D}';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Theory]
    [InlineData(LocalFinancialSupervisorResolutionTarget.CardRefund)]
    [InlineData(LocalFinancialSupervisorResolutionTarget.InstallmentRefund)]
    public async Task Refund_journal_still_rejects_empty_reason(
        LocalFinancialSupervisorResolutionTarget target)
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            await using var connection = await store.OpenConnectionAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            var attemptGuid = Guid.NewGuid();
            var operationGuid = Guid.NewGuid();
            var refundStepGuid = Guid.NewGuid();
            var resolution = new LocalFinancialSupervisorResolution(
                Guid.NewGuid(),
                target,
                "Linkly",
                "Production",
                "S001",
                "POS-01",
                target == LocalFinancialSupervisorResolutionTarget.CardRefund ? attemptGuid : null,
                target == LocalFinancialSupervisorResolutionTarget.InstallmentRefund ? refundStepGuid : null,
                target == LocalFinancialSupervisorResolutionTarget.InstallmentRefund ? operationGuid : null,
                SessionId: null,
                Decision: "ConfirmedRefunded",
                OperatorCashierId: "manager-1",
                OperatorUserGuid: null,
                OperatorName: "Manager",
                Reason: string.Empty,
                Evidence: "bank evidence",
                FinancialReference: "REFUND-001",
                RetryReference: null,
                ResolvedAt: DateTimeOffset.Parse("2026-07-28T00:30:00+00:00"),
                AuditEventId: Guid.NewGuid(),
                AuditPayloadJson: """{"decision":"ConfirmedRefunded"}""");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                LocalFinancialSupervisorResolutionRepository.InsertAsync(
                    connection,
                    transaction,
                    resolution,
                    CancellationToken.None));
            await transaction.RollbackAsync();
            Assert.Equal(0L, await ReadLongAsync(
                connection,
                "SELECT COUNT(*) FROM LocalFinancialSupervisorResolutions;"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Sale_supervisor_not_paid_resolution_remains_recoverable_until_acknowledged()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var now = DateTimeOffset.Parse("2026-07-28T00:40:00+00:00");
            var attempt = CreateLinklyAttempt(
                Guid.Parse("21500000-0000-0000-0000-000000000001"),
                "SESSION-SALE-001",
                "Sale") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = now
            };
            await repository.CreateAsync(attempt);
            var resolution = new ActiveSessionResolution(
                attempt.AttemptGuid,
                Assert.IsType<string>(attempt.SessionId),
                ActiveSessionSupervisorDecision.ConfirmNotPaid,
                LocalCardPaymentAttemptStatus.Recovering,
                now,
                "bank confirmed no charge",
                "bank case sale-123",
                PaymentReference: null,
                now.AddMinutes(1));

            Assert.True(await repository.ResolvePaymentWithJournalAsync(
                resolution,
                CreateActiveSessionJournal(
                    Guid.Parse("21500000-0000-0000-0000-000000000101"),
                    Guid.Parse("21500000-0000-0000-0000-000000000201"),
                    attempt,
                    now.AddMinutes(1),
                    "manager-sale")));

            var recoverable = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetLatestOpenAttemptAsync(
                    attempt.StoreCode,
                    attempt.DeviceCode,
                    cashierId: null,
                    attempt.Environment));
            Assert.Equal(attempt.AttemptGuid, recoverable.AttemptGuid);
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, recoverable.Status);
            Assert.Equal(CardRecoveryPhases.FinalizePending, recoverable.RecoveryPhase);
            Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), recoverable.RecoveryTargetStatus);
            Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, recoverable.ResponseCode);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Terminal_payment_resolution_returns_cas_loss_without_writing_journal()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var now = DateTimeOffset.Parse("2026-07-28T00:41:00+00:00");
            var attempt = CreateLinklyAttempt(
                Guid.Parse("21550000-0000-0000-0000-000000000001"),
                "SESSION-TERMINAL-001",
                "Sale") with
            {
                Status = LocalCardPaymentAttemptStatus.Declined,
                UpdatedAt = now,
                CompletedAt = now
            };
            await repository.CreateAsync(attempt);
            var resolution = new ActiveSessionResolution(
                attempt.AttemptGuid,
                Assert.IsType<string>(attempt.SessionId),
                ActiveSessionSupervisorDecision.ConfirmNotPaid,
                LocalCardPaymentAttemptStatus.Declined,
                now,
                string.Empty,
                "bank confirms no charge",
                PaymentReference: null,
                now.AddMinutes(1));

            var applied = await repository.ResolvePaymentWithJournalAsync(
                resolution,
                CreateActiveSessionJournal(
                    Guid.Parse("21550000-0000-0000-0000-000000000101"),
                    Guid.Parse("21550000-0000-0000-0000-000000000201"),
                    attempt,
                    now.AddMinutes(1),
                    "manager-terminal"));

            Assert.False(applied);
            Assert.Equal(
                LocalCardPaymentAttemptStatus.Declined,
                (await repository.GetAttemptAsync(attempt.AttemptGuid))?.Status);
            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(0L, await ReadLongAsync(
                connection,
                $"SELECT COUNT(*) FROM LocalFinancialSupervisorResolutions WHERE AttemptGuid='{attempt.AttemptGuid:D}';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Stale_recovery_writes_cannot_overwrite_supervisor_payment_resolution()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var now = DateTimeOffset.Parse("2026-07-28T00:42:00+00:00");
            var attempt = CreateLinklyAttempt(
                Guid.Parse("21600000-0000-0000-0000-000000000001"),
                "SESSION-RACE-001",
                "Sale") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = now
            };
            await repository.CreateAsync(attempt);
            var resolvedAt = now.AddMinutes(1);
            Assert.True(await repository.ResolvePaymentWithJournalAsync(
                new ActiveSessionResolution(
                    attempt.AttemptGuid,
                    Assert.IsType<string>(attempt.SessionId),
                    ActiveSessionSupervisorDecision.ConfirmNotPaid,
                    LocalCardPaymentAttemptStatus.Recovering,
                    now,
                    "bank confirmed no charge",
                    "bank case race-123",
                    PaymentReference: null,
                    resolvedAt),
                CreateActiveSessionJournal(
                    Guid.Parse("21600000-0000-0000-0000-000000000101"),
                    Guid.Parse("21600000-0000-0000-0000-000000000201"),
                    attempt,
                    resolvedAt,
                    "manager-race")));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpdateOutcomeAsync(
                    attempt.AttemptGuid,
                    LocalCardPaymentAttemptStatus.Approved,
                    "00",
                    "late terminal approval",
                    "PAYMENT-LATE",
                    resolvedAt.AddSeconds(1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.MarkRecoveringAsync(
                    attempt.AttemptGuid,
                    resolvedAt.AddSeconds(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpdateSessionAsync(
                    attempt.AttemptGuid,
                    "SESSION-RACE-LATE",
                    "TXN-RACE-LATE",
                    resolvedAt.AddSeconds(3)));

            var persisted = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, persisted.Status);
            Assert.Equal(CardRecoveryPhases.FinalizePending, persisted.RecoveryPhase);
            Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), persisted.RecoveryTargetStatus);
            Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, persisted.ResponseCode);
            Assert.Null(persisted.PaymentReference);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Active_session_journal_failure_rolls_back_status_change()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var now = DateTimeOffset.Parse("2026-07-28T00:45:00+00:00");
            var first = CreateLinklyAttempt(
                Guid.Parse("22000000-0000-0000-0000-000000000001"),
                "SESSION-ROLLBACK-001",
                "ActiveSession") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = now
            };
            var second = CreateLinklyAttempt(
                Guid.Parse("22000000-0000-0000-0000-000000000002"),
                "SESSION-ROLLBACK-002",
                "ActiveSession") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = now
            };
            await repository.CreateOrGetActiveSessionAsync(first);
            await repository.CreateOrGetActiveSessionAsync(second);
            var duplicateAuditEventId = Guid.Parse("22000000-0000-0000-0000-000000000099");

            Assert.True(await repository.ResolveActiveSessionWithJournalAsync(
                CreateActiveSessionResolution(first, now),
                CreateActiveSessionJournal(
                    Guid.NewGuid(),
                    duplicateAuditEventId,
                    first,
                    now.AddMinutes(1),
                    "manager-1")));

            await Assert.ThrowsAsync<SqliteException>(() =>
                repository.ResolveActiveSessionWithJournalAsync(
                    CreateActiveSessionResolution(second, now),
                    CreateActiveSessionJournal(
                        Guid.NewGuid(),
                        duplicateAuditEventId,
                        second,
                        now.AddMinutes(1),
                        "manager-2")));

            var persistedSecond = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(second.AttemptGuid));
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, persistedSecond.Status);
            Assert.Equal(now, persistedSecond.UpdatedAt);
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Linkly_resolution_cas_and_journal_insert_are_atomic_for_two_supervisors()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository1 = new LocalCardPaymentAttemptRepository(store);
            var repository2 = new LocalCardPaymentAttemptRepository(store);
            var attempt = CreateLinklyAttempt(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                sessionId: null,
                operationKind: "Refund") with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                OperationGuid = Guid.Parse("30000000-0000-0000-0000-000000000010")
            };
            await repository1.CreateAsync(attempt);
            var now = DateTimeOffset.Parse("2026-07-28T01:00:00+00:00");

            var results = await Task.WhenAll(
                repository1.ResolveRefundWithJournalAsync(
                    CreateCardResolution(attempt.AttemptGuid, now),
                    CreateJournal(
                        Guid.Parse("30000000-0000-0000-0000-000000000101"),
                        Guid.Parse("30000000-0000-0000-0000-000000000201"),
                        attempt,
                        now,
                        "manager-1")),
                repository2.ResolveRefundWithJournalAsync(
                    CreateCardResolution(attempt.AttemptGuid, now.AddTicks(1)),
                    CreateJournal(
                        Guid.Parse("30000000-0000-0000-0000-000000000102"),
                        Guid.Parse("30000000-0000-0000-0000-000000000202"),
                        attempt,
                        now.AddTicks(1),
                        "manager-2")));

            Assert.Equal(1, results.Count(result => result));
            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadLongAsync(
                connection,
                $"SELECT COUNT(*) FROM LocalFinancialSupervisorResolutions WHERE AttemptGuid='{attempt.AttemptGuid:D}';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Journal_constraint_failure_rolls_back_the_attempt_state_change()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var first = CreateLinklyAttempt(
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                sessionId: null,
                operationKind: "Refund") with { Status = LocalCardPaymentAttemptStatus.Recovering };
            var second = CreateLinklyAttempt(
                Guid.Parse("40000000-0000-0000-0000-000000000002"),
                sessionId: null,
                operationKind: "Refund") with { Status = LocalCardPaymentAttemptStatus.Recovering };
            await repository.CreateAsync(first);
            await repository.CreateAsync(second);
            var duplicateAuditEventId = Guid.Parse("40000000-0000-0000-0000-000000000099");
            var now = DateTimeOffset.Parse("2026-07-28T02:00:00+00:00");

            Assert.True(await repository.ResolveRefundWithJournalAsync(
                CreateCardResolution(first.AttemptGuid, now),
                CreateJournal(Guid.NewGuid(), duplicateAuditEventId, first, now, "manager-1")));

            await Assert.ThrowsAsync<SqliteException>(() => repository.ResolveRefundWithJournalAsync(
                CreateCardResolution(second.AttemptGuid, now.AddMinutes(1)),
                CreateJournal(Guid.NewGuid(), duplicateAuditEventId, second, now.AddMinutes(1), "manager-2")));

            var savedSecond = Assert.IsType<LocalCardPaymentAttempt>(await repository.GetAttemptAsync(second.AttemptGuid));
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, savedSecond.Status);
            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadLongAsync(
                connection,
                $"SELECT COUNT(*) FROM LocalFinancialSupervisorResolutions WHERE AuditEventId='{duplicateAuditEventId:D}';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Square_resolution_and_journal_are_committed_together()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSquareRefundAttempt();
            await repository.CreateAsync(attempt);
            var now = DateTimeOffset.Parse("2026-07-28T02:30:00+00:00");
            var auditEventId = Guid.Parse("45000000-0000-0000-0000-000000000099");
            var journal = CreateJournal(
                Guid.Parse("45000000-0000-0000-0000-000000000098"),
                auditEventId,
                attempt,
                now,
                "manager-square");

            Assert.True(await repository.ResolveRefundWithJournalAsync(
                CreateCardResolution(attempt.AttemptGuid, now),
                journal));

            var saved = Assert.IsType<LocalSquarePaymentAttempt>(await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, saved.Status);
            Assert.Equal(CardRecoveryPhases.FinalizePending, saved.RecoveryPhase);
            Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, saved.RecoveryTargetStatus);
            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal("Square", await ReadStringAsync(
                connection,
                $"SELECT Processor FROM LocalFinancialSupervisorResolutions WHERE AuditEventId='{auditEventId:D}';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Installment_refund_step_and_journal_are_committed_together()
    {
        var path = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalInstallmentOperationRepository(store);
            var now = DateTimeOffset.Parse("2026-07-28T02:45:00+00:00");
            var operation = new LocalInstallmentOperation(
                Guid.Parse("46000000-0000-0000-0000-000000000001"),
                LocalInstallmentOperationKind.Cancel,
                Guid.Parse("46000000-0000-0000-0000-000000000002"),
                PaymentGuid: null,
                "S001",
                "POS-01",
                "C001",
                "cancel-key",
                "{}",
                LocalInstallmentOperationState.ResultUnknown,
                TerminalAttemptGuid: null,
                TerminalProcessor: null,
                ResponseJson: null,
                FailureMessage: null,
                now,
                now);
            var step = new LocalInstallmentRefundStep(
                Guid.Parse("46000000-0000-0000-0000-000000000003"),
                operation.OperationGuid,
                Guid.Parse("46000000-0000-0000-0000-000000000004"),
                PaymentMethodKind.Card,
                10m,
                "TXN-ORIGINAL",
                "refund-key",
                LocalInstallmentRefundStepState.ResultUnknown,
                RefundReference: null,
                CardTransactionsJson: null,
                FailureMessage: null,
                SupervisorDecision: null,
                SupervisorUserId: null,
                SupervisorReason: null,
                SupervisorEvidence: null,
                ResolvedAt: null,
                now,
                now);
            await repository.CreateCancelOrGetAsync(operation, [step]);
            var resolution = new InstallmentRefundSupervisorResolution(
                InstallmentRefundSupervisorDecision.ConfirmNotRefunded,
                "manager-installment",
                "bank confirmed no refund",
                "case-460");
            var journal = new LocalFinancialSupervisorResolution(
                Guid.Parse("46000000-0000-0000-0000-000000000005"),
                LocalFinancialSupervisorResolutionTarget.InstallmentRefund,
                "Linkly",
                "Production",
                "S001",
                "POS-01",
                AttemptGuid: null,
                step.RefundStepGuid,
                operation.OperationGuid,
                SessionId: null,
                resolution.Decision.ToString(),
                resolution.OperatorId,
                OperatorUserGuid: null,
                OperatorName: "Manager",
                resolution.Reason,
                resolution.Evidence,
                FinancialReference: null,
                RetryReference: null,
                now,
                Guid.Parse("46000000-0000-0000-0000-000000000006"),
                """{"eventId":"46000000-0000-0000-0000-000000000006","storeCode":"S001","deviceCode":"POS-01"}""");

            Assert.True(await repository.ResolveRefundStepWithJournalAsync(
                step.RefundStepGuid,
                resolution,
                journal,
                now));

            var savedStep = Assert.Single(await repository.GetRefundStepsAsync(operation.OperationGuid));
            Assert.Equal(LocalInstallmentRefundStepState.Prepared, savedStep.State);
            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(1L, await ReadLongAsync(
                connection,
                $"SELECT COUNT(*) FROM LocalFinancialSupervisorResolutions WHERE RefundStepGuid='{step.RefundStepGuid:D}';"));
        }
        finally
        {
            DeleteTempDatabase(path);
        }
    }

    [Fact]
    public async Task Failed_outbox_write_is_replayed_idempotently_from_the_journal()
    {
        var path = CreateTempDatabasePath();
        var root = Path.Combine(Path.GetTempPath(), $"hbpos-financial-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var invalidOutboxPath = Path.Combine(root, "outbox-directory");
        Directory.CreateDirectory(invalidOutboxPath);
        var validOutboxPath = Path.Combine(root, "audit.db");
        try
        {
            var store = new LocalSqliteStore(path);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalCardPaymentAttemptRepository(store);
            var attempt = CreateLinklyAttempt(
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                sessionId: null,
                operationKind: "Refund") with { Status = LocalCardPaymentAttemptStatus.Recovering };
            await repository.CreateAsync(attempt);
            var now = DateTimeOffset.Parse("2026-07-28T03:00:00+00:00");
            var journal = CreateJournal(
                Guid.Parse("50000000-0000-0000-0000-000000000011"),
                Guid.Parse("50000000-0000-0000-0000-000000000012"),
                attempt,
                now,
                "manager-1");
            Assert.True(await repository.ResolveRefundWithJournalAsync(
                CreateCardResolution(attempt.AttemptGuid, now),
                journal));

            var journalRepository = new LocalFinancialSupervisorResolutionRepository(store);
            var failedReplay = new FinancialSupervisorAuditReplayService(
                journalRepository,
                new ClientLogOutboxStore(invalidOutboxPath));
            Assert.False(await failedReplay.PersistAfterCommitAsync(journal));
            Assert.Single(await journalRepository.GetPendingAuditAsync(10));

            var outbox = new ClientLogOutboxStore(validOutboxPath);
            var replay = new FinancialSupervisorAuditReplayService(journalRepository, outbox);
            var hostedReplay = new FinancialSupervisorAuditReplayHostedService(
                new LocalSchemaService(store),
                replay);
            await hostedReplay.StartAsync(CancellationToken.None);
            await replay.ReplayPendingAsync();

            Assert.Empty(await journalRepository.GetPendingAuditAsync(10));
            Assert.Equal(1, await outbox.CountPendingAsync(
                ClientLogOutboxKind.OperationAudit,
                CancellationToken.None));
            var pending = Assert.Single(await outbox.ReadPendingAsync(
                ClientLogOutboxKind.OperationAudit,
                now.AddDays(1),
                10,
                CancellationToken.None));
            Assert.Equal(journal.AuditEventId, pending.EventId);
        }
        finally
        {
            DeleteTempDatabase(path);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static CardRefundAttemptResolution CreateCardResolution(Guid attemptGuid, DateTimeOffset resolvedAt) => new(
        attemptGuid,
        CardRefundSupervisorDecision.ConfirmRefunded,
        "bank confirmed refund",
        Evidence: null,
        RefundReference: "REFUND-001",
        RetryTxnRef: null,
        resolvedAt);

    private static ActiveSessionResolution CreateActiveSessionResolution(
        LocalCardPaymentAttempt attempt,
        DateTimeOffset expectedUpdatedAt) => new(
        attempt.AttemptGuid,
        Assert.IsType<string>(attempt.SessionId),
        ActiveSessionSupervisorDecision.ConfirmNotPaid,
        LocalCardPaymentAttemptStatus.Recovering,
        expectedUpdatedAt,
        "bank confirmed no charge",
        "bank case 123",
        PaymentReference: null,
        expectedUpdatedAt.AddMinutes(1));

    private static LocalFinancialSupervisorResolution CreateJournal(
        Guid resolutionGuid,
        Guid auditEventId,
        LocalCardPaymentAttempt attempt,
        DateTimeOffset resolvedAt,
        string operatorCashierId) => new(
        resolutionGuid,
        LocalFinancialSupervisorResolutionTarget.CardRefund,
        "Linkly",
        attempt.Environment,
        attempt.StoreCode,
        attempt.DeviceCode,
        attempt.AttemptGuid,
        RefundStepGuid: null,
        attempt.OperationGuid,
        attempt.SessionId,
        CardRefundSupervisorDecision.ConfirmRefunded.ToString(),
        operatorCashierId,
        OperatorUserGuid: null,
        OperatorName: operatorCashierId,
        "bank confirmed refund",
        Evidence: null,
        FinancialReference: "REFUND-001",
        RetryReference: null,
        resolvedAt,
        auditEventId,
        $$"""{"eventId":"{{auditEventId:D}}","storeCode":"{{attempt.StoreCode}}","deviceCode":"{{attempt.DeviceCode}}"}""");

    private static LocalFinancialSupervisorResolution CreateActiveSessionJournal(
        Guid resolutionGuid,
        Guid auditEventId,
        LocalCardPaymentAttempt attempt,
        DateTimeOffset resolvedAt,
        string operatorCashierId) => new(
        resolutionGuid,
        LocalFinancialSupervisorResolutionTarget.ActiveSession,
        "Linkly",
        attempt.Environment,
        attempt.StoreCode,
        attempt.DeviceCode,
        attempt.AttemptGuid,
        RefundStepGuid: null,
        OperationGuid: null,
        attempt.SessionId,
        ActiveSessionSupervisorDecision.ConfirmNotPaid.ToString(),
        operatorCashierId,
        OperatorUserGuid: null,
        OperatorName: operatorCashierId,
        "bank confirmed no charge",
        "bank case 123",
        FinancialReference: null,
        RetryReference: null,
        resolvedAt,
        auditEventId,
        $$"""{"eventId":"{{auditEventId:D}}","sessionId":"{{attempt.SessionId}}"}""");

    private static LocalFinancialSupervisorResolution CreateJournal(
        Guid resolutionGuid,
        Guid auditEventId,
        LocalSquarePaymentAttempt attempt,
        DateTimeOffset resolvedAt,
        string operatorCashierId) => new(
        resolutionGuid,
        LocalFinancialSupervisorResolutionTarget.CardRefund,
        "Square",
        attempt.Environment,
        attempt.StoreCode,
        attempt.DeviceCode,
        attempt.AttemptGuid,
        RefundStepGuid: null,
        attempt.OperationGuid,
        SessionId: null,
        CardRefundSupervisorDecision.ConfirmRefunded.ToString(),
        operatorCashierId,
        OperatorUserGuid: null,
        OperatorName: operatorCashierId,
        "bank confirmed refund",
        Evidence: null,
        FinancialReference: "REFUND-SQUARE-001",
        RetryReference: attempt.IdempotencyKey,
        resolvedAt,
        auditEventId,
        $$"""{"eventId":"{{auditEventId:D}}","storeCode":"{{attempt.StoreCode}}","deviceCode":"{{attempt.DeviceCode}}"}""");

    private static LocalCardPaymentAttempt CreateLinklyAttempt(
        Guid attemptGuid,
        string? sessionId,
        string operationKind)
    {
        var now = DateTimeOffset.Parse("2026-07-28T00:00:00+00:00");
        return new LocalCardPaymentAttempt(
            attemptGuid,
            sessionId,
            TxnRef: $"TXN-{attemptGuid:N}",
            Processor: "Linkly",
            Environment: "Production",
            ConnectionMode: "CloudBackendAsync",
            TxnType: "P",
            Amount: 10m,
            Status: LocalCardPaymentAttemptStatus.Pending,
            OrderDraftJson: "{}",
            StoreCode: "S001",
            DeviceCode: "POS-01",
            CashierId: "C001",
            ResponseCode: null,
            ResponseText: null,
            PaymentReference: null,
            CreatedAt: now,
            UpdatedAt: now,
            CompletedAt: null,
            AcknowledgedAt: null,
            OperationKind: operationKind);
    }

    private static LocalSquarePaymentAttempt CreateSquareRefundAttempt()
    {
        var now = DateTimeOffset.Parse("2026-07-28T00:00:00+00:00");
        return new LocalSquarePaymentAttempt(
            Guid.Parse("45000000-0000-0000-0000-000000000001"),
            CheckoutId: "CHECKOUT-REFUND-001",
            IdempotencyKey: "square-refund-key",
            DeviceId: "SQ-DEVICE",
            LocationId: "SQ-LOCATION",
            Environment: "Production",
            Amount: 10m,
            AmountCents: 1000,
            Currency: "AUD",
            Status: LocalSquarePaymentAttemptStatus.Recovering,
            CheckoutStatus: "IN_PROGRESS",
            CancelReason: null,
            OrderDraftJson: "{}",
            StoreCode: "S001",
            DeviceCode: "POS-01",
            CashierId: "C001",
            PaymentId: null,
            PaymentStatus: null,
            ResponseCode: null,
            ResponseText: null,
            CreatedAt: now,
            UpdatedAt: now,
            CompletedAt: null,
            OrderCompletedAt: null,
            ResolvedAt: null,
            OperationKind: "Refund",
            OperationGuid: Guid.Parse("45000000-0000-0000-0000-000000000002"),
            SubmissionToken: "submission-token",
            RefundBusinessKey: "refund-business-key");
    }

    private static async Task<long> ReadLongAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static string CreateTempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"hbpos-financial-supervisor-{Guid.NewGuid():N}.db");

    private static void DeleteTempDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
