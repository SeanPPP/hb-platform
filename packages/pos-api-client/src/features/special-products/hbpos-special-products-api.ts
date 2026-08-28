import { HbposApiError, unwrapHbposEnvelope, type HbposEnvelope, type HbposTransport } from "../../transport";
import type { SpecialProductItem, SpecialProductsRemotePort } from "@hb/pos-domain/core/contracts/special-products";
import type { components } from "../../openapi";

type GeneratedItem = components["schemas"]["CatalogLookupItemDto"];
type GeneratedPage =
  components["schemas"]["CatalogSpecialProductsPageResponse"];
type GeneratedMarkRequest =
  components["schemas"]["CatalogSpecialProductMarkRequest"];
type GeneratedMarkResponse =
  components["schemas"]["CatalogSpecialProductMarkResponse"];

type RemoteSpecialProduct = Omit<SpecialProductItem, "sortOrder">;

export class HbposSpecialProductsApi implements SpecialProductsRemotePort {
  public constructor(private readonly transport: HbposTransport) {}

  public async getPage(input: Readonly<{
    storeCode: string;
    cursor: string | null;
    pageSize: number;
  }>) {
    const storeCode = requestIdentity(input.storeCode, "storeCode");
    const cursor = requestCursor(input.cursor);
    const pageSize = requestPageSize(input.pageSize);
    const response = await this.transport.request<
      HbposEnvelope<GeneratedPage>
    >({
      method: "GET",
      url: "/api/v1/catalog/special-products/page",
      params: {
        storeCode,
        cursor: cursor ?? undefined,
        pageSize,
      },
    });
    const page = unwrapHbposEnvelope(response.data);
    const responseStoreCode = responseIdentity(
      page.storeCode,
      "response.storeCode",
    );
    if (responseStoreCode !== storeCode) {
      throw invalidResponse("response.storeCode");
    }
    responseTimestamp(page.generatedAt, "response.generatedAt");

    const responseCursor = responseCursorValue(
      page.cursor,
      "response.cursor",
    );
    if (responseCursor !== cursor) {
      throw invalidResponse("response.cursor");
    }
    const items = responseArray(page.items, "response.items").map((item) =>
      normalizeItem(item, {
        isSpecialProduct: true,
        storeCode,
      }),
    );
    const hasMore = responseBoolean(page.hasMore, "response.hasMore");
    const nextCursor = responseCursorValue(
      page.nextCursor,
      "response.nextCursor",
    );
    const totalCount = responseNonNegativeInteger(
      page.totalCount,
      "response.totalCount",
    );
    if (totalCount < items.length) {
      throw invalidResponse("response.totalCount");
    }
    if (hasMore) {
      if (
        items.length === 0 ||
        nextCursor === null ||
        nextCursor === responseCursor
      ) {
        throw invalidResponse("response.nextCursor");
      }
    } else if (nextCursor !== null) {
      throw invalidResponse("response.nextCursor");
    }

    return Object.freeze({
      items: Object.freeze(items),
      nextCursor,
      hasMore,
      totalCount,
    });
  }

  public async mark(input: Readonly<{
    storeCode: string;
    productCode: string;
    isSpecialProduct: boolean;
  }>): Promise<readonly RemoteSpecialProduct[]> {
    const storeCode = requestIdentity(input.storeCode, "storeCode");
    const productCode = requestIdentity(input.productCode, "productCode");
    if (typeof input.isSpecialProduct !== "boolean") {
      throw invalidRequest("isSpecialProduct");
    }
    const request: GeneratedMarkRequest = {
      storeCode,
      productCode,
      isSpecialProduct: input.isSpecialProduct,
    };
    const response = await this.transport.request<
      HbposEnvelope<GeneratedMarkResponse>
    >({
      method: "POST",
      url: "/api/v1/catalog/special-products/mark",
      data: request,
    });
    const result = unwrapHbposEnvelope(response.data);
    if (
      responseIdentity(result.storeCode, "response.storeCode") !==
      storeCode
    ) {
      throw invalidResponse("response.storeCode");
    }
    if (
      responseIdentity(result.productCode, "response.productCode") !==
      productCode
    ) {
      throw invalidResponse("response.productCode");
    }
    if (
      responseBoolean(
        result.isSpecialProduct,
        "response.isSpecialProduct",
      ) !== input.isSpecialProduct
    ) {
      throw invalidResponse("response.isSpecialProduct");
    }
    responseTimestamp(result.generatedAt, "response.generatedAt");
    const items = responseArray(result.items, "response.items").map((item) =>
      normalizeItem(item, {
        isSpecialProduct: input.isSpecialProduct,
        productCode,
        storeCode,
      }),
    );
    return Object.freeze(items);
  }
}

