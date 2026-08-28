import assert from "node:assert/strict";
import test from "node:test";

import {
  buildSyncHistorySupportExport,
  serializeSyncHistorySupportExport,
  type LocalSyncHistoryOrder,
  type LocalSyncHistoryPage,
  type LocalSyncHistoryPageQuery,
  type LocalSyncHistoryPort,
} from "./sync-history-domain";
import { SyncHistoryPresenter } from "./sync-history-presenter";

function order(overrides: Partial<LocalSyncHistoryOrder> = {}): LocalSyncHistoryOrder {
  return {
    orderGuid: "order-100",
    localSequence: 100,
    storeCode: "BNE",
    deviceCode: "IPAD-1",
    soldAtIso: "2026-07-28T10:11:12.000Z",
    state: "PendingSync",
    totalCents: 1200,
    discountCents: 100,
    actualAmountCents: 1100,
    tenders: [{ method: "cash", amountCents: 1100 }],
    outbox: {
      state: "pending",
      attemptCount: 2,
      lastErrorCode: "SYNC_NETWORK",
      nextAttemptAtIso: "2026-07-28T10:12:12.000Z",
    },
    ...overrides,
  };
}

class MemoryPort implements LocalSyncHistoryPort {
  public readonly queries: LocalSyncHistoryPageQuery[] = [];
  public readonly supportSnapshotQueries: {
    filters: LocalSyncHistoryPageQuery["filters"];
    limit: number;
  }[] = [];
  public readonly restoreCalls: string[][] = [];
  public supportContextCalls = 0;
  public pageError = false;
  public restoreHold: Promise<void> | null = null;

  public constructor(public orders: LocalSyncHistoryOrder[]) {}

  public async listLocalSyncHistory(query: LocalSyncHistoryPageQuery): Promise<LocalSyncHistoryPage> {
    this.queries.push(query);
    if (this.pageError) throw new Error("database unavailable");
    const filtered = this.orders
      .filter((candidate) => query.beforeLocalSequence === null || candidate.localSequence < query.beforeLocalSequence)
      .filter((candidate) => !query.filters.dateFromIso || candidate.soldAtIso >= query.filters.dateFromIso)
      .filter((candidate) => !query.filters.dateToIso || candidate.soldAtIso <= query.filters.dateToIso)
      .filter((candidate) => !query.filters.states.length || query.filters.states.includes(candidate.state))
      .sort((left, right) => right.localSequence - left.localSequence);
    const page = filtered.slice(0, query.limit);
    const final = page.at(-1)?.localSequence ?? null;
    return {
      orders: page,
      nextBeforeLocalSequence: page.length === query.limit && filtered.length > page.length ? final : null,
      pendingCount: this.orders.filter((candidate) => candidate.outbox?.state === "pending").length,
    };
  }

  public async getLocalSyncHistorySupportSnapshot(query: {
    filters: LocalSyncHistoryPageQuery["filters"];
    limit: number;
  }) {
    this.supportSnapshotQueries.push(query);
    const orders = this.orders
      .filter(
        (candidate) =>
          !query.filters.dateFromIso ||
          candidate.soldAtIso >= query.filters.dateFromIso,
      )
      .filter(
        (candidate) =>
          !query.filters.dateToIso ||
          candidate.soldAtIso <= query.filters.dateToIso,
      )
      .filter(
        (candidate) =>
          !query.filters.states.length ||
          query.filters.states.includes(candidate.state),
      )
      .sort((left, right) => right.localSequence - left.localSequence);
    return {
      orders: orders.slice(0, query.limit),
      totalMatchingCount: orders.length,
    };
  }

