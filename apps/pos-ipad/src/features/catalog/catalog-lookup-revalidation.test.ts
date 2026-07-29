import assert from "node:assert/strict";
import test from "node:test";

import {
  RemoteCatalogLookupRevalidationService,
  type CatalogLookupOverlayWritePort,
} from "./catalog-lookup-revalidation";
import type {
  CatalogRemoteLookupPort,
  CatalogRemoteLookupResult,
  LocalCatalogReadPort,
} from "./remote-catalog-fallback";

import type { LocalCatalogMatch } from "@/core/db/catalog-repository";

test("同一门店、目录代次和规范化条码只发一次远程请求，并持久化完整覆盖记录", async () => {
  const lookup = deferred<CatalogRemoteLookupResult>();
  const overlay = new Overlay();
  const remoteCalls: string[] = [];
  const remote: CatalogRemoteLookupPort = {
    lookup(input) {
      remoteCalls.push(`${input.storeCode}:${input.lookupCode}`);
      return lookup.promise;
    },
  };
  const service = new RemoteCatalogLookupRevalidationService({
    storeCode: "S1",
    remote,
    overlay,
    isOnline: () => true,
  });

  const first = service.revalidate(" 930000000001 ");
  const second = service.revalidate("930000000001");
  await waitFor(() => remoteCalls.length === 1);
  assert.deepEqual(remoteCalls, ["S1:930000000001"]);

  lookup.resolve(foundResult(remoteItem()));
  const [firstResult, secondResult] = await Promise.all([first, second]);

  assert.equal(firstResult, secondResult);
  assert.equal(firstResult.kind, "found");
  assert.equal(overlay.upserts.length, 1);
  assert.equal(overlay.upserts[0]?.baseSnapshotId, "snapshot-1");
  assert.equal(overlay.upserts[0]?.item.retailPriceCents, 734);
});

test("远程期间目录换代时旧结果不落库，并按新代次重新查询一次", async () => {
  const overlay = new Overlay();
  const remoteResults = [
    deferred<CatalogRemoteLookupResult>(),
    deferred<CatalogRemoteLookupResult>(),
  ];
  let calls = 0;
  const service = new RemoteCatalogLookupRevalidationService({
    storeCode: "S1",
    remote: {
      lookup() {
        const result = remoteResults[calls];
        calls += 1;
        if (!result) throw new Error("unexpected lookup");
        return result.promise;
      },
    },
    overlay,
    isOnline: () => true,
  });

  const pending = service.revalidate("930000000001");
  await Promise.resolve();
  overlay.activeSnapshotId = "snapshot-2";
  remoteResults[0]!.resolve(foundResult(remoteItem({ displayName: "Old" })));
  await waitFor(() => calls === 2);
  assert.equal(calls, 2);
  remoteResults[1]!.resolve(foundResult(remoteItem({ displayName: "New" })));

  const result = await pending;
  assert.equal(result.kind, "found");
  assert.equal(result.kind === "found" ? result.item.displayName : null, "New");
  assert.equal(result.kind === "found" ? result.baseSnapshotId : null, "snapshot-2");
  assert.deepEqual(
    overlay.upserts.map((entry) => entry.item.displayName),
    ["New"],
  );
});

test("明确未命中写 tombstone；失败和身份不一致不污染覆盖层", async () => {
  const overlay = new Overlay();
  const results: CatalogRemoteLookupResult[] = [
    {
      storeCode: "S1",
      lookupCode: "MISSING",
      lookupCodeNormalized: "MISSING",
      found: false,
      item: null,
    },
    foundResult(remoteItem({ storeCode: "OTHER" })),
  ];
  const service = new RemoteCatalogLookupRevalidationService({
    storeCode: "S1",
    remote: {
      async lookup() {
        const result = results.shift();
        if (!result) throw new Error("offline");
        return result;
      },
    },
    overlay,
    isOnline: () => true,
  });

  assert.equal((await service.revalidate("MISSING")).kind, "not-found");
  assert.equal((await service.revalidate("930000000001")).kind, "unavailable");
  assert.equal((await service.revalidate("NETWORK")).kind, "unavailable");
  assert.deepEqual(overlay.tombstones, [{
    baseSnapshotId: "snapshot-1",
    storeCode: "S1",
    lookupCodeNormalized: "MISSING",
  }]);
  assert.equal(overlay.upserts.length, 0);
});

test("远程 DTO 缺少税率时，同身份商品保留本地税率", async () => {
  const overlay = new Overlay();
  const local: LocalCatalogReadPort = {
    async findExact() {
      return localItem({ taxRateBasisPoints: 1_000 });
    },
    async searchByName() {
      return [];
    },
  };
  const service = new RemoteCatalogLookupRevalidationService({
    storeCode: "S1",
    remote: {
      async lookup() {
        return foundResult(remoteItem({ retailPrice: 8.25 }));
      },
    },
    overlay,
    local,
    isOnline: () => true,
  });

  const result = await service.revalidate("930000000001");

  assert.equal(result.kind, "found");
  assert.equal(
    result.kind === "found" ? result.item.taxRateBasisPoints : null,
    1_000,
  );
});

