using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hbpos.Contracts.HeldOrders;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public static class SharedHeldOrderErrorCodes
{
    public const string Disabled = "SHARED_HELD_ORDER_DISABLED";
    public const string Busy = "SHARED_HELD_ORDER_BUSY";
    public const string Mismatch = "SHARED_HELD_ORDER_MISMATCH";
    public const string NotFound = "SHARED_HELD_ORDER_NOT_FOUND";
    public const string Expired = "SHARED_HELD_ORDER_CLAIM_EXPIRED";
    public const string Invalid = "SHARED_HELD_ORDER_INVALID";
    public const string PermissionDenied = "SHARED_HELD_ORDER_PERMISSION_DENIED";
    public const string CrossStore = "SHARED_HELD_ORDER_CROSS_STORE";
}

public sealed class SharedHeldOrderException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class SharedHeldOrderOptions
{
    /// <summary>跨设备挂单默认启用；紧急情况下仍可通过显式配置 false 关闭。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>新客户端发布无 baseline cart 时优先选择的 payload 版本；默认 1，可安全切换为 2。</summary>
    public int PreferredPayloadVersion { get; set; } =
        SharedSaleCartV1Constants.PayloadVersion;
}

/// <summary>
/// 权威 store/device 来自设备 claims；cashier 来自已验票收银员票据。
/// 同店跨设备允许，跨店一律拒绝。
/// </summary>
public sealed record SharedHeldOrderIdentity(
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    IReadOnlyCollection<string>? PermissionCodes = null,
    string? CashierUserGuid = null);

public interface ISharedHeldOrderService
{
    SharedHeldOrderCapabilitiesResponse GetCapabilities();

    Task<SharedHeldOrderPublishResponse> PublishAsync(
        SharedHeldOrderPublishRequest request,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    Task<SharedHeldOrderCancelResponse> CancelAsync(
        Guid holdGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
        SharedHeldOrderIdentity identity,
        IReadOnlyCollection<int>? supportedPayloadVersions,
        CancellationToken cancellationToken) =>
        ListPendingAsync(identity, cancellationToken);

    Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
        Guid holdGuid,
        SharedHeldOrderClaimPrepareRequest request,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
        Guid holdGuid,
        SharedHeldOrderClaimPrepareRequest request,
        SharedHeldOrderIdentity identity,
        IReadOnlyCollection<int>? supportedPayloadVersions,
        CancellationToken cancellationToken) =>
        PrepareAsync(holdGuid, request, identity, cancellationToken);

