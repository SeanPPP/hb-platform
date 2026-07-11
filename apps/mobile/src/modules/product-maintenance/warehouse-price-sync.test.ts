import {
  buildWarehousePriceSyncRequest,
  calculateDiscountedRetailPrice,
  createWarehousePriceSyncState,
  extractWarehousePriceSyncConflict,
  formatWarehouseDiscountRate,
  getWarehousePriceSyncApplicability,
  isProductQueryInteractionBlocked,
  isWarehousePriceConflictSnapshotComplete,
  isWarehousePriceInteractionLocked,
  isWarehousePriceSyncSupplier,
  normalizeWarehouseDiscountRate,
  normalizeWarehouseMoney,
  normalizeWarehousePriceSyncResponse,
  reduceWarehousePriceSyncState,
  resolveWarehousePriceConfirmationFeedback,
  shouldAutoPrintWarehousePrice,
} from "./warehouse-price-sync";

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

const camelSnapshot = normalizeWarehousePriceSyncResponse({
  status: "confirmation_required",
  purchaseUpdated: true,
  retailUpdated: false,
  retailConfirmationRequired: true,
  storePrice: {
    uuid: "price-1",
    storeCode: "S01",
    purchasePrice: "3.25",
    retailPrice: 5,
    discountRate: 0.2,
    isAutoPricing: true,
    isSpecialProduct: false,
    isActive: true,
  },
  warehousePurchasePrice: "3.25",
  warehouseRetailPrice: 6,
  previousStorePurchasePrice: 3,
  previousStoreRetailPrice: 5,
  discountRate: 0.2,
  previousDiscountedRetailPrice: 4,
  newDiscountedRetailPrice: 4.8,
});

assertEqual(camelSnapshot.status, "confirmation_required", "camel response normalizes status");
assertEqual(camelSnapshot.storePrice?.purchasePrice, 3.25, "camel response normalizes latest purchase");
assertEqual(camelSnapshot.storePrice?.isAutoPricing, true, "camel response preserves auto pricing flag");

const pascalSnapshot = normalizeWarehousePriceSyncResponse({
  Success: true,
  Data: {
    Status: "synced",
    PurchaseUpdated: false,
    RetailUpdated: true,
    RetailConfirmationRequired: false,
    StorePrice: {
      Uuid: "price-2",
      StoreCode: "S02",
      PurchasePrice: 4,
      RetailPrice: 8,
      DiscountRate: 15,
      IsAutoPricing: false,
      IsSpecialProduct: true,
      IsActive: true,
    },
    WarehousePurchasePrice: 4,
    WarehouseRetailPrice: 8,
    PreviousStorePurchasePrice: 4,
    PreviousStoreRetailPrice: 7,
    DiscountRate: 15,
    PreviousDiscountedRetailPrice: 5.95,
    NewDiscountedRetailPrice: 6.8,
  },
});

assertEqual(pascalSnapshot.storePrice?.discountRate, 0.15, "Pascal response normalizes percent discount");
assertEqual(pascalSnapshot.newDiscountedRetailPrice, 6.8, "Pascal response normalizes discounted price");

const nullSnapshot = normalizeWarehousePriceSyncResponse(null);
assertDeepEqual(
  {
    status: nullSnapshot.status,
    purchaseUpdated: nullSnapshot.purchaseUpdated,
    retailUpdated: nullSnapshot.retailUpdated,
    retailConfirmationRequired: nullSnapshot.retailConfirmationRequired,
    storePrice: nullSnapshot.storePrice,
    warehousePurchasePrice: nullSnapshot.warehousePurchasePrice,
  },
  {
    status: "missing_source",
    purchaseUpdated: false,
    retailUpdated: false,
    retailConfirmationRequired: false,
    storePrice: null,
    warehousePurchasePrice: null,
  },
  "null response normalizes to a safe non-writing snapshot"
);

assertEqual(isWarehousePriceSyncSupplier(" 200 "), true, "supplier 200 is trim-aware");
assertEqual(isWarehousePriceSyncSupplier("0200"), false, "other supplier is not applicable");
assertEqual(isWarehousePriceSyncSupplier(null), false, "missing supplier is not applicable");

