import type {
  CartSnapshot,
  HeldOrderActor,
  HeldOrderRecordRepositoryPort,
  HeldOrderScope,
  HeldOrderSummary,
  PricingCartStateSnapshot,
  RecallActiveBinding,
} from "@/core/contracts";
import { auditActorPayload } from "@/core/contracts";
import type { AuditEventDraft } from "@hb/pos-domain/core/contracts/order";

export const HOLD_ORDER_PERMISSION =
  "Permissions.PosTerminal.Sales.HoldOrder";
export const RECALL_LIST_PERMISSION =
  "Permissions.PosTerminal.Sales.RecallOrder";
export const RECALL_RESTORE_PERMISSION =
  "Permissions.PosTerminal.History.Recall";

export type HeldOrderIdentity = Readonly<{
  storeCode: string;
  deviceCode: string;
  cashierId: string;
  cashierName: string;
  userGuid: string | null;
}>;

export type ActivePricingCartSnapshot = Readonly<{
  sessionRevision: number;
  pricingState: PricingCartStateSnapshot;
  cart: CartSnapshot;
  recallBinding: RecallActiveBinding | null;
  terminalRecoveryRequired: boolean;
}>;

/**
 * lease 只在 runExclusive 回调内有效；等待主管扫码时不占锁，授权成功后
 * 必须在 lease 内重新读取并校验，避免旧快照覆盖新扫码商品。
 */
export interface ActivePricingCartLeasePort {
  read(): ActivePricingCartSnapshot;
  blockForRecallRecovery(
    recallBinding: RecallActiveBinding,
  ): void | Promise<void>;
  replace(
    pricingState: PricingCartStateSnapshot,
    recallBinding: RecallActiveBinding | null,
  ): void | Promise<void>;
  setRecallBinding(
    recallBinding: RecallActiveBinding | null,
  ): void | Promise<void>;
}

export interface ActivePricingCartPort {
  runExclusive<T>(
    operation: (lease: ActivePricingCartLeasePort) => T | Promise<T>,
  ): Promise<T>;
}

/**
 * 授权票据只由组合根或主管授权 feature 管理。挂单 feature 不登录、不缓存、
 * 不记录任何 token 或 scope；上层在 callback 的 finally 内负责释放临时授权。
 */
export interface HeldOrderAuthorizationPort {
  authorizeAndRun<T>(
    input: Readonly<{
      permissionCode: string;
      action: "hold" | "list" | "recall" | "recover" | "release" | "delete";
    }>,
    operation: () => T | Promise<T>,
  ): Promise<
    | Readonly<{ authorized: true; value: T }>
    | Readonly<{ authorized: false }>
  >;
}

/**
 * 手持 POS 复用旧 core contract 的挂单 Port；删除分阶段能力只在本 feature
 * 通过扩展口声明，避免越界修改共享的 core/contracts/held-orders.ts。
 */
export type HeldOrderDeleteStage = Readonly<{
  holdId: string;
  remoteCancellationRequired: boolean;
}>;

export type HeldOrderDeleteRepositoryPort = HeldOrderRecordRepositoryPort & {
  stageDeletePending(input: Readonly<{
    holdId: string;
    scope: HeldOrderScope;
    stagedAtIso: string;
  }>): Promise<HeldOrderDeleteStage | null>;
  deleteStagedPending(input: Readonly<{
    holdId: string;
    scope: HeldOrderScope;
  }>): Promise<boolean>;
};

export type HeldOrdersOrchestratorOptions = Readonly<{
  repository: HeldOrderRecordRepositoryPort;
  activeCart: ActivePricingCartPort;
  authorization: HeldOrderAuthorizationPort;
  identity: HeldOrderIdentity;
  createId(): string;
  nowIso(): string;
}>;