    Task<SharedHeldOrderClaimDto> ActivateAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    Task<SharedHeldOrderClaimDto> ReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    Task<SharedHeldOrderClaimDto> ForceReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderForceReleaseRequest request,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    /// <summary>崩溃恢复入口：仅本人设备可读取已 prepare/active 的解密 payload。 </summary>
    Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ListMyClaimsAsync(
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ListMyClaimsAsync(
        SharedHeldOrderIdentity identity,
        IReadOnlyCollection<int>? supportedPayloadVersions,
        CancellationToken cancellationToken) =>
        ListMyClaimsAsync(identity, cancellationToken);
}

public sealed class SharedHeldOrderService(
    ISharedHeldOrderRepository repository,
    ISharedHeldOrderPayloadProtector payloadProtector,
    IOptions<SharedHeldOrderOptions> options,
    TimeProvider? timeProvider = null) : ISharedHeldOrderService
{
    public const int PreparedTtlSeconds = 120;
    private readonly SharedHeldOrderOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public SharedHeldOrderCapabilitiesResponse GetCapabilities()
    {
        var preferredPayloadVersion = _options.PreferredPayloadVersion;
        if (preferredPayloadVersion is not SharedSaleCartVersioning.PayloadVersionV1
            and not SharedSaleCartVersioning.PayloadVersionV2)
        {
            throw new InvalidOperationException(
                "PreferredPayloadVersion must be a supported shared held order payload version (1 or 2).");
        }

        return new SharedHeldOrderCapabilitiesResponse(
            Enabled: _options.Enabled,
            PayloadVersion: SharedSaleCartV1Constants.PayloadVersion,
            PreparedTtlSeconds: PreparedTtlSeconds,
            ForceReleaseSupported: true)
        {
            SupportedPayloadVersions = [SharedSaleCartVersioning.PayloadVersionV1, SharedSaleCartVersioning.PayloadVersionV2],
            PreferredPayloadVersion = preferredPayloadVersion
        };
    }

    public async Task<SharedHeldOrderPublishResponse> PublishAsync(
        SharedHeldOrderPublishRequest request,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        var normalized = NormalizePublishRequest(request, normalizedIdentity);
        var cart = SharedSaleCartVersioning.Validate(normalized.Cart);
        var fingerprint = Fingerprint(cart);
        var existing = await repository.GetHoldAsync(normalized.HoldGuid, cancellationToken);
        if (existing is not null)
        {
            ValidateSameStore(existing, normalizedIdentity);
            ValidatePublishReplay(existing, normalized, fingerprint);

            return Map(existing, alreadyExists: true);
        }

        var now = _timeProvider.GetUtcNow();
        var summary = Summarize(cart);
        var hold = new SharedHeldOrderRecord(
            normalized.HoldGuid,
            normalizedIdentity.StoreCode,
            normalizedIdentity.DeviceCode,
            normalizedIdentity.CashierId,
            normalizedIdentity.CashierName,
            SharedSaleCartVersioning.GetPayloadVersion(cart),
            payloadProtector.Protect(cart),
            fingerprint,
            normalized.IdempotencyKey,
            SharedHeldOrderStatus.Pending,
            1,
            now,
            now,
            now,
            summary.LineCount,
            summary.TotalCents,
            summary.DiscountCents,
            summary.ActualCents);
        if (await repository.TryInsertHoldAsync(hold, cancellationToken))
        {
            return Map(hold, alreadyExists: false);
        }

        // 唯一键竞争可能是同一幂等重试，也可能是另一台设备并发发布同一 hold。
        existing = await repository.GetHoldByIdempotencyAsync(
                normalizedIdentity.StoreCode,
                normalized.IdempotencyKey,
                cancellationToken)
            ?? await repository.GetHoldAsync(normalized.HoldGuid, cancellationToken);
        if (existing is not null)
        {
            ValidateSameStore(existing, normalizedIdentity);
            ValidatePublishReplay(existing, normalized, fingerprint);

            return Map(existing, alreadyExists: true);
        }

        throw Busy("Hold was concurrently created; retry with the same idempotency key.");
    }

    public async Task<SharedHeldOrderCancelResponse> CancelAsync(
        Guid holdGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        var hold = await GetRequiredHoldAsync(holdGuid, normalizedIdentity, cancellationToken);
        ValidateCancelOrigin(hold, normalizedIdentity);

        if (hold.Status == SharedHeldOrderStatus.Cancelled)
        {
            return MapCancel(hold, alreadyCancelled: true);
        }

        if (hold.Status != SharedHeldOrderStatus.Pending)
        {
            throw hold.Status == SharedHeldOrderStatus.Completed
                ? Mismatch("A completed held order cannot be cancelled.")
                : Busy("Held order is already claimed and cannot be cancelled.");
        }

        // 过期 Prepared 沿用既有 TTL 释放逻辑；仍为 Prepared/Active 的 blocking claim 必须拒绝。
        var blocking = await repository.GetBlockingClaimAsync(holdGuid, cancellationToken);
        if (blocking is not null)
        {
            blocking = await ExpirePreparedAsync(blocking, cancellationToken);
            if (blocking.IsBlocking &&
                blocking.Status is SharedHeldOrderClaimStatus.Prepared or SharedHeldOrderClaimStatus.Active)
            {
                throw Busy("Hold has a blocking claim and cannot be cancelled.");
            }
        }

        var cancelledAtUtc = _timeProvider.GetUtcNow();
        if (await repository.TryCancelHoldAsync(
                holdGuid,
                hold.StoreCode,
                hold.DeviceCode,
                hold.Revision,
                cancelledAtUtc,
                cancellationToken))
        {
            return new SharedHeldOrderCancelResponse(
                holdGuid,
                SharedHeldOrderStatus.Cancelled,
                hold.Revision + 1,
                cancelledAtUtc);
        }

        // 原子仓储检查失败后只重读权威状态：并发取消返回幂等成功，其他状态返回明确拒绝。
        var current = await repository.GetHoldAsync(holdGuid, cancellationToken)
            ?? throw NotFound("Held order disappeared while cancelling.");
        ValidateSameStore(current, normalizedIdentity);
        ValidateCancelOrigin(current, normalizedIdentity);
        if (current.Status == SharedHeldOrderStatus.Cancelled)
        {
            return MapCancel(current, alreadyCancelled: true);
        }

        throw current.Status == SharedHeldOrderStatus.Completed
            ? Mismatch("A completed held order cannot be cancelled.")
            : Busy("Held order changed while cancelling; retry.");
    }

    public async Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        return await ListPendingAsync(identity, supportedPayloadVersions: null, cancellationToken);
    }

