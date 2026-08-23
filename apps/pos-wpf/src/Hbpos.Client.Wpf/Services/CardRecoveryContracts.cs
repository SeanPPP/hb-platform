using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

// 恢复中心定点身份：provider + AttemptGuid 唯一定位一条未结 attempt。
public readonly record struct CardRecoveryAttemptKey(
    CardProcessorKind Processor,
    Guid AttemptGuid);

// 恢复队列条目：只读快照，供列表展示与定点恢复/结案使用。
public sealed record CardRecoveryQueueItem(
    CardProcessorKind Processor,
    Guid AttemptGuid,
    string OperationKind,
    decimal Amount,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string Environment,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? OrderDraftJson = null,
    string? SessionId = null,
    string? TxnRef = null,
    string? CheckoutId = null,
    string? ResponseCode = null,
    string? ResponseText = null,
    string? PaymentReference = null,
    string? PaymentId = null,
    Guid? OperationGuid = null,
    string? PaymentStatus = null)
{
    public CardRecoveryAttemptKey Key => new(Processor, AttemptGuid);
}

// 恢复中心统一的三态主管决定；仅作为定点结案命令，不落库、不是持久化状态枚举。
public enum CardRecoverySupervisorDecision
{
    ConfirmProcessed,
    ConfirmNotProcessed,
    ContinueWaiting
}

// 定点结案的统一结果。
public sealed record CardRecoveryResolutionResult(
    bool Succeeded,
    string Message,
    CardPaymentRecoveryResult? RecoveryResult = null,
    bool RetryAllowed = false,
    bool LockRetained = false,
    bool ResolutionPersisted = false,
    bool ResolutionApplied = false);

public static class CardRecoveryPhases
{
    public const string None = "None";
    public const string FinalizePending = "FinalizePending";
}
