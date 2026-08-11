import assert from "node:assert/strict";
import test from "node:test";

import type {
  SharedHeldOrderRemoteRow,
  SharedHeldOrdersViewPort,
  SharedHeldOrderTakeViewResult,
} from "./held-orders-domain";
import { HeldOrdersOrchestrator } from "./held-orders-orchestrator";
import { HeldOrdersPresenter } from "./held-orders-presenter";

import type { HeldOrderSummary } from "@/core/contracts";

function localSummary(overrides: Partial<HeldOrderSummary> = {}): HeldOrderSummary {
  return {
    holdId: "H1",
    localSequence: 8,
    scope: { storeCode: "BNE", deviceCode: "IPAD-1" },
    heldBy: { cashierId: "C1", cashierName: "Cashier" },
    status: "Pending",
    itemCount: 2,
    subtotalCents: 1_200,
    discountCents: 0,
    actualAmountCents: 1_200,
    heldAtIso: "2026-07-28T01:00:00.000Z",
    recallingAtIso: null,
    ...overrides,
  };
}

function remoteRow(overrides: Partial<SharedHeldOrderRemoteRow> = {}): SharedHeldOrderRemoteRow {
  return {
    holdGuid: "H1",
    deviceCode: "IPAD-2",
    cashierName: "Other Cashier",
    heldAtIso: "2026-07-28T02:00:00.000Z",
    lineCount: 3,
    actualCents: 3_300,
    ...overrides,
  };
}

function fakeOrchestrator(
  rows: readonly HeldOrderSummary[] = [],
  failure: string | null = null,
) {
  const calls = { list: 0 };
  return {
    calls,
    orchestrator: {
      list: async () => {
        calls.list += 1;
        if (failure) throw new Error(failure);
        return rows;
      },
    } as unknown as HeldOrdersOrchestrator,
  };
}

function fakeSharedPort(
  overrides: Partial<SharedHeldOrdersViewPort> = {},
): SharedHeldOrdersViewPort {
  return {
    listRemotePending: async () => [],
    takeRemoteHold: async (holdGuid: string): Promise<SharedHeldOrderTakeViewResult> => ({
      ok: true,
      outcome: "restored",
      holdGuid,
    }),
    recallLocalPublication: async (
      holdGuid: string,
    ): Promise<SharedHeldOrderTakeViewResult> => ({
      ok: true,
      outcome: "restored",
      holdGuid,
    }),
    ...overrides,
  };
}

test("共享刷新按 HoldGuid 去重：本地副本优先，远端补充，阻断保留原因", async () => {
  const { orchestrator } = fakeOrchestrator([
    localSummary({ holdId: "H1" }),
    localSummary({ holdId: "H2" }),
  ]);
  const presenter = new HeldOrdersPresenter(orchestrator);
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => [
        remoteRow({ holdGuid: "H1" }),
        remoteRow({ holdGuid: "R3" }),
      ],
      listLocalShareState: async () => [
        { holdId: "H2", shareState: "Blocked", blockReason: "SHARED_CART_MODE_NOT_SALE" },
        { holdId: "H1", shareState: "Published", blockReason: null },
      ],
    }),
  );

  await presenter.refresh();

  assert.equal(presenter.state.kind, "ready");
  assert.equal(presenter.state.sharedEnabled, true);
  assert.equal(presenter.state.refreshError, null);
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["R3", "H1", "H2"],
  );
  const h1 = presenter.state.rows.find((row) => row.holdId === "H1");
  assert.equal(h1?.status, "published-shareable");
  assert.equal(h1?.local?.localSequence, 8);
  assert.equal(h1?.remote?.deviceCode, "IPAD-2");
  const h2 = presenter.state.rows.find((row) => row.holdId === "H2");
  assert.equal(h2?.status, "blocked");
  assert.equal(h2?.blockReason, "SHARED_CART_MODE_NOT_SALE");
  const r3 = presenter.state.rows.find((row) => row.holdId === "R3");
  assert.equal(r3?.status, "remote-pending");
  assert.equal(r3?.local, null);
  assert.equal(r3?.remote?.cashierName, "Other Cashier");
});

