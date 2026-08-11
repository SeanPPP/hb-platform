import assert from "node:assert/strict";
import test from "node:test";

import { fromSharedSaleCartV1 } from "./shared-held-order-cart-reverse-mapper";
import type {
  PrepareClaimResult,
  SharedHeldOrderClaim,
  SharedHeldOrderClaimRepositoryPort,
  PreparedClaimInput,
} from "./shared-held-order-claim-repository";
import {
  SharedHeldOrderCoordinator,
  SharedHeldOrderCoordinatorError,
} from "./shared-held-order-coordinator";
import type { LocalPublicationEligibility } from "./shared-held-order-local-publication";
import {
  SharedHeldOrderApiError,
  type SharedHeldOrderApiErrorKind,
  type SharedHeldOrderClaimDto,
  type SharedHeldOrderNetworkApiPort,
  type SharedHeldOrderPrepareResult,
  type SharedHeldOrderRecoveryClaimDto,
} from "./shared-held-order-network-api";
import {
  normalizeSharedSaleCartV1,
  type SharedSaleCartV1,
} from "./shared-sale-cart-v1";

import type {
  HeldOrderScope,
  PricingCartStateSnapshot,
  RecallActiveBinding,
} from "@/core/contracts";
import type {
  ActivePricingCartLeasePort,
  ActivePricingCartPort,
  ActivePricingCartSnapshot,
  HeldOrderIdentity,
} from "@/features/held-orders/held-orders-domain";

const IDENTITY: HeldOrderIdentity = {
  storeCode: "BNE",
  deviceCode: "IPAD-1",
  cashierId: "CASHIER-1",
  cashierName: "Cashier",
  userGuid: "USER-1",
};

const SCOPE: HeldOrderScope = { storeCode: "BNE", deviceCode: "IPAD-1" };
const NOW = "2026-07-28T08:00:00.000Z";

function sharedCart(): SharedSaleCartV1 {
  return normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      revision: 5,
      mode: "sale",
      asOfIso: "2026-07-28T07:00:00.000Z",
      promotions: [],
      lines: [
        {
          lineId: "line-1",
          productCode: "P-1",
          itemNumber: null,
          lookupCode: "100",
          displayName: "Item",
          quantity: 1,
          unitPriceCents: 1_100,
          basePriceSource: "catalog",
          syncProvenance: { referenceCode: "REF", priceSource: 0 },
          kind: "sale",
          returnSourceKey: null,
          originalOrderGuid: null,
          originalOrderDetailGuid: null,
          discountState: { mode: "none" },
        },
      ],
    },
  });
}

function defaultPrepareResult(): SharedHeldOrderPrepareResult {
  return {
    holdGuid: "hold-1",
    claimGuid: "claim-1",
    status: "Prepared",
    payload: sharedCart(),
    claimantDeviceCode: "IPAD-1",
    claimantCashierId: "CASHIER-2",
    claimantCashierName: "Other",
    createdAtIso: NOW,
    expiresAtIso: "2026-07-28T08:15:00.000Z",
    revision: 7,
    alreadyExists: false,
  };
}

function defaultActivateResult(): SharedHeldOrderClaimDto {
  return {
    holdGuid: "hold-1",
    claimGuid: "claim-1",
    status: "Active",
    storeCode: "BNE",
    claimantDeviceCode: "IPAD-1",
    claimantCashierId: "CASHIER-2",
    claimantCashierName: "Other",
    createdAtIso: NOW,
    updatedAtIso: NOW,
    expiresAtIso: null,
    activatedAtIso: NOW,
    releasedAtIso: null,
    forceReleased: false,
    forceReleaseReason: null,
    forceReleaseCashierId: null,
    forceReleaseCashierName: null,
    forceReleasedAtIso: null,
    revision: 7,
    alreadyExists: false,
  };
}

function serverActiveClaim(
  claimGuid: string,
  holdGuid: string,
): SharedHeldOrderRecoveryClaimDto {
  return {
    holdGuid,
    claimGuid,
    status: "Active",
    storeCode: "BNE",
    claimantDeviceCode: "IPAD-1",
    claimantCashierId: "CASHIER-2",
    claimantCashierName: "Other",
    payload: sharedCart(),
    createdAtIso: NOW,
    updatedAtIso: NOW,
    expiresAtIso: null,
    activatedAtIso: NOW,
    revision: 9,
  };
}

function snapshot(
  lines: number,
  recallBinding: RecallActiveBinding | null = null,
): ActivePricingCartSnapshot {
  const pricingLines = Array.from({ length: lines }, (_, index) => ({
    lineId: `line-${index + 1}`,
    productCode: `P-${index + 1}`,
    itemNumber: null,
    lookupCode: `CODE-${index + 1}`,
    displayName: `Product ${index + 1}`,
    quantity: 1,
    unitPriceCents: 1_100,
    basePriceSource: "catalog" as const,
    kind: "sale" as const,
    returnSourceKey: null,
    originalOrderGuid: null,
    originalOrderDetailGuid: null,
    discountState: { kind: "none" as const },
  }));
  const pricingState: PricingCartStateSnapshot = {
    revision: 4,
    mode: "sale",
    asOfIso: "2026-07-28T07:00:00.000Z",
    promotions: [],
    lines: pricingLines,
  };
  return {
    sessionRevision: 4,
    recallBinding,
    terminalRecoveryRequired: false,
    pricingState,
    cart: {
      revision: 4,
      mode: "sale",
      lines: pricingLines.map((line) => ({
        ...line,
        quantity: String(line.quantity),
        unitPrice: { currency: "AUD", cents: line.unitPriceCents },
        discount: { currency: "AUD", cents: 0 },
        actualAmount: { currency: "AUD", cents: line.unitPriceCents },
        priceSource: line.basePriceSource,
      })),
      subtotal: { currency: "AUD", cents: lines * 1_100 },
      discount: { currency: "AUD", cents: 0 },
      actualAmount: { currency: "AUD", cents: lines * 1_100 },
    },
  };
}

class FakeCart implements ActivePricingCartPort {
  public value: ActivePricingCartSnapshot;
  public readonly blockCalls: RecallActiveBinding[] = [];
  public readonly replaceCalls: Readonly<{
    pricingState: PricingCartStateSnapshot;
    recallBinding: RecallActiveBinding | null;
  }>[] = [];
  public busy = false;
  public failRestore = false;
  public failRestoreOnce = false;
  public failClear = false;

  public constructor(initial: ActivePricingCartSnapshot) {
    this.value = initial;
  }

  public async runExclusive<T>(
    operation: (lease: ActivePricingCartLeasePort) => T | Promise<T>,
  ): Promise<T> {
    if (this.busy) {
      throw Object.assign(new Error("cart busy"), {
        code: "ACTIVE_PRICING_CART_BUSY",
      });
    }
    const lease: ActivePricingCartLeasePort = {
      read: () => this.value,
      blockForRecallRecovery: (binding) => {
        this.blockCalls.push(binding);
      },
      replace: (pricingState, recallBinding) => {
        if (this.failRestoreOnce) {
          this.failRestoreOnce = false;
          throw new Error("restore failed");
        }
        if (this.failRestore) {
          throw new Error("restore failed");
        }
        if (this.failClear) {
          throw new Error("clear failed");
        }
        this.replaceCalls.push({ pricingState, recallBinding });
        this.value = {
          ...this.value,
          pricingState,
          cart: cartFromPricing(pricingState),
          recallBinding,
        };
      },
      setRecallBinding: (recallBinding) => {
        this.value = { ...this.value, recallBinding };
      },
    };
    return operation(lease);
  }
}

function cartFromPricing(state: PricingCartStateSnapshot): ActivePricingCartSnapshot["cart"] {
  return {
    revision: state.revision,
    mode: state.mode,
    lines: state.lines.map((line) => ({
      ...line,
      quantity: String(line.quantity),
      unitPrice: { currency: "AUD", cents: line.unitPriceCents },
      discount: { currency: "AUD", cents: 0 },
      actualAmount: { currency: "AUD", cents: line.unitPriceCents },
      priceSource: line.basePriceSource,
    })),
    subtotal: { currency: "AUD", cents: 0 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: 0 },
  };
}