  public async restoreExistingOrderOutboxToPending(orderGuids: readonly string[]) {
    this.restoreCalls.push([...orderGuids]);
    await this.restoreHold;
    const restored: string[] = [];
    const skipped: string[] = [];
    for (const orderGuid of orderGuids) {
      const current = this.orders.find((candidate) => candidate.orderGuid === orderGuid);
      const currentOutbox = current?.outbox;
      if (!current || !currentOutbox || currentOutbox.state !== "pending") {
        skipped.push(orderGuid);
        continue;
      }
      this.orders = this.orders.map((candidate) => candidate.orderGuid === orderGuid
        ? {
            ...candidate,
            outbox: {
              state: currentOutbox.state,
              attemptCount: currentOutbox.attemptCount,
              lastErrorCode: null,
              nextAttemptAtIso: "2026-07-28T10:11:13.000Z",
            },
          }
        : candidate);
      restored.push(orderGuid);
    }
    return { restoredOrderGuids: restored, skippedOrderGuids: skipped };
  }

  public async getSupportContext() {
    this.supportContextCalls += 1;
    return { appId: "hb-pos-ipad", appVersion: "2.11.0", deviceCode: "IPAD-1", storeCode: "BNE" };
  }
}

const SYNC_HISTORY_ALL_PERMISSIONS = [
  "Permissions.PosTerminal.Audit.View",
  "Permissions.PosTerminal.History.View",
  "Permissions.PosTerminal.System.Sync",
];

function createPresenter(
  port: LocalSyncHistoryPort,
  pageSize?: number,
): SyncHistoryPresenter {
  return new SyncHistoryPresenter({
    permissionCodes: SYNC_HISTORY_ALL_PERMISSIONS,
    port,
    ...(pageSize === undefined ? {} : { pageSize }),
  });
}

test("缺少历史查看权限时不读取本地仓储或导出，且缺少手动同步权限时不允许选择或补传", async () => {
  const noHistoryPort = new MemoryPort([order()]);
  const noHistory = new SyncHistoryPresenter({
    permissionCodes: [],
    port: noHistoryPort,
  });

  await noHistory.refresh();
  await noHistory.loadNextPage();
  noHistory.setSelected("order-100", true);
  const selectedWithoutHistory = await noHistory.requestRetransmitSelected();
  const rangeWithoutHistory = await noHistory.requestRetransmitDateRange();

  await assert.rejects(
    noHistory.createSupportExport(),
    /permission-required/,
  );
  assert.equal(noHistory.state.kind, "empty");
  assert.deepEqual(noHistory.state.rows, []);
  assert.deepEqual(noHistory.state.selectedOrderGuids, []);
  assert.equal(selectedWithoutHistory.errorCode, "permission-required");
  assert.equal(rangeWithoutHistory.errorCode, "permission-required");
  assert.deepEqual(noHistoryPort.queries, []);
  assert.deepEqual(noHistoryPort.restoreCalls, []);
  assert.equal(noHistoryPort.supportContextCalls, 0);

  const viewOnlyPort = new MemoryPort([order()]);
  const viewOnly = new SyncHistoryPresenter({
    permissionCodes: ["Permissions.PosTerminal.History.View"],
    port: viewOnlyPort,
  });
  await viewOnly.refresh();
  viewOnly.setSelected("order-100", true);
  const selectedWithoutSync = await viewOnly.requestRetransmitSelected();
  viewOnly.setFilters({
    dateFromIso: "2026-07-28T00:00:00.000Z",
    dateToIso: "2026-07-28T23:59:59.999Z",
    states: [],
  });
  const rangeWithoutSync = await viewOnly.requestRetransmitDateRange();

  assert.deepEqual(viewOnly.state.selectedOrderGuids, []);
  assert.equal(selectedWithoutSync.errorCode, "permission-required");
  assert.equal(rangeWithoutSync.errorCode, "permission-required");
  assert.deepEqual(viewOnlyPort.restoreCalls, []);
});

