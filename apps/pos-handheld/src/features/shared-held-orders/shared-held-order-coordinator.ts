import {
  emptySalePricingState,
  isEmptySaleCart,
  isSaleCart,
  type ActivePricingCartLeasePort,
  type ActivePricingCartPort,
  type ActivePricingCartSnapshot,
  type HeldOrderIdentity,
} from "../held-orders/held-orders-domain";

import { fromSharedSaleCart } from "./shared-held-order-cart-reverse-mapper";
import {
  SharedHeldOrderClaimInvariantError,
  type SharedHeldOrderClaim,
  type SharedHeldOrderClaimRepositoryPort,
} from "./shared-held-order-claim-repository";
import type {
  SharedHeldOrderLocalPublicationPort,
} from "./shared-held-order-local-publication";
import {
  SharedHeldOrderApiError,
  type SharedHeldOrderClaimDto,
  type SharedHeldOrderNetworkApiPort,
  type SharedHeldOrderPrepareResult,
  type SharedHeldOrderRecoveryClaimDto,
} from "./shared-held-order-network-api";
import { toSharedSaleCartV1 } from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";
import {
  sameSharedSaleCart,
  toSharedSaleCartV2,
  type SharedSaleCartPayload,
} from "./shared-sale-cart-v2";

import type {
  HeldOrderScope,
  PricingCartStateSnapshot,
  RecallActiveBinding,
} from "@/core/contracts";

export type SharedHeldOrderTakeResult =
  | Readonly<{
      outcome: "restored";
      claimGuid: string;
      holdGuid: string;
    }>
  | Readonly<{
      outcome: "prepared-awaiting-activation";
      claimGuid: string;
      holdGuid: string;
    }>
  | Readonly<{
      outcome: "fence-held";
      claimGuid: string;
      holdGuid: string;
    }>;

export type SharedHeldOrderReconcileMismatch = Readonly<{
  claimGuid: string;
  holdGuid: string | null;
  reason: string;
}>;

export type SharedHeldOrderReconcileResult = Readonly<{
  restoredClaimIds: readonly string[];
  reconciledPreparedClaimIds: readonly string[];
  mismatches: readonly SharedHeldOrderReconcileMismatch[];
}>;

export type SharedHeldOrderForceReleaseResult = Readonly<{
  claimGuid: string;
  holdGuid: string;
}>;

export type SharedHeldOrderOwnerReleaseResult = Readonly<{
  claimGuid: string;
  holdGuid: string;
}>;

/**
 * 服务端可接受重放但未返回 expiry 时，本地 Prepared 冻结 TTL 的兜底值。
 * 固定 120 秒（不是 capabilities 的 900 秒），避免本地事实与远端过期不一致。
 */
const LOCAL_PREPARED_TTL_FALLBACK_SECONDS = 120;

export class SharedHeldOrderCoordinatorError extends Error {
  public readonly code:
    | "CONFLICT"
    | "FENCE_CONFLICT"
    | "RESTORE_FAILED"
    | "NOT_FOUND"
    | "INVALID"
    | "SALE_MODE_REQUIRED"
    | "CART_NOT_EMPTY";

  public constructor(
    code: SharedHeldOrderCoordinatorError["code"],
    message: string,
  ) {
    super(message);
    this.name = "SharedHeldOrderCoordinatorError";
    this.code = code;
  }
}

export type SharedHeldOrderCoordinatorOptions = Readonly<{
  api: SharedHeldOrderNetworkApiPort;
  claims: SharedHeldOrderClaimRepositoryPort;
  localPublications: SharedHeldOrderLocalPublicationPort;
  activeCart: ActivePricingCartPort;
  identity: HeldOrderIdentity;
  createId(): string;
  nowIso(): string;
}>;

/**
 * 跨设备共享挂单取单协调器（运行时编排，不含 UI）。
 *
 * 在线取单固定顺序：server prepare -> 本地 durable claim/fence -> server activate
 * -> 恢复购物车。本地 durable 写失败/输家绝不 activate；activate 网络结果未知时
 * 保持本地 Prepared、不恢复，交给 claims/mine 对账。Active 不因过期或服务端
 * 状态自动释放；只允许仓储确认旧版 release 已完成 fence/held 清理后补终态。
 * 恢复失败清空本次恢复产生的购物车状态、保留 Active，绝不自动 release。
 * 原设备离线 recall 只读取本地已发布副本，走 OfflineOrigin durable claim，
 * API 不可用/disabled 完全不影响本地挂单。
 */
export class SharedHeldOrderCoordinator {
  private mutationInFlight: Promise<unknown> | null = null;

  public constructor(private readonly options: SharedHeldOrderCoordinatorOptions) {}

  public takeRemoteHold(holdGuid: string): Promise<SharedHeldOrderTakeResult> {
    return this.runMutation(() => this.takeRemoteHoldOnce(holdGuid));
  }

  public recallLocalPublication(
    holdGuid: string,
  ): Promise<SharedHeldOrderTakeResult> {
    return this.runMutation(() => this.recallLocalPublicationOnce(holdGuid));
  }

  public reconcileClaims(): Promise<SharedHeldOrderReconcileResult> {
    return this.runMutation(() => this.reconcileClaimsOnce());
  }

  public forceRelease(
    holdGuid: string,
    reason: string,
  ): Promise<SharedHeldOrderForceReleaseResult> {
    return this.runMutation(() => this.forceReleaseOnce(holdGuid, reason));
  }

  /**
   * 普通 owner release（共享召回购物车的正常清车路径，不是主管 force-release）。
   * RemoteClaim 固定顺序：服务端 owner-scoped release 成功 -> 本地
   * claim/fence/cart 原子清理；OfflineOrigin 只做本地 release/clear。
   * 任一失败都保持购物车与 binding；崩溃重放（本地已 Released + 同 owner key）
   * 跳过服务端并只补购物车清理。
   */
  public ownerRelease(
    holdGuidInput: string,
  ): Promise<SharedHeldOrderOwnerReleaseResult> {
    return this.runMutation(() => this.ownerReleaseOnce(holdGuidInput));
  }

