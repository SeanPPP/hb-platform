using System.Globalization;
using Hbpos.Client.Wpf.Models;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public enum LocalCardPaymentAttemptStatus
{
    Pending,
    SessionStarted,
    Recovering,
    Approved,
    RequiresReview,
    Declined,
    TimedOut,
    Cancelled,
    Failed,
    OrderCompleted,
    Abandoned
}

public sealed record LocalCardPaymentAttempt(
    Guid AttemptGuid,
    string? SessionId,
    string? TxnRef,
    string Processor,
    string Environment,
    string ConnectionMode,
    string TxnType,
    decimal Amount,
    LocalCardPaymentAttemptStatus Status,
    string OrderDraftJson,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string? ResponseCode,
    string? ResponseText,
    string? PaymentReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? AcknowledgedAt,
    string OperationKind = "Sale",
    Guid? OperationGuid = null,
    string? SubmissionToken = null,
    string? RefundBusinessKey = null);

public enum ActiveSessionSupervisorDecision
{
    ConfirmPaid,
    ConfirmNotPaid,
    ContinueWaiting
}

public sealed record ActiveSessionResolution(
    Guid AttemptGuid,
    string SessionId,
    ActiveSessionSupervisorDecision Decision,
    LocalCardPaymentAttemptStatus ExpectedStatus,
    DateTimeOffset ExpectedUpdatedAt,
    string Reason,
    string? Evidence,
    string? PaymentReference,
    DateTimeOffset ResolvedAt);

public static class ActiveSessionSupervisorResolutionCodes
{
    public const string ConfirmedPaid = "SUPERVISOR_CONFIRMED_PAID";
    public const string ConfirmedNotPaid = "SUPERVISOR_CONFIRMED_NOT_PAID";
    public const string ContinueWaiting = "SUPERVISOR_CONTINUE_WAITING";
}

public sealed record LinklyPaymentAttemptContext(
    Guid AttemptGuid,
    Func<string, string?, DateTimeOffset, CancellationToken, Task> BindSessionAsync,
    string? TxnRef = null,
    string? SubmissionToken = null);

public interface ILinklyPaymentAttemptContextAccessor
{
    LinklyPaymentAttemptContext? Current { get; }

    IDisposable Begin(LinklyPaymentAttemptContext context);
}

public sealed class LinklyPaymentAttemptContextAccessor : ILinklyPaymentAttemptContextAccessor
{
    private readonly AsyncLocal<LinklyPaymentAttemptContext?> _current = new();

    public LinklyPaymentAttemptContext? Current => _current.Value;

    public IDisposable Begin(LinklyPaymentAttemptContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new Scope(this, previous);
    }

    private sealed class Scope(
        LinklyPaymentAttemptContextAccessor owner,
        LinklyPaymentAttemptContext? previous) : IDisposable
    {
        public void Dispose()
        {
            owner._current.Value = previous;
        }
    }
}

public interface ILocalCardPaymentAttemptRepository
{
    Task CreateAsync(LocalCardPaymentAttempt attempt, CancellationToken cancellationToken = default);

    async Task<LocalCardPaymentAttempt> CreateOrGetActiveSessionAsync(
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        await CreateAsync(attempt, cancellationToken);
        return attempt;
    }

    async Task<LocalCardPaymentAttempt> CreateOrGetOpenRefundAsync(
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        await CreateAsync(attempt, cancellationToken);
        return attempt;
    }

    async Task<bool> TryBeginRefundSubmissionAsync(
        Guid attemptGuid,
        DateTimeOffset expectedUpdatedAt,
        string submissionToken,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await MarkRecoveringAsync(attemptGuid, updatedAt, cancellationToken);
        return true;
    }

    async Task<bool> TryUpdateRefundSessionAsync(
        Guid attemptGuid,
        string submissionToken,
        string sessionId,
        string? txnRef,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await UpdateSessionAsync(attemptGuid, sessionId, txnRef, updatedAt, cancellationToken);
        return true;
    }

    async Task<bool> TryUpdateRefundOutcomeAsync(
        Guid attemptGuid,
        string submissionToken,
        LocalCardPaymentAttemptStatus status,
        string? responseCode,
        string? responseText,
        string? paymentReference,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await UpdateOutcomeAsync(
            attemptGuid,
            status,
            responseCode,
            responseText,
            paymentReference,
            completedAt,
            cancellationToken);
        return true;
    }

    async Task<bool> TryMarkRefundRecoveringAsync(
        Guid attemptGuid,
        string submissionToken,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await MarkRecoveringAsync(attemptGuid, updatedAt, cancellationToken);
        return true;
    }

