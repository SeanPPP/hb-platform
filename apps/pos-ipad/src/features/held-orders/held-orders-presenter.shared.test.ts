import assert from "node:assert/strict";
import test from "node:test";

import type {
  SharedHeldOrderRemoteRow,
  SharedHeldOrdersViewPort,
  SharedHeldOrderTakeViewResult,
} from "./held-orders-domain";
import { HeldOrdersOrchestrator } from "./held-orders-orchestrator";
import {
  HeldOrdersPresenter,
  type HeldOrdersPresenterOptions,
} from "./held-orders-presenter";

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

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((accept) => {
    resolve = accept;
  });
  return { promise, resolve };
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

function createHeldOrdersPresenter(
  orchestrator: HeldOrdersOrchestrator,
  options: HeldOrdersPresenterOptions = {},
): HeldOrdersPresenter {
  return new HeldOrdersPresenter(orchestrator, {
    businessTimeZone: "Australia/Brisbane",
    now: () => new Date("2026-07-28T04:00:00.000Z"),
    ...options,
  });
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
  const presenter = createHeldOrdersPresenter(orchestrator);
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

test("Presenter 默认本机分栏且本地 SQLite 完成即 ready，远端延迟在后台合并", async () => {
  const remote = deferred<readonly SharedHeldOrderRemoteRow[]>();
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([
      localSummary({ holdId: "local-real" }),
      localSummary({
        holdId: "local-other-device",
        scope: { storeCode: "BNE", deviceCode: "IPAD-2" },
      }),
      localSummary({ holdId: "synthetic-claim", isSyntheticSharedClaim: true }),
    ]).orchestrator,
    { currentDeviceCode: "IPAD-1" },
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: () => remote.promise,
    }),
  );

  const refresh = presenter.refresh();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(presenter.state.kind, "ready");
  assert.equal(presenter.state.sourceTab, "local");
  assert.equal(presenter.state.remoteRefreshing, true);
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["local-real"],
  );

  remote.resolve([
    remoteRow({ holdGuid: "remote-current", deviceCode: "IPAD-1" }),
    remoteRow({ holdGuid: "remote-other", deviceCode: "IPAD-2" }),
  ]);
  await refresh;
  await Promise.resolve();
  assert.equal(presenter.state.remoteRefreshing, false);
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["remote-current", "local-real"],
  );
  presenter.setSourceTab("other");
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["remote-other", "local-other-device", "synthetic-claim"],
  );
});

test("远端慢请求跨多轮本地刷新保持 single-flight，完成后由最新代次收敛", async () => {
  const remote = deferred<readonly SharedHeldOrderRemoteRow[]>();
  let remoteCalls = 0;
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([localSummary({ holdId: "local-real" })]).orchestrator,
    { currentDeviceCode: "IPAD-1" },
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: () => {
        remoteCalls += 1;
        return remote.promise;
      },
    }),
  );

  await presenter.refresh();
  await presenter.refresh();
  assert.equal(remoteCalls, 1);
  assert.equal(presenter.state.remoteRefreshing, true);

  remote.resolve([remoteRow({ holdGuid: "remote-current", deviceCode: "IPAD-1" })]);
  await new Promise<void>((resolve) => setImmediate(resolve));
  assert.equal(presenter.state.remoteRefreshing, false);
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["remote-current", "local-real"],
  );
});

test("Presenter 共享请求委托端口，并保持远端失败时本地缓存", async () => {
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([localSummary({ holdId: "local-real" })]).orchestrator,
  );
  const requestShare = async () => "requested" as const;
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => {
        throw new Error("offline");
      },
      requestShare,
    }),
  );

  await presenter.refresh();
  assert.equal(presenter.state.kind, "ready");
  assert.deepEqual(await presenter.requestShare("local-real"), {
    ok: true,
    outcome: "requested",
    holdId: "local-real",
  });
  assert.deepEqual(presenter.state.rows.map((row) => row.holdId), ["local-real"]);
  assert.equal(presenter.state.refreshError, "SHARED_HELD_ORDERS_SYNC_FAILED");
});

