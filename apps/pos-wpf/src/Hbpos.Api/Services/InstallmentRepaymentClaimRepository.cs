using Hbpos.Api.Data;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using SqlSugar;

namespace Hbpos.Api.Services;

public sealed record InstallmentRepaymentClaimRecord(
    Guid InstallmentGuid,
    Guid OperationGuid,
    Guid PaymentGuid,
    string StoreCode,
    string ClaimantDeviceCode,
    string CashierId,
    string CashierName,
    decimal Amount,
    PaymentMethodKind Method,
    string IdempotencyKey,
    string Fingerprint,
    InstallmentRepaymentClaimStatus Status,
    string? Provider,
    string? ProviderAttemptId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? CommittedAtUtc,
    long Revision,
    string? CommitResponseJson = null,
    string? LastRecoveryCashierId = null,
    string? LastRecoveryCashierName = null,
    string? LastRecoveryCashierUserGuid = null,
    DateTimeOffset? RecoveredAtUtc = null);

public sealed record InstallmentRepaymentClaimInsertSnapshot(
    InstallmentStatus Status,
    decimal PaidAmount,
    decimal BalanceAmount);

public interface IInstallmentRepaymentClaimRepository
{
    Task<InstallmentRepaymentClaimRecord?> GetAsync(
        Guid operationGuid,
        CancellationToken cancellationToken);

    Task<InstallmentRepaymentClaimRecord?> GetBlockingAsync(
        Guid installmentGuid,
        CancellationToken cancellationToken);

