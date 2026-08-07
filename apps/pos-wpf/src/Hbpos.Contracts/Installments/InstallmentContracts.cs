using System.Text.Json.Serialization;
using Hbpos.Contracts.Orders;

namespace Hbpos.Contracts.Installments;

public enum InstallmentStatus
{
    Active = 1,
    PaidOff = 2,
    PickedUp = 3,
    Cancelled = 4
}

public enum InstallmentPaymentStatus
{
    Recorded = 1,
    Voided = 2
}

public enum InstallmentCancellationKind
{
    RefundCancel = 1,
    VoidCancel = 2
}

public enum InstallmentRepaymentClaimStatus
{
    Prepared = 1,
    ProviderPending = 2,
    Committed = 3,
    Released = 4,
    Declined = 5,
    Unknown = 6
}

public enum InstallmentRepaymentClaimResolveOutcome
{
    Released = 1,
    Declined = 2,
    Unknown = 3
}

public enum InstallmentCancelClaimStatus
{
    Prepared = 1,
    RefundPending = 2,
    Committed = 3,
    Released = 4,
    Declined = 5,
    Unknown = 6
}

public enum InstallmentCancelClaimResolveOutcome
{
    Released = 1,
    Declined = 2,
    Unknown = 3
}

public sealed record InstallmentRepaymentCapabilitiesResponse(
    bool RepaymentClaimsSupported,
    bool RepaymentClaimsRequired,
    bool CrossDeviceRepaymentEnabled,
    int PreparedClaimTtlSeconds,
    bool CancelClaimsSupported = true,
    bool CancelClaimsRequired = false,
    int CancelPreparedClaimTtlSeconds = 120,
    bool CrossDeviceCancelRefundEnabled = false,
    bool CrossDeviceVoidEnabled = false,
    bool CrossDevicePickupEnabled = false,
    bool CardRepaymentSupported = false,
    [property: JsonPropertyName("repaymentClaimPrepareProviderV1")]
    bool RepaymentClaimPrepareProviderV1 = false);

public sealed record InstallmentRepaymentClaimCreateRequest(
    Guid OperationGuid,
    Guid PaymentGuid,
    decimal Amount,
    PaymentMethodKind Method,
    string IdempotencyKey);

public sealed record InstallmentRepaymentClaimBeginProviderRequest(
    string Provider,
    string ProviderAttemptId);

public sealed record InstallmentRepaymentClaimPrepareProviderRequest(
    Guid PaymentGuid,
    decimal Amount,
    PaymentMethodKind Method,
    string IdempotencyKey,
    string Provider,
    string ProviderAttemptId);

public sealed record InstallmentRepaymentClaimResolveRequest(
    InstallmentRepaymentClaimResolveOutcome Outcome,
    bool CashNotCollectedConfirmed = false,
    string? ProviderAttemptId = null);

public sealed record InstallmentRepaymentClaimCommitRequest(
    string? Reference = null,
    string? ReservationToken = null,
    IReadOnlyList<CardTransactionDto>? CardTransactions = null);

public sealed record InstallmentRepaymentClaimDto(
    Guid InstallmentGuid,
    Guid OperationGuid,
    Guid PaymentGuid,
    decimal Amount,
    PaymentMethodKind Method,
    string IdempotencyKey,
    InstallmentRepaymentClaimStatus Status,
    string? Provider,
    string? ProviderAttemptId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    InstallmentAppendPaymentResponse? Commit = null,
    bool AlreadyExists = false);

public sealed record InstallmentCancelClaimCreateRequest(
    Guid OperationGuid,
    string IdempotencyKey,
    string? Reason,
    string RefundPlanFingerprint);

public sealed record InstallmentCancelClaimResolveRequest(
    InstallmentCancelClaimResolveOutcome Outcome,
    IReadOnlyList<InstallmentRefundPaymentCommandDto>? ApprovedRefunds = null);

public sealed record InstallmentCancelClaimCommitRequest(
    IReadOnlyList<InstallmentRefundPaymentCommandDto> Refunds);

public sealed record InstallmentCancelClaimCommitResponse(
    InstallmentDetailsDto Details,
    bool AlreadyCancelled);

public sealed record InstallmentCancelClaimDto(
    Guid InstallmentGuid,
    Guid OperationGuid,
    string IdempotencyKey,
    string RefundPlanFingerprint,
    InstallmentCancelClaimStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    InstallmentCancelClaimCommitResponse? Commit = null,
    bool AlreadyExists = false,
    string? OriginalDeviceCode = null,
    string? ExecutingDeviceCode = null);

public sealed record InstallmentLineDto(
    Guid InstallmentLineGuid,
    string ProductCode,
    string? ReferenceCode,
    string DisplayName,
    string LookupCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal ActualAmount,
    string? ItemNumber = null);

public sealed record InstallmentPaymentCommandDto(
    Guid PaymentGuid,
    PaymentMethodKind Method,
    decimal Amount,
    string? Reference,
    string? ReservationToken = null,
    IReadOnlyList<CardTransactionDto>? CardTransactions = null,
    string? IdempotencyKey = null);