  private runMutation<T>(operation: () => Promise<T>): Promise<T> {
    if (this.mutationInFlight) {
      return Promise.reject(
        new SharedHeldOrderCoordinatorError(
          "CONFLICT",
          "另一项共享挂单操作正在进行中，请稍后重试。",
        ),
      );
    }
    const task = operation().finally(() => {
      if (this.mutationInFlight === task) this.mutationInFlight = null;
    });
    this.mutationInFlight = task;
    return task;
  }

  private async takeRemoteHoldOnce(
    holdGuidInput: string,
  ): Promise<SharedHeldOrderTakeResult> {
    const holdGuid = requiredText(holdGuidInput, "hold guid");
    const nowIso = this.options.nowIso();
    // 下一次 prepare 前先检查本地 durable fence。只有 stale RemoteClaim 才在线
    // 读取 claims/mine 尝试清扫；服务端仍返回 Prepared/Active 时以服务端阻塞事实
    // 为准。任何 open fence 未清空都必须在 prepare 前停止，避免创建未跟踪 claim。
    let openClaim = await this.getOpenClaim();
    if (
      openClaim &&
      this.staleRemoteClaims([openClaim], nowIso).length > 0
    ) {
      // 这里必须传播 claims/mine 错误并停止：若继续调用 prepare，服务端可能
      // 新建 claim，而本地仍被旧 fence 拦截，形成未跟踪的 Prepared claim。
      const serverClaims = await this.options.api.claimsMine();
      await this.expireStaleRemoteClaims(
        [openClaim],
        nowIso,
        serverClaims,
      );
      openClaim = await this.getOpenClaim();
    }

    if (
      openClaim?.source === "OfflineOrigin" &&
      openClaim.state === "Active" &&
      openClaim.boundOrderGuid === null
    ) {
      const orphan = openClaim;
      const cartAllowsLegacyRepair = await this.withCartLease((lease) => {
        const active = lease.read();
        return (
          isSaleCart(active) &&
          isEmptySaleCart(active) &&
          active.recallBinding === null &&
          !active.terminalRecoveryRequired
        );
      });
      if (cartAllowsLegacyRepair) {
        // 旧 presenter 可能已清 legacy fence/held 却漏掉 shared claim。仓储会在
        // 同一事务内核对完整孤儿拓扑；正常 Active（fence 仍在）仍然拒绝修复。
        const repaired =
          await this.options.claims.repairLegacyClearedOfflineOriginClaim({
            claimGuid: orphan.claimGuid,
            releaseIdempotencyKey: `handheld-owner-release:${orphan.claimGuid}`,
            releasedAtIso: this.options.nowIso(),
          });
        if (repaired) {
          openClaim = await this.getOpenClaim();
        }
      }
    }
    if (openClaim) {
      throw new SharedHeldOrderCoordinatorError(
        "FENCE_CONFLICT",
        "本机已有未完成的共享挂单 claim，请先恢复或释放后再取单。",
      );
    }
    await this.withCartLease(async (lease) => {
      if (!isSaleCart(lease.read())) {
        throw new SharedHeldOrderCoordinatorError(
          "SALE_MODE_REQUIRED",
          "共享挂单取单需要普通销售购物车。",
        );
      }
      if (!isEmptySaleCart(lease.read())) {
        throw new SharedHeldOrderCoordinatorError(
          "CART_NOT_EMPTY",
          "取单前必须清空购物车。",
        );
      }
    });

    const claimGuid = this.options.createId();
    const prepareKey = `handheld-prepare:${claimGuid}`;
    const prepared = await this.options.api.prepare({
      holdGuid,
      claimGuid,
      idempotencyKey: prepareKey,
    });
    assertPrepareMatchesRequest(prepared, holdGuid, claimGuid, this.scope());
    if (prepared.status !== "Prepared" && prepared.status !== "Active") {
      // 服务端终态：本地绝不新建 claim。
      throw new SharedHeldOrderCoordinatorError(
        "CONFLICT",
        "共享挂单 claim 已处于终态，拒绝继续取单。",
      );
    }
    const saved = await this.options.claims.prepareClaim({
      claimGuid,
      holdGuid,
      recallAttemptId: claimGuid,
      scope: this.scope(),
      source: "RemoteClaim",
      prepareIdempotencyKey: prepareKey,
      payload: prepared.payload,
      preparedExpiresAtIso:
        prepared.expiresAtIso ??
        addSeconds(nowIso, LOCAL_PREPARED_TTL_FALLBACK_SECONDS),
      heldAtIso: nowIso,
      heldBy: this.heldBy(),
      createdAtIso: nowIso,
    });
    if (saved.outcome === "fence-held") {
      return {
        outcome: "fence-held",
        claimGuid: saved.winner.claimGuid,
        holdGuid,
      };
    }
    const claim = saved.claim;

    const activateKey = `handheld-activate:${claimGuid}`;
    try {
      const activated = await this.options.api.activate({ holdGuid, claimGuid });
      assertActivateMatchesRequest(activated, holdGuid, claimGuid, this.scope());
      const localActivated = await this.options.claims.activatePreparedClaim({
        claimGuid,
        prepareIdempotencyKey: prepareKey,
        activateIdempotencyKey: activateKey,
        serverRevision: activated.revision,
        activatedAtIso: this.options.nowIso(),
      });
      if (!localActivated) {
        return {
          outcome: "prepared-awaiting-activation",
          claimGuid,
          holdGuid,
        };
      }
      return this.restoreClaim(
        claim,
        fromSharedSaleCart(claim.payload),
        claimGuid,
        holdGuid,
      );
    } catch (error) {
      // 只有 Retryable 视为 activate 网络结果未知：保持本地 Prepared，不恢复，
      // 交给 claims/mine 对账；业务拒绝（Conflict/Forbidden/Invalid/Disabled）
      // 与非 SharedHeldOrderApiError 程序错误一律向上抛，绝不吞掉。
      if (error instanceof SharedHeldOrderApiError && error.kind === "Retryable") {
        return {
          outcome: "prepared-awaiting-activation",
          claimGuid,
          holdGuid,
        };
      }
      throw error;
    }
  }

