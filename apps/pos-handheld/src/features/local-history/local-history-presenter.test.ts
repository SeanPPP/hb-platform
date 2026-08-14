import assert from "node:assert/strict";
import test from "node:test";

import type {
  LocalHistoryDetails,
  LocalHistoryPage,
  LocalHistoryPort,
  LocalHistoryQuery,
  LocalHistoryReceiptPreviewPort,
  LocalHistoryReprintPort,
  LocalHistorySummary,
} from "./local-history-domain";
import {
  LOCAL_HISTORY_REPRINT_PERMISSION,
  LOCAL_HISTORY_VIEW_PERMISSION,
  LocalHistoryPresenter,
  hasLocalHistoryReprintPermission,
  hasLocalHistoryViewPermission,
  localHistoryBusinessDayRange,
} from "./local-history-presenter";

import type { EscPosDocument } from "@/features/receipts/receipt-document";

const firstSummary: LocalHistorySummary = {
  orderGuid: "order-42",
  localSequence: 42,
  soldAtIso: "2026-07-31T01:02:03.000Z",
  cashierName: "Alice",
  state: "PendingSync",
  totalCents: 1_234,
  discountCents: 34,
  actualAmountCents: 1_200,
  lineCount: 1,
  paymentSummary: "Card",
};

const secondSummary: LocalHistorySummary = {
  ...firstSummary,
  orderGuid: "order-41",
  localSequence: 41,
  cashierName: "Bob",
};

const firstDetails: LocalHistoryDetails = {
  orderGuid: firstSummary.orderGuid,
  localSequence: firstSummary.localSequence,
  soldAtIso: firstSummary.soldAtIso,
  cashierName: firstSummary.cashierName,
  state: firstSummary.state,
  totalCents: firstSummary.totalCents,
  discountCents: firstSummary.discountCents,
  actualAmountCents: firstSummary.actualAmountCents,
  lines: [
    {
      lineId: "line-1",
      productCode: "P1",
      itemNumber: "I1",
      lookupCode: "930001",
      displayName: "Tea",
      quantity: "1",
      unitPriceCents: 1_234,
      discountCents: 34,
      actualAmountCents: 1_200,
      kind: "sale",
    },
  ],
  tenders: [{ method: "card", amountCents: 1_200 }],
};

class MemoryPort implements LocalHistoryPort {
  public readonly queries: LocalHistoryQuery[] = [];
  public readonly detailRequests: string[] = [];
  public listImpl: (query: LocalHistoryQuery) => Promise<LocalHistoryPage> =
    async () => ({ orders: [], nextCursor: null });
  public detailsImpl: (
    orderGuid: string,
  ) => Promise<LocalHistoryDetails | null> = async () => null;

  public list(query: LocalHistoryQuery): Promise<LocalHistoryPage> {
    this.queries.push(query);
    return this.listImpl(query);
  }

  public getDetails(
    orderGuid: string,
  ): Promise<LocalHistoryDetails | null> {
    this.detailRequests.push(orderGuid);
    return this.detailsImpl(orderGuid);
  }
}

class MemoryReprintPort implements LocalHistoryReprintPort {
  public readonly orderGuids: string[] = [];
  public reprintImpl: (orderGuid: string) => Promise<void> = async () =>
    undefined;

  public reprintExistingOrder(orderGuid: string): Promise<void> {
    this.orderGuids.push(orderGuid);
    return this.reprintImpl(orderGuid);
  }
}

const receiptPreviewDocument: EscPosDocument = {
  paper: "80mm",
  lines: [
    {
      kind: "text",
      text: "HOT BARGAIN",
      align: "center",
      bold: true,
    },
  ],
};

class MemoryReceiptPreviewPort implements LocalHistoryReceiptPreviewPort {
  public readonly orderGuids: string[] = [];
  public previewImpl: (
    orderGuid: string,
  ) => Promise<EscPosDocument | null> = async () => null;

  public getPreview(
    orderGuid: string,
  ): Promise<EscPosDocument | null> {
    this.orderGuids.push(orderGuid);
    return this.previewImpl(orderGuid);
  }
}

