import assert from "node:assert/strict";
import test from "node:test";
import type { OfflineCatalogRemote } from "./offline-catalog-remote";
import { OfflineCatalogDeltaBaseChangedError } from "./offline-catalog-repository";
import {
  OfflineCatalogSyncService,
  type OfflineCatalogRefreshProgressEvent,
  type OfflineCatalogSyncStorage,
} from "./offline-catalog-sync-service";
import {
  buildOfflineLookupKey,
  normalizeOfflineLookupCode,
  OfflineCatalogError,
  type ActiveOfflineCatalogMetadata,
  type OfflineCatalogDeltaPage,
  type OfflineCatalogItem,
  type OfflineCatalogPage,
  type OfflineCatalogSyncPlan,
} from "./types";

function item(lookupCode: string, productCode = `P-${lookupCode}`): OfflineCatalogItem {
  const lookupCodeNormalized = normalizeOfflineLookupCode(lookupCode);
  return {
    storeCode: "S001",
    lookupKey: buildOfflineLookupKey({ lookupCodeNormalized, matchSource: "ProductBarcode", productCode, codeId: null }),
    lookupCode,
    lookupCodeNormalized,
    matchSource: "ProductBarcode",
    productCode,
    productName: lookupCode,
    itemNumber: null,
    barcode: lookupCode,
    productImage: null,
    productType: 0,
    grade: null,
    localSupplierCode: null,
    localSupplierName: null,
    storeName: null,
    storePriceUuid: "sp",
    purchasePrice: 1,
    retailPrice: 2,
    discountRate: null,
    isAutoPricing: false,
    isSpecialProduct: false,
    rate: null,
    strategySourceLabel: null,
    strategyRuleLabel: null,
    clearanceUuid: null,
    clearanceBarcode: null,
    clearancePrice: null,
    codeId: null,
    codeUuid: null,
    codeProductCode: null,
    codeItemNumber: null,
    codeRetailPrice: null,
    codePurchasePrice: null,
    codeQuantity: null,
    codeType: null,
    codeDiscountRate: null,
    codeIsAutoPricing: null,
    codeIsSpecialProduct: null,
    codeIsActive: null,
    updatedAt: null,
    rowVersion: `rv-${lookupCode}`,
  };
}

/** 内存版仓储：只记录调用，模拟 active/staging 状态。 */
class MemoryStorage implements OfflineCatalogSyncStorage {
  public active: ActiveOfflineCatalogMetadata | null = null;
  public staged = new Map<string, OfflineCatalogItem[]>();
  public deltaStaged = new Map<string, { items: OfflineCatalogItem[]; deleted: string[] }>();
  public discarded: string[] = [];
  public calls: string[] = [];

  public async getActiveMetadata(): Promise<ActiveOfflineCatalogMetadata | null> {
    return this.active;
  }
  public async beginStaging(snapshot: { snapshotId: string }): Promise<void> {
    this.calls.push(`beginStaging:${snapshot.snapshotId}`);
    this.staged.set(snapshot.snapshotId, []);
  }
  public async appendPage(snapshotId: string, items: readonly OfflineCatalogItem[]): Promise<void> {
    this.calls.push(`appendPage:${items.length}`);
    this.staged.get(snapshotId)?.push(...items);
  }
  public async activate(snapshotId: string, expectedItemCount: number, activatedAtIso: string): Promise<void> {
    const items = this.staged.get(snapshotId) ?? [];
    if (items.length !== expectedItemCount) throw new Error("count mismatch");
    this.calls.push(`activate:${snapshotId}`);
    this.active = { snapshotId, storeCode: "S001", catalogVersion: "v-new", itemCount: items.length, generatedAt: "2026-09-17T00:00:00.000Z", activatedAt: activatedAtIso };
  }
  public async beginDeltaStaging(input: { snapshotId: string; baseCatalogVersion: string }): Promise<void> {
    if (this.active?.catalogVersion !== input.baseCatalogVersion) throw new OfflineCatalogDeltaBaseChangedError();
    this.calls.push(`beginDeltaStaging:${input.snapshotId}`);
    this.deltaStaged.set(input.snapshotId, { items: [], deleted: [] });
  }
  public async appendDeltaBatch(snapshotId: string, batch: { items: readonly OfflineCatalogItem[]; deletedItems: readonly { lookupKey: string }[] }): Promise<void> {
    this.calls.push(`appendDeltaBatch:${batch.items.length}/${batch.deletedItems.length}`);
    const staged = this.deltaStaged.get(snapshotId);
    staged?.items.push(...batch.items);
    staged?.deleted.push(...batch.deletedItems.map((d) => d.lookupKey));
  }
  public async activateDelta(input: { stagingSnapshotId: string; expectedItemCount: number; activatedAtIso: string }): Promise<ActiveOfflineCatalogMetadata> {
    this.calls.push(`activateDelta:${input.stagingSnapshotId}`);
    this.active = { ...this.active!, catalogVersion: "v-delta", itemCount: input.expectedItemCount, activatedAt: input.activatedAtIso };
    return this.active;
  }
  public async discardStagingBatch(snapshotId: string): Promise<number> {
    if (this.staged.delete(snapshotId) || this.deltaStaged.delete(snapshotId)) {
      this.discarded.push(snapshotId);
      return 1;
    }
    return 0;
  }
  public async cleanupStagingBatch(): Promise<number> { return 0; }
  public async cleanupRetiredBatch(): Promise<number> { return 0; }
}