  private async recallLocalPublicationOnce(
    holdGuidInput: string,
  ): Promise<SharedHeldOrderTakeResult> {
    const holdGuid = requiredText(holdGuidInput, "hold guid");
    await this.withCartLease(async (lease) => {
      if (!isSaleCart(lease.read())) {
        throw new SharedHeldOrderCoordinatorError(
          "SALE_MODE_REQUIRED",
          "共享挂单取单需要普通销售购物车。",
        );
      }
      if (!isEmptySaleCart(lease.read())) {
        throw new SharedHeldOrderCoordinatorError(
          "CART_NOT_EMPTY",
          "取单前必须清空购物车。",
        );
      }
    });
    const eligibility = await this.options.localPublications.loadEligible(
      holdGuid,
      this.scope(),
    );
    if (!eligibility.eligible) {
      throw new SharedHeldOrderCoordinatorError(
        eligibility.reason === "not-found"
          ? "NOT_FOUND"
          : "CONFLICT",
        "本地没有可恢复的共享挂单副本。",
      );
    }
    const claimGuid = this.options.createId();
    const prepareKey = `handheld-offline:${holdGuid}`;
    const nowIso = this.options.nowIso();
    const saved = await this.options.claims.prepareClaim({
      claimGuid,
      holdGuid,
      recallAttemptId: claimGuid,
      scope: this.scope(),
      source: "OfflineOrigin",
      prepareIdempotencyKey: prepareKey,
      payload: eligibility.cart,
      preparedExpiresAtIso: nowIso,
      heldAtIso: nowIso,
      heldBy: this.heldBy(),
      createdAtIso: nowIso,
    });
    if (saved.outcome === "fence-held") {
      return {
        outcome: "fence-held",
        claimGuid: saved.winner.claimGuid,
        holdGuid,
      };
    }
    const claim = saved.claim;
    // 离线激活：无服务端 revision，本地 Active 即事实（不访问 API）。
    const activated = await this.options.claims.activatePreparedClaim({
      claimGuid,
      prepareIdempotencyKey: prepareKey,
      activateIdempotencyKey: `handheld-offline-activate:${claimGuid}`,
      serverRevision: null,
      activatedAtIso: nowIso,
    });
    if (!activated) {
      throw new SharedHeldOrderCoordinatorError(
        "CONFLICT",
        "离线 claim 本地激活失败。",
      );
    }
    return this.restoreClaim(
      claim,
      fromSharedSaleCart(claim.payload),
      claimGuid,
      holdGuid,
    );
  }

  private async forceReleaseOnce(
    holdGuidInput: string,
    reasonInput: string,
  ): Promise<SharedHeldOrderForceReleaseResult> {
    const holdGuid = requiredText(holdGuidInput, "hold guid");
    const reason = requiredText(reasonInput, "force release reason");
    const claim = await this.getOpenClaim();
    if (!claim) {
      throw new SharedHeldOrderCoordinatorError(
        "NOT_FOUND",
        "找不到唯一的本机 open claim，拒绝强制释放。",
      );
    }
    if (claim.holdGuid !== holdGuid) {
      throw new SharedHeldOrderCoordinatorError(
        "CONFLICT",
        "本机 open claim 属于另一笔挂单，拒绝强制释放。",
      );
    }
    if (claim.state !== "Prepared" && claim.state !== "Active") {
      throw new SharedHeldOrderCoordinatorError(
        "CONFLICT",
        "本机 claim 已不再处于可释放状态。",
      );
    }
    const expectedState = claim.state;
    if (claim.source !== "RemoteClaim") {
      throw new SharedHeldOrderCoordinatorError(
        "INVALID",
        "本机离线取回没有服务端 claim，不能执行服务端强制释放。",
      );
    }

    // 服务端先成为 Released 真相；失败时本地 claim、fence 与购物车完全不推进。
    const released = await this.options.api.forceRelease({
      holdGuid,
      claimGuid: claim.claimGuid,
      reason,
    });
    assertForceReleaseMatchesRequest(
      released,
      holdGuid,
      claim.claimGuid,
      this.scope(),
    );

    if (expectedState === "Active") {
      await this.clearReleasedActiveCart(claim);
    }
    const releasedLocally = await this.options.claims.releaseClaim({
      claimGuid: claim.claimGuid,
      releaseIdempotencyKey: `handheld-force-release:${claim.claimGuid}`,
      releasedAtIso: this.options.nowIso(),
      expectedState,
    });
    if (!releasedLocally) {
      throw new SharedHeldOrderCoordinatorError(
        "FENCE_CONFLICT",
        "服务端已释放，但本地 claim/fence 未能幂等释放；保留事实等待重试。",
      );
    }
    return { claimGuid: claim.claimGuid, holdGuid };
  }