    Task<bool> TryInsertAsync(
        InstallmentRepaymentClaimRecord claim,
        InstallmentRepaymentClaimInsertSnapshot snapshot,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateAsync(
        InstallmentRepaymentClaimRecord claim,
        long expectedRevision,
        CancellationToken cancellationToken);
}

public sealed class SqlSugarInstallmentRepaymentClaimRepository(
    HbposSqlSugarContext dbContext) : IInstallmentRepaymentClaimRepository
{
    private const string SelectColumns = """
        [InstallmentGuid], [OperationGuid], [PaymentGuid], [StoreCode], [ClaimantDeviceCode], [CashierId], [CashierName],
        [Amount], [Method], [IdempotencyKey], [Fingerprint], [Status], [Provider], [ProviderAttemptId],
        [CreatedAtUtc], [UpdatedAtUtc], [ExpiresAtUtc], [CommittedAtUtc], [Revision],
        [CommitResponseJson], [LastRecoveryCashierId], [LastRecoveryCashierName],
        [LastRecoveryCashierUserGuid], [RecoveredAtUtc]
        """;

    public async Task<InstallmentRepaymentClaimRecord?> GetAsync(
        Guid operationGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = $"""
            SELECT TOP 1 {SelectColumns}
            FROM [dbo].[POSM_InstallmentRepaymentClaim]
            WHERE [OperationGuid] = @OperationGuid;
            """;
        var row = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<ClaimRow>(
            sql,
            new SugarParameter("@OperationGuid", operationGuid));
        return row is null ? null : Map(row);
    }

    public async Task<InstallmentRepaymentClaimRecord?> GetBlockingAsync(
        Guid installmentGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = $"""
            SELECT TOP 1 {SelectColumns}
            FROM [dbo].[POSM_InstallmentRepaymentClaim]
            WHERE [InstallmentGuid] = @InstallmentGuid
              AND [IsBlocking] = 1
            ORDER BY [CreatedAtUtc], [OperationGuid];
            """;
        var row = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<ClaimRow>(
            sql,
            new SugarParameter("@InstallmentGuid", installmentGuid));
        return row is null ? null : Map(row);
    }

    public async Task<bool> TryInsertAsync(
        InstallmentRepaymentClaimRecord claim,
        InstallmentRepaymentClaimInsertSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = dbContext.PosmDb;
        await using var processLock = await InstallmentMutationLock.AcquireProcessAsync(
            claim.InstallmentGuid,
            cancellationToken);
        await db.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            await InstallmentMutationLock.AcquireDatabaseAsync(db, claim.InstallmentGuid);
            var order = await InstallmentMutationLock.LockOrderAsync(
                db,
                claim.InstallmentGuid,
                cancellationToken);
            if (order is null ||
                !string.Equals(order.StoreCode, claim.StoreCode, StringComparison.OrdinalIgnoreCase) ||
                order.Status != (int)InstallmentStatus.Active ||
                order.BalanceAmount <= 0m ||
                claim.Amount > order.BalanceAmount ||
                order.Status != (int)snapshot.Status ||
                order.PaidAmount != snapshot.PaidAmount ||
                order.BalanceAmount != snapshot.BalanceAmount)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            await InstallmentMutationLock.EnsureNoBlockingClaimAsync(
                db,
                claim.InstallmentGuid,
                cancellationToken);
            var inserted = await db.Insertable(MapEntity(claim)).ExecuteCommandAsync(cancellationToken) == 1;
            await db.Ado.CommitTranAsync();
            return inserted;
        }
        catch (InstallmentRepaymentClaimException ex)
            when (ex.Code == InstallmentRepaymentClaimErrorCodes.Busy)
        {
            await db.Ado.RollbackTranAsync();
            return false;
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            await db.Ado.RollbackTranAsync();
            return false;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<bool> TryUpdateAsync(
        InstallmentRepaymentClaimRecord claim,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string sql = """
            UPDATE [dbo].[POSM_InstallmentRepaymentClaim]
            SET [Status] = @Status,
                [IsBlocking] = @IsBlocking,
                [Provider] = @Provider,
                [ProviderAttemptId] = @ProviderAttemptId,
                [UpdatedAtUtc] = @UpdatedAtUtc,
                [ExpiresAtUtc] = @ExpiresAtUtc,
                [CommittedAtUtc] = @CommittedAtUtc,
                [CommitResponseJson] = @CommitResponseJson,
                [LastRecoveryCashierId] = @LastRecoveryCashierId,
                [LastRecoveryCashierName] = @LastRecoveryCashierName,
                [LastRecoveryCashierUserGuid] = @LastRecoveryCashierUserGuid,
                [RecoveredAtUtc] = @RecoveredAtUtc,
                [Revision] = @Revision
            WHERE [OperationGuid] = @OperationGuid
              AND [Revision] = @ExpectedRevision;
            """;
        var parameters = ToParameters(claim)
            .Append(new SugarParameter("@ExpectedRevision", expectedRevision))
            .ToArray();
        try
        {
            return await dbContext.PosmDb.Ado.ExecuteCommandAsync(sql, parameters) == 1;
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            return false;
        }
    }

    private static SugarParameter[] ToParameters(InstallmentRepaymentClaimRecord claim)
    {
        return
        [
            new("@InstallmentGuid", claim.InstallmentGuid),
            new("@OperationGuid", claim.OperationGuid),
            new("@PaymentGuid", claim.PaymentGuid),
            new("@StoreCode", claim.StoreCode),
            new("@ClaimantDeviceCode", claim.ClaimantDeviceCode),
            new("@CashierId", claim.CashierId),
            new("@CashierName", claim.CashierName),
            new("@Amount", claim.Amount),
            new("@Method", (int)claim.Method),
            new("@IdempotencyKey", claim.IdempotencyKey),
            new("@Fingerprint", claim.Fingerprint),
            new("@Status", claim.Status.ToString()),
            new("@IsBlocking", IsBlocking(claim.Status)),
            new("@Provider", claim.Provider),
            new("@ProviderAttemptId", claim.ProviderAttemptId),
            new("@CreatedAtUtc", claim.CreatedAtUtc.UtcDateTime),
            new("@UpdatedAtUtc", claim.UpdatedAtUtc.UtcDateTime),
            new("@ExpiresAtUtc", claim.ExpiresAtUtc?.UtcDateTime),
            new("@CommittedAtUtc", claim.CommittedAtUtc?.UtcDateTime),
            new("@CommitResponseJson", claim.CommitResponseJson),
            new("@LastRecoveryCashierId", claim.LastRecoveryCashierId),
            new("@LastRecoveryCashierName", claim.LastRecoveryCashierName),
            new("@LastRecoveryCashierUserGuid", claim.LastRecoveryCashierUserGuid),
            new("@RecoveredAtUtc", claim.RecoveredAtUtc?.UtcDateTime),
            new("@Revision", claim.Revision)
        ];
    }

    private static InstallmentRepaymentClaimEntity MapEntity(InstallmentRepaymentClaimRecord claim)
    {
        return new InstallmentRepaymentClaimEntity
        {
            InstallmentGuid = claim.InstallmentGuid,
            OperationGuid = claim.OperationGuid,
            PaymentGuid = claim.PaymentGuid,
            StoreCode = claim.StoreCode,
            ClaimantDeviceCode = claim.ClaimantDeviceCode,
            CashierId = claim.CashierId,
            CashierName = claim.CashierName,
            Amount = claim.Amount,
            Method = (int)claim.Method,
            IdempotencyKey = claim.IdempotencyKey,
            Fingerprint = claim.Fingerprint,
            Status = claim.Status.ToString(),
            IsBlocking = IsBlocking(claim.Status),
            Provider = claim.Provider,
            ProviderAttemptId = claim.ProviderAttemptId,
            CreatedAtUtc = claim.CreatedAtUtc.UtcDateTime,
            UpdatedAtUtc = claim.UpdatedAtUtc.UtcDateTime,
            ExpiresAtUtc = claim.ExpiresAtUtc?.UtcDateTime,
            CommittedAtUtc = claim.CommittedAtUtc?.UtcDateTime,
            CommitResponseJson = claim.CommitResponseJson,
            LastRecoveryCashierId = claim.LastRecoveryCashierId,
            LastRecoveryCashierName = claim.LastRecoveryCashierName,
            LastRecoveryCashierUserGuid = claim.LastRecoveryCashierUserGuid,
            RecoveredAtUtc = claim.RecoveredAtUtc?.UtcDateTime,
            Revision = claim.Revision
        };
    }

    private static InstallmentRepaymentClaimRecord Map(ClaimRow row)
    {
        return new InstallmentRepaymentClaimRecord(
            row.InstallmentGuid,
            row.OperationGuid,
            row.PaymentGuid,
            row.StoreCode,
            row.ClaimantDeviceCode,
            row.CashierId,
            row.CashierName,
            row.Amount,
            (PaymentMethodKind)row.Method,
            row.IdempotencyKey,
            row.Fingerprint,
            Enum.Parse<InstallmentRepaymentClaimStatus>(row.Status, ignoreCase: true),
            row.Provider,
            row.ProviderAttemptId,
            ToUtc(row.CreatedAtUtc),
            ToUtc(row.UpdatedAtUtc),
            row.ExpiresAtUtc is null ? null : ToUtc(row.ExpiresAtUtc.Value),
            row.CommittedAtUtc is null ? null : ToUtc(row.CommittedAtUtc.Value),
            row.Revision,
            row.CommitResponseJson,
            row.LastRecoveryCashierId,
            row.LastRecoveryCashierName,
            row.LastRecoveryCashierUserGuid,
            row.RecoveredAtUtc is null ? null : ToUtc(row.RecoveredAtUtc.Value));
    }

    private static bool IsBlocking(InstallmentRepaymentClaimStatus status) =>
        status is InstallmentRepaymentClaimStatus.Prepared
            or InstallmentRepaymentClaimStatus.ProviderPending
            or InstallmentRepaymentClaimStatus.Unknown;

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("2601", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("2627", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("UX_POSM_InstallmentRepaymentClaim", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ClaimRow
    {
        public Guid InstallmentGuid { get; set; }

        public Guid OperationGuid { get; set; }

        public Guid PaymentGuid { get; set; }

        public string StoreCode { get; set; } = string.Empty;

        public string ClaimantDeviceCode { get; set; } = string.Empty;

        public string CashierId { get; set; } = string.Empty;

        public string CashierName { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public int Method { get; set; }

        public string IdempotencyKey { get; set; } = string.Empty;

        public string Fingerprint { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? Provider { get; set; }

        public string? ProviderAttemptId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }

        public DateTime? CommittedAtUtc { get; set; }

        public long Revision { get; set; }

        public string? CommitResponseJson { get; set; }

        public string? LastRecoveryCashierId { get; set; }

        public string? LastRecoveryCashierName { get; set; }

        public string? LastRecoveryCashierUserGuid { get; set; }

        public DateTime? RecoveredAtUtc { get; set; }
    }
}
