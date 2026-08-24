using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class SquareRecoveryCasRepositoryTests
{
    [Fact]
    public async Task Supervisor_paid_winner_persists_financial_reference_without_polluting_provider_evidence()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSaleAttempt();
            await repository.CreateAsync(attempt);

            var applied = await repository.ResolvePaymentWithJournalAsync(
                CreatePaymentResolution(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    paymentReference: "BANK-TERMINAL-001"),
                CreateJournal(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    financialReference: "BANK-TERMINAL-001"));

            Assert.True(applied);
            await using var connection = await store.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Status, PaymentId, PaymentStatus, SupervisorFinancialReference, ResponseCode
                FROM LocalSquarePaymentAttempts
                WHERE AttemptGuid = $AttemptGuid;
                """;
            command.Parameters.AddWithValue("$AttemptGuid", attempt.AttemptGuid.ToString());
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering.ToString(), reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.Equal("BANK-TERMINAL-001", reader.GetString(3));
            Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedPaid, reader.GetString(4));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Provider_verified_winner_rejects_supervisor_resolution_and_keeps_one_durable_winner()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSaleAttempt();
            await repository.CreateAsync(attempt);
            var providerVerifiedAt = attempt.UpdatedAt.AddMinutes(1);
            await repository.MarkPaymentVerifiedAsync(
                attempt.AttemptGuid,
                "SQ-PAYMENT-REAL-001",
                "COMPLETED",
                responseCode: null,
                responseText: "Provider verified.",
                providerVerifiedAt);

            var supervisorApplied = await repository.ResolvePaymentWithJournalAsync(
                CreatePaymentResolution(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    paymentReference: "BANK-TERMINAL-LOSER"),
                CreateJournal(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    financialReference: "BANK-TERMINAL-LOSER"));

            Assert.False(supervisorApplied);
            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.PaymentVerified, saved.Status);
            Assert.Equal("SQ-PAYMENT-REAL-001", saved.PaymentId);
            Assert.Equal("COMPLETED", saved.PaymentStatus);
            Assert.Equal(0, await CountSupervisorJournalsAsync(store, attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Supervisor_paid_winner_rejects_late_provider_verified_write()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSaleAttempt();
            await repository.CreateAsync(attempt);
            Assert.True(await repository.ResolvePaymentWithJournalAsync(
                CreatePaymentResolution(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    paymentReference: "BANK-WINNER"),
                CreateJournal(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    financialReference: "BANK-WINNER")));

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.MarkPaymentVerifiedAsync(
                attempt.AttemptGuid,
                "SQ-LATE-PAYMENT",
                "COMPLETED",
                responseCode: null,
                responseText: "Late provider callback.",
                attempt.UpdatedAt.AddMinutes(2)));

            await using var connection = await store.OpenConnectionAsync();
            Assert.Equal(
                "BANK-WINNER",
                await ReadScalarStringAsync(
                    connection,
                    "SELECT SupervisorFinancialReference FROM LocalSquarePaymentAttempts LIMIT 1;"));
            Assert.Equal(
                0,
                await ReadScalarIntAsync(
                    connection,
                    "SELECT COUNT(*) FROM LocalSquarePaymentAttempts WHERE PaymentId IS NOT NULL OR PaymentStatus IS NOT NULL;"));
            Assert.Equal(1, await CountSupervisorJournalsAsync(store, attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Supervisor_not_paid_winner_rejects_late_provider_verified_write_and_keeps_journal_consistent()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSaleAttempt();
            await repository.CreateAsync(attempt);
            Assert.True(await repository.ResolvePaymentWithJournalAsync(
                CreatePaymentResolution(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmNotProcessed,
                    paymentReference: null),
                CreateJournal(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmNotProcessed,
                    financialReference: null)));

            var providerApplied = await repository.TryPersistPaymentVerifiedRecoveryAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                "SQ-LATE-PAYMENT",
                "COMPLETED",
                responseCode: null,
                responseText: "Late provider callback.",
                attempt.UpdatedAt.AddMinutes(2));

            Assert.False(providerApplied);
            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, saved.ResponseCode);
            Assert.Null(saved.PaymentId);
            Assert.Null(saved.PaymentStatus);
            Assert.Equal(CardRecoveryPhases.FinalizePending, saved.RecoveryPhase);
            Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, saved.RecoveryTargetStatus);
            Assert.Equal(1, await CountSupervisorJournalsAsync(store, attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Automatic_writes_reject_terminal_and_finalize_pending_attempts()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var terminal = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.Parse("a1000000-0000-0000-0000-000000000002"),
                Status = LocalSquarePaymentAttemptStatus.Canceled
            };
            var finalizePending = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.Parse("a1000000-0000-0000-0000-000000000003")
            };
            await repository.CreateAsync(terminal);
            await repository.CreateAsync(finalizePending);
            await using (var connection = await store.OpenConnectionAsync())
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    UPDATE LocalSquarePaymentAttempts
                    SET RecoveryPhase = $RecoveryPhase,
                        RecoveryTargetStatus = $RecoveryTargetStatus
                    WHERE AttemptGuid = $AttemptGuid;
                    """;
                command.Parameters.AddWithValue("$RecoveryPhase", CardRecoveryPhases.FinalizePending);
                command.Parameters.AddWithValue("$RecoveryTargetStatus", LocalSquarePaymentAttemptStatus.OrderCompleted.ToString());
                command.Parameters.AddWithValue("$AttemptGuid", finalizePending.AttemptGuid.ToString());
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateCheckoutStatusAsync(
                terminal.AttemptGuid,
                LocalSquarePaymentAttemptStatus.Recovering,
                "PENDING",
                null,
                terminal.UpdatedAt.AddMinutes(1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.MarkRecoveringAsync(
                finalizePending.AttemptGuid,
                finalizePending.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryMarkRecoveringAsync(
                terminal.AttemptGuid,
                terminal.Status,
                terminal.UpdatedAt,
                terminal.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryMarkRecoveringAsync(
                finalizePending.AttemptGuid,
                finalizePending.Status,
                finalizePending.UpdatedAt,
                finalizePending.UpdatedAt.AddMinutes(1)));

            Assert.Equal(
                LocalSquarePaymentAttemptStatus.Canceled,
                (await repository.GetAttemptAsync(terminal.AttemptGuid))?.Status);
            Assert.Equal(
                LocalSquarePaymentAttemptStatus.Recovering,
                (await repository.GetAttemptAsync(finalizePending.AttemptGuid))?.Status);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Try_mark_recovering_uses_status_and_timestamp_cas_and_returns_false_for_stale_writer()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.Parse("a1000000-0000-0000-0000-000000000004"),
                Status = LocalSquarePaymentAttemptStatus.CheckoutCreated
            };
            await repository.CreateAsync(attempt);

            var refreshedAt = attempt.UpdatedAt.AddMinutes(1);
            Assert.True(await repository.TryUpdateCheckoutStatusAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.Status,
                checkoutStatus: null,
                cancelReason: null,
                refreshedAt));
            Assert.False(await repository.TryMarkRecoveringAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                refreshedAt.AddMinutes(1)));

            var recoveringAt = refreshedAt.AddMinutes(1);
            Assert.True(await repository.TryMarkRecoveringAsync(
                attempt.AttemptGuid,
                attempt.Status,
                refreshedAt,
                recoveringAt));

            var persisted = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, persisted.Status);
            Assert.Equal(recoveringAt, persisted.UpdatedAt);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData(LocalSquarePaymentAttemptStatus.Canceled)]
    [InlineData(LocalSquarePaymentAttemptStatus.PaymentVerified)]
    public async Task Supervisor_sale_resolution_rejects_terminal_or_provider_verified_status(
        LocalSquarePaymentAttemptStatus status)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalSquarePaymentAttemptRepository(store);
            var attempt = CreateSaleAttempt() with
            {
                Status = status,
                PaymentId = status == LocalSquarePaymentAttemptStatus.PaymentVerified ? "SQ-REAL" : null,
                PaymentStatus = status == LocalSquarePaymentAttemptStatus.PaymentVerified ? "COMPLETED" : null
            };
            await repository.CreateAsync(attempt);

            var applied = await repository.ResolvePaymentWithJournalAsync(
                CreatePaymentResolution(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    paymentReference: "BANK-MUST-NOT-APPLY"),
                CreateJournal(
                    attempt,
                    CardRecoverySupervisorDecision.ConfirmProcessed,
                    financialReference: "BANK-MUST-NOT-APPLY"));

            Assert.False(applied);
            Assert.Equal(0, await CountSupervisorJournalsAsync(store, attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Provider_verified_recovery_persists_financial_result_and_finalize_phase_atomically()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt();
            await repository.CreateAsync(attempt);
            var verifiedAt = attempt.UpdatedAt.AddMinutes(1);

            var applied = await repository.TryPersistPaymentVerifiedRecoveryAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                "SQ-PAYMENT-REAL-002",
                "COMPLETED",
                responseCode: null,
                responseText: "Provider verified during recovery.",
                verifiedAt);

            Assert.True(applied);
            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.PaymentVerified, saved.Status);
            Assert.Equal("SQ-PAYMENT-REAL-002", saved.PaymentId);
            Assert.Equal("COMPLETED", saved.PaymentStatus);
            Assert.Equal(CardRecoveryPhases.FinalizePending, saved.RecoveryPhase);
            Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, saved.RecoveryTargetStatus);
            Assert.Equal(verifiedAt, saved.UpdatedAt);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Provider_verified_refund_persists_financial_result_and_finalize_phase_atomically()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.Parse("a1000000-0000-0000-0000-000000000004"),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                SubmissionToken = "refund-token"
            };
            await repository.CreateAsync(attempt);
            var verifiedAt = attempt.UpdatedAt.AddMinutes(1);

            var applied = await repository.TryMarkRefundPaymentVerifiedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                "SQ-REFUND-REAL-001",
                "COMPLETED",
                responseCode: null,
                responseText: "Refund verified during recovery.",
                verifiedAt);

            Assert.True(applied);
            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.PaymentVerified, saved.Status);
            Assert.Equal("SQ-REFUND-REAL-001", saved.PaymentId);
            Assert.Equal(CardRecoveryPhases.FinalizePending, saved.RecoveryPhase);
            Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, saved.RecoveryTargetStatus);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData("FAILED")]
    [InlineData("REJECTED")]
    public async Task Refund_failure_persists_financial_status_and_finalize_phase_atomically_without_replacing_refund_identity(
        string paymentStatus)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                PaymentId = "REFUND-AUTHORITATIVE-001",
                PaymentStatus = "PENDING",
                ResponseCode = string.Empty,
                ResponseText = string.Empty,
                SubmissionToken = "refund-token"
            };
            await repository.CreateAsync(attempt);
            var finalizedAt = attempt.UpdatedAt.AddMinutes(1);

            var applied = await repository.TryPersistRefundFailureForFinalizationAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                paymentStatus,
                responseCode: "SQUARE_FAILURE",
                responseText: "Square refund reached a terminal failure.",
                finalizedAt);

            Assert.True(applied);
            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, saved.Status);
            Assert.Equal(paymentStatus, saved.PaymentStatus);
            Assert.Equal(CardRecoveryPhases.FinalizePending, saved.RecoveryPhase);
            Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, saved.RecoveryTargetStatus);
            Assert.Equal(attempt.PaymentId, saved.PaymentId);
            Assert.Equal("SQUARE_FAILURE", saved.ResponseCode);
            Assert.Equal("Square refund reached a terminal failure.", saved.ResponseText);
            Assert.Equal(finalizedAt, saved.UpdatedAt);
            Assert.Null(saved.CompletedAt);
            Assert.Null(saved.ResolvedAt);
            Assert.Contains(
                saved,
                await repository.GetOpenAttemptsAsync(
                    saved.StoreCode,
                    saved.DeviceCode,
                    saved.Environment));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Refund_failure_cas_preserves_existing_response_evidence_and_rejects_completed_winner()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                PaymentId = "REFUND-AUTHORITATIVE-002",
                PaymentStatus = "PENDING",
                ResponseCode = "EXISTING-CODE",
                ResponseText = "Existing provider evidence.",
                SubmissionToken = "refund-token"
            };
            await repository.CreateAsync(attempt);

            var completedAt = attempt.UpdatedAt.AddMinutes(1);
            Assert.True(await repository.TryMarkRefundPaymentVerifiedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                attempt.PaymentId!,
                "COMPLETED",
                responseCode: attempt.ResponseCode,
                responseText: attempt.ResponseText,
                completedAt));

            var applied = await repository.TryPersistRefundFailureForFinalizationAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                "FAILED",
                responseCode: "LATE-FAILURE",
                responseText: "Late failure must not win.",
                attempt.UpdatedAt.AddMinutes(2));

            Assert.False(applied);
            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.PaymentVerified, saved.Status);
            Assert.Equal("COMPLETED", saved.PaymentStatus);
            Assert.Equal(attempt.PaymentId, saved.PaymentId);
            Assert.Equal(CardRecoveryPhases.FinalizePending, saved.RecoveryPhase);
            Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, saved.RecoveryTargetStatus);
            Assert.Equal("EXISTING-CODE", saved.ResponseCode);
            Assert.Equal("Existing provider evidence.", saved.ResponseText);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Refund_failure_cas_does_not_mutate_recovering_completed_evidence_with_matching_version_and_submission_token()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                PaymentId = "REFUND-COMPLETED-LOCAL",
                PaymentStatus = " completed ",
                SubmissionToken = "refund-token",
                RecoveryPhase = CardRecoveryPhases.None,
                RecoveryTargetStatus = null
            };
            await repository.CreateAsync(attempt);

            var applied = await repository.TryPersistRefundFailureForFinalizationAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                "FAILED",
                responseCode: "LATE-FAILURE",
                responseText: "Late failure must not win.",
                attempt.UpdatedAt.AddMinutes(1));

            Assert.False(applied);
            Assert.Equal(attempt, await repository.GetAttemptAsync(attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Refund_failure_cas_rejects_supervisor_terminal_and_finalize_pending_winners()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var supervisorWinner = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                PaymentId = "REFUND-SUPERVISOR-001",
                SubmissionToken = "refund-token",
                ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
                RecoveryPhase = CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.Abandoned
            };
            await repository.CreateAsync(supervisorWinner);

            var applied = await repository.TryPersistRefundFailureForFinalizationAsync(
                supervisorWinner.AttemptGuid,
                supervisorWinner.Status,
                supervisorWinner.UpdatedAt,
                supervisorWinner.SubmissionToken!,
                "REJECTED",
                responseCode: "LATE-FAILURE",
                responseText: "Late failure must not win.",
                supervisorWinner.UpdatedAt.AddMinutes(1));

            Assert.False(applied);
            Assert.Equal(supervisorWinner, await repository.GetAttemptAsync(supervisorWinner.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Refund_failure_finalization_winner_rejects_legacy_token_only_writes_without_mutation()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                PaymentId = "REFUND-AUTHORITATIVE-003",
                PaymentStatus = "PENDING",
                ResponseCode = string.Empty,
                ResponseText = string.Empty,
                SubmissionToken = "refund-token"
            };
            await repository.CreateAsync(attempt);
            var finalizedAt = attempt.UpdatedAt.AddMinutes(1);

            Assert.True(await repository.TryPersistRefundFailureForFinalizationAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                "FAILED",
                responseCode: "SQUARE_FAILURE",
                responseText: "Square refund reached a terminal failure.",
                finalizedAt));

            var finalized = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, finalized.Status);
            Assert.Equal(CardRecoveryPhases.FinalizePending, finalized.RecoveryPhase);
            Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, finalized.RecoveryTargetStatus);

            Assert.False(await repository.TryRecordRefundResponseAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                "LATE-REFUND",
                "PENDING",
                finalizedAt.AddMinutes(1)));
            Assert.Equal(finalized, await repository.GetAttemptAsync(attempt.AttemptGuid));

            Assert.False(await repository.TryMarkRefundPaymentVerifiedAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                "LATE-COMPLETED",
                "COMPLETED",
                responseCode: "LATE-COMPLETED",
                responseText: "Late completed callback.",
                completedAt: finalizedAt.AddMinutes(2)));
            Assert.Equal(finalized, await repository.GetAttemptAsync(attempt.AttemptGuid));

            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                status: LocalSquarePaymentAttemptStatus.Failed,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "LATE-FAILED",
                responseText: "Late failed callback.",
                resolvedAt: finalizedAt.AddMinutes(3)));
            Assert.Equal(finalized, await repository.GetAttemptAsync(attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Versioned_refund_failure_uses_status_and_timestamp_cas_and_preserves_financial_write_semantics()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                PaymentId = "REFUND-CAS-001",
                PaymentStatus = "PENDING",
                SubmissionToken = "refund-token"
            };
            await repository.CreateAsync(attempt);
            var resolvedAt = attempt.UpdatedAt.AddMinutes(1);

            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt.AddTicks(-1),
                attempt.SubmissionToken!,
                LocalSquarePaymentAttemptStatus.Unknown,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "SQUARE_FAILURE",
                responseText: "Stale failure must not win.",
                resolvedAt,
                cancelReason: "stale"));
            Assert.Equal(attempt, await repository.GetAttemptAsync(attempt.AttemptGuid));

            Assert.True(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                LocalSquarePaymentAttemptStatus.Unknown,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "SQUARE_FAILURE",
                responseText: "Square refund failed.",
                resolvedAt,
                cancelReason: "provider failure"));

            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, saved.Status);
            Assert.Equal("FAILED", saved.CheckoutStatus);
            Assert.Equal("provider failure", saved.CancelReason);
            Assert.Equal("FAILED", saved.PaymentStatus);
            Assert.Equal("SQUARE_FAILURE", saved.ResponseCode);
            Assert.Equal("Square refund failed.", saved.ResponseText);
            Assert.Equal(resolvedAt, saved.ResolvedAt);
            Assert.Equal(resolvedAt, saved.UpdatedAt);
            Assert.Equal(attempt.PaymentId, saved.PaymentId);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Versioned_refund_failure_rejects_finalize_pending_rows()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                SubmissionToken = "refund-token",
                RecoveryPhase = CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.Abandoned
            };
            await repository.CreateAsync(attempt);

            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                LocalSquarePaymentAttemptStatus.Failed,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "LATE-FAILED",
                responseText: "Late failure must not win.",
                resolvedAt: attempt.UpdatedAt.AddMinutes(1)));
            Assert.Equal(attempt, await repository.GetAttemptAsync(attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Refund_late_failure_and_pending_callbacks_reject_completed_evidence()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                PaymentId = "REFUND-COMPLETED-WINNER",
                PaymentStatus = " completed ",
                SubmissionToken = "refund-token"
            };
            await repository.CreateAsync(attempt);

            Assert.False(await repository.TryRecordRefundResponseAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                "REFUND-LATE-PENDING",
                "PENDING",
                attempt.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryRecordRefundResponseAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                "REFUND-LATE-PENDING",
                "PENDING",
                attempt.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                LocalSquarePaymentAttemptStatus.Failed,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "LATE-FAILED",
                responseText: "Late failure must not win.",
                resolvedAt: attempt.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                LocalSquarePaymentAttemptStatus.Failed,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "LATE-VERSIONED-FAILED",
                responseText: "Late versioned failure must not win.",
                resolvedAt: attempt.UpdatedAt.AddMinutes(1)));

            Assert.Equal(attempt, await repository.GetAttemptAsync(attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData(LocalSquarePaymentAttemptStatus.Canceled)]
    [InlineData(LocalSquarePaymentAttemptStatus.TimedOut)]
    [InlineData(LocalSquarePaymentAttemptStatus.Failed)]
    [InlineData(LocalSquarePaymentAttemptStatus.OrderCompleted)]
    [InlineData(LocalSquarePaymentAttemptStatus.Abandoned)]
    public async Task Refund_failure_writes_reject_all_terminal_rows(
        LocalSquarePaymentAttemptStatus terminalStatus)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            ILocalSquarePaymentAttemptRepository repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.NewGuid(),
                OperationKind = "Refund",
                Status = terminalStatus,
                SubmissionToken = "refund-token"
            };
            await repository.CreateAsync(attempt);

            Assert.False(await repository.TryMarkRefundPaymentVerifiedAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                "LATE-COMPLETED",
                "COMPLETED",
                responseCode: "LATE-COMPLETED",
                responseText: "Late completed callback.",
                completedAt: attempt.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.SubmissionToken!,
                status: LocalSquarePaymentAttemptStatus.Failed,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "LATE-FAILED",
                responseText: "Late failed callback.",
                resolvedAt: attempt.UpdatedAt.AddMinutes(1)));
            Assert.False(await repository.TryMarkRefundFailedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                attempt.SubmissionToken!,
                LocalSquarePaymentAttemptStatus.Failed,
                checkoutStatus: "FAILED",
                paymentStatus: "FAILED",
                responseCode: "LATE-VERSIONED-FAILED",
                responseText: "Late versioned failure callback.",
                resolvedAt: attempt.UpdatedAt.AddMinutes(1)));
            Assert.Equal(attempt, await repository.GetAttemptAsync(attempt.AttemptGuid));
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task FinalizePending_refund_cannot_begin_a_second_submission()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                AttemptGuid = Guid.Parse("a1000000-0000-0000-0000-000000000005"),
                OperationKind = "Refund",
                Status = LocalSquarePaymentAttemptStatus.Pending,
                SubmissionToken = null,
                ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
                RecoveryPhase = CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.Pending
            };
            await repository.CreateAsync(attempt);

            var applied = await repository.TryBeginRefundSubmissionAsync(
                attempt.AttemptGuid,
                attempt.UpdatedAt,
                "SECOND-SUBMISSION",
                attempt.UpdatedAt.AddMinutes(1));

            Assert.False(applied);
            Assert.Null((await repository.GetAttemptAsync(attempt.AttemptGuid))?.SubmissionToken);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Confirm_refunded_without_real_refund_reference_rejects_before_writing_attempt()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var attempt = CreateSaleAttempt() with
            {
                OperationKind = "Refund"
            };
            await repository.CreateAsync(attempt);

            await Assert.ThrowsAsync<ArgumentException>(() => repository.ResolveRefundAsync(
                new CardRefundAttemptResolution(
                    attempt.AttemptGuid,
                    CardRefundSupervisorDecision.ConfirmRefunded,
                    Reason: "Supervisor note only",
                    Evidence: null,
                    RefundReference: "   ",
                    RetryTxnRef: null,
                    attempt.UpdatedAt.AddMinutes(1))));

            var saved = Assert.IsType<LocalSquarePaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(attempt.Status, saved.Status);
            Assert.Null(saved.SupervisorFinancialReference);
            Assert.Null(saved.ResponseCode);
            Assert.Equal(attempt.UpdatedAt, saved.UpdatedAt);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static async Task<LocalSquarePaymentAttemptRepository> CreateRepositoryAsync(string databasePath)
    {
        var store = new LocalSqliteStore(databasePath);
        await new LocalSchemaService(store).InitializeAsync();
        return new LocalSquarePaymentAttemptRepository(store);
    }

    private static LocalSquarePaymentAttempt CreateSaleAttempt()
    {
        var timestamp = DateTimeOffset.Parse("2026-08-23T09:00:00+10:00");
        return new LocalSquarePaymentAttempt(
            Guid.Parse("a1000000-0000-0000-0000-000000000001"),
            CheckoutId: "CHECKOUT-001",
            IdempotencyKey: "square-sale-cas-001",
            DeviceId: "SQ-DEVICE",
            LocationId: "SQ-LOCATION",
            Environment: "Sandbox",
            Amount: 12.34m,
            AmountCents: 1234,
            Currency: "AUD",
            Status: LocalSquarePaymentAttemptStatus.Recovering,
            CheckoutStatus: "PENDING",
            CancelReason: null,
            OrderDraftJson: "{}",
            StoreCode: "S001",
            DeviceCode: "POS-01",
            CashierId: "C001",
            PaymentId: null,
            PaymentStatus: null,
            ResponseCode: null,
            ResponseText: null,
            CreatedAt: timestamp.AddMinutes(-1),
            UpdatedAt: timestamp,
            CompletedAt: null,
            OrderCompletedAt: null,
            ResolvedAt: null,
            OperationKind: "Sale",
            OperationGuid: Guid.Parse("a2000000-0000-0000-0000-000000000001"),
            SubmissionToken: "sale-token",
            RefundBusinessKey: null);
    }

    private static SquarePaymentResolution CreatePaymentResolution(
        LocalSquarePaymentAttempt attempt,
        CardRecoverySupervisorDecision decision,
        string? paymentReference)
    {
        return new SquarePaymentResolution(
            attempt.AttemptGuid,
            decision,
            Reason: string.Empty,
            Evidence: decision == CardRecoverySupervisorDecision.ConfirmNotProcessed
                ? "Bank portal confirms no payment."
                : null,
            paymentReference,
            attempt.Status,
            attempt.UpdatedAt,
            attempt.UpdatedAt.AddMinutes(1));
    }

    private static LocalFinancialSupervisorResolution CreateJournal(
        LocalSquarePaymentAttempt attempt,
        CardRecoverySupervisorDecision decision,
        string? financialReference)
    {
        return new LocalFinancialSupervisorResolution(
            Guid.NewGuid(),
            LocalFinancialSupervisorResolutionTarget.ActiveSession,
            "Square",
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.AttemptGuid,
            RefundStepGuid: null,
            attempt.OperationGuid,
            attempt.CheckoutId,
            decision.ToString(),
            "SUPERVISOR",
            OperatorUserGuid: null,
            OperatorName: "Supervisor",
            Reason: string.Empty,
            Evidence: null,
            financialReference,
            RetryReference: null,
            attempt.UpdatedAt.AddMinutes(1),
            Guid.NewGuid(),
            "{}");
    }

    private static async Task<int> CountSupervisorJournalsAsync(
        LocalSqliteStore store,
        Guid attemptGuid)
    {
        await using var connection = await store.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM LocalFinancialSupervisorResolutions
            WHERE AttemptGuid = $AttemptGuid;
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ReadScalarIntAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static string CreateTempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"hbpos-square-recovery-cas-{Guid.NewGuid():N}.db");

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
