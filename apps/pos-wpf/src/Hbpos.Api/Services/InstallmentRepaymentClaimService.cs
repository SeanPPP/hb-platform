using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.Constants;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public static class InstallmentRepaymentClaimErrorCodes
{
    public const string Busy = "INSTALLMENT_REPAYMENT_BUSY";
    public const string ClaimRequired = "INSTALLMENT_REPAYMENT_CLAIM_REQUIRED";
    public const string Mismatch = "INSTALLMENT_REPAYMENT_CLAIM_MISMATCH";
    public const string NotFound = "INSTALLMENT_REPAYMENT_CLAIM_NOT_FOUND";
    public const string Expired = "INSTALLMENT_REPAYMENT_CLAIM_EXPIRED";
    public const string Invalid = "INSTALLMENT_REPAYMENT_CLAIM_INVALID";
    public const string PermissionDenied = "INSTALLMENT_REPAYMENT_PERMISSION_DENIED";
}

public sealed class InstallmentRepaymentClaimException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class InstallmentRepaymentClaimOptions
{
    public bool Required { get; set; }

    public bool CrossDeviceEnabled { get; set; }
}

public sealed record InstallmentRepaymentClaimIdentity(
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    IReadOnlyCollection<string>? PermissionCodes = null,
    string? CashierUserGuid = null);

public interface IInstallmentRepaymentClaimService
{
    InstallmentRepaymentCapabilitiesResponse GetCapabilities();

    Task<InstallmentRepaymentClaimDto> CreateAsync(
        Guid installmentGuid,
        InstallmentRepaymentClaimCreateRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken);

    Task<InstallmentRepaymentClaimDto> BeginProviderAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimBeginProviderRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken);

    Task<InstallmentRepaymentClaimDto> GetAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken);

    Task<InstallmentRepaymentClaimDto> ResolveAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimResolveRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken);

    Task<InstallmentRepaymentClaimDto> CommitAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimCommitRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken);

    Task EnsureLegacyAppendAllowedAsync(Guid installmentGuid, CancellationToken cancellationToken);

    Task EnsureNoBlockingClaimAsync(Guid installmentGuid, CancellationToken cancellationToken);
}

