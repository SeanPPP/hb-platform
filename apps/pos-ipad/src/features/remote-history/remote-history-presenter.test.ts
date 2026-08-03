import assert from "node:assert/strict";
import test from "node:test";

import {
  REMOTE_HISTORY_VIEW_PERMISSION,
  REMOTE_HISTORY_REPRINT_PERMISSION,
  RemoteHistoryPresenter,
  type RemoteHistoryReprintPort,
} from "./remote-history-presenter";

import type {
  RemoteOrderHistoryDetails,
  RemoteOrderHistoryPort,
  RemoteOrderHistoryQuery,
  RemoteOrderHistorySummary,
} from "@/core/contracts/remote-history";

const firstRow: RemoteOrderHistorySummary = {
  orderGuid: "10000000-0000-4000-8000-000000000001",
  storeCode: "S1",
  deviceCode: "IPAD-1",
  cashierName: "Alice",
  soldAtIso: "2026-07-27T01:02:03.000Z",
  totalCents: 1234,
  discountCents: 0,
  actualAmountCents: 1234,
  lineCount: 1,
  paymentSummary: "Cash",
  statusLabel: "Synced",
};

const secondRow: RemoteOrderHistorySummary = {
  ...firstRow,
  orderGuid: "10000000-0000-4000-8000-000000000002",
  deviceCode: "IPAD-2",
  cashierName: "Bob",
};

const firstDetails: RemoteOrderHistoryDetails = {
  ...firstRow,
  lines: [],
  payments: [],
};

class MemoryPort implements RemoteOrderHistoryPort {
  public readonly queries: RemoteOrderHistoryQuery[] = [];
  public readonly detailRequests: string[] = [];
  public listImpl: (
    query: RemoteOrderHistoryQuery,
  ) => Promise<readonly RemoteOrderHistorySummary[]> = async () => [];
  public detailsImpl: (
    orderGuid: string,
  ) => Promise<RemoteOrderHistoryDetails | null> = async () => null;

  public list(
    query: RemoteOrderHistoryQuery,
  ): Promise<readonly RemoteOrderHistorySummary[]> {
    this.queries.push(query);
    return this.listImpl(query);
  }

  public getDetails(
    orderGuid: string,
  ): Promise<RemoteOrderHistoryDetails | null> {
    this.detailRequests.push(orderGuid);
    return this.detailsImpl(orderGuid);
  }
}

class MemoryReprintPort implements RemoteHistoryReprintPort {
  public readonly orderGuids: string[] = [];
  public canReprintImpl: (details: RemoteOrderHistoryDetails) => boolean = () => true;
  public reprintImpl: (orderGuid: string) => Promise<void> = async () =>
    undefined;

  public canReprint(details: RemoteOrderHistoryDetails): boolean {
    return this.canReprintImpl(details);
  }

  public reprintExistingOrder(orderGuid: string): Promise<void> {
    this.orderGuids.push(orderGuid);
    return this.reprintImpl(orderGuid);
  }
}

function presenter(
  port: RemoteOrderHistoryPort | null,
  overrides: Partial<{
    online: boolean;
    permissionCodes: readonly string[];
    reprintPort: RemoteHistoryReprintPort | null;
  }> = {},
) {
  return new RemoteHistoryPresenter({
    port,
    trustedStoreCode: "S1",
    currentDeviceCode: "IPAD-1",
    online: overrides.online ?? true,
    permissionCodes:
      overrides.permissionCodes ?? [REMOTE_HISTORY_VIEW_PERMISSION],
    reprintPort: overrides.reprintPort ?? null,
    now: () => new Date("2026-07-27T05:00:00Z"),
  });
}

test("refresh 固定可信门店、默认查询全部终端和 take=100，并自动读取首单详情", async () => {
  const port = new MemoryPort();
  port.listImpl = async () => [firstRow];
  port.detailsImpl = async () => firstDetails;
  const value = presenter(port);

  await value.refresh();

  assert.equal(value.state.kind, "ready");
  assert.equal(value.state.rows[0]?.orderGuid, firstRow.orderGuid);
  assert.equal(value.state.details.kind, "ready");
  assert.deepEqual(port.queries, [
    {
      storeCode: "S1",
      deviceCode: null,
      soldFromIso: "2026-07-26T14:00:00.000Z",
      soldToIso: "2026-07-27T13:59:59.999Z",
      keyword: null,
      take: 100,
    },
  ]);
  assert.deepEqual(port.detailRequests, [firstRow.orderGuid]);
  assert.deepEqual(value.capabilities, {
    refund: false,
    recall: false,
    reprint: false,
  });
});

