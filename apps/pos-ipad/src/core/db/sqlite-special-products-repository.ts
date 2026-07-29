import {
  normalizeSpecialProductOrder,
  type SpecialProductItem,
  type SpecialProductsRepositoryPort,
} from "../contracts/special-products";

import type { SqliteConnectionPort, SqlValue } from "./types";

type SpecialProductRow = Readonly<{
  store_code: unknown;
  product_code: unknown;
  reference_code: unknown;
  item_number: unknown;
  display_name: unknown;
  barcode: unknown;
  lookup_code: unknown;
  retail_price_cents: unknown;
  price_source: unknown;
  quantity_factor: unknown;
  product_image: unknown;
  discount_rate: unknown;
  sort_order: unknown;
}>;

type ProductCodeRow = Readonly<{
  product_code: unknown;
  sort_order: unknown;
}>;

type MaxSortRow = Readonly<{ max_sort_order: unknown }>;

type WritableSpecialProduct = Omit<SpecialProductItem, "sortOrder">;

export class SqliteSpecialProductsRepository
  implements SpecialProductsRepositoryPort
{
  public constructor(private readonly connection: SqliteConnectionPort) {}

  public async list(
    storeCode: string,
    limit: number,
    offset: number,
  ): Promise<readonly SpecialProductItem[]> {
    const store = boundedText(storeCode, "store code", 128);
    const normalizedLimit = pageLimit(limit);
    const normalizedOffset = pageOffset(offset);
    const rows = await this.connection.getAll<SpecialProductRow>(
      `${selectStoredItems()}
       WHERE store_code = ?
       ORDER BY sort_order, product_code COLLATE NOCASE
       LIMIT ? OFFSET ?`,
      [store, normalizedLimit, normalizedOffset],
    );
    return Object.freeze(rows.map(mapItem));
  }

  public async searchCandidates(
    storeCode: string,
    query: string,
    limit: number,
  ): Promise<readonly SpecialProductItem[]> {
    const store = boundedText(storeCode, "store code", 128);
    const normalizedLimit = pageLimit(limit);
    if (typeof query !== "string") {
      throw new TypeError("Special product search query is invalid.");
    }
    const normalizedQuery = query.trim();
    if (normalizedQuery.length === 0) {
      return Object.freeze([]);
    }
    if (normalizedQuery.length > 256) {
      throw new TypeError("Special product search query is invalid.");
    }
    const pattern = `%${escapeLike(normalizedQuery)}%`;
    const rows = await this.connection.getAll<SpecialProductRow>(
      `WITH candidates AS (
         SELECT
           item.store_code,
           item.product_code,
           item.reference_code,
           item.item_number,
           item.display_name,
           item.barcode,
           item.lookup_code,
           item.retail_price_cents,
           item.price_source,
           item.quantity_factor,
           item.product_image,
           item.discount_rate,
           ROW_NUMBER() OVER (
             PARTITION BY item.store_code, item.product_code
             ORDER BY
               CASE
                 WHEN item.lookup_code = item.product_code THEN 0
                 WHEN item.lookup_code = COALESCE(item.item_number, '') THEN 1
                 WHEN item.lookup_code = COALESCE(item.barcode, '') THEN 2
                 ELSE 3
               END,
               item.lookup_code COLLATE NOCASE,
               item.lookup_code_normalized
           ) AS candidate_rank
         FROM catalog_snapshots snapshot
         INNER JOIN catalog_items item
           ON item.snapshot_id = snapshot.snapshot_id
         WHERE snapshot.state = 'active'
           AND item.is_active = 1
           AND item.store_code = ?
           AND NOT EXISTS (
             SELECT 1
             FROM local_special_products special
             WHERE special.store_code = item.store_code
               AND special.product_code = item.product_code
           )
           AND (
             item.display_name LIKE ? ESCAPE '\\'
             OR item.product_code LIKE ? ESCAPE '\\'
             OR COALESCE(item.reference_code, '') LIKE ? ESCAPE '\\'
             OR COALESCE(item.item_number, '') LIKE ? ESCAPE '\\'
             OR COALESCE(item.barcode, '') LIKE ? ESCAPE '\\'
             OR item.lookup_code LIKE ? ESCAPE '\\'
           )
       )
       SELECT
         store_code, product_code, reference_code, item_number,
         display_name, barcode, lookup_code, retail_price_cents,
         price_source, quantity_factor, product_image, discount_rate,
         -1 AS sort_order
       FROM candidates
       WHERE candidate_rank = 1
       ORDER BY
         display_name COLLATE NOCASE,
         product_code COLLATE NOCASE,
         lookup_code COLLATE NOCASE
       LIMIT ?`,
      [
        store,
        pattern,
        pattern,
        pattern,
        pattern,
        pattern,
        pattern,
        normalizedLimit,
      ],
    );
    return Object.freeze(rows.map(mapItem));
  }

  public replaceDownloaded(
    storeCode: string,
    items: readonly WritableSpecialProduct[],
  ): Promise<void> {
    const store = boundedText(storeCode, "store code", 128);
    const normalizedItems = deduplicateItems(store, items);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const existingRows = await readProductCodes(transaction, store);
      const byProduct = new Map(
        normalizedItems.map((item) => [item.productCode, item]),
      );
      const ordered: WritableSpecialProduct[] = [];
      for (const existing of existingRows) {
        const item = byProduct.get(existing.productCode);
        if (!item) continue;
        ordered.push(item);
        byProduct.delete(existing.productCode);
      }
      for (const item of normalizedItems) {
        if (!byProduct.has(item.productCode)) continue;
        ordered.push(item);
        byProduct.delete(item.productCode);
      }

      await transaction.run(
        "DELETE FROM local_special_products WHERE store_code = ?",
        [store],
      );
      for (const [sortOrder, item] of ordered.entries()) {
        await insertItem(transaction, item, sortOrder);
      }
    });
  }

  public applyMark(
    storeCode: string,
    productCode: string,
    isSpecialProduct: boolean,
    items: readonly WritableSpecialProduct[],
  ): Promise<void> {
    const store = boundedText(storeCode, "store code", 128);
    const product = boundedText(productCode, "special product code", 128);
    if (typeof isSpecialProduct !== "boolean") {
      throw new TypeError("Special product mark is invalid.");
    }
    const normalizedItems = deduplicateItems(store, items);
    if (
      normalizedItems.some((item) => item.productCode !== product)
    ) {
      throw new TypeError(
        "Special product mark payload does not match product code.",
      );
    }
    const selected = normalizedItems[0] ?? null;
    if (isSpecialProduct && !selected) {
      throw new TypeError("Marked special product requires item facts.");
    }

    return this.connection.withExclusiveTransaction(async (transaction) => {
      if (!isSpecialProduct) {
        await transaction.run(
          `DELETE FROM local_special_products
           WHERE store_code = ? AND product_code = ?`,
          [store, product],
        );
        const remaining = await readProductCodes(transaction, store);
        await persistOrder(
          transaction,
          store,
          remaining.map((entry) => entry.productCode),
        );
        return;
      }

      const current = await transaction.getFirst<ProductCodeRow>(
        `SELECT product_code, sort_order
         FROM local_special_products
         WHERE store_code = ? AND product_code = ?`,
        [store, product],
      );
      const sortOrder =
        current === null
          ? await nextSortOrder(transaction, store)
          : nonNegativeInteger(
              current.sort_order,
              "special product sort order",
            );
      await transaction.run(
        `INSERT INTO local_special_products (
          store_code, product_code, reference_code, item_number,
          display_name, barcode, lookup_code, retail_price_cents,
          price_source, quantity_factor, product_image, discount_rate,
          sort_order
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(store_code, product_code) DO UPDATE SET
          reference_code = excluded.reference_code,
          item_number = excluded.item_number,
          display_name = excluded.display_name,
          barcode = excluded.barcode,
          lookup_code = excluded.lookup_code,
          retail_price_cents = excluded.retail_price_cents,
          price_source = excluded.price_source,
          quantity_factor = excluded.quantity_factor,
          product_image = excluded.product_image,
          discount_rate = excluded.discount_rate`,
        itemParameters(selected!, sortOrder),
      );
    });
  }

  public saveOrder(
    storeCode: string,
    orderedProductCodes: readonly string[],
  ): Promise<void> {
    const store = boundedText(storeCode, "store code", 128);
    return this.connection.withExclusiveTransaction(async (transaction) => {
      const available = await readProductCodes(transaction, store);
      const availableCodes = new Set(
        available.map((entry) => entry.productCode),
      );
      const normalized = normalizeSpecialProductOrder(
        orderedProductCodes,
        availableCodes,
      );
      await persistOrder(transaction, store, normalized);
    });
  }
}