export type HeldOrderActionCode =
  | "held"
  | "recalled"
  | "recovered"
  | "released"
  | "deleted"
  | "authorization-denied"
  | "sale-mode-required"
  | "cart-empty"
  | "cart-not-empty"
  | "operation-in-progress"
  | "hold-failed"
  | "hold-committed-cart-not-cleared"
  | "hold-fence-not-cleared"
  | "terminal-fence-blocked"
  | "claim-failed"
  | "restore-failed"
  | "complete-failed"
  | "rollback-failed"
  | "release-failed"
  | "delete-failed"
  | "delete-shared-failed"
  | "load-failed"
  | "shared-prepared-awaiting-activation"
  | "shared-fence-held"
  | "shared-restore-failed"
  | "shared-conflict"
  | "shared-not-available"
  | "force-released"
  | "force-release-failed"
  | "force-release-unavailable"
  | "force-release-reason-required";

export type HeldOrderActionResult = Readonly<{
  ok: boolean;
  code: HeldOrderActionCode;
  holdId?: string;
}>;

/**
 * 跨设备共享挂单的远端 Pending 行（服务端 listPending 的视图投影）。
 * 只暴露列表所需字段，绝不携带 canonical 购物车 payload。
 */
export type SharedHeldOrderRemoteRow = Readonly<{
  holdGuid: string;
  deviceCode: string;
  cashierName: string;
  heldAtIso: string;
  lineCount: number;
  actualCents: number;
}>;

/** 本地挂单的共享发布状态（可选数据源；组合根未接线时 presenter 保守合并）。 */
export type SharedHeldOrderLocalShareRow = Readonly<{
  holdId: string;
  shareState: "NeedsEvaluation" | "PendingPublish" | "Published" | "Blocked";
  blockReason: string | null;
  requestedAtIso?: string | null | undefined;
  isSyntheticSharedClaim?: boolean | undefined;
}>;

export type SharedHeldOrderShareRequestOutcome =
  | "requested"
  | "already-requested"
  | "ineligible"
  | "not-found";

export type SharedHeldOrderTakeViewOutcome =
  | "restored"
  | "prepared-awaiting-activation"
  | "fence-held"
  | "conflict";

export type SharedHeldOrderTakeViewResult = Readonly<{
  ok: boolean;
  outcome: SharedHeldOrderTakeViewOutcome;
  holdGuid: string;
}>;

/**
 * 共享挂单视图端口：presenter 只依赖该端口，绝不在此层伪造持久能力。
 * 组合根负责把 shared coordinator/API 适配进来；forceRelease 未接线时
 * UI 不显示入口，presenter 返回 force-release-unavailable。
 */
export interface SharedHeldOrdersViewPort {
  listRemotePending(): Promise<readonly SharedHeldOrderRemoteRow[]>;
  listLocalShareState?(): Promise<readonly SharedHeldOrderLocalShareRow[]>;
  requestShare?(holdGuid: string): Promise<SharedHeldOrderShareRequestOutcome>;
  takeRemoteHold(holdGuid: string): Promise<SharedHeldOrderTakeViewResult>;
  recallLocalPublication(holdGuid: string): Promise<SharedHeldOrderTakeViewResult>;
  /** 仅原设备可取消其已发布挂单；组合根负责先暂停并等待本地发布循环。 */
  cancelOwnedHold?(holdGuid: string): Promise<void>;
  /**
   * 优先释放本机 shared claim；返回 false 表示不存在 shared claim，调用方才可
   * 回退 legacy release，避免后者只清 fence 而留下 shared claim 孤儿。
   */
  releaseOwnedClaim?(holdGuid: string): Promise<boolean>;
  /**
   * 可选强制释放。实现必须由组合根包一层 supervisor/History.Recall 授权，
   * 并在调用服务端 force-release 前拒绝空原因。
   */
  forceRelease?(input: Readonly<{
    holdGuid: string;
    reason: string;
  }>): Promise<HeldOrderActionResult>;
}

export function heldOrderScope(identity: HeldOrderIdentity): HeldOrderScope {
  return {
    storeCode: requiredText(identity.storeCode, "Store code"),
    deviceCode: requiredText(identity.deviceCode, "Device code"),
  };
}

export function heldOrderActor(identity: HeldOrderIdentity): HeldOrderActor {
  return {
    cashierId: requiredText(identity.cashierId, "Cashier id"),
    cashierName: requiredText(identity.cashierName, "Cashier name"),
  };
}

