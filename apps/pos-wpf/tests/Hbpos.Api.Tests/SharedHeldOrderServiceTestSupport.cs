using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.HeldOrders;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

internal static class SharedHeldOrderServiceTestSupport
{
    internal static readonly DateTimeOffset InitialNow =
        DateTimeOffset.Parse("2026-08-10T02:00:00Z");

    internal static SharedHeldOrderIdentity Identity(
        string storeCode = "S01",
        string deviceCode = "POS-01",
        string cashierId = "C01",
        string cashierName = "持单收银员",
        IReadOnlyCollection<string>? permissionCodes = null,
        string? cashierUserGuid = "U-CASHIER-1")
    {
        permissionCodes ??= ["Permissions.PosTerminal.Sales.RecallOrder"];
        return new SharedHeldOrderIdentity(
            storeCode,
            deviceCode,
            cashierId,
            cashierName,
            permissionCodes,
            cashierUserGuid);
    }

    internal static SharedHeldOrderPublishRequest PublishRequest(
        Guid? holdGuid = null,
        SharedSaleCartV1? cart = null,
        string? idempotencyKey = null) => new(
        holdGuid ?? Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        "S01",
        "POS-01",
        cart ?? ValidCart(),
        idempotencyKey ?? "publish-1");

    internal static SharedHeldOrderClaimPrepareRequest PrepareRequest(
        Guid? claimGuid = null,
        string? idempotencyKey = null) => new(
        claimGuid ?? Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
        idempotencyKey ?? "claim-1");

    internal static SharedSaleCartV1 DiscountedCart(
        string discountMode,
        long? cents = null,
        int? basisPoints = null)
    {
        var cart = ValidCart();
        return cart with
        {
            PricingState = cart.PricingState with
            {
                Lines =
                [
                    cart.PricingState.Lines[0] with
                    {
                        DiscountState = new SharedLineDiscountStateV1(
                            discountMode,
                            cents,
                            basisPoints,
                            discountMode == SharedSaleCartV1Constants.DiscountModePromotion
                                ? ["P1"]
                                : null)
                    }
                ]
            }
        };
    }

    internal static SharedSaleCartV1 ValidCart() => new(
        Version: 1,
        new SharedPricingStateV1(
            Revision: 1,
            Mode: SharedSaleCartV1Constants.PricingModeSale,
            AsOfIso: "2026-08-10T02:00:00Z",
            Promotions:
            [
                new SharedPromotionV1(
                    Id: "P1",
                    Name: "特价促销",
                    EffectiveStartIso: "2026-08-01T00:00:00Z",
                    EffectiveEndIso: "2026-08-31T00:00:00Z",
                    IsExclusive: true,
                    Priority: 10,
                    ApplyQuantity: 2,
                    FixedPriceCents: 1000,
                    MaxApplicationsPerOrder: 1,
                    Products: [new SharedPromotionProductV1("SKU-1", 0.25m)])
            ],
            Lines:
            [
                new SharedSaleLineV1(
                    LineId: "L1",
                    ProductCode: "SKU-1",
                    ItemNumber: "ITEM-1",
                    LookupCode: "BAR-1",
                    DisplayName: "测试商品",
                    Quantity: 2m,
                    UnitPriceCents: 1500,
                    BasePriceSource: SharedSaleCartV1Constants.PriceSourceCatalog,
                    SyncProvenance: new SharedLineSyncProvenanceV1(
                        "REF-1",
                        PriceSourceKind.StoreRetailPrice),
                    Kind: SharedSaleCartV1Constants.LineKindSale,
                    ReturnSourceKey: null,
                    OriginalOrderGuid: null,
                    OriginalOrderDetailGuid: null,
                    DiscountState: new SharedLineDiscountStateV1(Mode: "none"))
            ]));

    internal static Harness CreateHarness(bool enabled = true)
    {
        var time = new ManualTimeProvider(InitialNow);
        var repository = new FakeSharedHeldOrderRepository();
        var protector = new EphemeralPayloadProtector();
        var service = new SharedHeldOrderService(
            repository,
            protector,
            Options.Create(new SharedHeldOrderOptions { Enabled = enabled }),
            time);
        return new Harness(service, repository, protector, time);
    }

    internal sealed record Harness(
        SharedHeldOrderService Service,
        FakeSharedHeldOrderRepository Repository,
        EphemeralPayloadProtector Protector,
        ManualTimeProvider Time)
    {
        internal SharedHeldOrderIdentity Identity { get; } = Identity();
    }

    internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    internal sealed class EphemeralPayloadProtector : ISharedHeldOrderPayloadProtector
    {
        private readonly IDataProtector _protector =
            new EphemeralDataProtectionProvider().CreateProtector(
                SharedHeldOrderPayloadProtector.Purpose);

        public string Protect(SharedSaleCartV1 payload) =>
            _protector.Protect(System.Text.Json.JsonSerializer.Serialize(payload));

        public SharedSaleCartV1 Unprotect(string ciphertext) =>
            System.Text.Json.JsonSerializer.Deserialize<SharedSaleCartV1>(
                _protector.Unprotect(ciphertext))!;
    }