test("支持导出额外要求 Audit.View，而手动重传仍只依赖 History.View 与 System.Sync", async () => {
  const port = new MemoryPort([order()]);
  const presenter = new SyncHistoryPresenter({
    permissionCodes: [
      "Permissions.PosTerminal.History.View",
      "Permissions.PosTerminal.System.Sync",
    ],
    port,
  });

  await presenter.refresh();
  presenter.setSelected("order-100", true);
  const retransmit = await presenter.requestRetransmitSelected();

  assert.equal(presenter.state.access.canManualRetransmit, true);
  assert.equal(presenter.state.access.canExport, false);
  assert.equal(retransmit.kind, "requested");
  assert.deepEqual(port.restoreCalls, [["order-100"]]);
  await assert.rejects(presenter.createSupportExport(), /permission-required/);
  assert.equal(port.supportContextCalls, 0);
});

test("稳定分页严格按 local_sequence 降序，并使用上一页的序号作为下页游标", async () => {
  const port = new MemoryPort([
    order({ orderGuid: "order-98", localSequence: 98 }),
    order({ orderGuid: "order-101", localSequence: 101 }),
    order({ orderGuid: "order-100", localSequence: 100 }),
    order({ orderGuid: "order-99", localSequence: 99 }),
  ]);
  const presenter = createPresenter(port, 2);

  await presenter.refresh();
  await presenter.loadNextPage();

  assert.equal(port.queries[0]?.beforeLocalSequence, null);
  assert.equal(port.queries[1]?.beforeLocalSequence, 100);
  assert.deepEqual(presenter.state.rows.map((row) => row.localSequence), [101, 100, 99, 98]);
  assert.equal(presenter.state.kind, "ready");
});

test("支持导出通过单次有界仓储快照读取筛选结果，并显式标记截断而不依赖 UI 已加载页", async () => {
  const port = new MemoryPort([
    order({ orderGuid: "order-105", localSequence: 105 }),
    order({ orderGuid: "order-104", localSequence: 104 }),
    order({ orderGuid: "order-103", localSequence: 103 }),
    order({ orderGuid: "order-102", localSequence: 102 }),
    order({ orderGuid: "order-101", localSequence: 101 }),
  ]);
  const presenter = new SyncHistoryPresenter({
    permissionCodes: SYNC_HISTORY_ALL_PERMISSIONS,
    port,
    pageSize: 2,
    supportExportMaxOrders: 3,
    nowIso: () => "2026-07-28T12:34:56.000Z",
  });

  await presenter.refresh();
  assert.equal(presenter.state.rows.length, 2);

  const exported = await presenter.createSupportExport();

  assert.equal(presenter.state.rows.length, 2);
  assert.deepEqual(
    exported.orders.map((candidate) => candidate.localSequence),
    [105, 104, 103],
  );
  assert.deepEqual(exported.snapshot, {
    createdAtIso: "2026-07-28T12:34:56.000Z",
    filters: {
      dateFromIso: null,
      dateToIso: null,
      states: [],
    },
    exportedCount: 3,
    totalMatchingCount: 5,
    truncated: true,
  });
  assert.deepEqual(port.queries.slice(1), []);
  assert.deepEqual(port.supportSnapshotQueries, [
    {
      filters: {
        dateFromIso: null,
        dateToIso: null,
        states: [],
      },
      limit: 3,
    },
  ]);
});

test("支持导出拒绝仓储越过硬上限或返回自相矛盾的匹配计数", async () => {
  const port = new MemoryPort([
    order({ orderGuid: "order-103", localSequence: 103 }),
    order({ orderGuid: "order-102", localSequence: 102 }),
    order({ orderGuid: "order-101", localSequence: 101 }),
  ]);
  port.getLocalSyncHistorySupportSnapshot = async () => ({
    orders: port.orders,
    totalMatchingCount: 2,
  });
  const presenter = new SyncHistoryPresenter({
    permissionCodes: SYNC_HISTORY_ALL_PERMISSIONS,
    port,
    supportExportMaxOrders: 2,
  });

  await assert.rejects(
    presenter.createSupportExport(),
    /support snapshot is invalid/i,
  );
});