function presenter(
  port: LocalHistoryPort,
  overrides: Partial<{
    permissionCodes: readonly string[];
    receiptPreviewPort: LocalHistoryReceiptPreviewPort | null;
    reprintPort: LocalHistoryReprintPort | null;
    pageSize: number;
  }> = {},
): LocalHistoryPresenter {
  return new LocalHistoryPresenter({
    port,
    permissionCodes:
      overrides.permissionCodes ?? [LOCAL_HISTORY_VIEW_PERMISSION],
    receiptPreviewPort: overrides.receiptPreviewPort ?? null,
    reprintPort: overrides.reprintPort ?? null,
    ...(overrides.pageSize === undefined
      ? {}
      : { pageSize: overrides.pageSize }),
    businessTimeZone: "Australia/Brisbane",
    now: () => new Date("2026-07-31T00:30:00.000Z"),
  });
}

test("默认按注入业务时区查询当天、每页 50，并自动读取首单安全详情", async () => {
  const port = new MemoryPort();
  port.listImpl = async () => ({
    orders: [firstSummary],
    nextCursor: null,
  });
  port.detailsImpl = async () => firstDetails;
  const value = presenter(port);

  await value.refresh();

  assert.deepEqual(port.queries, [
    {
      soldFromIso: "2026-07-30T14:00:00.000Z",
      soldToIso: "2026-07-31T13:59:59.999Z",
      keyword: null,
      cursor: null,
      limit: 50,
    },
  ]);
  assert.equal(value.state.businessTimeZone, "Australia/Brisbane");
  assert.equal(value.state.kind, "ready");
  assert.equal(value.state.selectedOrderGuid, firstSummary.orderGuid);
  assert.deepEqual(value.state.details, {
    kind: "ready",
    orderGuid: firstSummary.orderGuid,
    value: firstDetails,
  });
  assert.equal(value.state.hasMore, false);
  assert.deepEqual(value.capabilities, {
    refund: false,
    recall: false,
    reprint: false,
  });
});

test("History.View 可加载脱敏小票预览，不依赖重打权限或打印机动作", async () => {
  const port = new MemoryPort();
  const receiptPreviewPort = new MemoryReceiptPreviewPort();
  port.listImpl = async () => ({
    orders: [firstSummary],
    nextCursor: null,
  });
  port.detailsImpl = async () => firstDetails;
  receiptPreviewPort.previewImpl = async () => receiptPreviewDocument;
  const value = presenter(port, { receiptPreviewPort });

  await value.refresh();

  assert.deepEqual(receiptPreviewPort.orderGuids, [firstSummary.orderGuid]);
  assert.deepEqual(value.state.receiptPreview, {
    kind: "ready",
    orderGuid: firstSummary.orderGuid,
    document: receiptPreviewDocument,
  });
  assert.equal(value.capabilities.reprint, false);
});

test("业务日期 helper 使用 IANA 时区的 DST-safe UTC 闭区间", () => {
  assert.deepEqual(
    localHistoryBusinessDayRange(
      "2026-10-04",
      "2026-10-04",
      "Australia/Sydney",
    ),
    {
      soldFromIso: "2026-10-03T14:00:00.000Z",
      soldToIso: "2026-10-04T12:59:59.999Z",
      keyword: null,
    },
  );
  assert.deepEqual(
    localHistoryBusinessDayRange(
      "2026-04-05",
      "2026-04-05",
      "Australia/Sydney",
    ),
    {
      soldFromIso: "2026-04-04T13:00:00.000Z",
      soldToIso: "2026-04-05T13:59:59.999Z",
      keyword: null,
    },
  );
  assert.equal(
    localHistoryBusinessDayRange(
      "2026-02-30",
      "2026-03-01",
      "Australia/Brisbane",
    ),
    null,
  );
});

test("History.View 门禁阻止任何读取，权限 helper 只精确接受独立权限", async () => {
  const port = new MemoryPort();
  const value = presenter(port, {
    permissionCodes: [LOCAL_HISTORY_REPRINT_PERMISSION],
  });

  await value.refresh();

  assert.equal(value.state.kind, "unauthorized");
  assert.equal(port.queries.length, 0);
  assert.equal(
    hasLocalHistoryViewPermission([LOCAL_HISTORY_VIEW_PERMISSION]),
    true,
  );
  assert.equal(
    hasLocalHistoryViewPermission([` ${LOCAL_HISTORY_VIEW_PERMISSION} `]),
    true,
  );
  assert.equal(
    hasLocalHistoryViewPermission([LOCAL_HISTORY_REPRINT_PERMISSION]),
    false,
  );
  assert.equal(
    hasLocalHistoryReprintPermission([LOCAL_HISTORY_REPRINT_PERMISSION]),
    true,
  );
});

