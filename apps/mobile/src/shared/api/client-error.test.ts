import assert from "node:assert/strict";
import type { AxiosError } from "axios";
import { extractWarehousePriceSyncConflict } from "../../modules/product-maintenance/warehouse-price-sync";
import { preserveApiClientError } from "./client-error";

const originalError = {
  name: "AxiosError",
  message: "Request failed with status code 409",
  isAxiosError: true,
  response: {
    status: 409,
    data: {
      success: false,
      errorCode: "PRICE_VERSION_CONFLICT",
      message: "Prices changed",
      data: {
        status: "confirmation_required",
        retailConfirmationRequired: true,
        storePrice: {
          uuid: "price-409",
          purchasePrice: 4,
          retailPrice: 8,
          discountRate: null,
          isAutoPricing: false,
          isSpecialProduct: false,
          isActive: true,
        },
        warehousePurchasePrice: 4,
        warehouseRetailPrice: 10,
        previousStorePurchasePrice: 4,
        previousStoreRetailPrice: 8,
        discountRate: null,
        previousDiscountedRetailPrice: 8,
        newDiscountedRetailPrice: 10,
      },
    },
  },
} as AxiosError;

const preservedError = preserveApiClientError(originalError);
assert.equal(preservedError, originalError, "interceptor keeps the original AxiosError instance");
assert.equal(preservedError.message, "Prices changed", "interceptor replaces message with backend detail");
assert.equal(preservedError.response?.status, 409, "interceptor keeps response status");
assert.equal(
  extractWarehousePriceSyncConflict(preservedError)?.warehouseRetailPrice,
  10,
  "preserved interceptor error reaches warehouse conflict normalization"
);

console.log("client-error.test.ts: ok");