class FakeClaims implements SharedHeldOrderClaimRepositoryPort {
  public readonly claims = new Map<string, SharedHeldOrderClaim>();
  public readonly prepareInputs: PreparedClaimInput[] = [];
  public readonly releaseCalls: Readonly<{
    claimGuid: string;
    releaseIdempotencyKey: string;
    expectedState: "Prepared" | "Active";
  }>[] = [];
  public fenceWinner: SharedHeldOrderClaim | null = null;
  public failActivate = false;
  public nextPrepareOutcome: "prepared" | "replayed" | "fence-held" | null = null;

  public async prepareClaim(input: PreparedClaimInput): Promise<PrepareClaimResult> {
    this.prepareInputs.push(input);
    const existing = [...this.claims.values()].find(
      (claim) => claim.prepareIdempotencyKey === input.prepareIdempotencyKey,
    );
    if (existing) return { outcome: "replayed", claim: existing };
    if (this.nextPrepareOutcome === "fence-held" || this.fenceWinner) {
      const winner = this.fenceWinner ?? {
        claimGuid: "winner-claim",
        holdGuid: input.holdGuid,
        recallAttemptId: "winner-attempt",
        scope: input.scope,
        source: "RemoteClaim" as const,
        state: "Prepared" as const,
        prepareIdempotencyKey: "winner-key",
        activateIdempotencyKey: null,
        releaseIdempotencyKey: null,
        supersedeIdempotencyKey: null,
        payload: input.payload,
        serverRevision: null,
        preparedExpiresAtIso: input.preparedExpiresAtIso,
        heldAtIso: input.heldAtIso,
        heldBy: input.heldBy,
        boundOrderGuid: null,
        createdAtIso: input.createdAtIso,
        updatedAtIso: input.createdAtIso,
      };
      return { outcome: "fence-held", winner };
    }
    const claim: SharedHeldOrderClaim = {
      claimGuid: input.claimGuid,
      holdGuid: input.holdGuid,
      recallAttemptId: input.recallAttemptId,
      scope: input.scope,
      source: input.source,
      state: "Prepared",
      prepareIdempotencyKey: input.prepareIdempotencyKey,
      activateIdempotencyKey: null,
      releaseIdempotencyKey: null,
      supersedeIdempotencyKey: null,
      payload: input.payload,
      serverRevision: null,
      preparedExpiresAtIso: input.preparedExpiresAtIso,
      heldAtIso: input.heldAtIso,
      heldBy: input.heldBy,
      boundOrderGuid: null,
      createdAtIso: input.createdAtIso,
      updatedAtIso: input.createdAtIso,
    };
    this.claims.set(claim.claimGuid, claim);
    return { outcome: "prepared", claim };
  }

  public async activatePreparedClaim(input: Readonly<{
    claimGuid: string;
    prepareIdempotencyKey: string;
    activateIdempotencyKey: string;
    serverRevision: number | null;
    activatedAtIso: string;
  }>): Promise<boolean> {
    if (this.failActivate) return false;
    const claim = this.claims.get(input.claimGuid);
    if (!claim || claim.prepareIdempotencyKey !== input.prepareIdempotencyKey) {
      return false;
    }
    this.claims.set(input.claimGuid, {
      ...claim,
      state: "Active",
      activateIdempotencyKey: input.activateIdempotencyKey,
      serverRevision: input.serverRevision,
      updatedAtIso: input.activatedAtIso,
    });
    return true;
  }

  public async bindOrderToActiveClaim(): Promise<boolean> {
    return true;
  }

  public async completeActiveClaim(): Promise<boolean> {
    return true;
  }

  public async releaseClaim(input: Readonly<{
    claimGuid: string;
    releaseIdempotencyKey: string;
    releasedAtIso: string;
    expectedState: "Prepared" | "Active";
  }>): Promise<boolean> {
    this.releaseCalls.push({
      claimGuid: input.claimGuid,
      releaseIdempotencyKey: input.releaseIdempotencyKey,
      expectedState: input.expectedState,
    });
    const claim = this.claims.get(input.claimGuid);
    if (!claim) return false;
    if (claim.state === "Released") {
      return claim.releaseIdempotencyKey === input.releaseIdempotencyKey;
    }
    if (claim.state !== input.expectedState) return false;
    this.claims.set(input.claimGuid, {
      ...claim,
      state: "Released",
      releaseIdempotencyKey: input.releaseIdempotencyKey,
      updatedAtIso: input.releasedAtIso,
    });
    return true;
  }

  public async supersedeClaim(): Promise<boolean> {
    return true;
  }

  public async getClaim(claimGuid: string): Promise<SharedHeldOrderClaim | null> {
    return this.claims.get(claimGuid) ?? null;
  }

  public async listMine(): Promise<readonly SharedHeldOrderClaim[]> {
    return [...this.claims.values()];
  }
}

class FakeApi implements SharedHeldOrderNetworkApiPort {
  public readonly calls: string[] = [];
  public prepareResult: SharedHeldOrderPrepareResult | null = null;
  public activateThrows: unknown = null;
  public activateResultOverride: SharedHeldOrderClaimDto | null = null;
  public beforeActivate: (() => void) | null = null;
  public activateStatus = 2;
  public claimsMineThrows: unknown = null;
  public forceReleaseThrows: unknown = null;
  public releaseThrows: unknown = null;
  public releaseResultOverride: SharedHeldOrderClaimDto | null = null;
  public claimsMineValue: Awaited<ReturnType<SharedHeldOrderNetworkApiPort["claimsMine"]>> = [];

  public async getCapabilities() {
    return {
      enabled: true,
      payloadVersion: 1,
      preparedTtlSeconds: 900,
      forceReleaseSupported: true,
    };
  }

  public async listPending() {
    return [];
  }

  public async publish(_input: Readonly<{
    holdGuid: string;
    storeCode: string;
    deviceCode: string;
    cart: SharedSaleCartV1;
    idempotencyKey: string;
  }>) {
    return {
      holdGuid: "hold-1",
      status: "Pending" as const,
      revision: 1,
      createdAtIso: NOW,
      alreadyExists: false,
    };
  }

  public async prepare(_input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    idempotencyKey: string;
  }>) {
    this.calls.push("prepare");
    return this.prepareResult ?? defaultPrepareResult();
  }

  public async activate(_input: Readonly<{ holdGuid: string; claimGuid: string }>) {
    this.calls.push("activate");
    this.beforeActivate?.();
    if (this.activateThrows !== null) throw this.activateThrows;
    if (this.activateResultOverride !== null) {
      return this.activateResultOverride;
    }
    return {
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      status: (this.activateStatus === 2 ? "Active" : "Prepared") as "Active" | "Prepared",
      storeCode: "BNE",
      claimantDeviceCode: "IPAD-1",
      claimantCashierId: "CASHIER-2",
      claimantCashierName: "Other",
      createdAtIso: NOW,
      updatedAtIso: NOW,
      expiresAtIso: null,
      activatedAtIso: NOW,
      releasedAtIso: null,
      forceReleased: false,
      forceReleaseReason: null,
      forceReleaseCashierId: null,
      forceReleaseCashierName: null,
      forceReleasedAtIso: null,
      revision: 7,
      alreadyExists: false,
    };
  }

  public async release(_input: Readonly<{ holdGuid: string; claimGuid: string }>) {
    this.calls.push("release");
    if (this.releaseThrows !== null) throw this.releaseThrows;
    return this.releaseResultOverride ?? {
      ...defaultActivateResult(),
      holdGuid: _input.holdGuid,
      claimGuid: _input.claimGuid,
      status: "Released" as const,
      updatedAtIso: NOW,
      expiresAtIso: null,
      activatedAtIso: null,
      releasedAtIso: NOW,
      forceReleased: false,
      forceReleaseReason: null,
      forceReleaseCashierId: null,
      forceReleaseCashierName: null,
      forceReleasedAtIso: null,
    };
  }

  public async forceRelease(_input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    reason: string;
  }>) {
    this.calls.push("force-release");
    if (this.forceReleaseThrows !== null) throw this.forceReleaseThrows;
    return {
      ...defaultActivateResult(),
      holdGuid: _input.holdGuid,
      claimGuid: _input.claimGuid,
      status: "Released" as const,
      updatedAtIso: NOW,
      expiresAtIso: null,
      activatedAtIso: null,
      releasedAtIso: NOW,
      forceReleased: true,
      forceReleaseReason: _input.reason,
      forceReleaseCashierId: IDENTITY.cashierId,
      forceReleaseCashierName: IDENTITY.cashierName,
      forceReleasedAtIso: NOW,
    };
  }

  public async claimsMine() {
    this.calls.push("claims-mine");
    if (this.claimsMineThrows !== null) throw this.claimsMineThrows;
    return this.claimsMineValue;
  }
}

