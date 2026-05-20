import { getUserStoresApi } from "@/modules/auth/api";
import type {
  Store,
  StoreOrderCategoryNode,
  StoreOrderDynamicData,
  StoreOrderDynamicDataRequest,
  StoreOrderProductItem,
  StoreOrderProductQuery,
  StoreOrderProductListResult,
  StoreOrderScanLookupResult,
  AddToCartPayload,
  UpdateCartQuantityPayload,
  StoreOrderCart,
} from "@/modules/shop/types";
import { apiClient } from "@/shared/api/client";

type ApiItem = Record<string, unknown>;

function getFiniteNumber(...values: unknown[]) {
  for (const value of values) {
    if (value == null || value === "") {
      continue;
    }

    const parsed = Number(value);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }

  return undefined;
}

function transformProductItem(raw: ApiItem): StoreOrderProductItem {
  const stockQuantity =
    raw.stockQuantity != null
      ? Number(raw.stockQuantity)
      : raw.StockQuantity != null
        ? Number(raw.StockQuantity)
        : 0;

  return {
    productCode: String(raw.productCode ?? raw.ProductCode ?? ""),
    itemNumber: raw.itemNumber != null ? String(raw.itemNumber) : raw.ItemNumber != null ? String(raw.ItemNumber) : undefined,
    barcode: raw.barcode != null ? String(raw.barcode) : raw.Barcode != null ? String(raw.Barcode) : undefined,
    grade:
      raw.grade != null
        ? String(raw.grade)
        : raw.Grade != null
          ? String(raw.Grade)
          : raw.productGrade != null
            ? String(raw.productGrade)
            : raw.ProductGrade != null
              ? String(raw.ProductGrade)
              : undefined,
    productName: raw.productName != null ? String(raw.productName) : raw.ProductName != null ? String(raw.ProductName) : undefined,
    productImage: raw.productImage != null ? String(raw.productImage) : raw.ProductImage != null ? String(raw.ProductImage) : undefined,
    categoryName: raw.categoryName != null ? String(raw.categoryName) : raw.CategoryName != null ? String(raw.CategoryName) : undefined,
    warehouseCategoryGUID:
      raw.warehouseCategoryGUID != null
        ? String(raw.warehouseCategoryGUID)
        : raw.WarehouseCategoryGUID != null
          ? String(raw.WarehouseCategoryGUID)
          : undefined,
    oemPrice: raw.oemPrice != null ? Number(raw.oemPrice) : raw.OEMPrice != null ? Number(raw.OEMPrice) : undefined,
    minOrderQuantity:
      raw.minOrderQuantity != null
        ? Number(raw.minOrderQuantity)
        : raw.MinOrderQuantity != null
          ? Number(raw.MinOrderQuantity)
          : 1,
    stockQuantity,
    isInStock:
      raw.isInStock != null
        ? Boolean(raw.isInStock)
        : raw.IsInStock != null
          ? Boolean(raw.IsInStock)
          : stockQuantity > 0,
    packQty: raw.packQty != null ? Number(raw.packQty) : raw.PackQty != null ? Number(raw.PackQty) : undefined,
    importPrice:
      raw.importPrice != null ? Number(raw.importPrice) : raw.ImportPrice != null ? Number(raw.ImportPrice) : undefined,
  };
}

function normalizeProductPagedList(payload: Partial<StoreOrderProductListResult> | null | undefined): StoreOrderProductListResult {
  const normalizedPayload = payload as
    | ((Partial<StoreOrderProductListResult> & {
        pageNumber?: number;
        PageNumber?: number;
        Total?: number;
        PageSize?: number;
      }) & {
        items?: ApiItem[];
        Items?: ApiItem[];
      })
    | null
    | undefined;

  const items = normalizedPayload?.items ?? normalizedPayload?.Items;

  return {
    items: Array.isArray(items) ? items.map(transformProductItem) : [],
    total: normalizedPayload?.total ?? normalizedPayload?.Total ?? 0,
    page: normalizedPayload?.page ?? normalizedPayload?.pageNumber ?? normalizedPayload?.PageNumber ?? 1,
    pageSize: normalizedPayload?.pageSize ?? normalizedPayload?.PageSize ?? 24,
  };
}

