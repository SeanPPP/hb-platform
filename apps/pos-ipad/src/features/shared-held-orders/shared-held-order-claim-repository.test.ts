import assert from "node:assert/strict";
import test from "node:test";


import {
  SqliteSharedHeldOrderClaimRepository,
  type PrepareClaimResult,
  type SharedHeldOrderClaimRepositoryPort,
} from "./shared-held-order-claim-repository";
import {
  TEST_NOW_ISO,
  fakeEncryptor,
  insertHeldOrderRow,
  openTestDatabase,
} from "./shared-held-order-test-support";
import {
  normalizeSharedSaleCartV1,
  type SharedSaleCartV1,
} from "./shared-sale-cart-v1";

import type { SqliteConnectionPort } from "@/core/db/types";

const SCOPE = { storeCode: "S1", deviceCode: "IPAD-01" } as const;

function cart(): SharedSaleCartV1 {
  return normalizeSharedSaleCartV1({
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
}

function decimalCart(): SharedSaleCartV1 {
  const baseLine = cart().pricingState.lines[0]!;
  return normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      ...cart().pricingState,
      lines: [
        {
          ...baseLine,
          lineId: "line-quarter",
          quantity: 0.25,
          unitPriceCents: 1002,
        },
        {
          ...baseLine,
          lineId: "line-one-half",
          quantity: 1.5,
          unitPriceCents: 501,
        },
      ],
    },
  });
}

async function openClaims(
  options: Readonly<{
    source: "RemoteClaim" | "OfflineOrigin";
    localHoldId?: string | null;
  }> = { source: "OfflineOrigin", localHoldId: "hold-claim-1" },
): Promise<Readonly<{
  connection: SqliteConnectionPort;
  claims: SharedHeldOrderClaimRepositoryPort;
}>> {
  const connection = await openTestDatabase();
  const localHoldId =
    options.localHoldId === undefined ? "hold-claim-1" : options.localHoldId;
  if (localHoldId !== null) {
    await insertHeldOrderRow(connection, {
      holdId: localHoldId,
      payloadCiphertext: await fakeEncryptor.encrypt("{}"),
    });
  }
  const claims = new SqliteSharedHeldOrderClaimRepository(
    connection,
    fakeEncryptor,
  );
  return { connection, claims };
}

function prepareInput(overrides: Partial<{
  claimGuid: string;
  holdGuid: string;
  recallAttemptId: string;
  heldBy: { cashierId: string; cashierName: string };
  heldAtIso: string;
  prepareIdempotencyKey: string;
  preparedExpiresAtIso: string;
  source: "RemoteClaim" | "OfflineOrigin";
  payload: SharedSaleCartV1;
}>) {
  return {
    claimGuid: "claim-1",
    holdGuid: "hold-claim-1",
    recallAttemptId: "recall-attempt-1",
    heldBy: { cashierId: "cashier-2", cashierName: "Cashier Two" },
    heldAtIso: TEST_NOW_ISO,
    scope: SCOPE,
    source: "OfflineOrigin" as const,
    prepareIdempotencyKey: "prepare-key-1",
    payload: cart(),
    preparedExpiresAtIso: "2026-07-28T08:10:00.000Z",
    createdAtIso: TEST_NOW_ISO,
    ...overrides,
  };
}

function assertPrepared(result: PrepareClaimResult, claimGuid: string): void {
  assert.equal(result.outcome, "prepared");
  if (result.outcome !== "prepared") return;
  assert.equal(result.claim.claimGuid, claimGuid);
  assert.equal(result.claim.state, "Prepared");
}

