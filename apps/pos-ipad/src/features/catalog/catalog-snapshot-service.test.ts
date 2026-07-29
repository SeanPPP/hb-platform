import assert from "node:assert/strict";
import test from "node:test";

import {
  CatalogSnapshotFailure,
  CatalogSnapshotService,
  mapCatalogLookupToStagedItem,
  type CatalogSnapshotStoragePort,
  type CatalogStagedItem,
  type CatalogSyncRemotePort,
} from "./catalog-snapshot-service";
import type {
  CatalogLookupItem,
  VerifiedCatalogSyncPage,
} from "./hbpos-catalog-remote";

import { HbposApiError } from "@/core/api/hbpos-api";

const item = (
  lookupCode: string,
  overrides: Partial<CatalogLookupItem> = {},
): CatalogLookupItem => ({
  storeCode: "S1",
  productCode: `P-${lookupCode}`,
  referenceCode: null,
  displayName: `商品 ${lookupCode}`,
  lookupCode,
  lookupCodeNormalized: lookupCode.toUpperCase(),
  itemNumber: `I-${lookupCode}`,
  barcode: `SOURCE-${lookupCode}`,
  retailPrice: 1,
  priceSource: 0,
  priceSourceLabel: "product",
  quantityFactor: 1,
  updatedAt: "2026-07-28T00:00:00.000Z",
  rowVersion: "ROW",
  productImage: null,
  discountRate: null,
  isSpecialProduct: false,
  ...overrides,
});

const page = (input: Readonly<{
  cursor: string | null;
  nextCursor: string | null;
  items: readonly CatalogLookupItem[];
  totalCount: number;
  catalogVersion?: string;
  storeCode?: string;
  hasMore?: boolean;
}>): VerifiedCatalogSyncPage => ({
  storeCode: input.storeCode ?? "S1",
  generatedAt: "2026-07-28T00:00:00.000Z",
  cursor: input.cursor,
  items: input.items,
  deletedLookups: [],
  nextCursor: input.nextCursor,
  hasMore: input.hasMore ?? input.nextCursor !== null,
  totalCount: input.totalCount,
  catalogVersion: input.catalogVersion ?? "catalog-v2",
  pageChecksum: `verified-${input.cursor ?? "first"}`,
});

class MemoryCatalogStorage implements CatalogSnapshotStoragePort {
  public readonly active = new Map<string, readonly CatalogStagedItem[]>();
  public readonly staged = new Map<string, readonly CatalogStagedItem[]>();
  public readonly appendBatchSizes: number[] = [];
  public activated: string | null = null;

  public async beginStaging(snapshot: { snapshotId: string }): Promise<void> {
    this.staged.set(snapshot.snapshotId, []);
  }

  public async appendPage(snapshotId: string, items: readonly CatalogStagedItem[]): Promise<void> {
    this.appendBatchSizes.push(items.length);
    this.staged.set(snapshotId, [...(this.staged.get(snapshotId) ?? []), ...items]);
  }

  public async replacePromotions(): Promise<void> {}

  public async activate(snapshotId: string, expectedItemCount: number): Promise<void> {
    const staged = this.staged.get(snapshotId) ?? [];
    if (staged.length !== expectedItemCount) throw new Error("staging count mismatch");
    this.active.clear();
    this.active.set(snapshotId, staged);
    this.activated = snapshotId;
  }

  public async discardStaging(snapshotId: string): Promise<void> {
    this.staged.delete(snapshotId);
  }
}

class AbortAfterBeginStorage extends MemoryCatalogStorage {
  public constructor(private readonly controller: AbortController) {
    super();
  }

  public override async beginStaging(snapshot: { snapshotId: string }): Promise<void> {
    await super.beginStaging(snapshot);
    // 中文注释：模拟暂存事务已成功、调用方尚未来得及标记 cleanup 所有权时取消。
    this.controller.abort();
  }
}

function remote(pages: readonly VerifiedCatalogSyncPage[]): CatalogSyncRemotePort {
  return {
    async getPage(input) {
      const result = pages.find((candidate) => candidate.cursor === input.cursor);
      if (!result) throw new Error("unexpected cursor");
      return result;
    },
  };
}

