import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import ts from "typescript";
import type { WarehousePriceSyncState } from "./warehouse-price-sync";
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

// 执行页面真实回调，只替换网络和打印边界，防止策略正确但按钮仍丢弃打印任务。
const pageSource = readFileSync(resolve(__dirname, "../../../app/(shell)/product-query.tsx"), "utf8");
const pageAst = ts.createSourceFile("product-query.tsx", pageSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const productQuery = pageAst.statements.find((node): node is ts.FunctionDeclaration =>
  ts.isFunctionDeclaration(node) && node.name?.text === "ProductQueryContent");
const skipHandlerStatement = productQuery?.body?.statements.find((node) =>
  ts.isVariableStatement(node) && node.declarationList.declarations.some((declaration) =>
    ts.isIdentifier(declaration.name) && declaration.name.text === "handleCancelWarehousePriceSync"));
assert.ok(skipHandlerStatement, "页面必须保留可执行的仓库价跳过处理器");

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
    HqSync: {
      OperationId: "operation-warehouse-1",
      Status: "pending",
      ProductCode: "P-WAREHOUSE",
      StoreCode: "S02",
      AttemptCount: 0,
      Retryable: true,
      Message: "pending",
    },
  },
});

assertEqual(pascalSnapshot.storePrice?.discountRate, 0.15, "Pascal response normalizes percent discount");
assertEqual(pascalSnapshot.newDiscountedRetailPrice, 6.8, "Pascal response normalizes discounted price");
assertEqual(
  pascalSnapshot.hqSync?.operationId,
  "operation-warehouse-1",
  "warehouse mutation exposes its HQ sync operation"
);

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

