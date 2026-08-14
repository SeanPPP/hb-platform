import assert from "node:assert/strict";
import test from "node:test";

import {
  HbposCatalogLookupApi,
  RemoteFallbackLocalCatalogPort,
  type CatalogRemoteLookupPort,
} from "./remote-catalog-fallback";

import type { HbposTransport, HbposTransportRequest } from "@/core/api";
import type { LocalCatalogMatch } from "@/core/db/catalog-repository";

const remoteItem = {
  storeCode: "S01",
  productCode: "P-REMOTE",
  referenceCode: "REF-REMOTE",
  displayName: "Remote tea",
  lookupCode: "930000000001",
  lookupCodeNormalized: "930000000001",
  itemNumber: "I-REMOTE",
  barcode: "930000000001",
  retailPrice: 12.34,
  priceSource: 1 as const,
  priceSourceLabel: "Store retail",
  quantityFactor: 1,
  updatedAt: "2026-07-29T01:02:03.456Z",
  rowVersion: "row-1",
  productImage: null,
  discountRate: null,
  isSpecialProduct: false,
};

test("远程 lookup adapter 使用固定 API 合同并验证返回的门店与精确身份", async () => {
  const calls: HbposTransportRequest[] = [];
  const transport: HbposTransport = {
    async request<T>(request: HbposTransportRequest) {
      calls.push(request);
      return {
        status: 200,
        data: {
          success: true,
          data: {
            storeCode: "S01",
            lookupCode: "930000000001",
            lookupCodeNormalized: "930000000001",
            found: true,
            item: remoteItem,
          },
        } as T,
      };
    },
  };

  const result = await new HbposCatalogLookupApi(transport).lookup({
    storeCode: " S01 ",
    lookupCode: " 930000000001 ",
  });

  assert.equal(result.found, true);
  assert.equal(result.item?.retailPrice, 12.34);
  assert.deepEqual(calls, [{
    method: "GET",
    url: "/api/v1/catalog/sellable-items/lookup",
    params: {
      storeCode: "S01",
      lookupCode: "930000000001",
    },
    acceptedStatuses: [404],
  }]);
});

test("仅明确的 LOOKUP_NOT_FOUND 可映射为未命中，门店 404 不能伪装成商品不存在", async () => {
  const notFoundTransport: HbposTransport = {
    async request<T>() {
      return {
        status: 404,
        data: {
          success: false,
          errorCode: "LOOKUP_NOT_FOUND",
          message: "not found",
        } as T,
      };
    },
  };
  const notFound = await new HbposCatalogLookupApi(notFoundTransport).lookup({
    storeCode: "S01",
    lookupCode: "MISSING",
  });
  assert.deepEqual(notFound, {
    storeCode: "S01",
    lookupCode: "MISSING",
    lookupCodeNormalized: "MISSING",
    found: false,
    item: null,
  });

  const storeMissingTransport: HbposTransport = {
    async request<T>() {
      return {
        status: 404,
        data: {
          success: false,
          errorCode: "STORE_NOT_FOUND",
          message: "store disabled",
        } as T,
      };
    },
  };
  await assert.rejects(
    () => new HbposCatalogLookupApi(storeMissingTransport).lookup({
      storeCode: "S01",
      lookupCode: "MISSING",
    }),
    /store disabled/,
  );
});

test("远程 lookup 只在在线本地精确查无时回退，并把合法结果转换为可加入购物车的本地模型", async () => {
  const calls: string[] = [];
  const remote: CatalogRemoteLookupPort = {
    async lookup(input) {
      calls.push(`${input.storeCode}:${input.lookupCode}`);
      return {
        storeCode: "S01",
        lookupCode: "930000000001",
        lookupCodeNormalized: "930000000001",
        found: true,
        item: remoteItem,
      };
    },
  };
  const port = new RemoteFallbackLocalCatalogPort({
    storeCode: "S01",
    remote,
    isOnline: () => true,
    local: localCatalog([]),
  });

  const result = await port.findExact(" 930000000001 ");

  assert.deepEqual(calls, ["S01:930000000001"]);
  assert.deepEqual(result, localItem({
    productCode: "P-REMOTE",
    referenceCode: "REF-REMOTE",
    displayName: "Remote tea",
    lookupCode: "930000000001",
    lookupCodeNormalized: "930000000001",
    retailPriceCents: 1234,
    priceSource: 1,
    priceSourceLabel: "Store retail",
    itemNumber: "I-REMOTE",
    barcode: "930000000001",
    updatedAtIso: "2026-07-29T01:02:03.456Z",
    rowVersion: "row-1",
  }));
});