test("固定 lookup 快照分页下载，显式转整数分后才原子切换 active", async () => {
  const storage = new MemoryCatalogStorage();
  storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
  const service = new CatalogSnapshotService(storage, remote([
    page({
      cursor: null,
      nextCursor: "c2",
      items: [item("A", { retailPrice: 12.34 })],
      totalCount: 2,
    }),
    page({
      cursor: "c2",
      nextCursor: null,
      items: [item("B", { productCode: "P-A", retailPrice: 0.05 })],
      totalCount: 2,
    }),
  ]), {
    createSnapshotId: () => "snapshot-v2",
    nowIso: () => "2026-07-28T00:00:00.000Z",
  });

  const events: { step: string; percent: number }[] = [];
  const result = await service.downloadAndActivate({
    storeCode: "S1",
    onProgress(event) {
      events.push({ step: event.step, percent: event.percent });
      throw new Error("观察器故障不得阻断目录刷新");
    },
  });

  assert.equal(storage.activated, "snapshot-v2");
  assert.deepEqual(result, {
    snapshotId: "snapshot-v2",
    catalogVersion: "catalog-v2",
    itemCount: 2,
    activatedAt: "2026-07-28T00:00:00.000Z",
  });
  assert.deepEqual(events, [
    { step: "prepare", percent: 0 },
    { step: "prepare", percent: 100 },
    { step: "products", percent: 0 },
    { step: "products", percent: 50 },
    { step: "products", percent: 100 },
    { step: "promotions", percent: 0 },
    { step: "promotions", percent: 100 },
    { step: "activate", percent: 0 },
    { step: "activate", percent: 100 },
  ]);
  assert.deepEqual(
    storage.active.get("snapshot-v2")?.map((entry) => ({
      lookup: entry.lookupCodeNormalized,
      cents: entry.retailPriceCents,
    })),
    [
      { lookup: "A", cents: 1234 },
      { lookup: "B", cents: 5 },
    ],
  );
});

test("第一页省略版本，后续分页固定使用首包 catalogVersion", async () => {
  const calls: Readonly<{
    storeCode: string;
    cursor: string | null;
    pageSize: number;
    catalogVersion?: string;
  }>[] = [];
  const pages = [
    page({
      cursor: null,
      nextCursor: "c2",
      items: [item("A")],
      totalCount: 2,
      catalogVersion: "catalog-v1:pinned",
    }),
    page({
      cursor: "c2",
      nextCursor: null,
      items: [item("B")],
      totalCount: 2,
      catalogVersion: "catalog-v1:pinned",
    }),
  ] as const;
  const service = new CatalogSnapshotService(
    new MemoryCatalogStorage(),
    {
      async getPage(input) {
        calls.push(input);
        const result = pages.find((candidate) => candidate.cursor === input.cursor);
        if (!result) throw new Error("unexpected cursor");
        return result;
      },
    },
    { createSnapshotId: () => "pinned-version" },
  );

  await service.downloadAndActivate({ storeCode: "S1" });

  assert.equal("catalogVersion" in (calls[0] ?? {}), false);
  assert.equal(calls[1]?.catalogVersion, "catalog-v1:pinned");
});

test("按 WPF 的 5000 条网络页下载，整页验证后最多每 500 条写入并报告真实页数与耗时", async () => {
  const storage = new MemoryCatalogStorage();
  const requestedPages: Parameters<CatalogSyncRemotePort["getPage"]>[0][] = [];
  const controller = new AbortController();
  let nowMilliseconds = 1_000;
  const items = Array.from({ length: 1_201 }, (_, index) => item(`ITEM-${index}`));
  const events: {
    step: string;
    percent: number;
    elapsedMilliseconds?: number;
    completedItemCount?: number;
    completedPageCount?: number;
    totalPageCount?: number;
  }[] = [];
  const service = new CatalogSnapshotService(
    storage,
    {
      async getPage(input) {
        requestedPages.push(input);
        nowMilliseconds += 250;
        return page({
          cursor: null,
          nextCursor: null,
          items,
          totalCount: items.length,
        });
      },
    },
    {
      createSnapshotId: () => "wpf-page-size",
      nowMilliseconds: () => nowMilliseconds,
    },
  );

  await service.downloadAndActivate({
    storeCode: "S1",
    signal: controller.signal,
    onProgress: (event) => events.push(event),
  });

  assert.equal(requestedPages.length, 1);
  assert.equal(requestedPages[0]?.pageSize, 5_000);
  assert.equal(requestedPages[0]?.signal, controller.signal);
  assert.deepEqual(storage.appendBatchSizes, [500, 500, 201]);
  const productEvents = events.filter((event) => event.step === "products");
  assert.deepEqual(
    productEvents.map((event) => ({
      percent: event.percent,
      completedItemCount: event.completedItemCount,
      completedPageCount: event.completedPageCount,
      totalPageCount: event.totalPageCount,
    })),
    [
      { percent: 0, completedItemCount: 0, completedPageCount: 0, totalPageCount: 1 },
      { percent: 41, completedItemCount: 500, completedPageCount: 0, totalPageCount: 1 },
      { percent: 83, completedItemCount: 1_000, completedPageCount: 0, totalPageCount: 1 },
      { percent: 100, completedItemCount: 1_201, completedPageCount: 1, totalPageCount: 1 },
    ],
  );
  assert.equal(events[0]?.step, "prepare");
  assert.equal(events[0]?.percent, 0);
  assert.equal(events[0]?.elapsedMilliseconds, 0);
  assert.equal(
    events.every((event, index) =>
      index === 0
      || (event.elapsedMilliseconds ?? 0) >= (events[index - 1]?.elapsedMilliseconds ?? 0)),
    true,
  );
});