test("仅对当前可信门店终端内已加载的订单详情，按 History.Reprint 调用窄重打 port", async () => {
  const port = new MemoryPort();
  const reprintPort = new MemoryReprintPort();
  port.listImpl = async () => [firstRow];
  port.detailsImpl = async () => firstDetails;
  const value = presenter(port, {
    permissionCodes: [
      REMOTE_HISTORY_VIEW_PERMISSION,
      REMOTE_HISTORY_REPRINT_PERMISSION,
    ],
    reprintPort,
  });

  await value.refresh();
  await value.reprintSelected();

  assert.deepEqual(reprintPort.orderGuids, [firstRow.orderGuid]);
  assert.deepEqual(value.capabilities, {
    refund: false,
    recall: false,
    reprint: true,
  });
  assert.deepEqual(value.state.reprint, {
    kind: "succeeded",
    orderGuid: firstRow.orderGuid,
  });
});

test("同店跨终端订单可由窄 port 判定后重打；缺权限、缺 port 或不满足票据条件仍拒绝", async () => {
  const port = new MemoryPort();
  const reprintPort = new MemoryReprintPort();
  port.listImpl = async () => [firstRow];
  port.detailsImpl = async () => firstDetails;
  const missingPermission = presenter(port, { reprintPort });
  await missingPermission.refresh();
  await missingPermission.reprintSelected();
  assert.deepEqual(reprintPort.orderGuids, []);

  const missingPort = presenter(port, {
    permissionCodes: [
      REMOTE_HISTORY_VIEW_PERMISSION,
      REMOTE_HISTORY_REPRINT_PERMISSION,
    ],
  });
  await missingPort.refresh();
  await missingPort.reprintSelected();
  assert.equal(missingPort.state.reprint.kind, "unavailable");

  const outsideDevicePort = new MemoryPort();
  outsideDevicePort.listImpl = async () => [secondRow];
  outsideDevicePort.detailsImpl = async () => ({
    ...firstDetails,
    orderGuid: secondRow.orderGuid,
    deviceCode: "IPAD-2",
  });
  const outsideDevice = presenter(outsideDevicePort, {
    permissionCodes: [
      REMOTE_HISTORY_VIEW_PERMISSION,
      REMOTE_HISTORY_REPRINT_PERMISSION,
    ],
    reprintPort,
  });
  await outsideDevice.refresh();
  assert.equal(outsideDevice.state.details.kind, "ready");
  assert.equal(outsideDevice.capabilities.reprint, true);
  await outsideDevice.reprintSelected();
  assert.deepEqual(reprintPort.orderGuids, [secondRow.orderGuid]);

  reprintPort.canReprintImpl = () => false;
  const ineligible = presenter(port, {
    permissionCodes: [
      REMOTE_HISTORY_VIEW_PERMISSION,
      REMOTE_HISTORY_REPRINT_PERMISSION,
    ],
    reprintPort,
  });
  await ineligible.refresh();
  assert.equal(ineligible.capabilities.reprint, false);
  await ineligible.reprintSelected();
  assert.deepEqual(reprintPort.orderGuids, [secondRow.orderGuid]);
  reprintPort.canReprintImpl = () => true;

  const failing = presenter(port, {
    permissionCodes: [
      REMOTE_HISTORY_VIEW_PERMISSION,
      REMOTE_HISTORY_REPRINT_PERMISSION,
    ],
    reprintPort,
  });
  await failing.refresh();
  const rowsBefore = failing.state.rows;
  const detailsBefore = failing.state.details;
  reprintPort.reprintImpl = async () => {
    throw new Error("printer unavailable");
  };
  await failing.reprintSelected();

  assert.equal(failing.state.kind, "ready");
  assert.equal(failing.state.rows, rowsBefore);
  assert.equal(failing.state.details, detailsBefore);
  assert.deepEqual(failing.state.reprint, {
    kind: "failed",
    orderGuid: firstRow.orderGuid,
    errorCode: "remote-history-reprint-failed",
  });
});

