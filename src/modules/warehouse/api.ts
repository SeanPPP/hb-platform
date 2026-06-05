import type { AxiosRequestConfig } from "axios";
import { apiClient } from "@/shared/api/client";
import { reportApplicationLog } from "@/shared/logging/log-center-runtime";
import { useDeviceStore } from "@/store/device-store";
import { normalizeWarehouseProduct } from "@/modules/warehouse/api-normalization";
import type {
  DirectUploadSignature,
  WarehouseLocationBindRequest,
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
  try {
    const fileResponse = await fetch(uri);
    const blob = await fileResponse.blob();
    const result = await fetch(signature.url, {
      method: "PUT",
      headers: signature.headers,
      body: blob,
    });

    if (!result.ok) {
      reportApplicationLog({
        level: "Error",
        message: "仓库图片上传失败",
        sourceType: "warehouse.upload",
        requestMethod: "PUT",
        requestPath: signature.url.split("?")[0],
        statusCode: result.status,
        properties: {
          objectKey: signature.objectKey,
          uploadUrl: signature.url.split("?")[0],
        },
      });
      throw new Error(`Upload failed with status ${result.status}`);
    }
  } catch (error) {
    const normalizedError = error instanceof Error ? error : new Error(String(error));
    reportApplicationLog({
      level: "Error",
      message: "仓库图片上传链路异常",
      sourceType: "warehouse.upload",
      requestMethod: "PUT",
      requestPath: signature.url.split("?")[0],
      exceptionType: normalizedError.name,
      exceptionMessage: normalizedError.message,
      stackTrace: normalizedError.stack,
      properties: {
        objectKey: signature.objectKey,
        uploadUrl: signature.url.split("?")[0],
        fileUriTail: uri.split("/").pop() || uri,
      },
    });
    throw error;
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

export async function bindProductToLocation(locationGuid: string, payload: WarehouseLocationBindRequest) {
  const response = await apiClient.post(
    `${LOCATION_BASE_PATH}/${encodeURIComponent(locationGuid)}/products/bind`,
    payload
  );
  return normalizeWarehouseLocationDetail(response.data);
}

export async function unbindProductFromLocation(locationGuid: string, productCode: string) {
  const response = await apiClient.delete(`${LOCATION_BASE_PATH}/${encodeURIComponent(locationGuid)}/products/${encodeURIComponent(productCode)}`);
  return normalizeWarehouseLocationDetail(response.data);
}
