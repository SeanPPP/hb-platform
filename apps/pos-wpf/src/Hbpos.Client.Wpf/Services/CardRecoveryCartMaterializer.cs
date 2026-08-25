using System.Text.Json;

namespace Hbpos.Client.Wpf.Services;

internal static class CardRecoveryCartMaterializer
{
    public static bool TryPrepare(
        string? orderDraftJson,
        JsonSerializerOptions jsonOptions,
        out CardPaymentOrderDraft? draft)
    {
        draft = null;
        try
        {
            if (string.IsNullOrWhiteSpace(orderDraftJson))
            {
                return false;
            }

            var candidate = JsonSerializer.Deserialize<CardPaymentOrderDraft>(orderDraftJson, jsonOptions);
            if (candidate is null ||
                candidate.OrderGuid == Guid.Empty ||
                candidate.Session is null ||
                candidate.CartSnapshot is null ||
                candidate.CurrentTenders is null)
            {
                return false;
            }

            // 中文注释：先在隔离购物车中完整物化并重新快照，语义错误不得触碰活动购物车。
            var recoveryCart = new PosCartService();
            recoveryCart.RestoreSnapshot(candidate.CartSnapshot);
            var normalizedSnapshot = recoveryCart.CreateSnapshot();
            if (normalizedSnapshot.Lines.Count == 0)
            {
                return false;
            }

            draft = candidate with
            {
                CartSnapshot = normalizedSnapshot,
                CurrentTenders = candidate.CurrentTenders.ToArray()
            };
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            draft = null;
            return false;
        }
    }
}