test("连续点击同一远程订单只创建一个在途重打动作", async () => {
  const port = new MemoryPort();
  const reprintPort = new MemoryReprintPort();
  const pending = deferred<void>();
  port.listImpl = async () => [firstRow];
  port.detailsImpl = async () => firstDetails;
  reprintPort.reprintImpl = () => pending.promise;
  const value = presenter(port, {
    permissionCodes: [
      REMOTE_HISTORY_VIEW_PERMISSION,
      REMOTE_HISTORY_REPRINT_PERMISSION,
    ],
    reprintPort,
  });
  await value.refresh();

  const first = value.reprintSelected();
  const duplicate = value.reprintSelected();

  assert.equal(first, duplicate);
  assert.deepEqual(reprintPort.orderGuids, [firstRow.orderGuid]);
  pending.resolve();
  await first;
  assert.equal(value.state.reprint.kind, "succeeded");
});

test("缺 View 权限、离线或 runtime 未接线时不调用远端", async () => {
  const port = new MemoryPort();
  const unauthorized = presenter(port, { permissionCodes: [] });
  await unauthorized.refresh();
  assert.equal(unauthorized.state.kind, "unauthorized");

  const offline = presenter(port, { online: false });
  await offline.refresh();
  assert.equal(offline.state.kind, "offline");

  const unavailable = presenter(null);
  await unavailable.refresh();
  assert.equal(unavailable.state.kind, "unavailable");
  assert.equal(port.queries.length, 0);
});

test("新筛选结果胜出，旧 list 响应不得回流", async () => {
  const port = new MemoryPort();
  const oldRequest = deferred<readonly RemoteOrderHistorySummary[]>();
  const newRequest = deferred<readonly RemoteOrderHistorySummary[]>();
  let call = 0;
  port.listImpl = () => (call++ === 0 ? oldRequest.promise : newRequest.promise);
  const value = presenter(port);

  const oldRefresh = value.refresh();
  value.setFilters({
    deviceCode: null,
    keyword: "new",
    soldFromIso: "2026-07-26T00:00:00Z",
    soldToIso: "2026-07-26T23:59:59.999Z",
  });
  const newRefresh = value.refresh();
  newRequest.resolve([secondRow]);
  await newRefresh;
  oldRequest.resolve([firstRow]);
  await oldRefresh;

  assert.deepEqual(value.state.rows.map((row) => row.orderGuid), [
    secondRow.orderGuid,
  ]);
  assert.equal(port.queries[1]?.storeCode, "S1");
  assert.equal(port.queries[1]?.take, 100);
});

test("快速切换选中订单时旧详情不得覆盖新详情", async () => {
  const port = new MemoryPort();
  port.listImpl = async () => [firstRow, secondRow];
  const firstRequest = deferred<RemoteOrderHistoryDetails | null>();
  const secondRequest = deferred<RemoteOrderHistoryDetails | null>();
  port.detailsImpl = (orderGuid) =>
    orderGuid === firstRow.orderGuid
      ? firstRequest.promise
      : secondRequest.promise;
  const value = presenter(port);

  const refresh = value.refresh();
  // 等待列表落地并触发首单详情请求，再模拟用户立即切换到第二单。
  await Promise.resolve();
  await Promise.resolve();
  const selectSecond = value.selectOrder(secondRow.orderGuid);
  secondRequest.resolve({ ...firstDetails, orderGuid: secondRow.orderGuid });
  await selectSecond;
  firstRequest.resolve(firstDetails);
  await refresh;

  assert.equal(value.state.selectedOrderGuid, secondRow.orderGuid);
  assert.equal(
    value.state.details.kind === "ready"
      ? value.state.details.value.orderGuid
      : null,
    secondRow.orderGuid,
  );
});

test("详情 200/null 显示 not-found；destroy 后异步结果不发布", async () => {
  const port = new MemoryPort();
  port.listImpl = async () => [firstRow];
  port.detailsImpl = async () => null;
  const notFound = presenter(port);
  await notFound.refresh();
  assert.equal(notFound.state.details.kind, "not-found");

  const pendingPort = new MemoryPort();
  const pending = deferred<readonly RemoteOrderHistorySummary[]>();
  pendingPort.listImpl = () => pending.promise;
  const destroyed = presenter(pendingPort);
  let notifications = 0;
  destroyed.subscribe(() => {
    notifications += 1;
  });
  const refresh = destroyed.refresh();
  const beforeDestroy = destroyed.state;
  destroyed.destroy();
  pending.resolve([firstRow]);
  await refresh;
  assert.equal(destroyed.state, beforeDestroy);
  assert.equal(notifications, 1);
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}