class FakeLocalPublications {
  public eligibility: LocalPublicationEligibility = {
    eligible: true,
    cart: sharedCart(),
  };

  public async loadEligible() {
    return this.eligibility;
  }
}

function makeCoordinator(
  overrides: Partial<{
    cart: FakeCart;
    claims: FakeClaims;
    api: FakeApi;
    localPublications: FakeLocalPublications;
  }> = {},
) {
  const cart = overrides.cart ?? new FakeCart(snapshot(0));
  const claims = overrides.claims ?? new FakeClaims();
  const api = overrides.api ?? new FakeApi();
  const localPublications = overrides.localPublications ?? new FakeLocalPublications();
  const coordinator = new SharedHeldOrderCoordinator({
    api,
    claims,
    localPublications,
    activeCart: cart,
    identity: IDENTITY,
    createId: () => "claim-1",
    nowIso: () => NOW,
  });
  return { cart, claims, api, localPublications, coordinator };
}

async function seedOpenClaim(
  claims: FakeClaims,
  input: Readonly<{
    claimGuid: string;
    holdGuid: string;
    source: "RemoteClaim" | "OfflineOrigin";
    state: "Prepared" | "Active";
    preparedExpiresAtIso?: string;
  }>,
): Promise<SharedHeldOrderClaim> {
  const prepared = await claims.prepareClaim({
    claimGuid: input.claimGuid,
    holdGuid: input.holdGuid,
    recallAttemptId: input.claimGuid,
    scope: SCOPE,
    source: input.source,
    prepareIdempotencyKey:
      input.source === "OfflineOrigin"
        ? `ipad-offline:${input.holdGuid}`
        : `prepare:${input.claimGuid}`,
    payload: sharedCart(),
    preparedExpiresAtIso: input.preparedExpiresAtIso ?? NOW,
    heldAtIso: NOW,
    heldBy: { cashierId: IDENTITY.cashierId, cashierName: IDENTITY.cashierName },
    createdAtIso: NOW,
  });
  if (prepared.outcome === "fence-held") {
    throw new Error("unexpected fence winner");
  }
  if (input.state === "Active") {
    const activated = await claims.activatePreparedClaim({
      claimGuid: input.claimGuid,
      prepareIdempotencyKey: prepared.claim.prepareIdempotencyKey,
      activateIdempotencyKey:
        input.source === "OfflineOrigin"
          ? `ipad-offline-activate:${input.claimGuid}`
          : `activate:${input.claimGuid}`,
      serverRevision: input.source === "RemoteClaim" ? 7 : null,
      activatedAtIso: NOW,
    });
    assert.equal(activated, true);
  }
  const claim = await claims.getClaim(input.claimGuid);
  assert.ok(claim);
  return claim;
}

test("在线取单：prepare -> 本地 claim/fence -> activate -> 恢复购物车，顺序固定", async () => {
  const { cart, claims, api, coordinator } = makeCoordinator();
  const result = await coordinator.takeRemoteHold("hold-1");

  assert.deepEqual(api.calls, ["prepare", "activate"]);
  assert.equal(result.outcome, "restored");
  if (result.outcome !== "restored") return;
  assert.equal(result.claimGuid, "claim-1");
  assert.equal(cart.replaceCalls.length, 1);
  assert.equal(cart.replaceCalls[0]?.recallBinding?.holdId, "hold-1");
  assert.equal(cart.replaceCalls[0]?.pricingState.lines.length, 1);
  const claim = await claims.getClaim("claim-1");
  assert.equal(claim?.state, "Active");
  assert.equal(claim?.serverRevision, 7);
  assert.equal(claims.prepareInputs[0]?.source, "RemoteClaim");
});

test("prepare 本地 fence 输家：不 activate、不恢复", async () => {
  const claims = new FakeClaims();
  claims.nextPrepareOutcome = "fence-held";
  const api = new FakeApi();
  const { cart, coordinator } = makeCoordinator({ claims, api });
  const result = await coordinator.takeRemoteHold("hold-1");
  assert.equal(result.outcome, "fence-held");
  assert.deepEqual(api.calls, ["prepare"]);
  assert.equal(cart.replaceCalls.length, 0);
});

test("activate 网络结果未知：保持本地 Prepared、不恢复，后续 claims/mine 调和", async () => {
  const api = new FakeApi();
  api.activateThrows = new SharedHeldOrderApiError("network", {
    kind: "Retryable",
    status: 503,
  });
  const { cart, claims, coordinator } = makeCoordinator({ api });
  const result = await coordinator.takeRemoteHold("hold-1");
  assert.equal(result.outcome, "prepared-awaiting-activation");
  const claim = await claims.getClaim("claim-1");
  assert.equal(claim?.state, "Prepared");
  assert.equal(cart.replaceCalls.length, 0);
});