test("空目录在验证并写入暂存后将商品步骤真实推进到百分之百", async () => {
  const events: { step: string; percent: number }[] = [];
  const service = new CatalogSnapshotService(
    new MemoryCatalogStorage(),
    remote([page({ cursor: null, nextCursor: null, items: [], totalCount: 0 })]),
    { createSnapshotId: () => "empty-catalog" },
  );

  await service.downloadAndActivate({
    storeCode: "S1",
    onProgress: (event) => events.push({ step: event.step, percent: event.percent }),
  });

  assert.deepEqual(
    events.filter((event) => event.step === "products"),
    [
      { step: "products", percent: 0 },
      { step: "products", percent: 100 },
    ],
  );
});

test("远端目录版本在开始暂存前严格拒绝空白、控制字符与过长值", async () => {
  const invalidVersions = ["", " catalog-v2", "catalog-v2 ", "catalog\u0000v2", "v".repeat(513)];
  for (const [index, catalogVersion] of invalidVersions.entries()) {
    const storage = new MemoryCatalogStorage();
    const service = new CatalogSnapshotService(
      storage,
      remote([page({
        cursor: null,
        nextCursor: null,
        items: [item("A")],
        totalCount: 1,
        catalogVersion,
      })]),
      { createSnapshotId: () => `invalid-version-${index}` },
    );

    await assert.rejects(
      () => service.downloadAndActivate({ storeCode: "S1" }),
      catalogError("CATALOG_SNAPSHOT_VERSION_INVALID"),
    );
    assert.equal(storage.staged.size, 0);
  }
});

