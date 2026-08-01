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
import type { AuditEventDraft } from "@/core/contracts/order";

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
      action: "hold" | "list" | "recall" | "recover" | "release";
    }>,
    operation: () => T | Promise<T>,
  ): Promise<
    | Readonly<{ authorized: true; value: T }>
    | Readonly<{ authorized: false }>
  >;
}

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
  | "load-failed";

export type HeldOrderActionResult = Readonly<{
  ok: boolean;
  code: HeldOrderActionCode;
  holdId?: string;
}>;

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
      source: "ipad-pos",
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