function plan(overrides: Partial<OfflineCatalogSyncPlan>): OfflineCatalogSyncPlan {
  return {
    storeCode: "S001",
    generatedAt: "2026-09-17T00:00:00.000Z",
    mode: "full",
    baseCatalogVersion: null,
    targetCatalogVersion: "v-new",
    targetTotal: 0,
    downloadLeaseId: "lease-1",
    deltaOperationCount: null,
    ...overrides,
  };
}

function page(items: OfflineCatalogItem[], overrides: Partial<OfflineCatalogPage>): OfflineCatalogPage {
  return {
    storeCode: "S001",
    generatedAt: "2026-09-17T00:00:00.000Z",
    cursor: null,
    items,
    nextCursor: null,
    hasMore: false,
    totalCount: items.length,
    catalogVersion: "v-new",
    pageChecksum: "sha",
    ...overrides,
  };
}

function createService(storage: MemoryStorage, remote: Partial<OfflineCatalogRemote>) {
  let counter = 0;
  const fullRemote: OfflineCatalogRemote = {
    getSyncPlan: async () => { throw new Error("unexpected getSyncPlan"); },
    getPage: async () => { throw new Error("unexpected getPage"); },
    getDeltaPage: async () => { throw new Error("unexpected getDeltaPage"); },
    ...remote,
  };
  return new OfflineCatalogSyncService(storage, fullRemote, {
    createSnapshotId: () => `snap-${++counter}`,
    nowIso: () => "2026-09-17T09:00:00.000Z",
    pageSize: 2,
    localBatchSize: 1,
    yieldControl: async () => undefined,
  });
}

test("无本地快照：full 分页下载 → staging → activate，并汇报进度", async () => {
  const storage = new MemoryStorage();
  const items = [item("A"), item("B"), item("C")];
  const pageRequests: (string | null)[] = [];
  const service = createService(storage, {
    getSyncPlan: async () => plan({ targetTotal: 3 }),
    getPage: async ({ cursor }) => {
      pageRequests.push(cursor);
      if (cursor === null) return page(items.slice(0, 2), { totalCount: 3, nextCursor: "B", hasMore: true });
      return page(items.slice(2), { cursor: "B", totalCount: 3 });
    },
  });
  const events: OfflineCatalogRefreshProgressEvent[] = [];
  const result = await service.refresh({ storeCode: "S001", onProgress: (event) => events.push(event) });
  assert.equal(result.mode, "full");
  assert.equal(result.metadata.itemCount, 3);
  assert.equal(result.metadata.generatedAt, "2026-09-17T00:00:00.000Z");
  assert.deepEqual(pageRequests, [null, "B"]);
  assert.ok(storage.calls.includes("activate:snap-1"));
  assert.equal(events.at(-1)?.step, "activate");
  assert.equal(events.at(-1)?.percent, 100);
  const productEvents = events.filter((event) => event.step === "products");
  assert.equal(productEvents.at(-1)?.completedItemCount, 3);
});

test("分页总数不一致时丢弃 staging 并抛出校验错误", async () => {
  const storage = new MemoryStorage();
  const service = createService(storage, {
    getSyncPlan: async () => plan({ targetTotal: 2 }),
    getPage: async () => page([item("A")], { totalCount: 2 }),
  });
  await assert.rejects(
    () => service.refresh({ storeCode: "S001" }),
    (error: unknown) => error instanceof OfflineCatalogError && error.code === "OFFLINE_CATALOG_ITEM_COUNT_MISMATCH",
  );
  assert.deepEqual(storage.discarded, ["snap-1"]);
  assert.equal(storage.active, null);
});

test("noChange 且条目数一致时直接返回本地 active", async () => {
  const storage = new MemoryStorage();
  storage.active = { snapshotId: "snap-0", storeCode: "S001", catalogVersion: "v-old", itemCount: 5, generatedAt: "2026-09-16T00:00:00.000Z", activatedAt: "2026-09-16T00:01:00.000Z" };
  const service = createService(storage, {
    getSyncPlan: async () => plan({ mode: "noChange", baseCatalogVersion: "v-old", targetCatalogVersion: "v-old", targetTotal: 5, downloadLeaseId: null }),
  });
  const result = await service.refresh({ storeCode: "S001" });
  assert.equal(result.mode, "noChange");
  assert.equal(result.metadata.snapshotId, "snap-0");
  assert.deepEqual(storage.calls, []);
});