test("prepare 原子保存本地 fence，数据库只留密文且可精确解密还原", async () => {
  const { connection, claims } = await openClaims();
  const result = await claims.prepareClaim(prepareInput({}));
  assertPrepared(result, "claim-1");

  const raw = await connection.getFirst<{ payload_ciphertext: Uint8Array }>(
    "SELECT payload_ciphertext FROM shared_held_order_claim_records WHERE claim_guid = ?",
    ["claim-1"],
  );
  assert.ok(raw);
  assert.ok(!new TextDecoder().decode(raw.payload_ciphertext).includes("P-1"));
  assert.deepEqual(
    raw.payload_ciphertext,
    await fakeEncryptor.encrypt(JSON.stringify(cart())),
  );

  const loaded = await claims.getClaim("claim-1");
  assert.deepEqual(loaded?.payload, cart());
  assert.equal(loaded?.state, "Prepared");
});

test("并发本地 fence 单赢家：同 scope 只有一个 Prepared/Active", async () => {
  const { claims } = await openClaims();
  const [first, second] = await Promise.all([
    claims.prepareClaim(
      prepareInput({ claimGuid: "claim-a", prepareIdempotencyKey: "key-a" }),
    ),
    claims.prepareClaim(
      prepareInput({ claimGuid: "claim-b", prepareIdempotencyKey: "key-b" }),
    ),
  ]);
  const outcomes = [first.outcome, second.outcome].sort();
  assert.deepEqual(outcomes, ["fence-held", "prepared"]);
  const winner = first.outcome === "prepared" ? first : second;
  const loser = first.outcome === "fence-held" ? first : second;
  if (winner.outcome !== "prepared" || loser.outcome !== "fence-held") return;
  assert.equal(loser.winner.claimGuid, winner.claim.claimGuid);
  const mine = await claims.listMine(SCOPE, 10);
  assert.equal(mine.length, 1);
  assert.equal(mine[0]?.claimGuid, winner.claim.claimGuid);
});

test("prepare 幂等与崩溃恢复：同 key 重放不重复建行，异 key 仍受 fence 保护", async () => {
  const { claims } = await openClaims();
  assertPrepared(await claims.prepareClaim(prepareInput({})), "claim-1");

  // 模拟崩溃后重试：同 prepare key 幂等重放，仍是唯一 Prepared。
  const replayed = await claims.prepareClaim(prepareInput({}));
  assert.equal(replayed.outcome, "replayed");
  if (replayed.outcome !== "replayed") return;
  assert.equal(replayed.claim.claimGuid, "claim-1");
  assert.equal(replayed.claim.state, "Prepared");
  assert.equal((await claims.listMine(SCOPE, 10)).length, 1);

  // 异 key 尝试被 fence 挡住，Prepared 不被自动释放/覆盖。
  const other = await claims.prepareClaim(
    prepareInput({ claimGuid: "claim-2", prepareIdempotencyKey: "key-2" }),
  );
  assert.equal(other.outcome, "fence-held");

  // 崩溃前未 activate，恢复后可继续 activate。
  assert.equal(
    await claims.activatePreparedClaim({
      claimGuid: "claim-1",
      prepareIdempotencyKey: "prepare-key-1",
      activateIdempotencyKey: "activate-key-1",
      serverRevision: 5,
      activatedAtIso: TEST_NOW_ISO,
    }),
    true,
  );
  const active = await claims.getClaim("claim-1");
  assert.equal(active?.state, "Active");
  assert.equal(active?.serverRevision, 5);
});

test("Active 不自动释放：过期 prepared 声明在恢复后仍保持 Active", async () => {
  const { claims } = await openClaims();
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({ preparedExpiresAtIso: "2020-01-01T00:00:00.000Z" }),
    ),
    "claim-1",
  );
  await claims.activatePreparedClaim({
    claimGuid: "claim-1",
    prepareIdempotencyKey: "prepare-key-1",
    activateIdempotencyKey: "activate-key-1",
    serverRevision: 1,
    activatedAtIso: TEST_NOW_ISO,
  });

  // 恢复入口只列声明，不做任何自动释放。
  const mine = await claims.listMine(SCOPE, 10);
  assert.equal(mine.length, 1);
  assert.equal(mine[0]?.state, "Active");
  assert.equal(mine[0]?.boundOrderGuid, null);
});

