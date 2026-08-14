import assert from "node:assert/strict";
import test from "node:test";

import { HbposSpecialProductsApi } from "./hbpos-special-products-api";

import type {
  HbposTransport,
  HbposTransportRequest,
} from "@/core/api/hbpos-api";
import type { components } from "@/generated/hbpos/schema";


test("分页 API 原样映射 generated priceSource 0..4 与金额分值", async () => {
  const items = ([0, 1, 2, 3, 4] as const).map((priceSource, index) =>
    generatedItem({
      lookupCode: `LOOKUP-${index}`,
      priceSource,
      productCode: `P-${index}`,
    }),
  );
  const transport = new RecordingTransport([
    envelope(pageResponse({ items, totalCount: items.length })),
  ]);

  const result = await new HbposSpecialProductsApi(transport).getPage({
    cursor: null,
    pageSize: 200,
    storeCode: "S1",
  });

  assert.deepEqual(
    result.items.map((item) => item.priceSource),
    [0, 1, 2, 3, 4],
  );
  assert.deepEqual(
    result.items.map((item) => item.retailPriceCents),
    [1234, 1234, 1234, 1234, 1234],
  );
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      params: {
        cursor: undefined,
        pageSize: 200,
        storeCode: "S1",
      },
      url: "/api/v1/catalog/special-products/page",
    },
  ]);
});

test("分页 API 不会把越界或字符串 priceSource 强制转换为合法来源", async () => {
  for (const invalid of [-1, 5, "1", null]) {
    const transport = new RecordingTransport([
      envelope(
        pageResponse({
          items: [generatedItem({ priceSource: invalid as never })],
        }),
      ),
    ]);

    await assert.rejects(
      () =>
        new HbposSpecialProductsApi(transport).getPage({
          cursor: null,
          pageSize: 200,
          storeCode: "S1",
        }),
      invalidResponse("item.priceSource"),
    );
  }
});

test("分页 API 拒绝响应或商品越过可信门店范围", async () => {
  const cases = [
    pageResponse({ storeCode: "S2" }),
    pageResponse({ items: [generatedItem({ storeCode: "S2" })] }),
  ];

  for (const response of cases) {
    const transport = new RecordingTransport([envelope(response)]);
    await assert.rejects(
      () =>
        new HbposSpecialProductsApi(transport).getPage({
          cursor: null,
          pageSize: 200,
          storeCode: "S1",
        }),
      invalidResponse("storeCode"),
    );
  }
});

test("分页 API 严格校验 cursor、hasMore、nextCursor 与 totalCount", async () => {
  const cases: {
    expectedField: string;
    response: ReturnType<typeof pageResponse>;
  }[] = [
    {
      expectedField: "cursor",
      response: pageResponse({ cursor: "other" }),
    },
    {
      expectedField: "nextCursor",
      response: pageResponse({
        cursor: "cursor-1",
        hasMore: true,
        nextCursor: null,
      }),
    },
    {
      expectedField: "nextCursor",
      response: pageResponse({
        cursor: "cursor-1",
        hasMore: false,
        nextCursor: "unexpected",
      }),
    },
    {
      expectedField: "totalCount",
      response: pageResponse({ cursor: "cursor-1", totalCount: 0 }),
    },
    {
      expectedField: "totalCount",
      response: pageResponse({ cursor: "cursor-1", totalCount: 1.5 }),
    },
  ];

  for (const { expectedField, response } of cases) {
    const transport = new RecordingTransport([envelope(response)]);
    await assert.rejects(
      () =>
        new HbposSpecialProductsApi(transport).getPage({
          cursor: "cursor-1",
          pageSize: 200,
          storeCode: "S1",
        }),
      invalidResponse(expectedField),
    );
  }
});

