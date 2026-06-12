import { validateCreateProductForm, type CreateProductFormValues } from "./create-product-validation";

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

function makeValues(overrides: Partial<CreateProductFormValues> = {}): CreateProductFormValues {
  return {
    localSupplierCode: " SUP-01 ",
    itemNumber: " ITEM-01 ",
    barcode: " BAR-001 ",
    productName: " Green Tea ",
    purchasePrice: "3.50",
    retailPrice: "7",
    isSpecialProduct: false,
    isAutoPricing: false,
    ...overrides,
  };
}

function assertInvalid(
  overrides: Partial<CreateProductFormValues>,
  reason: Exclude<ReturnType<typeof validateCreateProductForm>, { ok: true }>["reason"],
  label: string
) {
  const result = validateCreateProductForm(makeValues(overrides));

  assertEqual(result.ok, false, `${label} fails`);
  if (!result.ok) {
    assertEqual(result.reason, reason, `${label} reports ${reason}`);
  }
}

const valid = validateCreateProductForm(makeValues());

assertEqual(valid.ok, true, "valid form passes");
if (valid.ok) {
  assertDeepEqual(
    valid.payload,
    {
      localSupplierCode: "SUP-01",
      itemNumber: "ITEM-01",
      barcode: "BAR-001",
      productName: "Green Tea",
      purchasePrice: 3.5,
      retailPrice: 7,
      isSpecialProduct: false,
      isAutoPricing: false,
    },
    "valid form returns trimmed backend payload"
  );
}

assertInvalid({ localSupplierCode: "" }, "required", "missing supplier");
assertInvalid({ itemNumber: "" }, "required", "missing item number");
assertInvalid({ barcode: "" }, "required", "missing barcode");
assertInvalid({ purchasePrice: "" }, "required", "missing purchase price");
assertInvalid({ retailPrice: "" }, "required", "missing retail price");
assertInvalid({ itemNumber: "ABCD" }, "itemNumberTooShort", "short item number");
assertInvalid({ barcode: "123456" }, "barcodeTooShort", "short barcode");
assertInvalid({ purchasePrice: "-1" }, "priceInvalid", "negative purchase price");
assertInvalid({ retailPrice: "6.99" }, "retailPriceTooLow", "manual retail below double purchase");

const autoLowRetail = validateCreateProductForm(
  makeValues({
    retailPrice: "1",
    isAutoPricing: true,
  })
);

assertEqual(autoLowRetail.ok, true, "auto pricing allows retail below double purchase");
