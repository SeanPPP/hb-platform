using System.Globalization;
using System.IO;
using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.Services;

/// <summary>
/// 取单完成来源解析：显式 cart claim binding 是唯一身份事实；召回后的数量、价格、
/// 折扣或行编辑不能把正式订单降级成普通订单。只有没有 binding 的普通购物车返回 null；
/// binding 对应的 durable claim 缺失、重复、跨 scope、非 Active 或已绑定时一律抛错阻断。
/// </summary>
public interface ISharedHeldOrderPaymentSourceResolver
{
    Task<LocalHeldOrderCompletionContext?> TryResolveAsync(
        PosSessionState session,
        PosCartSnapshot cartSnapshot,
        CancellationToken cancellationToken = default);
}

public sealed class SharedHeldOrderPaymentSourceResolver(
    ISharedHeldOrderRepository sharedHeldOrderRepository,
    ISharedHeldOrderReverseMapper reverseMapper) : ISharedHeldOrderPaymentSourceResolver
{
    public async Task<LocalHeldOrderCompletionContext?> TryResolveAsync(
        PosSessionState session,
        PosCartSnapshot cartSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cartSnapshot);
        if (cartSnapshot.SharedHeldOrderClaimId is not Guid boundClaimId)
        {
            // 只有 coordinator 恢复时写入的显式 cart binding 才能关联来源；
            // 内容相同但由人工重建的购物车绝不能凭快照猜测 claim。
            return null;
        }

        // 保留构造契约，reverse mapper 仍由同一 DI 组合提供；付款绑定只信 durable 身份事实，
        // 不再重放/比较原 payload，否则召回后的合法编辑会静默丢失来源。
        _ = reverseMapper;

        var claims = await sharedHeldOrderRepository.FindRecoverableClaimsAsync(
            session.StoreCode,
            session.DeviceCode,
            cancellationToken);

        var matches = claims
            .Where(claim => claim.ClaimId == boundClaimId)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                matches.Length == 0
                    ? "共享挂单购物车 binding 缺少 durable claim，拒绝付款。"
                    : "共享挂单购物车 binding 对应多个 durable claim，拒绝付款。");
        }

        var match = matches[0];
        // 仓库已按 store+device 查询；这里再防御性核验，防止未来查询语义变化导致跨终端误绑。
        if (!string.Equals(match.StoreCode, session.StoreCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(match.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase)
            || match.Status != SharedHeldOrderClaimStatus.Active
            || string.IsNullOrWhiteSpace(match.ActivateIdempotencyKey)
            || !string.IsNullOrEmpty(match.BoundOrderGuid))
        {
            throw new InvalidDataException(
                "共享挂单购物车 binding 的 durable claim 状态或 scope 无效，拒绝付款。");
        }

        return new LocalHeldOrderCompletionContext(
            match.HoldGuid,
            match.ClaimId,
            match.Source,
            match.PrepareIdempotencyKey,
            match.ActivateIdempotencyKey,
            match.BoundOrderGuid,
            DateTimeOffset.UtcNow
                .ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
