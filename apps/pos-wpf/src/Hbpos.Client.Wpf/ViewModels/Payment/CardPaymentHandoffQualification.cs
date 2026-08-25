using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

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
        Equals(left.CashierSession, right.CashierSession);
}