  private async ownerReleaseOnce(
    holdGuidInput: string,
  ): Promise<SharedHeldOrderOwnerReleaseResult> {
    const holdGuid = requiredText(holdGuidInput, "hold guid");
    const openClaim = await this.getOpenClaim();
    const activeBinding = await this.withCartLease(
      (lease) => lease.read().recallBinding,
    );
    let claim: SharedHeldOrderClaim | null;
    if (openClaim?.holdGuid === holdGuid) {
      claim = openClaim;
    } else if (activeBinding?.holdId === holdGuid) {
      // 当前购物车 binding 才是清车路由真相。legacy recallAttemptId 不会命中
      // shared claim，此时返回 NOT_FOUND 让组合根安全回退 legacy fence。
      claim = await this.options.claims.getClaim(
        activeBinding.recallAttemptId,
      );
      if (claim && !recallBindingMatchesClaim(activeBinding, claim)) {
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "当前购物车 binding 与本机 shared claim 不一致，拒绝清理。",
        );
      }
    } else {
      claim = await this.options.claims.getLatestClaimForHold(
        this.scope(),
        holdGuid,
      );
    }
    if (!claim) {
      throw new SharedHeldOrderCoordinatorError(
        "NOT_FOUND",
        "本机不存在该共享挂单 claim，拒绝普通释放。",
      );
    }
    const ownerKey = `handheld-owner-release:${claim.claimGuid}`;
    if (claim.state === "Released") {
      if (claim.releaseIdempotencyKey !== ownerKey) {
        throw new SharedHeldOrderCoordinatorError(
          "CONFLICT",
          "本机 claim 已由其他流程释放，拒绝重复普通释放。",
        );
      }
      // 崩溃重放：本地已 Released（owner key），只补购物车清理。
      await this.finishReleasedCartCleanup(claim);
      return { claimGuid: claim.claimGuid, holdGuid };
    }
    if (claim.state !== "Prepared" && claim.state !== "Active") {
      throw new SharedHeldOrderCoordinatorError(
        "CONFLICT",
        "本机 claim 已处于终态，拒绝普通释放。",
      );
    }
    // 本地前置校验先于服务端调用：购物车属于其他 claim（或 Active 无 binding
    // 且非空）时 fail-closed，绝不先释放服务端再制造孤儿状态。
    if (claim.state === "Active") {
      await this.assertActiveCartReleasable(claim);
    }
    if (claim.source === "RemoteClaim") {
      // 固定顺序：服务端 owner-scoped release 先成为真相；失败时本地完全不动。
      const released = await this.options.api.release({
        holdGuid,
        claimGuid: claim.claimGuid,
      });
      assertOwnerReleaseMatchesRequest(
        released,
        holdGuid,
        claim.claimGuid,
        this.scope(),
      );
    }
    await this.releaseLocalClaimAtomically(claim, ownerKey);
    return { claimGuid: claim.claimGuid, holdGuid };
  }

  /** Prepared 直接 releaseClaim；Active 在 cart lease 内清车 + release + 解绑，
   *  失败回滚购物车内容并保留 binding（绝不留下空车孤儿 Active）。 */
  private async releaseLocalClaimAtomically(
    claim: SharedHeldOrderClaim,
    ownerKey: string,
  ): Promise<void> {
    if (claim.state === "Prepared") {
      const released = await this.options.claims.releaseClaim({
        claimGuid: claim.claimGuid,
        releaseIdempotencyKey: ownerKey,
        releasedAtIso: this.options.nowIso(),
        expectedState: "Prepared",
      });
      if (!released) {
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "服务端已释放，但本地 claim/fence 未能幂等释放；保留事实等待重试。",
        );
      }
      return;
    }
    await this.withCartLease(async (lease) => {
      const active = lease.read();
      let original: ActivePricingCartSnapshot | null = null;
      if (active.recallBinding === null) {
        if (!isEmptySaleCart(active)) {
          throw new SharedHeldOrderCoordinatorError(
            "FENCE_CONFLICT",
            "本地 Active claim 已无购物车 binding，但当前购物车非空；拒绝清理。",
          );
        }
      } else {
        if (!recallBindingMatchesClaim(active.recallBinding, claim)) {
          throw new SharedHeldOrderCoordinatorError(
            "FENCE_CONFLICT",
            "当前购物车属于另一项 claim，拒绝清理。",
          );
        }
        original = active;
      }
      try {
        if (original) {
          // 先清车但保留 binding；releaseClaim 事务清除 fence/synthetic held。
          await lease.replace(
            emptySalePricingState(original.pricingState),
            original.recallBinding,
          );
        }
        const released = await this.options.claims.releaseClaim({
          claimGuid: claim.claimGuid,
          releaseIdempotencyKey: ownerKey,
          releasedAtIso: this.options.nowIso(),
          expectedState: "Active",
        });
        if (!released) {
          throw new SharedHeldOrderCoordinatorError(
            "FENCE_CONFLICT",
            "服务端已释放，但本地 claim/fence 未能幂等释放；保留事实等待重试。",
          );
        }
        if (original) {
          await lease.setRecallBinding(null);
        }
      } catch (error) {
        // 本地清理失败：回滚购物车内容，保持 binding。
        if (original) {
          try {
            await lease.replace(original.pricingState, original.recallBinding);
          } catch {
            // 回滚失败也无法恢复；抛原始错误，由对账/人工处置。
          }
        }
        throw error;
      }
    });
  }

  private async assertActiveCartReleasable(
    claim: SharedHeldOrderClaim,
  ): Promise<void> {
    await this.withCartLease(async (lease) => {
      const active = lease.read();
      if (active.recallBinding === null) {
        if (isEmptySaleCart(active)) return;
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "本地 Active claim 已无购物车 binding，但当前购物车非空；拒绝清理。",
        );
      }
      if (!recallBindingMatchesClaim(active.recallBinding, claim)) {
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "当前购物车不属于该共享 claim，拒绝普通释放。",
        );
      }
    });
  }

  private async finishReleasedCartCleanup(
    claim: SharedHeldOrderClaim,
  ): Promise<void> {
    await this.withCartLease(async (lease) => {
      const active = lease.read();
      if (active.recallBinding === null) {
        if (isEmptySaleCart(active)) return;
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "本机 Released claim 已无购物车 binding，但当前购物车非空；拒绝清理。",
        );
      }
      if (!recallBindingMatchesClaim(active.recallBinding, claim)) {
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "当前购物车属于另一项 claim，拒绝清理。",
        );
      }
      await lease.replace(
        emptySalePricingState(active.pricingState),
        active.recallBinding,
      );
      await lease.setRecallBinding(null);
    });
  }

  private async clearReleasedActiveCart(
    claim: SharedHeldOrderClaim,
  ): Promise<void> {
    await this.withCartLease(async (lease) => {
      const active = lease.read();
      if (active.recallBinding === null) {
        if (isEmptySaleCart(active)) return;
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "本地 Active claim 已无购物车 binding，但当前购物车非空；拒绝清理。",
        );
      }
      if (!recallBindingMatchesClaim(active.recallBinding, claim)) {
        throw new SharedHeldOrderCoordinatorError(
          "FENCE_CONFLICT",
          "当前购物车属于另一项 claim，拒绝清理。",
        );
      }
      await lease.replace(emptySalePricingState(active.pricingState), null);
    });
  }

  private async reconcileClaimsOnce(): Promise<SharedHeldOrderReconcileResult> {
    const mismatches: SharedHeldOrderReconcileMismatch[] = [];
    const localClaims = await this.listMineIncludingOpen();
    const restored: string[] = [];
    const reconciledPrepared: string[] = [];
    let serverClaims: readonly SharedHeldOrderRecoveryClaimDto[];
    try {
      serverClaims = await this.options.api.claimsMine();
    } catch (error) {
      // claims/mine 离线时仍先恢复纯本地 OfflineOrigin durable 事实，
      // 然后保留原远端错误让调用方知道 RemoteClaim 尚未完成对账。
      await this.reconcileOfflineOriginClaims(
        localClaims,
        restored,
        reconciledPrepared,
        mismatches,
      );
      throw error;
    }
    const serverSeen = new Set(serverClaims.map((claim) => claim.claimGuid));
    // 在线时，claimGuid 同时出现在服务端的本地行必须先走 remote facts 校验；
    // 只有服务端确实不存在的 OfflineOrigin 才可本地恢复。
    await this.reconcileOfflineOriginClaims(
      localClaims.filter((claim) => !serverSeen.has(claim.claimGuid)),
      restored,
      reconciledPrepared,
      mismatches,
    );
    // 复用同一次 claims/mine：只有服务端不再返回 blocking claim 时才幂等释放；
    // Prepared/Active 都是服务端权威阻塞事实，后者由下面主循环补激活。
    await this.expireStaleRemoteClaims(
      localClaims,
      this.options.nowIso(),
      serverClaims,
      mismatches,
    );
    const refreshedClaims = await this.listMineIncludingOpen();
    const localByClaimId = new Map(
      refreshedClaims.map((claim) => [claim.claimGuid, claim]),
    );

    for (const server of serverClaims) {
      const local = localByClaimId.get(server.claimGuid);
      if (server.status === "Prepared") {
        if (local) {
          if (
            local.state === "Prepared" &&
            this.remoteClaimFactsMatch(local, server)
          ) {
            // 同 facts：只按幂等状态保存/等待，绝不激活或恢复。
            reconciledPrepared.push(server.claimGuid);
          } else if (
            local.state === "Released" ||
            local.state === "Completed" ||
            local.state === "Superseded"
          ) {
            // 本地已推进终态（本地过期/释放/完成），服务端尚未同步：
            // 保持本地终态，不复活、不重复报错。
            continue;
          } else {
            mismatches.push({
              claimGuid: server.claimGuid,
              holdGuid: server.holdGuid,
              reason: "本地 claim facts 与服务端 Prepared 不一致，保留本地事实。",
            });
          }
          continue;
        }
        // 服务端 Prepared 且本地无记录：幂等保存为 RemoteClaim 并等待。
        if (!serverClaimMatchesScope(server, this.scope())) {
          mismatches.push({
            claimGuid: server.claimGuid,
            holdGuid: server.holdGuid,
            reason: "服务端 Prepared claim 与本机 store/device 不一致，拒绝落库。",
          });
          continue;
        }
        const saved = await this.options.claims.prepareClaim({
          claimGuid: server.claimGuid,
          holdGuid: server.holdGuid,
          recallAttemptId: server.claimGuid,
          scope: this.scope(),
          source: "RemoteClaim",
          prepareIdempotencyKey: `reconcile:${server.claimGuid}`,
          payload: server.payload,
          preparedExpiresAtIso:
            server.expiresAtIso ??
            addSeconds(
              this.options.nowIso(),
              LOCAL_PREPARED_TTL_FALLBACK_SECONDS,
            ),
          heldAtIso: this.options.nowIso(),
          heldBy: this.heldBy(),
          createdAtIso: this.options.nowIso(),
        });
        if (saved.outcome === "prepared" || saved.outcome === "replayed") {
          reconciledPrepared.push(server.claimGuid);
        } else {
          mismatches.push({
            claimGuid: server.claimGuid,
            holdGuid: server.holdGuid,
            reason: "本机 open fence 被其他 claim 占用，无法保存服务端 Prepared。",
          });
        }
        continue;
      }

      if (server.status === "Active") {
        if (!local) {
          // 服务端 Active 但本地无 durable 事实：fail-closed，绝不自动 release。
          mismatches.push({
            claimGuid: server.claimGuid,
            holdGuid: server.holdGuid,
            reason: "服务端 Active claim 缺少本地 durable 事实，拒绝恢复。",
          });
          continue;
        }
        if (!this.remoteClaimFactsMatch(local, server)) {
          mismatches.push({
            claimGuid: server.claimGuid,
            holdGuid: server.holdGuid,
            reason: "本地 claim facts 与服务端 Active 不一致，拒绝恢复。",
          });
          continue;
        }
        if (local.state === "Prepared") {
          // 崩溃窗口：服务端已 Active，本地仍 Prepared —— 补本地激活后恢复。
          const activated = await this.options.claims.activatePreparedClaim({
            claimGuid: local.claimGuid,
            prepareIdempotencyKey: local.prepareIdempotencyKey,
            activateIdempotencyKey: `reconcile-activate:${local.claimGuid}`,
            serverRevision: server.revision,
            activatedAtIso: this.options.nowIso(),
          });
          if (!activated) {
            mismatches.push({
              claimGuid: server.claimGuid,
              holdGuid: server.holdGuid,
              reason: "本地 Prepared 无法补激活，保留事实等待重试。",
            });
            continue;
          }
          await this.tryRestoreForReconcile(local, restored, mismatches);
          continue;
        }
        if (local.state === "Active") {
          await this.tryRestoreForReconcile(local, restored, mismatches);
          continue;
        }
        mismatches.push({
          claimGuid: server.claimGuid,
          holdGuid: server.holdGuid,
          reason: "本地 claim 状态与服务端 Active 不一致，保留本地事实。",
        });
        continue;
      }

      // 服务端终态（Released/Completed/Superseded）：本地 open claim 保留，不自动 release。
      if (local && (local.state === "Prepared" || local.state === "Active")) {
        mismatches.push({
          claimGuid: server.claimGuid,
          holdGuid: server.holdGuid,
          reason: "服务端 claim 已终态而本地仍 open，保留本地事实。",
        });
      }
    }

    // 本地 RemoteClaim 有但服务端缺失：保留本地事实，fail-closed 不恢复。
    // OfflineOrigin 是本机离线 recall 产生的本地事实，服务端本就没有对应 claim，
    // 不属于“服务端缺 claim”错误。
    for (const local of refreshedClaims) {
      if (
        local.source === "RemoteClaim" &&
        !serverSeen.has(local.claimGuid) &&
        (local.state === "Prepared" || local.state === "Active")
      ) {
        mismatches.push({
          claimGuid: local.claimGuid,
          holdGuid: local.holdGuid,
          reason: "服务端没有对应 claim，本地事实保留。",
        });
      }
    }

    return {
      restoredClaimIds: Object.freeze(restored),
      reconciledPreparedClaimIds: Object.freeze(reconciledPrepared),
      mismatches: Object.freeze(mismatches),
    };
  }

  /**
   * 可信本地 expiry 清扫：只处理 RemoteClaim + Prepared + preparedExpiresAtIso
   * 已过（invalid ISO 一律 fail-closed，绝不当作过期）。claims/mine 只返回服务端
   * 当前仍 blocking 的 Prepared/Active，因此任一状态存在都保留本地 fence；仅当
   * 服务端缺失或显式返回终态时幂等 releaseClaim（相同 expire key 可重放）。
   * claimsMine 失败（serverClaims === null）保留 fence；Active/OfflineOrigin 永不
   * 自动释放。
   */
  private async expireStaleRemoteClaims(
    localClaims: readonly SharedHeldOrderClaim[],
    nowIso: string,
    serverClaims: readonly SharedHeldOrderRecoveryClaimDto[] | null,
    mismatches?: SharedHeldOrderReconcileMismatch[],
  ): Promise<void> {
    const stale = this.staleRemoteClaims(localClaims, nowIso);
    if (stale.length === 0) return;
    if (serverClaims === null) return; // claims/mine 失败：保留 fence fail-closed。
    const serverByClaimGuid = new Map(
      serverClaims.map((claim) => [claim.claimGuid, claim]),
    );
    for (const claim of stale) {
      const server = serverByClaimGuid.get(claim.claimGuid);
      if (server?.status === "Prepared" || server?.status === "Active") {
        continue; // 服务端权威 blocking 事实：绝不按本地时钟单方面过期。
      }
      const released = await this.options.claims.releaseClaim({
        claimGuid: claim.claimGuid,
        releaseIdempotencyKey: `handheld-expire:${claim.claimGuid}`,
        releasedAtIso: nowIso,
        expectedState: "Prepared",
      });
      if (!released && mismatches) {
        mismatches.push({
          claimGuid: claim.claimGuid,
          holdGuid: claim.holdGuid,
          reason: "本地过期 Prepared 释放失败，保留事实等待重试。",
        });
      }
    }
  }

  private staleRemoteClaims(
    localClaims: readonly SharedHeldOrderClaim[],
    nowIso: string,
  ): readonly SharedHeldOrderClaim[] {
    const nowMillis = Date.parse(nowIso);
    if (!Number.isFinite(nowMillis)) {
      throw new TypeError("nowIso must be a valid ISO timestamp.");
    }
    return localClaims.filter(
      (claim) =>
        claim.source === "RemoteClaim" &&
        claim.state === "Prepared" &&
        isExpiredIso(claim.preparedExpiresAtIso, nowMillis),
    );
  }

  private async reconcileOfflineOriginClaims(
    localClaims: readonly SharedHeldOrderClaim[],
    restored: string[],
    reconciledPrepared: string[],
    mismatches: SharedHeldOrderReconcileMismatch[],
  ): Promise<void> {
    for (const local of localClaims) {
      if (
        local.source !== "OfflineOrigin" ||
        (local.state !== "Prepared" && local.state !== "Active")
      ) {
        continue;
      }
      let activeClaim = local;
      if (local.state === "Prepared") {
        const activated = await this.options.claims.activatePreparedClaim({
          claimGuid: local.claimGuid,
          prepareIdempotencyKey: local.prepareIdempotencyKey,
          activateIdempotencyKey: `handheld-offline-activate:${local.claimGuid}`,
          serverRevision: null,
          activatedAtIso: this.options.nowIso(),
        });
        if (!activated) {
          mismatches.push({
            claimGuid: local.claimGuid,
            holdGuid: local.holdGuid,
            reason: "本机离线 Prepared claim 无法补激活，保留事实等待重试。",
          });
          continue;
        }
        reconciledPrepared.push(local.claimGuid);
        const reloaded = await this.options.claims.getClaim(local.claimGuid);
        if (!reloaded || reloaded.state !== "Active") {
          mismatches.push({
            claimGuid: local.claimGuid,
            holdGuid: local.holdGuid,
            reason: "本机离线 claim 激活后无法重读 Active 事实，拒绝恢复。",
          });
          continue;
        }
        activeClaim = reloaded;
      }
      const existingCartHandled =
        await this.reconcileExistingOfflineOriginCart(
          activeClaim,
          restored,
          mismatches,
        );
      if (existingCartHandled) {
        continue;
      }
      await this.tryRestoreForReconcile(activeClaim, restored, mismatches);
    }
  }

  /**
   * 旧版 owner release 可能只清掉 held/fence，却把 OfflineOrigin claim 留在
   * Active。若支付草稿已经恢复相同 binding，必须补回 durable 拓扑并保留
   * 当前购物车（用户可能已改数量）；若购物车仍为空，则补 Released，避免
   * 把用户已经清掉的旧挂单重新恢复进购物车。
   */
  private async reconcileExistingOfflineOriginCart(
    claim: SharedHeldOrderClaim,
    restored: string[],
    mismatches: SharedHeldOrderReconcileMismatch[],
  ): Promise<boolean> {
    try {
      const outcome = await this.withCartLease(async (lease) => {
        const active = lease.read();
        if (
          isSaleCart(active) &&
          !active.terminalRecoveryRequired &&
          active.recallBinding !== null &&
          recallBindingMatchesClaim(active.recallBinding, claim)
        ) {
          const repaired =
            await this.options.claims.ensureRestoredOfflineOriginClaimFence({
              claimGuid: claim.claimGuid,
              repairedAtIso: this.options.nowIso(),
            });
          return repaired ? "restored" : "repair-failed";
        }
        if (
          isSaleCart(active) &&
          isEmptySaleCart(active) &&
          active.recallBinding === null &&
          !active.terminalRecoveryRequired
        ) {
          const released =
            await this.options.claims.repairLegacyClearedOfflineOriginClaim({
              claimGuid: claim.claimGuid,
              releaseIdempotencyKey: `handheld-owner-release:${claim.claimGuid}`,
              releasedAtIso: this.options.nowIso(),
            });
          return released ? "released" : "not-handled";
        }
        return "not-handled";
      });

      if (outcome === "restored") {
        if (!restored.includes(claim.claimGuid)) {
          restored.push(claim.claimGuid);
        }
        return true;
      }
      if (outcome === "released") {
        return true;
      }
      if (outcome === "repair-failed") {
        mismatches.push({
          claimGuid: claim.claimGuid,
          holdGuid: claim.holdGuid,
          reason:
            "购物车已恢复，但本地挂单围栏无法补回；保留 Active 与购物车等待重试。",
        });
        return true;
      }
      return false;
    } catch {
      mismatches.push({
        claimGuid: claim.claimGuid,
        holdGuid: claim.holdGuid,
        reason:
          "OfflineOrigin 旧状态修复失败；保留 Active 与购物车等待重试。",
      });
      return true;
    }
  }

  private async tryRestoreForReconcile(
    claim: SharedHeldOrderClaim,
    restored: string[],
    mismatches: SharedHeldOrderReconcileMismatch[],
  ): Promise<void> {
    try {
      const result = await this.restoreClaim(
        claim,
        fromSharedSaleCart(claim.payload),
        claim.claimGuid,
        claim.holdGuid,
      );
      if (result.outcome === "restored") {
        restored.push(claim.claimGuid);
      } else {
        mismatches.push({
          claimGuid: claim.claimGuid,
          holdGuid: claim.holdGuid,
          reason: "服务端 Active 但对账恢复未完成，保留 Active 等待重试。",
        });
      }
    } catch {
      const index = restored.indexOf(claim.claimGuid);
      if (index >= 0) {
        restored.splice(index, 1);
      }
      mismatches.push({
        claimGuid: claim.claimGuid,
        holdGuid: claim.holdGuid,
        reason: "服务端 Active 但对账恢复失败，保留 Active 等待重试。",
      });
    }
  }

  private remoteClaimFactsMatch(
    local: SharedHeldOrderClaim,
    server: SharedHeldOrderRecoveryClaimDto,
  ): boolean {
    return (
      local.source === "RemoteClaim" &&
      local.holdGuid === server.holdGuid &&
      local.scope.storeCode === server.storeCode &&
      local.scope.deviceCode === server.claimantDeviceCode &&
      sameSharedSaleCart(local.payload, server.payload)
    );
  }

  private async restoreClaim(
    claim: SharedHeldOrderClaim,
    pricingState: PricingCartStateSnapshot,
    claimGuid: string,
    holdGuid: string,
  ): Promise<SharedHeldOrderTakeResult> {
    const binding = recallBinding(claim);
    try {
      await this.withCartLease(async (lease) => {
        const active = lease.read();
        if (active.terminalRecoveryRequired) {
          throw new SharedHeldOrderCoordinatorError(
            "FENCE_CONFLICT",
            "终端已有未完成的取单围栏。",
          );
        }
        if (active.recallBinding) {
          if (
            recallBindingMatchesClaim(active.recallBinding, claim) &&
            activePricingStateMatchesClaim(active.pricingState, claim.payload)
          ) {
            // 崩溃发生在购物车交换之后：相同 binding + 冻结快照即幂等成功。
            return;
          }
          throw new SharedHeldOrderCoordinatorError(
            "FENCE_CONFLICT",
            "终端购物车已绑定另一项 claim 或冻结快照不一致。",
          );
        }
        if (!isEmptySaleCart(active)) {
          throw new SharedHeldOrderCoordinatorError(
            "CART_NOT_EMPTY",
            "claim 激活后购物车已被新交易占用，拒绝覆盖。",
          );
        }
        await lease.blockForRecallRecovery(binding);
        // PricingCart 在隔离实例中校验成功后，购物车与 active binding 一次性交换。
        await lease.replace(pricingState, binding);
      });
      return { outcome: "restored", claimGuid, holdGuid };
    } catch (error) {
      if (
        error instanceof SharedHeldOrderCoordinatorError &&
        (error.code === "FENCE_CONFLICT" || error.code === "CART_NOT_EMPTY")
      ) {
        // 现有购物车/围栏属于其他事实时绝不能执行恢复失败清车。
        throw error;
      }
      try {
        // 恢复失败：清空本次恢复产生的购物车状态、保留 Active binding，
        // 绝不自动 release。
        await this.withCartLease(async (lease) => {
          await lease.blockForRecallRecovery(binding);
          await lease.replace(
            emptySalePricingState(lease.read().pricingState),
            binding,
          );
        });
      } catch {
        // 清空也失败：本地事实保留，交由对账/人工处置。
      }
      throw new SharedHeldOrderCoordinatorError(
        "RESTORE_FAILED",
        "共享挂单恢复购物车失败，购物车已清空；Active 本地事实保留。",
      );
    }
  }

  private async withCartLease<T>(
    operation: (lease: ActivePricingCartLeasePort) => T | Promise<T>,
  ): Promise<T> {
    return this.options.activeCart.runExclusive(operation);
  }

  private async listMineIncludingOpen(): Promise<readonly SharedHeldOrderClaim[]> {
    const claims = await this.options.claims.listMine(this.scope(), 200);
    const claimIds = new Set(claims.map((claim) => claim.claimGuid));
    const omittedOpenClaims = (await this.options.claims.listOpenClaims(
      this.scope(),
    )).filter((claim) => !claimIds.has(claim.claimGuid));
    return omittedOpenClaims.length === 0
      ? claims
      : [...claims, ...omittedOpenClaims];
  }

  private async getOpenClaim(): Promise<SharedHeldOrderClaim | null> {
    try {
      return await this.options.claims.getOpenClaim(this.scope());
    } catch (error: unknown) {
      if (error instanceof SharedHeldOrderClaimInvariantError) {
        throw new SharedHeldOrderCoordinatorError(
          "CONFLICT",
          "本机存在多个未完成的共享挂单 claim，拒绝继续。",
        );
      }
      throw error;
    }
  }

  private scope() {
    return {
      storeCode: requiredText(this.options.identity.storeCode, "store code"),
      deviceCode: requiredText(this.options.identity.deviceCode, "device code"),
    };
  }

  private heldBy() {
    return {
      cashierId: requiredText(this.options.identity.cashierId, "cashier id"),
      cashierName: requiredText(
        this.options.identity.cashierName,
        "cashier name",
      ),
    };
  }
}