async function insertItem(
  connection: SqliteConnectionPort,
  item: WritableSpecialProduct,
  sortOrder: number,
): Promise<void> {
  await connection.run(
    `INSERT INTO local_special_products (
      store_code, product_code, reference_code, item_number,
      display_name, barcode, lookup_code, retail_price_cents,
      price_source, quantity_factor, product_image, discount_rate, sort_order
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    itemParameters(item, sortOrder),
  );
}

function itemParameters(
  item: WritableSpecialProduct,
  sortOrder: number,
): readonly SqlValue[] {
  return [
    item.storeCode,
    item.productCode,
    item.referenceCode,
    item.itemNumber,
    item.displayName,
    item.barcode,
    item.lookupCode,
    item.retailPriceCents,
    item.priceSource,
    formatDecimal(item.quantityFactor, "quantity factor"),
    item.productImage,
    item.discountRate === null
      ? null
      : formatDecimal(item.discountRate, "discount rate"),
    nonNegativeInteger(sortOrder, "special product sort order"),
  ];
}

async function readProductCodes(
  connection: SqliteConnectionPort,
  storeCode: string,
): Promise<readonly Readonly<{ productCode: string; sortOrder: number }>[]> {
  const rows = await connection.getAll<ProductCodeRow>(
    `SELECT product_code, sort_order
     FROM local_special_products
     WHERE store_code = ?
     ORDER BY sort_order, product_code COLLATE NOCASE`,
    [storeCode],
  );
  return rows.map((row) =>
    Object.freeze({
      productCode: boundedText(
        row.product_code,
        "special product code",
        128,
      ),
      sortOrder: nonNegativeInteger(
        row.sort_order,
        "special product sort order",
      ),
    }),
  );
}

async function persistOrder(
  connection: SqliteConnectionPort,
  storeCode: string,
  orderedProductCodes: readonly string[],
): Promise<void> {
  if (orderedProductCodes.length === 0) return;
  const maximum = await connection.getFirst<MaxSortRow>(
    `SELECT COALESCE(MAX(sort_order), -1) AS max_sort_order
     FROM local_special_products
     WHERE store_code = ?`,
    [storeCode],
  );
  const maxSort = Number(maximum?.max_sort_order ?? -1);
  if (!Number.isSafeInteger(maxSort) || maxSort < -1) {
    throw new Error("Special product sort state is invalid.");
  }
  const offset = maxSort + orderedProductCodes.length + 1;
  if (!Number.isSafeInteger(offset)) {
    throw new Error("Special product sort state is invalid.");
  }
  await connection.run(
    `UPDATE local_special_products
     SET sort_order = sort_order + ?
     WHERE store_code = ?`,
    [offset, storeCode],
  );
  for (const [sortOrder, productCode] of orderedProductCodes.entries()) {
    const updated = await connection.run(
      `UPDATE local_special_products
       SET sort_order = ?
       WHERE store_code = ? AND product_code = ?`,
      [sortOrder, storeCode, productCode],
    );
    if (updated.changes !== 1) {
      throw new Error("Special product order changed during save.");
    }
  }
}

async function nextSortOrder(
  connection: SqliteConnectionPort,
  storeCode: string,
): Promise<number> {
  const row = await connection.getFirst<MaxSortRow>(
    `SELECT COALESCE(MAX(sort_order), -1) AS max_sort_order
     FROM local_special_products
     WHERE store_code = ?`,
    [storeCode],
  );
  const maximum = Number(row?.max_sort_order ?? -1);
  if (!Number.isSafeInteger(maximum) || maximum < -1) {
    throw new Error("Special product sort state is invalid.");
  }
  const next = maximum + 1;
  if (!Number.isSafeInteger(next)) {
    throw new Error("Special product sort state is invalid.");
  }
  return next;
}

function deduplicateItems(
  storeCode: string,
  items: readonly WritableSpecialProduct[],
): readonly WritableSpecialProduct[] {
  if (!Array.isArray(items)) {
    throw new TypeError("Special product items are invalid.");
  }
  const order: string[] = [];
  const byProduct = new Map<string, WritableSpecialProduct>();
  for (const raw of items) {
    const item = validateItem(raw);
    if (item.storeCode !== storeCode) {
      throw new TypeError("Special product item store does not match.");
    }
    const current = byProduct.get(item.productCode);
    if (!current) {
      order.push(item.productCode);
      byProduct.set(item.productCode, item);
      continue;
    }
    if (itemSortKey(item) < itemSortKey(current)) {
      byProduct.set(item.productCode, item);
    }
  }
  return Object.freeze(
    order.map((productCode) => {
      const item = byProduct.get(productCode);
      if (!item) {
        throw new Error("Special product deduplication failed.");
      }
      return item;
    }),
  );
}

function validateItem(input: WritableSpecialProduct): WritableSpecialProduct {
  if (!input || typeof input !== "object") {
    throw new TypeError("Special product item is invalid.");
  }
  const quantityFactor = finiteNumber(
    input.quantityFactor,
    "quantity factor",
  );
  if (quantityFactor <= 0) {
    throw new TypeError("Special product quantity factor is invalid.");
  }
  return Object.freeze({
    storeCode: boundedText(input.storeCode, "store code", 128),
    productCode: boundedText(
      input.productCode,
      "special product code",
      128,
    ),
    referenceCode: optionalText(input.referenceCode, "reference code", 256),
    itemNumber: optionalText(input.itemNumber, "item number", 256),
    displayName: boundedText(input.displayName, "display name", 512),
    barcode: optionalText(input.barcode, "barcode", 256),
    lookupCode: boundedText(input.lookupCode, "lookup code", 256),
    retailPriceCents: safeInteger(
      input.retailPriceCents,
      "retail price",
    ),
    priceSource: priceSource(input.priceSource),
    quantityFactor,
    productImage: optionalText(input.productImage, "product image", 2048),
    discountRate:
      input.discountRate === null
        ? null
        : finiteNumber(input.discountRate, "discount rate"),
  });
}

function mapItem(row: SpecialProductRow): SpecialProductItem {
  const quantityFactor = finiteNumber(
    row.quantity_factor,
    "quantity factor",
  );
  if (quantityFactor <= 0) {
    throw new Error("Special product quantity factor is invalid.");
  }
  return Object.freeze({
    storeCode: boundedText(row.store_code, "store code", 128),
    productCode: boundedText(
      row.product_code,
      "special product code",
      128,
    ),
    referenceCode: optionalText(row.reference_code, "reference code", 256),
    itemNumber: optionalText(row.item_number, "item number", 256),
    displayName: boundedText(row.display_name, "display name", 512),
    barcode: optionalText(row.barcode, "barcode", 256),
    lookupCode: boundedText(row.lookup_code, "lookup code", 256),
    retailPriceCents: safeInteger(row.retail_price_cents, "retail price"),
    priceSource: priceSource(row.price_source),
    quantityFactor,
    productImage: optionalText(row.product_image, "product image", 2048),
    discountRate:
      row.discount_rate === null || row.discount_rate === undefined
        ? null
        : finiteNumber(row.discount_rate, "discount rate"),
    sortOrder: safeInteger(row.sort_order, "special product sort order"),
  });
}

function selectStoredItems(): string {
  return `SELECT
    store_code, product_code, reference_code, item_number,
    display_name, barcode, lookup_code, retail_price_cents,
    price_source, quantity_factor, product_image, discount_rate, sort_order
    FROM local_special_products`;
}

function itemSortKey(item: WritableSpecialProduct): string {
  return [
    item.lookupCode.toLocaleLowerCase("en-AU"),
    item.itemNumber?.toLocaleLowerCase("en-AU") ?? "",
    item.barcode?.toLocaleLowerCase("en-AU") ?? "",
    item.referenceCode?.toLocaleLowerCase("en-AU") ?? "",
  ].join("\u0000");
}

function pageLimit(value: unknown): number {
  const limit = safeInteger(value, "special product page limit");
  if (limit <= 0 || limit > 5_000) {
    throw new TypeError("Special product page limit is invalid.");
  }
  return limit;
}

function pageOffset(value: unknown): number {
  return nonNegativeInteger(value, "special product page offset");
}

function boundedText(value: unknown, label: string, maxLength: number): string {
  if (typeof value !== "string") {
    throw new TypeError(`Special product ${label} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`Special product ${label} is invalid.`);
  }
  return normalized;
}