test("非末页 cursor 必须等于本页最后序号，禁止跳过中间 local_sequence", async () => {
  const port = new MemoryPort([]);
  port.listLocalSyncHistory = async (query) => {
    port.queries.push(query);
    return {
      orders: [
        order({ orderGuid: "order-100", localSequence: 100 }),
        order({ orderGuid: "order-99", localSequence: 99 }),
      ],
      nextBeforeLocalSequence: 98,
      pendingCount: 2,
    };
  };
  const presenter = createPresenter(port, 2);

  await presenter.refresh();

  assert.equal(presenter.state.kind, "failed");
  assert.equal(presenter.state.rows.length, 0);
  assert.equal(port.queries.length, 1);
});

test("空页必须结束分页，禁止返回不前进 cursor 造成死循环", async () => {
  const port = new MemoryPort([]);
  port.listLocalSyncHistory = async (query) => {
    port.queries.push(query);
    if (query.beforeLocalSequence === null) {
      return {
        orders: [
          order({ orderGuid: "order-101", localSequence: 101 }),
          order({ orderGuid: "order-100", localSequence: 100 }),
        ],
        nextBeforeLocalSequence: 100,
        pendingCount: 2,
      };
    }
    return { orders: [], nextBeforeLocalSequence: 100, pendingCount: 2 };
  };
  const presenter = createPresenter(port, 2);

  await presenter.refresh();
  await presenter.loadNextPage();

  assert.equal(presenter.state.kind, "failed");
  assert.equal(port.queries.length, 2);
});

test("状态、tender 摘要与 pending count 映射到 UI 行，且只显示安全错误码", async () => {
  const port = new MemoryPort([
    order({ orderGuid: "completed", state: "CompletedLocal" }),
    order({ orderGuid: "syncing", localSequence: 99, state: "Syncing", outbox: { state: "leased", attemptCount: 1, lastErrorCode: "SYNC_TIMEOUT", nextAttemptAtIso: null } }),
    order({ orderGuid: "blocked", localSequence: 98, state: "Blocked403", outbox: { state: "blocked403", attemptCount: 3, lastErrorCode: "SYNC_403", nextAttemptAtIso: null } }),
    order({ orderGuid: "rejected", localSequence: 97, state: "Rejected", outbox: { state: "rejected", attemptCount: 4, lastErrorCode: "voucher-token-secret", nextAttemptAtIso: null } }),
    order({ orderGuid: "synced", localSequence: 96, state: "Synced", outbox: { state: "succeeded", attemptCount: 1, lastErrorCode: null, nextAttemptAtIso: null } }),
  ]);
  const presenter = createPresenter(port);

  await presenter.refresh();

  assert.deepEqual(presenter.state.rows.map((row) => row.state), ["CompletedLocal", "Syncing", "Blocked403", "Rejected", "Synced"]);
  assert.equal(presenter.state.rows[0]?.tenderSummary, "CASH $11.00");
  assert.equal(presenter.state.pendingCount, 1);
  assert.equal(presenter.state.rows.find((row) => row.orderGuid === "rejected")?.outbox?.lastErrorCode, null);
});

