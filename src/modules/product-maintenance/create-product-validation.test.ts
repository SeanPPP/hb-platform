import { validateCreateProductForm } from "./create-product-validation";

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

const valid = validateCreateProductForm({
  localSupplierCode: " SUP-01 ",
  itemNumber: " ITEM-01 ",
  barcode: " BAR-01 ",
  productName: " Green Tea ",
  purchasePrice: "3.50",
  retailPrice: "5",
  isSpecialProduct: false,
  isAutoPricing: false,
});

assertEqual(valid.ok, true, "valid form passes");
if (valid.ok) {
  assertDeepEqual(
    valid.payload,
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
    "valid form returns trimmed backend payload"
  );
}

const missing = validateCreateProductForm({
  localSupplierCode: "",
  itemNumber: "ITEM-01",
  barcode: "BAR-01",
  productName: "Green Tea",
  purchasePrice: "3.50",
  retailPrice: "5",
  isSpecialProduct: false,
  isAutoPricing: false,
});

assertEqual(missing.ok, false, "missing required fields fail");
if (!missing.ok) {
  assertEqual(missing.reason, "required", "missing supplier reports required");
}

const invalidPrice = validateCreateProductForm({
  localSupplierCode: "SUP-01",
  itemNumber: "ITEM-01",
  barcode: "BAR-01",
  productName: "Green Tea",
  purchasePrice: "-1",
  retailPrice: "5",
  isSpecialProduct: false,
  isAutoPricing: false,
});

assertEqual(invalidPrice.ok, false, "negative purchase price fails");
if (!invalidPrice.ok) {
  assertEqual(invalidPrice.reason, "priceInvalid", "negative purchase price reports priceInvalid");
}

const manualZeroRetail = validateCreateProductForm({
  localSupplierCode: "SUP-01",
  itemNumber: "ITEM-01",
  barcode: "BAR-01",
  productName: "Green Tea",
  purchasePrice: "0",
  retailPrice: "0",
  isSpecialProduct: false,
  isAutoPricing: false,
});

assertEqual(manualZeroRetail.ok, false, "manual pricing with zero retail fails before API request");
if (!manualZeroRetail.ok) {
  assertEqual(
    manualZeroRetail.reason,
    "manualRetailPriceInvalid",
    "manual pricing zero retail matches backend rule before submit"
  );
}

const autoZeroRetail = validateCreateProductForm({
  localSupplierCode: "SUP-01",
  itemNumber: "ITEM-01",
  barcode: "BAR-01",
  productName: "Green Tea",
  purchasePrice: "0",
  retailPrice: "0",
  isSpecialProduct: false,
  isAutoPricing: true,
});

assertEqual(autoZeroRetail.ok, true, "auto pricing allows zero retail because backend rule only applies to manual pricing");
