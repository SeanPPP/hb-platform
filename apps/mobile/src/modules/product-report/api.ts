import type { ProductReportDateRange } from "./date-ranges";
import { getDashboardCompareMode, getProductReportCompareRange } from "./date-ranges";
import { PRODUCT_PAGE_SIZE } from "./pagination";
import { REPORT_QUERY_TIMEOUT_MS } from "../reports/report-config";

export type SupplierReportKind = "australia" | "china";

export function getProductReportRequestConfig() {
  return { timeout: REPORT_QUERY_TIMEOUT_MS } as const;
}

export interface ProductReportDateQuery {
  startDate: string;
  endDate: string;
  compareStartDate: string;
  compareEndDate: string;
  compareMode: "ByDate" | "ByWeek";
  branchCodes?: string[];
}

export interface ProductReportStoreOption {
  label: string;
  value: string;
}

export interface ProductReportTotalRevenue {
  revenue: number;
  compareRevenue: number;
}

export interface SupplierReportRow {
  id: string;
  supplierCode: string;
  supplierName: string;
  revenue: number;
  compareRevenue: number;
  totalQuantity: number;
  storeCount: number;
  orderCount: number;
  compareOrderCount: number;
  averageTransaction: number;
  compareAverageTransaction: number;
}

export interface ProductReportProductRow {
  id: string;
  productCode: string;
  itemNumber: string;
  productImage: string | null;
  productName: string;
  quantity: number;
  compareQuantity: number;
  salesAmount: number;
  compareSalesAmount: number;
  averageUnitPrice: number;
  compareAverageUnitPrice: number;
  orderCount: number;
  compareOrderCount: number;
}

export interface ProductReportProductPage {
  rows: ProductReportProductRow[];
  total: number;
  pageIndex: number;
  pageSize: number;
}

export interface SupplierBranchBreakdownRow {
  id: string;
  branchCode: string;
  branchName: string;
  supplierCode: string;
  supplierName: string;
  revenue: number;
  compareRevenue: number;
  totalQuantity: number;
  orderCount: number;
  compareOrderCount: number;
  averageTransaction: number;
  compareAverageTransaction: number;
}

export interface ProductBranchBreakdownRow {
  id: string;
  branchCode: string;
  branchName: string;
  quantity: number;
  compareQuantity: number;
  discountedQuantity: number;
  salesAmount: number;
  compareSalesAmount: number;
  averageUnitPrice: number;
  compareAverageUnitPrice: number;
}

async function getApiClient() {
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function pick(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) {
      return raw[key];
    }
  }
  return undefined;
}

function asString(value: unknown, fallback = "") {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return fallback;
}