function recallBinding(claim: SharedHeldOrderClaim): RecallActiveBinding {
  return {
    kind: "recalled",
    scope: claim.scope,
    holdId: claim.holdGuid,
    recallAttemptId: claim.recallAttemptId,
  };
}

function recallBindingMatchesClaim(
  binding: RecallActiveBinding,
  claim: SharedHeldOrderClaim,
): boolean {
  return (
    binding.kind === "recalled" &&
    binding.holdId === claim.holdGuid &&
    binding.recallAttemptId === claim.recallAttemptId &&
    binding.scope.storeCode === claim.scope.storeCode &&
    binding.scope.deviceCode === claim.scope.deviceCode
  );
}

function assertPrepareMatchesRequest(
  prepared: SharedHeldOrderPrepareResult,
  holdGuid: string,
  claimGuid: string,
  scope: HeldOrderScope,
): void {
  if (
    prepared.holdGuid !== holdGuid ||
    prepared.claimGuid !== claimGuid ||
    prepared.claimantDeviceCode !== scope.deviceCode
  ) {
    throw new SharedHeldOrderCoordinatorError(
      "INVALID",
      "服务端 prepare 响应与请求不匹配，拒绝写入本地事实。",
    );
  }
}

function assertActivateMatchesRequest(
  activated: SharedHeldOrderClaimDto,
  holdGuid: string,
  claimGuid: string,
  scope: HeldOrderScope,
): void {
  if (
    activated.holdGuid !== holdGuid ||
    activated.claimGuid !== claimGuid ||
    activated.storeCode !== scope.storeCode ||
    activated.claimantDeviceCode !== scope.deviceCode ||
    activated.status !== "Active"
  ) {
    throw new SharedHeldOrderCoordinatorError(
      "INVALID",
      "服务端 activate 响应与请求不匹配，拒绝推进本地激活。",
    );
  }
}