test("共享同步失败保留本地行并显示非阻塞错误，本地账本失败保持旧 fail-closed 语义", async () => {
  const sharedFailure = new HeldOrdersPresenter(
    fakeOrchestrator([localSummary({ holdId: "H1" })]).orchestrator,
  );
  sharedFailure.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => {
        throw new Error("offline");
      },
    }),
  );
  await sharedFailure.refresh();
  assert.equal(sharedFailure.state.kind, "ready");
  assert.equal(sharedFailure.state.rows.length, 1);
  const preservedRow = sharedFailure.state.rows[0];
  assert.ok(preservedRow);
  assert.equal(preservedRow.status, "local-pending");
  assert.equal(
    sharedFailure.state.refreshError,
    "SHARED_HELD_ORDERS_SYNC_FAILED",
  );

  const localFailure = new HeldOrdersPresenter(
    fakeOrchestrator([], "HELD_ORDER_LIST_UNAUTHORIZED").orchestrator,
  );
  localFailure.attachSharedOrders(fakeSharedPort());
  await localFailure.refresh();
  assert.equal(localFailure.state.kind, "unauthorized");
  assert.deepEqual(localFailure.state.rows, []);
  assert.equal(localFailure.state.refreshError, null);

  const ledgerFailure = new HeldOrdersPresenter(
    fakeOrchestrator([], "encrypted ledger corrupted").orchestrator,
  );
  ledgerFailure.attachSharedOrders(fakeSharedPort());
  await ledgerFailure.refresh();
  assert.equal(ledgerFailure.state.kind, "failed");
  assert.deepEqual(ledgerFailure.state.rows, []);
  assert.equal(ledgerFailure.state.refreshError, null);
});

test("远端列表失败不丢本地发布状态；缺少共享状态的旧本地挂单保持 local-pending", async () => {
  const presenter = new HeldOrdersPresenter(
    fakeOrchestrator([
      localSummary({ holdId: "blocked" }),
      localSummary({ holdId: "legacy" }),
    ]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => {
        throw new Error("offline");
      },
      listLocalShareState: async () => [
        {
          holdId: "blocked",
          shareState: "Blocked",
          blockReason: "SHARED_CART_INVALID",
        },
      ],
    }),
  );

  await presenter.refresh();

  assert.equal(presenter.state.refreshError, "SHARED_HELD_ORDERS_SYNC_FAILED");
  assert.equal(
    presenter.state.rows.find((row) => row.holdId === "blocked")?.status,
    "blocked",
  );
  assert.equal(
    presenter.state.rows.find((row) => row.holdId === "legacy")?.status,
    "local-pending",
  );
});

test("本地发布状态精确映射 NeedsEvaluation/PendingPublish/Published", async () => {
  const presenter = new HeldOrdersPresenter(
    fakeOrchestrator([
      localSummary({ holdId: "needs" }),
      localSummary({ holdId: "pending" }),
      localSummary({ holdId: "published" }),
    ]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listLocalShareState: async () => [
        { holdId: "needs", shareState: "NeedsEvaluation", blockReason: null },
        { holdId: "pending", shareState: "PendingPublish", blockReason: null },
        { holdId: "published", shareState: "Published", blockReason: null },
      ],
    }),
  );

  await presenter.refresh();

  const statuses = Object.fromEntries(
    presenter.state.rows.map((row) => [row.holdId, row.status]),
  );
  assert.equal(statuses.needs, "local-pending-publish");
  assert.equal(statuses.pending, "local-pending-publish");
  assert.equal(statuses.published, "published-shareable");
});

test("在线取单委托 shared coordinator 并把 restored 映射为 recalled", async () => {
  const takeRemoteHold = async (
    holdGuid: string,
  ): Promise<SharedHeldOrderTakeViewResult> => ({
    ok: true,
    outcome: "restored",
    holdGuid,
  });
  const presenter = new HeldOrdersPresenter(fakeOrchestrator().orchestrator);
  presenter.attachSharedOrders(
    fakeSharedPort({
      takeRemoteHold,
      recallLocalPublication: async () => ({
        ok: false,
        outcome: "prepared-awaiting-activation",
        holdGuid: "R1",
      }),
    }),
  );

  const restored = await presenter.takeRemote("R1");
  assert.deepEqual(restored, { ok: true, code: "recalled", holdId: "R1" });

  const prepared = await presenter.recallLocalShared("R1");
  assert.equal(prepared.code, "shared-prepared-awaiting-activation");
  assert.equal(prepared.ok, false);
});

test("coordinator 冲突/异常映射为 shared-conflict 且不覆盖列表", async () => {
  const presenter = new HeldOrdersPresenter(
    fakeOrchestrator([localSummary()]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      takeRemoteHold: async () => {
        throw new Error("CONFLICT");
      },
    }),
  );
  await presenter.refresh();
  const result = await presenter.takeRemote("H1");
  assert.equal(result.code, "shared-conflict");
  assert.equal(presenter.state.rows.length, 1);
});

