import assert from "node:assert/strict";
import test from "node:test";


import {
  SqliteSharedHeldOrderPublicationQueue,
  publishRetryDelayMs,
  type SharedHeldOrderPublicationQueuePort,
} from "./shared-held-order-publication-queue";
import {
  TEST_NOW_ISO,
  fakeEncryptor,
  insertHeldOrderRow,
  openTestDatabase,
} from "./shared-held-order-test-support";

import type { SqliteConnectionPort } from "@/core/db/types";

const SCOPE = { storeCode: "S1", deviceCode: "IPAD-01" } as const;
const PAYLOAD_JSON = JSON.stringify({
  version: 1,
  pricingState: {
    revision: 7,
    mode: "sale",
    asOfIso: TEST_NOW_ISO,
    promotions: [],
    lines: [
      {
        lineId: "line-1",
        productCode: "P-1",
        itemNumber: null,
        lookupCode: "100",
        displayName: "Item one",
        quantity: 1,
        unitPriceCents: 1002,
        basePriceSource: "catalog",
        syncProvenance: { referenceCode: null, priceSource: 0 },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: { mode: "none" },
      },
    ],
  },
});

async function openQueue(): Promise<Readonly<{
  connection: SqliteConnectionPort;
  queue: SharedHeldOrderPublicationQueuePort;
}>> {
  const connection = await openTestDatabase();
  const queue = new SqliteSharedHeldOrderPublicationQueue(connection);
  return { connection, queue };
}

test("评估后旧行进入 PendingPublish，发布队列只暴露密文不存明文", async () => {
  const { connection, queue } = await openQueue();
  const ciphertext = await fakeEncryptor.encrypt(PAYLOAD_JSON);
  await insertHeldOrderRow(connection, {
    holdId: "hold-pub-1",
    payloadCiphertext: ciphertext,
  });

  const pending = await queue.listNeedsEvaluation(SCOPE, 10);
  assert.equal(pending.length, 1);
  assert.deepEqual(pending[0]?.payloadCiphertext, ciphertext);

  assert.equal(
    await queue.applyShareEvaluation({
      holdId: "hold-pub-1",
      evaluation: { outcome: "pending-publish" },
      evaluatedAtIso: TEST_NOW_ISO,
    }),
    "updated",
  );
  assert.equal((await queue.listNeedsEvaluation(SCOPE, 10)).length, 0);

  const due = await queue.listDue(SCOPE, TEST_NOW_ISO, 10);
  assert.equal(due.length, 1);
  assert.equal(due[0]?.holdId, "hold-pub-1");
  assert.deepEqual(due[0]?.payloadCiphertext, ciphertext);
  assert.equal(due[0]?.publishAttemptCount, 0);

  // 数据库只保留密文。
  const raw = await connection.getFirst<{ payload_ciphertext: Uint8Array }>(
    "SELECT payload_ciphertext FROM held_order_records WHERE hold_id = ?",
    ["hold-pub-1"],
  );
  assert.deepEqual(raw?.payload_ciphertext, ciphertext);
  assert.ok(!new TextDecoder().decode(raw?.payload_ciphertext).includes("P-1"));
});