test("恢复购物车失败：清空购物车、保留 Active，不自动 release", async () => {
  const cart = new FakeCart(snapshot(0));
  cart.failRestoreOnce = true;
  const { claims, coordinator } = makeCoordinator({ cart });
  await assert.rejects(
    coordinator.takeRemoteHold("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "RESTORE_FAILED",
  );
  const claim = await claims.getClaim("claim-1");
  assert.equal(claim?.state, "Active");
  // 失败后显式清空购物车并保留 binding。
  assert.equal(cart.replaceCalls.length, 1);
  assert.equal(cart.replaceCalls[0]?.pricingState.lines.length, 0);
  assert.equal(cart.replaceCalls[0]?.recallBinding?.holdId, "hold-1");
});

test("prepare 后购物车被新交易占用：activate 后 fail-closed，绝不覆盖或清空新购物车", async () => {
  const cart = new FakeCart(snapshot(0));
  const api = new FakeApi();
  api.beforeActivate = () => {
    cart.value = snapshot(1);
  };
  const { claims, coordinator } = makeCoordinator({ cart, api });

  await assert.rejects(
    coordinator.takeRemoteHold("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "CART_NOT_EMPTY",
  );

  assert.equal((await claims.getClaim("claim-1"))?.state, "Active");
  assert.equal(cart.value.pricingState.lines.length, 1);
  assert.equal(cart.value.recallBinding, null);
  assert.equal(cart.replaceCalls.length, 0);
});

test("原设备离线 recall：不访问 API，走 OfflineOrigin claim 并恢复购物车", async () => {
  const { cart, claims, api, coordinator } = makeCoordinator();
  const result = await coordinator.recallLocalPublication("hold-1");
  assert.equal(result.outcome, "restored");
  assert.deepEqual(api.calls, []);
  assert.equal(claims.prepareInputs[0]?.source, "OfflineOrigin");
  assert.equal(cart.replaceCalls[0]?.recallBinding?.holdId, "hold-1");
});

test("崩溃恢复：OfflineOrigin Prepared 先本地激活并恢复，claims/mine 离线不能阻断本地事实", async () => {
  const claims = new FakeClaims();
  await seedOpenClaim(claims, {
    claimGuid: "offline-prepared",
    holdGuid: "hold-offline",
    source: "OfflineOrigin",
    state: "Prepared",
  });
  const api = new FakeApi();
  api.claimsMineThrows = new SharedHeldOrderApiError("offline", {
    kind: "Retryable",
  });
  const { cart, coordinator } = makeCoordinator({ claims, api });

  await assert.rejects(coordinator.reconcileClaims(), /offline/);

  const recovered = await claims.getClaim("offline-prepared");
  assert.equal(recovered?.state, "Active");
  assert.equal(
    recovered?.activateIdempotencyKey,
    "ipad-offline-activate:offline-prepared",
  );
  assert.equal(cart.value.recallBinding?.recallAttemptId, "offline-prepared");
  assert.equal(cart.value.pricingState.lines.length, 1);
});

test("崩溃恢复：OfflineOrigin Active 在服务端无 claim 时仍恢复购物车", async () => {
  const claims = new FakeClaims();
  await seedOpenClaim(claims, {
    claimGuid: "offline-active",
    holdGuid: "hold-offline",
    source: "OfflineOrigin",
    state: "Active",
  });
  const api = new FakeApi();
  api.claimsMineValue = [];
  const { cart, coordinator } = makeCoordinator({ claims, api });

  const result = await coordinator.reconcileClaims();

  assert.deepEqual(result.restoredClaimIds, ["offline-active"]);
  assert.deepEqual(result.mismatches, []);
  assert.equal(cart.value.recallBinding?.recallAttemptId, "offline-active");
  assert.equal(cart.value.pricingState.lines.length, 1);
});

test("崩溃恢复：购物车已带相同 claim binding 与冻结快照时按幂等成功，不重复交换", async () => {
  const claims = new FakeClaims();
  const claim = await seedOpenClaim(claims, {
    claimGuid: "offline-restored",
    holdGuid: "hold-offline",
    source: "OfflineOrigin",
    state: "Active",
  });
  const pricingState = fromSharedSaleCartV1(claim.payload);
  const cart = new FakeCart({
    ...snapshot(0),
    pricingState,
    cart: cartFromPricing(pricingState),
    recallBinding: {
      kind: "recalled",
      scope: SCOPE,
      holdId: claim.holdGuid,
      recallAttemptId: claim.recallAttemptId,
    },
  });
  const { coordinator } = makeCoordinator({ claims, cart });

  const result = await coordinator.reconcileClaims();

  assert.deepEqual(result.restoredClaimIds, ["offline-restored"]);
  assert.equal(cart.replaceCalls.length, 0);
});

test("主管强制释放：Remote Active 先服务端释放，再精确清理匹配购物车和本地 fence", async () => {
  const claims = new FakeClaims();
  const claim = await seedOpenClaim(claims, {
    claimGuid: "claim-force",
    holdGuid: "hold-force",
    source: "RemoteClaim",
    state: "Active",
  });
  const cart = new FakeCart(
    snapshot(1, {
      kind: "recalled",
      scope: SCOPE,
      holdId: claim.holdGuid,
      recallAttemptId: claim.recallAttemptId,
    }),
  );
  const api = new FakeApi();
  const { coordinator } = makeCoordinator({ claims, cart, api });

  const released = await coordinator.forceRelease("hold-force", " duplicate claim ");

  assert.equal(released.claimGuid, "claim-force");
  assert.deepEqual(api.calls, ["force-release"]);
  assert.equal((await claims.getClaim("claim-force"))?.state, "Released");
  assert.equal(cart.value.recallBinding, null);
  assert.equal(cart.value.pricingState.lines.length, 0);
});

test("主管强制释放：Prepared 不清理当前购物车；API 失败则任何本地事实都不推进", async () => {
  const preparedClaims = new FakeClaims();
  await seedOpenClaim(preparedClaims, {
    claimGuid: "claim-prepared",
    holdGuid: "hold-prepared",
    source: "RemoteClaim",
    state: "Prepared",
  });
  const preparedCart = new FakeCart(snapshot(1));
  const preparedCoordinator = makeCoordinator({
    claims: preparedClaims,
    cart: preparedCart,
  }).coordinator;

  await preparedCoordinator.forceRelease("hold-prepared", "stale prepare");

  assert.equal((await preparedClaims.getClaim("claim-prepared"))?.state, "Released");
  assert.equal(preparedCart.value.pricingState.lines.length, 1);

  const activeClaims = new FakeClaims();
  const active = await seedOpenClaim(activeClaims, {
    claimGuid: "claim-active",
    holdGuid: "hold-active",
    source: "RemoteClaim",
    state: "Active",
  });
  const activeCart = new FakeCart(
    snapshot(1, {
      kind: "recalled",
      scope: SCOPE,
      holdId: active.holdGuid,
      recallAttemptId: active.recallAttemptId,
    }),
  );
  const api = new FakeApi();
  api.forceReleaseThrows = new SharedHeldOrderApiError("offline", {
    kind: "Retryable",
  });
  const activeCoordinator = makeCoordinator({
    claims: activeClaims,
    cart: activeCart,
    api,
  }).coordinator;

  await assert.rejects(
    activeCoordinator.forceRelease("hold-active", "network retry"),
    /offline/,
  );
  assert.equal((await activeClaims.getClaim("claim-active"))?.state, "Active");
  assert.equal(activeCart.value.recallBinding?.recallAttemptId, "claim-active");
  assert.equal(activeCart.value.pricingState.lines.length, 1);
});

test("主管强制释放：Active 购物车 binding 不匹配时保留本地 claim 与购物车等待人工重试", async () => {
  const claims = new FakeClaims();
  await seedOpenClaim(claims, {
    claimGuid: "claim-force",
    holdGuid: "hold-force",
    source: "RemoteClaim",
    state: "Active",
  });
  const cart = new FakeCart(
    snapshot(1, {
      kind: "recalled",
      scope: SCOPE,
      holdId: "another-hold",
      recallAttemptId: "another-claim",
    }),
  );
  const { coordinator } = makeCoordinator({ claims, cart });

  await assert.rejects(
    coordinator.forceRelease("hold-force", "mismatched fence"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "FENCE_CONFLICT",
  );
  assert.equal((await claims.getClaim("claim-force"))?.state, "Active");
  assert.equal(cart.value.recallBinding?.recallAttemptId, "another-claim");
  assert.equal(cart.value.pricingState.lines.length, 1);
});

test("本地副本不可用：NOT_FOUND 拒绝，不访问 API", async () => {
  const localPublications = new FakeLocalPublications();
  localPublications.eligibility = { eligible: false, reason: "not-found" };
  const api = new FakeApi();
  const { coordinator } = makeCoordinator({ localPublications, api });
  await assert.rejects(
    coordinator.recallLocalPublication("hold-missing"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "NOT_FOUND",
  );
  assert.deepEqual(api.calls, []);
});

test("对账：服务端 Active + 本地 Prepared 补激活并恢复；服务端 Prepared 幂等保存；终态不自动释放", async () => {
  const claims = new FakeClaims();
  // 本地已有 Prepared claim（崩溃窗口）。
  await claims.prepareClaim({
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    recallAttemptId: "claim-1",
    scope: SCOPE,
    source: "RemoteClaim",
    prepareIdempotencyKey: "prepare-key",
    payload: sharedCart(),
    preparedExpiresAtIso: "2026-07-28T09:00:00.000Z",
    heldAtIso: NOW,
    heldBy: { cashierId: "CASHIER-1", cashierName: "Cashier" },
    createdAtIso: NOW,
  });
  const api = new FakeApi();
  api.claimsMineValue = [
    {
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      status: "Active",
      storeCode: "BNE",
      claimantDeviceCode: "IPAD-1",
      claimantCashierId: "CASHIER-2",
      claimantCashierName: "Other",
      payload: sharedCart(),
      createdAtIso: NOW,
      updatedAtIso: NOW,
      expiresAtIso: null,
      activatedAtIso: NOW,
      revision: 9,
    },
    {
      holdGuid: "hold-2",
      claimGuid: "claim-2",
      status: "Prepared",
      storeCode: "BNE",
      claimantDeviceCode: "IPAD-1",
      claimantCashierId: "CASHIER-2",
      claimantCashierName: "Other",
      payload: sharedCart(),
      createdAtIso: NOW,
      updatedAtIso: NOW,
      expiresAtIso: "2026-07-28T08:30:00.000Z",
      activatedAtIso: null,
      revision: 1,
    },
    {
      holdGuid: "hold-3",
      claimGuid: "claim-3",
      status: "Released",
      storeCode: "BNE",
      claimantDeviceCode: "IPAD-1",
      claimantCashierId: "CASHIER-2",
      claimantCashierName: "Other",
      payload: sharedCart(),
      createdAtIso: NOW,
      updatedAtIso: NOW,
      expiresAtIso: null,
      activatedAtIso: null,
      revision: 2,
    },
  ];
  const { cart, coordinator } = makeCoordinator({ claims, api });
  const result = await coordinator.reconcileClaims();

  assert.deepEqual(result.restoredClaimIds, ["claim-1"]);
  assert.deepEqual(result.reconciledPreparedClaimIds, ["claim-2"]);
  // 服务端终态且本地无 open claim：无事实可保留，也不创建/释放任何东西。
  assert.equal(result.mismatches.length, 0);
  const local1 = await claims.getClaim("claim-1");
  assert.equal(local1?.state, "Active");
  assert.equal(local1?.serverRevision, 9);
  const local2 = await claims.getClaim("claim-2");
  assert.equal(local2?.state, "Prepared");
  assert.equal(cart.replaceCalls[0]?.recallBinding?.holdId, "hold-1");
});

test("对账：服务端 Active 但本地无事实或 HoldGuid 不一致：fail-closed，不恢复不 release", async () => {
  const api = new FakeApi();
  api.claimsMineValue = [
    {
      holdGuid: "hold-x",
      claimGuid: "claim-x",
      status: "Active",
      storeCode: "BNE",
      claimantDeviceCode: "IPAD-2",
      claimantCashierId: "CASHIER-2",
      claimantCashierName: "Other",
      payload: sharedCart(),
      createdAtIso: NOW,
      updatedAtIso: NOW,
      expiresAtIso: null,
      activatedAtIso: NOW,
      revision: 1,
    },
  ];
  const { cart, coordinator } = makeCoordinator({ api });
  const result = await coordinator.reconcileClaims();
  assert.deepEqual(result.restoredClaimIds, []);
  assert.equal(result.mismatches.length, 1);
  assert.equal(cart.replaceCalls.length, 0);
});

test("购物车非空时拒绝在线取单，不访问 API", async () => {
  const cart = new FakeCart(snapshot(1));
  const api = new FakeApi();
  const { coordinator } = makeCoordinator({ cart, api });
  await assert.rejects(
    coordinator.takeRemoteHold("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "CART_NOT_EMPTY",
  );
  assert.deepEqual(api.calls, []);
});

test("并发 mutation：第二项操作直接拒绝，避免重复取单", async () => {
  const api = new FakeApi();
  let releaseActivate!: () => void;
  const gate = new Promise<void>((resolve) => {
    releaseActivate = resolve;
  });
  const originalActivate = api.activate.bind(api);
  api.activate = async (_input: Readonly<{ holdGuid: string; claimGuid: string }>) => {
    await gate;
    return originalActivate(_input);
  };
  const { coordinator } = makeCoordinator({ api });
  const first = coordinator.takeRemoteHold("hold-1");
  await Promise.resolve();
  await assert.rejects(
    coordinator.takeRemoteHold("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "CONFLICT",
  );
  releaseActivate();
  await first;
});

test("在线取单：prepare 响应与请求不匹配时在 durable fence 前 fail-closed", async () => {
  const cases: readonly {
    name: string;
    patch: (result: SharedHeldOrderPrepareResult) => SharedHeldOrderPrepareResult;
  }[] = [
    {
      name: "holdGuid",
      patch: (result) => ({ ...result, holdGuid: "hold-other" }),
    },
    {
      name: "claimGuid",
      patch: (result) => ({ ...result, claimGuid: "claim-other" }),
    },
    {
      name: "claimantDeviceCode",
      patch: (result) => ({ ...result, claimantDeviceCode: "IPAD-2" }),
    },
  ];

  for (const { name, patch } of cases) {
    const claims = new FakeClaims();
    const api = new FakeApi();
    api.prepareResult = patch(defaultPrepareResult());
    const { cart, coordinator } = makeCoordinator({ claims, api });
    await assert.rejects(
      coordinator.takeRemoteHold("hold-1"),
      (error: unknown) =>
        error instanceof SharedHeldOrderCoordinatorError &&
        error.code === "INVALID",
      name,
    );
    assert.deepEqual(api.calls, ["prepare"], name);
    assert.equal(claims.prepareInputs.length, 0, name);
    assert.equal(cart.replaceCalls.length, 0, name);
  }
});

test("在线取单：activate 响应与请求不匹配时 fail-closed，本地保持 Prepared 不恢复", async () => {
  const cases: readonly {
    name: string;
    patch: (result: SharedHeldOrderClaimDto) => SharedHeldOrderClaimDto;
  }[] = [
    {
      name: "holdGuid",
      patch: (result) => ({ ...result, holdGuid: "hold-other" }),
    },
    {
      name: "claimGuid",
      patch: (result) => ({ ...result, claimGuid: "claim-other" }),
    },
    {
      name: "storeCode",
      patch: (result) => ({ ...result, storeCode: "SYD" }),
    },
    {
      name: "claimantDeviceCode",
      patch: (result) => ({ ...result, claimantDeviceCode: "IPAD-2" }),
    },
    {
      name: "status",
      patch: (result) => ({ ...result, status: "Prepared" }),
    },
  ];

  for (const { name, patch } of cases) {
    const claims = new FakeClaims();
    const api = new FakeApi();
    api.activateResultOverride = patch(defaultActivateResult());
    const { cart, coordinator } = makeCoordinator({ claims, api });
    await assert.rejects(
      coordinator.takeRemoteHold("hold-1"),
      (error: unknown) =>
        error instanceof SharedHeldOrderCoordinatorError &&
        error.code === "INVALID",
      name,
    );
    const claim = await claims.getClaim("claim-1");
    assert.equal(claim?.state, "Prepared", name);
    assert.equal(cart.replaceCalls.length, 0, name);
  }
});

test("在线取单：activate 非 Retryable 业务错误向上抛，本地保持 Prepared", async () => {
  const kinds: readonly SharedHeldOrderApiErrorKind[] = [
    "Conflict",
    "Forbidden",
    "Invalid",
    "Disabled",
  ];
  for (const kind of kinds) {
    const api = new FakeApi();
    api.activateThrows = new SharedHeldOrderApiError(`rejected: ${kind}`, { kind });
    const { claims, coordinator } = makeCoordinator({ api });
    await assert.rejects(
      coordinator.takeRemoteHold("hold-1"),
      (error: unknown) =>
        error instanceof SharedHeldOrderApiError && error.kind === kind,
      kind,
    );
    const claim = await claims.getClaim("claim-1");
    assert.equal(claim?.state, "Prepared", kind);
  }
});

test("在线取单：activate 未知非 API 程序错误向上抛，不被吞掉", async () => {
  const api = new FakeApi();
  api.activateThrows = new Error("program bug");
  const { claims, coordinator } = makeCoordinator({ api });
  await assert.rejects(coordinator.takeRemoteHold("hold-1"), /program bug/);
  const claim = await claims.getClaim("claim-1");
  assert.equal(claim?.state, "Prepared");
});

test("在线取单：prepare 缺 expiresAt 时本地 Prepared 冻结 TTL 兜底 120 秒", async () => {
  const api = new FakeApi();
  api.prepareResult = { ...defaultPrepareResult(), expiresAtIso: null };
  const { claims, coordinator } = makeCoordinator({ api });
  const result = await coordinator.takeRemoteHold("hold-1");
  assert.equal(result.outcome, "restored");
  assert.equal(
    claims.prepareInputs[0]?.preparedExpiresAtIso,
    "2026-07-28T08:02:00.000Z",
  );
});

test("对账：服务端 Prepared 缺 expiresAt 时本地 Prepared 冻结 TTL 兜底 120 秒", async () => {
  const api = new FakeApi();
  api.claimsMineValue = [
    {
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      status: "Prepared",
      storeCode: "BNE",
      claimantDeviceCode: "IPAD-1",
      claimantCashierId: "CASHIER-2",
      claimantCashierName: "Other",
      payload: sharedCart(),
      createdAtIso: NOW,
      updatedAtIso: NOW,
      expiresAtIso: null,
      activatedAtIso: null,
      revision: 1,
    },
  ];
  const { claims, coordinator } = makeCoordinator({ api });
  const result = await coordinator.reconcileClaims();
  assert.deepEqual(result.reconciledPreparedClaimIds, ["claim-1"]);
  assert.equal(
    claims.prepareInputs[0]?.preparedExpiresAtIso,
    "2026-07-28T08:02:00.000Z",
  );
});

test("对账：无本地事实的 Prepared 必须先校验 store/device，跨 scope 不落 durable claim", async () => {
  const api = new FakeApi();
  api.claimsMineValue = [
    {
      holdGuid: "hold-1",
      claimGuid: "claim-1",
      status: "Prepared",
      storeCode: "SYD",
      claimantDeviceCode: "IPAD-2",
      claimantCashierId: "CASHIER-2",
      claimantCashierName: "Other",
      payload: sharedCart(),
      createdAtIso: NOW,
      updatedAtIso: NOW,
      expiresAtIso: "2026-07-28T08:02:00.000Z",
      activatedAtIso: null,
      revision: 1,
    },
  ];
  const { claims, coordinator } = makeCoordinator({ api });

  const result = await coordinator.reconcileClaims();

  assert.deepEqual(result.reconciledPreparedClaimIds, []);
  assert.equal(result.mismatches.length, 1);
  assert.equal(claims.prepareInputs.length, 0);
  assert.equal(await claims.getClaim("claim-1"), null);
});

test("对账：RemoteClaim store/device/source/hold/payload 任一不一致 fail-closed", async () => {
  const variants: readonly {
    name: string;
    patchLocal?: (claim: SharedHeldOrderClaim) => SharedHeldOrderClaim;
    patchServer?: (
      claim: SharedHeldOrderRecoveryClaimDto,
    ) => SharedHeldOrderRecoveryClaimDto;
  }[] = [
    {
      name: "payload",
      patchServer: (server) => ({
        ...server,
        payload: {
          ...sharedCart(),
          pricingState: { ...sharedCart().pricingState, revision: 99 },
        },
      }),
    },
    {
      name: "storeCode",
      patchServer: (server) => ({ ...server, storeCode: "SYD" }),
    },
    {
      name: "claimantDeviceCode",
      patchServer: (server) => ({ ...server, claimantDeviceCode: "IPAD-2" }),
    },
    {
      name: "holdGuid",
      patchServer: (server) => ({ ...server, holdGuid: "hold-other" }),
    },
    {
      name: "source",
      patchLocal: (claim) => ({ ...claim, source: "OfflineOrigin" }),
    },
  ];

  for (const { name, patchLocal, patchServer } of variants) {
    const claims = new FakeClaims();
    const local = await claims.prepareClaim({
      claimGuid: "claim-1",
      holdGuid: "hold-1",
      recallAttemptId: "claim-1",
      scope: SCOPE,
      source: "RemoteClaim",
      prepareIdempotencyKey: "prepare-key",
      payload: sharedCart(),
      preparedExpiresAtIso: "2026-07-28T09:00:00.000Z",
      heldAtIso: NOW,
      heldBy: { cashierId: "CASHIER-1", cashierName: "Cashier" },
      createdAtIso: NOW,
    });
    if (local.outcome === "fence-held") {
      throw new Error("unexpected fence");
    }
    await claims.activatePreparedClaim({
      claimGuid: "claim-1",
      prepareIdempotencyKey: "prepare-key",
      activateIdempotencyKey: "activate-key",
      serverRevision: 9,
      activatedAtIso: NOW,
    });
    if (patchLocal) {
      const claim = await claims.getClaim("claim-1");
      if (claim) claims.claims.set("claim-1", patchLocal(claim));
    }
    const api = new FakeApi();
    const server = serverActiveClaim("claim-1", "hold-1");
    api.claimsMineValue = [patchServer ? patchServer(server) : server];
    const { cart, coordinator } = makeCoordinator({ claims, api });
    const result = await coordinator.reconcileClaims();
    assert.deepEqual(result.restoredClaimIds, [], name);
    assert.equal(result.mismatches.length, 1, name);
    assert.ok(result.mismatches[0]?.reason.includes("不一致"), name);
    assert.equal(cart.replaceCalls.length, 0, name);
  }
});

test("对账：OfflineOrigin 本地 claim 在服务端缺失时本地激活恢复且不算错误", async () => {
  const claims = new FakeClaims();
  await claims.prepareClaim({
    claimGuid: "offline-1",
    holdGuid: "hold-offline",
    recallAttemptId: "offline-1",
    scope: SCOPE,
    source: "OfflineOrigin",
    prepareIdempotencyKey: "offline-key",
    payload: sharedCart(),
    preparedExpiresAtIso: NOW,
    heldAtIso: NOW,
    heldBy: { cashierId: "CASHIER-1", cashierName: "Cashier" },
    createdAtIso: NOW,
  });
  const api = new FakeApi();
  api.claimsMineValue = [];
  const { coordinator } = makeCoordinator({ claims, api });
  const result = await coordinator.reconcileClaims();
  assert.deepEqual(result.mismatches, []);
  assert.deepEqual(result.restoredClaimIds, ["offline-1"]);
  const claim = await claims.getClaim("offline-1");
  assert.equal(claim?.state, "Active");
});

test("对账：恢复失败不删除之前成功恢复的 claim id（回归）", async () => {
  const claims = new FakeClaims();
  for (const [claimGuid, holdGuid] of [
    ["claim-1", "hold-1"],
    ["claim-2", "hold-2"],
  ] as const) {
    const local = await claims.prepareClaim({
      claimGuid,
      holdGuid,
      recallAttemptId: claimGuid,
      scope: SCOPE,
      source: "RemoteClaim",
      prepareIdempotencyKey: `prepare-${claimGuid}`,
      payload: sharedCart(),
      preparedExpiresAtIso: "2026-07-28T09:00:00.000Z",
      heldAtIso: NOW,
      heldBy: { cashierId: "CASHIER-1", cashierName: "Cashier" },
      createdAtIso: NOW,
    });
    if (local.outcome === "fence-held") {
      throw new Error("unexpected fence");
    }
    await claims.activatePreparedClaim({
      claimGuid,
      prepareIdempotencyKey: `prepare-${claimGuid}`,
      activateIdempotencyKey: `activate-${claimGuid}`,
      serverRevision: 9,
      activatedAtIso: NOW,
    });
  }
  const api = new FakeApi();
  api.claimsMineValue = [
    serverActiveClaim("claim-1", "hold-1"),
    serverActiveClaim("claim-2", "hold-2"),
  ];
  const { cart, coordinator } = makeCoordinator({ claims, api });
  const result = await coordinator.reconcileClaims();
  // claim-1 先恢复成功；claim-2 因 cart 已有 recall binding 恢复失败，
  // 失败路径不得 splice(-1) 把 claim-1 从成功列表里删掉。
  assert.deepEqual(result.restoredClaimIds, ["claim-1"]);
  assert.equal(result.mismatches.length, 1);
  assert.equal(result.mismatches[0]?.claimGuid, "claim-2");
  // 第二项看到第一项已经占有 cart binding 后 fail-closed，不再执行清车交换。
  assert.equal(cart.replaceCalls.length, 1);
});

test("过期 Prepared RemoteClaim 在下一次 prepare 前先本地释放并清 fence，新 claim 可继续", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-expired",
    holdGuid: "hold-expired",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });

  const result = await coordinator.takeRemoteHold("hold-1");

  // 过期 Prepared 必须先本地推进 Released，fence 让位后新 claim 才能继续。
  assert.equal(result.outcome, "restored");
  assert.equal(api.calls.includes("prepare"), true);
  assert.deepEqual(claims.releaseCalls, [
    {
      claimGuid: "claim-expired",
      releaseIdempotencyKey: "ipad-expire:claim-expired",
      expectedState: "Prepared",
    },
  ]);
  const expired = await claims.getClaim("claim-expired");
  assert.equal(expired?.state, "Released");
  assert.equal(expired?.releaseIdempotencyKey, "ipad-expire:claim-expired");
  assert.equal((await claims.getClaim("claim-1"))?.state, "Active");
});

test("过期 Prepared 在 reconcile 前本地释放，不依赖 claims/mine 返回终态", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-expired",
    holdGuid: "hold-expired",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  // claims/mine 不返回终态（服务端已过期但未列出，或完全离线语义）。
  api.claimsMineValue = [];

  const result = await coordinator.reconcileClaims();

  const expired = await claims.getClaim("claim-expired");
  assert.equal(expired?.state, "Released");
  assert.equal(expired?.releaseIdempotencyKey, "ipad-expire:claim-expired");
  assert.deepEqual(result.mismatches, []);
  assert.deepEqual(result.reconciledPreparedClaimIds, []);
});

test("服务端仍返回 Prepared：以服务端阻塞事实为准，保留本地 Prepared", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-expired",
    holdGuid: "hold-expired",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  const server = serverActiveClaim("claim-expired", "hold-expired");
  api.claimsMineValue = [
    {
      ...server,
      status: "Prepared",
      payload: sharedCart(),
      expiresAtIso: "2026-07-28T07:30:00.000Z",
      activatedAtIso: null,
    },
  ];

  const result = await coordinator.reconcileClaims();

  const preserved = await claims.getClaim("claim-expired");
  assert.equal(preserved?.state, "Prepared");
  assert.equal(preserved?.releaseIdempotencyKey, null);
  assert.deepEqual(claims.releaseCalls, []);
  assert.equal(result.mismatches.length, 0);
  assert.equal(result.reconciledPreparedClaimIds.includes("claim-expired"), true);
});

test("过期释放崩溃重放：已 Released + 相同 expire key 幂等，新 claim 可继续", async () => {
  const { claims, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-expired",
    holdGuid: "hold-expired",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  // 模拟崩溃发生在 releaseClaim 成功之后、下次操作之前。
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-expired",
      releaseIdempotencyKey: "ipad-expire:claim-expired",
      releasedAtIso: "2026-07-28T07:45:00.000Z",
      expectedState: "Prepared",
    }),
    true,
  );
  claims.releaseCalls.length = 0;

  const result = await coordinator.takeRemoteHold("hold-1");

  assert.equal(result.outcome, "restored");
  const expired = await claims.getClaim("claim-expired");
  assert.equal(expired?.state, "Released");
  assert.equal(expired?.releaseIdempotencyKey, "ipad-expire:claim-expired");
});