test("bind order 持久化：Active 绑定订单后可 complete，绑定不可被释放覆盖", async () => {
  const { connection, claims } = await openClaims();
  assertPrepared(await claims.prepareClaim(prepareInput({})), "claim-1");
  await claims.activatePreparedClaim({
    claimGuid: "claim-1",
    prepareIdempotencyKey: "prepare-key-1",
    activateIdempotencyKey: "activate-key-1",
    serverRevision: 2,
    activatedAtIso: TEST_NOW_ISO,
  });

  assert.equal(
    await claims.bindOrderToActiveClaim({
      claimGuid: "claim-1",
      activateIdempotencyKey: "activate-key-1",
      boundOrderGuid: "order-bound-1",
      boundAtIso: TEST_NOW_ISO,
    }),
    true,
  );
  let loaded = await claims.getClaim("claim-1");
  assert.equal(loaded?.boundOrderGuid, "order-bound-1");
  assert.equal(loaded?.state, "Active");

  // 已绑定订单的 Active 不允许 release，只能 complete。
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-1",
      releaseIdempotencyKey: "release-key-x",
      releasedAtIso: TEST_NOW_ISO,
      expectedState: "Active",
    }),
    false,
  );
  assert.equal(
    await claims.completeActiveClaim({
      claimGuid: "claim-1",
      activateIdempotencyKey: "activate-key-1",
      releaseIdempotencyKey: "release-key-1",
      completedAtIso: TEST_NOW_ISO,
    }),
    true,
  );
  loaded = await claims.getClaim("claim-1");
  assert.equal(loaded?.state, "Completed");
  assert.equal(loaded?.boundOrderGuid, "order-bound-1");
  assert.equal(loaded?.releaseIdempotencyKey, "release-key-1");

  // complete 原子清理本地 fence 并把 held 置为 Recalled。
  const fence = await connection.getFirst<{ kind: string }>(
    "SELECT kind FROM terminal_cart_fences WHERE store_code = ? AND device_code = ?",
    [SCOPE.storeCode, SCOPE.deviceCode],
  );
  assert.equal(fence, null);
  const held = await connection.getFirst<{ status: string }>(
    "SELECT status FROM held_order_records WHERE hold_id = ?",
    ["hold-claim-1"],
  );
  assert.equal(held?.status, "Recalled");

  // 完成态持久化在数据库中。
  const raw = await connection.getFirst<{
    state: string;
    bound_order_guid: string | null;
  }>(
    "SELECT state, bound_order_guid FROM shared_held_order_claim_records WHERE claim_guid = ?",
    ["claim-1"],
  );
  assert.equal(raw?.state, "Completed");
  assert.equal(raw?.bound_order_guid, "order-bound-1");

  // 完成态幂等：同 release key 重放返回 true，异 key 拒绝。
  assert.equal(
    await claims.completeActiveClaim({
      claimGuid: "claim-1",
      activateIdempotencyKey: "activate-key-1",
      releaseIdempotencyKey: "release-key-1",
      completedAtIso: TEST_NOW_ISO,
    }),
    true,
  );
  assert.equal(
    await claims.completeActiveClaim({
      claimGuid: "claim-1",
      activateIdempotencyKey: "activate-key-1",
      releaseIdempotencyKey: "release-key-different",
      completedAtIso: TEST_NOW_ISO,
    }),
    false,
  );
});

test("release 释放 Prepared 释放 fence，异 key/终态均拒绝", async () => {
  const { claims } = await openClaims();
  assertPrepared(await claims.prepareClaim(prepareInput({})), "claim-1");
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-1",
      releaseIdempotencyKey: "release-key-1",
      releasedAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    true,
  );
  let loaded = await claims.getClaim("claim-1");
  assert.equal(loaded?.state, "Released");

  // fence 已释放：新声明可进入。
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        claimGuid: "claim-2",
        prepareIdempotencyKey: "key-2",
        recallAttemptId: "recall-attempt-2",
      }),
    ),
    "claim-2",
  );
  // 已 Released 终态不能再次 release / activate。
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-1",
      releaseIdempotencyKey: "release-key-2",
      releasedAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    false,
  );
  loaded = await claims.getClaim("claim-2");
  assert.equal(loaded?.state, "Prepared");
  assert.equal(
    await claims.activatePreparedClaim({
      claimGuid: "claim-2",
      prepareIdempotencyKey: "key-2",
      activateIdempotencyKey: "activate-key-2",
      serverRevision: 3,
      activatedAtIso: TEST_NOW_ISO,
    }),
    true,
  );
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-2",
      releaseIdempotencyKey: "release-key-3",
      releasedAtIso: TEST_NOW_ISO,
      expectedState: "Active",
    }),
    true,
  );
});