    Task UpdateSessionAsync(
        Guid attemptGuid,
        string sessionId,
        string? txnRef,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task UpdateOutcomeAsync(
        Guid attemptGuid,
        LocalCardPaymentAttemptStatus status,
        string? responseCode,
        string? responseText,
        string? paymentReference,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task MarkOrderCompletedAsync(
        Guid attemptGuid,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task MarkAcknowledgedAsync(
        Guid attemptGuid,
        DateTimeOffset acknowledgedAt,
        CancellationToken cancellationToken = default);

    Task MarkRecoveringAsync(
        Guid attemptGuid,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<LocalCardPaymentAttempt?> GetLatestOpenAttemptAsync(
        string storeCode,
        string deviceCode,
        string? cashierId,
        string environment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenRefundAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalCardPaymentAttempt>>([]);

    Task<bool> ResolveRefundAsync(
        CardRefundAttemptResolution resolution,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    Task<bool> ResolveRefundWithJournalAsync(
        CardRefundAttemptResolution resolution,
        LocalFinancialSupervisorResolution journal,
        CancellationToken cancellationToken = default) =>
        ResolveRefundAsync(resolution, cancellationToken);

    Task<bool> ResolveActiveSessionWithJournalAsync(
        ActiveSessionResolution resolution,
        LocalFinancialSupervisorResolution journal,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    Task<bool> ResolvePaymentWithJournalAsync(
        ActiveSessionResolution resolution,
        LocalFinancialSupervisorResolution journal,
        CancellationToken cancellationToken = default) =>
        ResolveActiveSessionWithJournalAsync(resolution, journal, cancellationToken);

    Task<LocalCardPaymentAttempt?> GetLatestOpenActiveSessionAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LocalCardPaymentAttempt?>(null);

    Task<LocalCardPaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default);
}

public sealed class LocalCardPaymentAttemptRepository(LocalSqliteStore store) : ILocalCardPaymentAttemptRepository
{
    private static readonly string[] TerminalStatuses =
    [
        LocalCardPaymentAttemptStatus.Declined.ToString(),
        LocalCardPaymentAttemptStatus.TimedOut.ToString(),
        LocalCardPaymentAttemptStatus.Cancelled.ToString(),
        LocalCardPaymentAttemptStatus.Failed.ToString(),
        LocalCardPaymentAttemptStatus.OrderCompleted.ToString(),
        LocalCardPaymentAttemptStatus.Abandoned.ToString()
    ];

    public async Task CreateAsync(LocalCardPaymentAttempt attempt, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LocalCardPaymentAttempts
            (
                AttemptGuid,
                SessionId,
                TxnRef,
                Processor,
                Environment,
                ConnectionMode,
                TxnType,
                Amount,
                Status,
                OrderDraftJson,
                StoreCode,
                DeviceCode,
                CashierId,
                ResponseCode,
                ResponseText,
                PaymentReference,
                CreatedAt,
                UpdatedAt,
                CompletedAt,
                AcknowledgedAt,
                OperationKind,
                OperationGuid,
                SubmissionToken,
                RefundBusinessKey
            )
            VALUES
            (
                $AttemptGuid,
                $SessionId,
                $TxnRef,
                $Processor,
                $Environment,
                $ConnectionMode,
                $TxnType,
                $Amount,
                $Status,
                $OrderDraftJson,
                $StoreCode,
                $DeviceCode,
                $CashierId,
                $ResponseCode,
                $ResponseText,
                $PaymentReference,
                $CreatedAt,
                $UpdatedAt,
                $CompletedAt,
                $AcknowledgedAt,
                $OperationKind,
                $OperationGuid,
                $SubmissionToken,
                $RefundBusinessKey
            );
            """;
        AddAttemptParameters(command, attempt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LocalCardPaymentAttempt> CreateOrGetActiveSessionAsync(
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(attempt.OperationKind, "ActiveSession", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attempt.SessionId))
        {
            throw new InvalidOperationException(
                "ActiveSession attempt must contain a stable SessionId and OperationKind.");
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        // 立即事务保证并发恢复入口只能创建一条稳定的 Session 记录。
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT OR IGNORE INTO LocalCardPaymentAttempts
                (
                    AttemptGuid, SessionId, TxnRef, Processor, Environment, ConnectionMode,
                    TxnType, Amount, Status, OrderDraftJson, StoreCode, DeviceCode, CashierId,
                    ResponseCode, ResponseText, PaymentReference, CreatedAt, UpdatedAt,
                    CompletedAt, AcknowledgedAt, OperationKind, OperationGuid, SubmissionToken,
                    RefundBusinessKey
                )
                VALUES
                (
                    $AttemptGuid, $SessionId, $TxnRef, $Processor, $Environment, $ConnectionMode,
                    $TxnType, $Amount, $Status, $OrderDraftJson, $StoreCode, $DeviceCode, $CashierId,
                    $ResponseCode, $ResponseText, $PaymentReference, $CreatedAt, $UpdatedAt,
                    $CompletedAt, $AcknowledgedAt, $OperationKind, $OperationGuid, $SubmissionToken,
                    $RefundBusinessKey
                );
                """;
            AddAttemptParameters(insertCommand, attempt);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        LocalCardPaymentAttempt? persisted;
        await using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = """
                SELECT *
                FROM LocalCardPaymentAttempts
                WHERE Environment = $Environment
                  AND StoreCode = $StoreCode
                  AND DeviceCode = $DeviceCode
                  AND SessionId = $SessionId
                  AND OperationKind = 'ActiveSession'
                LIMIT 1;
                """;
            readCommand.Parameters.AddWithValue("$Environment", attempt.Environment);
            readCommand.Parameters.AddWithValue("$StoreCode", attempt.StoreCode);
            readCommand.Parameters.AddWithValue("$DeviceCode", attempt.DeviceCode);
            readCommand.Parameters.AddWithValue("$SessionId", attempt.SessionId);
            await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
            persisted = await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
        }

        if (persisted is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("ActiveSession attempt could not be persisted.");
        }

        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task UpdateSessionAsync(
        Guid attemptGuid,
        string sessionId,
        string? txnRef,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                SessionId = $SessionId,
                TxnRef = $TxnRef,
                Status = $Status,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND COALESCE(ResponseCode, '') NOT IN (
                    $ResolvedCode1,
                    $ResolvedCode2,
                    $ResolvedCode3,
                    $ResolvedCode4
                  );
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$SessionId", sessionId);
        command.Parameters.AddWithValue("$TxnRef", (object?)txnRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$Status", LocalCardPaymentAttemptStatus.SessionStarted.ToString());
        command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
        AddSupervisorResolvedCodeParameters(command);
        await EnsureSingleUpdateAsync(command, cancellationToken);
    }

    public async Task UpdateOutcomeAsync(
        Guid attemptGuid,
        LocalCardPaymentAttemptStatus status,
        string? responseCode,
        string? responseText,
        string? paymentReference,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                Status = $Status,
                ResponseCode = $ResponseCode,
                ResponseText = $ResponseText,
                PaymentReference = $PaymentReference,
                CompletedAt = $CompletedAt,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND COALESCE(ResponseCode, '') NOT IN (
                    $ResolvedCode1,
                    $ResolvedCode2,
                    $ResolvedCode3,
                    $ResolvedCode4
                  );
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$Status", status.ToString());
        command.Parameters.AddWithValue("$ResponseCode", (object?)responseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ResponseText", (object?)responseText ?? DBNull.Value);
        command.Parameters.AddWithValue("$PaymentReference", (object?)paymentReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$CompletedAt", completedAt.ToString("O"));
        command.Parameters.AddWithValue("$UpdatedAt", completedAt.ToString("O"));
        AddSupervisorResolvedCodeParameters(command);
        await EnsureSingleUpdateAsync(command, cancellationToken);
    }

    public async Task MarkOrderCompletedAsync(
        Guid attemptGuid,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                Status = $Status,
                CompletedAt = COALESCE(CompletedAt, $CompletedAt),
                UpdatedAt = $CompletedAt
            WHERE AttemptGuid = $AttemptGuid;
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$Status", LocalCardPaymentAttemptStatus.OrderCompleted.ToString());
        command.Parameters.AddWithValue("$CompletedAt", completedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkAcknowledgedAsync(
        Guid attemptGuid,
        DateTimeOffset acknowledgedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                AcknowledgedAt = $AcknowledgedAt,
                UpdatedAt = $AcknowledgedAt
            WHERE AttemptGuid = $AttemptGuid;
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$AcknowledgedAt", acknowledgedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkRecoveringAsync(
        Guid attemptGuid,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                Status = $Status,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND COALESCE(ResponseCode, '') NOT IN (
                    $ResolvedCode1,
                    $ResolvedCode2,
                    $ResolvedCode3,
                    $ResolvedCode4
                  );
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$Status", LocalCardPaymentAttemptStatus.Recovering.ToString());
        command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
        AddSupervisorResolvedCodeParameters(command);
        await EnsureSingleUpdateAsync(command, cancellationToken);
    }

    public async Task<LocalCardPaymentAttempt> CreateOrGetOpenRefundAsync(
        LocalCardPaymentAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(attempt.OperationKind, "Refund", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(attempt.RefundBusinessKey))
        {
            throw new InvalidOperationException("退款 attempt 必须包含稳定的 RefundBusinessKey。");
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT OR IGNORE INTO LocalCardPaymentAttempts
                (
                    AttemptGuid,
                    SessionId,
                    TxnRef,
                    Processor,
                    Environment,
                    ConnectionMode,
                    TxnType,
                    Amount,
                    Status,
                    OrderDraftJson,
                    StoreCode,
                    DeviceCode,
                    CashierId,
                    ResponseCode,
                    ResponseText,
                    PaymentReference,
                    CreatedAt,
                    UpdatedAt,
                    CompletedAt,
                    AcknowledgedAt,
                    OperationKind,
                    OperationGuid,
                    SubmissionToken,
                    RefundBusinessKey
                )
                VALUES
                (
                    $AttemptGuid,
                    $SessionId,
                    $TxnRef,
                    $Processor,
                    $Environment,
                    $ConnectionMode,
                    $TxnType,
                    $Amount,
                    $Status,
                    $OrderDraftJson,
                    $StoreCode,
                    $DeviceCode,
                    $CashierId,
                    $ResponseCode,
                    $ResponseText,
                    $PaymentReference,
                    $CreatedAt,
                    $UpdatedAt,
                    $CompletedAt,
                    $AcknowledgedAt,
                    $OperationKind,
                    $OperationGuid,
                    $SubmissionToken,
                    $RefundBusinessKey
                );
                """;
            AddAttemptParameters(insertCommand, attempt);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        LocalCardPaymentAttempt? persisted;
        await using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = """
                SELECT
                    AttemptGuid,
                    SessionId,
                    TxnRef,
                    Processor,
                    Environment,
                    ConnectionMode,
                    TxnType,
                    Amount,
                    Status,
                    OrderDraftJson,
                    StoreCode,
                    DeviceCode,
                    CashierId,
                    ResponseCode,
                    ResponseText,
                    PaymentReference,
                    CreatedAt,
                    UpdatedAt,
                    CompletedAt,
                    AcknowledgedAt,
                    OperationKind,
                    OperationGuid,
                    SubmissionToken,
                    RefundBusinessKey
                FROM LocalCardPaymentAttempts
                WHERE OperationKind = 'Refund'
                  AND RefundBusinessKey = $RefundBusinessKey
                  AND Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5, $TerminalStatus6)
                LIMIT 1;
                """;
            readCommand.Parameters.AddWithValue("$RefundBusinessKey", attempt.RefundBusinessKey);
            for (var i = 0; i < TerminalStatuses.Length; i++)
            {
                readCommand.Parameters.AddWithValue($"$TerminalStatus{i + 1}", TerminalStatuses[i]);
            }

            await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
            persisted = await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
        }

        await transaction.CommitAsync(cancellationToken);
        return persisted ?? throw new InvalidOperationException("退款 attempt 原子落库后无法读取。");
    }

    public async Task<bool> TryBeginRefundSubmissionAsync(
        Guid attemptGuid,
        DateTimeOffset expectedUpdatedAt,
        string submissionToken,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                Status = $RecoveringStatus,
                ResponseCode = NULL,
                ResponseText = NULL,
                SubmissionToken = $SubmissionToken,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND Status = $PendingStatus
              AND UpdatedAt = $ExpectedUpdatedAt
              AND SubmissionToken IS NULL
              AND COALESCE(ResponseCode, '') IN ('', $ConfirmedNotRefunded);
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$PendingStatus", LocalCardPaymentAttemptStatus.Pending.ToString());
        command.Parameters.AddWithValue("$RecoveringStatus", LocalCardPaymentAttemptStatus.Recovering.ToString());
        command.Parameters.AddWithValue("$ExpectedUpdatedAt", expectedUpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$SubmissionToken", submissionToken);
        command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
        command.Parameters.AddWithValue("$ConfirmedNotRefunded", CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateRefundSessionAsync(
        Guid attemptGuid,
        string submissionToken,
        string sessionId,
        string? txnRef,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                SessionId = $SessionId,
                TxnRef = $TxnRef,
                Status = $Status,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND SubmissionToken = $SubmissionToken
              AND COALESCE(ResponseCode, '') NOT IN (
                    $ResolvedCode1,
                    $ResolvedCode2,
                    $ResolvedCode3,
                    $ResolvedCode4
                  );
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$SubmissionToken", submissionToken);
        command.Parameters.AddWithValue("$SessionId", sessionId);
        command.Parameters.AddWithValue("$TxnRef", (object?)txnRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$Status", LocalCardPaymentAttemptStatus.SessionStarted.ToString());
        command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
        AddSupervisorResolvedCodeParameters(command);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryUpdateRefundOutcomeAsync(
        Guid attemptGuid,
        string submissionToken,
        LocalCardPaymentAttemptStatus status,
        string? responseCode,
        string? responseText,
        string? paymentReference,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                Status = $Status,
                ResponseCode = $ResponseCode,
                ResponseText = $ResponseText,
                PaymentReference = $PaymentReference,
                CompletedAt = $CompletedAt,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND SubmissionToken = $SubmissionToken
              AND COALESCE(ResponseCode, '') NOT IN (
                    $ResolvedCode1,
                    $ResolvedCode2,
                    $ResolvedCode3,
                    $ResolvedCode4
                  );
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$SubmissionToken", submissionToken);
        command.Parameters.AddWithValue("$Status", status.ToString());
        command.Parameters.AddWithValue("$ResponseCode", (object?)responseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ResponseText", (object?)responseText ?? DBNull.Value);
        command.Parameters.AddWithValue("$PaymentReference", (object?)paymentReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$CompletedAt", completedAt.ToString("O"));
        command.Parameters.AddWithValue("$UpdatedAt", completedAt.ToString("O"));
        AddSupervisorResolvedCodeParameters(command);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryMarkRefundRecoveringAsync(
        Guid attemptGuid,
        string submissionToken,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                Status = $Status,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND SubmissionToken = $SubmissionToken
              AND COALESCE(ResponseCode, '') NOT IN (
                    $ResolvedCode1,
                    $ResolvedCode2,
                    $ResolvedCode3,
                    $ResolvedCode4
                  );
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
        command.Parameters.AddWithValue("$SubmissionToken", submissionToken);
        command.Parameters.AddWithValue("$Status", LocalCardPaymentAttemptStatus.Recovering.ToString());
        command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
        AddSupervisorResolvedCodeParameters(command);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public Task<bool> ResolveRefundAsync(
        CardRefundAttemptResolution resolution,
        CancellationToken cancellationToken = default) =>
        ResolveRefundCoreAsync(resolution, journal: null, cancellationToken);

    public Task<bool> ResolveRefundWithJournalAsync(
        CardRefundAttemptResolution resolution,
        LocalFinancialSupervisorResolution journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.Target != LocalFinancialSupervisorResolutionTarget.CardRefund ||
            journal.AttemptGuid != resolution.AttemptGuid)
        {
            throw new ArgumentException("主管结案 journal 与 Linkly 退款 attempt 不匹配。", nameof(journal));
        }

        return ResolveRefundCoreAsync(resolution, journal, cancellationToken);
    }

    private async Task<bool> ResolveRefundCoreAsync(
        CardRefundAttemptResolution resolution,
        LocalFinancialSupervisorResolution? journal,
        CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = resolution.Decision switch
        {
            CardRefundSupervisorDecision.ConfirmRefunded => """
                UPDATE LocalCardPaymentAttempts
                SET
                    Status = $Status,
                    ResponseCode = $ResponseCode,
                    ResponseText = $ResponseText,
                    PaymentReference = $PaymentReference,
                    SubmissionToken = NULL,
                    CompletedAt = $ResolvedAt,
                    UpdatedAt = $ResolvedAt
                WHERE AttemptGuid = $AttemptGuid
                  AND OperationKind = 'Refund'
                  AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  AND Status IN ($ReviewStatus1, $ReviewStatus2, $ReviewStatus3);
                """,
            CardRefundSupervisorDecision.ConfirmNotRefunded => """
                UPDATE LocalCardPaymentAttempts
                SET
                    SessionId = NULL,
                    TxnRef = $RetryTxnRef,
                    Status = $Status,
                    ResponseCode = $ResponseCode,
                    ResponseText = $ResponseText,
                    PaymentReference = NULL,
                    SubmissionToken = NULL,
                    CompletedAt = NULL,
                    AcknowledgedAt = NULL,
                    UpdatedAt = $ResolvedAt
                WHERE AttemptGuid = $AttemptGuid
                  AND OperationKind = 'Refund'
                  AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  AND Status IN ($ReviewStatus1, $ReviewStatus2, $ReviewStatus3);
                """,
            _ => """
                UPDATE LocalCardPaymentAttempts
                SET
                    Status = $Status,
                    ResponseCode = $ResponseCode,
                    ResponseText = $ResponseText,
                    SubmissionToken = NULL,
                    UpdatedAt = $ResolvedAt
                WHERE AttemptGuid = $AttemptGuid
                  AND OperationKind = 'Refund'
                  AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  AND Status IN ($ReviewStatus1, $ReviewStatus2, $ReviewStatus3);
                """
        };

        command.Parameters.AddWithValue("$AttemptGuid", resolution.AttemptGuid.ToString());
        command.Parameters.AddWithValue("$Status", resolution.Decision switch
        {
            CardRefundSupervisorDecision.ConfirmRefunded => LocalCardPaymentAttemptStatus.Approved.ToString(),
            CardRefundSupervisorDecision.ConfirmNotRefunded => LocalCardPaymentAttemptStatus.Pending.ToString(),
            _ => LocalCardPaymentAttemptStatus.Recovering.ToString()
        });
        command.Parameters.AddWithValue("$ResponseCode", resolution.Decision switch
        {
            CardRefundSupervisorDecision.ConfirmRefunded => CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
            CardRefundSupervisorDecision.ConfirmNotRefunded => CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            _ => CardRefundSupervisorResolutionCodes.ContinueWaiting
        });
        command.Parameters.AddWithValue("$ResponseText", BuildResolutionText(resolution));
        command.Parameters.AddWithValue("$PaymentReference", (object?)Normalize(resolution.RefundReference) ?? DBNull.Value);
        command.Parameters.AddWithValue("$RetryTxnRef", (object?)Normalize(resolution.RetryTxnRef) ?? DBNull.Value);
        command.Parameters.AddWithValue("$ResolvedAt", resolution.ResolvedAt.ToString("O"));
        command.Parameters.AddWithValue("$ResolvedCode1", CardRefundSupervisorResolutionCodes.ConfirmedRefunded);
        command.Parameters.AddWithValue("$ResolvedCode2", CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded);
        command.Parameters.AddWithValue("$ReviewStatus1", LocalCardPaymentAttemptStatus.Recovering.ToString());
        command.Parameters.AddWithValue("$ReviewStatus2", LocalCardPaymentAttemptStatus.RequiresReview.ToString());
        command.Parameters.AddWithValue("$ReviewStatus3", LocalCardPaymentAttemptStatus.SessionStarted.ToString());

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (journal is not null)
        {
            await LocalFinancialSupervisorResolutionRepository.InsertAsync(
                connection,
                transaction,
                journal,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<bool> ResolveActiveSessionWithJournalAsync(
        ActiveSessionResolution resolution,
        LocalFinancialSupervisorResolution journal,
        CancellationToken cancellationToken = default) =>
        ResolvePaymentWithJournalAsync(resolution, journal, cancellationToken);

    public async Task<bool> ResolvePaymentWithJournalAsync(
        ActiveSessionResolution resolution,
        LocalFinancialSupervisorResolution journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (string.IsNullOrWhiteSpace(resolution.SessionId))
        {
            throw new ArgumentException("ActiveSession resolution must include SessionId.", nameof(resolution));
        }

        if (journal.Target != LocalFinancialSupervisorResolutionTarget.ActiveSession ||
            journal.AttemptGuid != resolution.AttemptGuid ||
            !string.Equals(journal.SessionId, resolution.SessionId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Supervisor journal does not match the ActiveSession attempt.",
                nameof(journal));
        }

        if (resolution.Decision == ActiveSessionSupervisorDecision.ConfirmPaid &&
            string.IsNullOrWhiteSpace(resolution.PaymentReference) &&
            string.IsNullOrWhiteSpace(resolution.Evidence))
        {
            throw new ArgumentException(
                "ConfirmPaid requires a payment reference or bank evidence.",
                nameof(resolution));
        }

        if (resolution.Decision == ActiveSessionSupervisorDecision.ConfirmNotPaid &&
            string.IsNullOrWhiteSpace(resolution.Evidence))
        {
            throw new ArgumentException(
                "ConfirmNotPaid requires bank evidence.",
                nameof(resolution));
        }

        if (resolution.ExpectedStatus is not (
                LocalCardPaymentAttemptStatus.Pending or
                LocalCardPaymentAttemptStatus.SessionStarted or
                LocalCardPaymentAttemptStatus.Recovering or
                LocalCardPaymentAttemptStatus.RequiresReview))
        {
            throw new ArgumentException(
                "ActiveSession can only be resolved from an unresolved status.",
                nameof(resolution));
        }

        var nextStatus = resolution.Decision switch
        {
            ActiveSessionSupervisorDecision.ConfirmPaid => LocalCardPaymentAttemptStatus.Approved,
            ActiveSessionSupervisorDecision.ConfirmNotPaid => LocalCardPaymentAttemptStatus.Cancelled,
            _ => LocalCardPaymentAttemptStatus.Recovering
        };
        var responseCode = resolution.Decision switch
        {
            ActiveSessionSupervisorDecision.ConfirmPaid => ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
            ActiveSessionSupervisorDecision.ConfirmNotPaid => ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
            _ => ActiveSessionSupervisorResolutionCodes.ContinueWaiting
        };
        var reason = Normalize(resolution.Reason);
        var evidence = Normalize(resolution.Evidence);
        var responseText = evidence is null
            ? reason ?? string.Empty
            : reason is null
                ? $"Evidence: {evidence}"
                : $"{reason} Evidence: {evidence}";

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // ExpectedUpdatedAt 与状态共同组成 CAS，避免两个主管同时结案。
        command.CommandText = """
            UPDATE LocalCardPaymentAttempts
            SET
                Status = $NextStatus,
                ResponseCode = $ResponseCode,
                ResponseText = $ResponseText,
                PaymentReference = $PaymentReference,
                CompletedAt = $CompletedAt,
                UpdatedAt = $ResolvedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind IN ('Sale', 'ActiveSession')
              AND COALESCE(SessionId, TxnRef) = $SessionId
              AND Status = $ExpectedStatus
              AND UpdatedAt = $ExpectedUpdatedAt;
            """;
        command.Parameters.AddWithValue("$AttemptGuid", resolution.AttemptGuid.ToString());
        command.Parameters.AddWithValue("$SessionId", resolution.SessionId.Trim());
        command.Parameters.AddWithValue(
            "$ExpectedStatus",
            resolution.ExpectedStatus.ToString());
        command.Parameters.AddWithValue("$ExpectedUpdatedAt", resolution.ExpectedUpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$NextStatus", nextStatus.ToString());
        command.Parameters.AddWithValue("$ResponseCode", responseCode);
        command.Parameters.AddWithValue("$ResponseText", responseText);
        command.Parameters.AddWithValue(
            "$PaymentReference",
            (object?)Normalize(resolution.PaymentReference) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$CompletedAt",
            resolution.Decision == ActiveSessionSupervisorDecision.ContinueWaiting
                ? DBNull.Value
                : resolution.ResolvedAt.ToString("O"));
        command.Parameters.AddWithValue("$ResolvedAt", resolution.ResolvedAt.ToString("O"));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await LocalFinancialSupervisorResolutionRepository.InsertAsync(
            connection,
            transaction,
            journal,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<LocalCardPaymentAttempt?> GetLatestOpenActiveSessionAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM LocalCardPaymentAttempts
            WHERE StoreCode = $StoreCode
              AND DeviceCode = $DeviceCode
              AND Environment = $Environment
              AND OperationKind = 'ActiveSession'
              AND AcknowledgedAt IS NULL
            ORDER BY UpdatedAt DESC, CreatedAt DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$DeviceCode", deviceCode);
        command.Parameters.AddWithValue("$Environment", environment);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadAttempt(reader)
            : null;
    }

    public async Task<LocalCardPaymentAttempt?> GetLatestOpenAttemptAsync(
        string storeCode,
        string deviceCode,
        string? cashierId,
        string environment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                AttemptGuid,
                SessionId,
                TxnRef,
                Processor,
                Environment,
                ConnectionMode,
                TxnType,
                Amount,
                Status,
                OrderDraftJson,
                StoreCode,
                DeviceCode,
                CashierId,
                ResponseCode,
                ResponseText,
                PaymentReference,
                CreatedAt,
                UpdatedAt,
                CompletedAt,
                AcknowledgedAt,
                OperationKind,
                OperationGuid,
                SubmissionToken,
                RefundBusinessKey
            FROM LocalCardPaymentAttempts
            WHERE StoreCode = $StoreCode
              AND DeviceCode = $DeviceCode
              -- 中文注释：启动恢复按终端兜底查询，手动传入 cashier 时仍保留原过滤。
              AND ($CashierId IS NULL OR CashierId = $CashierId)
              AND Environment = $Environment
              AND OperationKind = $OperationKind
              AND (
                    Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5, $TerminalStatus6)
                    OR (Status = $OrderCompletedStatus AND AcknowledgedAt IS NULL AND SessionId IS NOT NULL)
                    OR (
                        ResponseCode IN ($SupervisorPaidCode, $SupervisorNotPaidCode)
                        AND AcknowledgedAt IS NULL
                    )
                  )
            ORDER BY UpdatedAt DESC, CreatedAt DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$DeviceCode", deviceCode);
        command.Parameters.AddWithValue("$CashierId", (object?)cashierId ?? DBNull.Value);
        command.Parameters.AddWithValue("$Environment", environment);
        command.Parameters.AddWithValue("$OperationKind", "Sale");
        for (var i = 0; i < TerminalStatuses.Length; i++)
        {
            command.Parameters.AddWithValue($"$TerminalStatus{i + 1}", TerminalStatuses[i]);
        }
        command.Parameters.AddWithValue("$OrderCompletedStatus", LocalCardPaymentAttemptStatus.OrderCompleted.ToString());
        command.Parameters.AddWithValue("$SupervisorPaidCode", ActiveSessionSupervisorResolutionCodes.ConfirmedPaid);
        command.Parameters.AddWithValue("$SupervisorNotPaidCode", ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadAttempt(reader)
            : null;
    }

    public async Task<LocalCardPaymentAttempt?> GetAttemptAsync(
        Guid attemptGuid,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                AttemptGuid,
                SessionId,
                TxnRef,
                Processor,
                Environment,
                ConnectionMode,
                TxnType,
                Amount,
                Status,
                OrderDraftJson,
                StoreCode,
                DeviceCode,
                CashierId,
                ResponseCode,
                ResponseText,
                PaymentReference,
                CreatedAt,
                UpdatedAt,
                CompletedAt,
                AcknowledgedAt,
                OperationKind,
                OperationGuid,
                SubmissionToken,
                RefundBusinessKey
            FROM LocalCardPaymentAttempts
            WHERE AttemptGuid = $AttemptGuid;
            """;
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadAttempt(reader)
            : null;
    }

    public async Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenRefundAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                AttemptGuid,
                SessionId,
                TxnRef,
                Processor,
                Environment,
                ConnectionMode,
                TxnType,
                Amount,
                Status,
                OrderDraftJson,
                StoreCode,
                DeviceCode,
                CashierId,
                ResponseCode,
                ResponseText,
                PaymentReference,
                CreatedAt,
                UpdatedAt,
                CompletedAt,
                AcknowledgedAt,
                OperationKind,
                OperationGuid,
                SubmissionToken,
                RefundBusinessKey
            FROM LocalCardPaymentAttempts
            WHERE StoreCode = $StoreCode
              AND DeviceCode = $DeviceCode
              AND Environment = $Environment
              AND OperationKind = $OperationKind
              AND Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5, $TerminalStatus6)
            ORDER BY UpdatedAt DESC, CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$DeviceCode", deviceCode);
        command.Parameters.AddWithValue("$Environment", environment);
        command.Parameters.AddWithValue("$OperationKind", "Refund");
        for (var i = 0; i < TerminalStatuses.Length; i++)
        {
            command.Parameters.AddWithValue($"$TerminalStatus{i + 1}", TerminalStatuses[i]);
        }

        var attempts = new List<LocalCardPaymentAttempt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(ReadAttempt(reader));
        }

        return attempts;
    }

    public async Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 异常中心队列按同一终端/环境跨收银员列出全部未结 Sale、Refund 与 ActiveSession。
        // 每种操作类型的“未结”语义与既有 latest-only 查询保持一致，避免队列漏掉可恢复记录。
        command.CommandText = """
            SELECT
                AttemptGuid,
                SessionId,
                TxnRef,
                Processor,
                Environment,
                ConnectionMode,
                TxnType,
                Amount,
                Status,
                OrderDraftJson,
                StoreCode,
                DeviceCode,
                CashierId,
                ResponseCode,
                ResponseText,
                PaymentReference,
                CreatedAt,
                UpdatedAt,
                CompletedAt,
                AcknowledgedAt,
                OperationKind,
                OperationGuid,
                SubmissionToken,
                RefundBusinessKey
            FROM LocalCardPaymentAttempts
            WHERE StoreCode = $StoreCode
              AND DeviceCode = $DeviceCode
              AND Environment = $Environment
              AND OperationKind IN ('Sale', 'Refund', 'ActiveSession')
              AND (
                    (OperationKind = 'ActiveSession' AND AcknowledgedAt IS NULL)
                    OR (
                        OperationKind = 'Refund'
                        AND Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5, $TerminalStatus6)
                    )
                    OR (
                        OperationKind = 'Sale'
                        AND (
                            Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5, $TerminalStatus6)
                            OR (Status = $OrderCompletedStatus AND AcknowledgedAt IS NULL AND SessionId IS NOT NULL)
                            OR (
                                ResponseCode IN ($SupervisorPaidCode, $SupervisorNotPaidCode)
                                AND AcknowledgedAt IS NULL
                            )
                        )
                    )
                  )
            ORDER BY UpdatedAt DESC, CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$DeviceCode", deviceCode);
        command.Parameters.AddWithValue("$Environment", environment);
        command.Parameters.AddWithValue("$OrderCompletedStatus", LocalCardPaymentAttemptStatus.OrderCompleted.ToString());
        command.Parameters.AddWithValue("$SupervisorPaidCode", ActiveSessionSupervisorResolutionCodes.ConfirmedPaid);
        command.Parameters.AddWithValue("$SupervisorNotPaidCode", ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid);
        for (var i = 0; i < TerminalStatuses.Length; i++)
        {
            command.Parameters.AddWithValue($"$TerminalStatus{i + 1}", TerminalStatuses[i]);
        }

        var attempts = new List<LocalCardPaymentAttempt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(ReadAttempt(reader));
        }

        return attempts;
    }

    private static void AddSupervisorResolvedCodeParameters(SqliteCommand command)
    {
        command.Parameters.AddWithValue("$ResolvedCode1", CardRefundSupervisorResolutionCodes.ConfirmedRefunded);
        command.Parameters.AddWithValue("$ResolvedCode2", CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded);
        command.Parameters.AddWithValue("$ResolvedCode3", ActiveSessionSupervisorResolutionCodes.ConfirmedPaid);
        command.Parameters.AddWithValue("$ResolvedCode4", ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid);
    }

    private static async Task EnsureSingleUpdateAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("支付 attempt 状态已变化，旧任务不得继续写入。");
        }
    }

    private static void AddAttemptParameters(SqliteCommand command, LocalCardPaymentAttempt attempt)
    {
        command.Parameters.AddWithValue("$AttemptGuid", attempt.AttemptGuid.ToString());
        command.Parameters.AddWithValue("$SessionId", (object?)attempt.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$TxnRef", (object?)attempt.TxnRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$Processor", attempt.Processor);
        command.Parameters.AddWithValue("$Environment", attempt.Environment);
        command.Parameters.AddWithValue("$ConnectionMode", attempt.ConnectionMode);
        command.Parameters.AddWithValue("$TxnType", attempt.TxnType);
        command.Parameters.AddWithValue("$Amount", attempt.Amount);
        command.Parameters.AddWithValue("$Status", attempt.Status.ToString());
        // OrderDraftJson 这里只做原样落盘，业务草稿结构由上层支付流程决定。
        command.Parameters.AddWithValue("$OrderDraftJson", attempt.OrderDraftJson);
        command.Parameters.AddWithValue("$StoreCode", attempt.StoreCode);
        command.Parameters.AddWithValue("$DeviceCode", attempt.DeviceCode);
        command.Parameters.AddWithValue("$CashierId", attempt.CashierId);
        command.Parameters.AddWithValue("$ResponseCode", (object?)attempt.ResponseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ResponseText", (object?)attempt.ResponseText ?? DBNull.Value);
        command.Parameters.AddWithValue("$PaymentReference", (object?)attempt.PaymentReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedAt", attempt.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$UpdatedAt", attempt.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$CompletedAt", attempt.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$AcknowledgedAt", attempt.AcknowledgedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$OperationKind", attempt.OperationKind);
        command.Parameters.AddWithValue("$OperationGuid", attempt.OperationGuid?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$SubmissionToken", (object?)attempt.SubmissionToken ?? DBNull.Value);
        command.Parameters.AddWithValue("$RefundBusinessKey", (object?)attempt.RefundBusinessKey ?? DBNull.Value);
    }

    private static string BuildResolutionText(CardRefundAttemptResolution resolution)
    {
        var evidence = Normalize(resolution.Evidence);
        return evidence is null
            ? resolution.Reason
            : $"{resolution.Reason} Evidence: {evidence}";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LocalCardPaymentAttempt ReadAttempt(SqliteDataReader reader)
    {
        return new LocalCardPaymentAttempt(
            ReadGuid(reader, "AttemptGuid"),
            ReadNullableString(reader, "SessionId"),
            ReadNullableString(reader, "TxnRef"),
            ReadString(reader, "Processor"),
            ReadString(reader, "Environment"),
            ReadString(reader, "ConnectionMode"),
            ReadString(reader, "TxnType"),
            ReadDecimal(reader, "Amount"),
            Enum.Parse<LocalCardPaymentAttemptStatus>(ReadString(reader, "Status")),
            ReadString(reader, "OrderDraftJson"),
            ReadString(reader, "StoreCode"),
            ReadString(reader, "DeviceCode"),
            ReadString(reader, "CashierId"),
            ReadNullableString(reader, "ResponseCode"),
            ReadNullableString(reader, "ResponseText"),
            ReadNullableString(reader, "PaymentReference"),
            ReadDateTimeOffset(reader, "CreatedAt"),
            ReadDateTimeOffset(reader, "UpdatedAt"),
            ReadNullableDateTimeOffset(reader, "CompletedAt"),
            ReadNullableDateTimeOffset(reader, "AcknowledgedAt"),
            ReadNullableString(reader, "OperationKind") ?? "Sale",
            ReadNullableGuid(reader, "OperationGuid"),
            ReadNullableString(reader, "SubmissionToken"),
            ReadNullableString(reader, "RefundBusinessKey"));
    }

    private static Guid ReadGuid(SqliteDataReader reader, string name)
    {
        return Guid.Parse(ReadString(reader, name));
    }

    private static string ReadString(SqliteDataReader reader, string name)
    {
        return reader.GetString(reader.GetOrdinal(name));
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static decimal ReadDecimal(SqliteDataReader reader, string name)
    {
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
            long longValue => longValue,
            int intValue => intValue,
            string stringValue => decimal.Parse(stringValue, CultureInfo.InvariantCulture),
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };
    }

    private static DateTimeOffset ReadDateTimeOffset(SqliteDataReader reader, string name)
    {
        return DateTimeOffset.Parse(ReadString(reader, name), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }

    private static Guid? ReadNullableGuid(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
    }
}
