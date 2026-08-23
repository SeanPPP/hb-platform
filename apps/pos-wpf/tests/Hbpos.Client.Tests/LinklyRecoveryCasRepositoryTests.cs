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