function assertForceReleaseMatchesRequest(
  released: SharedHeldOrderClaimDto,
  holdGuid: string,
  claimGuid: string,
  scope: HeldOrderScope,
): void {
  if (
    released.holdGuid !== holdGuid ||
    released.claimGuid !== claimGuid ||
    released.storeCode !== scope.storeCode ||
    released.claimantDeviceCode !== scope.deviceCode ||
    released.status !== "Released" ||
    released.forceReleased !== true
  ) {
    throw new SharedHeldOrderCoordinatorError(
      "INVALID",
      "服务端 force-release 响应与本机 claim 不匹配，拒绝推进本地释放。",
    );
  }
}

function assertOwnerReleaseMatchesRequest(
  released: SharedHeldOrderClaimDto,
  holdGuid: string,
  claimGuid: string,
  scope: HeldOrderScope,
): void {
  if (
    released.holdGuid !== holdGuid ||
    released.claimGuid !== claimGuid ||
    released.storeCode !== scope.storeCode ||
    released.claimantDeviceCode !== scope.deviceCode ||
    released.status !== "Released"
  ) {
    throw new SharedHeldOrderCoordinatorError(
      "INVALID",
      "服务端 release 响应与本机 claim 不匹配，拒绝推进本地释放。",
    );
  }
}

