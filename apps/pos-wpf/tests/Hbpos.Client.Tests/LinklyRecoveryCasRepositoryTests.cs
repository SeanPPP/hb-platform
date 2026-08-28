using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

public sealed class LinklyRecoveryCasRepositoryTests
{
    [Fact]
    public async Task TryMarkRecovering_refuses_terminal_status()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.Declined, updatedAt);
            await repository.CreateAsync(attempt);

            var applied = await repository.TryMarkRecoveringAsync(
                attempt.AttemptGuid,
                attempt.Status,
                updatedAt,
                updatedAt.AddMinutes(1));

            Assert.False(applied);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TryMarkRecovering_approved_keeps_status_and_advances_cas_timestamp()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var recoveringAt = updatedAt.AddMinutes(1);
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.Approved, updatedAt);
            await repository.CreateAsync(attempt);

            var applied = await repository.TryMarkRecoveringAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Approved,
                updatedAt,
                recoveringAt);
            var staleReplay = await repository.TryMarkRecoveringAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Approved,
                updatedAt,
                recoveringAt.AddMinutes(1));

            Assert.True(applied);
            Assert.False(staleReplay);
            var persisted = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(LocalCardPaymentAttemptStatus.Approved, persisted.Status);
            Assert.Equal(recoveringAt, persisted.UpdatedAt);
            Assert.Equal(CardRecoveryPhases.None, persisted.RecoveryPhase);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData(LocalCardPaymentAttemptStatus.Declined, CardRecoveryPhases.None)]
    [InlineData(LocalCardPaymentAttemptStatus.Pending, CardRecoveryPhases.FinalizePending)]
    public async Task Legacy_mark_recovering_refuses_terminal_and_finalize_pending_rows(
        LocalCardPaymentAttemptStatus status,
        string recoveryPhase)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(
                status,
                updatedAt,
                recoveryPhase: recoveryPhase,
                recoveryTargetStatus: recoveryPhase == CardRecoveryPhases.FinalizePending
                    ? LocalCardPaymentAttemptStatus.OrderCompleted.ToString()
                    : null);
            await repository.CreateAsync(attempt);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.MarkRecoveringAsync(
                    attempt.AttemptGuid,
                    updatedAt.AddMinutes(1)));

            var persisted = await repository.GetAttemptAsync(attempt.AttemptGuid);
            Assert.Equal(status, persisted?.Status);
            Assert.Equal(recoveryPhase, persisted?.RecoveryPhase);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TryPersistRecoveryOutcome_refuses_terminal_status_even_when_cas_matches()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.Declined, updatedAt);
            await repository.CreateAsync(attempt);

            var applied = await repository.TryPersistRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Recovering,
                "00",
                "Approved",
                "REF-LATE",
                attempt.Status,
                attempt.UpdatedAt,
                LocalCardPaymentAttemptStatus.OrderCompleted,
                updatedAt.AddMinutes(1));

            Assert.False(applied);
            Assert.Equal(
                LocalCardPaymentAttemptStatus.Declined,
                (await repository.GetAttemptAsync(attempt.AttemptGuid))?.Status);
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
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Pending,
                updatedAt,
                responseCode: CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
                recoveryPhase: CardRecoveryPhases.FinalizePending,
                recoveryTargetStatus: LocalCardPaymentAttemptStatus.Pending.ToString(),
                operationKind: "Refund");
            await repository.CreateAsync(attempt);

            var applied = await repository.TryBeginRefundSubmissionAsync(
                attempt.AttemptGuid,
                attempt.UpdatedAt,
                "SECOND-SUBMISSION",
                updatedAt.AddMinutes(1));

            Assert.False(applied);
            Assert.Null((await repository.GetAttemptAsync(attempt.AttemptGuid))?.SubmissionToken);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TryMarkRecovering_refuses_supervisor_resolution()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Pending,
                updatedAt,
                responseCode: ActiveSessionSupervisorResolutionCodes.ConfirmedPaid);
            await repository.CreateAsync(attempt);

            var applied = await repository.TryMarkRecoveringAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Pending,
                updatedAt,
                updatedAt.AddMinutes(1));

            Assert.False(applied);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Persist_then_finalize_round_trips_finalize_pending_phase()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.SessionStarted, updatedAt);
            await repository.CreateAsync(attempt);

            var persistedAt = updatedAt.AddMinutes(1);
            var persisted = await repository.TryPersistRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Recovering,
                "00",
                "Approved",
                "REF-1",
                LocalCardPaymentAttemptStatus.SessionStarted,
                updatedAt,
                LocalCardPaymentAttemptStatus.OrderCompleted,
                persistedAt);

            Assert.True(persisted);
            var pending = await repository.GetAttemptAsync(attempt.AttemptGuid);
            Assert.NotNull(pending);
            Assert.Equal(CardRecoveryPhases.FinalizePending, pending.RecoveryPhase);
            Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted.ToString(), pending.RecoveryTargetStatus);
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, pending.Status);

            var completedAt = persistedAt.AddMinutes(1);
            var finalized = await repository.TryFinalizeRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Recovering,
                persistedAt,
                completedAt);

            Assert.True(finalized);
            var finished = await repository.GetAttemptAsync(attempt.AttemptGuid);
            Assert.NotNull(finished);
            Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, finished.Status);
            Assert.Equal(CardRecoveryPhases.None, finished.RecoveryPhase);
            Assert.Null(finished.RecoveryTargetStatus);
            Assert.Equal(completedAt, finished.CompletedAt);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TryMarkRecovering_refuses_finalize_pending_phase()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.SessionStarted, updatedAt);
            await repository.CreateAsync(attempt);

            var persistedAt = updatedAt.AddMinutes(1);
            await repository.TryPersistRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Recovering,
                "00",
                "Approved",
                null,
                LocalCardPaymentAttemptStatus.SessionStarted,
                updatedAt,
                LocalCardPaymentAttemptStatus.OrderCompleted,
                persistedAt);

            var applied = await repository.TryMarkRecoveringAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Recovering,
                persistedAt,
                persistedAt.AddMinutes(1));

            Assert.False(applied);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TryFinalize_refuses_when_not_finalize_pending()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.SessionStarted, updatedAt);
            await repository.CreateAsync(attempt);

            var finalized = await repository.TryFinalizeRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.SessionStarted,
                updatedAt,
                updatedAt.AddMinutes(1));

            Assert.False(finalized);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Supervisor_not_paid_finalize_atomically_abandons_and_acknowledges()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var completedAt = updatedAt.AddMinutes(1);
            var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Recovering,
                updatedAt,
                responseCode: ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
                recoveryPhase: CardRecoveryPhases.FinalizePending,
                recoveryTargetStatus: LocalCardPaymentAttemptStatus.Abandoned.ToString());
            await repository.CreateAsync(attempt);

            var method = typeof(LocalCardPaymentAttemptRepository).GetMethod(
                "TryFinalizeSupervisorNotPaidAndAcknowledgeAsync",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            Assert.NotNull(method);

            var invocation = method.Invoke(
                repository,
                [
                    attempt.AttemptGuid,
                    attempt.Status,
                    attempt.UpdatedAt,
                    completedAt,
                    CancellationToken.None
                ]);
            var appliedTask = invocation as Task<bool>;
            Assert.NotNull(appliedTask);

            Assert.True(await appliedTask);
            var finished = await repository.GetAttemptAsync(attempt.AttemptGuid);
            Assert.NotNull(finished);
            Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned, finished.Status);
            Assert.Equal(CardRecoveryPhases.None, finished.RecoveryPhase);
            Assert.Null(finished.RecoveryTargetStatus);
            Assert.Equal(completedAt, finished.AcknowledgedAt);
            Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, finished.ResponseCode);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData(LocalCardPaymentAttemptStatus.Abandoned, ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid)]
    [InlineData(LocalCardPaymentAttemptStatus.Declined, "05")]
    [InlineData(LocalCardPaymentAttemptStatus.TimedOut, null)]
    [InlineData(LocalCardPaymentAttemptStatus.Cancelled, "17")]
    [InlineData(LocalCardPaymentAttemptStatus.Failed, null)]
    public async Task TryMarkAcknowledged_allows_only_exact_sale_failure_finalize_pending_target(
        LocalCardPaymentAttemptStatus targetStatus,
        string? responseCode)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var acknowledgedAt = updatedAt.AddMinutes(1);
            var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Recovering,
                updatedAt,
                responseCode,
                CardRecoveryPhases.FinalizePending,
                targetStatus.ToString());
            await repository.CreateAsync(attempt);

            var applied = await repository.TryMarkAcknowledgedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                acknowledgedAt);

            Assert.True(applied);
            var persisted = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(attempt.Status, persisted.Status);
            Assert.Equal(CardRecoveryPhases.FinalizePending, persisted.RecoveryPhase);
            Assert.Equal(targetStatus.ToString(), persisted.RecoveryTargetStatus);
            Assert.Equal(acknowledgedAt, persisted.AcknowledgedAt);
            Assert.Equal(acknowledgedAt, persisted.UpdatedAt);

            var completedAt = acknowledgedAt.AddMinutes(1);
            Assert.True(await repository.TryFinalizeRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                persisted.Status,
                persisted.UpdatedAt,
                targetStatus,
                completedAt));
            var completed = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(targetStatus, completed.Status);
            Assert.Equal(CardRecoveryPhases.None, completed.RecoveryPhase);
            Assert.Null(completed.RecoveryTargetStatus);
            Assert.Equal(acknowledgedAt, completed.AcknowledgedAt);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData("Sale", LocalCardPaymentAttemptStatus.OrderCompleted, "00")]
    [InlineData("Sale", LocalCardPaymentAttemptStatus.Abandoned, "05")]
    [InlineData("Sale", LocalCardPaymentAttemptStatus.Declined, "00")]
    [InlineData("Refund", LocalCardPaymentAttemptStatus.Declined, "05")]
    public async Task TryMarkAcknowledged_refuses_unrelated_or_approved_finalize_pending_target(
        string operationKind,
        LocalCardPaymentAttemptStatus targetStatus,
        string? responseCode)
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Recovering,
                updatedAt,
                responseCode,
                CardRecoveryPhases.FinalizePending,
                targetStatus.ToString(),
                operationKind);
            await repository.CreateAsync(attempt);

            var applied = await repository.TryMarkAcknowledgedAsync(
                attempt.AttemptGuid,
                attempt.Status,
                attempt.UpdatedAt,
                updatedAt.AddMinutes(1));

            Assert.False(applied);
            var persisted = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Null(persisted.AcknowledgedAt);
            Assert.Equal(updatedAt, persisted.UpdatedAt);
            Assert.Equal(CardRecoveryPhases.FinalizePending, persisted.RecoveryPhase);
            Assert.Equal(targetStatus.ToString(), persisted.RecoveryTargetStatus);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Continue_waiting_with_existing_payment_reference_loses_cas_without_clearing_evidence()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.Recovering, updatedAt) with
            {
                ResponseCode = "05",
                PaymentReference = "BANK-EXISTING-002"
            };
            await repository.CreateAsync(attempt);

            var applied = await repository.ResolvePaymentWithJournalAsync(
                CreatePaymentResolution(
                    attempt,
                    ActiveSessionSupervisorDecision.ContinueWaiting,
                    updatedAt.AddMinutes(1)),
                CreatePaymentJournal(
                    attempt,
                    ActiveSessionSupervisorDecision.ContinueWaiting,
                    updatedAt.AddMinutes(1)));

            Assert.False(applied);
            var persisted = await repository.GetAttemptAsync(attempt.AttemptGuid);
            Assert.NotNull(persisted);
            Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, persisted.Status);
            Assert.Equal("05", persisted.ResponseCode);
            Assert.Equal("BANK-EXISTING-002", persisted.PaymentReference);
            Assert.Equal(updatedAt, persisted.UpdatedAt);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Continue_waiting_payment_reference_stays_audit_only_then_terminal_approval_writes_real_reference()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var repository = await CreateRepositoryAsync(databasePath);
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var waitingResolvedAt = updatedAt.AddMinutes(1);
            const string supervisorInputReference = "SUPERVISOR-INPUT-001";
            const string terminalReference = "TERMINAL-REF-001";
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.SessionStarted, updatedAt);
            await repository.CreateAsync(attempt);

            var resolution = CreatePaymentResolution(
                attempt,
                ActiveSessionSupervisorDecision.ContinueWaiting,
                waitingResolvedAt) with
            {
                PaymentReference = supervisorInputReference
            };
            var journal = CreatePaymentJournal(
                attempt,
                ActiveSessionSupervisorDecision.ContinueWaiting,
                waitingResolvedAt) with
            {
                FinancialReference = supervisorInputReference
            };

            Assert.True(await repository.ResolvePaymentWithJournalAsync(resolution, journal));

            var waiting = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal(ActiveSessionSupervisorResolutionCodes.ContinueWaiting, waiting.ResponseCode);
            Assert.Null(waiting.PaymentReference);
            Assert.Equal(waitingResolvedAt, waiting.UpdatedAt);
            Assert.Equal(CardRecoveryPhases.None, waiting.RecoveryPhase);

            var journalRepository = new LocalFinancialSupervisorResolutionRepository(
                new LocalSqliteStore(databasePath));
            var persistedJournal = Assert.Single(await journalRepository.GetPendingAuditAsync(10));
            Assert.Equal(supervisorInputReference, persistedJournal.FinancialReference);

            var approvedAt = waitingResolvedAt.AddMinutes(1);
            Assert.True(await repository.TryPersistRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Recovering,
                "00",
                "APPROVED",
                terminalReference,
                waiting.Status,
                waiting.UpdatedAt,
                LocalCardPaymentAttemptStatus.OrderCompleted,
                approvedAt));

            var approved = Assert.IsType<LocalCardPaymentAttempt>(
                await repository.GetAttemptAsync(attempt.AttemptGuid));
            Assert.Equal("00", approved.ResponseCode);
            Assert.Equal(terminalReference, approved.PaymentReference);
            Assert.Equal(CardRecoveryPhases.FinalizePending, approved.RecoveryPhase);
            Assert.Equal(
                LocalCardPaymentAttemptStatus.OrderCompleted.ToString(),
                approved.RecoveryTargetStatus);
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Automatic_approval_and_supervisor_not_paid_cas_have_exactly_one_winner()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var automaticRepository = await CreateRepositoryAsync(databasePath);
            var supervisorRepository = new LocalCardPaymentAttemptRepository(new LocalSqliteStore(databasePath));
            var updatedAt = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
            var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.SessionStarted, updatedAt);
            await automaticRepository.CreateAsync(attempt);

            var automatic = automaticRepository.TryPersistRecoveryOutcomeAsync(
                attempt.AttemptGuid,
                LocalCardPaymentAttemptStatus.Recovering,
                "00",
                "APPROVED",
                "AUTO-REF-001",
                attempt.Status,
                attempt.UpdatedAt,
                LocalCardPaymentAttemptStatus.OrderCompleted,
                updatedAt.AddMinutes(1));
            var supervisor = supervisorRepository.ResolvePaymentWithJournalAsync(
                CreatePaymentResolution(
                    attempt,
                    ActiveSessionSupervisorDecision.ConfirmNotPaid,
                    updatedAt.AddMinutes(1)),
                CreatePaymentJournal(
                    attempt,
                    ActiveSessionSupervisorDecision.ConfirmNotPaid,
                    updatedAt.AddMinutes(1)));

            var outcomes = await Task.WhenAll(automatic, supervisor);

            Assert.Equal(1, outcomes.Count(applied => applied));
            var winner = await automaticRepository.GetAttemptAsync(attempt.AttemptGuid);
            Assert.NotNull(winner);
            Assert.Equal(CardRecoveryPhases.FinalizePending, winner.RecoveryPhase);
            if (winner.ResponseCode == "00")
            {
                Assert.Equal("AUTO-REF-001", winner.PaymentReference);
                Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, winner.Status);
            }
            else
            {
                Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, winner.ResponseCode);
                Assert.Null(winner.PaymentReference);
                Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, winner.Status);
            }
        }
        finally
        {
            DeleteTempDatabase(databasePath);
        }
    }

    private static ActiveSessionResolution CreatePaymentResolution(
        LocalCardPaymentAttempt attempt,
        ActiveSessionSupervisorDecision decision,
        DateTimeOffset resolvedAt) =>
        new(
            attempt.AttemptGuid,
            attempt.SessionId ?? attempt.TxnRef ?? throw new InvalidOperationException("Missing session id."),
            decision,
            attempt.Status,
            attempt.UpdatedAt,
            "Supervisor decision",
            decision == ActiveSessionSupervisorDecision.ConfirmNotPaid
                ? "Bank evidence"
                : null,
            null,
            resolvedAt);

    private static LocalFinancialSupervisorResolution CreatePaymentJournal(
        LocalCardPaymentAttempt attempt,
        ActiveSessionSupervisorDecision decision,
        DateTimeOffset resolvedAt) =>
        new(
            Guid.NewGuid(),
            LocalFinancialSupervisorResolutionTarget.ActiveSession,
            attempt.Processor,
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.AttemptGuid,
            null,
            attempt.OperationGuid,
            attempt.SessionId ?? attempt.TxnRef,
            decision.ToString(),
            "MANAGER-01",
            null,
            "Manager One",
            "Supervisor decision",
            decision == ActiveSessionSupervisorDecision.ConfirmNotPaid
                ? "Bank evidence"
                : null,
            null,
            null,
            resolvedAt,
            Guid.NewGuid(),
            "{}");

    private static async Task<LocalCardPaymentAttemptRepository> CreateRepositoryAsync(string databasePath)
    {
        var store = new LocalSqliteStore(databasePath);
        var schema = new LocalSchemaService(store);
        await schema.InitializeAsync();
        return new LocalCardPaymentAttemptRepository(store);
    }

    private static LocalCardPaymentAttempt CreateAttempt(
        LocalCardPaymentAttemptStatus status,
        DateTimeOffset updatedAt,
        string? responseCode = null,
        string recoveryPhase = CardRecoveryPhases.None,
        string? recoveryTargetStatus = null,
        string operationKind = "Sale")
    {
        return new LocalCardPaymentAttempt(
            Guid.NewGuid(),
            "SESSION-001",
            "TXN-001",
            "Linkly",
            "Sandbox",
            "CloudBackendAsync",
            "P",
            10m,
            status,
            "{}",
            "S001",
            "POS-01",
            "C001",
            responseCode,
            null,
            null,
            updatedAt.AddMinutes(-1),
            updatedAt,
            status == LocalCardPaymentAttemptStatus.OrderCompleted ? updatedAt : null,
            null,
            operationKind,
            null,
            null,
            null,
            recoveryPhase,
            recoveryTargetStatus);
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"hbpos-linkly-cas-{Guid.NewGuid():N}.db");
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
