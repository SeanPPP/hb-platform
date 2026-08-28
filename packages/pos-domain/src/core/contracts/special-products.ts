import type { BackendPriceSource } from "./line-sync-provenance";

export type SpecialProductItem = Readonly<{
  storeCode: string;
  productCode: string;
  referenceCode: string | null;
  itemNumber: string | null;
  displayName: string;
  barcode: string | null;
  lookupCode: string;
  retailPriceCents: number;
  /** 与 WPF SellableItemDto.PriceSource 原值一致，订单补传不得按当前目录反推。 */
  priceSource: BackendPriceSource;
  quantityFactor: number;
  productImage: string | null;
  discountRate: number | null;
  sortOrder: number;
}>;

export type SpecialProductDownloadPage = Readonly<{
  items: readonly Omit<SpecialProductItem, "sortOrder">[];
  nextCursor: string | null;
  hasMore: boolean;
  totalCount: number;
}>;

export interface SpecialProductsRemotePort {
  getPage(input: Readonly<{
    storeCode: string;
    cursor: string | null;
    pageSize: number;
  }>): Promise<SpecialProductDownloadPage>;
  mark(input: Readonly<{
    storeCode: string;
    productCode: string;
    isSpecialProduct: boolean;
  }>): Promise<readonly Omit<SpecialProductItem, "sortOrder">[]>;
}

export interface SpecialProductsRepositoryPort {
  list(
    storeCode: string,
    limit: number,
    offset: number,
  ): Promise<readonly SpecialProductItem[]>;
  searchCandidates(
    storeCode: string,
    query: string,
    limit: number,
  ): Promise<readonly SpecialProductItem[]>;
  replaceDownloaded(
    storeCode: string,
    items: readonly Omit<SpecialProductItem, "sortOrder">[],
  ): Promise<void>;
  applyMark(
    storeCode: string,
    productCode: string,
    isSpecialProduct: boolean,
    items: readonly Omit<SpecialProductItem, "sortOrder">[],
  ): Promise<void>;
  saveOrder(storeCode: string, orderedProductCodes: readonly string[]): Promise<void>;
}

export function normalizeSpecialProductOrder(
  orderedProductCodes: readonly string[],
  availableProductCodes: ReadonlySet<string>,
): readonly string[] {
  const normalized = orderedProductCodes.map((value) =>
    requiredProductCode(value),
  );
  if (
    normalized.length !== availableProductCodes.size ||
    new Set(normalized).size !== normalized.length ||
    normalized.some((productCode) => !availableProductCodes.has(productCode))
  ) {
    throw new TypeError(
      "Special product order must be a complete unique permutation.",
    );
  }
  return Object.freeze(normalized);
}

function requiredProductCode(value: unknown): string {
  if (typeof value !== "string") {
    throw new TypeError("Special product code is invalid.");
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 128 ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError("Special product code is invalid.");
  }
  return normalized;
}
