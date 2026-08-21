using System.Globalization;
using Hbpos.Client.Wpf.Models;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public enum LocalSquarePaymentAttemptStatus
{
    Pending,
    CheckoutCreated,
    Recovering,
    CheckoutCompleted,
    PaymentVerified,
    Canceled,
    TimedOut,
    Failed,
    Unknown,
    OrderCompleted,
    Abandoned
}

public sealed record LocalSquarePaymentAttempt(
    Guid AttemptGuid,
    string? CheckoutId,
    string IdempotencyKey,
    string DeviceId,
    string LocationId,
    string Environment,
    decimal Amount,
    long AmountCents,
    string Currency,
    LocalSquarePaymentAttemptStatus Status,
    string? CheckoutStatus,
    string? CancelReason,
    string OrderDraftJson,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string? PaymentId,
    string? PaymentStatus,
    string? ResponseCode,
    string? ResponseText,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? OrderCompletedAt,
    DateTimeOffset? ResolvedAt,
    string OperationKind = "Sale",
    Guid? OperationGuid = null,
    string? SubmissionToken = null,
    string? RefundBusinessKey = null);

public sealed record SquarePaymentAttemptContext(
    Guid AttemptGuid,
    string IdempotencyKey,
    Func<string, string?, DateTimeOffset, CancellationToken, Task>? BindCheckoutAsync = null,
    string? SubmissionToken = null,
    Func<string, string, DateTimeOffset, CancellationToken, Task>? BindRefundAsync = null,
    Func<string, string, DateTimeOffset, CardTerminalEnvironment, CancellationToken, Task>? BindRefundEvidenceAsync = null)
{
    public bool CanBindCheckout => BindCheckoutAsync is not null;

    public bool CanBindRefund => BindRefundEvidenceAsync is not null || BindRefundAsync is not null;
}

public interface ISquarePaymentAttemptContextAccessor
{
    SquarePaymentAttemptContext? Current { get; }

    IDisposable Begin(SquarePaymentAttemptContext context);
}

public sealed class SquarePaymentAttemptContextAccessor : ISquarePaymentAttemptContextAccessor
{
    private readonly AsyncLocal<SquarePaymentAttemptContext?> _current = new();

    public SquarePaymentAttemptContext? Current => _current.Value;

    public IDisposable Begin(SquarePaymentAttemptContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new Scope(this, previous);
    }

    private sealed class Scope(
        SquarePaymentAttemptContextAccessor owner,
        SquarePaymentAttemptContext? previous) : IDisposable
    {
        public void Dispose()
        {
            owner._current.Value = previous;
        }
    }
}

public interface ILocalSquarePaymentAttemptRepository
{
    Task CreateAsync(LocalSquarePaymentAttempt attempt, CancellationToken cancellationToken = default);