function asNumber(value: unknown, fallback = 0) {
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function getRows(payload: unknown): unknown[] {
  if (Array.isArray(payload)) {
    return payload;
  }

  const root = asRecord(payload) ?? {};
  const data = pick(root, "items", "Items", "rows", "Rows", "data", "Data");
  if (Array.isArray(data)) {
    return data;
  }

  const nested = asRecord(data);
  return nested ? getRows(nested) : [];
}

function appendListParams(params: URLSearchParams, key: string, values?: string[]) {
  values?.filter(Boolean).forEach((value) => params.append(key, value));
}

function buildBaseParams(query: ProductReportDateQuery) {
  const params = new URLSearchParams({
    startDate: query.startDate,
    endDate: query.endDate,
    compareStartDate: query.compareStartDate,
    compareEndDate: query.compareEndDate,
    compareMode: query.compareMode,
  });
  appendListParams(params, "branchCodes", query.branchCodes);
  return params;
}

export function buildProductReportDateQuery(
  range: ProductReportDateRange,
  branchCodes?: string[]
): ProductReportDateQuery {
  const compare = getProductReportCompareRange(range);
  return {
    startDate: range.startDate,
    endDate: range.endDate,
    compareStartDate: compare.startDate,
    compareEndDate: compare.endDate,
    compareMode: getDashboardCompareMode(range),
    branchCodes,
  };
}

export function normalizeStoreOptions(payload: unknown): ProductReportStoreOption[] {
  return getRows(payload)
    .map((raw, index) => {
      const item = asRecord(raw) ?? {};
      const value = asString(pick(item, "value", "Value", "storeCode", "StoreCode"), String(index));
      return {
        value,
        label: asString(pick(item, "label", "Label", "storeName", "StoreName"), value),
      };
    })
    .filter((item) => item.value.length > 0);
}

export function normalizeSupplierRows(payload: unknown): SupplierReportRow[] {
  return getRows(payload).map((raw, index) => {
    const item = asRecord(raw) ?? {};
    const supplierCode = asString(pick(item, "supplierCode", "SupplierCode"), `supplier-${index}`);
    const revenue = asNumber(pick(item, "totalAmount", "TotalAmount", "revenue", "Revenue"));
    const orderCount = asNumber(pick(item, "orderCount", "OrderCount", "transactions", "Transactions"));
    const compareRevenue = asNumber(pick(item, "compareTotalAmount", "CompareTotalAmount", "revenueLY", "RevenueLY"));
    const compareOrderCount = asNumber(pick(item, "compareOrderCount", "CompareOrderCount", "orderCountLY", "OrderCountLY"));
    return {
      id: supplierCode || String(index),
      supplierCode,
      supplierName: asString(pick(item, "supplierName", "SupplierName"), supplierCode),
      revenue,
      compareRevenue,
      totalQuantity: asNumber(pick(item, "totalQuantity", "TotalQuantity")),
      storeCount: asNumber(pick(item, "storeCount", "StoreCount")),
      orderCount,
      compareOrderCount,
      averageTransaction: asNumber(
        pick(item, "averageTransaction", "AverageTransaction", "aov", "Aov"),
        orderCount > 0 ? revenue / orderCount : 0
      ),
      compareAverageTransaction: asNumber(
        pick(item, "compareAverageTransaction", "CompareAverageTransaction", "aovLY", "AovLY"),
        compareOrderCount > 0 ? compareRevenue / compareOrderCount : 0
      ),
    };
  });
}

export function normalizeProductPage(payload: unknown): ProductReportProductPage {
  const root = asRecord(payload) ?? {};
  const rows = getRows(payload).map((raw, index) => {
    const item = asRecord(raw) ?? {};
    const productCode = asString(pick(item, "productCode", "ProductCode"), `product-${index}`);
    const salesAmount = asNumber(pick(item, "salesAmount", "SalesAmount", "amount", "Amount"));
    return {
      id: productCode || String(index),
      productCode,
      itemNumber: asString(pick(item, "itemNumber", "ItemNumber", "barcode", "Barcode")),
      productImage: asString(pick(item, "productImage", "ProductImage"), "") || null,
      productName: asString(pick(item, "productName", "ProductName")),
      quantity: asNumber(pick(item, "quantity", "Quantity")),
      compareQuantity: asNumber(pick(item, "compareQuantity", "CompareQuantity", "quantityLY", "QuantityLY")),
      salesAmount,
      compareSalesAmount: asNumber(
        pick(item, "compareSalesAmount", "CompareSalesAmount", "salesAmountLY", "SalesAmountLY")
      ),
      averageUnitPrice: asNumber(pick(item, "averageUnitPrice", "AverageUnitPrice", "unitPrice", "UnitPrice")),
      compareAverageUnitPrice: asNumber(
        pick(item, "compareAverageUnitPrice", "CompareAverageUnitPrice", "averageUnitPriceLY", "AverageUnitPriceLY")
      ),
      orderCount: asNumber(pick(item, "orderCount", "OrderCount")),
      compareOrderCount: asNumber(pick(item, "compareOrderCount", "CompareOrderCount", "orderCountLY", "OrderCountLY")),
    };
  });

  return {
    rows,
    total: asNumber(pick(root, "total", "Total"), rows.length),
    pageIndex: asNumber(pick(root, "pageIndex", "PageIndex"), 1),
    pageSize: asNumber(pick(root, "pageSize", "PageSize"), 50),
  };
}

export function normalizeTotalRevenue(payload: unknown): ProductReportTotalRevenue {
  return getRows(payload).reduce<ProductReportTotalRevenue>(
    (sum, raw) => {
      const item = asRecord(raw) ?? {};
      return {
        revenue: sum.revenue + asNumber(pick(item, "revenue", "Revenue", "totalAmount", "TotalAmount")),
        compareRevenue:
          sum.compareRevenue +
          asNumber(pick(item, "revenueLY", "RevenueLY", "compareRevenue", "CompareRevenue", "totalAmountLY", "TotalAmountLY")),
      };
    },
    { revenue: 0, compareRevenue: 0 }
  );
}

export function normalizeSupplierBranchRows(payload: unknown): SupplierBranchBreakdownRow[] {
  return getRows(payload).map((raw, index) => {
    const item = asRecord(raw) ?? {};
    const branchCode = asString(pick(item, "branchCode", "BranchCode"), `branch-${index}`);
    const supplierCode = asString(pick(item, "supplierCode", "SupplierCode"));
    const revenue = asNumber(pick(item, "totalAmount", "TotalAmount", "revenue", "Revenue"));
    const orderCount = asNumber(pick(item, "orderCount", "OrderCount"));
    const compareRevenue = asNumber(pick(item, "compareTotalAmount", "CompareTotalAmount", "revenueLY", "RevenueLY"));
    const compareOrderCount = asNumber(pick(item, "compareOrderCount", "CompareOrderCount"));
    return {
      id: `${branchCode}-${supplierCode || index}`,
      branchCode,
      branchName: asString(pick(item, "branchName", "BranchName", "storeName", "StoreName"), branchCode),
      supplierCode,
      supplierName: asString(pick(item, "supplierName", "SupplierName"), supplierCode),
      revenue,
      compareRevenue,
      totalQuantity: asNumber(pick(item, "totalQuantity", "TotalQuantity")),
      orderCount,
      compareOrderCount,
      averageTransaction: asNumber(
        pick(item, "averageTransaction", "AverageTransaction"),
        orderCount > 0 ? revenue / orderCount : 0
      ),
      compareAverageTransaction: asNumber(
        pick(item, "compareAverageTransaction", "CompareAverageTransaction"),
        compareOrderCount > 0 ? compareRevenue / compareOrderCount : 0
      ),
    };
  });
}

export function normalizeProductBranchRows(payload: unknown): ProductBranchBreakdownRow[] {
  return getRows(payload).map((raw, index) => {
    const item = asRecord(raw) ?? {};
    const branchCode = asString(pick(item, "branchCode", "BranchCode"), `branch-${index}`);
    const quantity = asNumber(pick(item, "quantity", "Quantity"));
    const salesAmount = asNumber(pick(item, "salesAmount", "SalesAmount"));
    const compareQuantity = asNumber(pick(item, "compareQuantity", "CompareQuantity", "quantityLY", "QuantityLY"));
    const compareSalesAmount = asNumber(
      pick(item, "compareSalesAmount", "CompareSalesAmount", "salesAmountLY", "SalesAmountLY")
    );
    return {
      id: branchCode || String(index),
      branchCode,
      branchName: asString(pick(item, "branchName", "BranchName", "storeName", "StoreName"), branchCode),
      quantity,
      compareQuantity,
      discountedQuantity: asNumber(pick(item, "discountedQuantity", "DiscountedQuantity")),
      salesAmount,
      compareSalesAmount,
      averageUnitPrice: asNumber(
        pick(item, "averageUnitPrice", "AverageUnitPrice"),
        quantity > 0 ? salesAmount / quantity : 0
      ),
      compareAverageUnitPrice: asNumber(
        pick(item, "compareAverageUnitPrice", "CompareAverageUnitPrice", "averageUnitPriceLY", "AverageUnitPriceLY"),
        compareQuantity > 0 ? compareSalesAmount / compareQuantity : 0
      ),
    };
  });
}

export async function fetchProductReportStoreOptions() {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/react/v1/product-movement-report/store-options");
  return normalizeStoreOptions(response.data);
}

export async function fetchProductReportTotalRevenue(query: ProductReportDateQuery) {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/react/v1/dashboard/executive-branch-performance", {
    params: buildBaseParams(query),
    ...getProductReportRequestConfig(),
  });
  return normalizeTotalRevenue(response.data);
}