function transformCartItem(raw: ApiItem) {
  return {
    detailGUID: String(raw.detailGUID ?? raw.DetailGUID ?? ""),
    productCode: String(raw.productCode ?? raw.ProductCode ?? ""),
    itemNumber: raw.itemNumber != null ? String(raw.itemNumber) : raw.ItemNumber != null ? String(raw.ItemNumber) : undefined,
    barcode: raw.barcode != null ? String(raw.barcode) : raw.Barcode != null ? String(raw.Barcode) : undefined,
    grade:
      raw.grade != null
        ? String(raw.grade)
        : raw.Grade != null
          ? String(raw.Grade)
          : raw.productGrade != null
            ? String(raw.productGrade)
            : raw.ProductGrade != null
              ? String(raw.ProductGrade)
              : undefined,
    productName: raw.productName != null ? String(raw.productName) : raw.ProductName != null ? String(raw.ProductName) : undefined,
    productImage: raw.productImage != null ? String(raw.productImage) : raw.ProductImage != null ? String(raw.ProductImage) : undefined,
    price: Number(raw.price ?? raw.Price ?? 0),
    quantity: Number(raw.quantity ?? raw.Quantity ?? 0),
    allocQuantity:
      raw.allocQuantity != null ? Number(raw.allocQuantity) : raw.AllocQuantity != null ? Number(raw.AllocQuantity) : undefined,
    amount: Number(raw.amount ?? raw.Amount ?? 0),
    importPrice: Number(raw.importPrice ?? raw.ImportPrice ?? 0),
    importAmount: Number(raw.importAmount ?? raw.ImportAmount ?? 0),
    volume: raw.volume != null ? Number(raw.volume) : raw.Volume != null ? Number(raw.Volume) : undefined,
    totalVolume:
      raw.totalVolume != null ? Number(raw.totalVolume) : raw.TotalVolume != null ? Number(raw.TotalVolume) : undefined,
    minOrderQuantity:
      raw.minOrderQuantity != null
        ? Number(raw.minOrderQuantity)
        : raw.MinOrderQuantity != null
          ? Number(raw.MinOrderQuantity)
          : 1,
    isActive: raw.isActive != null ? Boolean(raw.isActive) : raw.IsActive != null ? Boolean(raw.IsActive) : true,
    locationCode:
      raw.locationCode != null ? String(raw.locationCode) : raw.LocationCode != null ? String(raw.LocationCode) : undefined,
    rrp: raw.rrp != null ? Number(raw.rrp) : raw.RRP != null ? Number(raw.RRP) : undefined,
    updatedAt: raw.updatedAt != null ? String(raw.updatedAt) : raw.UpdatedAt != null ? String(raw.UpdatedAt) : undefined,
  };
}

function normalizeCart(payload: Partial<StoreOrderCart> | null | undefined): StoreOrderCart | null {
  if (!payload) {
    return null;
  }

  const normalizedPayload = payload as
    | (Partial<StoreOrderCart> & {
        ImportTotal?: unknown;
        ImportTotalAmount?: unknown;
        Items?: ApiItem[];
        SKUCount?: unknown;
        SkuCount?: unknown;
        TotalAmount?: unknown;
        TotalImportAmount?: unknown;
        TotalQuantity?: unknown;
        TotalSKU?: unknown;
        TotalSku?: unknown;
        TotalVolume?: unknown;
        importTotal?: unknown;
        importTotalAmount?: unknown;
        items?: ApiItem[];
        skuCount?: unknown;
        totalSKU?: unknown;
      })
    | null
    | undefined;
  const items = normalizedPayload?.items ?? normalizedPayload?.Items;
  const cartItems = Array.isArray(items) ? items.map(transformCartItem) : [];
  const fallbackImportAmount = cartItems.reduce(
    (sum, item) => sum + (Number.isFinite(item.importAmount) ? item.importAmount : item.importPrice * item.quantity),
    0
  );
  const fallbackTotalQuantity = cartItems.reduce((sum, item) => sum + item.quantity, 0);
  const fallbackTotalSku = new Set(cartItems.map((item) => item.productCode).filter(Boolean)).size;

  return {
    orderGUID: payload.orderGUID ?? "",
    orderNo: payload.orderNo,
    storeCode: payload.storeCode,
    storeName: payload.storeName,
    totalAmount: getFiniteNumber(normalizedPayload?.totalAmount, normalizedPayload?.TotalAmount) ?? 0,
    totalQuantity:
      getFiniteNumber(normalizedPayload?.totalQuantity, normalizedPayload?.TotalQuantity) ?? fallbackTotalQuantity,
    totalImportAmount:
      getFiniteNumber(
        normalizedPayload?.totalImportAmount,
        normalizedPayload?.TotalImportAmount,
        normalizedPayload?.importTotalAmount,
        normalizedPayload?.ImportTotalAmount,
        normalizedPayload?.importTotal,
        normalizedPayload?.ImportTotal
      ) ?? fallbackImportAmount,
    totalSku:
      getFiniteNumber(
        normalizedPayload?.totalSku,
        normalizedPayload?.totalSKU,
        normalizedPayload?.TotalSku,
        normalizedPayload?.TotalSKU,
        normalizedPayload?.skuCount,
        normalizedPayload?.SkuCount,
        normalizedPayload?.SKUCount
      ) ?? fallbackTotalSku,
    totalVolume: getFiniteNumber(normalizedPayload?.totalVolume, normalizedPayload?.TotalVolume) ?? 0,
    remarks: payload.remarks,
    shippingFee: payload.shippingFee,
    orderDate: payload.orderDate,
    storeAddress: payload.storeAddress,
    flowStatus: payload.flowStatus,
    items: cartItems,
  };
}

