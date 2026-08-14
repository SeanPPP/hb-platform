import type { ReceiptLocale, ReceiptPaper } from "./receipt-document";

import type {
  CashFulfilmentDraft,
  CompleteCashOrderCommand,
  LocalOrder,
} from "@/core/contracts";
import type {
  DurableCashFulfilmentPlan,
  DurableCashFulfilmentPlannerPort,
} from "@/features/checkout/cash/durable-cash-checkout-service";

export type ReceiptStoreHeading = Readonly<{
  brandName: string;
  storeName: string;
  address: string;
  phone: string;
  abn: string;
}>;

/** 打印、钱箱共用同一台芯烨设备；缺失其标识时两者都不能排队。 */
export type ReceiptFulfilmentSettings = Readonly<{
  printEnabled: boolean;
  drawerEnabled: boolean;
  /** 已解析的 Permissions.PosTerminal.CashDrawer.Open；缺失或 false 均禁止排队。 */
  cashDrawerPermissionAllowed: boolean;
  printerId: string | null;
  paper: ReceiptPaper;
  locale: ReceiptLocale;
  store: ReceiptStoreHeading;
}>;

export interface ReceiptFulfilmentSettingsProvider {
  getSettings(): Promise<ReceiptFulfilmentSettings | null>;
}

type CashSettlementForReceipt = Readonly<{
  isRefund: boolean;
}>;

/**
 * 把已验证的耐久现金命令转换为事务内持久化的外设草稿。
 *
 * 不在这里发送 BLE 指令：打印和开钱箱必须等订单、审计和 outbox 同一 SQLCipher
 * 事务成功后才由履约层发出，避免外设成功而订单丢失。
 */
export class CashFulfilmentPlanner implements DurableCashFulfilmentPlannerPort {
  public constructor(
    private readonly settingsProvider: ReceiptFulfilmentSettingsProvider,
    private readonly createId: () => string,
  ) {}

  public async createDraft(command: CompleteCashOrderCommand): Promise<DurableCashFulfilmentPlan> {
    const settlement = validateCashCommand(command);
    const requested = command.requiresDrawer;
    if (!requested) {
      return {
        draft: emptyDraft(),
        drawerDisposition: "not-required",
      };
    }

    const settings = await this.settingsProvider.getSettings();
    const disposition = classifyDrawer(settings);
    if (disposition !== "queued") {
      return {
        draft: emptyDraft(),
        drawerDisposition: disposition,
      };
    }
    if (!settings) {
      throw new Error("Queued cash drawer settings are unavailable.");
    }

    // 中文注释：与 WPF OnPaymentCompleted 对齐，普通现金/混合销售完成不自动打印。
    // 手动打印和后续卡/券退款自动小票必须走各自明确工作流，不能在现金提交时伪造作业。
    const print = null;
    const drawer = {
      eventId: requiredId(this.createId(), "drawer event id"),
      orderGuid: command.order.orderGuid,
      printerId: requiredId(settings.printerId ?? "", "printer id").trim(),
      printJobId: null,
      reason: settlement.isRefund ? "cash-refund" : "cash-sale",
    } as const;

    return {
      draft: { print, drawer },
      drawerDisposition: "queued",
    };
  }
}

function emptyDraft(): CashFulfilmentDraft {
  return { print: null, drawer: null };
}

function validateCashCommand(command: CompleteCashOrderCommand): CashSettlementForReceipt {
  const { order } = command;
  assertIdentifier(order.orderGuid, "order guid");
  assertIdentifier(order.storeCode, "store code");
  assertIdentifier(order.deviceCode, "device code");
  assertIdentifier(order.cashierId, "cashier id");
  assertIdentifier(order.cashierName, "cashier name");
  assertIso(order.soldAtIso, "order sold time");
  assertPositiveSafeInteger(order.localSequence, "order local sequence");
  assertCents(order.total.cents, "order total");
  assertCents(order.discount.cents, "order discount");
  assertCents(order.actualAmount.cents, "order actual amount");
  if (!order.lines.length) throw new Error("cash command requires order lines.");
  if (command.outbox.aggregateId !== order.orderGuid) throw new Error("outbox order guid mismatch.");
  assertIdentifier(command.outbox.messageId, "outbox message id");
  assertIso(command.outbox.nextAttemptAtIso, "outbox next attempt time");

  let lineTotal = 0;
  for (const line of order.lines) {
    assertIdentifier(line.lineId, "line id");
    assertIdentifier(line.lookupCode, "line lookup code");
    assertIdentifier(line.displayName, "line display name");
    assertIdentifier(line.quantity, "line quantity");
    assertCents(line.unitPrice.cents, "line unit price");
    assertCents(line.discount.cents, "line discount");
    assertCents(line.actualAmount.cents, "line actual amount");
    lineTotal = checkedAdd(lineTotal, line.actualAmount.cents, "line actual amount");
  }
  if (lineTotal !== order.actualAmount.cents) throw new Error("order and line actual amounts mismatch.");

  const isRefund = order.actualAmount.cents < 0;
  validateCashTenders(order, isRefund);
  const audit = readCompletionAudit(command, isRefund);
  const cashDueCents = readCents(audit.payload, "cashDueCents");
  const changeCents = readCents(audit.payload, "changeCents");
  const auditLocalSequence = readPositiveSafeInteger(audit.payload, "localSequence");
  const checkoutIntentId = audit.payload.checkoutIntentId;
  if (auditLocalSequence !== order.localSequence) throw new Error("audit local sequence mismatch.");
  if (typeof checkoutIntentId !== "string" || !checkoutIntentId.trim()) throw new Error("audit checkout intent id is required.");

  if (isRefund) {
    if (cashDueCents !== roundCashCents(order.actualAmount.cents) || changeCents !== 0) {
      throw new Error("refund cash settlement mismatch.");
    }
    return { isRefund: true };
  }

  if (cashDueCents < 0 || changeCents < 0) throw new Error("sale cash settlement cannot be negative.");
  checkedAdd(cashDueCents, changeCents, "cash tendered");
  if (order.actualAmount.cents === 0) {
    if (cashDueCents !== 0 || changeCents !== 0) throw new Error("zero order cash settlement mismatch.");
    return { isRefund: false };
  }
  if (cashDueCents !== roundCashCents(order.actualAmount.cents)) throw new Error("sale cash due mismatch.");
  return { isRefund: false };
}