test("共享 busy 按行独立维护，并发完成一行不会清掉另一行", async () => {
  const first = deferred<"requested">();
  const second = deferred<"requested">();
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([
      localSummary({ holdId: "share-a" }),
      localSummary({ holdId: "share-b" }),
    ]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      requestShare: (holdId) => holdId === "share-a" ? first.promise : second.promise,
    }),
  );

  const firstAction = presenter.requestShare("share-a");
  const secondAction = presenter.requestShare("share-b");
  assert.equal(presenter.requestShare("share-a"), firstAction);
  assert.deepEqual([...presenter.state.shareBusyHoldIds].sort(), ["share-a", "share-b"]);

  first.resolve("requested");
  await firstAction;
  assert.deepEqual(presenter.state.shareBusyHoldIds, ["share-b"]);

  second.resolve("requested");
  await secondAction;
  assert.deepEqual(presenter.state.shareBusyHoldIds, []);
});

test("默认只显示门店当天挂单，切换全部后恢复历史挂单", async () => {
  const { orchestrator } = fakeOrchestrator([
    localSummary({
      holdId: "today-local",
      heldAtIso: "2026-08-10T14:00:00.000Z",
    }),
    localSummary({
      holdId: "yesterday-local",
      heldAtIso: "2026-08-10T13:59:59.999Z",
    }),
  ]);
  const presenter = createHeldOrdersPresenter(orchestrator, {
    businessTimeZone: "Australia/Brisbane",
    now: () => new Date("2026-08-11T02:00:00.000Z"),
  });
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => [
        remoteRow({
          holdGuid: "today-remote",
          heldAtIso: "2026-08-11T13:59:59.999Z",
        }),
        remoteRow({
          holdGuid: "tomorrow-remote",
          heldAtIso: "2026-08-11T14:00:00.000Z",
        }),
      ],
    }),
  );

  await presenter.refresh();

  assert.equal(presenter.state.dateFilter, "today");
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["today-remote", "today-local"],
  );

  presenter.setDateFilter("all");

  assert.equal(presenter.state.dateFilter, "all");
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["tomorrow-remote", "today-remote", "today-local", "yesterday-local"],
  );
});

test("共享同步失败保留本地行并显示非阻塞错误，本地账本失败保持旧 fail-closed 语义", async () => {
  const sharedFailure = createHeldOrdersPresenter(
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

  const localFailure = createHeldOrdersPresenter(
    fakeOrchestrator([], "HELD_ORDER_LIST_UNAUTHORIZED").orchestrator,
  );
  localFailure.attachSharedOrders(fakeSharedPort());
  await localFailure.refresh();
  assert.equal(localFailure.state.kind, "unauthorized");
  assert.deepEqual(localFailure.state.rows, []);
  assert.equal(localFailure.state.refreshError, null);

  const ledgerFailure = createHeldOrdersPresenter(
    fakeOrchestrator([], "encrypted ledger corrupted").orchestrator,
  );
  ledgerFailure.attachSharedOrders(fakeSharedPort());
  await ledgerFailure.refresh();
  assert.equal(ledgerFailure.state.kind, "failed");
  assert.deepEqual(ledgerFailure.state.rows, []);
  assert.equal(ledgerFailure.state.refreshError, null);
});

test("共享端口同步抛错不会让刷新 Promise 拒绝或永久停在 loading", async () => {
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([localSummary({ holdId: "local-safe" })]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listLocalShareState: () => {
        throw new Error("CURRENT_CASHIER_REQUIRED");
      },
    }),
  );

  await assert.doesNotReject(presenter.refresh());

  assert.equal(presenter.state.kind, "ready");
  assert.deepEqual(
    presenter.state.rows.map((row) => row.holdId),
    ["local-safe"],
  );
  assert.equal(
    presenter.state.refreshError,
    "SHARED_HELD_ORDERS_SYNC_FAILED",
  );
});

