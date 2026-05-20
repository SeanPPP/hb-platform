import type { AxiosRequestConfig } from "axios";
import { apiClient } from "@/shared/api/client";
import { useDeviceStore } from "@/store/device-store";
import type {
  DirectUploadSignature,
  WarehouseLocation,
  WarehouseLocationDetail,
  WarehouseLocationMutation,
  WarehouseLocationPrintPayload,
  WarehouseProduct,
  WarehouseProductPatchRequest,
  WarehouseProductPrintPayload,
} from "@/modules/warehouse/types";

const PRODUCT_BASE_PATH = "/react/v1/product-warehouse";
const LOCATION_BASE_PATH = "/react/v1/locations";

function buildReadRequestConfig(): AxiosRequestConfig {
  const session = useDeviceStore.getState().session;
  if (!session?.hardwareId || !session.authCode) {
    return {};
  }

  return {
    headers: {
      "X-Device-Id": session.hardwareId,
      "X-Auth-Code": session.authCode,
    },
  };
}

function toNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function normalizeWarehouseProduct(payload: unknown): WarehouseProduct {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    productName: String(data.productName ?? data.ProductName ?? ""),
    itemNumber: (data.itemNumber ?? data.ItemNumber ?? null) as string | null,
    barcode: (data.barcode ?? data.Barcode ?? null) as string | null,
    productImage: (data.productImage ?? data.ProductImage ?? null) as string | null,
    productType: toNumber(data.productType ?? data.ProductType),
    productTypeLabel: (data.productTypeLabel ?? data.ProductTypeLabel ?? null) as string | null,
    localSupplierCode: (data.localSupplierCode ?? data.LocalSupplierCode ?? null) as string | null,
    supplierCode: (data.supplierCode ?? data.SupplierCode ?? null) as string | null,
    supplierName: (data.supplierName ?? data.SupplierName ?? null) as string | null,
    isActive: Boolean(data.isActive ?? data.IsActive),
    purchasePrice: toNumber(data.purchasePrice ?? data.PurchasePrice),
    retailPrice: toNumber(data.retailPrice ?? data.RetailPrice),
    domesticPrice: toNumber(data.domesticPrice ?? data.DomesticPrice),
    oemPrice: toNumber(data.oEMPrice ?? data.OEMPrice ?? data.oemPrice ?? data.OemPrice),
    importPrice: toNumber(data.importPrice ?? data.ImportPrice),
    middlePackageQuantity: toNumber(data.middlePackageQuantity ?? data.MiddlePackageQuantity),
    packingQuantity: toNumber(data.packingQuantity ?? data.PackingQuantity),
    volume: toNumber(data.volume ?? data.Volume),
    locationGuid: (data.locationGuid ?? data.LocationGuid ?? null) as string | null,
    locationCode: (data.locationCode ?? data.LocationCode ?? null) as string | null,
    locationBarcode: (data.locationBarcode ?? data.LocationBarcode ?? null) as string | null,
    updatedAt: (data.updatedAt ?? data.UpdatedAt ?? null) as string | null,
  };
}

function normalizeWarehouseLocation(payload: unknown): WarehouseLocation {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    locationGuid: String(data.locationGuid ?? data.LocationGuid ?? ""),
    locationCode: (data.locationCode ?? data.LocationCode ?? null) as string | null,
    locationBarcode: (data.locationBarcode ?? data.LocationBarcode ?? null) as string | null,
    status: toNumber(data.status ?? data.Status),
    locationType: toNumber(data.locationType ?? data.LocationType),
    productCount: Number(data.productCount ?? data.ProductCount ?? 0),
  };
}

function normalizeWarehouseLocationDetail(payload: unknown): WarehouseLocationDetail {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  const productsRaw = data.products ?? data.Products;
  return {
    locationGuid: String(data.locationGuid ?? data.LocationGuid ?? ""),
    locationCode: (data.locationCode ?? data.LocationCode ?? null) as string | null,
    locationBarcode: (data.locationBarcode ?? data.LocationBarcode ?? null) as string | null,
    status: toNumber(data.status ?? data.Status),
    locationType: toNumber(data.locationType ?? data.LocationType),
    updatedAt: (data.updatedAt ?? data.UpdatedAt ?? null) as string | null,
    updatedBy: (data.updatedBy ?? data.UpdatedBy ?? null) as string | null,
    products: Array.isArray(productsRaw)
      ? productsRaw.map((item) => {
          const product = (item && typeof item === "object" ? item : {}) as Record<string, unknown>;
          return {
            productCode: (product.productCode ?? product.ProductCode ?? null) as string | null,
            itemNumber: (product.itemNumber ?? product.ItemNumber ?? null) as string | null,
            productName: (product.productName ?? product.ProductName ?? null) as string | null,
            productImage: (product.productImage ?? product.ProductImage ?? null) as string | null,
          };
        })
      : [],
  };
}