function validateCashTenders(order: LocalOrder, isRefund: boolean): void {
  if (order.actualAmount.cents === 0) {
    if (order.tenders.length !== 0) throw new Error("zero order cannot have cash tender.");
    return;
  }
  if (order.tenders.length !== 1 || order.tenders[0]?.method !== "cash") {
    throw new Error("cash command requires exactly one cash tender.");
  }
  const tender = order.tenders[0];
  if (!tender) throw new Error("cash tender is required.");
  assertIdentifier(tender.tenderGuid, "cash tender id");
  assertCents(tender.amount.cents, "cash tender amount");
  if (tender.amount.cents !== order.actualAmount.cents || (isRefund && tender.amount.cents >= 0)) {
    throw new Error("cash tender amount mismatch.");
  }
}

function readCompletionAudit(command: CompleteCashOrderCommand, isRefund: boolean) {
  if (command.auditEvents.length !== 1) throw new Error("cash command requires exactly one completion audit.");
  const audit = command.auditEvents[0];
  if (!audit) throw new Error("cash completion audit is required.");
  const expectedEventType = isRefund ? "RETURN_REFUND_COMPLETE" : "SALE_COMPLETE";
  if (audit.eventType !== expectedEventType) throw new Error("cash completion audit event type mismatch.");
  if (audit.orderGuid !== command.order.orderGuid || audit.correlationId !== command.order.orderGuid) {
    throw new Error("audit order guid mismatch.");
  }
  assertIdentifier(audit.eventId, "audit event id");
  assertIso(audit.occurredAtIso, "audit occurred time");
  return audit;
}

function classifyDrawer(
  value: ReceiptFulfilmentSettings | null,
): DurableCashFulfilmentPlan["drawerDisposition"] {
  if (
    !value ||
    typeof value.drawerEnabled !== "boolean" ||
    typeof value.cashDrawerPermissionAllowed !== "boolean"
  ) {
    return "unavailable";
  }
  if (!value.drawerEnabled) return "disabled";
  if (!value.cashDrawerPermissionAllowed) return "permission-denied";
  if (typeof value.printerId !== "string" || !value.printerId.trim()) {
    return "unavailable";
  }
  return "queued";
}

function readCents(payload: Readonly<Record<string, unknown>>, key: string): number {
  const value = payload[key];
  assertCents(value, `audit ${key}`);
  return value;
}

function readPositiveSafeInteger(payload: Readonly<Record<string, unknown>>, key: string): number {
  const value = payload[key];
  assertPositiveSafeInteger(value, `audit ${key}`);
  return value;
}

function checkedAdd(left: number, right: number, label: string): number {
  const result = left + right;
  assertCents(result, label);
  return result;
}

/** 与销售领域的 AUD 0.05 现金取整保持一致，避免由 UI 浮点数再次计算。 */
function roundCashCents(value: number): number {
  const sign = Math.sign(value);
  const absolute = Math.abs(value);
  const rounded = (Math.floor(absolute / 5) + (absolute % 5 >= 3 ? 1 : 0)) * 5;
  return sign * rounded;
}

function assertCents(value: unknown, label: string): asserts value is number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) throw new Error(`${label} must use integer cents.`);
}

function assertPositiveSafeInteger(value: unknown, label: string): asserts value is number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value <= 0) throw new Error(`${label} must be a positive safe integer.`);
}

function assertIdentifier(value: unknown, label: string): asserts value is string {
  if (typeof value !== "string" || !value.trim()) throw new Error(`${label} is required.`);
}

function assertIso(value: unknown, label: string): asserts value is string {
  assertIdentifier(value, label);
  if (Number.isNaN(Date.parse(value))) throw new Error(`${label} must be an ISO date.`);
}

function requiredId(value: string, label: string): string {
  assertIdentifier(value, label);
  return value;
}