test("Active 永不自动释放：即使 preparedExpiresAt 已过也不触发", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-active",
    holdGuid: "hold-active",
    source: "RemoteClaim",
    state: "Active",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineValue = [
    {
      ...serverActiveClaim("claim-active", "hold-active"),
      payload: sharedCart(),
    },
  ];

  await coordinator.reconcileClaims();

  const active = await claims.getClaim("claim-active");
  assert.equal(active?.state, "Active");
  assert.deepEqual(claims.releaseCalls, []);
});

test("未过期 Prepared 不释放，并以本地 fence 阻止新取单", async () => {
  const { claims, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-future",
    holdGuid: "hold-future",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T08:01:00.000Z",
  });
  await assert.rejects(
    () => coordinator.takeRemoteHold("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "FENCE_CONFLICT",
  );
  assert.deepEqual(claims.releaseCalls, []);
  assert.equal((await claims.getClaim("claim-future"))?.state, "Prepared");
});

test("OfflineOrigin Prepared 过期时间不触发 sweep：claims/mine 离线仍本地激活恢复", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-offline",
    holdGuid: "hold-offline",
    source: "OfflineOrigin",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineThrows = new Error("offline");

  await assert.rejects(
    () => coordinator.reconcileClaims(),
    /offline/,
  );

  assert.deepEqual(claims.releaseCalls, []);
  const offline = await claims.getClaim("claim-offline");
  assert.equal(offline?.state, "Active");
  assert.equal(offline?.releaseIdempotencyKey, null);
});