    public async Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
        SharedHeldOrderIdentity identity,
        IReadOnlyCollection<int>? supportedPayloadVersions,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        var supportedVersions = NormalizeSupportedPayloadVersions(supportedPayloadVersions);
        var holds = await repository.ListPendingAsync(
            normalizedIdentity.StoreCode,
            cancellationToken);
        // 关键逻辑：列表只返回 Pending 汇总，Claimed/Completed 一律隐藏，且永不接触 ciphertext。
        return holds
            .Where(hold => supportedVersions.Contains(hold.PayloadVersion))
            .Select(MapListItem)
            .ToArray();
    }

    public async Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
        Guid holdGuid,
        SharedHeldOrderClaimPrepareRequest request,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        return await PrepareAsync(
            holdGuid,
            request,
            identity,
            supportedPayloadVersions: null,
            cancellationToken);
    }

    public async Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
        Guid holdGuid,
        SharedHeldOrderClaimPrepareRequest request,
        SharedHeldOrderIdentity identity,
        IReadOnlyCollection<int>? supportedPayloadVersions,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        var normalized = NormalizePrepareRequest(request);
        var supportedVersions = NormalizeSupportedPayloadVersions(supportedPayloadVersions);
        var hold = await GetRequiredHoldAsync(holdGuid, normalizedIdentity, cancellationToken);
        // 不支持 V2 的旧客户端不得在 claim 写入前进入 claim 创建路径。
        if (!supportedVersions.Contains(hold.PayloadVersion))
        {
            throw Invalid("This client does not support the held order payload version.");
        }

        var claimFingerprint = ClaimFingerprint(holdGuid, normalized.ClaimGuid, normalized.IdempotencyKey);
        var existing = await repository.GetClaimAsync(normalized.ClaimGuid, cancellationToken);
        if (existing is not null)
        {
            existing = await ExpirePreparedAsync(existing, cancellationToken);
            ValidateClaimScope(existing, holdGuid, normalizedIdentity);
            // 普通 prepare 只允许本人设备操作已有 claim；跨设备一律拒绝。
            ValidateClaimOwner(existing, normalizedIdentity);
            if (!string.Equals(existing.Fingerprint, claimFingerprint, StringComparison.Ordinal))
            {
                throw Mismatch("claimGuid is already bound to a different claim.");
            }

            // Released/Completed/Superseded 均为终态：同 claim 幂等读取，绝不自动复活。
            return await MapPrepareAsync(existing, hold, alreadyExists: true);
        }

        // 新建 claim 只允许 Pending hold；Completed/Claimed 不得创建新 claim。
        if (hold.Status != SharedHeldOrderStatus.Pending)
        {
            throw hold.Status is SharedHeldOrderStatus.Completed or SharedHeldOrderStatus.Cancelled
                ? Mismatch("A completed or cancelled held order cannot be prepared.")
                : Busy("Hold already has a blocking claim from another device.");
        }

        await ExpireBlockingIfExpiredAsync(holdGuid, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var claim = new SharedHeldOrderClaimRecord(
            normalized.ClaimGuid,
            holdGuid,
            normalizedIdentity.StoreCode,
            normalizedIdentity.DeviceCode,
            normalizedIdentity.CashierId,
            normalizedIdentity.CashierName,
            normalized.IdempotencyKey,
            claimFingerprint,
            SharedHeldOrderClaimStatus.Prepared,
            IsBlocking: true,
            Revision: 1,
            now,
            now,
            now.AddSeconds(PreparedTtlSeconds),
            ActivatedAtUtc: null,
            ReleasedAtUtc: null);
        if (await repository.TryInsertClaimAsync(claim, cancellationToken))
        {
            return await MapPrepareAsync(claim, hold, alreadyExists: false);
        }

        // 并发 prepare 单赢家：插入失败后重读 hold/blocking claim，避免 Completed 竞争窗口。
        var currentHold = await repository.GetHoldAsync(holdGuid, cancellationToken)
            ?? throw NotFound("Held order disappeared during claim preparation.");
        if (currentHold.Status != SharedHeldOrderStatus.Pending)
        {
            throw currentHold.Status is SharedHeldOrderStatus.Completed or SharedHeldOrderStatus.Cancelled
                ? Mismatch("A completed or cancelled held order cannot be prepared.")
                : Busy("Hold already has a blocking claim from another device.");
        }

        // 并发 prepare 单赢家：插入失败后重读 blocking claim，只有同 claim 同指纹才算重试。
        var blocking = await repository.GetBlockingClaimAsync(holdGuid, cancellationToken);
        if (blocking is not null &&
            blocking.ClaimGuid == normalized.ClaimGuid &&
            string.Equals(blocking.Fingerprint, claimFingerprint, StringComparison.Ordinal))
        {
            return await MapPrepareAsync(blocking, hold, alreadyExists: true);
        }

        throw Busy("Hold already has a blocking claim from another device.");
    }

    public async Task<SharedHeldOrderClaimDto> ActivateAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        var hold = await GetRequiredHoldAsync(holdGuid, normalizedIdentity, cancellationToken);
        var claim = await GetRequiredClaimAsync(holdGuid, claimGuid, normalizedIdentity, cancellationToken);
        claim = await ExpirePreparedAsync(claim, cancellationToken);
        // owner-scope 必须先行：另一设备不能借 Active 幂等分支读取成功。
        ValidateClaimOwner(claim, normalizedIdentity);
        if (claim.Status == SharedHeldOrderClaimStatus.Active)
        {
            return Map(claim, alreadyExists: true);
        }

        if (claim.Status != SharedHeldOrderClaimStatus.Prepared)
        {
            throw Mismatch("Only a prepared claim can be activated.");
        }

        var now = _timeProvider.GetUtcNow();
        var activated = claim with
        {
            Status = SharedHeldOrderClaimStatus.Active,
            ExpiresAtUtc = null,
            ActivatedAtUtc = now,
            UpdatedAtUtc = now,
            Revision = claim.Revision + 1
        };
        var claimedHold = hold with
        {
            Status = SharedHeldOrderStatus.Claimed,
            UpdatedAtUtc = now,
            Revision = hold.Revision + 1
        };
        if (!await repository.TryUpdateClaimAndHoldAsync(
                activated,
                claim.Revision,
                claimedHold,
                hold.Revision,
                cancellationToken))
        {
            throw Busy("Claim changed while activating; retry.");
        }

        return Map(activated, alreadyExists: false);
    }

    public async Task<SharedHeldOrderClaimDto> ReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        var hold = await GetRequiredHoldAsync(holdGuid, normalizedIdentity, cancellationToken);
        var claim = await GetRequiredClaimAsync(holdGuid, claimGuid, normalizedIdentity, cancellationToken);
        claim = await ExpirePreparedAsync(claim, cancellationToken);
        // owner-scope 必须先行：另一设备不能借 Released 幂等分支读取成功。
        ValidateClaimOwner(claim, normalizedIdentity);
        if (claim.Status == SharedHeldOrderClaimStatus.Released)
        {
            return Map(claim, alreadyExists: true);
        }

        if (claim.Status is not (SharedHeldOrderClaimStatus.Prepared or SharedHeldOrderClaimStatus.Active))
        {
            throw Mismatch("Only a prepared or active claim can be released.");
        }

        var now = _timeProvider.GetUtcNow();
        var released = claim with
        {
            Status = SharedHeldOrderClaimStatus.Released,
            IsBlocking = false,
            ExpiresAtUtc = null,
            ReleasedAtUtc = now,
            UpdatedAtUtc = now,
            Revision = claim.Revision + 1
        };
        var pendingHold = hold with
        {
            Status = SharedHeldOrderStatus.Pending,
            UpdatedAtUtc = now,
            Revision = hold.Revision + 1
        };
        if (!await repository.TryUpdateClaimAndHoldAsync(
                released,
                claim.Revision,
                pendingHold,
                hold.Revision,
                cancellationToken))
        {
            throw Busy("Claim changed while releasing; retry.");
        }

        return Map(released, alreadyExists: false);
    }

    public async Task<SharedHeldOrderClaimDto> ForceReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderForceReleaseRequest request,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        // 主管票据语义：必须来自已验票收银员且持有 History.Recall 权限。
        if (string.IsNullOrWhiteSpace(normalizedIdentity.CashierUserGuid))
        {
            throw PermissionDenied("Force release requires a verified supervisor cashier ticket.");
        }

        if (normalizedIdentity.PermissionCodes is null ||
            !normalizedIdentity.PermissionCodes.Contains(
                "Permissions.PosTerminal.History.Recall",
                StringComparer.OrdinalIgnoreCase))
        {
            throw PermissionDenied("Verified cashier lacks the recall order permission.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw Invalid("Force release requires a non-empty reason up to 500 characters.");
        }

        var hold = await GetRequiredHoldAsync(holdGuid, normalizedIdentity, cancellationToken);
        var claim = await GetRequiredClaimAsync(holdGuid, claimGuid, normalizedIdentity, cancellationToken);
        claim = await ExpirePreparedAsync(claim, cancellationToken);
        if (claim.Status == SharedHeldOrderClaimStatus.Released)
        {
            return Map(claim, alreadyExists: true);
        }

        if (claim.Status is not (SharedHeldOrderClaimStatus.Prepared or SharedHeldOrderClaimStatus.Active))
        {
            throw Mismatch("Only a prepared or active claim can be force released.");
        }

        var now = _timeProvider.GetUtcNow();
        var released = claim with
        {
            Status = SharedHeldOrderClaimStatus.Released,
            IsBlocking = false,
            ExpiresAtUtc = null,
            ReleasedAtUtc = now,
            UpdatedAtUtc = now,
            ForceReleased = true,
            ForceReleaseReason = reason,
            ForceReleaseCashierId = normalizedIdentity.CashierId,
            ForceReleaseCashierName = normalizedIdentity.CashierName,
            ForceReleaseCashierUserGuid = normalizedIdentity.CashierUserGuid,
            ForceReleasedAtUtc = now,
            Revision = claim.Revision + 1
        };
        var pendingHold = hold with
        {
            Status = SharedHeldOrderStatus.Pending,
            UpdatedAtUtc = now,
            Revision = hold.Revision + 1
        };
        if (!await repository.TryUpdateClaimAndHoldAsync(
                released,
                claim.Revision,
                pendingHold,
                hold.Revision,
                cancellationToken))
        {
            throw Busy("Claim changed while force releasing; retry.");
        }

        return Map(released, alreadyExists: false);
    }

    public async Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ListMyClaimsAsync(
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        return await ListMyClaimsAsync(identity, supportedPayloadVersions: null, cancellationToken);
    }

    public async Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ListMyClaimsAsync(
        SharedHeldOrderIdentity identity,
        IReadOnlyCollection<int>? supportedPayloadVersions,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var normalizedIdentity = NormalizeIdentity(identity);
        var supportedVersions = NormalizeSupportedPayloadVersions(supportedPayloadVersions);
        var claims = await repository.ListMyClaimsAsync(
            normalizedIdentity.StoreCode,
            normalizedIdentity.DeviceCode,
            cancellationToken);
        var result = new List<SharedHeldOrderRecoveryClaimDto>(claims.Count);
        foreach (var listedClaim in claims)
        {
            // mine 也是 Prepared TTL 的恢复入口；过期项先耐久释放并从结果排除。
            // Active 的 ExpiresAt 固定为空，永远不会在此自动释放。
            var claim = await ExpirePreparedAsync(listedClaim, cancellationToken);
            if (claim.Status is not (SharedHeldOrderClaimStatus.Prepared or SharedHeldOrderClaimStatus.Active))
            {
                continue;
            }

            var hold = await repository.GetHoldAsync(claim.HoldGuid, cancellationToken)
                ?? throw NotFound("Hold disappeared while recovering the claim.");
            if (!supportedVersions.Contains(hold.PayloadVersion))
            {
                continue;
            }

            result.Add(MapRecovery(claim, hold));
        }

        return result;
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.Disabled,
                "Shared held orders are not enabled on this server.");
        }
    }

    private async Task<SharedHeldOrderRecord> GetRequiredHoldAsync(
        Guid holdGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        var hold = await repository.GetHoldAsync(holdGuid, cancellationToken)
            ?? throw NotFound("Held order was not found.");
        ValidateSameStore(hold, identity);
        return hold;
    }

    private async Task<SharedHeldOrderClaimRecord> GetRequiredClaimAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderIdentity identity,
        CancellationToken cancellationToken)
    {
        if (claimGuid == Guid.Empty)
        {
            throw Invalid("claimGuid is required.");
        }

        var claim = await repository.GetClaimAsync(claimGuid, cancellationToken)
            ?? throw NotFound("Held order claim was not found.");
        ValidateClaimScope(claim, holdGuid, identity);
        return claim;
    }

    private static void ValidateSameStore(
        SharedHeldOrderRecord hold,
        SharedHeldOrderIdentity identity)
    {
        if (!string.Equals(hold.StoreCode, identity.StoreCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.CrossStore,
                "Held order belongs to another store.");
        }
    }

    private static void ValidateCancelOrigin(
        SharedHeldOrderRecord hold,
        SharedHeldOrderIdentity identity)
    {
        if (!string.Equals(hold.DeviceCode, identity.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw PermissionDenied("Only the device that published the held order can cancel it.");
        }
    }

    private static void ValidatePublishReplay(
        SharedHeldOrderRecord existing,
        SharedHeldOrderPublishRequest request,
        string fingerprint)
    {
        // publish 重放必须复用完整不可变事实；相同 payload 不能掩盖错误的 hold/key/device。
        if (existing.HoldGuid != request.HoldGuid ||
            !string.Equals(existing.DeviceCode, request.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existing.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal) ||
            existing.PayloadVersion != SharedSaleCartVersioning.GetPayloadVersion(request.Cart) ||
            !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw Mismatch("holdGuid, deviceCode, idempotencyKey, payload version or cart payload does not match the existing hold.");
        }
    }

    private static void ValidateClaimScope(
        SharedHeldOrderClaimRecord claim,
        Guid holdGuid,
        SharedHeldOrderIdentity identity)
    {
        if (claim.HoldGuid != holdGuid)
        {
            throw Mismatch("Claim does not belong to the hold in the route.");
        }

        if (!string.Equals(claim.StoreCode, identity.StoreCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.CrossStore,
                "Claim belongs to another store.");
        }
    }

    private static void ValidateClaimOwner(
        SharedHeldOrderClaimRecord claim,
        SharedHeldOrderIdentity identity)
    {
        if (!string.Equals(claim.ClaimantDeviceCode, identity.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.PermissionDenied,
                "Claim belongs to another device.");
        }
    }

    private async Task<SharedHeldOrderClaimRecord> ExpirePreparedAsync(
        SharedHeldOrderClaimRecord claim,
        CancellationToken cancellationToken)
    {
        if (claim.Status != SharedHeldOrderClaimStatus.Prepared ||
            claim.ExpiresAtUtc is null ||
            claim.ExpiresAtUtc > _timeProvider.GetUtcNow())
        {
            return claim;
        }

        var now = _timeProvider.GetUtcNow();
        var released = claim with
        {
            Status = SharedHeldOrderClaimStatus.Released,
            IsBlocking = false,
            ExpiresAtUtc = null,
            ReleasedAtUtc = now,
            UpdatedAtUtc = now,
            Revision = claim.Revision + 1
        };
        if (await repository.TryUpdateClaimAsync(released, claim.Revision, cancellationToken))
        {
            return released;
        }

        return await repository.GetClaimAsync(claim.ClaimGuid, cancellationToken) ?? released;
    }

    private async Task ExpireBlockingIfExpiredAsync(
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        var blocking = await repository.GetBlockingClaimAsync(holdGuid, cancellationToken);
        if (blocking is not null)
        {
            _ = await ExpirePreparedAsync(blocking, cancellationToken);
        }
    }

    private static SharedHeldOrderIdentity NormalizeIdentity(SharedHeldOrderIdentity identity)
    {
        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.StoreCode) ||
            string.IsNullOrWhiteSpace(identity.DeviceCode) ||
            string.IsNullOrWhiteSpace(identity.CashierId))
        {
            throw Invalid("A verified device and cashier identity is required.");
        }

        return identity with
        {
            StoreCode = identity.StoreCode.Trim(),
            DeviceCode = identity.DeviceCode.Trim(),
            CashierId = identity.CashierId.Trim(),
            CashierName = identity.CashierName?.Trim() ?? string.Empty
        };
    }

    private static SharedHeldOrderPublishRequest NormalizePublishRequest(
        SharedHeldOrderPublishRequest request,
        SharedHeldOrderIdentity identity)
    {
        if (request is null ||
            request.HoldGuid == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.StoreCode) ||
            string.IsNullOrWhiteSpace(request.DeviceCode) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw Invalid("holdGuid, storeCode, deviceCode and idempotencyKey are required.");
        }

        if (request.IdempotencyKey.Length > 100)
        {
            throw Invalid("idempotencyKey must not exceed 100 characters.");
        }

        // 请求 scope 只能与设备 claims 一致；跨店跨设备发布一律拒绝。
        if (!string.Equals(request.StoreCode.Trim(), identity.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.DeviceCode.Trim(), identity.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.CrossStore,
                "Request scope must match the device claims.");
        }

        return request with
        {
            StoreCode = request.StoreCode.Trim(),
            DeviceCode = request.DeviceCode.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim()
        };
    }

    private static SharedHeldOrderClaimPrepareRequest NormalizePrepareRequest(
        SharedHeldOrderClaimPrepareRequest request)
    {
        if (request is null ||
            request.ClaimGuid == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw Invalid("claimGuid and idempotencyKey are required.");
        }

        if (request.IdempotencyKey.Length > 100)
        {
            throw Invalid("idempotencyKey must not exceed 100 characters.");
        }

        return request with { IdempotencyKey = request.IdempotencyKey.Trim() };
    }

    private static SharedHeldOrderSummary Summarize(object cart)
    {
        return cart switch
        {
            SharedSaleCartV2 v2 => SummarizeV2(v2),
            SharedSaleCartV1 v1 => SummarizeV1(v1),
            _ => throw new SharedSaleCartValidationException(
                "Shared sale cart payload must be SharedSaleCartV1 or SharedSaleCartV2.")
        };
    }

    private static SharedHeldOrderSummary SummarizeV1(SharedSaleCartV1 cart)
    {
        var totalCents = 0L;
        var discountCents = 0L;
        foreach (var line in cart.PricingState.Lines)
        {
            var lineTotal = checked((long)Math.Round(
                line.UnitPriceCents * line.Quantity,
                MidpointRounding.AwayFromZero));
            totalCents = checked(totalCents + lineTotal);
            discountCents = checked(discountCents + LineDiscountCents(line, lineTotal));
        }

        if (discountCents > totalCents)
        {
            throw Invalid("Line discounts must not exceed the cart total.");
        }

        return new SharedHeldOrderSummary(
            cart.PricingState.Lines.Count,
            totalCents,
            discountCents,
            totalCents - discountCents);
    }

    private static SharedHeldOrderSummary SummarizeV2(SharedSaleCartV2 cart)
    {
        var totalCents = 0L;
        var discountCents = 0L;
        foreach (var line in cart.PricingState.Lines)
        {
            var lineTotal = checked((long)Math.Round(
                line.UnitPriceCents * line.Quantity,
                MidpointRounding.AwayFromZero));
            totalCents = checked(totalCents + lineTotal);
            discountCents = checked(discountCents + LineDiscountCents(line, lineTotal));
        }

        if (discountCents > totalCents)
        {
            throw Invalid("Line discounts must not exceed the cart total.");
        }

        return new SharedHeldOrderSummary(
            cart.PricingState.Lines.Count,
            totalCents,
            discountCents,
            totalCents - discountCents);
    }

    private static long LineDiscountCents(SharedSaleLineV1 line, long lineTotalCents)
    {
        return line.DiscountState.Mode switch
        {
            SharedSaleCartV1Constants.DiscountModeManualAmount =>
                line.DiscountState.Cents!.Value,
            SharedSaleCartV1Constants.DiscountModeManualPercent =>
                checked((long)decimal.Round(
                    (decimal)lineTotalCents * line.DiscountState.BasisPoints!.Value / 10_000m,
                    0,
                    MidpointRounding.AwayFromZero)),
            SharedSaleCartV1Constants.DiscountModePromotion =>
                line.DiscountState.Cents!.Value,
            _ => 0L
        };
    }

    private static long LineDiscountCents(SharedSaleLineV2 line, long lineTotalCents)
    {
        return line.DiscountState.Mode switch
        {
            SharedSaleCartV1Constants.DiscountModeNone =>
                checked((long)decimal.Round(
                    (decimal)lineTotalCents * line.CatalogDiscountBasisPoints / 10_000m,
                    0,
                    MidpointRounding.AwayFromZero)),
            SharedSaleCartV1Constants.DiscountModeManualAmount =>
                line.DiscountState.Cents!.Value,
            SharedSaleCartV1Constants.DiscountModeManualPercent =>
                checked((long)decimal.Round(
                    (decimal)lineTotalCents * line.DiscountState.BasisPoints!.Value / 10_000m,
                    0,
                    MidpointRounding.AwayFromZero)),
            SharedSaleCartV1Constants.DiscountModePromotion =>
                line.DiscountState.Cents!.Value,
            _ => 0L
        };
    }

    private static string Fingerprint(object cart) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(cart, cart.GetType()))));

    private static string ClaimFingerprint(Guid holdGuid, Guid claimGuid, string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{holdGuid:D}|{claimGuid:D}|{idempotencyKey}")));

    private Task<SharedHeldOrderClaimPrepareResponse> MapPrepareAsync(
        SharedHeldOrderClaimRecord claim,
        SharedHeldOrderRecord hold,
        bool alreadyExists)
    {
        return Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
            claim.HoldGuid,
            claim.ClaimGuid,
            claim.Status,
            ValidateUnprotected(payloadProtector.Unprotect(hold.PayloadCiphertext, hold.PayloadVersion)),
            claim.ClaimantDeviceCode,
            claim.CashierId,
            claim.CashierName,
            claim.CreatedAtUtc,
            claim.ExpiresAtUtc,
            claim.Revision,
            alreadyExists));
    }

    private static SharedHeldOrderPublishResponse Map(
        SharedHeldOrderRecord hold,
        bool alreadyExists) => new(
        hold.HoldGuid,
        hold.Status,
        hold.Revision,
        hold.CreatedAtUtc,
        alreadyExists);

    private static SharedHeldOrderCancelResponse MapCancel(
        SharedHeldOrderRecord hold,
        bool alreadyCancelled) => new(
        hold.HoldGuid,
        hold.Status,
        hold.Revision,
        hold.UpdatedAtUtc,
        alreadyCancelled);

    private static SharedHeldOrderListItemDto MapListItem(SharedHeldOrderRecord hold) => new(
        hold.HoldGuid,
        hold.StoreCode,
        hold.DeviceCode,
        hold.CashierId,
        hold.CashierName,
        hold.HeldAtUtc,
        hold.UpdatedAtUtc,
        hold.LineCount,
        hold.TotalCents,
        hold.DiscountCents,
        hold.ActualCents,
        hold.Revision);

    private static SharedHeldOrderClaimDto Map(
        SharedHeldOrderClaimRecord claim,
        bool alreadyExists) => new(
        claim.HoldGuid,
        claim.ClaimGuid,
        claim.Status,
        claim.StoreCode,
        claim.ClaimantDeviceCode,
        claim.CashierId,
        claim.CashierName,
        claim.CreatedAtUtc,
        claim.UpdatedAtUtc,
        claim.ExpiresAtUtc,
        claim.ActivatedAtUtc,
        claim.ReleasedAtUtc,
        claim.ForceReleased,
        claim.ForceReleaseReason,
        claim.ForceReleaseCashierId,
        claim.ForceReleaseCashierName,
        claim.ForceReleasedAtUtc,
        claim.Revision,
        alreadyExists);

    private SharedHeldOrderRecoveryClaimDto MapRecovery(
        SharedHeldOrderClaimRecord claim,
        SharedHeldOrderRecord hold) => new(
            claim.HoldGuid,
            claim.ClaimGuid,
            claim.Status,
            claim.StoreCode,
            claim.ClaimantDeviceCode,
            claim.CashierId,
            claim.CashierName,
            ValidateUnprotected(payloadProtector.Unprotect(hold.PayloadCiphertext, hold.PayloadVersion)),
            claim.CreatedAtUtc,
            claim.UpdatedAtUtc,
            claim.ExpiresAtUtc,
            claim.ActivatedAtUtc,
            claim.Revision);

    private static SharedHeldOrderException Busy(string message) =>
        new(SharedHeldOrderErrorCodes.Busy, message);

    private static SharedHeldOrderException Mismatch(string message) =>
        new(SharedHeldOrderErrorCodes.Mismatch, message);

    private static SharedHeldOrderException NotFound(string message) =>
        new(SharedHeldOrderErrorCodes.NotFound, message);

    private static SharedHeldOrderException Invalid(string message) =>
        new(SharedHeldOrderErrorCodes.Invalid, message);

    private static SharedHeldOrderException PermissionDenied(string message) =>
        new(SharedHeldOrderErrorCodes.PermissionDenied, message);

    private static object ValidateUnprotected(object payload)
    {
        // 解密后的 payload 必须再次通过 canonical 校验，防止库内密文被篡改或跨版本漂移。
        return SharedSaleCartVersioning.Validate(payload);
    }

    private static IReadOnlyCollection<int> NormalizeSupportedPayloadVersions(
        IReadOnlyCollection<int>? supportedPayloadVersions)
    {
        // 未提供过滤时默认仅 V1（旧客户端）；显式过滤只保留受支持版本，
        // 显式 [99]/[] 不回退，list 为空且 prepare 在写 claim 前拒绝。
        if (supportedPayloadVersions is null)
        {
            return [SharedSaleCartV1Constants.PayloadVersion];
        }

        return supportedPayloadVersions
            .Where(version => version is SharedSaleCartVersioning.PayloadVersionV1
                or SharedSaleCartVersioning.PayloadVersionV2)
            .Distinct()
            .OrderBy(version => version)
            .ToArray();
    }

    private sealed record SharedHeldOrderSummary(
        int LineCount,
        long TotalCents,
        long DiscountCents,
        long ActualCents);
}