export async function fetchSupplierReportRows(
  kind: SupplierReportKind,
  query: ProductReportDateQuery,
  topN = 1000
) {
  const apiClient = await getApiClient();
  const params = buildBaseParams(query);
  params.set("topN", String(topN));
  const endpoint =
    kind === "china"
      ? "/react/v1/dashboard/china-supplier-sales-rank"
      : "/react/v1/dashboard/supplier-sales-rank";
  const response = await apiClient.get(endpoint, { params, ...getProductReportRequestConfig() });
  return normalizeSupplierRows(response.data);
}

export async function fetchProductReportProductRows(
  kind: SupplierReportKind,
  query: ProductReportDateQuery,
  supplierCodes: string[] | undefined,
  pageIndex: number,
  pageSize = PRODUCT_PAGE_SIZE,
  productSearch?: string
) {
  const apiClient = await getApiClient();
  const params = buildProductReportProductParams(kind, query, supplierCodes, pageIndex, pageSize, productSearch);
  const response = await apiClient.get("/react/v1/dashboard/enhanced-sales-product-details", {
    params,
    ...getProductReportRequestConfig(),
  });
  return normalizeProductPage(response.data);
}

export function buildProductReportProductParams(
  kind: SupplierReportKind,
  query: ProductReportDateQuery,
  supplierCodes: string[] | undefined,
  pageIndex: number,
  pageSize = PRODUCT_PAGE_SIZE,
  productSearch?: string
) {
  const params = buildBaseParams(query);
  params.set("pageIndex", String(pageIndex));
  params.set("pageSize", String(pageSize));
  appendListParams(params, kind === "china" ? "chinaSupplierCodes" : "localSupplierCodes", supplierCodes);
  const normalizedProductSearch = productSearch?.trim();
  if (normalizedProductSearch) {
    params.set("productSearch", normalizedProductSearch);
  }
  return params;
}

export async function fetchSupplierBranchBreakdown(
  kind: SupplierReportKind,
  query: ProductReportDateQuery,
  supplierCode: string
) {
  const apiClient = await getApiClient();
  const params = buildBaseParams(query);
  params.append("supplierCodes", supplierCode);
  const endpoint =
    kind === "china"
      ? "/react/v1/dashboard/china-supplier-store-sales"
      : "/react/v1/dashboard/supplier-store-sales";
  const response = await apiClient.get(endpoint, { params, ...getProductReportRequestConfig() });
  return normalizeSupplierBranchRows(response.data);
}

export async function fetchProductBranchBreakdown(
  query: ProductReportDateQuery,
  productCode: string
) {
  const apiClient = await getApiClient();
  const params = buildBaseParams(query);
  params.set("productCode", productCode);
  const response = await apiClient.get("/react/v1/dashboard/product-sales-by-branches", {
    params,
    ...getProductReportRequestConfig(),
  });
  return normalizeProductBranchRows(response.data);
}