test("过期 Prepared 但服务端已 Active（activate 成功本地未激活）：绝不本地过期，reconcile 补激活", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-active-window",
    holdGuid: "hold-active-window",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineValue = [
    {
      ...serverActiveClaim("claim-active-window", "hold-active-window"),
      payload: sharedCart(),
    },
  ];

  const result = await coordinator.reconcileClaims();

  // 崩溃窗口：本地 Prepared 不能因 expiry 被误释放；reconcile 补激活并恢复。
  assert.deepEqual(claims.releaseCalls, []);
  assert.deepEqual(result.restoredClaimIds, ["claim-active-window"]);
  const claim = await claims.getClaim("claim-active-window");
  assert.equal(claim?.state, "Active");
  assert.equal(claim?.releaseIdempotencyKey, null);
});

test("过期 Prepared 且 claims/mine 失败：保留 fence fail-closed，不释放（reconcile）", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-stale-offline",
    holdGuid: "hold-stale-offline",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineThrows = new Error("offline");

  await assert.rejects(
    () => coordinator.reconcileClaims(),
    /offline/,
  );
  assert.deepEqual(claims.releaseCalls, []);
  assert.equal((await claims.getClaim("claim-stale-offline"))?.state, "Prepared");
});

test("过期 Prepared 且 claims/mine 失败：下一次 prepare 也不释放，fence 保持（fail-closed）", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-stale-offline-2",
    holdGuid: "hold-stale-offline-2",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineThrows = new Error("offline");

  await assert.rejects(
    () => coordinator.takeRemoteHold("hold-1"),
    /offline/,
  );

  assert.deepEqual(claims.releaseCalls, []);
  assert.equal((await claims.getClaim("claim-stale-offline-2"))?.state, "Prepared");
  assert.equal(api.calls.includes("prepare"), false);
  assert.equal(api.calls.includes("activate"), false);
});

