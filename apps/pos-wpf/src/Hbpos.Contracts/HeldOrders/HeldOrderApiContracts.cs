using System.Text.Json.Serialization;

namespace Hbpos.Contracts.HeldOrders;

public enum SharedHeldOrderStatus
{
    Pending = 1,
    Claimed = 2,
    Completed = 3,
    Cancelled = 4
}

public enum SharedHeldOrderClaimStatus
{
    Prepared = 1,
    Active = 2,
    Released = 3,
    Completed = 4,
    Superseded = 5
}

public sealed record SharedHeldOrderCapabilitiesResponse
{
    public bool Enabled { get; init; }

    public int PayloadVersion { get; init; } = 1;

    public int PreparedTtlSeconds { get; init; } = 120;

    public bool ForceReleaseSupported { get; init; } = true;

    // 旧服务端不返回此字段；新客户端反序列化时必须安全回退为只支持 V1。
    public IReadOnlyList<int> SupportedPayloadVersions { get; init; } = [1];

    public int PreferredPayloadVersion { get; init; } = 1;

    public SharedHeldOrderCapabilitiesResponse(
        bool Enabled,
        int PayloadVersion = 1,
        int PreparedTtlSeconds = 120,
        bool ForceReleaseSupported = true)
    {
        this.Enabled = Enabled;
        this.PayloadVersion = PayloadVersion;
        this.PreparedTtlSeconds = PreparedTtlSeconds;
        this.ForceReleaseSupported = ForceReleaseSupported;
    }
}

public sealed record SharedHeldOrderPublishRequest(
    Guid HoldGuid,
    string StoreCode,
    string DeviceCode,
    [property: JsonConverter(typeof(SharedSaleCartPayloadJsonConverter))]
    object Cart,
    string IdempotencyKey);

public sealed record SharedHeldOrderPublishResponse(
    Guid HoldGuid,
    SharedHeldOrderStatus Status,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    bool AlreadyExists = false);

public sealed record SharedHeldOrderCancelResponse(
    Guid HoldGuid,
    SharedHeldOrderStatus Status,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    bool AlreadyCancelled = false);

/// <summary>列表 DTO 仅含汇总，禁止携带明文或密文 payload。 </summary>
public sealed record SharedHeldOrderListItemDto(
    Guid HoldGuid,
    string StoreCode,
    string DeviceCode,
    string HeldByCashierId,
    string HeldByCashierName,
    DateTimeOffset HeldAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int LineCount,
    long TotalCents,
    long DiscountCents,
    long ActualCents,
    long Revision);

public sealed record SharedHeldOrderClaimPrepareRequest(
    Guid ClaimGuid,
    string IdempotencyKey);

/// <summary>仅 prepare 返回解密 payload；其余 claim DTO 一律不含 payload。 </summary>
public sealed record SharedHeldOrderClaimPrepareResponse(
    Guid HoldGuid,
    Guid ClaimGuid,
    SharedHeldOrderClaimStatus Status,
    [property: JsonConverter(typeof(SharedSaleCartPayloadJsonConverter))]
    object Payload,
    string ClaimantDeviceCode,
    string ClaimantCashierId,
    string ClaimantCashierName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    long Revision,
    bool AlreadyExists = false);

public sealed record SharedHeldOrderClaimDto(
    Guid HoldGuid,
    Guid ClaimGuid,
    SharedHeldOrderClaimStatus Status,
    string StoreCode,
    string ClaimantDeviceCode,
    string ClaimantCashierId,
    string ClaimantCashierName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    bool ForceReleased,
    string? ForceReleaseReason,
    string? ForceReleaseCashierId,
    string? ForceReleaseCashierName,
    DateTimeOffset? ForceReleasedAtUtc,
    long Revision,
    bool AlreadyExists = false);

/// <summary>
/// claims/mine 是崩溃恢复入口：仅限本人设备，可返回已 prepare/active 的解密 payload。
/// 普通列表/claim DTO 仍绝不含 payload。
/// </summary>
public sealed record SharedHeldOrderRecoveryClaimDto(
    Guid HoldGuid,
    Guid ClaimGuid,
    SharedHeldOrderClaimStatus Status,
    string StoreCode,
    string ClaimantDeviceCode,
    string ClaimantCashierId,
    string ClaimantCashierName,
    [property: JsonConverter(typeof(SharedSaleCartPayloadJsonConverter))]
    object Payload,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    long Revision);

public sealed record SharedHeldOrderForceReleaseRequest(
    string Reason);