test("RemoteClaim prepare 原子写入 claim 密文、RecallActive fence 与 synthetic Recalling 行", async () => {
  const { connection, claims } = await openClaims({
    source: "RemoteClaim",
    localHoldId: null,
  });
  const result = await claims.prepareClaim(
    prepareInput({
      source: "RemoteClaim",
      recallAttemptId: "remote-attempt-1",
    }),
  );
  assertPrepared(result, "claim-1");

  // synthetic held 行满足 fence 的 FK/trigger 前提：Recalling + 同一 recall_attempt_id。
  const held = await connection.getFirst<{
    hold_id: string;
    status: string;
    recall_attempt_id: string | null;
    is_synthetic_shared_claim: number;
    payload_ciphertext: Uint8Array;
    item_count: number;
    subtotal_cents: number;
    discount_cents: number;
    actual_amount_cents: number;
  }>(
    `SELECT hold_id, status, recall_attempt_id, is_synthetic_shared_claim,
      payload_ciphertext, item_count, subtotal_cents, discount_cents,
      actual_amount_cents
     FROM held_order_records WHERE hold_id = ?`,
    ["hold-claim-1"],
  );
  assert.ok(held);
  assert.equal(held.status, "Recalling");
  assert.equal(held.recall_attempt_id, "remote-attempt-1");
  assert.equal(held.is_synthetic_shared_claim, 1);
  assert.equal(held.item_count, 1);
  assert.equal(held.subtotal_cents, 1002);
  assert.equal(held.discount_cents, 0);
  assert.equal(held.actual_amount_cents, 1002);
  // 只存密文，绝不落明文。
  assert.ok(!new TextDecoder().decode(held.payload_ciphertext).includes("P-1"));
  assert.deepEqual(
    held.payload_ciphertext,
    await fakeEncryptor.encrypt(JSON.stringify(cart())),
  );

  const fence = await connection.getFirst<{
    kind: string;
    hold_id: string;
    recall_attempt_id: string | null;
  }>(
    "SELECT kind, hold_id, recall_attempt_id FROM terminal_cart_fences WHERE store_code = ? AND device_code = ?",
    [SCOPE.storeCode, SCOPE.deviceCode],
  );
  assert.ok(fence);
  assert.equal(fence.kind, "RecallActive");
  assert.equal(fence.hold_id, "hold-claim-1");
  assert.equal(fence.recall_attempt_id, "remote-attempt-1");
});

test("RemoteClaim synthetic 汇总按 decimal quantity 逐行 AwayFromZero，item_count 使用行数", async () => {
  const { connection, claims } = await openClaims({
    source: "RemoteClaim",
    localHoldId: null,
  });
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        source: "RemoteClaim",
        recallAttemptId: "remote-decimal-attempt",
        payload: decimalCart(),
      }),
    ),
    "claim-1",
  );

  const held = await connection.getFirst<{
    item_count: number;
    subtotal_cents: number;
    discount_cents: number;
    actual_amount_cents: number;
  }>(
    `SELECT item_count, subtotal_cents, discount_cents, actual_amount_cents
     FROM held_order_records WHERE hold_id = ?`,
    ["hold-claim-1"],
  );
  assert.ok(held);
  assert.deepEqual({ ...held }, {
    item_count: 2,
    subtotal_cents: 1003,
    discount_cents: 0,
    actual_amount_cents: 1003,
  });
});