function optionalText(
  value: unknown,
  label: string,
  maxLength: number,
): string | null {
  if (value === null || value === undefined) return null;
  return boundedText(value, label, maxLength);
}

function safeInteger(value: unknown, label: string): number {
  const numeric = Number(value);
  if (!Number.isSafeInteger(numeric)) {
    throw new TypeError(`Special product ${label} must be a safe integer.`);
  }
  return numeric;
}

function nonNegativeInteger(value: unknown, label: string): number {
  const numeric = safeInteger(value, label);
  if (numeric < 0) {
    throw new TypeError(`Special product ${label} must be non-negative.`);
  }
  return numeric;
}

function finiteNumber(value: unknown, label: string): number {
  const numeric =
    typeof value === "number"
      ? value
      : typeof value === "string"
        ? Number(value)
        : Number.NaN;
  if (!Number.isFinite(numeric)) {
    throw new TypeError(`Special product ${label} must be finite.`);
  }
  return Object.is(numeric, -0) ? 0 : numeric;
}

function priceSource(value: unknown): 0 | 1 | 2 | 3 | 4 {
  const numeric = Number(value);
  if (
    numeric === 0 ||
    numeric === 1 ||
    numeric === 2 ||
    numeric === 3 ||
    numeric === 4
  ) {
    return numeric;
  }
  throw new TypeError("Special product price source is invalid.");
}

function formatDecimal(value: number, label: string): string {
  return String(finiteNumber(value, label));
}

function escapeLike(value: string): string {
  return value.replace(/[\\%_]/gu, "\\$&");
}