test("UI 共享状态只列当前 store/device 的未结束挂单，并且不暴露 payload", async () => {
  const { connection, queue } = await openQueue();
  const ciphertext = await fakeEncryptor.encrypt(PAYLOAD_JSON);
  await insertHeldOrderRow(connection, {
    holdId: "hold-published",
    payloadCiphertext: ciphertext,
    localSequence: 1,
  });
  await insertHeldOrderRow(connection, {
    holdId: "hold-blocked",
    payloadCiphertext: ciphertext,
    localSequence: 2,
  });
  await insertHeldOrderRow(connection, {
    holdId: "hold-other-device",
    payloadCiphertext: ciphertext,
    localSequence: 3,
    deviceCode: "IPAD-02",
  });
  await insertHeldOrderRow(connection, {
    holdId: "hold-completed",
    payloadCiphertext: ciphertext,
    localSequence: 4,
  });
  await connection.run(
    "UPDATE held_order_records SET share_state = 'Published' WHERE hold_id = ?",
    ["hold-published"],
  );
  await connection.run(
    "UPDATE held_order_records SET share_state = 'Blocked', publish_block_reason = 'SHARED_CART_INVALID' WHERE hold_id = ?",
    ["hold-blocked"],
  );
  await connection.run(
    `UPDATE held_order_records
     SET status = 'Recalled', recalling_at_iso = ?, recall_attempt_id = ?,
         recalling_cashier_id = ?, recalling_cashier_name = ?, recalled_at_iso = ?
     WHERE hold_id = ?`,
    [
      TEST_NOW_ISO,
      "recall-completed",
      "cashier-1",
      "Cashier One",
      TEST_NOW_ISO,
      "hold-completed",
    ],
  );

  const rows = await queue.listShareStates(SCOPE, 10);

  assert.deepEqual(rows, [
    { holdId: "hold-published", shareState: "Published", blockReason: null },
    {
      holdId: "hold-blocked",
      shareState: "Blocked",
      blockReason: "SHARED_CART_INVALID",
    },
  ]);
  assert.equal("payloadCiphertext" in rows[0]!, false);
});

test("损坏/非 sale 评估阻断为 Blocked + 稳定原因且不进发布队列", async () => {
  const { connection, queue } = await openQueue();
  await insertHeldOrderRow(connection, {
    holdId: "hold-block-1",
    payloadCiphertext: await fakeEncryptor.encrypt(PAYLOAD_JSON),
  });

  assert.equal(
    await queue.applyShareEvaluation({
      holdId: "hold-block-1",
      evaluation: {
        outcome: "blocked",
        reason: "SHARED_CART_MODE_NOT_SALE",
      },
      evaluatedAtIso: TEST_NOW_ISO,
    }),
    "updated",
  );
  assert.equal((await queue.listDue(SCOPE, TEST_NOW_ISO, 10)).length, 0);
  const raw = await connection.getFirst<{
    share_state: string;
    publish_block_reason: string | null;
  }>(
    "SELECT share_state, publish_block_reason FROM held_order_records WHERE hold_id = ?",
    ["hold-block-1"],
  );
  assert.equal(raw?.share_state, "Blocked");
  assert.equal(raw?.publish_block_reason, "SHARED_CART_MODE_NOT_SALE");
});

test("评估幂等：重复评估返回 already-evaluated，未知挂单返回 not-found", async () => {
  const { connection, queue } = await openQueue();
  await insertHeldOrderRow(connection, {
    holdId: "hold-idem-1",
    payloadCiphertext: await fakeEncryptor.encrypt(PAYLOAD_JSON),
  });
  assert.equal(
    await queue.applyShareEvaluation({
      holdId: "hold-idem-1",
      evaluation: { outcome: "pending-publish" },
      evaluatedAtIso: TEST_NOW_ISO,
    }),
    "updated",
  );
  assert.equal(
    await queue.applyShareEvaluation({
      holdId: "hold-idem-1",
      evaluation: { outcome: "pending-publish" },
      evaluatedAtIso: TEST_NOW_ISO,
    }),
    "already-evaluated",
  );
  assert.equal(
    await queue.applyShareEvaluation({
      holdId: "missing-hold",
      evaluation: { outcome: "pending-publish" },
      evaluatedAtIso: TEST_NOW_ISO,
    }),
    "not-found",
  );
});

test("发布 CAS：按 attempt 计数标记 Published，重复/过期计数均失败", async () => {
  const { connection, queue } = await openQueue();
  await insertHeldOrderRow(connection, {
    holdId: "hold-cas-1",
    payloadCiphertext: await fakeEncryptor.encrypt(PAYLOAD_JSON),
  });
  await queue.applyShareEvaluation({
    holdId: "hold-cas-1",
    evaluation: { outcome: "pending-publish" },
    evaluatedAtIso: TEST_NOW_ISO,
  });

  assert.equal(
    await queue.markPublished({
      holdId: "hold-cas-1",
      remoteRevision: 11,
      remoteUpdatedAtIso: "2026-07-28T08:01:00.000Z",
      expectedAttemptCount: 0,
      publishedAtIso: "2026-07-28T08:01:00.000Z",
    }),
    true,
  );
  const raw = await connection.getFirst<{
    share_state: string;
    remote_revision: number | null;
    remote_updated_at_iso: string | null;
  }>(
    "SELECT share_state, remote_revision, remote_updated_at_iso FROM held_order_records WHERE hold_id = ?",
    ["hold-cas-1"],
  );
  assert.equal(raw?.share_state, "Published");
  assert.equal(raw?.remote_revision, 11);
  assert.equal(raw?.remote_updated_at_iso, "2026-07-28T08:01:00.000Z");
  assert.equal((await queue.listDue(SCOPE, TEST_NOW_ISO, 10)).length, 0);
  assert.equal(
    await queue.markPublished({
      holdId: "hold-cas-1",
      remoteRevision: 12,
      remoteUpdatedAtIso: "2026-07-28T08:02:00.000Z",
      expectedAttemptCount: 0,
      publishedAtIso: "2026-07-28T08:02:00.000Z",
    }),
    false,
  );
});