test("远端列表失败不丢本地发布状态；缺少共享状态的旧本地挂单保持 local-pending", async () => {
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([
      localSummary({ holdId: "blocked" }),
      localSummary({ holdId: "published" }),
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
        { holdId: "published", shareState: "Published", blockReason: null },
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
    presenter.state.rows.find((row) => row.holdId === "published")?.status,
    "published-shareable",
  );
  assert.equal(
    presenter.state.rows.find((row) => row.holdId === "legacy")?.status,
    "local-pending",
  );
});

test("远端待取列表成功后隐藏已非 Pending 的本地 Published 副本", async () => {
  let pendingRemoteRows: readonly SharedHeldOrderRemoteRow[] = [
    remoteRow({ holdGuid: "still-pending" }),
  ];
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([
      localSummary({ holdId: "still-pending" }),
      localSummary({ holdId: "claimed-or-completed" }),
      localSummary({ holdId: "recalling-here", status: "Recalling" }),
    ]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => pendingRemoteRows,
      listLocalShareState: async () => [
        {
          holdId: "still-pending",
          shareState: "Published",
          blockReason: null,
        },
        {
          holdId: "claimed-or-completed",
          shareState: "Published",
          blockReason: null,
        },
        {
          holdId: "recalling-here",
          shareState: "Published",
          blockReason: null,
        },
      ],
    }),
  );

  await presenter.refresh();

  assert.equal(
    presenter.state.rows.some((row) => row.holdId === "claimed-or-completed"),
    false,
  );
  assert.equal(
    presenter.state.rows.find((row) => row.holdId === "recalling-here")?.status,
    "claiming-here",
  );

  pendingRemoteRows = [
    remoteRow({ holdGuid: "still-pending" }),
    remoteRow({ holdGuid: "claimed-or-completed" }),
  ];
  await presenter.refresh();

  assert.equal(
    presenter.state.rows.find((row) => row.holdId === "claimed-or-completed")
      ?.status,
    "published-shareable",
  );
});

test("新一轮远端仍在加载时旧缓存不能提前隐藏刚发布的本机副本", async () => {
  const secondRemote = deferred<readonly SharedHeldOrderRemoteRow[]>();
  let remoteCalls = 0;
  let shareCalls = 0;
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([localSummary({ holdId: "newly-published" })]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: () => {
        remoteCalls += 1;
        return remoteCalls === 1 ? Promise.resolve([]) : secondRemote.promise;
      },
      listLocalShareState: async () => {
        shareCalls += 1;
        return [{
          holdId: "newly-published",
          shareState: shareCalls === 1 ? "NeedsEvaluation" : "Published",
          blockReason: null,
          requestedAtIso: shareCalls === 1 ? null : "2026-07-28T04:00:00.000Z",
        }];
      },
    }),
  );

  await presenter.refresh();
  await Promise.resolve();
  assert.equal(presenter.state.remoteRefreshing, false);

  await presenter.refresh();

  assert.equal(presenter.state.remoteRefreshing, true);
  assert.equal(
    presenter.state.rows.find((row) => row.holdId === "newly-published")?.status,
    "published-shareable",
  );

  secondRemote.resolve([]);
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(
    presenter.state.rows.some((row) => row.holdId === "newly-published"),
    false,
  );
});

test("本地读取失败时远端拒绝也被后台刷新消费", async () => {
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([], "local-ledger-failed").orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => {
        throw new Error("remote-failed-too");
      },
    }),
  );

  await assert.doesNotReject(presenter.refresh());
  await Promise.resolve();
  assert.equal(presenter.state.kind, "failed");
});