test("本地税率读取异常时远程命中降级 unavailable 且不写覆盖层", async () => {
  const overlay = new Overlay();
  const service = new RemoteCatalogLookupRevalidationService({
    storeCode: "S1",
    remote: {
      async lookup() {
        return foundResult(remoteItem());
      },
    },
    overlay,
    local: {
      async findExact() {
        throw new Error("local catalog unavailable");
      },
      async searchByName() {
        return [];
      },
    },
    isOnline: () => true,
  });

  assert.equal(
    (await service.revalidate("930000000001")).kind,
    "unavailable",
  );
  assert.equal(overlay.upserts.length, 0);
});

test("本地明确未命中的远程新商品允许以空税率写入覆盖层", async () => {
  const overlay = new Overlay();
  const service = new RemoteCatalogLookupRevalidationService({
    storeCode: "S1",
    remote: {
      async lookup() {
        return foundResult(remoteItem());
      },
    },
    overlay,
    local: {
      async findExact() {
        return null;
      },
      async searchByName() {
        return [];
      },
    },
    isOnline: () => true,
  });

  const result = await service.revalidate("930000000001");

  assert.equal(result.kind, "found");
  assert.equal(
    result.kind === "found" ? result.item.taxRateBasisPoints : 1,
    null,
  );
  assert.equal(overlay.upserts.length, 1);
});

class Overlay implements CatalogLookupOverlayWritePort {
  public activeSnapshotId: string | null = "snapshot-1";
  public readonly upserts: {
    baseSnapshotId: string | null;
    item: LocalCatalogMatch;
  }[] = [];
  public readonly tombstones: {
    baseSnapshotId: string | null;
    storeCode: string;
    lookupCodeNormalized: string;
  }[] = [];

  public async getActiveSnapshotId(): Promise<string | null> {
    return this.activeSnapshotId;
  }

  public async upsert(input: {
    baseSnapshotId: string | null;
    item: LocalCatalogMatch;
  }): Promise<"applied" | "stale-generation"> {
    if (input.baseSnapshotId !== this.activeSnapshotId) {
      return "stale-generation";
    }
    this.upserts.push(input);
    return "applied";
  }

  public async tombstone(input: {
    baseSnapshotId: string | null;
    storeCode: string;
    lookupCodeNormalized: string;
  }): Promise<"applied" | "stale-generation"> {
    if (input.baseSnapshotId !== this.activeSnapshotId) {
      return "stale-generation";
    }
    this.tombstones.push(input);
    return "applied";
  }
}

function foundResult(
  item: ReturnType<typeof remoteItem>,
): CatalogRemoteLookupResult {
  return {
    storeCode: "S1",
    lookupCode: "930000000001",
    lookupCodeNormalized: "930000000001",
    found: true,
    item,
  };
}

function remoteItem(overrides: Record<string, unknown> = {}) {
  return {
    storeCode: "S1",
    productCode: "P-TEA",
    referenceCode: "REF-TEA",
    displayName: "Remote tea",
    lookupCode: "930000000001",
    lookupCodeNormalized: "930000000001",
    itemNumber: "100",
    barcode: "930000000001",
    retailPrice: 7.34,
    priceSource: 1 as const,
    priceSourceLabel: "Store retail",
    quantityFactor: 1,
    updatedAt: "2026-07-29T00:00:00.000Z",
    rowVersion: "row-2",
    productImage: null,
    discountRate: null,
    isSpecialProduct: false,
    ...overrides,
  };
}

function localItem(
  overrides: Partial<LocalCatalogMatch> = {},
): LocalCatalogMatch {
  return {
    storeCode: "S1",
    productCode: "P-TEA",
    referenceCode: "REF-TEA",
    itemNumber: "100",
    displayName: "Local tea",
    barcode: "930000000001",
    lookupCode: "930000000001",
    lookupCodeNormalized: "930000000001",
    retailPriceCents: 500,
    priceSource: 0,
    priceSourceLabel: "Product",
    quantityFactor: 1,
    taxRateBasisPoints: null,
    updatedAtIso: null,
    rowVersion: "row-1",
    productImage: null,
    discountRate: null,
    isSpecialProduct: false,
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

async function waitFor(predicate: () => boolean): Promise<void> {
  for (let attempts = 0; attempts < 50; attempts += 1) {
    if (predicate()) return;
    await new Promise<void>((resolve) => setImmediate(resolve));
  }
  throw new Error("condition was not reached");
}