test("新筛选 generation 胜出，旧列表和旧详情均不得回流", async () => {
  const port = new MemoryPort();
  const oldList = deferred<LocalHistoryPage>();
  const newList = deferred<LocalHistoryPage>();
  let call = 0;
  port.listImpl = () => (call++ === 0 ? oldList.promise : newList.promise);
  port.detailsImpl = async (orderGuid) => ({
    ...firstDetails,
    orderGuid,
    localSequence:
      orderGuid === secondSummary.orderGuid
        ? secondSummary.localSequence
        : firstSummary.localSequence,
    cashierName:
      orderGuid === secondSummary.orderGuid
        ? secondSummary.cashierName
        : firstSummary.cashierName,
  });
  const value = presenter(port);

  const staleRefresh = value.refresh();
  value.setFilters({
    soldFromIso: "2026-07-29T14:00:00.000Z",
    soldToIso: "2026-07-30T13:59:59.999Z",
    keyword: "new",
  });
  const currentRefresh = value.refresh();
  newList.resolve({ orders: [secondSummary], nextCursor: null });
  await currentRefresh;
  oldList.resolve({ orders: [firstSummary], nextCursor: null });
  await staleRefresh;

  assert.deepEqual(
    value.state.rows.map((row) => row.orderGuid),
    [secondSummary.orderGuid],
  );
  assert.equal(value.state.selectedOrderGuid, secondSummary.orderGuid);
  assert.equal(port.queries[1]?.keyword, "new");
});

test("loadMore 使用严格 sequence cursor 并保留已有 rows", async () => {
  const port = new MemoryPort();
  port.listImpl = async (query) =>
    query.cursor === null
      ? { orders: [firstSummary, secondSummary], nextCursor: 41 }
      : {
          orders: [
            {
              ...secondSummary,
              orderGuid: "order-40",
              localSequence: 40,
            },
          ],
          nextCursor: null,
        };
  const value = presenter(port);
  await value.refresh();

  const loading = value.loadMore();
  assert.equal(value.state.kind, "ready");
  assert.equal(value.state.loadingMore, true);
  await loading;

  assert.deepEqual(
    value.state.rows.map((row) => row.localSequence),
    [42, 41, 40],
  );
  assert.equal(port.queries[1]?.cursor, 41);
  assert.equal(value.state.loadingMore, false);
  assert.equal(value.state.hasMore, false);
  assert.equal(value.state.nextCursor, null);
});

test("快速切换详情时，仅当前订单 generation 可发布", async () => {
  const port = new MemoryPort();
  port.listImpl = async () => ({
    orders: [firstSummary, secondSummary],
    nextCursor: null,
  });
  const firstRequest = deferred<LocalHistoryDetails | null>();
  const secondRequest = deferred<LocalHistoryDetails | null>();
  port.detailsImpl = (orderGuid) =>
    orderGuid === firstSummary.orderGuid
      ? firstRequest.promise
      : secondRequest.promise;
  const value = presenter(port);

  const refresh = value.refresh();
  for (
    let attempt = 0;
    attempt < 10 && port.detailRequests.length === 0;
    attempt += 1
  ) {
    await Promise.resolve();
  }
  assert.deepEqual(port.detailRequests, [firstSummary.orderGuid]);
  const selectSecond = value.selectOrder(secondSummary.orderGuid);
  secondRequest.resolve({
    ...firstDetails,
    orderGuid: secondSummary.orderGuid,
    localSequence: secondSummary.localSequence,
    cashierName: secondSummary.cashierName,
  });
  await selectSecond;
  firstRequest.resolve(firstDetails);
  await refresh;

  assert.equal(value.state.selectedOrderGuid, secondSummary.orderGuid);
  assert.equal(
    value.state.details.kind === "ready"
      ? value.state.details.value.orderGuid
      : null,
    secondSummary.orderGuid,
  );
});

test("快速切换订单时，过期小票预览结果不得覆盖当前订单", async () => {
  const port = new MemoryPort();
  const receiptPreviewPort = new MemoryReceiptPreviewPort();
  const firstPreview = deferred<EscPosDocument | null>();
  const secondPreview = deferred<EscPosDocument | null>();
  const secondDocument: EscPosDocument = {
    paper: "58mm",
    lines: [{ kind: "text", text: "ORDER 41", align: "center", bold: true }],
  };
  port.listImpl = async () => ({
    orders: [firstSummary, secondSummary],
    nextCursor: null,
  });
  port.detailsImpl = async (orderGuid) => ({
    ...firstDetails,
    orderGuid,
    localSequence: orderGuid === secondSummary.orderGuid ? 41 : 42,
  });
  receiptPreviewPort.previewImpl = (orderGuid) =>
    orderGuid === firstSummary.orderGuid
      ? firstPreview.promise
      : secondPreview.promise;
  const value = presenter(port, { receiptPreviewPort });

  const refresh = value.refresh();
  for (
    let attempt = 0;
    attempt < 10 && receiptPreviewPort.orderGuids.length === 0;
    attempt += 1
  ) {
    await Promise.resolve();
  }
  const selectSecond = value.selectOrder(secondSummary.orderGuid);
  secondPreview.resolve(secondDocument);
  await selectSecond;
  firstPreview.resolve(receiptPreviewDocument);
  await refresh;

  assert.deepEqual(value.state.receiptPreview, {
    kind: "ready",
    orderGuid: secondSummary.orderGuid,
    document: secondDocument,
  });
});