    internal sealed class FakeSharedHeldOrderRepository : ISharedHeldOrderRepository
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<Guid, SharedHeldOrderRecord> _holds = [];
        private readonly Dictionary<Guid, SharedHeldOrderClaimRecord> _claims = [];

        public Task<SharedHeldOrderRecord?> GetHoldAsync(
            Guid holdGuid,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _holds.TryGetValue(holdGuid, out var hold);
            return Task.FromResult(hold);
        }

        public Task<SharedHeldOrderRecord?> GetHoldByIdempotencyAsync(
            string storeCode,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_holds.Values.FirstOrDefault(hold =>
                string.Equals(hold.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(hold.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));
        }

        public async Task<bool> TryInsertHoldAsync(
            SharedHeldOrderRecord hold,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_holds.ContainsKey(hold.HoldGuid) ||
                    _holds.Values.Any(existing =>
                        string.Equals(existing.StoreCode, hold.StoreCode, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.IdempotencyKey, hold.IdempotencyKey, StringComparison.Ordinal)))
                {
                    return false;
                }

                _holds[hold.HoldGuid] = hold;
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> TryUpdateHoldAsync(
            SharedHeldOrderRecord hold,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_holds.TryGetValue(hold.HoldGuid, out var current) ||
                    current.Revision != expectedRevision)
                {
                    return false;
                }

                _holds[hold.HoldGuid] = hold;
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> TryCancelHoldAsync(
            Guid holdGuid,
            string storeCode,
            string deviceCode,
            long expectedRevision,
            DateTimeOffset cancelledAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_holds.TryGetValue(holdGuid, out var current) ||
                    current.Revision != expectedRevision ||
                    current.Status != SharedHeldOrderStatus.Pending ||
                    !string.Equals(current.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase) ||
                    _claims.Values.Any(claim => claim.HoldGuid == holdGuid && claim.IsBlocking))
                {
                    return false;
                }

                _holds[holdGuid] = current with
                {
                    Status = SharedHeldOrderStatus.Cancelled,
                    UpdatedAtUtc = cancelledAtUtc,
                    Revision = current.Revision + 1
                };
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<IReadOnlyList<SharedHeldOrderRecord>> ListPendingAsync(
            string storeCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SharedHeldOrderRecord> result = _holds.Values
                .Where(hold => hold.Status == SharedHeldOrderStatus.Pending &&
                    string.Equals(hold.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(hold => hold.CreatedAtUtc)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<SharedHeldOrderClaimRecord?> GetClaimAsync(
            Guid claimGuid,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _claims.TryGetValue(claimGuid, out var claim);
            return Task.FromResult(claim);
        }

        public Task<SharedHeldOrderClaimRecord?> GetBlockingClaimAsync(
            Guid holdGuid,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_claims.Values.FirstOrDefault(claim =>
                claim.HoldGuid == holdGuid && claim.IsBlocking));
        }

        public async Task<bool> TryInsertClaimAsync(
            SharedHeldOrderClaimRecord claim,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_claims.ContainsKey(claim.ClaimGuid) ||
                    _claims.Values.Any(existing =>
                        existing.HoldGuid == claim.HoldGuid &&
                        (existing.IsBlocking ||
                         string.Equals(existing.IdempotencyKey, claim.IdempotencyKey, StringComparison.Ordinal))))
                {
                    return false;
                }

                _claims[claim.ClaimGuid] = claim;
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> TryUpdateClaimAsync(
            SharedHeldOrderClaimRecord claim,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_claims.TryGetValue(claim.ClaimGuid, out var current) ||
                    current.Revision != expectedRevision)
                {
                    return false;
                }

                _claims[claim.ClaimGuid] = claim;
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<bool> TryUpdateClaimAndHoldAsync(
            SharedHeldOrderClaimRecord claim,
            long expectedClaimRevision,
            SharedHeldOrderRecord hold,
            long expectedHoldRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_claims.TryGetValue(claim.ClaimGuid, out var currentClaim) ||
                    currentClaim.Revision != expectedClaimRevision ||
                    !_holds.TryGetValue(hold.HoldGuid, out var currentHold) ||
                    currentHold.Revision != expectedHoldRevision)
                {
                    return false;
                }

                _claims[claim.ClaimGuid] = claim;
                _holds[hold.HoldGuid] = hold;
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<IReadOnlyList<SharedHeldOrderClaimRecord>> ListMyClaimsAsync(
            string storeCode,
            string deviceCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SharedHeldOrderClaimRecord> result = _claims.Values
                .Where(claim => claim.IsBlocking &&
                    string.Equals(claim.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(claim.ClaimantDeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(claim => claim.CreatedAtUtc)
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