flowState = reduceWarehousePriceSyncState(flowState, { type: "print_started" });
assertEqual(flowState.phase, "printing", "explicit keep-current-price print locks the flow");
assertEqual(isWarehousePriceInteractionLocked(flowState), true, "printing keeps scanner and editing locked");
flowState = reduceWarehousePriceSyncState(flowState, { type: "cancelled" });
assertEqual(flowState.phase, "idle", "print completion cleanup returns the flow to idle");
assertEqual(flowState.snapshot, null, "cancel cleanup clears the transient snapshot");

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
const keepCurrentPriceSnapshot = {
  ...camelSnapshot,
  status: "confirmation_required" as const,
  retailConfirmationRequired: true,
  retailUpdated: false,
  storePrice: {
    ...camelSnapshot.storePrice!,
    uuid: "price-current",
    retailPrice: 5,
  },
};
assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "retail_update_skipped",
    snapshot: keepCurrentPriceSnapshot,
    alreadyPrinted: false,
  }),
  true,
  "explicitly keeping the current store price allows the scanned label to print"
);
for (const [label, snapshot] of [
  ["skip print requires confirmation status", { ...keepCurrentPriceSnapshot, status: "synced" as const }],
  ["skip print requires a confirmation marker", { ...keepCurrentPriceSnapshot, retailConfirmationRequired: false }],
  ["skip print requires a store-price UUID", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, uuid: "" } }],
  ["skip print rejects a missing current retail price", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, retailPrice: null } }],
  ["skip print rejects a negative current retail price", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, retailPrice: -1 } }],
  ["skip print rejects a non-finite current retail price", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, retailPrice: Number.NaN } }],
] as const) {
  assertEqual(
    shouldAutoPrintWarehousePrice({
      lookupOrigin: "scan",
      stage: "retail_update_skipped",
      snapshot,
      alreadyPrinted: false,
    }),
    false,
    label
  );
}
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
  [
    "dismissal/cancel does not print the current label",
    { lookupOrigin: "scan", stage: "cancelled", snapshot: keepCurrentPriceSnapshot, alreadyPrinted: false },
  ],
  [
    "manual keep-current-price action never auto prints",
    { lookupOrigin: "manual", stage: "retail_update_skipped", snapshot: keepCurrentPriceSnapshot, alreadyPrinted: false },
  ],
  [
    "repeat keep-current-price action cannot print twice",
    { lookupOrigin: "scan", stage: "retail_update_skipped", snapshot: keepCurrentPriceSnapshot, alreadyPrinted: true },
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

const currentDetail = {
  productCode: "PRODUCT-1",
  barcode: "MAIN-1",
  storePrice: { ...camelSnapshot.storePrice! },
  setCodes: [{ setBarcode: "SET-1", setRetailPrice: 10 }],
  multiCodes: [{ barcode: "MULTI-1", retailPrice: 5 }],
};
function createSkipHandler(options: {
  autoPrint?: boolean;
  scopeCurrent?: boolean;
  errorMessage?: string;
  scanKeyword?: string;
  print?: () => Promise<boolean>;
} = {}) {
  let state: WarehousePriceSyncState = {
    phase: "confirmation", snapshot: camelSnapshot, errorMessage: options.errorMessage ?? null,
  };
  const requestRef = { current: false };
  const prints: { keyword: string; detail: typeof currentDetail }[] = [];
  const restored: unknown[] = [];
  const messages: string[] = [];
  let contextCleared = false;
  let writes = 0;
  const context = {
    detail: structuredClone(currentDetail), lookupOrigin: "scan", storeCodeOverride: "S01",
    scanSource: "camera", scanKeyword: options.scanKeyword ?? "MULTI-1", autoPrintEnabled: options.autoPrint !== false,
    alreadyPrinted: false,
  };
  const deps = {
    useCallback: (callback: unknown) => callback,
    warehousePriceSyncState: state,
    warehousePriceSyncContext: context,
    warehousePriceRequestInFlightRef: requestRef,
    shouldAutoPrintWarehousePrice,
    reduceWarehousePriceSyncState,
    ensureCurrentDetailStoreScope: () => options.scopeCurrent !== false,
    setWarehousePriceSyncState: (reduce: (value: WarehousePriceSyncState) => WarehousePriceSyncState) => {
      state = reduce(state);
    },
    setWarehousePriceSyncContext: (value: unknown) => { contextCleared = value === null; },
    restoreScanAbility: (value: unknown) => { restored.push(value); },
    getErrorMessage: (_error: unknown, fallback: string) => fallback,
    t: (key: string) => key,
    setSnackbarMessage: (message: string) => { messages.push(message); },
    smartAutoPrint: async (keyword: string, detail: typeof currentDetail) => {
      prints.push({ keyword, detail });
      return options.print ? options.print() : true;
    },
    syncWarehousePrice: () => { writes += 1; throw new Error("跳过更新不能写价格"); },
  };
  const handler = new Function("deps", ts.transpile(
    `const { ${Object.keys(deps).join(", ")} } = deps;\n${skipHandlerStatement!.getText(pageAst)}\nreturn handleCancelWarehousePriceSync;`,
    { target: ts.ScriptTarget.ES2022 },
  ))(deps) as (resumeAutoPrint?: boolean) => Promise<void>;
  return {
    handler, requestRef, prints, restored, messages, context,
    get state() { return state; },
    assertReleased() {
      assert.equal(requestRef.current, false);
      assert.equal(state.phase, "idle");
      assert.equal(contextCleared, true);
      assert.deepEqual(restored, ["camera"]);
      assert.equal(writes, 0);
      assert.deepEqual(context.detail, currentDetail, "保留门店价、折扣及条码价");
    },
  };
}

async function runSkipHandlerRegression() {
  const skip = createSkipHandler();
  await skip.handler(true);
  assert.equal(skip.prints.length, 1, "明确跳过更新应继续扫码打印");
  assert.equal(skip.prints[0].keyword, "MULTI-1");
  assert.equal(skip.prints[0].detail, skip.context.detail);
  assert.equal(skip.prints[0].detail.storePrice.retailPrice, 5, "不能使用仓库新价 6");
  skip.assertReleased();

  for (const scanKeyword of ["MAIN-1", "PRODUCT-1", "SET-1"]) {
    const known = createSkipHandler({ scanKeyword });
    await known.handler(true);
    assert.equal(known.prints.length, 1, "主条码、商品码及已加载的组码均可打印");
    assert.equal(known.prints[0].keyword, scanKeyword);
    known.assertReleased();
  }
  const unloadedCode = createSkipHandler({ scanKeyword: "MULTI-ON-PAGE-2" });
  await unloadedCode.handler(true);
  assert.equal(unloadedCode.prints.length, 0, "未加载的多码不能回退打印主条码和主价");
  assert.deepEqual(unloadedCode.messages, ["messages.codesLoadFailed"]);
  unloadedCode.assertReleased();

  for (const options of [{}, { autoPrint: false }, { scopeCurrent: false }]) {
    const closed = createSkipHandler(options);
    await closed.handler(Object.keys(options).length > 0);
    assert.equal(closed.prints.length, 0, "关闭、关闭自动打印或失效门店不打印");
    closed.assertReleased();
  }

  let finishPrint!: (value: boolean) => void;
  const pendingPrint = new Promise<boolean>((resolve) => { finishPrint = resolve; });
  const doubleClick = createSkipHandler({ print: () => pendingPrint });
  const first = doubleClick.handler(true);
  assert.equal(doubleClick.state.phase, "printing");
  assert.equal(isWarehousePriceInteractionLocked(doubleClick.state), true);
  await doubleClick.handler(true);
  await doubleClick.handler();
  assert.equal(doubleClick.prints.length, 1, "打印过程中连点或关闭不得重复打印/提前恢复扫码");
  assert.equal(doubleClick.restored.length, 0);
  finishPrint(true);
  await first;
  doubleClick.assertReleased();

  for (const print of [async () => false, async () => { throw new Error("printer disconnected"); }]) {
    const failed = createSkipHandler({ print });
    await failed.handler(true);
    assert.equal(failed.prints.length, 1);
    failed.assertReleased();
  }

  const uncertain = createSkipHandler({ errorMessage: "confirmation timed out" });
  await uncertain.handler(true);
  assert.equal(uncertain.prints.length, 0, "确认结果不确定时不能按旧快照打印");
  assert.deepEqual(uncertain.messages, ["confirmation timed out"]);
  uncertain.assertReleased();
  console.log("仓库零售价跳过更新与打印回归通过");
}

void runSkipHandlerRegression().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