test("本地发布状态精确映射 NeedsEvaluation/PendingPublish/Published", async () => {
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([
      localSummary({ holdId: "needs" }),
      localSummary({ holdId: "pending" }),
      localSummary({ holdId: "published" }),
    ]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      listRemotePending: async () => [remoteRow({ holdGuid: "published" })],
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
  const presenter = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
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

test("在线取单恢复成功后不等待列表刷新即可返回结果", async () => {
  let listCalls = 0;
  let releaseList!: () => void;
  const listGate = new Promise<readonly []>((resolve) => {
    releaseList = () => resolve([]);
  });
  const orchestrator = {
    async list() {
      listCalls += 1;
      return listGate;
    },
  } as unknown as HeldOrdersOrchestrator;
  const presenter = createHeldOrdersPresenter(orchestrator);
  presenter.attachSharedOrders(
    fakeSharedPort({
      takeRemoteHold: async (holdGuid) => ({
        ok: true,
        outcome: "restored",
        holdGuid,
      }),
    }),
  );

  const action = presenter.takeRemote("R1");
  for (let turn = 0; turn < 5; turn += 1) await Promise.resolve();
  try {
    assert.equal(listCalls, 0);
  } finally {
    releaseList();
  }
  assert.deepEqual(await action, {
    ok: true,
    code: "recalled",
    holdId: "R1",
  });
});

test("共享 owner release 优先于 legacy release；无共享 claim 才回退旧路径", async () => {
  const calls = { shared: 0, legacy: 0 };
  const orchestrator = {
    list: async () => [],
    release: async (
      holdId: string,
      releaseOwnedClaim?: (holdId: string) => Promise<boolean>,
    ) => {
      if (releaseOwnedClaim) {
        try {
          if (await releaseOwnedClaim(holdId)) {
            return { ok: true as const, code: "released" as const, holdId };
          }
        } catch {
          return { ok: false as const, code: "release-failed" as const, holdId };
        }
      }
      calls.legacy += 1;
      return { ok: true as const, code: "released" as const, holdId };
    },
  } as unknown as HeldOrdersOrchestrator;
  const presenter = createHeldOrdersPresenter(orchestrator);
  presenter.attachSharedOrders(
    fakeSharedPort({
      releaseOwnedClaim: async () => {
        calls.shared += 1;
        return true;
      },
    }),
  );

  assert.deepEqual(await presenter.release("shared-1"), {
    ok: true,
    code: "released",
    holdId: "shared-1",
  });
  assert.deepEqual(calls, { shared: 1, legacy: 0 });

  presenter.attachSharedOrders(
    fakeSharedPort({
      releaseOwnedClaim: async () => {
        calls.shared += 1;
        return false;
      },
    }),
  );
  assert.deepEqual(await presenter.release("legacy-1"), {
    ok: true,
    code: "released",
    holdId: "legacy-1",
  });
  assert.deepEqual(calls, { shared: 2, legacy: 1 });

  presenter.attachSharedOrders(
    fakeSharedPort({
      releaseOwnedClaim: async () => {
        calls.shared += 1;
        throw new Error("shared release failed");
      },
    }),
  );
  assert.deepEqual(await presenter.release("shared-failed"), {
    ok: false,
    code: "release-failed",
    holdId: "shared-failed",
  });
  assert.deepEqual(calls, { shared: 3, legacy: 1 });
});

test("删除本地挂单时把共享取消端口交给 orchestrator，并在成功后刷新列表", async () => {
  const calls = { deleted: [] as string[], cancelled: [] as string[], listed: 0 };
  const orchestrator = {
    async list() {
      calls.listed += 1;
      return [];
    },
    async delete(
      holdId: string,
      cancelShared?: (holdId: string) => Promise<void>,
    ) {
      calls.deleted.push(holdId);
      await cancelShared?.(holdId);
      return { ok: true as const, code: "deleted" as const, holdId };
    },
  } as unknown as HeldOrdersOrchestrator;
  const presenter = createHeldOrdersPresenter(orchestrator);
  presenter.attachSharedOrders(
    fakeSharedPort({
      cancelOwnedHold: async (holdId) => {
        calls.cancelled.push(holdId);
      },
    }),
  );

  assert.deepEqual(await presenter.delete("H1"), {
    ok: true,
    code: "deleted",
    holdId: "H1",
  });
  assert.deepEqual(calls.deleted, ["H1"]);
  assert.deepEqual(calls.cancelled, ["H1"]);
  assert.equal(calls.listed, 1);
});

test("远端取消失败后立即刷新列表以展示本地删除阻断状态", async () => {
  let listed = 0;
  const orchestrator = {
    async list() {
      listed += 1;
      return [];
    },
    async delete(holdId: string) {
      return {
        ok: false as const,
        code: "delete-shared-failed" as const,
        holdId,
      };
    },
  } as unknown as HeldOrdersOrchestrator;
  const presenter = createHeldOrdersPresenter(orchestrator);

  assert.deepEqual(await presenter.delete("H1"), {
    ok: false,
    code: "delete-shared-failed",
    holdId: "H1",
  });
  assert.equal(listed, 1);
});

test("coordinator fence 冲突映射为 shared-fence-held 且不覆盖列表", async () => {
  const presenter = createHeldOrdersPresenter(
    fakeOrchestrator([localSummary()]).orchestrator,
  );
  presenter.attachSharedOrders(
    fakeSharedPort({
      takeRemoteHold: async () => {
        throw Object.assign(new Error("claim remains open"), {
          name: "SharedHeldOrderCoordinatorError",
          code: "FENCE_CONFLICT",
        });
      },
    }),
  );
  await presenter.refresh();
  const result = await presenter.takeRemote("H1");
  assert.equal(result.code, "shared-fence-held");
  assert.equal(presenter.state.rows.length, 1);
});

test("未知 coordinator 异常仍保守映射为 shared-conflict", async () => {
  const presenter = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
  presenter.attachSharedOrders(
    fakeSharedPort({
      takeRemoteHold: async () => {
        throw new Error("unknown");
      },
    }),
  );
  assert.equal((await presenter.takeRemote("H1")).code, "shared-conflict");
});

test("共享取单错误只把专用销售模式码映射为 sale-mode-required，协议 INVALID 保持冲突", async () => {
  const resultFor = async (code: string) => {
    const presenter = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
    presenter.attachSharedOrders(
      fakeSharedPort({
        takeRemoteHold: async () => {
          throw Object.assign(new Error(code), {
            name: "SharedHeldOrderCoordinatorError",
            code,
          });
        },
      }),
    );
    return presenter.takeRemote("H1");
  };

  assert.equal((await resultFor("SALE_MODE_REQUIRED")).code, "sale-mode-required");
  assert.equal((await resultFor("INVALID")).code, "shared-conflict");
  assert.equal((await resultFor("RESTORE_FAILED")).code, "shared-restore-failed");
});

test("离线本地取回委托 recallLocalPublication，远端列表不可用也不影响取回", async () => {
  let remoteListCalls = 0;
  const presenter = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
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
  // 恢复成功后页面立即返回收银，不再等待或触发无意义的远端列表刷新。
  assert.equal(remoteListCalls, 0);
  assert.equal(presenter.state.refreshError, null);
});

test("强制释放：未接线不可用、空原因被拒、有原因才委托授权端口", async () => {
  const noPort = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
  assert.equal(noPort.supportsForceRelease(), false);
  assert.equal(
    (await noPort.forceRelease("H1", "reason")).code,
    "force-release-unavailable",
  );

  const withoutAdapter = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
  withoutAdapter.attachSharedOrders(fakeSharedPort());
  assert.equal(withoutAdapter.supportsForceRelease(), false);
  assert.equal(
    (await withoutAdapter.forceRelease("H1", "reason")).code,
    "force-release-unavailable",
  );

  const forceRelease = fakeForceRelease();
  const withAdapter = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
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

  const throwing = createHeldOrdersPresenter(fakeOrchestrator().orchestrator);
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
  const presenter = createHeldOrdersPresenter(orchestrator);
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
  const presenter = createHeldOrdersPresenter(orchestrator);
  presenter.attachSharedOrders(fakeSharedPort());

  const first = presenter.refresh();
  const second = presenter.refresh();
  assert.equal(first, second);
  await first;
  assert.equal(calls.list, 1);
  await presenter.refresh();
  assert.equal(calls.list, 2);
});