test("过期 Prepared 仍被服务端确认为 Prepared：保留 fence 且不创建新 claim", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-stale-server-prepared",
    holdGuid: "hold-stale-server-prepared",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineValue = [
    {
      ...serverActiveClaim(
        "claim-stale-server-prepared",
        "hold-stale-server-prepared",
      ),
      status: "Prepared",
      activatedAtIso: null,
    },
  ];

  await assert.rejects(
    () => coordinator.takeRemoteHold("hold-new"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "FENCE_CONFLICT",
  );

  assert.deepEqual(api.calls, ["claims-mine"]);
  assert.deepEqual(claims.releaseCalls, []);
  assert.equal(
    (await claims.getClaim("claim-stale-server-prepared"))?.state,
    "Prepared",
  );
});

test("invalid expiresAt fail-closed：Date.parse NaN 绝不当作已过期释放", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-invalid-expiry",
    holdGuid: "hold-invalid-expiry",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "not-a-date",
  });
  api.claimsMineValue = [];

  await coordinator.reconcileClaims();

  assert.deepEqual(claims.releaseCalls, []);
  const claim = await claims.getClaim("claim-invalid-expiry");
  assert.equal(claim?.state, "Prepared");
  assert.equal(claim?.releaseIdempotencyKey, null);
});

test("reconcile 复用同一次 claims/mine：过期清扫不重复请求", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-reuse",
    holdGuid: "hold-reuse",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineValue = [];

  await coordinator.reconcileClaims();

  assert.equal(
    api.calls.filter((call) => call === "claims-mine").length,
    1,
  );
  assert.equal((await claims.getClaim("claim-reuse"))?.state, "Released");
});