test("发布失败退避：attempt 递增、错误码落库、到点才重新入列且封顶", async () => {
  const { connection, queue } = await openQueue();
  await insertHeldOrderRow(connection, {
    holdId: "hold-retry-1",
    payloadCiphertext: await fakeEncryptor.encrypt(PAYLOAD_JSON),
  });
  await queue.applyShareEvaluation({
    holdId: "hold-retry-1",
    evaluation: { outcome: "pending-publish" },
    evaluatedAtIso: TEST_NOW_ISO,
  });

  const failedAt = "2026-07-28T08:00:00.000Z";
  assert.equal(
    await queue.recordPublishFailure({
      holdId: "hold-retry-1",
      errorCode: "PUBLISH_HTTP_500",
      failedAtIso: failedAt,
    }),
    true,
  );
  let raw = await connection.getFirst<{
    publish_attempt_count: number;
    publish_error_code: string | null;
    next_publish_at_iso: string | null;
  }>(
    "SELECT publish_attempt_count, publish_error_code, next_publish_at_iso FROM held_order_records WHERE hold_id = ?",
    ["hold-retry-1"],
  );
  assert.equal(raw?.publish_attempt_count, 1);
  assert.equal(raw?.publish_error_code, "PUBLISH_HTTP_500");
  assert.equal(raw?.next_publish_at_iso, "2026-07-28T08:00:30.000Z");
  assert.equal((await queue.listDue(SCOPE, failedAt, 10)).length, 0);
  assert.equal(
    (await queue.listDue(SCOPE, "2026-07-28T08:00:30.000Z", 10)).length,
    1,
  );

  await queue.recordPublishFailure({
    holdId: "hold-retry-1",
    errorCode: "PUBLISH_TIMEOUT",
    failedAtIso: "2026-07-28T08:00:31.000Z",
  });
  raw = await connection.getFirst<{
    publish_attempt_count: number;
    publish_error_code: string | null;
    next_publish_at_iso: string | null;
  }>(
    "SELECT publish_attempt_count, publish_error_code, next_publish_at_iso FROM held_order_records WHERE hold_id = ?",
    ["hold-retry-1"],
  );
  assert.equal(raw?.publish_attempt_count, 2);
  assert.equal(raw?.next_publish_at_iso, "2026-07-28T08:01:31.000Z");

  assert.equal(publishRetryDelayMs(1), 30_000);
  assert.equal(publishRetryDelayMs(2), 60_000);
  assert.equal(publishRetryDelayMs(200), 3_600_000);
});

test("发布成功后不能 block；block 写稳定原因", async () => {
  const { connection, queue } = await openQueue();
  await insertHeldOrderRow(connection, {
    holdId: "hold-blocked-1",
    payloadCiphertext: await fakeEncryptor.encrypt(PAYLOAD_JSON),
  });
  await queue.applyShareEvaluation({
    holdId: "hold-blocked-1",
    evaluation: { outcome: "pending-publish" },
    evaluatedAtIso: TEST_NOW_ISO,
  });
  assert.equal(
    await queue.blockPublication({
      holdId: "hold-blocked-1",
      reason: "SHARED_CART_INVALID",
      atIso: TEST_NOW_ISO,
    }),
    true,
  );
  const raw = await connection.getFirst<{
    share_state: string;
    publish_block_reason: string | null;
  }>(
    "SELECT share_state, publish_block_reason FROM held_order_records WHERE hold_id = ?",
    ["hold-blocked-1"],
  );
  assert.equal(raw?.share_state, "Blocked");
  assert.equal(raw?.publish_block_reason, "SHARED_CART_INVALID");
  assert.equal(
    await queue.blockPublication({
      holdId: "hold-blocked-1",
      reason: "SHARED_CART_INVALID",
      atIso: TEST_NOW_ISO,
    }),
    false,
  );
});