    async Task<LocalSquarePaymentAttempt> CreateOrGetOpenRefundAsync(
        LocalSquarePaymentAttempt attempt,
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

    async Task<bool> TryMarkRefundCheckoutCreatedAsync(
        Guid attemptGuid,
        string submissionToken,
        string checkoutId,
        string? checkoutStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await MarkCheckoutCreatedAsync(attemptGuid, checkoutId, checkoutStatus, updatedAt, cancellationToken);
        return true;
    }

    Task<bool> TryRecordRefundResponseAsync(
        Guid attemptGuid,
        string submissionToken,
        string refundId,
        string refundStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    async Task<bool> TryMarkRefundPaymentVerifiedAsync(
        Guid attemptGuid,
        string submissionToken,
        string paymentId,
        string paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await MarkPaymentVerifiedAsync(
            attemptGuid,
            paymentId,
            paymentStatus,
            responseCode,
            responseText,
            completedAt,
            cancellationToken);
        return true;
    }

    async Task<bool> TryMarkRefundFailedAsync(
        Guid attemptGuid,
        string submissionToken,
        LocalSquarePaymentAttemptStatus status,
        string? checkoutStatus,
        string? paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default,
        string? cancelReason = null)
    {
        await MarkFailedAsync(
            attemptGuid,
            status,
            checkoutStatus,
            paymentStatus,
            responseCode,
            responseText,
            resolvedAt,
            cancellationToken,
            cancelReason);
        return true;
    }

    Task MarkCheckoutCreatedAsync(
        Guid attemptGuid,
        string checkoutId,
        string? checkoutStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    async Task<bool> TryMarkCheckoutCreatedAsync(
        Guid attemptGuid,
        string submissionToken,
        string checkoutId,
        string? checkoutStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await MarkCheckoutCreatedAsync(attemptGuid, checkoutId, checkoutStatus, updatedAt, cancellationToken);
        return true;
    }

    Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);

    Task UpdateCheckoutStatusAsync(
        Guid attemptGuid,
        LocalSquarePaymentAttemptStatus status,
        string? checkoutStatus,
        string? cancelReason,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task MarkPaymentVerifiedAsync(
        Guid attemptGuid,
        string paymentId,
        string paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid attemptGuid,
        LocalSquarePaymentAttemptStatus status,
        string? checkoutStatus,
        string? paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default,
        string? cancelReason = null);

    Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default);

    Task<LocalSquarePaymentAttempt?> GetLatestOpenAttemptAsync(
        string storeCode,
        string deviceCode,
        string? cashierId,
        string environment,
        CancellationToken cancellationToken = default);

    Task<LocalSquarePaymentAttempt?> GetLatestOpenSaleAttemptForTerminalAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default) =>
        GetLatestOpenAttemptAsync(
            storeCode,
            deviceCode,
            cashierId: null,
            environment,
            cancellationToken);

    Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenRefundAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalSquarePaymentAttempt>>([]);

    Task<bool> ResolveRefundAsync(
        CardRefundAttemptResolution resolution,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    Task<bool> ResolveRefundWithJournalAsync(
        CardRefundAttemptResolution resolution,
        LocalFinancialSupervisorResolution journal,
        CancellationToken cancellationToken = default) =>
        ResolveRefundAsync(resolution, cancellationToken);

    Task<LocalSquarePaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default);
}

public sealed class LocalSquarePaymentAttemptRepository(LocalSqliteStore store) : ILocalSquarePaymentAttemptRepository
{
    private static readonly string[] TerminalStatuses =
    [
        LocalSquarePaymentAttemptStatus.Canceled.ToString(),
        LocalSquarePaymentAttemptStatus.TimedOut.ToString(),
        LocalSquarePaymentAttemptStatus.Failed.ToString(),
        LocalSquarePaymentAttemptStatus.OrderCompleted.ToString(),
        LocalSquarePaymentAttemptStatus.Abandoned.ToString()
    ];

