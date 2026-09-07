using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Client.Wpf.ViewModels;

internal static class CardPaymentHandoffQualification
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CardPaymentHandoffCandidate? SelectCandidate(
        IReadOnlyList<CardRecoveryQueueItem> openAttempts,
        CardPaymentHandoffRequest request)
    {
        if (request.RecoveryAttemptKey is not { AttemptGuid: var attemptGuid } key ||
            attemptGuid == Guid.Empty ||
            request.RecoveryOrderGuid is not { } orderGuid ||
            orderGuid == Guid.Empty)
        {
            return null;
        }

        var matches = openAttempts
            .Where(item =>
                item.Processor == key.Processor &&
                item.AttemptGuid == key.AttemptGuid &&
                IsMatchingAttempt(item, request))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? new CardPaymentHandoffCandidate(matches[0].Processor, matches[0].AttemptGuid)
            : null;
    }

    public static bool CandidateStillMatches(
        IReadOnlyList<CardRecoveryQueueItem> openAttempts,
        CardPaymentHandoffCandidate candidate,
        CardPaymentHandoffRequest request) =>
        openAttempts.Any(item =>
            item.Processor == candidate.Processor &&
            item.AttemptGuid == candidate.AttemptGuid &&
            IsMatchingAttempt(item, request));

    private static bool IsMatchingAttempt(
        CardRecoveryQueueItem item,
        CardPaymentHandoffRequest request)
    {
        if (item.AttemptGuid == Guid.Empty ||
            request.RecoveryAttemptKey is not { } key ||
            request.RecoveryOrderGuid is not { } expectedOrderGuid ||
            item.Processor != key.Processor ||
            item.AttemptGuid != key.AttemptGuid ||
            string.Equals(item.OperationKind, "ActiveSession", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(item.OrderDraftJson) ||
            !string.Equals(item.StoreCode, request.Session.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(item.DeviceCode, request.Session.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CardPaymentOrderDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(item.OrderDraftJson, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (draft is null ||
            draft.OrderGuid == Guid.Empty ||
            draft.OrderGuid != expectedOrderGuid ||
            draft.Session is null ||
            draft.CartSnapshot?.Lines is not { Count: > 0 } ||
            draft.CurrentTenders is null ||
            string.IsNullOrWhiteSpace(draft.TxnType) ||
            draft.CreatedAt == default)
        {
            return false;
        }

        // 资格只属于触发未知结果的当前订单；任何会话、购物车或既有 tender 漂移都拒绝移交。
        return SessionsMatch(draft.Session, request.Session) &&
            draft.ActualAmount == request.ActualAmount &&
            draft.CartSnapshot.SharedHeldOrderClaimId == request.CartSnapshot.SharedHeldOrderClaimId &&
            draft.CartSnapshot.Lines.SequenceEqual(request.CartSnapshot.Lines) &&
            draft.CurrentTenders.SequenceEqual(request.CurrentTenders);
    }

    private static bool SessionsMatch(PosSessionState left, PosSessionState right) =>
        string.Equals(left.StoreCode, right.StoreCode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.DeviceCode, right.DeviceCode, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.CashierId, right.CashierId, StringComparison.Ordinal) &&
        CashierSessionsMatch(left.CashierSession, right.CashierSession);

    private static bool CashierSessionsMatch(CashierSessionDto? left, CashierSessionDto? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        // JSON 往返会创建新的数组实例；按内容比较，仍严格拒绝任何收银身份或授权漂移。
        return string.Equals(left.CashierId, right.CashierId, StringComparison.Ordinal) &&
            string.Equals(left.UserGuid, right.UserGuid, StringComparison.Ordinal) &&
            string.Equals(left.CashierName, right.CashierName, StringComparison.Ordinal) &&
            string.Equals(left.StoreCode, right.StoreCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.DeviceCode, right.DeviceCode, StringComparison.OrdinalIgnoreCase) &&
            SequencesMatch(left.Roles, right.Roles) &&
            SequencesMatch(left.PermissionCodes, right.PermissionCodes) &&
            SequencesMatch(left.AllowedStoreCodes, right.AllowedStoreCodes) &&
            left.IsSuperAdmin == right.IsSuperAdmin &&
            left.IsOfflineCached == right.IsOfflineCached &&
            left.IsEmergencyOverride == right.IsEmergencyOverride &&
            string.Equals(left.AuthorizationToken, right.AuthorizationToken, StringComparison.Ordinal) &&
            left.AuthorizationExpiresAtUtc == right.AuthorizationExpiresAtUtc &&
            string.Equals(left.EmergencyGrantId, right.EmergencyGrantId, StringComparison.Ordinal);
    }

    private static bool SequencesMatch(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right) =>
        ReferenceEquals(left, right) ||
        (left is not null && right is not null && left.SequenceEqual(right, StringComparer.Ordinal));
}