test("NeedsEvaluation/listDue 排除 Recalling/Recalled，付款后迟到发布不再入队", async () => {
  const { connection, queue } = await openQueue();
  const ciphertext = await fakeEncryptor.encrypt(PAYLOAD_JSON);
  await insertHeldOrderRow(connection, {
    holdId: "hold-queue-recalling",
    payloadCiphertext: ciphertext,
  });
  await insertHeldOrderRow(connection, {
    holdId: "hold-queue-recalled",
    payloadCiphertext: ciphertext,
  });
  await insertHeldOrderRow(connection, {
    holdId: "hold-queue-pending",
    payloadCiphertext: ciphertext,
  });

  await connection.run(
    `UPDATE held_order_records
     SET status = 'Recalling', recalling_at_iso = ?, recall_attempt_id = ?,
         recalling_cashier_id = 'c-1', recalling_cashier_name = 'C',
         updated_at_iso = ?
     WHERE hold_id = 'hold-queue-recalling'`,
    [TEST_NOW_ISO, "attempt-recalling", TEST_NOW_ISO],
  );
  await connection.run(
    `UPDATE held_order_records
     SET status = 'Recalled', recalling_at_iso = ?, recall_attempt_id = ?,
         recalling_cashier_id = 'c-1', recalling_cashier_name = 'C',
         recalled_at_iso = ?, updated_at_iso = ?
     WHERE hold_id = 'hold-queue-recalled'`,
    [TEST_NOW_ISO, "attempt-recalled", TEST_NOW_ISO, TEST_NOW_ISO],
  );

  const needs = await queue.listNeedsEvaluation(SCOPE, 10);
  assert.deepEqual(
    needs.map((row) => row.holdId),
    ["hold-queue-pending"],
  );

  for (const holdId of [
    "hold-queue-recalling",
    "hold-queue-recalled",
    "hold-queue-pending",
  ]) {
    assert.equal(
      await queue.applyShareEvaluation({
        holdId,
        evaluation: { outcome: "pending-publish" },
        evaluatedAtIso: TEST_NOW_ISO,
      }),
      "updated",
    );
  }
  const due = await queue.listDue(SCOPE, TEST_NOW_ISO, 10);
  assert.deepEqual(
    due.map((row) => row.holdId),
    ["hold-queue-pending"],
  );
});

test("发布队列在 SQL limit 前按门店设备过滤，外部 scope 不会饿死当前终端", async () => {
  const { connection, queue } = await openQueue();
  const ciphertext = await fakeEncryptor.encrypt(PAYLOAD_JSON);
  await insertHeldOrderRow(connection, {
    holdId: "hold-foreign-first",
    payloadCiphertext: ciphertext,
    localSequence: 1,
    storeCode: "S2",
    deviceCode: "IPAD-02",
  });
  await insertHeldOrderRow(connection, {
    holdId: "hold-local-second",
    payloadCiphertext: ciphertext,
    localSequence: 2,
  });

  const needsEvaluation = await queue.listNeedsEvaluation(SCOPE, 1);
  assert.deepEqual(
    needsEvaluation.map((row) => row.holdId),
    ["hold-local-second"],
  );

  for (const holdId of ["hold-foreign-first", "hold-local-second"]) {
    assert.equal(
      await queue.applyShareEvaluation({
        holdId,
        evaluation: { outcome: "pending-publish" },
        evaluatedAtIso: TEST_NOW_ISO,
      }),
      "updated",
    );
  }

  const due = await queue.listDue(SCOPE, TEST_NOW_ISO, 1);
  assert.deepEqual(
    due.map((row) => row.holdId),
    ["hold-local-second"],
  );
});