test("选择订单与日期范围重传都只提交允许的 pending outbox", async () => {
  const eligible = order({ orderGuid: "eligible", localSequence: 103, soldAtIso: "2026-07-27T10:00:00.000Z" });
  const syncing = order({ orderGuid: "syncing", localSequence: 102, soldAtIso: "2026-07-27T11:00:00.000Z", state: "Syncing", outbox: { state: "leased", attemptCount: 2, lastErrorCode: null, nextAttemptAtIso: null } });
  const blocked = order({ orderGuid: "blocked", localSequence: 101, soldAtIso: "2026-07-27T12:00:00.000Z", state: "Blocked403", outbox: { state: "blocked403", attemptCount: 2, lastErrorCode: "SYNC_403", nextAttemptAtIso: null } });
  const rejected = order({ orderGuid: "rejected", localSequence: 100, soldAtIso: "2026-07-27T13:00:00.000Z", state: "Rejected", outbox: { state: "rejected", attemptCount: 2, lastErrorCode: "BUSINESS_REJECTED", nextAttemptAtIso: null } });
  const port = new MemoryPort([eligible, syncing, blocked, rejected]);
  const presenter = createPresenter(port, 2);

  await presenter.refresh();
  presenter.setSelected("eligible", true);
  presenter.setSelected("syncing", true);
  const selected = await presenter.requestRetransmitSelected();

  assert.deepEqual(port.restoreCalls[0], ["eligible"]);
  assert.deepEqual(selected, {
    kind: "requested",
    requestedCount: 1,
    skippedCount: 0,
    reauthenticationRequiredCount: 0,
    supervisorRequiredCount: 0,
    errorCode: null,
  });

  presenter.setFilters({ dateFromIso: "2026-07-27T00:00:00.000Z", dateToIso: "2026-07-27T23:59:59.999Z", states: [] });
  const range = await presenter.requestRetransmitDateRange();

  assert.deepEqual(port.restoreCalls[1], ["eligible"]);
  assert.equal(range.reauthenticationRequiredCount, 1);
  assert.equal(range.supervisorRequiredCount, 1);
  assert.equal(range.skippedCount, 3);
});

test("Synced、Syncing、Blocked403 与 Rejected 不会重传，并返回相应的门禁结果", async () => {
  const port = new MemoryPort([
    order({ orderGuid: "synced", localSequence: 104, state: "Synced", outbox: { state: "succeeded", attemptCount: 1, lastErrorCode: null, nextAttemptAtIso: null } }),
    order({ orderGuid: "syncing", localSequence: 103, state: "Syncing", outbox: { state: "leased", attemptCount: 1, lastErrorCode: null, nextAttemptAtIso: null } }),
    order({ orderGuid: "blocked", localSequence: 102, state: "Blocked403", outbox: { state: "blocked403", attemptCount: 1, lastErrorCode: "SYNC_403", nextAttemptAtIso: null } }),
    order({ orderGuid: "rejected", localSequence: 101, state: "Rejected", outbox: { state: "rejected", attemptCount: 1, lastErrorCode: "REJECTED", nextAttemptAtIso: null } }),
  ]);
  const presenter = createPresenter(port);

  await presenter.refresh();
  for (const row of presenter.state.rows) presenter.setSelected(row.orderGuid, true);
  assert.deepEqual(presenter.state.selectedOrderGuids, []);
  const result = await presenter.requestRetransmitSelected();

  assert.deepEqual(port.restoreCalls, []);
  assert.deepEqual(result, {
    kind: "nothing-eligible",
    requestedCount: 0,
    skippedCount: 0,
    reauthenticationRequiredCount: 0,
    supervisorRequiredCount: 0,
    errorCode: null,
  });
});

test("日期范围补传 501 笔时交给仓储单事务恢复，避免部分成功被误报为全失败", async () => {
  const orders = Array.from({ length: 501 }, (_, index) =>
    order({
      orderGuid: `order-${index + 1}`,
      localSequence: 501 - index,
      soldAtIso: "2026-07-27T12:00:00.000Z",
    }),
  );
  const port = new MemoryPort(orders);
  const presenter = createPresenter(port, 100);
  presenter.setFilters({
    dateFromIso: "2026-07-27T00:00:00.000Z",
    dateToIso: "2026-07-27T23:59:59.999Z",
    states: [],
  });

  const result = await presenter.requestRetransmitDateRange();

  assert.deepEqual(port.restoreCalls.map((batch) => batch.length), [501]);
  assert.equal(result.kind, "requested");
  assert.equal(result.requestedCount, 501);
  assert.equal(result.skippedCount, 0);
});