test("mark 使用冻结请求形状并返回同门店同目标商品", async () => {
  const transport = new RecordingTransport([
    envelope(
      markResponse({
        items: [generatedItem({ priceSource: 4 })],
      }),
    ),
  ]);

  const result = await new HbposSpecialProductsApi(transport).mark({
    isSpecialProduct: true,
    productCode: "P1",
    storeCode: "S1",
  });

  assert.equal(result[0]?.priceSource, 4);
  assert.deepEqual(transport.requests, [
    {
      data: {
        isSpecialProduct: true,
        productCode: "P1",
        storeCode: "S1",
      },
      method: "POST",
      url: "/api/v1/catalog/special-products/mark",
    },
  ]);
});

test("mark 拒绝跨门店、跨目标或与请求标记状态不一致的回包", async () => {
  const cases: {
    expectedField: string;
    response: ReturnType<typeof markResponse>;
  }[] = [
    {
      expectedField: "response.storeCode",
      response: markResponse({ storeCode: "S2" }),
    },
    {
      expectedField: "response.productCode",
      response: markResponse({ productCode: "P2" }),
    },
    {
      expectedField: "response.isSpecialProduct",
      response: markResponse({ isSpecialProduct: false }),
    },
    {
      expectedField: "item.storeCode",
      response: markResponse({
        items: [generatedItem({ storeCode: "S2" })],
      }),
    },
    {
      expectedField: "item.productCode",
      response: markResponse({
        items: [generatedItem({ productCode: "P2" })],
      }),
    },
    {
      expectedField: "item.isSpecialProduct",
      response: markResponse({
        items: [generatedItem({ isSpecialProduct: false })],
      }),
    },
  ];

  for (const { expectedField, response } of cases) {
    const transport = new RecordingTransport([envelope(response)]);
    await assert.rejects(
      () =>
        new HbposSpecialProductsApi(transport).mark({
          isSpecialProduct: true,
          productCode: "P1",
          storeCode: "S1",
        }),
      invalidResponse(expectedField),
    );
  }
});

class RecordingTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public constructor(private readonly responses: unknown[]) {}

  public async request<T>(request: HbposTransportRequest) {
    this.requests.push(request);
    const response = this.responses.shift();
    if (response === undefined) {
      throw new Error("Unexpected transport request.");
    }
    return { data: response as T, status: 200 };
  }
}

type GeneratedItem = components["schemas"]["CatalogLookupItemDto"];
type GeneratedPage =
  components["schemas"]["CatalogSpecialProductsPageResponse"];
type GeneratedMark =
  components["schemas"]["CatalogSpecialProductMarkResponse"];

function generatedItem(
  overrides: Partial<GeneratedItem> = {},
): GeneratedItem {
  return {
    barcode: "930000000001",
    discountRate: null,
    displayName: "Product 1",
    isSpecialProduct: true,
    itemNumber: "ITEM-1",
    lookupCode: "LOOKUP-1",
    lookupCodeNormalized: "LOOKUP-1",
    priceSource: 0,
    priceSourceLabel: "product",
    productCode: "P1",
    productImage: null,
    quantityFactor: 1,
    referenceCode: null,
    retailPrice: 12.34,
    rowVersion: "ROW-1",
    storeCode: "S1",
    updatedAt: "2026-07-28T01:02:03.000Z",
    ...overrides,
  };
}

function pageResponse(
  overrides: Partial<GeneratedPage> = {},
): GeneratedPage {
  return {
    cursor: null,
    generatedAt: "2026-07-28T01:02:03.000Z",
    hasMore: false,
    items: [generatedItem()],
    nextCursor: null,
    storeCode: "S1",
    totalCount: 1,
    ...overrides,
  };
}

function markResponse(
  overrides: Partial<GeneratedMark> = {},
): GeneratedMark {
  return {
    generatedAt: "2026-07-28T01:02:03.000Z",
    isSpecialProduct: true,
    items: [generatedItem()],
    productCode: "P1",
    storeCode: "S1",
    ...overrides,
  };
}

function envelope<T>(data: T) {
  return { data, success: true };
}

function invalidResponse(field: string) {
  return (error: unknown) =>
    error instanceof Error &&
    "code" in error &&
    error.code === "SPECIAL_PRODUCTS_RESPONSE_INVALID" &&
    error.message.includes(field);
}
