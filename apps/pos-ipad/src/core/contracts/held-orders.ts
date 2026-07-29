import type { AuditEventDraft } from "./order";
import type { PricingCartStateSnapshot } from "./pricing-cart-state";
import type {
  RecallActiveBinding,
  TerminalCartFence,
  TerminalCartScope,
} from "./terminal-cart";

export type HeldOrderStatus = "Pending" | "Recalling" | "Recalled";

export type HeldOrderScope = TerminalCartScope;

export type HeldOrderActor = Readonly<{
  cashierId: string;
  cashierName: string;
}>;

export type HeldOrderPayloadV1 = Readonly<{
  version: 1;
  pricingState: PricingCartStateSnapshot;
}>;

export type HeldOrderSummary = Readonly<{
  holdId: string;
  localSequence: number;
  scope: HeldOrderScope;
  heldBy: HeldOrderActor;
  status: HeldOrderStatus;
  itemCount: number;
  subtotalCents: number;
  discountCents: number;
  actualAmountCents: number;
  heldAtIso: string;
  recallingAtIso: string | null;
}>;

export type HoldCartCommand = Readonly<{
  holdId: string;
  scope: HeldOrderScope;
  heldBy: HeldOrderActor;
  payload: HeldOrderPayloadV1;
  heldAtIso: string;
  audit: AuditEventDraft;
}>;

export type RecallClaim = Readonly<{
  hold: HeldOrderSummary;
  recallAttemptId: string;
  payload: HeldOrderPayloadV1;
}>;

/**
 * V2 挂单 Port 与 M3 legacy HeldOrderRepositoryPort 并存。
 * legacy 记录缺少完整定价状态和设备范围，只能支持导出，禁止静默有损恢复。
 */
export interface HeldOrderRecordRepositoryPort {
  /**
   * 同一事务写 Pending 挂单、ORDER_HOLD 审计和该终端唯一的 HoldClear fence。
   */
  hold(command: HoldCartCommand): Promise<HeldOrderSummary>;
  listPending(
    scope: HeldOrderScope,
    limit: number,
  ): Promise<readonly HeldOrderSummary[]>;
  /**
   * 同一事务执行 Pending -> Recalling 并写唯一 RecallActive fence。
   */
  claimRecall(input: Readonly<{
    holdId: string;
    scope: HeldOrderScope;
    recalledBy: HeldOrderActor;
    recallAttemptId: string;
    recallingAtIso: string;
  }>): Promise<RecallClaim | null>;
  getTerminalFence(scope: HeldOrderScope): Promise<TerminalCartFence | null>;
  loadRecallForFence(
    binding: RecallActiveBinding,
  ): Promise<RecallClaim | null>;
  confirmHoldCartCleared(input: Readonly<{
    scope: HeldOrderScope;
    holdId: string;
  }>): Promise<boolean>;
  releaseRecallAfterCartCleared(input: Readonly<{
    binding: RecallActiveBinding;
    releasedAtIso: string;
  }>): Promise<boolean>;
  listRecoverable(
    scope: HeldOrderScope,
  ): Promise<readonly RecallClaim[]>;
}