test("RemoteClaim synthetic 汇总修复 JS double：0.29 × 50 汇总为 15 分", async () => {
  const { connection, claims } = await openClaims({
    source: "RemoteClaim",
    localHoldId: null,
  });
  const baseLine = cart().pricingState.lines[0]!;
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        source: "RemoteClaim",
        recallAttemptId: "remote-decimal-29-attempt",
        payload: normalizeSharedSaleCartV1({
          version: 1,
          pricingState: {
            ...cart().pricingState,
            lines: [
              {
                ...baseLine,
                lineId: "line-29",
                quantity: 0.29,
                unitPriceCents: 50,
              },
            ],
          },
        }),
      }),
    ),
    "claim-1",
  );

  const held = await connection.getFirst<{
    item_count: number;
    subtotal_cents: number;
    discount_cents: number;
    actual_amount_cents: number;
  }>(
    `SELECT item_count, subtotal_cents, discount_cents, actual_amount_cents
     FROM held_order_records WHERE hold_id = ?`,
    ["hold-claim-1"],
  );
  assert.ok(held);
  assert.deepEqual({ ...held }, {
    item_count: 1,
    subtotal_cents: 15,
    discount_cents: 0,
    actual_amount_cents: 15,
  });
});

test("prepare 同 key 但 facts 不同（hold/payload/attempt）拒绝，不静默重放", async () => {
  const { claims } = await openClaims({ source: "OfflineOrigin" });
  assertPrepared(await claims.prepareClaim(prepareInput({})), "claim-1");

  await assert.rejects(
    claims.prepareClaim(prepareInput({ holdGuid: "hold-claim-different" })),
    /SHARED_HELD_ORDER_CLAIM_PREPARE_FACTS_MISMATCH/,
  );
  await assert.rejects(
    claims.prepareClaim(
      prepareInput({
        payload: normalizeSharedSaleCartV1({
          version: 1,
          pricingState: {
            ...cart().pricingState,
            lines: [
              {
                ...cart().pricingState.lines[0]!,
                productCode: "P-DIFFERENT",
              },
            ],
          },
        }),
      }),
    ),
    /SHARED_HELD_ORDER_CLAIM_PREPARE_FACTS_MISMATCH/,
  );
  await assert.rejects(
    claims.prepareClaim(prepareInput({ recallAttemptId: "different-attempt" })),
    /SHARED_HELD_ORDER_CLAIM_PREPARE_FACTS_MISMATCH/,
  );

  // 原声明仍可正常使用，未被覆盖。
  const loaded = await claims.getClaim("claim-1");
  assert.equal(loaded?.state, "Prepared");
});

test("RemoteClaim 不能与本地已有挂单同 hold_id 并存；OfflineOrigin 必须有本地行", async () => {
  const remote = await openClaims({ source: "RemoteClaim", localHoldId: null });
  await insertHeldOrderRow(remote.connection, {
    holdId: "hold-local-existing",
    payloadCiphertext: await fakeEncryptor.encrypt("{}"),
  });
  await assert.rejects(
    remote.claims.prepareClaim(
      prepareInput({
        source: "RemoteClaim",
        holdGuid: "hold-local-existing",
        recallAttemptId: "remote-attempt-x",
      }),
    ),
    /SHARED_HELD_ORDER_CLAIM_LOCAL_HOLD_CONFLICT/,
  );

  const offline = await openClaims({ source: "OfflineOrigin", localHoldId: null });
  await assert.rejects(
    offline.claims.prepareClaim(
      prepareInput({
        source: "OfflineOrigin",
        holdGuid: "hold-missing-offline",
        recallAttemptId: "offline-attempt-x",
      }),
    ),
    /SHARED_HELD_ORDER_CLAIM_LOCAL_HOLD_MISSING/,
  );
});

