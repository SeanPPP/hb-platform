import { createAud, type CartSnapshot, type DatabasePort, type LocalOrder } from "@/core/contracts";
import { calculateCashSettlement } from "@/features/sales/domain";

export interface LocalSequencePort { nextLocalSequence(): Promise<number>; }
export interface OfflineReturnCapacityPort { hasCapacity(snapshot: CartSnapshot): Promise<boolean>; }
export type CashCheckoutDependencies = Readonly<{ createId: () => string; nowIso: () => string; returnCapacity: (snapshot: CartSnapshot) => Promise<boolean> }>;
export type CashCheckoutInput = Readonly<{ checkoutIntentId: string; cart: CartSnapshot; cashTenderedCents: number | null; storeCode: string; deviceCode: string; cashierId: string; cashierName: string }>;
export type CashDrawerDisposition =
  | "not-required"
  | "queued"
  | "permission-denied"
  | "disabled"
  | "unavailable"
  | "replayed";
export type CashCheckoutResult = Readonly<{ completed: true; canClearCart: true; orderGuid: string; cashDueCents: number; changeCents: number; postCommit: Readonly<{ requestDrawer: boolean; drawerDisposition: CashDrawerDisposition; printPolicy: "automatic" | "never" }> }>;

/** 现金交易只在账本事务提交后才允许 UI 清空购物车；外设动作严格留在提交之后。 */
export class CashCheckoutService {
  private readonly byIntent = new Map<string, Promise<CashCheckoutResult>>();
  public constructor(private readonly database: DatabasePort, private readonly sequences: LocalSequencePort, private readonly deps: CashCheckoutDependencies) {}
  public complete(input: CashCheckoutInput): Promise<CashCheckoutResult> {
    if (!input.checkoutIntentId.trim()) return Promise.reject(new Error("checkoutIntentId is required."));
    const existing = this.byIntent.get(input.checkoutIntentId); if (existing) return existing;
    const pending = this.completeOnce(input).catch((error: unknown) => { this.byIntent.delete(input.checkoutIntentId); throw error; });
    this.byIntent.set(input.checkoutIntentId, pending); return pending;
  }
  private async completeOnce(input: CashCheckoutInput): Promise<CashCheckoutResult> {
    const actual = input.cart.actualAmount.cents; assertCents(actual); if (!input.cart.lines.length) throw new Error("Cash checkout requires cart lines.");
    if (input.cart.mode === "return") {
      if (input.cart.lines.some((line) => line.kind !== "return" || !line.returnSourceKey || !line.originalOrderGuid) || !(await this.deps.returnCapacity(input.cart))) throw new Error("Offline return capacity is unknown or exhausted.");
    }
    const settlement = calculateCashSettlement({ actualAmount: createAud(actual), cashTendered: createAud(input.cashTenderedCents ?? 0) });
    if (actual > 0 && (input.cashTenderedCents === null || settlement.normalizedCashTendered.cents < settlement.cashDue.cents)) throw new Error("Insufficient cash tendered.");
    if (actual < 0 && (input.cashTenderedCents === null || settlement.normalizedCashTendered.cents > settlement.cashDue.cents)) throw new Error("Insufficient cash refund tendered.");
    if (actual === 0 && input.cashTenderedCents !== null && input.cashTenderedCents !== 0) throw new Error("Zero order cannot accept cash tender.");
    const orderGuid = this.deps.createId(); const soldAtIso = this.deps.nowIso(); const sequence = await this.sequences.nextLocalSequence();
    const order: LocalOrder = { orderGuid, localSequence: sequence, storeCode: input.storeCode, deviceCode: input.deviceCode, cashierId: input.cashierId, cashierName: input.cashierName, soldAtIso, state: "PendingSync", total: input.cart.subtotal, discount: input.cart.discount, actualAmount: input.cart.actualAmount, lines: input.cart.lines, tenders: actual === 0 ? [] : [{ tenderGuid: this.deps.createId(), method: "cash", amount: createAud(actual), reference: null, reservationToken: null }], originalOrderGuid: input.cart.lines.find((line) => line.originalOrderGuid)?.originalOrderGuid ?? null };
    await this.database.runInTransaction((tx) => tx.completeCashOrder({ order, auditEvents: [{ eventId: this.deps.createId(), eventType: actual < 0 ? "RETURN_REFUND_COMPLETE" : "SALE_COMPLETE", occurredAtIso: soldAtIso, orderGuid, correlationId: orderGuid, payload: { checkoutIntentId: input.checkoutIntentId, localSequence: sequence, cashDueCents: settlement.cashDue.cents, changeCents: settlement.change.cents } }], outbox: { messageId: this.deps.createId(), aggregateId: orderGuid, kind: "order-sync", payloadJson: JSON.stringify({ orderGuid }), nextAttemptAtIso: soldAtIso }, requiresDrawer: actual !== 0, printPolicy: "automatic" }));
    return { completed: true, canClearCart: true, orderGuid, cashDueCents: settlement.cashDue.cents, changeCents: settlement.change.cents, postCommit: { requestDrawer: actual !== 0, drawerDisposition: actual !== 0 ? "queued" : "not-required", printPolicy: "automatic" } };
  }
}
function assertCents(value: number): void { if (!Number.isSafeInteger(value)) throw new Error("Cash amount must be integer cents."); }