test("delta：分页拉取 upsert/delete 并回放激活", async () => {
  const storage = new MemoryStorage();
  storage.active = { snapshotId: "snap-0", storeCode: "S001", catalogVersion: "v-old", itemCount: 3, generatedAt: "2026-09-16T00:00:00.000Z", activatedAt: "2026-09-16T00:01:00.000Z" };
  const deltaPage: OfflineCatalogDeltaPage = {
    storeCode: "S001",
    generatedAt: "2026-09-17T00:00:00.000Z",
    baseCatalogVersion: "v-old",
    targetCatalogVersion: "v-delta",
    cursor: null,
    items: [item("B")],
    deletedItems: [{ storeCode: "S001", lookupKey: item("C").lookupKey, deletedAt: null }],
    nextCursor: null,
    hasMore: false,
    targetTotal: 3,
    pageChecksum: "sha",
  };
  const service = createService(storage, {
    getSyncPlan: async () => plan({ mode: "delta", baseCatalogVersion: "v-old", targetCatalogVersion: "v-delta", targetTotal: 3, deltaOperationCount: 2 }),
    getDeltaPage: async () => deltaPage,
  });
  const result = await service.refresh({ storeCode: "S001" });
  assert.equal(result.mode, "delta");
  assert.equal(result.metadata.catalogVersion, "v-delta");
  assert.ok(storage.calls.includes("appendDeltaBatch:1/1"));
  assert.ok(storage.calls.includes("activateDelta:snap-1"));
});

test("delta 基线过期（409）时丢弃 staging 并回退全量", async () => {
  const storage = new MemoryStorage();
  storage.active = { snapshotId: "snap-0", storeCode: "S001", catalogVersion: "v-old", itemCount: 1, generatedAt: "2026-09-16T00:00:00.000Z", activatedAt: "2026-09-16T00:01:00.000Z" };
  let planCalls = 0;
  const service = createService(storage, {
    getSyncPlan: async ({ baseCatalogVersion }) => {
      planCalls += 1;
      return baseCatalogVersion === null
        ? plan({ targetTotal: 1 })
        : plan({ mode: "delta", baseCatalogVersion: "v-old", targetCatalogVersion: "v-new", targetTotal: 1, deltaOperationCount: 1 });
    },
    getDeltaPage: async () => { throw new OfflineCatalogError("expired", "OFFLINE_CATALOG_SNAPSHOT_EXPIRED", 409); },
    getPage: async () => page([item("A")], {}),
  });
  const result = await service.refresh({ storeCode: "S001" });
  assert.equal(result.mode, "full");
  assert.equal(planCalls, 2);
  assert.deepEqual(storage.discarded, ["snap-1"]);
  assert.ok(storage.calls.includes("activate:snap-2"));
});

test("delta 操作数超过上限时回退全量", async () => {
  const storage = new MemoryStorage();
  storage.active = { snapshotId: "snap-0", storeCode: "S001", catalogVersion: "v-old", itemCount: 1, generatedAt: "2026-09-16T00:00:00.000Z", activatedAt: "2026-09-16T00:01:00.000Z" };
  const service = createService(storage, {
    getSyncPlan: async ({ baseCatalogVersion }) =>
      baseCatalogVersion === null
        ? plan({ targetTotal: 1 })
        : plan({ mode: "delta", baseCatalogVersion: "v-old", targetCatalogVersion: "v-new", targetTotal: 1, deltaOperationCount: 5_001 }),
    getPage: async () => page([item("A")], {}),
  });
  const result = await service.refresh({ storeCode: "S001" });
  assert.equal(result.mode, "full");
  assert.equal(storage.calls.some((call) => call.startsWith("beginDeltaStaging")), false);
});

test("取消信号在落库前生效，staging 被丢弃", async () => {
  const storage = new MemoryStorage();
  const controller = new AbortController();
  const service = createService(storage, {
    getSyncPlan: async () => plan({ targetTotal: 1 }),
    getPage: async () => {
      controller.abort();
      return page([item("A")], {});
    },
  });
  await assert.rejects(
    () => service.refresh({ storeCode: "S001", signal: controller.signal }),
    (error: unknown) => error instanceof OfflineCatalogError && error.code === "OFFLINE_CATALOG_CANCELLED",
  );
  assert.equal(storage.active, null);
});

test("同一页内重复 lookupKey 视为校验失败", async () => {
  const storage = new MemoryStorage();
  const service = createService(storage, {
    getSyncPlan: async () => plan({ targetTotal: 2 }),
    getPage: async () => page([item("A"), item("A")], { totalCount: 2 }),
  });
  await assert.rejects(
    () => service.refresh({ storeCode: "S001" }),
    (error: unknown) => error instanceof OfflineCatalogError && error.code === "OFFLINE_CATALOG_DUPLICATE_LOOKUP",
  );
});