test("release RemoteClaim 原子清理 synthetic held 与 fence，新声明可进入", async () => {
  const { connection, claims } = await openClaims({
    source: "RemoteClaim",
    localHoldId: null,
  });
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        source: "RemoteClaim",
        recallAttemptId: "remote-attempt-rel",
      }),
    ),
    "claim-1",
  );
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-1",
      releaseIdempotencyKey: "release-remote-1",
      releasedAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    true,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT hold_id FROM held_order_records WHERE hold_id = ?",
      ["hold-claim-1"],
    ),
    null,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT hold_id FROM terminal_cart_fences WHERE store_code = ? AND device_code = ?",
      [SCOPE.storeCode, SCOPE.deviceCode],
    ),
    null,
  );
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        claimGuid: "claim-remote-2",
        prepareIdempotencyKey: "key-remote-2",
        source: "RemoteClaim",
        recallAttemptId: "remote-attempt-2",
      }),
    ),
    "claim-remote-2",
  );
});

test("OfflineOrigin release 把真实本地 held 恢复 Pending，不删除本地副本", async () => {
  const { connection, claims } = await openClaims({ source: "OfflineOrigin" });
  assertPrepared(await claims.prepareClaim(prepareInput({})), "claim-1");
  assert.equal(
    await claims.releaseClaim({
      claimGuid: "claim-1",
      releaseIdempotencyKey: "release-offline-1",
      releasedAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    true,
  );
  const held = await connection.getFirst<{
    status: string;
    recall_attempt_id: string | null;
  }>(
    "SELECT status, recall_attempt_id FROM held_order_records WHERE hold_id = ?",
    ["hold-claim-1"],
  );
  assert.ok(held);
  assert.equal(held.status, "Pending");
  assert.equal(held.recall_attempt_id, null);
  assert.equal(
    await connection.getFirst(
      "SELECT hold_id FROM terminal_cart_fences WHERE store_code = ? AND device_code = ?",
      [SCOPE.storeCode, SCOPE.deviceCode],
    ),
    null,
  );
});

test("supersede 幂等清理 Prepared：synthetic held/fence 清除且已绑定订单不可 supersede", async () => {
  const { connection, claims } = await openClaims({
    source: "RemoteClaim",
    localHoldId: null,
  });
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        source: "RemoteClaim",
        recallAttemptId: "remote-attempt-sup",
      }),
    ),
    "claim-1",
  );
  assert.equal(
    await claims.supersedeClaim({
      claimGuid: "claim-1",
      supersedeIdempotencyKey: "supersede-1",
      supersededAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    true,
  );
  let loaded = await claims.getClaim("claim-1");
  assert.equal(loaded?.state, "Superseded");
  assert.equal(
    await connection.getFirst(
      "SELECT hold_id FROM held_order_records WHERE hold_id = ?",
      ["hold-claim-1"],
    ),
    null,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT hold_id FROM terminal_cart_fences WHERE store_code = ? AND device_code = ?",
      [SCOPE.storeCode, SCOPE.deviceCode],
    ),
    null,
  );
  assert.equal(
    await claims.supersedeClaim({
      claimGuid: "claim-1",
      supersedeIdempotencyKey: "supersede-1",
      supersededAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    true,
  );
  assert.equal(
    await claims.supersedeClaim({
      claimGuid: "claim-1",
      supersedeIdempotencyKey: "supersede-2",
      supersededAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    false,
  );

  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        claimGuid: "claim-bound-sup",
        prepareIdempotencyKey: "key-bound-sup",
        source: "RemoteClaim",
        recallAttemptId: "remote-attempt-bound",
      }),
    ),
    "claim-bound-sup",
  );
  await claims.activatePreparedClaim({
    claimGuid: "claim-bound-sup",
    prepareIdempotencyKey: "key-bound-sup",
    activateIdempotencyKey: "activate-bound-sup",
    serverRevision: 1,
    activatedAtIso: TEST_NOW_ISO,
  });
  await claims.bindOrderToActiveClaim({
    claimGuid: "claim-bound-sup",
    activateIdempotencyKey: "activate-bound-sup",
    boundOrderGuid: "order-bound-sup",
    boundAtIso: TEST_NOW_ISO,
  });
  assert.equal(
    await claims.supersedeClaim({
      claimGuid: "claim-bound-sup",
      supersedeIdempotencyKey: "supersede-bound",
      supersededAtIso: TEST_NOW_ISO,
      expectedState: "Prepared",
    }),
    false,
  );
});