export async function lookupWarehouseProducts(keyword: string) {
  const response = await apiClient.get(`${PRODUCT_BASE_PATH}/mobile/lookup`, {
    ...buildReadRequestConfig(),
    params: { keyword },
  });
  return Array.isArray(response.data) ? response.data.map(normalizeWarehouseProduct) : [];
}

export async function getWarehouseProduct(productCode: string) {
  const response = await apiClient.get(`${PRODUCT_BASE_PATH}/mobile/${encodeURIComponent(productCode)}`, buildReadRequestConfig());
  return normalizeWarehouseProduct(response.data);
}

export async function patchWarehouseProduct(productCode: string, payload: WarehouseProductPatchRequest) {
  const response = await apiClient.patch(`${PRODUCT_BASE_PATH}/mobile/${encodeURIComponent(productCode)}`, payload);
  return normalizeWarehouseProduct(response.data);
}

export async function setWarehouseProductLocation(productCode: string, locationGuid?: string | null) {
  const response = await apiClient.put(`${PRODUCT_BASE_PATH}/mobile/${encodeURIComponent(productCode)}/location`, {
    locationGuid: locationGuid ?? null,
  });
  return normalizeWarehouseProduct(response.data);
}

export async function getWarehouseProductPrintPayload(productCode: string) {
  const response = await apiClient.get(`${PRODUCT_BASE_PATH}/mobile/${encodeURIComponent(productCode)}/print-payload`, {
    ...buildReadRequestConfig(),
    params: { type: "product" },
  });
  return response.data as WarehouseProductPrintPayload;
}

export async function getWarehouseLocationPrintPayload(productCode: string) {
  const response = await apiClient.get(`${PRODUCT_BASE_PATH}/mobile/${encodeURIComponent(productCode)}/print-payload`, {
    ...buildReadRequestConfig(),
    params: { type: "location" },
  });
  return response.data as WarehouseLocationPrintPayload;
}

export async function getWarehouseImageUploadSignature(
  productCode: string,
  request: { fileName: string; contentType: string; fileSize: number; objectKey?: string | null }
) {
  const response = await apiClient.post(
    `${PRODUCT_BASE_PATH}/mobile/${encodeURIComponent(productCode)}/image-upload-signature`,
    request
  );
  return response.data as DirectUploadSignature;
}

export async function uploadFileToSignedUrl(uri: string, signature: DirectUploadSignature) {
  const fileResponse = await fetch(uri);
  const blob = await fileResponse.blob();
  const result = await fetch(signature.url, {
    method: "PUT",
    headers: signature.headers,
    body: blob,
  });

  if (!result.ok) {
    throw new Error(`Upload failed with status ${result.status}`);
  }

  return signature.objectKey;
}

export async function lookupLocations(keyword: string) {
  const response = await apiClient.get(`${LOCATION_BASE_PATH}/lookup`, {
    ...buildReadRequestConfig(),
    params: { keyword },
  });
  return Array.isArray(response.data) ? response.data.map(normalizeWarehouseLocation) : [];
}

export async function getLocationDetail(locationGuid: string) {
  const response = await apiClient.get(
    `${LOCATION_BASE_PATH}/${encodeURIComponent(locationGuid)}`,
    buildReadRequestConfig()
  );
  return normalizeWarehouseLocationDetail(response.data);
}

export async function createLocation(payload: WarehouseLocationMutation) {
  const response = await apiClient.post(LOCATION_BASE_PATH, payload);
  return normalizeWarehouseLocationDetail(response.data);
}

export async function updateLocation(locationGuid: string, payload: WarehouseLocationMutation) {
  const response = await apiClient.put(`${LOCATION_BASE_PATH}/${encodeURIComponent(locationGuid)}`, payload);
  return normalizeWarehouseLocationDetail(response.data);
}

export async function deleteLocation(locationGuid: string) {
  await apiClient.delete(`${LOCATION_BASE_PATH}/${encodeURIComponent(locationGuid)}`);
  return true;
}

export async function bindProductToLocation(locationGuid: string, productCode: string) {
  const response = await apiClient.post(`${LOCATION_BASE_PATH}/${encodeURIComponent(locationGuid)}/products/${encodeURIComponent(productCode)}`);
  return normalizeWarehouseLocationDetail(response.data);
}

export async function unbindProductFromLocation(locationGuid: string, productCode: string) {
  const response = await apiClient.delete(`${LOCATION_BASE_PATH}/${encodeURIComponent(locationGuid)}/products/${encodeURIComponent(productCode)}`);
  return normalizeWarehouseLocationDetail(response.data);
}