/** 挂单清车必须保留当前促销快照与售卖模式，避免恢复时按新时间重新定价。 */
export function emptySalePricingState(
  source: PricingCartStateSnapshot,
): PricingCartStateSnapshot {
  if (source.mode !== "sale") {
    throw new TypeError("Held orders only support sale carts.");
  }
  return {
    revision: source.revision + 1,
    mode: "sale",
    asOfIso: source.asOfIso,
    promotions: source.promotions,
    lines: [],
  };
}

export function isEmptySaleCart(snapshot: ActivePricingCartSnapshot): boolean {
  return (
    snapshot.cart.mode === "sale" &&
    snapshot.pricingState.mode === "sale" &&
    snapshot.cart.lines.length === 0 &&
    snapshot.pricingState.lines.length === 0
  );
}

export function isSaleCart(snapshot: ActivePricingCartSnapshot): boolean {
  return (
    snapshot.cart.mode === "sale" && snapshot.pricingState.mode === "sale"
  );
}

export function isInHeldOrderScope(
  summary: HeldOrderSummary,
  scope: HeldOrderScope,
): boolean {
  return (
    summary.scope.storeCode === scope.storeCode &&
    summary.scope.deviceCode === scope.deviceCode
  );
}

export function createHoldAudit(
  input: Readonly<{
    identity: HeldOrderIdentity;
    holdId: string;
    occurredAtIso: string;
    beforeActualAmountCents: number;
    createId(): string;
  }>,
): AuditEventDraft {
  return createHeldOrderAudit({
    ...input,
    action: "hold",
    eventType: "ORDER_HOLD",
    afterActualAmountCents: 0,
  });
}

export function createRecallAudit(
  input: Readonly<{
    identity: HeldOrderIdentity;
    holdId: string;
    occurredAtIso: string;
    afterActualAmountCents: number;
    createId(): string;
  }>,
): AuditEventDraft {
  return createHeldOrderAudit({
    ...input,
    action: "recall",
    eventType: "ORDER_RECALL",
    beforeActualAmountCents: 0,
  });
}

function createHeldOrderAudit(
  input: Readonly<{
    identity: HeldOrderIdentity;
    holdId: string;
    occurredAtIso: string;
    beforeActualAmountCents: number;
    afterActualAmountCents: number;
    action: "hold" | "recall";
    eventType: "ORDER_HOLD" | "ORDER_RECALL";
    createId(): string;
  }>,
): AuditEventDraft {
  assertIso(input.occurredAtIso);
  assertMoney(input.beforeActualAmountCents);
  assertMoney(input.afterActualAmountCents);
  return {
    eventId: requiredText(input.createId(), "Audit event id"),
    eventType: input.eventType,
    occurredAtIso: input.occurredAtIso,
    orderGuid: null,
    correlationId: requiredText(input.holdId, "Hold id"),
    // 中文注释：审计仅记录动作、范围和汇总金额，绝不复制商品、条码、顾客或付款资料。
    payload: {
      source: "pos-handheld",
      action: input.action,
      storeCode: requiredText(input.identity.storeCode, "Store code"),
      deviceCode: requiredText(input.identity.deviceCode, "Device code"),
      cashierId: requiredText(input.identity.cashierId, "Cashier id"),
      beforeActualAmountCents: input.beforeActualAmountCents,
      afterActualAmountCents: input.afterActualAmountCents,
      ...auditActorPayload(input.identity),
    },
  };
}

export function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new TypeError(`${label} is required.`);
  return normalized;
}

export function requiredId(createId: () => string, label: string): string {
  return requiredText(createId(), label);
}

export function assertIso(value: string): string {
  if (!Number.isFinite(Date.parse(value))) {
    throw new TypeError("Held order clock must return a valid ISO timestamp.");
  }
  return value;
}

function assertMoney(value: number): void {
  if (!Number.isSafeInteger(value)) {
    throw new TypeError("Held order amount must be an integer number of cents.");
  }
}