test("supersede 可清理未绑定 Active，已绑定 Active 仍 fail-closed", async () => {
  const { connection, claims } = await openClaims({
    source: "RemoteClaim",
    localHoldId: null,
  });
  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        source: "RemoteClaim",
        recallAttemptId: "remote-active-supersede",
      }),
    ),
    "claim-1",
  );
  await claims.activatePreparedClaim({
    claimGuid: "claim-1",
    prepareIdempotencyKey: "prepare-key-1",
    activateIdempotencyKey: "activate-active-supersede",
    serverRevision: 9,
    activatedAtIso: TEST_NOW_ISO,
  });

  assert.equal(
    await claims.supersedeClaim({
      claimGuid: "claim-1",
      supersedeIdempotencyKey: "supersede-active-1",
      supersededAtIso: TEST_NOW_ISO,
      expectedState: "Active",
    }),
    true,
  );
  const superseded = await claims.getClaim("claim-1");
  assert.equal(superseded?.state, "Superseded");
  assert.equal(
    superseded?.activateIdempotencyKey,
    "activate-active-supersede",
  );
  assert.equal(
    await claims.supersedeClaim({
      claimGuid: "claim-1",
      supersedeIdempotencyKey: "supersede-active-1",
      supersededAtIso: TEST_NOW_ISO,
      expectedState: "Active",
    }),
    true,
  );
  assert.equal(
    await connection.getFirst(
      "SELECT hold_id FROM terminal_cart_fences WHERE store_code = ? AND device_code = ?",
      [SCOPE.storeCode, SCOPE.deviceCode],
    ),
    null,
  );

  assertPrepared(
    await claims.prepareClaim(
      prepareInput({
        claimGuid: "claim-bound-active",
        prepareIdempotencyKey: "prepare-bound-active",
        source: "RemoteClaim",
        recallAttemptId: "remote-bound-active",
      }),
    ),
    "claim-bound-active",
  );
  await claims.activatePreparedClaim({
    claimGuid: "claim-bound-active",
    prepareIdempotencyKey: "prepare-bound-active",
    activateIdempotencyKey: "activate-bound-active",
    serverRevision: 10,
    activatedAtIso: TEST_NOW_ISO,
  });
  await claims.bindOrderToActiveClaim({
    claimGuid: "claim-bound-active",
    activateIdempotencyKey: "activate-bound-active",
    boundOrderGuid: "order-bound-active",
    boundAtIso: TEST_NOW_ISO,
  });
  assert.equal(
    await claims.supersedeClaim({
      claimGuid: "claim-bound-active",
      supersedeIdempotencyKey: "supersede-bound-active",
      supersededAtIso: TEST_NOW_ISO,
      expectedState: "Active",
    }),
    false,
  );
});

test("activate 持久化超过 int.MaxValue 的 server revision，同 key 重放幂等", async () => {
  const { claims } = await openClaims({ source: "OfflineOrigin" });
  assertPrepared(await claims.prepareClaim(prepareInput({})), "claim-1");
  const bigRevision = 2_147_483_653;
  assert.equal(
    await claims.activatePreparedClaim({
      claimGuid: "claim-1",
      prepareIdempotencyKey: "prepare-key-1",
      activateIdempotencyKey: "activate-big-1",
      serverRevision: bigRevision,
      activatedAtIso: TEST_NOW_ISO,
    }),
    true,
  );
  const loaded = await claims.getClaim("claim-1");
  assert.equal(loaded?.serverRevision, bigRevision);
  assert.equal(
    await claims.activatePreparedClaim({
      claimGuid: "claim-1",
      prepareIdempotencyKey: "prepare-key-1",
      activateIdempotencyKey: "activate-big-1",
      serverRevision: bigRevision,
      activatedAtIso: TEST_NOW_ISO,
    }),
    true,
  );
});