    public async Task CreateAsync(LocalSquarePaymentAttempt attempt, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LocalSquarePaymentAttempts
            (
                AttemptGuid, CheckoutId, IdempotencyKey, DeviceId, LocationId, Environment,
                Amount, AmountCents, Currency, Status, CheckoutStatus, CancelReason,
                OrderDraftJson, StoreCode, DeviceCode, CashierId, PaymentId, PaymentStatus,
                ResponseCode, ResponseText, CreatedAt, UpdatedAt, CompletedAt, OrderCompletedAt, ResolvedAt
                , OperationKind, OperationGuid, SubmissionToken, RefundBusinessKey
            )
            VALUES
            (
                $AttemptGuid, $CheckoutId, $IdempotencyKey, $DeviceId, $LocationId, $Environment,
                $Amount, $AmountCents, $Currency, $Status, $CheckoutStatus, $CancelReason,
                $OrderDraftJson, $StoreCode, $DeviceCode, $CashierId, $PaymentId, $PaymentStatus,
                $ResponseCode, $ResponseText, $CreatedAt, $UpdatedAt, $CompletedAt, $OrderCompletedAt, $ResolvedAt
                , $OperationKind, $OperationGuid, $SubmissionToken, $RefundBusinessKey
            );
            """;
        AddAttemptParameters(command, attempt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LocalSquarePaymentAttempt> CreateOrGetOpenRefundAsync(
        LocalSquarePaymentAttempt attempt,
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
                INSERT OR IGNORE INTO LocalSquarePaymentAttempts
                (
                    AttemptGuid, CheckoutId, IdempotencyKey, DeviceId, LocationId, Environment,
                    Amount, AmountCents, Currency, Status, CheckoutStatus, CancelReason,
                    OrderDraftJson, StoreCode, DeviceCode, CashierId, PaymentId, PaymentStatus,
                    ResponseCode, ResponseText, CreatedAt, UpdatedAt, CompletedAt, OrderCompletedAt, ResolvedAt,
                    OperationKind, OperationGuid, SubmissionToken, RefundBusinessKey
                )
                VALUES
                (
                    $AttemptGuid, $CheckoutId, $IdempotencyKey, $DeviceId, $LocationId, $Environment,
                    $Amount, $AmountCents, $Currency, $Status, $CheckoutStatus, $CancelReason,
                    $OrderDraftJson, $StoreCode, $DeviceCode, $CashierId, $PaymentId, $PaymentStatus,
                    $ResponseCode, $ResponseText, $CreatedAt, $UpdatedAt, $CompletedAt, $OrderCompletedAt, $ResolvedAt,
                    $OperationKind, $OperationGuid, $SubmissionToken, $RefundBusinessKey
                );
                """;
            AddAttemptParameters(insertCommand, attempt);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        LocalSquarePaymentAttempt? persisted;
        await using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = """
                SELECT *
                FROM LocalSquarePaymentAttempts
                WHERE OperationKind = 'Refund'
                  AND RefundBusinessKey = $RefundBusinessKey
                  AND Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5)
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
        return await ExecuteUpdateCountAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $RecoveringStatus,
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
            """,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$PendingStatus", LocalSquarePaymentAttemptStatus.Pending.ToString());
                command.Parameters.AddWithValue("$RecoveringStatus", LocalSquarePaymentAttemptStatus.Recovering.ToString());
                command.Parameters.AddWithValue("$ExpectedUpdatedAt", expectedUpdatedAt.ToString("O"));
                command.Parameters.AddWithValue("$SubmissionToken", submissionToken);
                command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
                command.Parameters.AddWithValue("$ConfirmedNotRefunded", CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded);
            },
            cancellationToken) == 1;
    }

    public async Task<bool> TryMarkRefundCheckoutCreatedAsync(
        Guid attemptGuid,
        string submissionToken,
        string checkoutId,
        string? checkoutStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteRefundSubmissionUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET CheckoutId = $CheckoutId,
                CheckoutStatus = $CheckoutStatus,
                Status = $Status,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND SubmissionToken = $SubmissionToken
              AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2);
            """,
            attemptGuid,
            submissionToken,
            command =>
            {
                command.Parameters.AddWithValue("$CheckoutId", checkoutId);
                command.Parameters.AddWithValue("$CheckoutStatus", (object?)checkoutStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$Status", LocalSquarePaymentAttemptStatus.CheckoutCreated.ToString());
                command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task<bool> TryRecordRefundResponseAsync(
        Guid attemptGuid,
        string submissionToken,
        string refundId,
        string refundStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteRefundSubmissionUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $Status,
                PaymentId = $RefundId,
                PaymentStatus = $RefundStatus,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND SubmissionToken = $SubmissionToken
              AND Status IN ($RecoveringStatus, $UnknownStatus)
              AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2);
            """,
            attemptGuid,
            submissionToken,
            command =>
            {
                command.Parameters.AddWithValue("$Status", LocalSquarePaymentAttemptStatus.Recovering.ToString());
                command.Parameters.AddWithValue("$RecoveringStatus", LocalSquarePaymentAttemptStatus.Recovering.ToString());
                command.Parameters.AddWithValue("$UnknownStatus", LocalSquarePaymentAttemptStatus.Unknown.ToString());
                command.Parameters.AddWithValue("$RefundId", refundId);
                command.Parameters.AddWithValue("$RefundStatus", refundStatus);
                command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task<bool> TryMarkRefundPaymentVerifiedAsync(
        Guid attemptGuid,
        string submissionToken,
        string paymentId,
        string paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteRefundSubmissionUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $Status,
                PaymentId = $PaymentId,
                PaymentStatus = $PaymentStatus,
                ResponseCode = $ResponseCode,
                ResponseText = $ResponseText,
                CompletedAt = $CompletedAt,
                UpdatedAt = $CompletedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND SubmissionToken = $SubmissionToken
              AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2);
            """,
            attemptGuid,
            submissionToken,
            command =>
            {
                command.Parameters.AddWithValue("$Status", LocalSquarePaymentAttemptStatus.PaymentVerified.ToString());
                command.Parameters.AddWithValue("$PaymentId", paymentId);
                command.Parameters.AddWithValue("$PaymentStatus", paymentStatus);
                command.Parameters.AddWithValue("$ResponseCode", (object?)responseCode ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResponseText", (object?)responseText ?? DBNull.Value);
                command.Parameters.AddWithValue("$CompletedAt", completedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task<bool> TryMarkRefundFailedAsync(
        Guid attemptGuid,
        string submissionToken,
        LocalSquarePaymentAttemptStatus status,
        string? checkoutStatus,
        string? paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default,
        string? cancelReason = null)
    {
        return await ExecuteRefundSubmissionUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $Status,
                CheckoutStatus = COALESCE($CheckoutStatus, CheckoutStatus),
                CancelReason = COALESCE($CancelReason, CancelReason),
                PaymentStatus = COALESCE($PaymentStatus, PaymentStatus),
                ResponseCode = $ResponseCode,
                ResponseText = $ResponseText,
                ResolvedAt = $ResolvedAt,
                UpdatedAt = $ResolvedAt
            WHERE AttemptGuid = $AttemptGuid
              AND OperationKind = 'Refund'
              AND SubmissionToken = $SubmissionToken
              AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2);
            """,
            attemptGuid,
            submissionToken,
            command =>
            {
                command.Parameters.AddWithValue("$Status", status.ToString());
                command.Parameters.AddWithValue("$CheckoutStatus", (object?)checkoutStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$CancelReason", (object?)cancelReason ?? DBNull.Value);
                command.Parameters.AddWithValue("$PaymentStatus", (object?)paymentStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResponseCode", (object?)responseCode ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResponseText", (object?)responseText ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResolvedAt", resolvedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task MarkCheckoutCreatedAsync(
        Guid attemptGuid,
        string checkoutId,
        string? checkoutStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await ExecuteGuardedUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET CheckoutId = $CheckoutId,
                CheckoutStatus = $CheckoutStatus,
                Status = $Status,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND (
                    OperationKind <> 'Refund'
                    OR COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  );
            """,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$CheckoutId", checkoutId);
                command.Parameters.AddWithValue("$CheckoutStatus", (object?)checkoutStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$Status", LocalSquarePaymentAttemptStatus.CheckoutCreated.ToString());
                command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
            },
            cancellationToken);
    }

    public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        return UpdateCheckoutStatusAsync(
            attemptGuid,
            LocalSquarePaymentAttemptStatus.Recovering,
            checkoutStatus: null,
            cancelReason: null,
            updatedAt,
            cancellationToken);
    }

    public async Task<bool> TryMarkCheckoutCreatedAsync(
        Guid attemptGuid,
        string submissionToken,
        string checkoutId,
        string? checkoutStatus,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteUpdateCountAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET CheckoutId = $CheckoutId,
                CheckoutStatus = $CheckoutStatus,
                Status = $Status,
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND SubmissionToken = $SubmissionToken
              AND Status IN ($PendingStatus, $RecoveringStatus, $CheckoutCreatedStatus);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$SubmissionToken", submissionToken);
                command.Parameters.AddWithValue("$CheckoutId", checkoutId);
                command.Parameters.AddWithValue("$CheckoutStatus", (object?)checkoutStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$Status", LocalSquarePaymentAttemptStatus.CheckoutCreated.ToString());
                command.Parameters.AddWithValue("$PendingStatus", LocalSquarePaymentAttemptStatus.Pending.ToString());
                command.Parameters.AddWithValue("$RecoveringStatus", LocalSquarePaymentAttemptStatus.Recovering.ToString());
                command.Parameters.AddWithValue("$CheckoutCreatedStatus", LocalSquarePaymentAttemptStatus.CheckoutCreated.ToString());
                command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
            },
            cancellationToken) == 1;
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
            throw new ArgumentException("主管结案 journal 与 Square 退款 attempt 不匹配。", nameof(journal));
        }

        return ResolveRefundCoreAsync(resolution, journal, cancellationToken);
    }

    private async Task<bool> ResolveRefundCoreAsync(
        CardRefundAttemptResolution resolution,
        LocalFinancialSupervisorResolution? journal,
        CancellationToken cancellationToken)
    {
        var sql = resolution.Decision switch
        {
            CardRefundSupervisorDecision.ConfirmRefunded => """
                UPDATE LocalSquarePaymentAttempts
                SET Status = $Status,
                    PaymentId = $RefundReference,
                    PaymentStatus = $PaymentStatus,
                    ResponseCode = $ResponseCode,
                    ResponseText = $ResponseText,
                    SubmissionToken = NULL,
                    CompletedAt = $ResolvedAt,
                    ResolvedAt = $ResolvedAt,
                    UpdatedAt = $ResolvedAt
                WHERE AttemptGuid = $AttemptGuid
                  AND OperationKind = 'Refund'
                  AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  AND Status IN ($ReviewStatus1, $ReviewStatus2, $ReviewStatus3);
                """,
            CardRefundSupervisorDecision.ConfirmNotRefunded => """
                UPDATE LocalSquarePaymentAttempts
                SET Status = $Status,
                    CheckoutId = NULL,
                    CheckoutStatus = NULL,
                    CancelReason = NULL,
                    PaymentId = NULL,
                    PaymentStatus = NULL,
                    ResponseCode = $ResponseCode,
                    ResponseText = $ResponseText,
                    SubmissionToken = NULL,
                    CompletedAt = NULL,
                    OrderCompletedAt = NULL,
                    ResolvedAt = NULL,
                    UpdatedAt = $ResolvedAt
                WHERE AttemptGuid = $AttemptGuid
                  AND OperationKind = 'Refund'
                  AND COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  AND Status IN ($ReviewStatus1, $ReviewStatus2, $ReviewStatus3);
                """,
            _ => """
                UPDATE LocalSquarePaymentAttempts
                SET Status = $Status,
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

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$AttemptGuid", resolution.AttemptGuid.ToString());
        command.Parameters.AddWithValue("$Status", resolution.Decision switch
        {
            CardRefundSupervisorDecision.ConfirmRefunded => LocalSquarePaymentAttemptStatus.PaymentVerified.ToString(),
            CardRefundSupervisorDecision.ConfirmNotRefunded => LocalSquarePaymentAttemptStatus.Pending.ToString(),
            _ => LocalSquarePaymentAttemptStatus.Recovering.ToString()
        });
        command.Parameters.AddWithValue("$RefundReference", (object?)Normalize(resolution.RefundReference) ?? DBNull.Value);
        command.Parameters.AddWithValue("$PaymentStatus", CardRefundSupervisorResolutionCodes.ConfirmedRefunded);
        command.Parameters.AddWithValue("$ResponseCode", resolution.Decision switch
        {
            CardRefundSupervisorDecision.ConfirmRefunded => CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
            CardRefundSupervisorDecision.ConfirmNotRefunded => CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            _ => CardRefundSupervisorResolutionCodes.ContinueWaiting
        });
        command.Parameters.AddWithValue("$ResponseText", BuildResolutionText(resolution));
        command.Parameters.AddWithValue("$ResolvedAt", resolution.ResolvedAt.ToString("O"));
        command.Parameters.AddWithValue("$ResolvedCode1", CardRefundSupervisorResolutionCodes.ConfirmedRefunded);
        command.Parameters.AddWithValue("$ResolvedCode2", CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded);
        command.Parameters.AddWithValue("$ReviewStatus1", LocalSquarePaymentAttemptStatus.Recovering.ToString());
        command.Parameters.AddWithValue("$ReviewStatus2", LocalSquarePaymentAttemptStatus.Unknown.ToString());
        command.Parameters.AddWithValue("$ReviewStatus3", LocalSquarePaymentAttemptStatus.CheckoutCreated.ToString());

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

    public async Task UpdateCheckoutStatusAsync(
        Guid attemptGuid,
        LocalSquarePaymentAttemptStatus status,
        string? checkoutStatus,
        string? cancelReason,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await ExecuteGuardedUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $Status,
                CheckoutStatus = COALESCE($CheckoutStatus, CheckoutStatus),
                CancelReason = COALESCE($CancelReason, CancelReason),
                UpdatedAt = $UpdatedAt
            WHERE AttemptGuid = $AttemptGuid
              AND (
                    OperationKind <> 'Refund'
                    OR COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  );
            """,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$Status", status.ToString());
                command.Parameters.AddWithValue("$CheckoutStatus", (object?)checkoutStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$CancelReason", (object?)cancelReason ?? DBNull.Value);
                command.Parameters.AddWithValue("$UpdatedAt", updatedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task MarkPaymentVerifiedAsync(
        Guid attemptGuid,
        string paymentId,
        string paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        await ExecuteGuardedUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $Status,
                PaymentId = $PaymentId,
                PaymentStatus = $PaymentStatus,
                ResponseCode = $ResponseCode,
                ResponseText = $ResponseText,
                CompletedAt = $CompletedAt,
                UpdatedAt = $CompletedAt
            WHERE AttemptGuid = $AttemptGuid
              AND (
                    OperationKind <> 'Refund'
                    OR COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  );
            """,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$Status", LocalSquarePaymentAttemptStatus.PaymentVerified.ToString());
                command.Parameters.AddWithValue("$PaymentId", paymentId);
                command.Parameters.AddWithValue("$PaymentStatus", paymentStatus);
                command.Parameters.AddWithValue("$ResponseCode", (object?)responseCode ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResponseText", (object?)responseText ?? DBNull.Value);
                command.Parameters.AddWithValue("$CompletedAt", completedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid attemptGuid,
        LocalSquarePaymentAttemptStatus status,
        string? checkoutStatus,
        string? paymentStatus,
        string? responseCode,
        string? responseText,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default,
        string? cancelReason = null)
    {
        await ExecuteGuardedUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $Status,
                CheckoutStatus = COALESCE($CheckoutStatus, CheckoutStatus),
                CancelReason = COALESCE($CancelReason, CancelReason),
                PaymentStatus = COALESCE($PaymentStatus, PaymentStatus),
                ResponseCode = $ResponseCode,
                ResponseText = $ResponseText,
                ResolvedAt = $ResolvedAt,
                UpdatedAt = $ResolvedAt
            WHERE AttemptGuid = $AttemptGuid
              AND (
                    OperationKind <> 'Refund'
                    OR COALESCE(ResponseCode, '') NOT IN ($ResolvedCode1, $ResolvedCode2)
                  );
            """,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$Status", status.ToString());
                command.Parameters.AddWithValue("$CheckoutStatus", (object?)checkoutStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$CancelReason", (object?)cancelReason ?? DBNull.Value);
                command.Parameters.AddWithValue("$PaymentStatus", (object?)paymentStatus ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResponseCode", (object?)responseCode ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResponseText", (object?)responseText ?? DBNull.Value);
                command.Parameters.AddWithValue("$ResolvedAt", resolvedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        await ExecuteUpdateAsync(
            """
            UPDATE LocalSquarePaymentAttempts
            SET Status = $Status,
                OrderCompletedAt = $CompletedAt,
                UpdatedAt = $CompletedAt
            WHERE AttemptGuid = $AttemptGuid;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$Status", LocalSquarePaymentAttemptStatus.OrderCompleted.ToString());
                command.Parameters.AddWithValue("$CompletedAt", completedAt.ToString("O"));
            },
            cancellationToken);
    }

    public async Task<LocalSquarePaymentAttempt?> GetLatestOpenAttemptAsync(
        string storeCode,
        string deviceCode,
        string? cashierId,
        string environment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM LocalSquarePaymentAttempts
            WHERE StoreCode = $StoreCode
              AND DeviceCode = $DeviceCode
              AND ($CashierId IS NULL OR CashierId = $CashierId)
              AND Environment = $Environment
              AND OperationKind = $OperationKind
              AND Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5)
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

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public Task<LocalSquarePaymentAttempt?> GetLatestOpenSaleAttemptForTerminalAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default) =>
        GetLatestOpenAttemptAsync(
            storeCode,
            deviceCode,
            cashierId: null,
            environment,
            cancellationToken);

    public async Task<LocalSquarePaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM LocalSquarePaymentAttempts WHERE AttemptGuid = $AttemptGuid;";
        command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public async Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenRefundAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM LocalSquarePaymentAttempts
            WHERE StoreCode = $StoreCode
              AND DeviceCode = $DeviceCode
              AND Environment = $Environment
              AND OperationKind = $OperationKind
              AND Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5)
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

        var attempts = new List<LocalSquarePaymentAttempt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(ReadAttempt(reader));
        }

        return attempts;
    }

    public async Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenAttemptsAsync(
        string storeCode,
        string deviceCode,
        string environment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Square 异常中心队列：同一终端/环境跨收银员列出全部未结 Sale 与 Refund。
        command.CommandText = """
            SELECT *
            FROM LocalSquarePaymentAttempts
            WHERE StoreCode = $StoreCode
              AND DeviceCode = $DeviceCode
              AND Environment = $Environment
              AND OperationKind IN ('Sale', 'Refund')
              AND Status NOT IN ($TerminalStatus1, $TerminalStatus2, $TerminalStatus3, $TerminalStatus4, $TerminalStatus5)
            ORDER BY UpdatedAt DESC, CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$DeviceCode", deviceCode);
        command.Parameters.AddWithValue("$Environment", environment);
        for (var i = 0; i < TerminalStatuses.Length; i++)
        {
            command.Parameters.AddWithValue($"$TerminalStatus{i + 1}", TerminalStatuses[i]);
        }

        var attempts = new List<LocalSquarePaymentAttempt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(ReadAttempt(reader));
        }

        return attempts;
    }

    private async Task ExecuteUpdateAsync(
        string sql,
        Action<SqliteCommand> configure,
        CancellationToken cancellationToken)
    {
        await ExecuteUpdateCountAsync(sql, configure, cancellationToken);
    }

    private async Task ExecuteGuardedUpdateAsync(
        string sql,
        Action<SqliteCommand> configure,
        CancellationToken cancellationToken)
    {
        var affected = await ExecuteUpdateCountAsync(
            sql,
            command =>
            {
                configure(command);
                AddSupervisorResolvedCodeParameters(command);
            },
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("支付 attempt 状态已变化，旧任务不得继续写入。");
        }
    }

    private async Task<bool> ExecuteRefundSubmissionUpdateAsync(
        string sql,
        Guid attemptGuid,
        string submissionToken,
        Action<SqliteCommand> configure,
        CancellationToken cancellationToken)
    {
        return await ExecuteUpdateCountAsync(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$AttemptGuid", attemptGuid.ToString());
                command.Parameters.AddWithValue("$SubmissionToken", submissionToken);
                AddSupervisorResolvedCodeParameters(command);
                configure(command);
            },
            cancellationToken) == 1;
    }

    private async Task<int> ExecuteUpdateCountAsync(
        string sql,
        Action<SqliteCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddSupervisorResolvedCodeParameters(SqliteCommand command)
    {
        command.Parameters.AddWithValue("$ResolvedCode1", CardRefundSupervisorResolutionCodes.ConfirmedRefunded);
        command.Parameters.AddWithValue("$ResolvedCode2", CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded);
    }

    private static void AddAttemptParameters(SqliteCommand command, LocalSquarePaymentAttempt attempt)
    {
        command.Parameters.AddWithValue("$AttemptGuid", attempt.AttemptGuid.ToString());
        command.Parameters.AddWithValue("$CheckoutId", (object?)attempt.CheckoutId ?? DBNull.Value);
        command.Parameters.AddWithValue("$IdempotencyKey", attempt.IdempotencyKey);
        command.Parameters.AddWithValue("$DeviceId", attempt.DeviceId);
        command.Parameters.AddWithValue("$LocationId", attempt.LocationId);
        command.Parameters.AddWithValue("$Environment", attempt.Environment);
        command.Parameters.AddWithValue("$Amount", attempt.Amount);
        command.Parameters.AddWithValue("$AmountCents", attempt.AmountCents);
        command.Parameters.AddWithValue("$Currency", attempt.Currency);
        command.Parameters.AddWithValue("$Status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$CheckoutStatus", (object?)attempt.CheckoutStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$CancelReason", (object?)attempt.CancelReason ?? DBNull.Value);
        // Square attempt 的草稿与 Linkly 分开存，避免 checkout_id 被误当 Linkly session 使用。
        command.Parameters.AddWithValue("$OrderDraftJson", attempt.OrderDraftJson);
        command.Parameters.AddWithValue("$StoreCode", attempt.StoreCode);
        command.Parameters.AddWithValue("$DeviceCode", attempt.DeviceCode);
        command.Parameters.AddWithValue("$CashierId", attempt.CashierId);
        command.Parameters.AddWithValue("$PaymentId", (object?)attempt.PaymentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$PaymentStatus", (object?)attempt.PaymentStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$ResponseCode", (object?)attempt.ResponseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ResponseText", (object?)attempt.ResponseText ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedAt", attempt.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$UpdatedAt", attempt.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$CompletedAt", attempt.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$OrderCompletedAt", attempt.OrderCompletedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$ResolvedAt", attempt.ResolvedAt?.ToString("O") ?? (object)DBNull.Value);
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

    private static LocalSquarePaymentAttempt ReadAttempt(SqliteDataReader reader)
    {
        return new LocalSquarePaymentAttempt(
            ReadGuid(reader, "AttemptGuid"),
            ReadNullableString(reader, "CheckoutId"),
            ReadString(reader, "IdempotencyKey"),
            ReadString(reader, "DeviceId"),
            ReadString(reader, "LocationId"),
            ReadString(reader, "Environment"),
            ReadDecimal(reader, "Amount"),
            ReadInt64(reader, "AmountCents"),
            ReadString(reader, "Currency"),
            Enum.Parse<LocalSquarePaymentAttemptStatus>(ReadString(reader, "Status")),
            ReadNullableString(reader, "CheckoutStatus"),
            ReadNullableString(reader, "CancelReason"),
            ReadString(reader, "OrderDraftJson"),
            ReadString(reader, "StoreCode"),
            ReadString(reader, "DeviceCode"),
            ReadString(reader, "CashierId"),
            ReadNullableString(reader, "PaymentId"),
            ReadNullableString(reader, "PaymentStatus"),
            ReadNullableString(reader, "ResponseCode"),
            ReadNullableString(reader, "ResponseText"),
            ReadDateTimeOffset(reader, "CreatedAt"),
            ReadDateTimeOffset(reader, "UpdatedAt"),
            ReadNullableDateTimeOffset(reader, "CompletedAt"),
            ReadNullableDateTimeOffset(reader, "OrderCompletedAt"),
            ReadNullableDateTimeOffset(reader, "ResolvedAt"),
            ReadNullableString(reader, "OperationKind") ?? "Sale",
            ReadNullableGuid(reader, "OperationGuid"),
            ReadNullableString(reader, "SubmissionToken"),
            ReadNullableString(reader, "RefundBusinessKey"));
    }

    private static Guid ReadGuid(SqliteDataReader reader, string name) => Guid.Parse(ReadString(reader, name));

    private static string ReadString(SqliteDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));

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

    private static long ReadInt64(SqliteDataReader reader, string name)
    {
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            string stringValue => long.Parse(stringValue, CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
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
