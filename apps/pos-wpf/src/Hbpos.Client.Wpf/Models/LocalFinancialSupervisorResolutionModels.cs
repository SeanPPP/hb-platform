namespace Hbpos.Client.Wpf.Models;

public enum LocalFinancialSupervisorResolutionTarget
{
    CardRefund,
    InstallmentRefund,
    ActiveSession
}

public sealed record LocalFinancialSupervisorResolution(
    Guid ResolutionGuid,
    LocalFinancialSupervisorResolutionTarget Target,
    string Processor,
    string Environment,
    string StoreCode,
    string DeviceCode,
    Guid? AttemptGuid,
    Guid? RefundStepGuid,
    Guid? OperationGuid,
    string? SessionId,
    string Decision,
    string OperatorCashierId,
    string? OperatorUserGuid,
    string? OperatorName,
    string Reason,
    string? Evidence,
    string? FinancialReference,
    string? RetryReference,
    DateTimeOffset ResolvedAt,
    Guid AuditEventId,
    string AuditPayloadJson,
    DateTimeOffset? AuditPersistedAt = null);