test("重复点击重传单飞，只向耐久 Port 发出一次请求", async () => {
  let release!: () => void;
  const port = new MemoryPort([order()]);
  port.restoreHold = new Promise<void>((resolve) => { release = resolve; });
  const presenter = createPresenter(port);
  await presenter.refresh();
  presenter.setSelected("order-100", true);

  const first = presenter.requestRetransmitSelected();
  const second = presenter.requestRetransmitSelected();
  assert.equal(first, second);
  await Promise.resolve();
  assert.equal(port.restoreCalls.length, 1);
  release();
  await first;
});

test("筛选切换立即清空旧快照，旧 generation 晚到也不能覆盖或参与重传", async () => {
  let releaseOld!: () => void;
  let markOldStarted!: () => void;
  const oldStarted = new Promise<void>((resolve) => { markOldStarted = resolve; });
  const oldHold = new Promise<void>((resolve) => { releaseOld = resolve; });
  const port = new MemoryPort([
    order({ orderGuid: "old-selected", localSequence: 103, state: "PendingSync" }),
    order({ orderGuid: "stale-completed", localSequence: 102, state: "CompletedLocal" }),
    order({ orderGuid: "current-synced", localSequence: 101, state: "Synced", outbox: { state: "succeeded", attemptCount: 1, lastErrorCode: null, nextAttemptAtIso: null } }),
  ]);
  const ordinaryList = port.listLocalSyncHistory.bind(port);
  port.listLocalSyncHistory = async (query) => {
    if (query.filters.states.includes("CompletedLocal")) {
      port.queries.push(query);
      markOldStarted();
      await oldHold;
      return {
        orders: [order({ orderGuid: "stale-completed", localSequence: 102, state: "CompletedLocal" })],
        nextBeforeLocalSequence: null,
        pendingCount: 1,
      };
    }
    return ordinaryList(query);
  };
  const presenter = createPresenter(port);
  await presenter.refresh();
  presenter.setSelected("old-selected", true);

  presenter.setFilters({ dateFromIso: null, dateToIso: null, states: ["CompletedLocal"] });
  const staleRefresh = presenter.refresh();
  await oldStarted;
  assert.equal(stateKind(presenter), "loading");

  presenter.setFilters({ dateFromIso: null, dateToIso: null, states: ["Synced"] });
  assert.equal(stateKind(presenter), "empty");
  assert.deepEqual(rowOrderGuids(presenter), []);
  assert.deepEqual(selectedOrderGuids(presenter), []);
  assert.equal(nextCursor(presenter), null);
  const currentRefresh = presenter.refresh();
  await currentRefresh;
  releaseOld();
  await staleRefresh;

  assert.deepEqual(rowOrderGuids(presenter), ["current-synced"]);
  assert.deepEqual(filterStates(presenter), ["Synced"]);
  const retransmit = await presenter.requestRetransmitSelected();
  assert.equal(retransmit.kind, "nothing-eligible");
  assert.deepEqual(port.restoreCalls, []);
});

test("日期范围查询被新筛选取代后不得把旧候选提交重传", async () => {
  let releaseOld!: () => void;
  let markOldStarted!: () => void;
  const oldStarted = new Promise<void>((resolve) => { markOldStarted = resolve; });
  const oldHold = new Promise<void>((resolve) => { releaseOld = resolve; });
  const port = new MemoryPort([order({ orderGuid: "stale-range-order" })]);
  port.listLocalSyncHistory = async (query) => {
    port.queries.push(query);
    markOldStarted();
    await oldHold;
    return { orders: [order({ orderGuid: "stale-range-order" })], nextBeforeLocalSequence: null, pendingCount: 1 };
  };
  const presenter = createPresenter(port);
  presenter.setFilters({
    dateFromIso: "2026-07-28T00:00:00.000Z",
    dateToIso: "2026-07-28T23:59:59.999Z",
    states: [],
  });

  const staleRetransmit = presenter.requestRetransmitDateRange();
  await oldStarted;
  presenter.setFilters({
    dateFromIso: "2026-07-29T00:00:00.000Z",
    dateToIso: "2026-07-29T23:59:59.999Z",
    states: [],
  });
  releaseOld();
  const result = await staleRetransmit;

  assert.equal(result.errorCode, "query-superseded");
  assert.deepEqual(port.restoreCalls, []);
  assert.deepEqual(rowOrderGuids(presenter), []);
  assert.equal(presenter.state.filters.dateFromIso, "2026-07-29T00:00:00.000Z");
});

