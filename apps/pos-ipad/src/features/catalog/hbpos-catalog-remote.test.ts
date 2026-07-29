import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";

import type { CatalogPromotion } from "./catalog-snapshot-service";
import {
  HbposCatalogPageApi,
  calculateCatalogPageChecksum,
  type CatalogPageDigest,
  type CatalogLookupItem,
} from "./hbpos-catalog-remote";

import type { HbposTransport, HbposTransportRequest } from "@/core/api";

const digest: CatalogPageDigest = async (payload) =>
  createHash("sha256").update(payload, "utf8").digest("hex");

const canonicalItem: CatalogLookupItem = {
  storeCode: "S01",
  productCode: "P-001",
  referenceCode: null,
  displayName: "牛奶🥛",
  lookupCode: "930000000001",
  lookupCodeNormalized: "930000000001",
  itemNumber: "I-001",
  barcode: "source-barcode-is-not-the-offline-lookup",
  retailPrice: 12.34,
  priceSource: 0,
  priceSourceLabel: "product",
  quantityFactor: 1,
  updatedAt: "2026-07-28T01:02:03.456Z",
  rowVersion: "ROW",
  productImage: null,
  discountRate: null,
  isSpecialProduct: true,
};

test("TypeScript 与服务端共享规范化 SHA256 v1 测试向量", async () => {
  assert.equal(
    await calculateCatalogPageChecksum([canonicalItem], digest),
    "sha256-catalog-page-v1:4eb87e036003575ca8b8e9961ab6c21dbe63ed6d18482886f1da92e8f4165530",
  );
});

test("checksum 以 JavaScript 可观测数值为协议表示，避免 decimal 精度误拒绝", async () => {
  assert.equal(
    await calculateCatalogPageChecksum([{
      ...canonicalItem,
      productCode: "P-HIGH",
      referenceCode: null,
      displayName: "精度",
      lookupCode: "HIGH",
      lookupCodeNormalized: "HIGH",
      itemNumber: null,
      barcode: null,
      retailPrice: 0.1000000000000000000000000001,
      quantityFactor: 12345678901234567890.123456789,
      updatedAt: null,
      productImage: null,
      discountRate: 0.3333333333333333333333333333,
      isSpecialProduct: false,
    }], digest),
    "sha256-catalog-page-v1:86178b9aa03175a4dc97d8c61fa8db018507d0c29d60b43a5d65b47106681e44",
  );
});

test("Hbpos catalog adapter 映射分页合同并校验服务端 checksum", async () => {
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
            generatedAt: "2026-07-28T01:02:03.456Z",
            cursor: null,
            items: [canonicalItem],
            deletedLookups: [],
            nextCursor: "930000000001",
            hasMore: true,
            totalCount: 2,
            catalogVersion: "catalog-v1:server",
            pageChecksum: "sha256-catalog-page-v1:4eb87e036003575ca8b8e9961ab6c21dbe63ed6d18482886f1da92e8f4165530",
          },
        } as T,
      };
    },
  };

  const page = await new HbposCatalogPageApi(transport, digest).getPage({
    storeCode: "S01",
    cursor: null,
    pageSize: 500,
  });

  assert.deepEqual(page, {
    storeCode: "S01",
    generatedAt: "2026-07-28T01:02:03.456Z",
    cursor: null,
    items: [canonicalItem],
    deletedLookups: [],
    nextCursor: "930000000001",
    hasMore: true,
    totalCount: 2,
    catalogVersion: "catalog-v1:server",
    pageChecksum: "sha256-catalog-page-v1:4eb87e036003575ca8b8e9961ab6c21dbe63ed6d18482886f1da92e8f4165530",
  });
  assert.deepEqual(calls, [{
    method: "GET",
    url: "/api/v1/catalog/sellable-items/page",
    params: {
      storeCode: "S01",
      cursor: undefined,
      pageSize: 500,
    },
  }]);
});

test("Hbpos catalog adapter 接受服务端可选字段的空字符串并规范化为 null", async () => {
  const normalizedItem: CatalogLookupItem = {
    ...canonicalItem,
    referenceCode: null,
    itemNumber: null,
    barcode: null,
    productImage: null,
  };
  const pageChecksum = await calculateCatalogPageChecksum([normalizedItem], digest);
  const transport: HbposTransport = {
    async request<T>() {
      return {
        status: 200,
        data: {
          success: true,
          data: {
            storeCode: "S01",
            generatedAt: "2026-07-28T01:02:03.456Z",
            cursor: null,
            items: [{
              ...normalizedItem,
              referenceCode: "",
              itemNumber: "",
              barcode: "",
              productImage: "",
            }],
            deletedLookups: [],
            nextCursor: null,
            hasMore: false,
            totalCount: 1,
            catalogVersion: "catalog-v1:server",
            pageChecksum,
          },
        } as T,
      };
    },
  };

  const page = await new HbposCatalogPageApi(transport, digest).getPage({
    storeCode: "S01",
    cursor: null,
    pageSize: 500,
  });

  assert.equal(page.items[0]?.referenceCode, null);
  assert.equal(page.items[0]?.itemNumber, null);
  assert.equal(page.items[0]?.barcode, null);
  assert.equal(page.items[0]?.productImage, null);
});