test("离线、本地已有结果和远端失败都不会伪装为远程商品成功", async () => {
  let remoteCalls = 0;
  const remote: CatalogRemoteLookupPort = {
    async lookup() {
      remoteCalls += 1;
      throw new Error("network unavailable");
    },
  };
  const existing = localItem();
  const local = localCatalog([existing]);
  const online = new RemoteFallbackLocalCatalogPort({
    storeCode: "S01",
    remote,
    isOnline: () => true,
    local,
  });
  const offline = new RemoteFallbackLocalCatalogPort({
    storeCode: "S01",
    remote,
    isOnline: () => false,
    local: localCatalog([]),
  });

  assert.equal(await online.findExact(existing.lookupCode), existing);
  assert.equal(await online.findExact("REMOTE-MISS"), null);
  assert.equal(await offline.findExact("OFFLINE-MISS"), null);
  assert.equal(remoteCalls, 1);
});

test("本地目录读取不可用时在线回退，跨门店或跨 lookup 的远程结果必须拒绝", async () => {
  let remoteCalls = 0;
  const remote: CatalogRemoteLookupPort = {
    async lookup() {
      remoteCalls += 1;
      return {
        storeCode: "S01",
        lookupCode: "930000000001",
        lookupCodeNormalized: "930000000001",
        found: true,
        item: {
          ...remoteItem,
          storeCode: "OTHER",
        },
      };
    },
  };
  const port = new RemoteFallbackLocalCatalogPort({
    storeCode: "S01",
    remote,
    isOnline: () => true,
    local: {
      async findExact() {
        throw new Error("SQLCipher unavailable");
      },
      async searchByName() {
        return [];
      },
    },
  });

  assert.equal(await port.findExact("930000000001"), null);
  assert.equal(remoteCalls, 1);
});

test("同一在线本地 miss 合并为一次远程请求，后续读取可复用本次会话缓存", async () => {
  let resolve: ((value: Awaited<ReturnType<CatalogRemoteLookupPort["lookup"]>>) => void) | undefined;
  let calls = 0;
  const remote: CatalogRemoteLookupPort = {
    lookup() {
      calls += 1;
      return new Promise((done) => {
        resolve = done;
      });
    },
  };
  const port = new RemoteFallbackLocalCatalogPort({
    storeCode: "S01",
    remote,
    isOnline: () => true,
  });

  const first = port.findExact("930000000001");
  const second = port.findExact(" 930000000001 ");
  await Promise.resolve();
  assert.equal(calls, 1);
  resolve?.({
    storeCode: "S01",
    lookupCode: "930000000001",
    lookupCodeNormalized: "930000000001",
    found: true,
    item: remoteItem,
  });

  assert.equal((await first)?.productCode, "P-REMOTE");
  assert.equal((await second)?.productCode, "P-REMOTE");
  assert.equal((await port.findExact("930000000001"))?.productCode, "P-REMOTE");
  assert.equal(calls, 1);
});

function localCatalog(values: readonly LocalCatalogMatch[]) {
  return {
    async findExact(lookupCode: string) {
      const normalized = lookupCode.trim().toUpperCase();
      return values.find((value) => value.lookupCodeNormalized === normalized) ?? null;
    },
    async searchByName(query: string) {
      return values.filter((value) => value.displayName.toLowerCase().includes(query.toLowerCase()));
    },
  };
}

function localItem(overrides: Partial<LocalCatalogMatch> = {}): LocalCatalogMatch {
  return {
    storeCode: "S01",
    productCode: "P-LOCAL",
    referenceCode: null,
    itemNumber: "I-LOCAL",
    displayName: "Local tea",
    barcode: "LOCAL-1",
    lookupCode: "LOCAL-1",
    lookupCodeNormalized: "LOCAL-1",
    retailPriceCents: 500,
    priceSource: 0,
    priceSourceLabel: "Product",
    quantityFactor: 1,
    taxRateBasisPoints: null,
    updatedAtIso: null,
    rowVersion: null,
    productImage: null,
    discountRate: null,
    isSpecialProduct: false,
    ...overrides,
  };
}