test("未闭合分页即使已达到 total 也不报告商品步骤百分之百", async () => {
  const events: { step: string; percent: number }[] = [];
  const service = new CatalogSnapshotService(
    new MemoryCatalogStorage(),
    remote([page({
      cursor: null,
      nextCursor: "missing-page",
      items: [item("A")],
      totalCount: 1,
    })]),
    { createSnapshotId: () => "unclosed-page" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({
      storeCode: "S1",
      onProgress: (event) => events.push({ step: event.step, percent: event.percent }),
    }),
    /cursor/i,
  );
  assert.deepEqual(
    events.filter((event) => event.step === "products"),
    [
      { step: "products", percent: 0 },
      { step: "products", percent: 99 },
    ],
  );
});

test("最终页数量与服务端 total 不闭合时不报告商品步骤百分之百", async () => {
  const productPercents: number[] = [];
  const service = new CatalogSnapshotService(
    new MemoryCatalogStorage(),
    remote([page({
      cursor: null,
      nextCursor: null,
      items: [item("A")],
      totalCount: 2,
    })]),
    { createSnapshotId: () => "mismatched-final-count" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({
      storeCode: "S1",
      onProgress: (event) => {
        if (event.step === "products") productPercents.push(event.percent);
      },
    }),
    catalogError("CATALOG_ITEM_COUNT_MISMATCH"),
  );
  assert.deepEqual(productPercents, [0]);
});

test("取消在暂存后只清理 staging，既有 active 目录保持可用", async () => {
  const controller = new AbortController();
  const storage = new MemoryCatalogStorage();
  storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
  const service = new CatalogSnapshotService(
    storage,
    remote([page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1 })]),
    { createSnapshotId: () => "cancelled" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({
      storeCode: "S1",
      signal: controller.signal,
      onProgress: (event) => {
        if (event.step === "products" && event.percent === 0) controller.abort();
      },
    }),
    /cancelled/i,
  );
  assert.equal(storage.active.has("old"), true);
  assert.equal(storage.staged.has("cancelled"), false);
});

test("同一取消信号穿透所有分页请求，在途取消后清理 staging 并释放串行队列", async () => {
  const controller = new AbortController();
  const secondPageStarted = deferred<void>();
  const capturedSignals: (AbortSignal | undefined)[] = [];
  let calls = 0;
  let nextId = 0;
  const storage = new MemoryCatalogStorage();
  storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
  const service = new CatalogSnapshotService(
    storage,
    {
      async getPage(input) {
        calls += 1;
        capturedSignals.push(input.signal);
        if (calls === 1) {
          return page({
            cursor: null,
            nextCursor: "c2",
            items: [item("A")],
            totalCount: 2,
          });
        }
        if (calls === 2) {
          secondPageStarted.resolve();
          return new Promise<VerifiedCatalogSyncPage>((_resolve, reject) => {
            input.signal?.addEventListener(
              "abort",
              () => reject(new Error("Catalog request cancelled.")),
              { once: true },
            );
          });
        }
        return page({
          cursor: null,
          nextCursor: null,
          items: [item("RECOVERED")],
          totalCount: 1,
        });
      },
    },
    { createSnapshotId: () => `cancel-in-flight-${++nextId}` },
  );

  const cancelled = service.downloadAndActivate({
    storeCode: "S1",
    signal: controller.signal,
  });
  await secondPageStarted.promise;
  controller.abort();
  await assert.rejects(() => cancelled, /cancel/i);

  assert.deepEqual(capturedSignals.slice(0, 2), [
    controller.signal,
    controller.signal,
  ]);
  assert.equal(storage.active.has("old"), true);
  assert.equal(storage.staged.has("cancel-in-flight-1"), false);

  const recovered = await service.downloadAndActivate({ storeCode: "S1" });
  assert.equal(recovered.snapshotId, "cancel-in-flight-2");
  assert.equal(storage.activated, "cancel-in-flight-2");
});

test("beginStaging 成功瞬间取消仍会清理 staging", async () => {
  const controller = new AbortController();
  const storage = new AbortAfterBeginStorage(controller);
  storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
  const service = new CatalogSnapshotService(
    storage,
    remote([page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1 })]),
    { createSnapshotId: () => "abort-after-begin" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({ storeCode: "S1", signal: controller.signal }),
    /cancelled/i,
  );
  assert.equal(storage.active.has("old"), true);
  assert.equal(storage.staged.has("abort-after-begin"), false);
});

test("同一快照服务串行刷新，后发请求不会越过在途激活", async () => {
  const gate = deferred<void>();
  let calls = 0;
  let nextId = 0;
  const storage = new RecordingActivationStorage();
  const service = new CatalogSnapshotService(
    storage,
    {
      async getPage() {
        calls += 1;
        if (calls === 1) await gate.promise;
        return page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1 });
      },
    },
    { createSnapshotId: () => `serial-${++nextId}` },
  );

  const first = service.downloadAndActivate({ storeCode: "S1" });
  const second = service.downloadAndActivate({ storeCode: "S1" });
  await Promise.resolve();
  assert.equal(calls, 1);
  gate.resolve();
  await Promise.all([first, second]);
  assert.deepEqual(storage.activationOrder, ["serial-1", "serial-2"]);
});

test("后置运行时重载未完成时，下一次刷新不会越过同一串行临界区", async () => {
  const afterActivateEntered = deferred<void>();
  const releaseAfterActivate = deferred<void>();
  const firstEvents: { step: string; percent: number }[] = [];
  let remoteCalls = 0;
  let nextId = 0;
  const storage = new RecordingActivationStorage();
  const service = new CatalogSnapshotService(
    storage,
    {
      async getPage() {
        remoteCalls += 1;
        return page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1 });
      },
    },
    { createSnapshotId: () => `after-activate-${++nextId}` },
  );

  const first = service.downloadAndActivate({
    storeCode: "S1",
    onProgress: (event) => firstEvents.push(event),
    afterActivate: async () => {
      afterActivateEntered.resolve();
      await releaseAfterActivate.promise;
    },
  });
  await afterActivateEntered.promise;
  const second = service.downloadAndActivate({ storeCode: "S1" });
  await Promise.resolve();

  assert.equal(remoteCalls, 1);
  assert.deepEqual(storage.activationOrder, ["after-activate-1"]);
  assert.equal(
    firstEvents.some((event) => event.step === "activate" && event.percent === 100),
    false,
  );

  releaseAfterActivate.resolve();
  await Promise.all([first, second]);
  assert.equal(remoteCalls, 2);
  assert.deepEqual(storage.activationOrder, ["after-activate-1", "after-activate-2"]);
  assert.equal(
    firstEvents.some((event) => event.step === "activate" && event.percent === 100),
    true,
  );
});