test("离线本地取回委托 recallLocalPublication，远端列表不可用也不影响取回", async () => {
  let remoteListCalls = 0;
  const presenter = new HeldOrdersPresenter(fakeOrchestrator().orchestrator);
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => {
        remoteListCalls += 1;
        throw new Error("offline");
      },
    }),
  );
  const result = await presenter.recallLocalShared("H1");
  assert.deepEqual(result, { ok: true, code: "recalled", holdId: "H1" });
  // 取单动作本身不访问远端列表；唯一一次调用来自动作成功后的可见刷新。
  assert.equal(remoteListCalls, 1);
  assert.equal(
    presenter.state.refreshError,
    "SHARED_HELD_ORDERS_SYNC_FAILED",
  );
});

test("强制释放：未接线不可用、空原因被拒、有原因才委托授权端口", async () => {
  const noPort = new HeldOrdersPresenter(fakeOrchestrator().orchestrator);
  assert.equal(noPort.supportsForceRelease(), false);
  assert.equal(
    (await noPort.forceRelease("H1", "reason")).code,
    "force-release-unavailable",
  );

  const withoutAdapter = new HeldOrdersPresenter(fakeOrchestrator().orchestrator);
  withoutAdapter.attachSharedOrders(fakeSharedPort());
  assert.equal(withoutAdapter.supportsForceRelease(), false);
  assert.equal(
    (await withoutAdapter.forceRelease("H1", "reason")).code,
    "force-release-unavailable",
  );

  const forceRelease = fakeForceRelease();
  const withAdapter = new HeldOrdersPresenter(fakeOrchestrator().orchestrator);
  withAdapter.attachSharedOrders(
    fakeSharedPort({
      forceRelease: forceRelease.fn,
    }),
  );
  assert.equal(withAdapter.supportsForceRelease(), true);
  const blank = await withAdapter.forceRelease("H1", "   ");
  assert.equal(blank.code, "force-release-reason-required");
  assert.equal(forceRelease.calls.count, 0);

  const released = await withAdapter.forceRelease("H1", "  duplicate claim  ");
  assert.deepEqual(released, { ok: true, code: "force-released", holdId: "H1" });
  assert.deepEqual(forceRelease.calls.input, {
    holdGuid: "H1",
    reason: "duplicate claim",
  });

  const throwing = new HeldOrdersPresenter(fakeOrchestrator().orchestrator);
  throwing.attachSharedOrders(
    fakeSharedPort({
      forceRelease: async () => {
        throw new Error("authorization cancelled");
      },
    }),
  );
  assert.equal(
    (await throwing.forceRelease("H1", "reason")).code,
    "force-release-failed",
  );
});

function fakeForceRelease() {
  const calls: {
    count: number;
    input: Readonly<{ holdGuid: string; reason: string }> | null;
  } = { count: 0, input: null };
  return {
    calls,
    fn: async (input: Readonly<{ holdGuid: string; reason: string }>) => {
      calls.count += 1;
      calls.input = input;
      return { ok: true as const, code: "force-released" as const, holdId: input.holdGuid };
    },
  };
}

test("可见刷新每 10 秒一次、停表后不再刷新、destroy 停表且 single-flight", async (t) => {
  t.mock.timers.enable({ apis: ["setInterval"] });
  const { orchestrator, calls } = fakeOrchestrator([localSummary()]);
  const presenter = new HeldOrdersPresenter(orchestrator);
  presenter.attachSharedOrders(fakeSharedPort());

  await presenter.refresh();
  assert.equal(calls.list, 1);

  presenter.startAutoRefresh(10_000);
  t.mock.timers.tick(10_000);
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(calls.list, 2);

  presenter.stopAutoRefresh();
  t.mock.timers.tick(30_000);
  await Promise.resolve();
  assert.equal(calls.list, 2);

  presenter.startAutoRefresh(10_000);
  presenter.destroy();
  t.mock.timers.tick(10_000);
  await Promise.resolve();
  assert.equal(calls.list, 2);

  presenter.startAutoRefresh(10_000);
  t.mock.timers.tick(10_000);
  await Promise.resolve();
  assert.equal(calls.list, 2);
  t.mock.timers.reset();
});

test("refresh single-flight：并发调用共享同一 in-flight", async () => {
  const { orchestrator, calls } = fakeOrchestrator([localSummary()]);
  const presenter = new HeldOrdersPresenter(orchestrator);
  presenter.attachSharedOrders(fakeSharedPort());

  const first = presenter.refresh();
  const second = presenter.refresh();
  assert.equal(first, second);
  await first;
  assert.equal(calls.list, 1);
  await presenter.refresh();
  assert.equal(calls.list, 2);
});