public sealed class InstallmentRepaymentClaimService(
    IInstallmentRepaymentClaimRepository claimRepository,
    IInstallmentRepository installmentRepository,
    IInstallmentRepaymentClaimCommitRepository commitRepository,
    IOptions<InstallmentRepaymentClaimOptions> options,
    TimeProvider? timeProvider = null,
    IOptions<InstallmentCancelClaimOptions>? cancelOptions = null) : IInstallmentRepaymentClaimService
{
    public const int PreparedClaimTtlSeconds = 120;
    private readonly InstallmentRepaymentClaimOptions _options = options.Value;
    private readonly InstallmentCancelClaimOptions _cancelOptions =
        cancelOptions?.Value ?? new InstallmentCancelClaimOptions();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public InstallmentRepaymentCapabilitiesResponse GetCapabilities()
    {
        return new InstallmentRepaymentCapabilitiesResponse(
            RepaymentClaimsSupported: true,
            RepaymentClaimsRequired: _options.Required,
            CrossDeviceRepaymentEnabled: _options.CrossDeviceEnabled,
            PreparedClaimTtlSeconds,
            CancelClaimsSupported: true,
            CancelClaimsRequired: _cancelOptions.Required,
            CancelPreparedClaimTtlSeconds: InstallmentCancelClaimService.PreparedClaimTtlSeconds);
    }

    public async Task<InstallmentRepaymentClaimDto> CreateAsync(
        Guid installmentGuid,
        InstallmentRepaymentClaimCreateRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken)
    {
        var normalizedIdentity = NormalizeIdentity(identity);
        var normalizedRequest = NormalizeCreateRequest(request);
        ValidatePaymentPermissions(normalizedIdentity, normalizedRequest.Method);
        var details = await GetRequiredInstallmentAsync(installmentGuid, cancellationToken);
        ValidateInstallmentForNewClaim(details, normalizedIdentity, normalizedRequest.Amount);
        var fingerprint = CreateFingerprint(installmentGuid, normalizedRequest);

        var existing = await claimRepository.GetAsync(normalizedRequest.OperationGuid, cancellationToken);
        if (existing is not null)
        {
            existing = await ExpirePreparedAsync(existing, cancellationToken);
            ValidateAccess(existing, installmentGuid, normalizedIdentity);
            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw Mismatch("operationGuid is already bound to different repayment facts.");
            }

            return await MapAsync(existing, alreadyExists: true);
        }

        await EnsureNoBlockingClaimAsync(installmentGuid, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var claim = new InstallmentRepaymentClaimRecord(
            installmentGuid,
            normalizedRequest.OperationGuid,
            normalizedRequest.PaymentGuid,
            normalizedIdentity.StoreCode,
            normalizedIdentity.DeviceCode,
            normalizedIdentity.CashierId,
            normalizedIdentity.CashierName,
            normalizedRequest.Amount,
            normalizedRequest.Method,
            normalizedRequest.IdempotencyKey,
            fingerprint,
            InstallmentRepaymentClaimStatus.Prepared,
            Provider: null,
            ProviderAttemptId: null,
            now,
            now,
            now.AddSeconds(PreparedClaimTtlSeconds),
            CommittedAtUtc: null,
            Revision: 1);
        var insertSnapshot = new InstallmentRepaymentClaimInsertSnapshot(
            details.Status,
            details.PaidAmount,
            details.BalanceAmount);
        if (await claimRepository.TryInsertAsync(claim, insertSnapshot, cancellationToken))
        {
            return await MapAsync(claim, alreadyExists: false);
        }

        // 唯一键竞争可能是同一 operation 的安全重试，也可能是同一分期的另一个 blocking claim。
        existing = await claimRepository.GetAsync(normalizedRequest.OperationGuid, cancellationToken);
        if (existing is not null)
        {
            ValidateAccess(existing, installmentGuid, normalizedIdentity);
            if (string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return await MapAsync(existing, alreadyExists: true);
            }

            throw Mismatch("operationGuid is already bound to different repayment facts.");
        }

        throw Busy();
    }

    public async Task<InstallmentRepaymentClaimDto> BeginProviderAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimBeginProviderRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken)
    {
        var provider = NormalizeRequired(request.Provider, "provider");
        if (provider.Length > 32)
        {
            throw Invalid("provider must not exceed 32 characters.");
        }

        var providerAttemptId = NormalizeRequired(request.ProviderAttemptId, "providerAttemptId");
        if (providerAttemptId.Length > 128)
        {
            throw Invalid("providerAttemptId must not exceed 128 characters.");
        }
        var current = await GetRequiredAccessibleClaimAsync(installmentGuid, operationGuid, identity, cancellationToken);
        current = await ExpirePreparedAsync(current, cancellationToken);
        InstallmentRepaymentClaimCommitEvidenceValidator.ValidateProviderForMethod(current.Method, provider);
        var currentIdentity = NormalizeIdentity(identity);
        if (current.Status == InstallmentRepaymentClaimStatus.Prepared &&
            !string.Equals(current.CashierId, currentIdentity.CashierId, StringComparison.OrdinalIgnoreCase))
        {
            throw Mismatch("A prepared repayment claim can only begin under its creating cashier; release it before changing cashier.");
        }
        if (current.Status == InstallmentRepaymentClaimStatus.Released && current.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.Expired,
                "Prepared repayment claim has expired.");
        }

        if (current.Status is InstallmentRepaymentClaimStatus.ProviderPending
            or InstallmentRepaymentClaimStatus.Unknown)
        {
            if (string.Equals(current.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.ProviderAttemptId, providerAttemptId, StringComparison.Ordinal))
            {
                if (current.Status == InstallmentRepaymentClaimStatus.ProviderPending)
                {
                    current = await RecordRecoveryCashierAsync(current, currentIdentity, cancellationToken);
                    return await MapAsync(current, alreadyExists: true);
                }

                var resumed = current with
                {
                    Status = InstallmentRepaymentClaimStatus.ProviderPending,
                    UpdatedAtUtc = _timeProvider.GetUtcNow(),
                    LastRecoveryCashierId = currentIdentity.CashierId,
                    LastRecoveryCashierName = currentIdentity.CashierName,
                    LastRecoveryCashierUserGuid = currentIdentity.CashierUserGuid,
                    RecoveredAtUtc = _timeProvider.GetUtcNow(),
                    Revision = current.Revision + 1
                };
                resumed = await UpdateOrReloadAsync(current, resumed, cancellationToken);
                if (resumed.Status != InstallmentRepaymentClaimStatus.ProviderPending)
                {
                    throw Mismatch("Repayment claim changed while resuming provider recovery.");
                }

                return await MapAsync(resumed, alreadyExists: false);
            }

            throw Mismatch("Provider attempt does not match the existing claim.");
        }

        if (current.Status != InstallmentRepaymentClaimStatus.Prepared)
        {
            throw Mismatch("Only a prepared repayment claim can begin a provider attempt.");
        }

        var updated = current with
        {
            Status = InstallmentRepaymentClaimStatus.ProviderPending,
            Provider = provider,
            ProviderAttemptId = providerAttemptId,
            UpdatedAtUtc = _timeProvider.GetUtcNow(),
            ExpiresAtUtc = null,
            LastRecoveryCashierId = currentIdentity.CashierId,
            LastRecoveryCashierName = currentIdentity.CashierName,
            LastRecoveryCashierUserGuid = currentIdentity.CashierUserGuid,
            RecoveredAtUtc = _timeProvider.GetUtcNow(),
            Revision = current.Revision + 1
        };
        updated = await UpdateOrReloadAsync(current, updated, cancellationToken);
        if (updated.Status != InstallmentRepaymentClaimStatus.ProviderPending ||
            !string.Equals(updated.Provider, provider, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(updated.ProviderAttemptId, providerAttemptId, StringComparison.Ordinal))
        {
            throw Mismatch("Repayment claim changed while beginning the provider attempt.");
        }

        return await MapAsync(updated, alreadyExists: false);
    }

    public async Task<InstallmentRepaymentClaimDto> GetAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken)
    {
        var current = await GetRequiredAccessibleClaimAsync(installmentGuid, operationGuid, identity, cancellationToken);
        current = await ExpirePreparedAsync(current, cancellationToken);
        return await MapAsync(current, alreadyExists: true);
    }

    public async Task<InstallmentRepaymentClaimDto> ResolveAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimResolveRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken)
    {
        var current = await GetRequiredAccessibleClaimAsync(installmentGuid, operationGuid, identity, cancellationToken);
        current = await ExpirePreparedAsync(current, cancellationToken);
        var targetStatus = request.Outcome switch
        {
            InstallmentRepaymentClaimResolveOutcome.Released => InstallmentRepaymentClaimStatus.Released,
            InstallmentRepaymentClaimResolveOutcome.Declined => InstallmentRepaymentClaimStatus.Declined,
            InstallmentRepaymentClaimResolveOutcome.Unknown => InstallmentRepaymentClaimStatus.Unknown,
            _ => throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.Invalid,
                "Unsupported repayment claim outcome.")
        };
        if (current.Status == targetStatus)
        {
            return await MapAsync(current, alreadyExists: true);
        }

        var validTransition = request.Outcome switch
        {
            InstallmentRepaymentClaimResolveOutcome.Released =>
                current.Status == InstallmentRepaymentClaimStatus.Prepared,
            InstallmentRepaymentClaimResolveOutcome.Declined =>
                current.Status == InstallmentRepaymentClaimStatus.ProviderPending,
            InstallmentRepaymentClaimResolveOutcome.Unknown =>
                current.Status == InstallmentRepaymentClaimStatus.ProviderPending,
            _ => false
        };
        if (!validTransition)
        {
            throw Mismatch(request.Outcome == InstallmentRepaymentClaimResolveOutcome.Released
                ? "Only a prepared claim can be released."
                : "Only a provider-pending claim can be declined or marked unknown.");
        }

        var recoveryIdentity = NormalizeIdentity(identity);
        var updated = current with
        {
            Status = targetStatus,
            UpdatedAtUtc = _timeProvider.GetUtcNow(),
            ExpiresAtUtc = null,
            LastRecoveryCashierId = recoveryIdentity.CashierId,
            LastRecoveryCashierName = recoveryIdentity.CashierName,
            LastRecoveryCashierUserGuid = recoveryIdentity.CashierUserGuid,
            RecoveredAtUtc = _timeProvider.GetUtcNow(),
            Revision = current.Revision + 1
        };
        updated = await UpdateOrReloadAsync(current, updated, cancellationToken);
        if (updated.Status != targetStatus)
        {
            throw Mismatch("Repayment claim changed while resolving the provider outcome.");
        }

        return await MapAsync(updated, alreadyExists: false);
    }

    public async Task<InstallmentRepaymentClaimDto> CommitAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimCommitRequest request,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken)
    {
        var current = await GetRequiredAccessibleClaimAsync(installmentGuid, operationGuid, identity, cancellationToken);
        if (current.Status == InstallmentRepaymentClaimStatus.Committed)
        {
            current = await RecordRecoveryCashierAsync(
                current,
                NormalizeIdentity(identity),
                cancellationToken);
            return await MapAsync(current, alreadyExists: true);
        }

        if (current.Status != InstallmentRepaymentClaimStatus.ProviderPending)
        {
            throw Mismatch("Only a provider-pending repayment claim can be committed.");
        }

        InstallmentRepaymentClaimCommitEvidenceValidator.Validate(current, request);

        var result = await commitRepository.CommitAsync(
            current,
            request,
            NormalizeIdentity(identity),
            _timeProvider.GetUtcNow(),
            cancellationToken);
        return Map(
            result.Claim,
            result.CommitResponse,
            alreadyExists: result.AlreadyRecorded);
    }

    public async Task EnsureLegacyAppendAllowedAsync(
        Guid installmentGuid,
        CancellationToken cancellationToken)
    {
        if (_options.Required)
        {
            throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.ClaimRequired,
                "This server requires a repayment claim before committing an installment payment.");
        }

        await EnsureNoBlockingClaimAsync(installmentGuid, cancellationToken);
    }

    public async Task EnsureNoBlockingClaimAsync(
        Guid installmentGuid,
        CancellationToken cancellationToken)
    {
        var blocking = await claimRepository.GetBlockingAsync(installmentGuid, cancellationToken);
        if (blocking is null)
        {
            return;
        }

        blocking = await ExpirePreparedAsync(blocking, cancellationToken);
        if (IsBlocking(blocking.Status))
        {
            throw Busy();
        }
    }

    private async Task<InstallmentRepaymentClaimRecord> GetRequiredAccessibleClaimAsync(
        Guid installmentGuid,
        Guid operationGuid,
        InstallmentRepaymentClaimIdentity identity,
        CancellationToken cancellationToken)
    {
        if (operationGuid == Guid.Empty)
        {
            throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.Invalid,
                "operationGuid is required.");
        }

        var normalizedIdentity = NormalizeIdentity(identity);
        var current = await claimRepository.GetAsync(operationGuid, cancellationToken)
            ?? throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.NotFound,
                "Repayment claim was not found.");
        ValidateAccess(current, installmentGuid, normalizedIdentity);
        ValidatePaymentPermissions(normalizedIdentity, current.Method);
        return current;
    }

    private async Task<InstallmentRepaymentClaimRecord> ExpirePreparedAsync(
        InstallmentRepaymentClaimRecord current,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (current.Status != InstallmentRepaymentClaimStatus.Prepared ||
            current.ExpiresAtUtc is null ||
            current.ExpiresAtUtc > now)
        {
            return current;
        }

        var released = current with
        {
            Status = InstallmentRepaymentClaimStatus.Released,
            UpdatedAtUtc = now,
            Revision = current.Revision + 1
        };
        return await UpdateOrReloadAsync(current, released, cancellationToken);
    }

    private async Task<InstallmentRepaymentClaimRecord> UpdateOrReloadAsync(
        InstallmentRepaymentClaimRecord current,
        InstallmentRepaymentClaimRecord updated,
        CancellationToken cancellationToken)
    {
        if (await claimRepository.TryUpdateAsync(updated, current.Revision, cancellationToken))
        {
            return updated;
        }

        return await claimRepository.GetAsync(current.OperationGuid, cancellationToken)
            ?? throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.NotFound,
                "Repayment claim disappeared during an update.");
    }

    private async Task<InstallmentDetailsDto> GetRequiredInstallmentAsync(
        Guid installmentGuid,
        CancellationToken cancellationToken)
    {
        return await installmentRepository.GetDetailsAsync(installmentGuid, cancellationToken)
            ?? throw new InstallmentRepaymentClaimException(
                InstallmentRepaymentClaimErrorCodes.NotFound,
                "Installment was not found.");
    }

    private void ValidateInstallmentForNewClaim(
        InstallmentDetailsDto details,
        InstallmentRepaymentClaimIdentity identity,
        decimal amount)
    {
        if (!string.Equals(details.StoreCode, identity.StoreCode, StringComparison.OrdinalIgnoreCase))
        {
            throw Mismatch("Installment does not belong to the authenticated store.");
        }

        if (!_options.CrossDeviceEnabled &&
            !string.Equals(details.DeviceCode, identity.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw Mismatch("Cross-device installment repayment is disabled.");
        }

        if (details.Status != InstallmentStatus.Active || details.BalanceAmount <= 0m)
        {
            throw Mismatch("Only active unpaid installments can start a repayment claim.");
        }

        if (amount > details.BalanceAmount)
        {
            throw Mismatch("Repayment claim amount cannot exceed the installment balance.");
        }
    }

    private void ValidateAccess(
        InstallmentRepaymentClaimRecord claim,
        Guid installmentGuid,
        InstallmentRepaymentClaimIdentity identity)
    {
        if (claim.InstallmentGuid != installmentGuid ||
            !string.Equals(claim.StoreCode, identity.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(claim.ClaimantDeviceCode, identity.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw Mismatch("Authenticated identity does not match the repayment claim.");
        }
    }

    private Task<InstallmentRepaymentClaimDto> MapAsync(
        InstallmentRepaymentClaimRecord claim,
        bool alreadyExists)
    {
        InstallmentAppendPaymentResponse? commit = null;
        if (claim.Status == InstallmentRepaymentClaimStatus.Committed)
        {
            if (string.IsNullOrWhiteSpace(claim.CommitResponseJson))
            {
                throw Mismatch("Committed claim has no persisted commit response.");
            }

            try
            {
                commit = JsonSerializer.Deserialize<InstallmentAppendPaymentResponse>(
                    claim.CommitResponseJson,
                    InstallmentRepaymentClaimCommitRepositoryJson.Options);
            }
            catch (JsonException)
            {
                throw Mismatch("Persisted repayment commit response is invalid.");
            }

            if (commit is null ||
                commit.InstallmentGuid != claim.InstallmentGuid ||
                commit.PaymentGuid != claim.PaymentGuid)
            {
                throw Mismatch("Persisted repayment commit response does not match the claim.");
            }
        }

        return Task.FromResult(Map(claim, commit, alreadyExists));
    }

    private static InstallmentRepaymentClaimDto Map(
        InstallmentRepaymentClaimRecord claim,
        InstallmentAppendPaymentResponse? commit,
        bool alreadyExists)
    {
        return new InstallmentRepaymentClaimDto(
            claim.InstallmentGuid,
            claim.OperationGuid,
            claim.PaymentGuid,
            claim.Amount,
            claim.Method,
            claim.IdempotencyKey,
            claim.Status,
            claim.Provider,
            claim.ProviderAttemptId,
            claim.CreatedAtUtc,
            claim.UpdatedAtUtc,
            claim.ExpiresAtUtc,
            commit,
            alreadyExists);
    }

    private static InstallmentRepaymentClaimCreateRequest NormalizeCreateRequest(
        InstallmentRepaymentClaimCreateRequest request)
    {
        if (request.OperationGuid == Guid.Empty)
        {
            throw Invalid("operationGuid is required.");
        }

        if (request.PaymentGuid == Guid.Empty)
        {
            throw Invalid("paymentGuid is required.");
        }

        var amount = RoundCurrency(request.Amount);
        if (amount <= 0m)
        {
            throw Invalid("amount must be greater than zero.");
        }

        if (!Enum.IsDefined(request.Method))
        {
            throw Invalid("method is invalid.");
        }

        return request with
        {
            Amount = amount,
            IdempotencyKey = NormalizeRequired(request.IdempotencyKey, "idempotencyKey", 100)
        };
    }

    private static InstallmentRepaymentClaimIdentity NormalizeIdentity(
        InstallmentRepaymentClaimIdentity identity)
    {
        return new InstallmentRepaymentClaimIdentity(
            NormalizeRequired(identity.StoreCode, "authenticated storeCode", 50),
            NormalizeRequired(identity.DeviceCode, "authenticated deviceCode", 50),
            NormalizeRequired(identity.CashierId, "verified cashierId", 50),
            NormalizeRequired(identity.CashierName, "verified cashierName", 100),
            identity.PermissionCodes?
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            NormalizeRequired(identity.CashierUserGuid, "verified cashierUserGuid", 50));
    }

    private static string CreateFingerprint(
        Guid installmentGuid,
        InstallmentRepaymentClaimCreateRequest request)
    {
        var material = string.Join(
            "|",
            installmentGuid.ToString("D"),
            request.OperationGuid.ToString("D"),
            request.PaymentGuid.ToString("D"),
            request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            ((int)request.Method).ToString(CultureInfo.InvariantCulture),
            request.IdempotencyKey);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static bool IsBlocking(InstallmentRepaymentClaimStatus status) =>
        status is InstallmentRepaymentClaimStatus.Prepared
            or InstallmentRepaymentClaimStatus.ProviderPending
            or InstallmentRepaymentClaimStatus.Unknown;

    private static InstallmentRepaymentClaimException Busy() => new(
        InstallmentRepaymentClaimErrorCodes.Busy,
        "Another repayment claim is blocking this installment.");

    private static InstallmentRepaymentClaimException Mismatch(string message) => new(
        InstallmentRepaymentClaimErrorCodes.Mismatch,
        message);

    private static InstallmentRepaymentClaimException Invalid(string message) => new(
        InstallmentRepaymentClaimErrorCodes.Invalid,
        message);

    private static InstallmentRepaymentClaimException PermissionDenied(string message) => new(
        InstallmentRepaymentClaimErrorCodes.PermissionDenied,
        message);

    private static string NormalizeRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid($"{fieldName} is required.");
        }

        return value.Trim();
    }

    private static string NormalizeRequired(string? value, string fieldName, int maxLength)
    {
        var normalized = NormalizeRequired(value, fieldName);
        if (normalized.Length > maxLength)
        {
            throw Invalid($"{fieldName} must not exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static void ValidatePaymentPermissions(
        InstallmentRepaymentClaimIdentity identity,
        PaymentMethodKind method)
    {
        var permissions = identity.PermissionCodes?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var methodPermission = method switch
        {
            PaymentMethodKind.Cash => Permissions.PosTerminal.Payment.TakeCash,
            PaymentMethodKind.Card => Permissions.PosTerminal.Payment.TakeCard,
            PaymentMethodKind.Voucher => Permissions.PosTerminal.Payment.TakeVoucher,
            _ => throw Invalid("method is invalid.")
        };
        if (permissions is null ||
            !permissions.Contains(Permissions.PosTerminal.Installments.AddRepayment) ||
            !permissions.Contains(Permissions.PosTerminal.Payment.Confirm) ||
            !permissions.Contains(methodPermission))
        {
            throw PermissionDenied("Verified cashier lacks the required payment permissions.");
        }
    }

    private async Task<InstallmentRepaymentClaimRecord> RecordRecoveryCashierAsync(
        InstallmentRepaymentClaimRecord current,
        InstallmentRepaymentClaimIdentity normalizedIdentity,
        CancellationToken cancellationToken)
    {
        if (string.Equals(current.LastRecoveryCashierId, normalizedIdentity.CashierId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.LastRecoveryCashierName, normalizedIdentity.CashierName, StringComparison.Ordinal) &&
            string.Equals(current.LastRecoveryCashierUserGuid, normalizedIdentity.CashierUserGuid, StringComparison.OrdinalIgnoreCase) &&
            current.RecoveredAtUtc is not null)
        {
            return current;
        }

        var updated = current with
        {
            LastRecoveryCashierId = normalizedIdentity.CashierId,
            LastRecoveryCashierName = normalizedIdentity.CashierName,
            LastRecoveryCashierUserGuid = normalizedIdentity.CashierUserGuid,
            RecoveredAtUtc = _timeProvider.GetUtcNow(),
            UpdatedAtUtc = _timeProvider.GetUtcNow(),
            Revision = current.Revision + 1
        };
        return await UpdateOrReloadAsync(current, updated, cancellationToken);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal RoundCurrency(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
