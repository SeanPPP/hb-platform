import type { CartLine } from "./cart";
import type { Money } from "./money";
import type { LocalOrderState } from "./state-machines";
import type {
  RecallActiveBinding,
  TerminalCheckoutContext,
} from "./terminal-cart";

export type TenderMethod = "cash" | "card" | "voucher";

export type OrderTender = Readonly<{
  tenderGuid: string;
  method: TenderMethod;
  amount: Money;
  reference: string | null;
  reservationToken: string | null;
}>;

export type LocalOrder = Readonly<{
  orderGuid: string;
  localSequence: number;
  storeCode: string;
  deviceCode: string;
  cashierId: string;
  cashierName: string;
  soldAtIso: string;
  state: LocalOrderState;
  total: Money;
  discount: Money;
  actualAmount: Money;
  lines: readonly CartLine[];
  tenders: readonly OrderTender[];
  originalOrderGuid: string | null;
}>;

export type CompleteCashOrderCommand = Readonly<{
  order: LocalOrder;
  auditEvents: readonly AuditEventDraft[];
  outbox: OutboxMessageDraft;
  requiresDrawer: boolean;
  printPolicy: "never" | "prompt" | "automatic";
}>;

export type CashCheckoutIntent = Readonly<{
  checkoutIntentId: string;
  /**
   * 由业务层对确认时的购物车、现金额、门店、设备和收银员生成确定性签名。
   * SQLCipher 账本用它拒绝同一 intent 被换内容重放。
   */
  requestSignature: string;
  cashDueCents: number;
  changeCents: number;
}>;

export type CashPrintJobDraft = Readonly<{
  jobId: string;
  orderGuid: string;
  printerId: string;
  receiptBytes: Uint8Array;
  isReprint: false;
}>;

export type CashDrawerEventDraft = Readonly<{
  eventId: string;
  orderGuid: string;
  /** 钱箱通过这台芯烨打印机的 RJ11 端口触发；任务必须永久绑定原始外设。 */
  printerId: string;
  printJobId: string | null;
  reason: string;
}>;

export type CashFulfilmentDraft = Readonly<{
  print: CashPrintJobDraft | null;
  drawer: CashDrawerEventDraft | null;
}>;

export type DurableCashOrderCommit = Readonly<{
  intent: CashCheckoutIntent;
  command: CompleteCashOrderCommand;
  fulfilment: CashFulfilmentDraft;
  /** 必须参与 checkout intent 签名，且只能由活动购物车 session 注入。 */
  terminalContext: TerminalCheckoutContext;
  recalledHoldCompletion: RecalledHoldCompletion | null;
}>;

export type RecalledHoldCompletion = Readonly<{
  binding: RecallActiveBinding;
  recalledAtIso: string;
  recallAudit: AuditEventDraft;
}>;

export type DurableCashOrderCommitResult = Readonly<{
  replayed: boolean;
  orderGuid: string;
  cashDueCents: number;
  changeCents: number;
}>;

export type ApprovedPaymentOrderCommit = Readonly<{
  attemptId: string;
  orderGuid: string;
  tenderGuid: string;
  completionAuditEvents: readonly AuditEventDraft[];
  outbox: OutboxMessageDraft;
  fulfilment: CashFulfilmentDraft;
}>;

export type ApprovedPaymentOrderCommitResult = Readonly<{
  replayed: boolean;
  orderGuid: string;
  tenderGuid: string;
  completed: boolean;
  signedTenderAmountCents: number;
}>;

export type AuditEventDraft = Readonly<{
  eventId: string;
  eventType: string;
  occurredAtIso: string;
  orderGuid: string | null;
  correlationId: string;
  payload: Readonly<Record<string, unknown>>;
}>;

export type OutboxMessageDraft = Readonly<{
  messageId: string;
  aggregateId: string;
  kind: "order-sync" | "audit-batch";
  payloadJson: string;
  nextAttemptAtIso: string;
}>;
