import assert from "node:assert/strict";
import test from "node:test";

import {
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
  public activated: string | null = null;

  public async beginStaging(snapshot: { snapshotId: string }): Promise<void> {
    this.staged.set(snapshot.snapshotId, []);
  }

  public async appendPage(snapshotId: string, items: readonly CatalogStagedItem[]): Promise<void> {
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
  ]), { createSnapshotId: () => "snapshot-v2" });

  await service.downloadAndActivate({ storeCode: "S1" });

  assert.equal(storage.activated, "snapshot-v2");
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

test("分页总数、版本、门店或 continuation 不一致时保留旧 active 并清理 staging", async () => {
  const cases: readonly VerifiedCatalogSyncPage[][] = [
    [page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 2 })],
    [
      page({ cursor: null, nextCursor: "c2", items: [item("A")], totalCount: 2 }),
      page({ cursor: "c2", nextCursor: null, items: [item("B")], totalCount: 2, catalogVersion: "catalog-v3" }),
    ],
    [page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1, storeCode: "OTHER" })],
    [page({ cursor: null, nextCursor: null, items: [item("A")], totalCount: 1, hasMore: true })],
  ];

  for (const [index, pages] of cases.entries()) {
    const storage = new MemoryCatalogStorage();
    storage.active.set("old", [mapCatalogLookupToStagedItem(item("OLD"))]);
    const service = new CatalogSnapshotService(storage, remote(pages), {
      createSnapshotId: () => `failed-${index}`,
    });

    await assert.rejects(() => service.downloadAndActivate({ storeCode: "S1" }));
    assert.equal(storage.active.has("old"), true);
    assert.equal(storage.staged.has(`failed-${index}`), false);
  }
});

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
    /duplicate lookup code/i,
  );
  assert.equal(storage.active.has("old"), true);
});

test("超过两位小数或超出安全整数范围的远端售价不会进入 SQLCipher", () => {
  assert.throws(
    () => mapCatalogLookupToStagedItem(item("A", { retailPrice: 1.001 })),
    /integer cents/i,
  );
  assert.throws(
    () => mapCatalogLookupToStagedItem(item("B", { retailPrice: Number.MAX_SAFE_INTEGER })),
    /integer cents/i,
  );
});