public sealed record InstallmentCreateRequest(
    Guid InstallmentGuid,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    DateTimeOffset CreatedAt,
    decimal TotalAmount,
    decimal DownPaymentAmount,
    IReadOnlyList<InstallmentLineDto> Lines,
    InstallmentPaymentCommandDto DownPayment,
    string CustomerName,
    string CustomerPhone,
    string? Note = null);

public sealed record InstallmentCreateResponse(
    Guid InstallmentGuid,
    string InstallmentNumber,
    InstallmentStatus Status,
    decimal PaidAmount,
    decimal BalanceAmount,
    InstallmentDetailsDto Details,
    bool AlreadyExists = false,
    string? Message = null);

public sealed record InstallmentAppendPaymentRequest(
    Guid InstallmentGuid,
    Guid PaymentGuid,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    decimal Amount,
    PaymentMethodKind Method,
    string? Reference,
    string? ReservationToken = null,
    IReadOnlyList<CardTransactionDto>? CardTransactions = null,
    string? IdempotencyKey = null);

public sealed record InstallmentAppendPaymentResponse(
    Guid InstallmentGuid,
    Guid PaymentGuid,
    decimal PaidAmount,
    decimal BalanceAmount,
    InstallmentStatus Status,
    InstallmentDetailsDto Details,
    bool AlreadyRecorded = false,
    string? Message = null);

public sealed record InstallmentConfirmPickupRequest(
    Guid InstallmentGuid,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    DateTimeOffset ConfirmedAt,
    string? Note = null,
    Guid OperationGuid = default,
    string? IdempotencyKey = null);

public sealed record InstallmentConfirmPickupResponse(
    Guid InstallmentGuid,
    InstallmentStatus Status,
    DateTimeOffset PickedUpAt,
    InstallmentDetailsDto Details,
    bool AlreadyConfirmed = false);

public sealed record InstallmentRefundPaymentCommandDto(
    Guid PaymentGuid,
    PaymentMethodKind Method,
    decimal Amount,
    string? Reference,
    IReadOnlyList<CardTransactionDto>? CardTransactions = null,
    string? IdempotencyKey = null,
    Guid OriginalPaymentGuid = default);

public sealed record InstallmentCancelRequest(
    Guid InstallmentGuid,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    DateTimeOffset CancelledAt,
    IReadOnlyList<InstallmentRefundPaymentCommandDto> Refunds,
    string? Reason = null,
    string? IdempotencyKey = null);

public sealed record InstallmentCancelResponse(
    Guid InstallmentGuid,
    InstallmentStatus Status,
    InstallmentDetailsDto Details,
    bool AlreadyCancelled = false,
    string? Message = null);

public sealed record InstallmentVoidRequest(
    Guid InstallmentGuid,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    DateTimeOffset VoidedAt,
    string? Reason = null,
    string? IdempotencyKey = null,
    Guid OperationGuid = default);

public sealed record InstallmentVoidResponse(
    Guid InstallmentGuid,
    InstallmentStatus Status,
    InstallmentDetailsDto Details,
    bool AlreadyVoided = false,
    string? Message = null);

public sealed record InstallmentHistoryQueryRequest(
    string StoreCode,
    string? DeviceCode = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? Keyword = null,
    InstallmentStatus? Status = null,
    int Take = 100,
    int Skip = 0);

public sealed record InstallmentHistoryQueryResponse(
    IReadOnlyList<InstallmentSummaryDto> Orders);

public sealed record InstallmentSummaryDto(
    Guid InstallmentGuid,
    string InstallmentNumber,
    string StoreCode,
    string DeviceCode,
    string CashierName,
    string CustomerName,
    string CustomerPhone,
    DateTimeOffset CreatedAt,
    decimal TotalAmount,
    decimal DownPaymentAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    InstallmentStatus Status,
    DateTimeOffset UpdatedAt);

public sealed record InstallmentDetailsDto(
    Guid InstallmentGuid,
    string InstallmentNumber,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    string CustomerName,
    string CustomerPhone,
    DateTimeOffset CreatedAt,
    decimal TotalAmount,
    decimal MinimumDownPayment,
    decimal DownPaymentAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    InstallmentStatus Status,
    IReadOnlyList<InstallmentLineDto> Lines,
    IReadOnlyList<InstallmentPaymentDto> Payments,
    InstallmentPickupInfoDto? PickupInfo,
    InstallmentCancellationInfoDto? CancellationInfo = null,
    string? Note = null);

public sealed record InstallmentPaymentDto(
    Guid PaymentGuid,
    PaymentMethodKind Method,
    decimal Amount,
    string? Reference,
    InstallmentPaymentStatus Status,
    DateTimeOffset RecordedAt,
    string CashierId,
    string DeviceCode,
    IReadOnlyList<CardTransactionDto>? CardTransactions = null,
    string? IdempotencyKey = null,
    [property: JsonIgnore] string? ReservationToken = null,
    string? CashierName = null);

public sealed record InstallmentPickupInfoDto(
    DateTimeOffset PickedUpAt,
    string PickedUpBy,
    string? Note = null);

public sealed record InstallmentCancellationInfoDto(
    InstallmentCancellationKind Kind,
    DateTimeOffset CancelledAt,
    string CancelledBy,
    string? Reason = null,
    string? IdempotencyKey = null);
