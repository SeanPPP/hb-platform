import assert from "node:assert/strict";
import test from "node:test";

import { SqliteSharedHeldOrderLocalPublication } from "./shared-held-order-local-publication";
import {
  TEST_NOW_ISO,
  fakeEncryptor,
  insertHeldOrderRow,
  openTestDatabase,
} from "./shared-held-order-test-support";

import type { PricingCartStateSnapshot } from "@/core/contracts";
import type { SqliteConnectionPort } from "@/core/db/types";

const SCOPE = { storeCode: "S1", deviceCode: "IPAD-01" } as const;

function pricingState(): PricingCartStateSnapshot {
  return {
    revision: 4,
    mode: "sale",
    asOfIso: TEST_NOW_ISO,
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
        syncProvenance: { referenceCode: null, priceSource: 0 },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: { kind: "none" },
      },
    ],
  };
}

async function seedHold(
  connection: SqliteConnectionPort,
  holdId: string,
): Promise<void> {
  await insertHeldOrderRow(connection, {
    holdId,
    payloadCiphertext: await fakeEncryptor.encrypt(
      JSON.stringify({ version: 1, pricingState: pricingState() }),
    ),
  });
}

test("本地已发布副本可离线 recall：返回可发布 cart", async () => {
  const connection = await openTestDatabase();
  await seedHold(connection, "hold-1");
  const reader = new SqliteSharedHeldOrderLocalPublication(connection, fakeEncryptor);
  const result = await reader.loadEligible("hold-1", SCOPE);
  assert.equal(result.eligible, true);
  if (result.eligible !== true) return;
  assert.equal(result.cart.pricingState.revision, 4);
  assert.equal(result.cart.pricingState.lines[0]?.lookupCode, "100");
  await connection.close();
});

test("本地副本不存在或已 Recalling 时拒绝，不暴露 payload", async () => {
  const connection = await openTestDatabase();
  const reader = new SqliteSharedHeldOrderLocalPublication(connection, fakeEncryptor);

  const missing = await reader.loadEligible("hold-missing", SCOPE);
  assert.deepEqual(missing, { eligible: false, reason: "not-found" });

  await seedHold(connection, "hold-2");
  await connection.run(
    `UPDATE held_order_records
     SET status = 'Recalling',
         recall_attempt_id = 'attempt-1',
         recalling_at_iso = ?,
         recalling_cashier_id = 'cashier-2',
         recalling_cashier_name = 'Cashier Two'
     WHERE hold_id = 'hold-2'`,
    [TEST_NOW_ISO],
  );
  const recalling = await reader.loadEligible("hold-2", SCOPE);
  assert.deepEqual(recalling, { eligible: false, reason: "in-progress" });
  await connection.close();
});

test("损坏 payload 或非可发布状态按 not-shareable 拒绝", async () => {
  const connection = await openTestDatabase();
  const reader = new SqliteSharedHeldOrderLocalPublication(connection, fakeEncryptor);

  await insertHeldOrderRow(connection, {
    holdId: "hold-bad",
    payloadCiphertext: await fakeEncryptor.encrypt(JSON.stringify({ version: 99 })),
  });
  const corrupted = await reader.loadEligible("hold-bad", SCOPE);
  assert.deepEqual(corrupted, { eligible: false, reason: "not-shareable" });

  await insertHeldOrderRow(connection, {
    holdId: "hold-blocked",
    payloadCiphertext: await fakeEncryptor.encrypt(
      JSON.stringify({ version: 1, pricingState: pricingState() }),
    ),
  });
  await connection.run(
    `UPDATE held_order_records
     SET share_requested_at_iso = ?, share_state = 'Blocked'
     WHERE hold_id = 'hold-blocked'`,
    [TEST_NOW_ISO],
  );
  const blocked = await reader.loadEligible("hold-blocked", SCOPE);
  assert.deepEqual(blocked, { eligible: false, reason: "not-shareable" });
  await connection.close();
});

test("删除中挂单可只读恢复同一冻结快照，用于建立服务端取消终态", async () => {
  const connection = await openTestDatabase();
  await seedHold(connection, "hold-delete");
  await connection.run(
    `UPDATE held_order_records
     SET share_state = 'Blocked',
         publish_block_reason = 'LOCAL_DELETE_PENDING'
     WHERE hold_id = 'hold-delete'`,
  );
  const reader = new SqliteSharedHeldOrderLocalPublication(connection, fakeEncryptor);

  const cart = await reader.loadDeletePending("hold-delete", SCOPE);

  assert.equal(cart?.pricingState.revision, 4);
  assert.equal(cart?.pricingState.lines[0]?.lookupCode, "100");
  assert.equal(await reader.loadDeletePending("hold-missing", SCOPE), null);
  await connection.close();
});

test("非 LOCAL_DELETE_PENDING 的 Blocked 行不能进入取消收口", async () => {
  const connection = await openTestDatabase();
  await seedHold(connection, "hold-other-block");
  await connection.run(
    `UPDATE held_order_records
     SET share_requested_at_iso = ?,
         share_state = 'Blocked',
         publish_block_reason = 'SHARED_CART_INVALID'
     WHERE hold_id = 'hold-other-block'`,
    [TEST_NOW_ISO],
  );
  const reader = new SqliteSharedHeldOrderLocalPublication(connection, fakeEncryptor);

  assert.equal(await reader.loadDeletePending("hold-other-block", SCOPE), null);
  await connection.close();
});