function activePricingStateMatchesClaim(
  pricingState: PricingCartStateSnapshot,
  claimPayload: SharedSaleCartPayload,
): boolean {
  try {
    const activePayload = claimPayload.version === 1
      ? toSharedSaleCartV1(pricingState)
      : toSharedSaleCartV2(pricingState);
    return sameSharedSaleCart(activePayload, claimPayload);
  } catch {
    // 例如 V1 claim 恢复后目录复核新增了 catalog baseline：不可有损降级
    // 来伪装幂等，应按快照不一致保留现有 fence/cart。
    return false;
  }
}

function serverClaimMatchesScope(
  claim: SharedHeldOrderRecoveryClaimDto,
  scope: HeldOrderScope,
): boolean {
  return (
    claim.storeCode === scope.storeCode &&
    claim.claimantDeviceCode === scope.deviceCode
  );
}

function addSeconds(iso: string, seconds: number): string {
  const parsed = Date.parse(iso);
  if (!Number.isFinite(parsed)) {
    throw new TypeError("nowIso must be a valid ISO timestamp.");
  }
  return new Date(parsed + seconds * 1_000).toISOString();
}

/** 过期判定 fail-closed：非法 ISO 一律视为未过期，绝不因 Date.parse NaN 误释放。 */
function isExpiredIso(iso: string, nowMillis: number): boolean {
  const parsed = Date.parse(iso);
  return Number.isFinite(parsed) && parsed <= nowMillis;
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw new TypeError(`${label} must not be blank.`);
  }
  return normalized;
}
