import {
  buildCreateProductWithPricesPayload,
  normalizeActiveLocalSuppliersResponse,
  normalizeCreateProductWithPricesResult,
} from "./api-normalization";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

const createPayload = buildCreateProductWithPricesPayload({
  localSupplierCode: " SUP-01 ",
  itemNumber: " ITEM-01 ",
  barcode: " BAR-01 ",
  productName: " Green Tea ",
  purchasePrice: 3.5,
  retailPrice: 5,
  isSpecialProduct: false,
  isAutoPricing: false,
});

assertDeepEqual(
  createPayload,
  {
    localSupplierCode: "SUP-01",
    itemNumber: "ITEM-01",
    barcode: "BAR-01",
    productName: "Green Tea",
    purchasePrice: 3.5,
    retailPrice: 5,
    isSpecialProduct: false,
    isAutoPricing: false,
  },
  "create product payload trims text fields and matches backend DTO names"
);

const suppliers = normalizeActiveLocalSuppliersResponse({
  success: true,
  data: [
    { LocalSupplierCode: "SUP-01", LocalSupplierName: "Supplier One" },
    { supplierCode: "SUP-02", Name: "Supplier Two" },
    { LocalSupplierName: "Missing Code" },
  ],
});

assertDeepEqual(
  suppliers,
  [
    { supplierCode: "SUP-01", supplierName: "Supplier One" },
    { supplierCode: "SUP-02", supplierName: "Supplier Two" },
  ],
  "active local suppliers normalize envelope payloads and drop missing codes"
);

const result = normalizeCreateProductWithPricesResult({
  success: true,
  data: {
    ProductCode: "PROD-01",
    StoreProductCodes: {
      S01: "SP-01",
    },
  },
});

assertEqual(result.productCode, "PROD-01", "create result normalizes product code");
assertDeepEqual(
  result.storeProductCodes,
  { S01: "SP-01" },
  "create result normalizes store product codes"
);