export async function getStoresByUserGuid(userGuid: string): Promise<Store[]> {
  const stores = await getUserStoresApi(userGuid);

  return stores
    .filter((item) => item.storeCode)
    .map((item) => ({
      storeCode: item.storeCode,
      storeName: item.storeName || item.storeCode,
    }));
}

export async function getAllStores(): Promise<Store[]> {
  const response = await apiClient.get("/stores/all-by-name");
  const stores = Array.isArray(response.data) ? response.data : [];

  return stores
    .filter((item): item is { storeCode?: string; storeName?: string } => Boolean(item))
    .filter((item) => Boolean(item.storeCode))
    .map((item) => ({
      storeCode: item.storeCode!,
      storeName: item.storeName || item.storeCode!,
    }));
}

export async function getCategoryTree(): Promise<StoreOrderCategoryNode[]> {
  const response = await apiClient.get("/react/v1/warehouse-categories/tree");
  return Array.isArray(response.data) ? (response.data as StoreOrderCategoryNode[]) : [];
}

export async function getProducts(query: StoreOrderProductQuery): Promise<StoreOrderProductListResult> {
  const response = await apiClient.post("/react/v1/store-order/products", query);
  return normalizeProductPagedList(response.data as Partial<StoreOrderProductListResult>);
}

export async function getProductDynamicData(
  payload: StoreOrderDynamicDataRequest
): Promise<StoreOrderDynamicData[]> {
  const response = await apiClient.post("/react/v1/store-order/dynamic-data", payload);
  return Array.isArray(response.data) ? (response.data as StoreOrderDynamicData[]) : [];
}

export async function getCart(storeCode: string): Promise<StoreOrderCart | null> {
  const response = await apiClient.get(`/react/v1/store-order/cart/${encodeURIComponent(storeCode)}`);
  return normalizeCart(response.data as Partial<StoreOrderCart> | null);
}

export async function addToCart(payload: AddToCartPayload): Promise<StoreOrderCart | null> {
  const response = await apiClient.post("/react/v1/store-order/cart/add", payload);
  return normalizeCart(response.data as Partial<StoreOrderCart> | null);
}

export async function updateCartQuantity(payload: UpdateCartQuantityPayload): Promise<void> {
  await apiClient.post("/react/v1/store-order/cart/update", payload);
}

export async function lookupProductsByBarcode(barcode: string): Promise<StoreOrderScanLookupResult> {
  const response = await apiClient.post("/react/v1/store-order/products/scan-lookup", {
    barcode,
  });

  const data = response.data as Partial<StoreOrderScanLookupResult> | null | undefined;

  return {
    barcode: data?.barcode ?? barcode,
    items: Array.isArray(data?.items)
      ? data.items.map((item) => transformProductItem(item as unknown as ApiItem))
      : Array.isArray((data as { Items?: ApiItem[] } | null | undefined)?.Items)
        ? ((data as { Items?: ApiItem[] }).Items ?? []).map(transformProductItem)
        : [],
  };
}