test("非法 ISO 日期或 from 晚于 to 时返回稳定错误且不查询", async () => {
  const port = new MemoryPort([order()]);
  const presenter = createPresenter(port);

  presenter.setFilters({
    dateFromIso: "2026-02-30T00:00:00.000Z",
    dateToIso: "2026-03-01T00:00:00.000Z",
    states: [],
  });
  await presenter.refresh();
  const invalidCalendar = presenter.state;
  assert.equal(invalidCalendar.kind, "failed");
  assert.equal(invalidCalendar.kind === "failed" ? invalidCalendar.errorCode : null, "invalid-date-range");
  const invalidRetransmit = await presenter.requestRetransmitDateRange();
  assert.equal(invalidRetransmit.errorCode, "invalid-date-range");
  assert.equal(port.queries.length, 0);

  presenter.setFilters({
    dateFromIso: "2026-07-29T00:00:00.000Z",
    dateToIso: "2026-07-28T00:00:00.000Z",
    states: [],
  });
  await presenter.refresh();
  const reversed = presenter.state;
  assert.equal(reversed.kind === "failed" ? reversed.errorCode : null, "invalid-date-range");
  assert.equal(port.queries.length, 0);
});

test("支持导出只含白名单同步诊断字段，敏感支付内容与未知字段一律丢弃", async () => {
  const sensitive = {
    ...order({
      orderGuid: "real-order-guid-100",
      soldAtIso: "2026-07-28T10:11:12.987Z",
      tenders: [{ method: "card" as const, amountCents: 1100 }],
      outbox: { state: "pending" as const, attemptCount: 2, lastErrorCode: "PAN-4111111111111111", nextAttemptAtIso: null },
    }),
    authorizationCode: "AUTH-SECRET",
    reservationToken: "voucher-secret",
    receiptBytes: Uint8Array.of(1, 2, 3),
    customerPhone: "0400000000",
  } as LocalSyncHistoryOrder;
  const model = buildSyncHistorySupportExport(
    {
      appId: "hb-pos-ipad",
      appVersion: "2.11.0",
      deviceCode: "IPAD-1",
      storeCode: "BNE",
      apiToken: "must-not-export",
    } as never,
    [
      sensitive,
      order({
        deviceCode: "IPAD-2",
        localSequence: 99,
        orderGuid: "real-order-guid-99",
        outbox: {
          attemptCount: 3,
          lastErrorCode: "VOUCHER_RELEASE_REJECTED",
          nextAttemptAtIso: null,
          state: "rejected",
        },
        soldAtIso: "2026-07-28T23:59:59.999Z",
        storeCode: "SYD",
      }),
      sensitive,
    ],
    {
      createdAtIso: "2026-07-28T12:34:56.000Z",
      filters: {
        dateFromIso: null,
        dateToIso: null,
        states: [],
      },
      exportedCount: 3,
      totalMatchingCount: 3,
      truncated: false,
    },
  );
  const json = serializeSyncHistorySupportExport(model);

  assert.equal(model.device.code, "device");
  assert.equal(model.store.code, "store");
  assert.deepEqual(
    model.orders.map((candidate) => candidate.orderGuid),
    ["order-0001", "order-0002", "order-0001"],
  );
  assert.deepEqual(
    model.orders.map((candidate) => candidate.storeCode),
    ["store", "store-0002", "store"],
  );
  assert.deepEqual(
    model.orders.map((candidate) => candidate.deviceCode),
    ["device", "device-0002", "device"],
  );
  assert.deepEqual(
    model.orders.map((candidate) => candidate.soldAtUtcDate),
    ["2026-07-28", "2026-07-28", "2026-07-28"],
  );
  assert.equal(model.orders[0]?.outbox?.lastErrorCode, null);
  assert.equal(
    model.orders[1]?.outbox?.lastErrorCode,
    "VOUCHER_RELEASE_REJECTED",
  );
  assert.match(json, /hb-pos-sync-history-v1/);
  assert.doesNotMatch(
    json,
    /AUTH-SECRET|voucher-secret|4111111111111111|customerPhone|receiptBytes|must-not-export|real-order-guid|IPAD-|BNE|SYD|10:11:12|23:59:59|soldAtIso/,
  );
});