test("后置回调失败不会把已提交 active 当作切换前失败清理", async () => {
  const events: { step: string; percent: number }[] = [];
  const storage = new MemoryCatalogStorage();
  const service = new CatalogSnapshotService(
    storage,
    remote([page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1 })]),
    { createSnapshotId: () => "post-activation-failure" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({
      storeCode: "S1",
      onProgress: (event) => events.push(event),
      afterActivate: () => {
        throw new Error("runtime reload failed");
      },
    }),
    /runtime reload failed/,
  );
  assert.equal(storage.activated, "post-activation-failure");
  assert.equal(storage.staged.has("post-activation-failure"), true);
  assert.equal(
    events.some((event) => event.step === "activate" && event.percent === 100),
    false,
  );
});

test("分页总数、版本、门店或 continuation 不一致时返回稳定校验码并清理 staging", async () => {
  const cases: readonly Readonly<{
    pages: readonly VerifiedCatalogSyncPage[];
    code: string;
  }>[] = [
    {
      pages: [page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 2 })],
      code: "CATALOG_ITEM_COUNT_MISMATCH",
    },
    {
      pages: [
        page({ cursor: null, nextCursor: "c2", items: [item("A")], totalCount: 2 }),
        page({ cursor: "c2", nextCursor: null, items: [item("B")], totalCount: 2, catalogVersion: "catalog-v3" }),
      ],
      code: "CATALOG_SNAPSHOT_VERSION_CHANGED",
    },
    {
      pages: [
        page({ cursor: null, nextCursor: "c2", items: [item("A")], totalCount: 2 }),
        page({ cursor: "c2", nextCursor: null, items: [item("B")], totalCount: 3 }),
      ],
      code: "CATALOG_SNAPSHOT_TOTAL_CHANGED",
    },
    {
      pages: [page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1, storeCode: "OTHER" })],
      code: "CATALOG_STORE_MISMATCH",
    },
    {
      pages: [page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1, hasMore: true })],
      code: "CATALOG_PAGINATION_INVALID",
    },
    {
      pages: [
        page({ cursor: null, nextCursor: "c2", items: [item("A")], totalCount: 3 }),
        page({ cursor: "c2", nextCursor: "c2", items: [item("B")], totalCount: 3 }),
      ],
      code: "CATALOG_CURSOR_REPEATED",
    },
  ];

  for (const [index, entry] of cases.entries()) {
    const storage = new MemoryCatalogStorage();
    storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
    const service = new CatalogSnapshotService(storage, remote(entry.pages), {
      createSnapshotId: () => `failed-${index}`,
    });

    await assert.rejects(
      () => service.downloadAndActivate({ storeCode: "S1" }),
      catalogError(entry.code),
    );
    assert.equal(storage.active.has("old"), true);
    assert.equal(storage.staged.has(`failed-${index}`), false);
  }
});