function normalizeItem(
  item: GeneratedItem,
  expected: Readonly<{
    isSpecialProduct: boolean;
    productCode?: string;
    storeCode: string;
  }>,
): RemoteSpecialProduct {
  const storeCode = responseIdentity(item.storeCode, "item.storeCode");
  if (storeCode !== expected.storeCode) {
    throw invalidResponse("item.storeCode");
  }
  const productCode = responseIdentity(
    item.productCode,
    "item.productCode",
  );
  if (
    expected.productCode !== undefined &&
    productCode !== expected.productCode
  ) {
    throw invalidResponse("item.productCode");
  }
  const isSpecialProduct = responseBoolean(
    item.isSpecialProduct,
    "item.isSpecialProduct",
  );
  if (isSpecialProduct !== expected.isSpecialProduct) {
    throw invalidResponse("item.isSpecialProduct");
  }

  return Object.freeze({
    storeCode,
    productCode,
    referenceCode: responseOptionalText(
      item.referenceCode,
      "item.referenceCode",
    ),
    itemNumber: responseOptionalText(item.itemNumber, "item.itemNumber"),
    displayName: responseText(item.displayName, "item.displayName"),
    barcode: responseOptionalText(item.barcode, "item.barcode"),
    lookupCode: responseText(item.lookupCode, "item.lookupCode"),
    retailPriceCents: responseMoneyCents(
      item.retailPrice,
      "item.retailPrice",
    ),
    priceSource: responsePriceSource(item.priceSource),
    quantityFactor: responsePositiveNumber(
      item.quantityFactor,
      "item.quantityFactor",
    ),
    productImage: responseOptionalText(
      item.productImage,
      "item.productImage",
    ),
    discountRate: responseOptionalFiniteNumber(
      item.discountRate,
      "item.discountRate",
    ),
  });
}

function requestIdentity(value: unknown, field: string): string {
  if (typeof value !== "string") throw invalidRequest(field);
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 128 ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw invalidRequest(field);
  }
  return normalized;
}

function requestCursor(value: unknown): string | null {
  if (value === null) return null;
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 2_048 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw invalidRequest("cursor");
  }
  return value;
}

function requestPageSize(value: unknown): number {
  if (!Number.isSafeInteger(value) || Number(value) < 1 || Number(value) > 500) {
    throw invalidRequest("pageSize");
  }
  return Number(value);
}

function responseIdentity(value: unknown, field: string): string {
  const text = responseText(value, field);
  if (text.length > 128) throw invalidResponse(field);
  return text;
}

function responseText(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 4_096 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw invalidResponse(field);
  }
  return value;
}

function responseOptionalText(value: unknown, field: string): string | null {
  if (value === null || value === undefined || value === "") return null;
  return responseText(value, field);
}

function responseCursorValue(
  value: unknown,
  field: string,
): string | null {
  if (value === null || value === undefined) return null;
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 2_048 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw invalidResponse(field);
  }
  return value;
}

function responseTimestamp(value: unknown, field: string): string {
  const text = responseText(value, field);
  if (Number.isNaN(new Date(text).valueOf())) {
    throw invalidResponse(field);
  }
  return text;
}

function responseArray<T>(
  value: readonly T[] | null | undefined,
  field: string,
): readonly T[] {
  if (!Array.isArray(value)) throw invalidResponse(field);
  return value;
}

function responseBoolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") throw invalidResponse(field);
  return value;
}

function responseNonNegativeInteger(value: unknown, field: string): number {
  if (!Number.isSafeInteger(value) || Number(value) < 0) {
    throw invalidResponse(field);
  }
  return Number(value);
}

function responseMoneyCents(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0) {
    throw invalidResponse(field);
  }
  const scaled = value * 100;
  const cents = Math.round(scaled);
  if (
    !Number.isSafeInteger(cents) ||
    Math.abs(scaled - cents) > Number.EPSILON * Math.max(100, Math.abs(scaled))
  ) {
    throw invalidResponse(field);
  }
  return cents;
}

function responsePositiveNumber(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    throw invalidResponse(field);
  }
  return value;
}

function responseOptionalFiniteNumber(
  value: unknown,
  field: string,
): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw invalidResponse(field);
  }
  return value;
}

function responsePriceSource(
  value: unknown,
): components["schemas"]["PriceSourceKind"] {
  if (value !== 0 && value !== 1 && value !== 2 && value !== 3 && value !== 4) {
    throw invalidResponse("item.priceSource");
  }
  return value;
}

function invalidRequest(field: string): HbposApiError {
  return new HbposApiError(
    `Special products request field is invalid: ${field}.`,
    {
      kind: "envelope",
      code: "SPECIAL_PRODUCTS_REQUEST_INVALID",
    },
  );
}

function invalidResponse(field: string): HbposApiError {
  return new HbposApiError(
    `Special products response field is invalid: ${field}.`,
    {
      kind: "envelope",
      code: "SPECIAL_PRODUCTS_RESPONSE_INVALID",
    },
  );
}