test("Hbpos catalog adapter 在落库前拒绝被篡改的页面", async () => {
  const transport: HbposTransport = {
    async request<T>() {
      return {
        status: 200,
        data: {
          success: true,
          data: {
            storeCode: "S01",
            generatedAt: "2026-07-28T01:02:03.456Z",
            cursor: null,
            items: [{
              storeCode: "S01",
              productCode: "P-001",
              referenceCode: null,
              displayName: "已被篡改",
              lookupCode: "930000000001",
              lookupCodeNormalized: "930000000001",
              itemNumber: "I-001",
              barcode: "source-barcode-is-not-the-offline-lookup",
              retailPrice: 12.34,
              priceSource: 0,
              priceSourceLabel: "product",
              quantityFactor: 1,
              updatedAt: "2026-07-28T01:02:03.456Z",
              rowVersion: "ROW",
              productImage: null,
              discountRate: null,
              isSpecialProduct: true,
            }],
            deletedLookups: [],
            nextCursor: null,
            hasMore: false,
            totalCount: 1,
            catalogVersion: "catalog-v1:server",
            // 使用未篡改 canonicalItem 的合法摘要，确保拒绝原因确实是响应内容被改写。
            pageChecksum: "sha256-catalog-page-v1:4eb87e036003575ca8b8e9961ab6c21dbe63ed6d18482886f1da92e8f4165530",
          },
        } as T,
      };
    },
  };

  await assert.rejects(
    () => new HbposCatalogPageApi(transport, digest).getPage({
      storeCode: "S01",
      cursor: null,
      pageSize: 500,
    }),
    /checksum/i,
  );
});

const canonicalPromotion = {
  promotionId: "PROMO-MILK-2",
  name: "牛奶两件特价",
  isExclusive: true,
  priority: 10,
  applyQuantity: 2,
  fixedPrice: 20.5,
  maxApplicationsPerOrder: 3,
  effectiveStart: "2026-07-28T00:00:00.000Z",
  effectiveEnd: "2026-08-28T00:00:00.000Z",
  updatedAt: "2026-07-27T11:12:13.000Z",
  products: [{ productCode: "P-001", unitWeight: 1 }],
};

test("Hbpos catalog adapter 将完整促销合同稳定白名单序列化给快照", async () => {
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
            generatedAt: "2026-07-28T01:02:03.456Z",
            promotions: [{
              ...canonicalPromotion,
              // 后端将来新增字段时，不得未审查地持久化进可影响金额的规则 JSON。
              unexpectedServerField: "ignored",
            }],
          },
        } as T,
      };
    },
  };

  const promotions = await new HbposCatalogPageApi(transport, digest).getPromotions({
    storeCode: "S01",
  });

  const expected: CatalogPromotion[] = [{
    promotionId: "PROMO-MILK-2",
    validFromIso: "2026-07-28T00:00:00.000Z",
    validUntilIso: "2026-08-28T00:00:00.000Z",
    priority: 10,
    definitionJson: JSON.stringify(canonicalPromotion),
  }];
  assert.deepEqual(promotions, expected);
  assert.deepEqual(calls, [{
    method: "GET",
    url: "/api/v1/catalog/promotions",
    params: { storeCode: "S01" },
  }]);
});

test("Hbpos catalog adapter 拒绝跨门店、重复或非法的促销身份", async () => {
  const cases = [
    {
      label: "跨门店响应",
      body: { storeCode: "S02", promotions: [canonicalPromotion] },
    },
    {
      label: "重复促销标识",
      body: { storeCode: "S01", promotions: [canonicalPromotion, canonicalPromotion] },
    },
    {
      label: "重复商品标识",
      body: {
        storeCode: "S01",
        promotions: [{
          ...canonicalPromotion,
          products: [
            { productCode: "P-001", unitWeight: 1 },
            { productCode: "P-001", unitWeight: 2 },
          ],
        }],
      },
    },
    {
      label: "空促销标识",
      body: { storeCode: "S01", promotions: [{ ...canonicalPromotion, promotionId: " " }] },
    },
  ] as const;

  for (const entry of cases) {
    const transport: HbposTransport = {
      async request<T>() {
        return {
          status: 200,
          data: { success: true, data: entry.body } as T,
        };
      },
    };
    await assert.rejects(
      () => new HbposCatalogPageApi(transport, digest).getPromotions({ storeCode: "S01" }),
      /promotion/i,
      entry.label,
    );
  }
});

test("Hbpos catalog adapter 拒绝缺失数组和不安全的促销金额、时间、整数或商品", async () => {
  const invalidPromotions = [
    { ...canonicalPromotion, fixedPrice: 20.555 },
    { ...canonicalPromotion, priority: 0.5 },
    { ...canonicalPromotion, applyQuantity: 0 },
    { ...canonicalPromotion, effectiveEnd: "not-a-time" },
    { ...canonicalPromotion, products: [{ productCode: "P-001", unitWeight: 0 }] },
  ];

  for (const promotions of [undefined, ...invalidPromotions.map((promotion) => [promotion])]) {
    const transport: HbposTransport = {
      async request<T>() {
        return {
          status: 200,
          data: {
            success: true,
            data: {
              storeCode: "S01",
              generatedAt: "2026-07-28T01:02:03.456Z",
              promotions,
            },
          } as T,
        };
      },
    };
    await assert.rejects(
      () => new HbposCatalogPageApi(transport, digest).getPromotions({ storeCode: "S01" }),
      /promotion/i,
    );
  }
});