test("空列表保持明确 empty；详情读取失败不丢失列表且关闭重打", async () => {
  const emptyPort = new MemoryPort();
  const empty = presenter(emptyPort);
  await empty.refresh();
  assert.equal(empty.state.kind, "empty");
  assert.equal(empty.state.details.kind, "idle");
  assert.deepEqual(emptyPort.detailRequests, []);

  const failedPort = new MemoryPort();
  failedPort.listImpl = async () => ({
    orders: [firstSummary],
    nextCursor: null,
  });
  failedPort.detailsImpl = async () => {
    throw new Error("sqlite read failed");
  };
  const failed = presenter(failedPort, {
    permissionCodes: [
      LOCAL_HISTORY_VIEW_PERMISSION,
      LOCAL_HISTORY_REPRINT_PERMISSION,
    ],
    reprintPort: new MemoryReprintPort(),
  });
  await failed.refresh();

  assert.equal(failed.state.kind, "ready");
  assert.deepEqual(
    failed.state.rows.map((row) => row.orderGuid),
    [firstSummary.orderGuid],
  );
  assert.deepEqual(failed.state.details, {
    kind: "failed",
    orderGuid: firstSummary.orderGuid,
    errorCode: "local-history-details-failed",
  });
  assert.equal(failed.capabilities.reprint, false);
});

test("History.Reprint 仅对已加载当前详情开放，并合并双击中的重打副作用", async () => {
  const port = new MemoryPort();
  const reprintPort = new MemoryReprintPort();
  const printed = deferred<void>();
  port.listImpl = async () => ({
    orders: [firstSummary],
    nextCursor: null,
  });
  port.detailsImpl = async () => firstDetails;
  reprintPort.reprintImpl = () => printed.promise;
  const value = presenter(port, {
    permissionCodes: [
      LOCAL_HISTORY_VIEW_PERMISSION,
      LOCAL_HISTORY_REPRINT_PERMISSION,
    ],
    reprintPort,
  });
  await value.refresh();

  assert.equal(value.capabilities.reprint, true);
  const first = value.reprintSelected();
  const duplicate = value.reprintSelected();

  assert.equal(first, duplicate);
  assert.deepEqual(reprintPort.orderGuids, [firstSummary.orderGuid]);
  assert.deepEqual(value.state.reprint, {
    kind: "submitting",
    orderGuid: firstSummary.orderGuid,
  });

  printed.resolve();
  await first;
  assert.deepEqual(value.state.reprint, {
    kind: "succeeded",
    orderGuid: firstSummary.orderGuid,
  });
});

test("缺 Reprint 权限时不调用 port；destroy 后所有异步结果失效", async () => {
  const port = new MemoryPort();
  const reprintPort = new MemoryReprintPort();
  port.listImpl = async () => ({
    orders: [firstSummary],
    nextCursor: null,
  });
  port.detailsImpl = async () => firstDetails;
  const missingPermission = presenter(port, { reprintPort });
  await missingPermission.refresh();
  await missingPermission.reprintSelected();
  assert.deepEqual(reprintPort.orderGuids, []);
  assert.equal(missingPermission.state.reprint.kind, "unavailable");

  const pendingPort = new MemoryPort();
  const pending = deferred<LocalHistoryPage>();
  pendingPort.listImpl = () => pending.promise;
  const destroyed = presenter(pendingPort);
  let notifications = 0;
  destroyed.subscribe(() => {
    notifications += 1;
  });
  const refresh = destroyed.refresh();
  const beforeDestroy = destroyed.state;
  destroyed.destroy();
  pending.resolve({ orders: [firstSummary], nextCursor: null });
  await refresh;
  assert.equal(destroyed.state, beforeDestroy);
  assert.equal(notifications, 1);
});

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}