test("服务端页 cursor 与请求不一致时返回稳定校验码且不开始 staging", async () => {
  const storage = new MemoryCatalogStorage();
  const service = new CatalogSnapshotService(
    storage,
    {
      async getPage() {
        return page({
          cursor: "unexpected",
          nextCursor: null,
          items: [item("A")],
          totalCount: 1,
        });
      },
    },
    { createSnapshotId: () => "cursor-mismatch" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({ storeCode: "S1" }),
    catalogError("CATALOG_CURSOR_MISMATCH"),
  );
  assert.equal(storage.staged.size, 0);
});

test("目录校验异常携带安全分页上下文且仍清理 staging", async () => {
  const storage = new MemoryCatalogStorage();
  const service = new CatalogSnapshotService(
    storage,
    remote([
      page({
        cursor: null,
        nextCursor: "c2",
        items: [item("A")],
        totalCount: 2,
      }),
      page({
        cursor: "c2",
        nextCursor: null,
        items: [item("B")],
        totalCount: 2,
        catalogVersion: "catalog-v3",
      }),
    ]),
    { createSnapshotId: () => "diagnostic" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({ storeCode: "S1" }),
    (error: unknown) => {
      assert.ok(error instanceof CatalogSnapshotFailure);
      assert.deepEqual(error.context, {
        code: "CATALOG_SNAPSHOT_VERSION_CHANGED",
        pageNumber: 2,
        completedItemCount: 1,
        totalItemCount: 2,
      });
      return true;
    },
  );
  assert.equal(storage.staged.has("diagnostic"), false);
});

test("固定快照过期保留服务端精确错误码、HTTP 状态和已完成位置", async () => {
  const storage = new MemoryCatalogStorage();
  let requestCount = 0;
  const service = new CatalogSnapshotService(
    storage,
    {
      async getPage() {
        requestCount += 1;
        if (requestCount === 1) {
          return page({
            cursor: null,
            nextCursor: "c2",
            items: [item("A")],
            totalCount: 2,
          });
        }
        throw new HbposApiError("response body must not enter diagnostics", {
          kind: "envelope",
          code: "CATALOG_SNAPSHOT_EXPIRED",
          status: 409,
        });
      },
    },
    { createSnapshotId: () => "expired-diagnostic" },
  );

  await assert.rejects(
    () => service.downloadAndActivate({ storeCode: "S1" }),
    (error: unknown) => {
      assert.ok(error instanceof CatalogSnapshotFailure);
      assert.deepEqual(error.context, {
        code: "CATALOG_SNAPSHOT_EXPIRED",
        pageNumber: 2,
        completedItemCount: 1,
        totalItemCount: 2,
        httpStatus: 409,
      });
      assert.equal(error.message.includes("response body"), false);
      return true;
    },
  );
  assert.equal(storage.staged.has("expired-diagnostic"), false);
});

class RecordingActivationStorage extends MemoryCatalogStorage {
  public readonly activationOrder: string[] = [];

  public override async activate(snapshotId: string, expectedItemCount: number): Promise<void> {
    await super.activate(snapshotId, expectedItemCount);
    this.activationOrder.push(snapshotId);
  }
}

function deferred<T>(): Readonly<{
  promise: Promise<T>;
  resolve(value: T): void;
}> {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}

test("促销合同校验失败时仅丢弃 staging，既有 active 目录绝不清空", async () => {
  const storage = new MemoryCatalogStorage();
  storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
  const service = new CatalogSnapshotService(storage, {
    ...remote([page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1 })]),
    async getPromotions() {
      throw new Error("Catalog promotion field is invalid: response storeCode.");
    },
  }, { createSnapshotId: () => "promotion-invalid" });

  await assert.rejects(() => service.downloadAndActivate({ storeCode: "S1" }), /promotion/i);
  assert.equal(storage.active.has("old"), true);
  assert.equal(storage.staged.has("promotion-invalid"), false);
});

test("同商品可保留多条售卖码，但重复规范化 lookup 在落库前拒绝", async () => {
  const storage = new MemoryCatalogStorage();
  storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
  const service = new CatalogSnapshotService(storage, remote([
    page({
      cursor: null,
      nextCursor: "c2",
      items: [item("A", { productCode: "P-SAME" })],
      totalCount: 2,
    }),
    page({
      cursor: "c2",
      nextCursor: null,
      items: [item("A", { productCode: "P-SAME", barcode: "ANOTHER-SOURCE" })],
      totalCount: 2,
    }),
  ]), { createSnapshotId: () => "duplicate" });

  await assert.rejects(
    () => service.downloadAndActivate({ storeCode: "S1" }),
    catalogError("CATALOG_DUPLICATE_LOOKUP"),
  );
  assert.equal(storage.active.has("old"), true);
});

test("超过两位小数或超出安全整数范围的远端售价不会进入 SQLCipher", () => {
  assert.throws(
    () => mapCatalogLookupToStagedItem(item("A", { retailPrice: 1.001 })),
    catalogError("CATALOG_PRICE_INVALID"),
  );
  assert.throws(
    () => mapCatalogLookupToStagedItem(item("B", { retailPrice: Number.MAX_SAFE_INTEGER })),
    catalogError("CATALOG_PRICE_INVALID"),
  );
});

function catalogError(code: string): (error: unknown) => boolean {
  return (error: unknown) =>
    error instanceof HbposApiError
    && error.kind === "envelope"
    && error.code === code;
}