assertEqual(normalizeWarehouseMoney(1.005), 1.01, "money rounds to two decimals");
assertEqual(normalizeWarehouseMoney("bad"), null, "invalid money becomes null");
assertEqual(normalizeWarehouseDiscountRate(20), 0.2, "percentage discount normalizes to ratio");
assertEqual(normalizeWarehouseDiscountRate(0.2), 0.2, "ratio discount stays unchanged");
assertEqual(calculateDiscountedRetailPrice(6, 0.2), 4.8, "discounted price preserves discount ratio");
assertEqual(calculateDiscountedRetailPrice(7.99, 15), 6.79, "discounted price rounds to cents");

const nullDiscountSnapshot = normalizeWarehousePriceSyncResponse({
  status: "confirmation_required",
  retailConfirmationRequired: true,
  storePrice: {
    uuid: "price-null-discount",
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
});
assertEqual(nullDiscountSnapshot.discountRate, null, "null discount remains null for concurrency snapshot");
assertEqual(calculateDiscountedRetailPrice(8, null), 8, "null discount calculates as zero discount");
assertEqual(formatWarehouseDiscountRate(null), "0%", "null discount displays as zero percent");
assertEqual(
  buildWarehousePriceSyncRequest(
    { ...nullDiscountSnapshot, discountRate: 0.2 },
    true
  ).expectedDiscountRate,
  null,
  "confirm request preserves latest store-price null discount for concurrency validation"
);
assertEqual(
  isWarehousePriceConflictSnapshotComplete(nullDiscountSnapshot),
  true,
  "conflict snapshot completeness allows a legitimate null discount"
);
assertEqual(
  isWarehousePriceConflictSnapshotComplete({
    ...nullDiscountSnapshot,
    previousStoreRetailPrice: null,
    storePrice: { ...nullDiscountSnapshot.storePrice!, retailPrice: null },
  }),
  true,
  "conflict snapshot completeness allows a legitimate null current retail price"
);

assertEqual(
  getWarehousePriceSyncApplicability("200", null),
  "missing_store_price",
  "supplier 200 without an existing store price cannot sync or auto print"
);
assertEqual(
  getWarehousePriceSyncApplicability(" 200 ", "price-1"),
  "sync",
  "supplier 200 with a store price enters warehouse sync"
);

assertEqual(
  isProductQueryInteractionBlocked({
    loading: false,
    lookupVisible: false,
    lookupSelectionOpen: false,
    autoPricingVisible: false,
    autoPricingSaving: false,
    warehouseLocked: false,
    requestInFlight: false,
    storeSelectionInFlight: false,
  }),
  false,
  "idle product query accepts a new scan"
);
assertEqual(
  isProductQueryInteractionBlocked({
    loading: false,
    lookupVisible: false,
    lookupSelectionOpen: false,
    autoPricingVisible: false,
    autoPricingSaving: false,
    warehouseLocked: false,
    requestInFlight: true,
    storeSelectionInFlight: false,
  }),
  true,
  "in-flight lookup serializes camera, HID, and store selection"
);
assertEqual(
  isProductQueryInteractionBlocked({
    loading: false,
    lookupVisible: false,
    lookupSelectionOpen: true,
    autoPricingVisible: false,
    autoPricingSaving: false,
    warehouseLocked: false,
    requestInFlight: false,
    storeSelectionInFlight: false,
  }),
  true,
  "synchronous selection ref closes the state-update window before lookup sheet renders"
);

const confirmRequest = buildWarehousePriceSyncRequest(camelSnapshot, true);
assertDeepEqual(
  confirmRequest,
  {
    confirmRetailPrice: true,
    expectedWarehousePurchasePrice: 3.25,
    expectedWarehouseRetailPrice: 6,
    expectedStorePurchasePrice: 3.25,
    expectedStoreRetailPrice: 5,
    expectedDiscountRate: 0.2,
  },
  "confirm request uses the latest post-purchase-sync store snapshot"
);

let flowState = createWarehousePriceSyncState();
flowState = reduceWarehousePriceSyncState(flowState, { type: "preview_started" });
assertEqual(flowState.phase, "previewing", "preview transition enters busy state");
assertEqual(isWarehousePriceInteractionLocked(flowState), true, "preview disables scanner and editing");

flowState = reduceWarehousePriceSyncState(flowState, {
  type: "preview_succeeded",
  snapshot: camelSnapshot,
});
assertEqual(flowState.phase, "confirmation", "retail difference opens confirmation state");
assertEqual(isWarehousePriceInteractionLocked(flowState), true, "confirmation keeps scanner disabled");

flowState = reduceWarehousePriceSyncState(flowState, { type: "confirm_started" });
assertEqual(flowState.phase, "confirming", "confirm transition enters submit state");

flowState = reduceWarehousePriceSyncState(flowState, {
  type: "confirm_failed",
  message: "network failed",
});
assertEqual(flowState.phase, "confirmation", "confirm failure keeps modal open");
assertEqual(flowState.errorMessage, "network failed", "confirm failure remains retryable with error");

const conflictSnapshot = extractWarehousePriceSyncConflict({
  response: {
    status: 409,
    data: {
      success: false,
      errorCode: "PRICE_VERSION_CONFLICT",
      data: {
        status: "confirmation_required",
        retailConfirmationRequired: true,
        storePrice: {
          uuid: "price-1",
          purchasePrice: 3.5,
          retailPrice: 5.5,
          discountRate: 0.2,
          isAutoPricing: true,
          isSpecialProduct: false,
          isActive: true,
        },
        warehousePurchasePrice: 3.5,
        warehouseRetailPrice: 6.5,
        previousStorePurchasePrice: 3.5,
        previousStoreRetailPrice: 5.5,
        discountRate: 0.2,
        previousDiscountedRetailPrice: 4.4,
        newDiscountedRetailPrice: 5.2,
      },
    },
  },
});

assertEqual(conflictSnapshot?.storePrice?.purchasePrice, 3.5, "409 exposes latest store snapshot");
assertEqual(conflictSnapshot?.warehouseRetailPrice, 6.5, "409 exposes latest warehouse snapshot");
assertEqual(
  extractWarehousePriceSyncConflict({ response: { status: 500, data: {} } }),
  null,
  "non-conflict errors do not masquerade as a version conflict"
);

flowState = reduceWarehousePriceSyncState(flowState, {
  type: "conflict_received",
  snapshot: conflictSnapshot!,
  message: "price changed",
});
assertEqual(flowState.phase, "confirmation", "conflict refresh keeps confirmation open");
assertEqual(flowState.snapshot?.warehouseRetailPrice, 6.5, "conflict transition replaces stale snapshot");

assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "preview_succeeded",
    snapshot: { ...camelSnapshot, status: "synced", retailConfirmationRequired: false },
    alreadyPrinted: false,
  }),
  true,
  "scan prints once when no retail confirmation is required"
);
assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "confirmation_succeeded",
    snapshot: { ...pascalSnapshot, retailUpdated: true },
    alreadyPrinted: false,
  }),
  true,
  "scan prints after successful retail confirmation"
);
assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "confirmation_succeeded",
    snapshot: { ...pascalSnapshot, retailUpdated: false },
    alreadyPrinted: false,
  }),
  false,
  "confirmation response without a retail update does not print"
);
for (const status of ["missing_source", "not_applicable"] as const) {
  assertEqual(
    shouldAutoPrintWarehousePrice({
      lookupOrigin: "scan",
      stage: "preview_succeeded",
      snapshot: { ...pascalSnapshot, status, retailUpdated: false },
      alreadyPrinted: false,
    }),
    false,
    `${status} does not print an unverified warehouse label`
  );
}
for (const [label, input] of [
  [
    "manual lookup never auto prints",
    { lookupOrigin: "manual", stage: "preview_succeeded", snapshot: pascalSnapshot, alreadyPrinted: false },
  ],
  [
    "cancel never prints stale label",
    { lookupOrigin: "scan", stage: "cancelled", snapshot: camelSnapshot, alreadyPrinted: false },
  ],
  [
    "failure never prints stale label",
    { lookupOrigin: "scan", stage: "failed", snapshot: camelSnapshot, alreadyPrinted: false },
  ],
  [
    "already printed scan cannot print twice",
    { lookupOrigin: "scan", stage: "confirmation_succeeded", snapshot: pascalSnapshot, alreadyPrinted: true },
  ],
] as const) {
  assertEqual(shouldAutoPrintWarehousePrice(input), false, label);
}

assertEqual(
  resolveWarehousePriceConfirmationFeedback({
    retailUpdated: true,
    printAttempted: true,
    labelPrinted: false,
  }),
  "retail_updated_print_failed",
  "price success plus print failure uses combined feedback"
);
assertEqual(
  resolveWarehousePriceConfirmationFeedback({
    retailUpdated: true,
    printAttempted: false,
    labelPrinted: false,
  }),
  "retail_updated",
  "price success without auto print reports only the price update"
);
assertEqual(
  resolveWarehousePriceConfirmationFeedback({
    retailUpdated: false,
    printAttempted: false,
    labelPrinted: false,
  }),
  "no_update",
  "unchanged confirmation never claims that retail was updated"
);