test("加载状态可区分 loading、ready、empty 和 failed，且 presenter 不提供退款 action", async () => {
  let release!: () => void;
  const port = new MemoryPort([order()]);
  const originalList = port.listLocalSyncHistory.bind(port);
  const hold = new Promise<void>((resolve) => { release = resolve; });
  port.listLocalSyncHistory = async (query) => {
    await hold;
    return originalList(query);
  };
  const presenter = createPresenter(port);

  const loading = presenter.refresh();
  assert.equal(stateKind(presenter), "loading");
  release();
  await loading;
  assert.equal(stateKind(presenter), "ready");
  assert.equal(presenter.refundActionAvailable, false);
  assert.equal("requestRefund" in presenter, false);

  port.orders = [];
  await presenter.refresh();
  assert.equal(stateKind(presenter), "empty");

  port.pageError = true;
  await presenter.refresh();
  const failed = presenter.state;
  assert.equal(failed.kind, "failed");
  assert.equal(failed.errorCode, "history-load-failed");
});

test("外部存储只在 publish 时通知，取消订阅与 destroy 后不再接收晚到结果", async () => {
  let release!: () => void;
  const hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const port = new MemoryPort([order()]);
  const ordinaryList = port.listLocalSyncHistory.bind(port);
  port.listLocalSyncHistory = async (query) => {
    await hold;
    return ordinaryList(query);
  };
  const presenter = createPresenter(port);
  const snapshots: string[] = [];
  const unsubscribe = presenter.subscribe(() => {
    snapshots.push(presenter.getState().kind);
  });

  assert.equal(presenter.getState(), presenter.state);
  presenter.setSelected("missing-order", true);
  assert.deepEqual(snapshots, []);

  const loading = presenter.refresh();
  assert.deepEqual(snapshots, ["loading"]);
  unsubscribe();
  unsubscribe();
  release();
  await loading;
  assert.deepEqual(snapshots, ["loading"]);
  assert.equal(presenter.getState().kind, "ready");

  const stateBeforeDestroy = presenter.getState();
  presenter.destroy();
  presenter.destroy();
  presenter.setFilters({
    dateFromIso: null,
    dateToIso: null,
    states: ["Synced"],
  });
  let afterDestroyCalls = 0;
  presenter.subscribe(() => {
    afterDestroyCalls += 1;
  });
  await presenter.refresh();

  assert.equal(presenter.getState(), stateBeforeDestroy);
  assert.deepEqual(snapshots, ["loading"]);
  assert.equal(afterDestroyCalls, 0);
  assert.equal(
    (await presenter.requestRetransmitSelected()).errorCode,
    "presenter-destroyed",
  );
});

function stateKind(presenter: SyncHistoryPresenter): string {
  return presenter.state.kind;
}

function rowOrderGuids(presenter: SyncHistoryPresenter): string[] {
  return presenter.state.rows.map((row) => row.orderGuid);
}

function selectedOrderGuids(presenter: SyncHistoryPresenter): readonly string[] {
  return presenter.state.selectedOrderGuids;
}

function nextCursor(presenter: SyncHistoryPresenter): number | null {
  return presenter.state.nextBeforeLocalSequence;
}

function filterStates(presenter: SyncHistoryPresenter): readonly string[] {
  return presenter.state.filters.states;
}