test("服务端终态 + 本地过期 Prepared：清理本地 fence", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-terminal",
    holdGuid: "hold-terminal",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T07:30:00.000Z",
  });
  api.claimsMineValue = [
    {
      ...serverActiveClaim("claim-terminal", "hold-terminal"),
      status: "Released",
      activatedAtIso: null,
    },
  ];

  const result = await coordinator.reconcileClaims();

  assert.equal(claims.releaseCalls.length, 1);
  assert.equal((await claims.getClaim("claim-terminal"))?.state, "Released");
  assert.equal(result.mismatches.length, 0);
});

test("未过期 Prepared 已占用本地 fence：不请求 claims/mine，也不 prepare", async () => {
  const { claims, api, coordinator } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-future-2",
    holdGuid: "hold-future-2",
    source: "RemoteClaim",
    state: "Prepared",
    preparedExpiresAtIso: "2026-07-28T08:01:00.000Z",
  });

  await assert.rejects(
    () => coordinator.takeRemoteHold("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "FENCE_CONFLICT",
  );

  assert.equal(api.calls.includes("claims-mine"), false);
  assert.equal(api.calls.includes("prepare"), false);
  assert.deepEqual(claims.releaseCalls, []);
});

function boundCartFor(claim: SharedHeldOrderClaim): FakeCart {
  return new FakeCart(
    snapshot(1, {
      kind: "recalled",
      scope: claim.scope,
      holdId: claim.holdGuid,
      recallAttemptId: claim.recallAttemptId,
    }),
  );
}

test("普通清车 owner release：RemoteClaim 先服务端 release 成功，再本地 claim/fence/cart 清理", async () => {
  const { claims, api } = makeCoordinator();
  const claim = await seedOpenClaim(claims, {
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Active",
  });
  const cart = boundCartFor(claim);
  const owned = makeCoordinator({ cart, claims, api });

  const result = await owned.coordinator.ownerRelease("hold-1");

  assert.deepEqual(result, { claimGuid: "claim-1", holdGuid: "hold-1" });
  assert.deepEqual(api.calls, ["release"]);
  assert.equal(api.calls.includes("force-release"), false);
  assert.equal(claims.releaseCalls.length, 1);
  assert.equal(claims.releaseCalls[0]?.releaseIdempotencyKey, "ipad-owner-release:claim-1");
  const released = await claims.getClaim("claim-1");
  assert.equal(released?.state, "Released");
  assert.equal(released?.releaseIdempotencyKey, "ipad-owner-release:claim-1");
  assert.equal(cart.value.cart.lines.length, 0);
  assert.equal(cart.value.recallBinding, null);
});

test("普通清车 owner release：服务端失败保持购物车和 binding，本地事实不动", async () => {
  const { claims, api } = makeCoordinator();
  const claim = await seedOpenClaim(claims, {
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Active",
  });
  const cart = boundCartFor(claim);
  const owned = makeCoordinator({ cart, claims, api });
  api.releaseThrows = new SharedHeldOrderApiError("down", { kind: "Retryable" });

  await assert.rejects(
    () => owned.coordinator.ownerRelease("hold-1"),
    SharedHeldOrderApiError,
  );
  assert.deepEqual(claims.releaseCalls, []);
  assert.equal(cart.replaceCalls.length, 0);
  assert.equal(cart.value.cart.lines.length, 1);
  assert.equal(cart.value.recallBinding?.holdId, "hold-1");
  assert.equal((await claims.getClaim("claim-1"))?.state, "Active");
});

test("普通清车 owner release：OfflineOrigin 不访问 API，本地原子 release/clear", async () => {
  const { claims, api } = makeCoordinator();
  const claim = await seedOpenClaim(claims, {
    claimGuid: "claim-offline",
    holdGuid: "hold-offline",
    source: "OfflineOrigin",
    state: "Active",
  });
  const cart = boundCartFor(claim);
  const owned = makeCoordinator({ cart, claims, api });

  await owned.coordinator.ownerRelease("hold-offline");

  assert.deepEqual(api.calls, []);
  const released = await claims.getClaim("claim-offline");
  assert.equal(released?.state, "Released");
  assert.equal(released?.releaseIdempotencyKey, "ipad-owner-release:claim-offline");
  assert.equal(cart.value.cart.lines.length, 0);
  assert.equal(cart.value.recallBinding, null);
});

test("普通清车 owner release：Prepared 不触碰购物车，服务端 release 后本地 release", async () => {
  const { claims, api } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Prepared",
  });
  const cart = new FakeCart(snapshot(0));
  const owned = makeCoordinator({ cart, claims, api });

  await owned.coordinator.ownerRelease("hold-1");

  assert.deepEqual(api.calls, ["release"]);
  assert.equal(cart.replaceCalls.length, 0);
  assert.equal((await claims.getClaim("claim-1"))?.state, "Released");
});

test("普通清车 owner release 崩溃重放：本地已 Released + 购物车仍绑定 → 跳过服务端并完成清车", async () => {
  const { claims, api } = makeCoordinator();
  const claim = await seedOpenClaim(claims, {
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Active",
  });
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-1",
      releaseIdempotencyKey: "ipad-owner-release:claim-1",
      releasedAtIso: NOW,
      expectedState: "Active",
    }),
    true,
  );
  const cart = boundCartFor(claim);
  const owned = makeCoordinator({ cart, claims, api });

  await owned.coordinator.ownerRelease("hold-1");

  assert.deepEqual(api.calls, []);
  assert.equal((await claims.getClaim("claim-1"))?.state, "Released");
  assert.equal(cart.value.cart.lines.length, 0);
  assert.equal(cart.value.recallBinding, null);
});

test("普通清车 owner release：购物车 binding 不匹配拒绝，claim 与购物车保持原状", async () => {
  const { claims, api } = makeCoordinator();
  await seedOpenClaim(claims, {
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Active",
  });
  const cart = new FakeCart(
    snapshot(1, {
      kind: "recalled",
      scope: SCOPE,
      holdId: "hold-OTHER",
      recallAttemptId: "attempt-OTHER",
    }),
  );
  const owned = makeCoordinator({ cart, claims, api });

  await assert.rejects(
    () => owned.coordinator.ownerRelease("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "FENCE_CONFLICT",
  );
  assert.deepEqual(api.calls, []);
  assert.deepEqual(claims.releaseCalls, []);
  assert.equal(cart.value.cart.lines.length, 1);
  assert.equal((await claims.getClaim("claim-1"))?.state, "Active");
});

test("普通清车 owner release：无 open claim 返回 NOT_FOUND，多个 open claim 拒绝", async () => {
  const { claims, coordinator } = makeCoordinator();
  await assert.rejects(
    () => coordinator.ownerRelease("hold-missing"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "NOT_FOUND",
  );

  await seedOpenClaim(claims, {
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Active",
  });
  await seedOpenClaim(claims, {
    claimGuid: "claim-2",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Prepared",
  });
  await assert.rejects(
    () => coordinator.ownerRelease("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "CONFLICT",
  );
});

test("普通清车 owner release：服务端 Released 响应与本机 claim 不匹配 fail-closed", async () => {
  const { claims, api } = makeCoordinator();
  const claim = await seedOpenClaim(claims, {
    claimGuid: "claim-1",
    holdGuid: "hold-1",
    source: "RemoteClaim",
    state: "Active",
  });
  const cart = boundCartFor(claim);
  const owned = makeCoordinator({ cart, claims, api });
  api.releaseResultOverride = {
    ...defaultActivateResult(),
    holdGuid: "hold-1",
    claimGuid: "claim-1",
    status: "Released",
    storeCode: "BNE",
    claimantDeviceCode: "IPAD-OTHER",
    releasedAtIso: NOW,
  };

  await assert.rejects(
    () => owned.coordinator.ownerRelease("hold-1"),
    (error: unknown) =>
      error instanceof SharedHeldOrderCoordinatorError &&
      error.code === "INVALID",
  );
  assert.deepEqual(claims.releaseCalls, []);
  assert.equal(cart.value.cart.lines.length, 1);
  assert.equal((await claims.getClaim("claim-1"))?.state, "Active");
});
