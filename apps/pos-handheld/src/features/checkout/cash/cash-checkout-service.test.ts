import assert from "node:assert/strict";
import test from "node:test";

import { CashCheckoutService } from "./cash-checkout-service";

import type { CartSnapshot, DatabasePort, DatabaseTransactionPort } from "@/core/contracts";


class Database implements DatabasePort {
  public commands: unknown[] = []; public fail = false;
  public async runInTransaction<T>(operation: (tx: DatabaseTransactionPort) => Promise<T>): Promise<T> { if (this.fail) throw new Error("disk full"); return operation({ completeCashOrder: async (command) => { this.commands.push(command); } }); }
}
const cart = (amount: number, kind: "sale" | "return" = "sale"): CartSnapshot => ({ revision: 1, mode: kind === "return" ? "return" : "sale", subtotal: { currency: "AUD", cents: amount }, discount: { currency: "AUD", cents: 0 }, actualAmount: { currency: "AUD", cents: amount }, lines: [{ lineId: "L1", productCode: "P1", itemNumber: null, lookupCode: "1", displayName: "Tea", quantity: "1", unitPrice: { currency: "AUD", cents: Math.abs(amount) }, discount: { currency: "AUD", cents: 0 }, actualAmount: { currency: "AUD", cents: amount }, priceSource: "catalog", kind, returnSourceKey: kind === "return" ? "R1" : null, originalOrderGuid: kind === "return" ? "O1" : null, originalOrderDetailGuid: null }] });
const input = (snapshot: CartSnapshot, tendered: number | null) => ({ checkoutIntentId: "intent-1", cart: snapshot, cashTenderedCents: tendered, storeCode: "S1", deviceCode: "D1", cashierId: "C1", cashierName: "Alice", userGuid: "user-guid-c1" });

test("7.82/7.83 现金应付按 0.05 取整，实收足额才完成", async () => {
  const db = new Database(); const service = new CashCheckoutService(db, { nextLocalSequence: async () => 1 }, ids());
  const a = await service.complete(input(cart(782), 1000)); const b = await service.complete({ ...input(cart(783), 1000), checkoutIntentId: "intent-2" });
  assert.equal(a.cashDueCents, 780); assert.equal(a.changeCents, 220); assert.equal(b.cashDueCents, 785); assert.equal(b.changeCents, 215); assert.equal(db.commands.length, 2);
});
test("少收拒绝，事务未写入且不允许清空购物车", async () => { const db = new Database(); const service = new CashCheckoutService(db, { nextLocalSequence: async () => 1 }, ids()); await assert.rejects(() => service.complete(input(cart(782), 775)), /insufficient/i); assert.equal(db.commands.length, 0); });
test("零金额订单不伪造 tender", async () => { const db = new Database(); const service = new CashCheckoutService(db, { nextLocalSequence: async () => 1 }, ids()); const result = await service.complete(input(cart(0), null)); assert.equal(result.completed, true); assert.equal((db.commands[0] as { order: { tenders: unknown[] } }).order.tenders.length, 0); });
test("事务失败不返回 completed 或清空许可", async () => { const db = new Database(); db.fail = true; const service = new CashCheckoutService(db, { nextLocalSequence: async () => 1 }, ids()); await assert.rejects(() => service.complete(input(cart(500), 500))); assert.equal(db.commands.length, 0); });
test("同一 checkoutIntentId 重复确认复用同一订单", async () => { const db = new Database(); const service = new CashCheckoutService(db, { nextLocalSequence: async () => 1 }, ids()); const a = await service.complete(input(cart(500), 500)); const b = await service.complete(input(cart(500), 500)); assert.equal(a.orderGuid, b.orderGuid); assert.equal(db.commands.length, 1); });
test("离线退货必须具备原单与本地容量；未知或超额不进入事务", async () => { const db = new Database(); const service = new CashCheckoutService(db, { nextLocalSequence: async () => 1 }, ids({ returnCapacity: async () => false })); await assert.rejects(() => service.complete(input(cart(-500, "return"), -500)), /capacity/i); assert.equal(db.commands.length, 0); });
function ids(overrides: Partial<{ returnCapacity: (snapshot: CartSnapshot) => Promise<boolean> }> = {}) { let n = 0; return { createId: () => `id-${++n}`, nowIso: () => "2026-07-28T00:00:00.000Z", returnCapacity: overrides.returnCapacity ?? (async () => true) }; }
